module Wasm.Supervisor.Compatibility.CompatibilityCorrespondence
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
open Wasm.Lifecycle.Correspondence.CorrespondenceTypes
open Wasm.Lifecycle.Correspondence.Correspondence
open Wasm.Lifecycle.Correspondence.InstalledHostControls
open Wasm.Supervisor.Compatibility.CompatibleTypes

let selectedSettings () =
    let baseline=settings Sc2ImportedStrict
    HostSettings.createCompatible (HostSettings.configuration baseline) (HostSettings.role baseline) Sc2SupervisorV1
    |> Result.defaultWith (failwithf "%A")
let dispatch (trace:CompatibleTrace) (host:Host) (previous:ModelStep) (expected:ModelStep) =
    let detail=trace.Steps |> List.find(fun step->step.Model=expected)
    if expected.Input.Name="frozenCommit" then
        let result=host.CommitCandidateFrozen(expected.Input.Observed,previous.State.FreezeToken,previous.State.CandidateTransaction,previous.State.ActiveGeneration,previous.State.CandidateGeneration)
        if expected.State.ActiveGeneration<>previous.State.CandidateGeneration then
            if result<>Error [RequestOwnerUnavailable] then failwithf "expiry commit decision differs: %A" result
        else result |> Result.defaultWith (failwithf "%A")
    else
        let original=eventFor previous expected
        let limited=
            match original.Input with
            | LoadRequested(identity,ReplaceCurrent,bytes) -> Some(LimitedLoad(identity,HostSettings.configuration(selectedSettings()),bytes))
            | LoadRequested(identity,PrepareCandidate(transaction,active),bytes) -> Some(LimitedCandidate(identity,transaction,active,HostSettings.configuration(selectedSettings()),bytes))
            | InitializeRequested(identity,bytes) -> Some(LimitedInitialize(identity,bytes))
            | InvocationRequested(identity,submission,bytes) -> Some(LimitedInvoke(identity,submission,bytes))
            | ShutdownRequested identity -> Some(LimitedShutdown identity)
            | _ -> None
        match limited with
        | Some request ->
            let result=host.SubmitLimited(expected.Input.Observed,request,detail.Limits)
            if expected.Effects |> List.exists(fun effect->effect.Request=expected.Input.Request.Id && effect.Kind="refused") then
                if result<>Error [RequestRefused GenerationRetired] then failwithf "expiry refusal decision differs: %A" result
            else result |> Result.defaultWith (failwithf "%A")
        | None -> host.Dispatch original
let replay (trace:CompatibleTrace) =
    let model:ModelTrace={Name=trace.Name;SourceSha256=trace.SourceSha256;Steps=trace.Steps |> List.map _.Model}
    let failures =
        [false;true] |> List.collect(fun connected ->
            let mutable projectionFailures=[]
            let dispatched host previous expected =
                dispatch trace host previous expected
                let detail=trace.Steps |> List.find(fun step->step.Model=expected)
                let projection=if connected then detail.Connected else detail.Pure
                if host.CompatibilityProjection<>projection then projectionFailures <- $"{trace.Name} full selected projection differs at {expected.Input.Name}: expected=%A{projection} actual=%A{host.CompatibilityProjection}"::projectionFailures
            let baseline=replayFacadeWith (selectedSettings()) dispatched connected model
            baseline @ projectionFailures)
    // Same pure facade emits captured configurations, projectionFailures independently of projected effects.
    let mutable commands=[]
    let host=Host.Create(selectedSettings(),fun effects ->
        commands<-effects |> List.choose(function
            | PostToWorker(_,command) ->
                let limits=(Validation.configuration command.Configuration).Limits
                Some(command.Identity.Request,limits.MaximumDeadlineMilliseconds,limits.MaximumOutputBytes)
            | _->None)) |> Result.defaultWith (failwithf "%A")
    let mutable commandFailures=[]
    for previous,target in List.pairwise trace.Steps do
        commands<-[]
        dispatch trace host previous.Model target.Model
        let expected=target.Commands |> List.map(fun (request,selected)->request,selected.MaximumDeadlineMilliseconds,selected.MaximumOutputBytes)
        if commands<>expected then commandFailures <- $"{trace.Name} captured command limits differ: expected=%A{expected} actual=%A{commands}"::commandFailures
    failures@commandFailures

