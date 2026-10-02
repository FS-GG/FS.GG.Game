namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type EffectInterpreter = HostEffect list -> unit

[<Sealed>]
type Host private (initialState: HostState, interpret: EffectInterpreter) =
    let mutable state = initialState

    static member Create(settings, interpret) =
        Lifecycle.create settings |> Result.map (fun state -> Host(state, interpret))

    member _.Projection = Lifecycle.project state

    member _.Dispatch(event) =
        let next, effects = Lifecycle.update event state
        state <- next
        interpret effects

    member this.Load(monotonicMilliseconds, identity, artifact) =
        this.Dispatch
            {
                MonotonicMilliseconds = monotonicMilliseconds
                Input = LoadRequested(identity, ReplaceCurrent, artifact)
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
