module Wasm.Lifecycle.Correspondence.Correspondence

open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
open Wasm.Lifecycle.Correspondence.CorrespondenceTypes

let private sha character = String.replicate 64 character

let settings profile =
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

// The original typed input survives pre-event expiry; LastAction is an output.
let eventFor (previous: ModelStep) (target: ModelStep) =
    let prior = previous.State
    let original = target.Input
    let request = original.Request
    let id = identity request.Worker request.Id request.Generation
    let correlation () =
        OperationToken.create request.Correlation
        |> Result.defaultWith (fun value -> failwithf "%A" value)
    let outcome phase = successfulOutcome phase id
    let observation input = WorkerObserved(request.Worker, input)
    let input =
        match original.Name with
        | "loadInitial" -> LoadRequested(id, ReplaceCurrent, Array.zeroCreate request.Bytes)
        | "prepareCandidate" ->
            LoadRequested(id, PrepareCandidate(string request.Generation, if prior.ActiveWorker = "" then None else Some prior.ActiveGeneration), Array.zeroCreate request.Bytes)
        | "compiled" -> observation (AbiVersionObserved(id, correlation (), if prior.Profile = "Bar" then 1u else 0x10000u))
        | "initialize" -> InitializeRequested(id, Array.zeroCreate request.Bytes)
        | "initialized" -> observation (InvocationObserved(outcome Initialize, correlation ()))
        | "phase" ->
            let phase =
                match original.Phase with
                | "instantiate" -> Instantiate
                | "allocate-input" -> AllocateInput
                | "initialize" -> Initialize
                | "process" -> Process
                | "free" -> Free
                | value -> failwithf "unsupported phase %s" value
            observation (PhaseObserved(id, correlation (), phase))
        | "ordinary" | "ordered" | "snapshot" ->
            let submission = match original.Name with "ordered" -> OrderedEvent | "snapshot" -> ReplaceableSnapshot | _ -> Ordinary
            InvocationRequested(id, submission, Array.zeroCreate request.Bytes)
        | "shutdown" -> ShutdownRequested id
        | "candidateValidated" -> CandidateValidated(prior.CandidateTransaction, prior.CandidateGeneration)
        | "commitCandidate" -> CandidateCommitRequested(prior.CandidateTransaction, prior.ActiveGeneration, prior.CandidateGeneration)
        | "complete" -> observation (InvocationObserved(outcome (if request.Operation = "shutdown" then Shutdown else Process), correlation ()))
        | "rejectedTermination" -> observation (InvocationTerminated({ outcome Process with State = GuestRejected 92 }, correlation ()))
        | "cleanupFault" -> observation (InvocationObserved({ outcome Process with State = Faulted "cleanup"; CleanupFault = Some { Phase = Free; Diagnostic = "cleanup" } }, correlation ()))
        | "candidateFailed" -> observation (WorkerFailed(id, Some(correlation ()), "candidate failure"))
        | "abortCandidate" -> CandidateAbortRequested prior.CandidateTransaction
        | "terminateRetiring" -> WorkerObserved(original.Worker, WorkerTerminated original.Worker)
        | "freeze" -> FreezeRequested "7"
        | "resume" -> ResumeRequested prior.FreezeToken
        | "dispose" -> DisposeRequested
        | "timer" -> TimerObserved { Identity = identity prior.ActiveWorker 0UL prior.ActiveGeneration; Correlation = operationToken (); Phase = Process }
        | value -> failwithf "unsupported original input %s" value
    { MonotonicMilliseconds = original.Observed; Input = input }

let private replayWithEffectMutation (mutate: ModelStep -> EffectProjection list -> EffectProjection list) (trace: ModelTrace) =
    let profile =
        if trace.Steps.Head.State.Profile = "Bar" then
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
        // Check raw cause and dispatch evidence which projectEffects deliberately omits.
        let terminals =
            effects |> List.choose (function
                | Settle result ->
                    let reason = match result.Reason with Some DeadlineExpired -> "DeadlineExpired" | Some HostDisposed -> "HostDisposed" | _ -> ""
                    if reason = "" then None else
                        let phase = result.Outcome |> Option.map (fun value -> match value.Phase with Compile -> "compile" | Instantiate -> "instantiate" | AllocateDescriptor -> "allocate-descriptor" | AllocateInput -> "allocate-input" | Initialize -> "initialize" | Process -> "process" | Free -> "free" | Shutdown -> "shutdown") |> Option.defaultValue ""
                        Some ({ Request = result.Identity.Request; Reason = reason; Outcome = result.Outcome |> Option.map (fun value -> if value.State = TimedOut then "TimedOut" else "unexpected") |> Option.defaultValue ""; Phase = phase; Dispatched = result.Outcome |> Option.exists (fun value -> value.Dispatch = Dispatched) } : TerminalExpectation)
                | _ -> None)
        if terminals <> expected.Terminals then
            failures <- $"step {index + 1} raw terminal cause/outcome/phase/dispatch differs after original {expected.Input.Name}: expected=%A{expected.Terminals} actual=%A{terminals}" :: failures

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
    let mutate (expected: ModelStep) (effects: EffectProjection list) =
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

// Causal corruptions are applied to actual projected effects before the SAME verifier.
let eventBoundaryMutationsAreDetected (trace: ModelTrace) =
    let hasFailure mutate = not (replayWithEffectMutation mutate trace).IsEmpty
    [ "expired-to-disposed", (fun (_: ModelStep) (effects: EffectProjection list) -> effects |> List.map(fun effect -> if effect.Kind = "settleTimedOut" then { effect with Kind = "settleDiscarded" } else effect))
      "drop-termination", (fun _ effects -> effects |> List.filter(fun effect -> effect.Kind <> "terminate"))
      "reorder-expiry", (fun _ effects -> if effects |> List.exists(fun effect -> effect.Kind = "settleTimedOut") then List.rev effects else effects)
      "drop-current-cancel", (fun _ effects -> effects |> List.filter(fun effect -> effect.Kind <> "cancelTimer"))
      "late-completion-current", (fun expected effects -> if expected.Input.Name = "complete" then effects |> List.map(fun effect -> if effect.Kind = "settleTimedOut" then { effect with Kind = "settleCurrent" } else effect) else effects)
      "queued-dispatch-after-expiry", (fun _ effects -> match effects |> List.tryFind(fun effect -> effect.Kind = "settleTimedOut") with Some terminal -> effects @ [{ terminal with Kind = "post"; Operation = "process"; Bytes = 1 }] | None -> effects)
      "wrong-terminal-correlation", (fun _ effects -> effects |> List.map(fun effect -> if effect.Kind = "cancelTimer" then { effect with Correlation = "foreign" } else effect)) ]
    |> List.map(fun (name,mutate) -> name,hasFailure mutate)
