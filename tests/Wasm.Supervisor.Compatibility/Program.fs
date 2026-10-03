module Wasm.Supervisor.Compatibility.Program
open System
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
open Wasm.Supervisor.Compatibility.Fixtures
let check value message = if not value then failwith message
let ok = function Ok value -> value | Error issues -> failwithf "%A" issues
let id worker request generation : HostIdentity = {WorkerInstance=worker;Request=request;Generation=generation}
let token (identity:HostIdentity) = OperationToken.create $"request-{identity.Generation}-{identity.Request}" |> ok
let success phase identity = {Identity=identity;Phase=phase;State=Succeeded;Dispatch=Dispatched;CleanupFault=None;CopiedOutput=Array.empty;EffectEligibility=ProductAdapterMustDecide}
let configuration path =
    let d=Profiles.tryFind path |> Option.get
    Validation.validateConfiguration {Path=path;ArtifactSha256=String.replicate 64 "a";ConfigurationSha256=String.replicate 64 "b";Limits=d.Limits;Deadline=d.Deadline;Scheduling=d.Scheduling;Replacement=d.Replacement} |> ok
let limits deadline output = {MaximumDeadlineMilliseconds=deadline;MaximumOutputBytes=output;EnclosingDeadlineMilliseconds=None}
let make selected =
    let mutable effects=[]
    let cfg=configuration Sc2ImportedStrict
    let settings=HostSettings.createCompatible cfg (Some Controller) selected |> ok
    let h=Host.Create(settings,fun emitted -> effects<-effects@emitted) |> ok
    let clear () = effects<-[]
    h,(fun ()->effects),clear,cfg
let ready (h:Host) worker generation first =
    let load=id worker first generation
    h.Load(0L,load,[|0uy|]);h.Observe(0L,worker,AbiVersionObserved(load,token load,0x10000u))
    let initialize=id worker (first+1UL) generation
    h.Initialize(0L,initialize,[|1uy|]);h.Observe(0L,worker,InvocationObserved(success Initialize initialize,token initialize))
let candidate (h:Host) =
    let load=id "candidate" 100UL 2UL
    h.PrepareCandidate(0L,load,"tx",Some 1UL,[|0uy|]);h.Observe(0L,"candidate",AbiVersionObserved(load,token load,0x10000u))
    let initialize=id "candidate" 101UL 2UL
    h.Initialize(0L,initialize,[|1uy|]);h.Observe(0L,"candidate",InvocationObserved(success Initialize initialize,token initialize));h.ValidateCandidate(0L,"tx",2UL)
