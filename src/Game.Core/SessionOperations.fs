namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
type SessionOperationTarget =
    | LocalWorker
    | AuthoritativeServer

[<Struct>]
type SessionOperationId =
    {
        Generation: uint64
        Operation: uint64
    }

type SessionOperationConfig =
    {
        Target: SessionOperationTarget
        MaxPendingRequired: uint32
    }

[<RequireQualifiedAccess>]
type SessionOperationStatus =
    | Active
    | Cancelled
    | Failed of SessionFailure
    | Disposed

type SessionRequiredDispatch<'request> =
    {
        Id: SessionOperationId
        Order: uint64
        Target: SessionOperationTarget
        Payload: 'request
    }

type SessionProjectionDispatch =
    {
        Id: SessionOperationId
        Target: SessionOperationTarget
    }

[<RequireQualifiedAccess>]
type SessionOperationObservation<'request, 'record, 'projection> =
    | EnqueueRequired of 'request
    | DemandProjection
    | CompleteRequired of SessionOperationId * 'record
    | CompleteProjection of SessionOperationId * revision: uint64 * 'projection
    | Fail of SessionOperationId * SessionFailure
    | Replace
    | Cancel
    | Dispose

[<RequireQualifiedAccess>]
type SessionOperationRefusal =
    | InvalidMaxPendingRequired of uint32
    | RequiredCapacityReached of uint32
    | StaleGeneration of expected: uint64 * actual: uint64
    | UnknownOrCompletedOperation of SessionOperationId
    | UnexpectedReplyKind of SessionOperationId
    | StaleProjectionRevision of accepted: uint64 * candidate: uint64
    | GenerationExhausted
    | OperationSequenceExhausted
    | CoordinatorInactive of SessionOperationStatus

[<RequireQualifiedAccess>]
type SessionOperationEffect<'request, 'record, 'projection> =
    | DispatchRequired of SessionRequiredDispatch<'request>
    | DispatchProjection of SessionProjectionDispatch
    | RequiredCommitted of order: uint64 * 'record
    | ProjectionCommitted of revision: uint64 * 'projection
    | ProjectionDemandCoalesced of SessionOperationId
    | GenerationInvalidated of previous: uint64 * current: uint64 * SessionOperationStatus
    | Refused of SessionOperationRefusal

type SessionOperationState<'record> =
    {
        Config: SessionOperationConfig
        Generation: uint64
        Status: SessionOperationStatus
        NextOperation: uint64
        NextRequiredOrder: uint64
        NextRequiredCommit: uint64
        PendingRequired: Map<uint64, uint64>
        BufferedRequired: Map<uint64, 'record>
        PendingProjection: uint64 option
        ProjectionDemandQueued: bool
        LastProjectionRevision: uint64 option
    }

