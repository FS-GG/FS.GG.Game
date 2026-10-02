module Wasm.Lifecycle.Correspondence.Correspondence

open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
open Wasm.Lifecycle.Correspondence.CorrespondenceTypes

let private sha character = String.replicate 64 character

let private settings profile =
    let descriptor = Profiles.tryFind profile |> Option.get

    let configuration =
        Validation.validateConfiguration
            {
                Path = profile
                ArtifactSha256 = sha "a"
                ConfigurationSha256 = sha "b"
                Limits = descriptor.Limits
                Deadline = descriptor.Deadline
                Scheduling = descriptor.Scheduling
                Replacement = descriptor.Replacement
            }
        |> function
            | Ok value -> value
            | Error issues -> failwithf "configuration failed: %A" issues

    HostSettings.create configuration (if profile = BarProtected then None else Some Controller)
    |> function
        | Ok value -> value
        | Error issues -> failwithf "host settings failed: %A" issues

let private identity worker request generation : HostIdentity =
    {
        WorkerInstance = worker
        Request = request
        Generation = generation
    }

let private operationToken () =
    OperationToken.create "model-trace"
    |> Result.defaultWith (fun issue -> failwithf "%A" issue)

let private successfulOutcome phase request =
    {
        Identity = request
        Phase = phase
        State = Succeeded
        Dispatch = Dispatched
        CleanupFault = None
        CopiedOutput = Array.empty
        EffectEligibility = ProductAdapterMustDecide
    }

let private requestIdentity (request: RequestProjection) worker =
    identity worker request.Id request.Generation

let private eventFor (previous: ModelStep) (target: ModelStep) =
    let state = target.State
    let prior = previous.State

    let at input =
        {
            MonotonicMilliseconds = state.Clock
            Input = input
        }

    let id (request: RequestProjection) =
        identity request.Worker request.Id request.Generation

    let correlation (request: RequestProjection) =
        OperationToken.create request.Correlation
        |> Result.defaultWith (fun value -> failwithf "%A" value)

    let removedControl () =
        prior.Controls
        |> List.find (fun old -> state.Controls |> List.forall (fun current -> current.Id <> old.Id))

    let addedControl () =
        state.Controls
        |> List.find (fun current -> prior.Controls |> List.forall (fun old -> old.Id <> current.Id))

    match state.LastAction with
    | "loadInitial" ->
        let request = addedControl ()
        at (LoadRequested(id request, ReplaceCurrent, Array.zeroCreate request.Bytes))
    | "prepareCandidate" ->
        let request = addedControl ()

        let expected =
            if prior.ActiveWorker = "" then
                None
            else
                Some prior.ActiveGeneration

        at (
            LoadRequested(
                id request,
                PrepareCandidate(state.CandidateTransaction, expected),
                Array.zeroCreate request.Bytes
            )
        )
    | "compiled" ->
        let request = removedControl ()
        let version = if state.Profile = "Bar" then 1u else 0x10000u
        at (WorkerObserved(request.Worker, AbiVersionObserved(id request, correlation request, version)))
    | "initialize" ->
        let request = addedControl ()
        at (InitializeRequested(id request, Array.zeroCreate request.Bytes))
    | "candidateInitialized" ->
        let request = removedControl ()

        at (
            WorkerObserved(
                request.Worker,
                InvocationObserved(successfulOutcome Initialize (id request), correlation request)
            )
        )
    | "phase" ->
        let priorRequests =
            prior.Controls @ (if prior.Current.Worker = "" then [] else [ prior.Current ])

        let requests =
            state.Controls @ (if state.Current.Worker = "" then [] else [ state.Current ])

        let request =
            requests
            |> List.find (fun current ->
                priorRequests
                |> List.exists (fun old -> old.Id = current.Id && old.Phase <> current.Phase))

        let phase =
            match request.Phase with
            | "instantiate" -> Instantiate
            | "allocate-input" -> AllocateInput
            | "initialize" -> Initialize
            | "process" -> Process
            | "free" -> Free
            | value -> failwithf "unsupported phase %s" value

        at (WorkerObserved(request.Worker, PhaseObserved(id request, correlation request, phase)))
    | "begin"
    | "queueOrdinary"
    | "queueOrdered"
    | "queueSnapshot"
    | "coalesceSnapshot" ->
        let request =
            match state.LastAction with
            | "begin" -> state.Current
            | "queueOrdinary" -> List.last state.Ordinary
            | "queueOrdered" -> List.last state.Ordered
            | _ -> state.Snapshot

        if request.Operation = "shutdown" then
            at (ShutdownRequested(id request))
        else
            let submission =
                match request.Submission with
                | "Ordinary" -> Ordinary
                | "Ordered" -> OrderedEvent
                | _ -> ReplaceableSnapshot

            at (InvocationRequested(id request, submission, Array.zeroCreate request.Bytes))
    | "refuse" ->
        let effect = List.head target.Effects
        at (InvocationRequested(identity effect.Worker effect.Request effect.Generation, Ordinary, [| 0uy |]))
    | "candidateValidated" -> at (CandidateValidated(state.CandidateTransaction, state.CandidateGeneration))
    | "commitCandidate" ->
        at (CandidateCommitRequested(prior.CandidateTransaction, prior.ActiveGeneration, prior.CandidateGeneration))
    | "complete" ->
        let request = prior.Current
        let phase = if request.Operation = "shutdown" then Shutdown else Process

        let rejected =
            target.Effects |> List.exists (fun effect -> effect.Kind = "settleRejected")

        let outcome =
            if rejected then
                { successfulOutcome phase (id request) with
                    State = GuestRejected 92
                }
            else
                successfulOutcome phase (id request)

        let observation =
            if rejected then
                InvocationTerminated(outcome, correlation request)
            else
                InvocationObserved(outcome, correlation request)

        at (WorkerObserved(request.Worker, observation))
    | "cleanupFault" ->
        let request = prior.Current

        let outcome =
            { successfulOutcome Process (id request) with
                State = Faulted "cleanup"
                CleanupFault = Some { Phase = Free; Diagnostic = "cleanup" }
            }

        at (WorkerObserved(request.Worker, InvocationObserved(outcome, correlation request)))
    | "workerFailed" ->
        if prior.Controls |> List.exists (fun r -> r.Worker = prior.CandidateWorker) then
            let request =
                prior.Controls |> List.find (fun r -> r.Worker = prior.CandidateWorker)

            at (
                WorkerObserved(request.Worker, WorkerFailed(id request, Some(correlation request), "candidate failure"))
            )
        else
            at (WorkerObserved(prior.Current.Worker, WorkerTerminated prior.Current.Worker))
    | "abortCandidate" -> at (CandidateAbortRequested prior.CandidateTransaction)
    | "terminateRetiring" ->
        let worker =
            prior.RetiringWorkers
            |> List.find (fun w -> not (List.contains w state.RetiringWorkers))

        at (WorkerObserved(worker, WorkerTerminated worker))
    | "freeze" -> at (FreezeRequested state.FreezeToken)
    | "resume" -> at (ResumeRequested prior.FreezeToken)
    | "dispose"
    | "disposeAgain" -> at DisposeRequested
    | "expireOrdinary"
    | "expireCurrent"
    | "expireControl"
    | "stutter" ->
        let timer =
            {
                Identity = identity prior.ActiveWorker 0UL prior.ActiveGeneration
                Correlation = operationToken ()
                Phase = Process
            }

        at (TimerObserved timer)
    | value -> failwithf "unsupported correspondence action %s" value