let complete (h:Host) now (identity:HostIdentity) = h.Observe(now,identity.WorkerInstance,InvocationObserved(success Process identity,token identity))
let posts effects = effects |> List.choose(function PostToWorker(_,c)->Some c.Identity.Request | _->None)
let runSynchronous () =
    let h,effects,clear,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;clear()
    let a=id "active" 6UL 1UL
    let b=id "active" 8UL 1UL
    let c=id "active" 9UL 1UL
    h.SubmitLimited(0L,LimitedInvoke(a,Ordinary,[|1uy|]),limits 100 44) |> ok
    h.SubmitLimited(0L,LimitedInvoke(b,OrderedEvent,[|2uy|]),limits 5 64) |> ok
    h.SubmitLimited(0L,LimitedInvoke(c,Ordinary,[|3uy|]),limits 50 100) |> ok
    check ((h.CompatibilityProjection.MixedQueue |> List.map _.Identity.Request)=[8UL;9UL]) "mixed admission FIFO"
    complete h 1L a;complete h 2L b
    check (posts(effects())=[6UL;8UL;9UL]) "ordered earlier than ordinary"
    check (h.CompatibilityProjection.Current.Value.Limits=limits 50 100) "next request retains own larger limit"
    let narrowed=effects() |> List.choose(function PostToWorker(_,command) -> Some((Validation.configuration command.Configuration).Limits) | _->None)
    check (narrowed.Head.MaximumOutputBytes=44 && narrowed.Head.MaximumDeadlineMilliseconds=100) "captured command narrowing"
    check (narrowed.[2].MaximumOutputBytes=100) "loaded ceiling not overwritten by first narrowing"
    for invalid in [limits 0 44;limits -1 44;limits 251 44;limits 10 0;limits 10 65537] do
        let before=h.Projection
        check (Result.isError(h.SubmitLimited(3L,LimitedInvoke(id "active" 999UL 1UL,Ordinary,[|1uy|]),invalid))) "invalid increase refused"
        check (h.Projection=before) "invalid input changes no state"
    check (Result.isError(h.SubmitLimited(3L,LimitedInvoke(id "active" 1000UL 1UL,Ordinary,[|1uy|]),{limits 10 44 with EnclosingDeadlineMilliseconds=Some 3L}))) "expired enclosing budget"
    let h,effects,clear,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;candidate h;clear()
    h.Invoke(0L,a,Ordinary,[|1uy|]);h.Invoke(0L,b,OrderedEvent,[|2uy|]);h.Invoke(0L,c,ReplaceableSnapshot,[|3uy|]);h.Invoke(0L,id "active" 12UL 1UL,ReplaceableSnapshot,[|4uy|])
    h.Freeze(1L,"recovery")
    check (h.Projection.Ordinary.IsEmpty && h.Projection.Snapshot.Id=0UL) "freeze drains queued and held"
    clear();complete h 2L a
    check (effects() |> List.exists(function Settle r when r.Identity=a -> r.Disposition=HistoricalOnly | _->false)) "frozen active remains historical"
    check (posts(effects()).IsEmpty) "freeze drains with no dispatch"
    let before=h.Projection
    check (Result.isError(h.CommitCandidateFrozen(3L,"wrong","tx",1UL,2UL))) "wrong token rejected"
    check (h.Projection=before) "wrong token no state change"
    h.CommitCandidateFrozen(3L,"recovery","tx",1UL,2UL) |> ok
    check (h.Projection.Frozen && h.Projection.FreezeToken="recovery" && h.Projection.ActiveGeneration=2UL) "atomic frozen promotion retains fence"
    check (Result.isError(h.CommitCandidateFrozen(3L,"recovery","tx",1UL,2UL))) "promotion once"
    check (posts(effects()).IsEmpty) "commit emits no old queue post"
    h.Resume(4L,"recovery");check (not h.Projection.Frozen) "explicit same token resume"
    h.Dispose(5L);h.Dispose(6L)
    check ((h.Projection.Deliveries |> Set.ofList |> Set.count)=h.Projection.Deliveries.Length) "settle once through disposal"
    // Head expiry uses an inclusive boundary and destroys candidate plus queue.
    for observed,expired in [4L,false;5L,true;6L,true] do
        let h,effects,clear,_=make Sc2SupervisorV1
        ready h "active" 1UL 1UL;candidate h;clear()
        h.SubmitLimited(0L,LimitedInvoke(a,Ordinary,[|1uy|]),limits 100 44) |> ok
        h.SubmitLimited(0L,LimitedInvoke(b,OrderedEvent,[|2uy|]),limits 5 44) |> ok
        h.SubmitLimited(0L,LimitedInvoke(c,Ordinary,[|3uy|]),limits 50 44) |> ok
        clear();complete h observed a
        if expired then
            check (h.Projection.Disposed && h.Projection.CandidateWorker="") "expired head destroys whole session"
            check (posts(effects()).IsEmpty) "expired head never posts next work"
            check (effects() |> List.exists(function Settle r when r.Identity=b -> r.Outcome |> Option.exists(fun o->o.State=TimedOut && o.Dispatch=NotDispatched) | _->false)) "head timeout raw undispatched"
        else check (posts(effects())=[8UL]) "before boundary dispatches head"
    // Held snapshots retain a relative budget and join the tail only after pump.
    let h,effects,clear,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;clear()
    h.SubmitLimited(0L,LimitedInvoke(a,ReplaceableSnapshot,[|1uy|]),limits 100 44) |> ok
    h.SubmitLimited(1L,LimitedInvoke(b,Ordinary,[|2uy|]),limits 100 44) |> ok
    h.SubmitLimited(2L,LimitedInvoke(c,ReplaceableSnapshot,[|3uy|]),limits 5 44) |> ok
    let held=id "active" 12UL 1UL
    h.SubmitLimited(3L,LimitedInvoke(held,ReplaceableSnapshot,[|4uy|]),limits 8 64) |> ok
    check (h.CompatibilityProjection.HeldSnapshot.Value.DeadlineMilliseconds=0L) "held budget has not started"
    check (effects() |> List.exists(function Settle r when r.Identity=c -> r.Disposition=Coalesced && r.Outcome.IsNone | _->false)) "held snapshot coalesces undispatched"
    clear();complete h 10L a
    check (posts(effects())=[8UL]) "pump earlier FIFO work first"
    check (h.CompatibilityProjection.MixedQueue.Head.Identity=held && h.CompatibilityProjection.MixedQueue.Head.DeadlineMilliseconds=18L) "held promotion starts selected budget at tail"
    complete h 11L b;check (posts(effects())=[8UL;12UL]) "promoted held snapshot dispatches"
    // Mixed ordinary cap counts earlier ordered entries.
    let h,_,_,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;h.Invoke(0L,a,Ordinary,[|1uy|])
    for request in 20UL..23UL do h.Invoke(0L,id "active" request 1UL,OrderedEvent,[|1uy|])
    check (Result.isError(h.SubmitLimited(0L,LimitedInvoke(b,Ordinary,[|1uy|]),limits 10 44))) "mixed ordinary admission four"
    // End-to-end enclosing budget clamps every relative deadline.
    let h,effects,clear,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;clear()
    h.SubmitLimited(2L,LimitedInvoke(a,Ordinary,[|1uy|]),{limits 10 44 with EnclosingDeadlineMilliseconds=Some 7L}) |> ok
    check (effects() |> List.exists(function ArmTimer(_,due)->due=7L|_->false)) "enclosing deadline clamps timer"
    h.Observe(3L,"active",PhaseObserved(a,token a,AllocateInput))
    check (h.Projection.Current.DeadlineMilliseconds=7L) "phase cannot extend SC2 enclosing deadline"
    check (Result.isError(HostSettings.createCompatible (configuration BarProtected) None Sc2SupervisorV1)) "compatibility optin is SC2 only"
    // Ordered census and bytes include every class, including active ordinary.
    let h,_,_,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;h.Invoke(0L,a,Ordinary,[|1uy|])
    for request in 200UL..454UL do h.Invoke(0L,id "active" request 1UL,OrderedEvent,[|1uy|])
    check (h.CompatibilityProjection.MixedQueue.Length=255) "ordered capacity equality includes active ordinary"
    check (Result.isError(h.SubmitLimited(0L,LimitedInvoke(id "active" 455UL 1UL,OrderedEvent,[|1uy|]),limits 10 44))) "ordered 257th cross-class request refuses"
    let h,_,_,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL
    let block=Array.zeroCreate (256*1024)
    h.Invoke(0L,a,Ordinary,block)
    for request in 200UL..214UL do h.Invoke(0L,id "active" request 1UL,OrderedEvent,block)
    check (h.CompatibilityProjection.MixedQueue.Length=15) "four MiB equality accepted including ordinary bytes"
    check (Result.isError(h.SubmitLimited(0L,LimitedInvoke(id "active" 215UL 1UL,OrderedEvent,[|1uy|]),limits 10 44))) "ordinary bytes cannot evade ordered ceiling"
    let h,_,_,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL
    check (h.SubmitLimited(0L,LimitedInvoke(a,ReplaceableSnapshot,Array.zeroCreate(256*1024+1)),limits 10 44)=Error[OversizedSnapshotInput]) "oversized snapshot is distinct nondestructive refusal"
    check (h.Projection.ActiveWorker="active" && not h.Projection.Disposed) "oversized snapshot preserves session"
    for activeFinished in [false;true] do
        let h,effects,clear,_=make Sc2SupervisorV1
        ready h "active" 1UL 1UL;candidate h;h.Invoke(0L,a,Ordinary,[|1uy|]);h.Freeze(1L,"recovery")
        if activeFinished then complete h 2L a
        clear();h.CommitCandidateFrozen(3L,"recovery","tx",1UL,2UL) |> ok
        check (h.Projection.Frozen && h.Projection.FreezeToken="recovery") "both active-present and finished promotions keep token"
        if not activeFinished then
            complete h 4L a
            check (effects() |> List.exists(function Settle r when r.Identity=a -> r.Disposition=HistoricalOnly | _->false)) "old executing generation historical after promotion"
        h.Resume(5L,"recovery")
    let h,_,_,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;candidate h;h.Freeze(1L,"recovery");h.AbortCandidate(2L,"tx")
    check (h.Projection.Frozen && h.Projection.FreezeToken="recovery" && h.Projection.CandidateWorker="") "abort retains unresolved recovery fence"
    printfn "supervisor-compatibility: focused FIFO, limits, held snapshots, freeze, token promotion, head boundaries and composite budgets PASS"