[<RequireQualifiedAccess>]
module SessionOperations =
    let private maximumPendingRequired = 65_536u

    let initialize config =
        if
            config.MaxPendingRequired = 0u
            || config.MaxPendingRequired > maximumPendingRequired
        then
            Error(SessionOperationRefusal.InvalidMaxPendingRequired config.MaxPendingRequired)
        else
            Ok
                {
                    Config = config
                    Generation = 0UL
                    Status = SessionOperationStatus.Active
                    NextOperation = 0UL
                    NextRequiredOrder = 0UL
                    NextRequiredCommit = 0UL
                    PendingRequired = Map.empty
                    BufferedRequired = Map.empty
                    PendingProjection = None
                    ProjectionDemandQueued = false
                    LastProjectionRevision = None
                }

    let private refused state refusal =
        state, [ SessionOperationEffect.Refused refusal ]

    let private id state operation =
        {
            Generation = state.Generation
            Operation = operation
        }

    let private dispatchProjection state =
        let operation = state.NextOperation

        let request =
            {
                Id = id state operation
                Target = state.Config.Target
            }

        { state with
            NextOperation = operation + 1UL
            PendingProjection = Some operation
            ProjectionDemandQueued = false
        },
        SessionOperationEffect.DispatchProjection request

    let private nextGeneration status state =
        if state.Generation = System.UInt64.MaxValue then
            refused state SessionOperationRefusal.GenerationExhausted
        else
            let current = state.Generation + 1UL

            { state with
                Generation = current
                Status = status
                NextOperation = 0UL
                NextRequiredOrder = 0UL
                NextRequiredCommit = 0UL
                PendingRequired = Map.empty
                BufferedRequired = Map.empty
                PendingProjection = None
                ProjectionDemandQueued = false
                LastProjectionRevision = None
            },
            [
                SessionOperationEffect.GenerationInvalidated(state.Generation, current, status)
            ]

    let private ensureCurrent state (operationId: SessionOperationId) =
        if operationId.Generation <> state.Generation then
            Error(SessionOperationRefusal.StaleGeneration(state.Generation, operationId.Generation))
        elif operationId.Operation >= state.NextOperation then
            Error(SessionOperationRefusal.UnknownOrCompletedOperation operationId)
        else
            Ok()

    let private drainRequired state =
        let mutable buffered = state.BufferedRequired
        let mutable order = state.NextRequiredCommit
        let effects = ResizeArray()
        let mutable draining = true

        while draining do
            match Map.tryFind order buffered with
            | Some record ->
                buffered <- Map.remove order buffered
                effects.Add(SessionOperationEffect.RequiredCommitted(order, record))
                order <- order + 1UL
            | None -> draining <- false

        { state with
            BufferedRequired = buffered
            NextRequiredCommit = order
        },
        List.ofSeq effects

    let update observation state =
        match observation with
        | SessionOperationObservation.Dispose when state.Status = SessionOperationStatus.Disposed -> state, []
        | _ when state.Status = SessionOperationStatus.Disposed ->
            refused state (SessionOperationRefusal.CoordinatorInactive state.Status)
        | SessionOperationObservation.Replace -> nextGeneration SessionOperationStatus.Active state
        | SessionOperationObservation.Dispose -> nextGeneration SessionOperationStatus.Disposed state
        | SessionOperationObservation.Cancel when state.Status = SessionOperationStatus.Cancelled -> state, []
        | SessionOperationObservation.Cancel -> nextGeneration SessionOperationStatus.Cancelled state
        | _ when
            state.Status <> SessionOperationStatus.Active
            && (match observation with
                | SessionOperationObservation.CompleteRequired _
                | SessionOperationObservation.CompleteProjection _
                | SessionOperationObservation.Fail _ -> false
                | _ -> true)
            ->
            refused state (SessionOperationRefusal.CoordinatorInactive state.Status)
        | SessionOperationObservation.EnqueueRequired payload ->
            if
                state.PendingRequired.Count + state.BufferedRequired.Count
                >= int state.Config.MaxPendingRequired
            then
                refused state (SessionOperationRefusal.RequiredCapacityReached state.Config.MaxPendingRequired)
            elif
                state.NextOperation = System.UInt64.MaxValue
                || state.NextRequiredOrder = System.UInt64.MaxValue
            then
                refused state SessionOperationRefusal.OperationSequenceExhausted
            else
                let operation = state.NextOperation
                let order = state.NextRequiredOrder

                let request =
                    {
                        Id = id state operation
                        Order = order
                        Target = state.Config.Target
                        Payload = payload
                    }

                { state with
                    NextOperation = operation + 1UL
                    NextRequiredOrder = order + 1UL
                    PendingRequired = Map.add operation order state.PendingRequired
                },
                [ SessionOperationEffect.DispatchRequired request ]
        | SessionOperationObservation.DemandProjection ->
            match state.PendingProjection with
            | Some operation ->
                { state with
                    ProjectionDemandQueued = true
                },
                [ SessionOperationEffect.ProjectionDemandCoalesced(id state operation) ]
            | None when state.NextOperation = System.UInt64.MaxValue ->
                refused state SessionOperationRefusal.OperationSequenceExhausted
            | None ->
                let next, effect = dispatchProjection state
                next, [ effect ]
        | SessionOperationObservation.CompleteRequired(operationId, record) ->
            match ensureCurrent state operationId with
            | Error refusal -> refused state refusal
            | Ok() ->
                match Map.tryFind operationId.Operation state.PendingRequired with
                | None when state.PendingProjection = Some operationId.Operation ->
                    refused state (SessionOperationRefusal.UnexpectedReplyKind operationId)
                | None -> refused state (SessionOperationRefusal.UnknownOrCompletedOperation operationId)
                | Some order ->
                    let staged =
                        { state with
                            PendingRequired = Map.remove operationId.Operation state.PendingRequired
                            BufferedRequired = Map.add order record state.BufferedRequired
                        }

                    drainRequired staged
        | SessionOperationObservation.CompleteProjection(operationId, revision, projection) ->
            match ensureCurrent state operationId with
            | Error refusal -> refused state refusal
            | Ok() when Map.containsKey operationId.Operation state.PendingRequired ->
                refused state (SessionOperationRefusal.UnexpectedReplyKind operationId)
            | Ok() when state.PendingProjection <> Some operationId.Operation ->
                refused state (SessionOperationRefusal.UnknownOrCompletedOperation operationId)
            | Ok() ->
                let cleared = { state with PendingProjection = None }

                let accepted, effects =
                    match state.LastProjectionRevision with
                    | Some previous when revision <= previous ->
                        cleared,
                        [
                            SessionOperationEffect.Refused(
                                SessionOperationRefusal.StaleProjectionRevision(previous, revision)
                            )
                        ]
                    | _ ->
                        { cleared with
                            LastProjectionRevision = Some revision
                        },
                        [ SessionOperationEffect.ProjectionCommitted(revision, projection) ]

                if accepted.ProjectionDemandQueued then
                    if accepted.NextOperation = System.UInt64.MaxValue then
                        accepted,
                        effects
                        @ [
                            SessionOperationEffect.Refused SessionOperationRefusal.OperationSequenceExhausted
                        ]
                    else
                        let next, dispatch = dispatchProjection accepted
                        next, effects @ [ dispatch ]
                else
                    accepted, effects
        | SessionOperationObservation.Fail(operationId, failure) ->
            match ensureCurrent state operationId with
            | Error refusal -> refused state refusal
            | Ok() ->
                let isRequired = Map.containsKey operationId.Operation state.PendingRequired
                let isProjection = state.PendingProjection = Some operationId.Operation

                if not isRequired && not isProjection then
                    refused state (SessionOperationRefusal.UnknownOrCompletedOperation operationId)
                else
                    nextGeneration (SessionOperationStatus.Failed failure) state