let private replayWithEffectMutation mutate trace =
    let profile =
        if trace.Name = "bar" || trace.Name = "phase" then
            BarProtected
        else
            Sc2ImportedStrict

    let mutable state =
        Lifecycle.create (settings profile)
        |> Result.defaultWith (fun issues -> failwithf "%A" issues)

    let mutable failures = []
    let initial = List.head trace.Steps

    if Lifecycle.project state <> initial.State then
        failures <- "initial state differs" :: failures

    trace.Steps
    |> List.pairwise
    |> List.iteri (fun index (previous, expected) ->
        let next, effects = Lifecycle.update (eventFor previous expected) state
        state <- next
        let actualState = Lifecycle.project state
        let actualEffects = Lifecycle.projectEffects effects |> mutate expected

        if actualState <> expected.State then
            failures <-
                $"step {index + 1} state differs after {expected.State.LastAction}: expected=%A{expected.State} actual=%A{actualState}"
                :: failures

        if actualEffects <> expected.Effects then
            failures <-
                $"step {index + 1} effects differ after {expected.State.LastAction}: expected=%A{expected.Effects} actual=%A{actualEffects}"
                :: failures)

    List.rev failures

let replay trace =
    replayWithEffectMutation (fun _ effects -> effects) trace

let staleGenerationMutationIsDetected trace =
    let mutate expected effects =
        if expected.Effects |> List.exists (fun effect -> effect.Kind = "settleHistorical") then
            effects
            |> List.map (fun effect ->
                if effect.Kind = "settleHistorical" then
                    { effect with Kind = "settleCurrent" }
                else
                    effect)
        else
            effects

    replayWithEffectMutation mutate trace
    |> List.exists (fun failure -> failure.Contains("effects differ after complete"))

let commandPayloadMutationIsDetected trace =
    replayWithEffectMutation
        (fun _ effects ->
            effects
            |> List.map (fun effect ->
                if effect.Kind = "post" && effect.Operation = "load" then
                    { effect with Bytes = 0 }
                else
                    effect))
        trace
    |> List.exists (fun failure -> failure.Contains("effects differ after loadInitial"))

let shutdownOperationMutationIsDetected trace =
    replayWithEffectMutation
        (fun _ effects ->
            effects
            |> List.map (fun effect ->
                if effect.Kind = "post" && effect.Operation = "shutdown" then
                    { effect with Operation = "process" }
                else
                    effect))
        trace
    |> List.exists (fun failure -> failure.Contains("effects differ after begin"))

let phaseDeadlineMutationIsDetected trace =
    replayWithEffectMutation
        (fun _ effects ->
            effects
            |> List.map (fun effect ->
                if effect.Kind = "armTimer" then
                    { effect with Due = effect.Due + 1L }
                else
                    effect))
        trace
    |> List.exists (fun failure -> failure.Contains("effects differ after phase"))
