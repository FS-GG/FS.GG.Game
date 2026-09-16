namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
type SessionRuntimeStatus =
    | Running
    | Paused
    | Disposed

type SessionRuntimeConfig =
    {
        StepMicroseconds: uint64
        MaxCatchUpSteps: uint32
    }

[<RequireQualifiedAccess>]
type SessionRuntimeRefusal =
    | InvalidStepMicroseconds of uint64
    | InvalidMaxCatchUpSteps of uint32
    | InvalidEnvelope of SessionContractIssue list
    | WrongSession of expected: string * actual: string
    | StaleInputSequence of accepted: uint64 * candidate: uint64
    | IncompatibleSnapshot of SessionCompatibilityIssue list
    | ContractFailure of SessionFailure
    | RuntimeDisposed

[<RequireQualifiedAccess>]
type SessionRuntimeObservation<'input, 'snapshot> =
    | AdmitInput of SessionInput<'input>
    | AdvanceElapsed of microseconds: uint64
    | Pause
    | Resume
    | StepOnce
    | Reset
    | Restore of SessionSnapshot<'snapshot>
    | Dispose

[<RequireQualifiedAccess>]
type SessionRuntimeEffect<'projection> =
    | InputAccepted of inputId: string * sequence: uint64
    | Advanced of stepCount: uint64
    | CatchUpClamped of droppedMicroseconds: uint64
    | StatusChanged of SessionRuntimeStatus
    | ProjectionReady of SessionProjection<'projection>
    | Refused of SessionRuntimeRefusal
    | Disposed

type SessionRuntimeState<'state, 'snapshot> =
    {
        SessionId: string
        Compatibility: SessionCompatibility
        Config: SessionRuntimeConfig
        Status: SessionRuntimeStatus
        Current: 'state
        InitialSnapshot: SessionSnapshot<'snapshot>
        AccumulatorMicroseconds: uint64
        LastInputSequence: uint64 option
    }

