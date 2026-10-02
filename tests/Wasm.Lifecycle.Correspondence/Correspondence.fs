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

    match state.LastAction with
    | "loadInitial" ->
        at (LoadRequested(identity state.ActiveWorker 0UL state.ActiveGeneration, ReplaceCurrent, Array.empty))
    | "begin" ->
        let request = state.Current

        let submission =
            match request.Submission with
            | "Ordinary" -> Ordinary
            | "Ordered" -> OrderedEvent
            | "Snapshot" -> ReplaceableSnapshot
            | value -> failwithf "unknown submission %s" value

        at (InvocationRequested(requestIdentity request state.ActiveWorker, submission, Array.zeroCreate request.Bytes))
    | "queueOrdinary" ->
        let request = List.last state.Ordinary
        at (InvocationRequested(requestIdentity request state.ActiveWorker, Ordinary, Array.zeroCreate request.Bytes))
    | "queueOrdered" ->
        let request = List.last state.Ordered

        at (
            InvocationRequested(
                requestIdentity request state.ActiveWorker,
                OrderedEvent,
                Array.zeroCreate request.Bytes
            )
        )
    | "queueSnapshot"
    | "coalesceSnapshot" ->
        let request = state.Snapshot

        at (
            InvocationRequested(
                requestIdentity request state.ActiveWorker,
                ReplaceableSnapshot,
                Array.zeroCreate request.Bytes
            )
        )
    | "refuse" ->
        let effect = List.head target.Effects
        at (InvocationRequested(identity effect.Worker effect.Request effect.Generation, Ordinary, [| 0uy |]))
    | "prepareCandidate" ->
        at (
            LoadRequested(
                identity state.CandidateWorker 0UL state.CandidateGeneration,
                PrepareCandidate(state.CandidateTransaction, Some prior.ActiveGeneration),
                Array.empty
            )
        )
    | "candidateInitialized" ->
        let candidateIdentity = identity state.CandidateWorker 0UL state.CandidateGeneration

        at (
            WorkerObserved(
                state.CandidateWorker,
                InvocationObserved(successfulOutcome Initialize candidateIdentity, operationToken ())
            )
        )
    | "candidateValidated" -> at (CandidateValidated(state.CandidateTransaction, state.CandidateGeneration))
    | "commitCandidate" ->
        at (CandidateCommitRequested(prior.CandidateTransaction, prior.ActiveGeneration, prior.CandidateGeneration))
    | "complete" ->
        let completed = prior.Current
        let completedWorker = $"worker-{completed.Generation}"
        let completedIdentity = requestIdentity completed completedWorker

        at (
            WorkerObserved(
                completedWorker,
                InvocationObserved(successfulOutcome Process completedIdentity, operationToken ())
            )
        )
    | "cleanupFault" ->
        let completed = prior.Current
        let completedIdentity = requestIdentity completed prior.ActiveWorker

        let outcome =
            { successfulOutcome Process completedIdentity with
                State = Faulted "cleanup"
            }

        at (WorkerObserved(prior.ActiveWorker, InvocationObserved(outcome, operationToken ())))
    | "freeze" -> at (FreezeRequested state.FreezeToken)
    | "resume" -> at (ResumeRequested prior.FreezeToken)
    | "dispose"
    | "disposeAgain" -> at DisposeRequested
    | "advanceTime"
    | "expireOrdinary"
    | "expireCurrent"
    | "stutter" ->
        let timer =
            {
                Identity = identity prior.ActiveWorker 0UL prior.ActiveGeneration
                Correlation = operationToken ()
                Phase = Process
            }

        at (TimerObserved timer)
    | action -> failwithf "correspondence trace action is unsupported: %s" action

let private replayWithEffectMutation mutate trace =
    let profile =
        if trace.Name = "bar" then
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