// Also called with raw numeric fields by the generated-JS boundary controls.
let invalidLimitProbe selected =
    let h,effects,clear,_=make Sc2SupervisorV1
    ready h "active" 1UL 1UL;clear()
    let before=h.Projection
    check (Result.isError(h.SubmitLimited(1L,LimitedInvoke(id "active" 6UL 1UL,Ordinary,[|1uy|]),selected))) "raw malformed limits refused"
    check (h.Projection=before && effects().IsEmpty) "raw malformed limits cannot mutate state or post"

let workerLimits () = async {
    let ceiling=configuration Sc2ImportedStrict
    let mutable output=44
    let mutable loadedCount=0
    let mutable allocation=0u
    let mutable observations:WorkerWireObservation list=[]
    let native:WorkerWireMechanics =
        { Sha256=(fun _ succeed _ -> succeed(String.replicate 64 "a"))
          Compile=(fun _ _ _ succeed _ -> loadedCount<-loadedCount+1;succeed 0x10000u)
          Perform=(fun effect ->
              let pointer=if effect.Kind="allocate" then allocation<-allocation+1024u;allocation else 0u
              {Pointer=pointer;MemoryBytes="131072";Status=0;OutputPointer=8192u;OutputLength=uint32 output;Bytes=Array.zeroCreate output;Diagnostic=""}) }
    let receive=WorkerEntry.createWireRuntime native (fun value -> observations<-observations@[value])
    let send identity cfg operation =
        observations<-[]
        receive (WorkerEntry.commandToWire cfg {Identity=identity;Correlation=token identity;Configuration=cfg;Operation=operation})
    let rec terminal remaining = async {
        if observations |> List.exists(fun value -> value.Kind="loaded" || value.Kind="failed" || value.Kind="outcome" || value.Kind="outcome-terminated") then return observations |> List.last
        elif remaining=0 then return failwith "wire runtime did not produce bounded terminal observation"
        else do! Async.Sleep 1
             return! terminal(remaining-1) }
    let narrow deadline bytes =
        let original=Validation.configuration ceiling
        Validation.validateConfiguration {original with Limits={original.Limits with MaximumDeadlineMilliseconds=deadline;MaximumOutputBytes=bytes}} |> ok
    send (id "worker" 1UL 1UL) ceiling (InspectAndCompile(ceiling,sc2Module))
    let! loaded=terminal 1000
    check (loaded.Kind="loaded") (sprintf "actual Worker wire loaded ceiling %A" loaded)
    send (id "worker" 2UL 1UL) (narrow 10 44) (InitializeGuest [|1uy|])
    let! initialized=terminal 1000
    check (initialized.State="succeeded" && initialized.Output.Length=44) "44-byte narrowed init succeeds without reload"
    output<-60
    send (id "worker" 3UL 1UL) (narrow 5 64) (ProcessGuest(Ordinary,[|1uy|]))
    let! processed=terminal 1000
    check (processed.State="succeeded" && processed.Output.Length=60 && loadedCount=1) "later larger output budget reuses immutable loaded guest"
    output<-45
    send (id "worker" 4UL 1UL) (narrow 5 44) (ProcessGuest(Ordinary,[|1uy|]))
    let! rejected=terminal 1000
    check (rejected.Kind="outcome-terminated" && rejected.State="faulted" && rejected.Output.Length=0) "45-byte output rejected by44 cap"
    // A new worker's downward relation refuses every other field and identity drift.
    send (id "worker" 5UL 1UL) ceiling (InspectAndCompile(ceiling,sc2Module))
    let! _=terminal 1000
    let original=Validation.configuration ceiling
    let changed=Validation.validateConfiguration {original with ConfigurationSha256=String.replicate 64 "c"} |> ok
    send (id "worker" 6UL 1UL) changed (InitializeGuest [|1uy|])
    let! drift=terminal 1000
    check (drift.Kind="failed") "configuration digest drift refused"
    send (id "worker" 7UL 2UL) ceiling (InitializeGuest [|1uy|])
    let! generation=terminal 1000
    check (generation.Kind="failed") "worker generation drift refused"
    printfn "worker-limits: immutable ceiling, same guest, selected output rejection, digest and generation controls PASS"
}
[<EntryPoint>]
let main _ =
    runSynchronous()
    let defaultFailures=Wasm.Lifecycle.Correspondence.GeneratedTraces.traces |> List.collect(fun trace ->
        Wasm.Lifecycle.Correspondence.InstalledHostControls.replayFacade false trace @ Wasm.Lifecycle.Correspondence.InstalledHostControls.replayFacade true trace)
    if not defaultFailures.IsEmpty then failwithf "default facade regression: %s" (String.concat "\n" defaultFailures)
    printfn "default canonical correspondence: 47 original traces, both public facades PASS"
    let correspondenceFailures=GeneratedCompatibilityTraces.traces |> List.collect CompatibilityCorrespondence.replay
    if not correspondenceFailures.IsEmpty then failwithf "%s" (String.concat "\n" correspondenceFailures)
    let detected=GeneratedCompatibilityTraces.traces |> List.filter(fun trace->trace.Name<>"capacity") |> List.collect CompatibilityCorrespondence.causalMutationsAreDetected
    for name in ["loaded-deadline-retained";"output-narrowing-ignored";"next-budget-overwritten";"ordinary-priority";"ordinary-bytes-omitted";"held-deadline-started-early";"snapshot-promoted-before-pump";"individual-head-expiry";"retained-frozen-queues";"temporary-resume-commit";"cleared-recovery-token"] do
        check (detected |> List.exists(fun (control,refused)->control=name && refused)) ("causal model mutation accepted: "+name)
    printfn "selected canonical correspondence: all full pure/connected states, ordered mechanical effects, callbacks, raw terminals and captured command limits PASS"
    #if FABLE_COMPILER
    Async.StartImmediate(workerLimits())
    #else
    Async.RunSynchronously(workerLimits())
    #endif
    0