[<RequireQualifiedAccess>]
module SessionRuntime =
    let private maxStepMicroseconds = 60_000_000UL
    let private maxCatchUpSteps = 10_000u

    let private validate config =
        if config.StepMicroseconds = 0UL || config.StepMicroseconds > maxStepMicroseconds then
            Error(SessionRuntimeRefusal.InvalidStepMicroseconds config.StepMicroseconds)
        elif config.MaxCatchUpSteps = 0u || config.MaxCatchUpSteps > maxCatchUpSteps then
            Error(SessionRuntimeRefusal.InvalidMaxCatchUpSteps config.MaxCatchUpSteps)
        else
            Ok()

    let initialize config contract request =
        match validate config with
        | Error refusal -> Error refusal
        | Ok() when not (SessionEnvelope.validateInitialization request).IsEmpty ->
            Error(SessionRuntimeRefusal.InvalidEnvelope(SessionEnvelope.validateInitialization request))
        | Ok() ->
            match contract.Initialize request with
            | Error failure -> Error(SessionRuntimeRefusal.ContractFailure failure)
            | Ok current ->
                let initial = contract.Snapshot current

                match SessionEnvelope.validateSnapshot initial with
                | _ :: _ as issues -> Error(SessionRuntimeRefusal.InvalidEnvelope issues)
                | [] when initial.SessionId <> request.SessionId ->
                    Error(SessionRuntimeRefusal.WrongSession(request.SessionId, initial.SessionId))
                | [] ->
                    match SessionCompatibility.compare request.Compatibility initial.Compatibility with
                    | _ :: _ as issues -> Error(SessionRuntimeRefusal.IncompatibleSnapshot issues)
                    | [] ->
                        Ok
                            {
                                SessionId = request.SessionId
                                Compatibility = request.Compatibility
                                Config = config
                                Status = SessionRuntimeStatus.Running
                                Current = current
                                InitialSnapshot = initial
                                AccumulatorMicroseconds = 0UL
                                LastInputSequence = None
                            }

    let project
        (contract: SessionContract<'configuration, 'state, 'input, 'projection, 'snapshot>)
        (state: SessionRuntimeState<'state, 'snapshot>)
        =
        contract.Project state.Current

    let snapshot
        (contract: SessionContract<'configuration, 'state, 'input, 'projection, 'snapshot>)
        (state: SessionRuntimeState<'state, 'snapshot>)
        =
        contract.Snapshot state.Current

    let private projection contract current =
        SessionRuntimeEffect.ProjectionReady(contract.Project current)

    let private refuse state value =
        state, [ SessionRuntimeEffect.Refused value ]

    let private advance contract stepCount state prefix =
        match
            contract.Advance
                {
                    SessionId = state.SessionId
                    StepCount = stepCount
                }
                state.Current
        with
        | Error failure -> refuse state (SessionRuntimeRefusal.ContractFailure failure)
        | Ok current ->
            { state with Current = current },
            prefix
            @ [ SessionRuntimeEffect.Advanced stepCount; projection contract current ]

    let update contract observation state =
        if state.Status = SessionRuntimeStatus.Disposed then
            match observation with
            | SessionRuntimeObservation.Dispose -> state, []
            | _ -> refuse state SessionRuntimeRefusal.RuntimeDisposed
        else
            match observation with
            | SessionRuntimeObservation.AdmitInput input ->
                if not (SessionEnvelope.validateInput input).IsEmpty then
                    refuse state (SessionRuntimeRefusal.InvalidEnvelope(SessionEnvelope.validateInput input))
                elif input.SessionId <> state.SessionId then
                    refuse state (SessionRuntimeRefusal.WrongSession(state.SessionId, input.SessionId))
                else
                    match state.LastInputSequence with
                    | Some accepted when input.Sequence <= accepted ->
                        refuse state (SessionRuntimeRefusal.StaleInputSequence(accepted, input.Sequence))
                    | _ ->
                        match contract.AdmitInput input state.Current with
                        | Error failure -> refuse state (SessionRuntimeRefusal.ContractFailure failure)
                        | Ok current ->
                            { state with
                                Current = current
                                LastInputSequence = Some input.Sequence
                            },
                            [
                                SessionRuntimeEffect.InputAccepted(input.InputId, input.Sequence)
                                projection contract current
                            ]
            | SessionRuntimeObservation.AdvanceElapsed elapsed when state.Status = SessionRuntimeStatus.Paused ->
                state, []
            | SessionRuntimeObservation.AdvanceElapsed elapsed ->
                let step = state.Config.StepMicroseconds
                let maximumSteps = uint64 state.Config.MaxCatchUpSteps
                let maximumTotal = maximumSteps * step + step - 1UL
                let room = maximumTotal - state.AccumulatorMicroseconds
                let accepted = min elapsed room
                let dropped = elapsed - accepted
                let total = state.AccumulatorMicroseconds + accepted
                let stepCount = total / step

                let next =
                    { state with
                        AccumulatorMicroseconds = total % step
                    }

                let prefix =
                    if dropped = 0UL then
                        []
                    else
                        [ SessionRuntimeEffect.CatchUpClamped dropped ]

                if stepCount = 0UL then
                    next, prefix
                else
                    advance contract stepCount next prefix
            | SessionRuntimeObservation.Pause when state.Status = SessionRuntimeStatus.Running ->
                { state with
                    Status = SessionRuntimeStatus.Paused
                },
                [ SessionRuntimeEffect.StatusChanged SessionRuntimeStatus.Paused ]
            | SessionRuntimeObservation.Pause -> state, []
            | SessionRuntimeObservation.Resume when state.Status = SessionRuntimeStatus.Paused ->
                { state with
                    Status = SessionRuntimeStatus.Running
                },
                [ SessionRuntimeEffect.StatusChanged SessionRuntimeStatus.Running ]
            | SessionRuntimeObservation.Resume -> state, []
            | SessionRuntimeObservation.StepOnce when state.Status = SessionRuntimeStatus.Paused ->
                advance contract 1UL state []
            | SessionRuntimeObservation.StepOnce -> state, []
            | SessionRuntimeObservation.Reset ->
                match contract.Restore state.InitialSnapshot with
                | Error failure -> refuse state (SessionRuntimeRefusal.ContractFailure failure)
                | Ok current ->
                    let next =
                        { state with
                            Current = current
                            AccumulatorMicroseconds = 0UL
                            LastInputSequence = None
                        }

                    next, [ projection contract current ]
            | SessionRuntimeObservation.Restore value ->
                if not (SessionEnvelope.validateSnapshot value).IsEmpty then
                    refuse state (SessionRuntimeRefusal.InvalidEnvelope(SessionEnvelope.validateSnapshot value))
                elif value.SessionId <> state.SessionId then
                    refuse state (SessionRuntimeRefusal.WrongSession(state.SessionId, value.SessionId))
                else
                    match SessionCompatibility.compare state.Compatibility value.Compatibility with
                    | _ :: _ as issues -> refuse state (SessionRuntimeRefusal.IncompatibleSnapshot issues)
                    | [] ->
                        match contract.Restore value with
                        | Error failure -> refuse state (SessionRuntimeRefusal.ContractFailure failure)
                        | Ok current ->
                            let next =
                                { state with
                                    Current = current
                                    AccumulatorMicroseconds = 0UL
                                    LastInputSequence = None
                                }

                            next, [ projection contract current ]
            | SessionRuntimeObservation.Dispose ->
                { state with
                    Status = SessionRuntimeStatus.Disposed
                    AccumulatorMicroseconds = 0UL
                },
                [ SessionRuntimeEffect.Disposed ]
