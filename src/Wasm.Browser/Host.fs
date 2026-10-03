namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type EffectInterpreter = HostEffect list -> unit

[<Sealed>]
type Host private (initialState: HostState, interpret: EffectInterpreter) =
    let mutable state = initialState
    let applyDecision (decision:HostDecision) =
        state <- decision.State
        interpret decision.Effects
        decision.Result

    static member Create(settings, interpret) =
        Lifecycle.create settings |> Result.map (fun state -> Host(state, interpret))

    member _.Projection = Lifecycle.project state
    member _.CompatibilityProjection = Lifecycle.projectCompatibility state

    member _.Dispatch(event) =
        let next, effects = Lifecycle.update event state
        state <- next
        interpret effects

    member _.SubmitLimited(monotonicMilliseconds, request, limits) =
        Lifecycle.submitLimited monotonicMilliseconds request limits state
        |> applyDecision

    member _.CommitCandidateFrozen(monotonicMilliseconds, recoveryToken, transaction, expectedActiveGeneration, candidateGeneration) =
        Lifecycle.commitCandidateFrozen monotonicMilliseconds recoveryToken transaction expectedActiveGeneration candidateGeneration state
        |> applyDecision

    member this.Load(monotonicMilliseconds, identity, artifact) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = LoadRequested(identity, ReplaceCurrent, artifact)
            }

    member this.LoadConfigured(monotonicMilliseconds, identity, configuration, artifact) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = ConfiguredLoadRequested(identity, ReplaceCurrent, configuration, artifact)
            }

    member this.Initialize(monotonicMilliseconds, identity, input) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = InitializeRequested(identity, input)
            }

    member this.PrepareConfiguredCandidate
        (monotonicMilliseconds, identity, transaction, expectedActiveGeneration, configuration, artifact)
        =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input =
                    ConfiguredLoadRequested(
                        identity,
                        PrepareCandidate(transaction, expectedActiveGeneration),
                        configuration,
                        artifact
                    )
            }

    member this.PrepareCandidate(monotonicMilliseconds, identity, transaction, expectedActiveGeneration, artifact) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = LoadRequested(identity, PrepareCandidate(transaction, expectedActiveGeneration), artifact)
            }

    member this.ValidateCandidate(monotonicMilliseconds, transaction, candidateGeneration) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = CandidateValidated(transaction, candidateGeneration)
            }

    member this.CommitCandidate(monotonicMilliseconds, transaction, expectedActiveGeneration, candidateGeneration) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = CandidateCommitRequested(transaction, expectedActiveGeneration, candidateGeneration)
            }

    member this.AbortCandidate(monotonicMilliseconds, transaction) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = CandidateAbortRequested transaction
            }

    member this.Invoke(monotonicMilliseconds, identity, submission, input) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = InvocationRequested(identity, submission, input)
            }

    member this.Shutdown(monotonicMilliseconds, identity) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = ShutdownRequested identity
            }

    member this.Freeze(monotonicMilliseconds, token) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = FreezeRequested token
            }

    member this.Resume(monotonicMilliseconds, token) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = ResumeRequested token
            }

    member this.RejectAdapterOutput(monotonicMilliseconds, identity, diagnostic) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = AdapterOutputRejected(identity, diagnostic)
            }

    member this.Observe(monotonicMilliseconds, workerInstance, observation) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = WorkerObserved(workerInstance, observation)
            }

    member this.ObserveTimer(monotonicMilliseconds, timer) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = TimerObserved timer
            }

    member this.Dispose(monotonicMilliseconds) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = DisposeRequested
            }

    /// Provides the package-owned mechanical interpreter, including identity-safe observations.
    static member CreateConnected(settings: HostSettings, transport: HostTransport, settled: BrowserResult -> unit) =
        let mutable host: Host option = None

        let timerKey (timer: TimerIdentity) =
            $"{timer.Identity.WorkerInstance}:{timer.Identity.Generation}:{timer.Identity.Request}:{OperationToken.value timer.Correlation}:{timer.Phase}"

        let observe worker wire =
            match host, WorkerEntry.observationFromWire wire with
            | Some current, Ok observation -> current.Observe(transport.Now(), worker, observation)
            | _ -> ()

        let interpret effects =
            for effect in effects do
                match effect with
                | CreateWorker(identity, _) ->
                    transport.CreateWorker identity.WorkerInstance (observe identity.WorkerInstance)
                | PostToWorker(worker, command) ->
                    transport.PostCommand worker (WorkerEntry.commandToWire command.Configuration command)
                | TerminateWorker worker ->
                    transport.TerminateWorker worker

                    host
                    |> Option.iter (fun current -> current.Observe(transport.Now(), worker, WorkerTerminated worker))
                | ArmTimer(timer, due) ->
                    let remaining = max 1L (due - transport.Now() + 1L)
                    let delay = int (min remaining (int64 System.Int32.MaxValue))

                    transport.ArmTimer (timerKey timer) delay (fun () ->
                        host
                        |> Option.iter (fun current -> current.ObserveTimer(transport.Now(), timer)))
                | CancelTimer timer -> transport.CancelTimer(timerKey timer)
                | Settle result -> settled result

        match Host.Create(settings, interpret) with
        | Error issues -> Error issues
        | Ok current ->
            host <- Some current
            Ok current