// Alter only expected causal facts; all controls use the same real facade validators.
let causalMutationsAreDetected (trace:CompatibleTrace) =
    let changed change =
        let candidate={trace with Steps=trace.Steps |> List.map change}
        try not(List.isEmpty(replay candidate)) with _ -> true
    let modelState change step = {step with Model={step.Model with State=change step.Model.State;Connected=change step.Model.Connected}}
    [ "loaded-deadline-retained",changed(fun step -> {step with Commands=step.Commands |> List.map(fun (id,limit)->id,{limit with MaximumDeadlineMilliseconds=250})})
      "output-narrowing-ignored",changed(fun step -> {step with Commands=step.Commands |> List.map(fun (id,limit)->id,{limit with MaximumOutputBytes=65536})})
      "next-budget-overwritten",changed(fun step -> {step with Commands=step.Commands |> List.map(fun (id,limit)->id,{limit with MaximumOutputBytes=44})})
      "ordinary-priority",changed(fun step ->
          modelState (fun state -> if state.Current.Submission="Ordered" && not state.Ordinary.IsEmpty then {state with Current=state.Ordinary.Head;Ordinary=state.Current::state.Ordinary.Tail} else state) step)
      "ordinary-bytes-omitted",changed(fun step -> modelState (fun state -> if state.Current.Submission="Ordinary" && state.Current.Bytes>1 then {state with Current={state.Current with Bytes=0}} else state) step)
      "held-deadline-started-early",changed(fun step ->
          let alter projection={projection with HeldSnapshot=projection.HeldSnapshot |> Option.map(fun request -> {request with DeadlineMilliseconds=1L})}
          {step with Pure=alter step.Pure;Connected=alter step.Connected})
      "snapshot-promoted-before-pump",changed(fun step ->
          modelState (fun state -> if state.Current.Submission="Ordinary" && (state.Ordinary |> List.exists(fun r->r.Submission="Snapshot")) then {state with Current=state.Ordinary |> List.find(fun r->r.Submission="Snapshot")} else state) step)
      "individual-head-expiry",changed(fun step -> modelState(fun state -> if state.Disposed && (step.Model.Terminals |> List.exists(fun value->value.Outcome="TimedOut" && not value.Dispatched)) then {state with Disposed=false;ActiveWorker="worker-1";ActiveGeneration=1UL} else state) step)
      "retained-frozen-queues",changed(fun step -> modelState(fun state -> if state.Frozen then {state with Ordinary=[{state.Current with Id=999UL;Worker="worker-1"}]} else state) step)
      "temporary-resume-commit",changed(fun step -> modelState(fun state -> if step.Model.Input.Name="frozenCommit" then {state with LastAction="resume"} else state) step)
      "discarded-refusal-expiry-effects",changed(fun step -> if step.Model.Input.Name="ordinary" && (step.Model.Effects |> List.exists(fun fx->fx.Kind="refused")) then {step with Model={step.Model with Effects=[];ConnectedEffects=[]}} else step)
      "discarded-frozen-commit-expiry-effects",changed(fun step -> if step.Model.Input.Name="frozenCommit" && step.Model.State.ActiveGeneration=0UL then {step with Model={step.Model with Effects=[];ConnectedEffects=[]}} else step)
      "cleared-recovery-token",changed(fun step -> modelState(fun state -> if step.Model.Input.Name="frozenCommit" then {state with Frozen=false;FreezeToken=""} else state) step) ]
