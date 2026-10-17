// Current facade controls shared by source and exact selected-package qualification.
module Wasm.Lifecycle.Correspondence.InstalledHostControls
open System
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
open Wasm.Lifecycle.Correspondence.CorrespondenceTypes
open Wasm.Lifecycle.Correspondence.Correspondence

type Mechanical =
    { Kind:string; Worker:string; Request:uint64; Generation:uint64; Correlation:string
      Operation:string; Bytes:int; Timer:string; Delay:int; Disposition:string; State:HostProjection }
let private phaseName = function
    | "compile" -> "Compile" | "instantiate" -> "Instantiate" | "allocate-descriptor" -> "AllocateDescriptor"
    | "allocate-input" -> "AllocateInput" | "initialize" -> "Initialize" | "process" -> "Process"
    | "free" -> "Free" | "shutdown" -> "Shutdown" | value -> failwithf "unknown phase %s" value
let private rawPhase = function
    | Compile -> "compile" | Instantiate -> "instantiate" | AllocateDescriptor -> "allocate-descriptor"
    | AllocateInput -> "allocate-input" | Initialize -> "initialize" | Process -> "process" | Free -> "free" | Shutdown -> "shutdown"
let private blank (state:HostProjection) kind worker : Mechanical =
    { Kind=kind;Worker=worker;Request=0UL;Generation=0UL;Correlation="";Operation="";Bytes=0;Timer="";Delay=0;Disposition="";State=state }
let private mechanicalExpectation (observed:int64) (state:HostProjection) (effect:EffectProjection) =
    let empty=blank state "" effect.Worker
    match effect.Kind with
    | "createActive" | "createCandidate" -> {empty with Kind="create"}
    | "terminate" -> {empty with Kind="terminate"}
    | "post" -> {empty with Kind="post";Request=effect.Request;Generation=effect.Generation;Correlation=effect.Correlation;Operation=effect.Operation;Bytes=effect.Bytes}
    | "armTimer" | "cancelTimer" ->
        let key = $"{effect.Worker}:{effect.Generation}:{effect.Request}:{effect.Correlation}:{phaseName effect.Phase}"
        {empty with Kind=effect.Kind;Timer=key;Delay=if effect.Kind="armTimer" then int(min (int64 Int32.MaxValue) (max 1L (effect.Due-observed+1L))) else 0}
    | value when value.StartsWith("settle") || value.StartsWith("refused") ->
        let disposition = if value.StartsWith("refused") then "Refused" elif value="settleHistorical" then "HistoricalOnly" elif value="settleCoalesced" then "Coalesced" elif value="settleDiscarded" || value="settleTimedOut" || value="settleInvalidated" then "Discarded" else "Current"
        {empty with Kind="settle";Request=effect.Request;Generation=effect.Generation;Disposition=disposition}
    | value -> failwithf "unmapped mechanical effect %s" value

let replayFacadeWith (selectedSettings:HostSettings) dispatch (connected:bool) (trace:ModelTrace) =
    let mutable emitted:HostEffect list=[]
    let mutable delivered:BrowserResult list=[]
    let mutable mechanical:Mechanical list=[]
    let mutable terminations:(string*int64) list=[]
    let mutable now=0L
    let mutable capturedHost:Host option=None
    let snapshot ()=capturedHost.Value.Projection
    let record kind worker = blank (snapshot()) kind worker
    let append value = mechanical <- mechanical @ [value]
    let transport:HostTransport =
        { Now=(fun () -> now)
          CreateWorker=(fun worker _ -> append(record "create" worker))
          PostCommand=(fun worker command -> append {record "post" worker with Request=UInt64.Parse command.Identity.Request;Generation=UInt64.Parse command.Identity.Generation;Correlation=command.Correlation;Operation=command.Kind;Bytes=command.Bytes.Length})
          TerminateWorker=(fun worker -> append(record "terminate" worker);terminations <- terminations @ [worker,now])
          ArmTimer=(fun key delay _ -> append {record "armTimer" (key.Split(':').[0]) with Timer=key;Delay=delay})
          CancelTimer=(fun key -> append {record "cancelTimer" (key.Split(':').[0]) with Timer=key}) }
    let host =
        (if connected then Host.CreateConnected(selectedSettings,transport,fun result ->
            delivered <- delivered @ [result]
            append {record "settle" result.Identity.WorkerInstance with Request=result.Identity.Request;Generation=result.Identity.Generation;Disposition=string result.Disposition})
         else Host.Create(selectedSettings,fun effects -> emitted <- emitted @ effects))
        |> Result.defaultWith(fun issues -> failwithf "%A" issues)
    capturedHost <- Some host
    let mutable failures=[]
    for (previous,expected) in List.pairwise trace.Steps do
        emitted <- [];delivered <- [];mechanical <- [];terminations <- [];now <- expected.Input.Observed
        dispatch host previous expected
        let target=if connected then expected.Connected else expected.State
        let effects=if connected then expected.ConnectedEffects else expected.Effects
        let terminals=if connected then expected.ConnectedTerminals else expected.Terminals
        if host.Projection<>target then failures <- $"{trace.Name} facade connected={connected} full state differs original={expected.Input.Name}" :: failures
        if not connected && Lifecycle.projectEffects emitted<>effects then failures <- $"{trace.Name} ordered facade effects differ" :: failures
        if connected then
            let expectedMechanical=List.map2 (mechanicalExpectation now) expected.ConnectedBeforeEffects effects
            if mechanical<>expectedMechanical then failures <- $"{trace.Name} ordered mechanical effects/full before-effect projections differ: expected=%A{expectedMechanical} actual=%A{mechanical}" :: failures
            let expectedCallbacks=expected.ConnectedCallbacks |> List.map(fun input -> input.Worker,input.Observed)
            if terminations<>expectedCallbacks then failures <- $"{trace.Name} synchronous termination callback identities/time differ" :: failures
        let results=if connected then delivered else emitted |> List.choose(function Settle result -> Some result | _ -> None)
        let ids=results |> List.map(fun result -> result.Identity.Request)
        let expectedIds=effects |> List.filter(fun effect -> effect.Kind.StartsWith("settle") || effect.Kind.StartsWith("refused")) |> List.map _.Request
        if ids<>expectedIds then failures <- $"{trace.Name} ordered facade terminal identity differs" :: failures
        for terminal in terminals do
            match results |> List.tryFind(fun result -> result.Identity.Request=terminal.Request) with
            | None -> failures <- $"{trace.Name} terminal callback missing" :: failures
            | Some result ->
                let reason=match result.Reason with Some DeadlineExpired -> "DeadlineExpired" | Some HostDisposed -> "HostDisposed" | Some (WorkerFault diagnostic) -> "WorkerFault:"+diagnostic | Some (AdapterRejected diagnostic) -> "AdapterRejected:"+diagnostic | _ -> ""
                let outcome=result.Outcome |> Option.map(fun value -> match value.State with TimedOut -> "TimedOut" | Faulted _ -> "Faulted" | _ -> "unexpected") |> Option.defaultValue ""
                let phase=result.Outcome |> Option.map(fun value -> rawPhase value.Phase) |> Option.defaultValue ""
                let dispatched=result.Outcome |> Option.exists(fun value -> value.Dispatch=Dispatched)
                if reason<>terminal.Reason || outcome<>terminal.Outcome || phase<>terminal.Phase || dispatched<>terminal.Dispatched then failures <- $"{trace.Name} raw facade cause/outcome/phase/dispatch differs" :: failures
    List.rev failures

let replayFacade connected (trace:ModelTrace) =
    let profile=if trace.Steps.Head.State.Profile="Bar" then BarProtected else Sc2ImportedStrict
    replayFacadeWith (settings profile) (fun host previous expected -> host.Dispatch(eventFor previous expected)) connected trace

// Causal controls pass altered expectations through the same real installed
// facade validator; neither the Host nor its callback interpreter is copied.
let nestedCallbackMutationsAreDetected (trace:ModelTrace) =
    let mutate change =
        let changed={trace with Steps=trace.Steps |> List.map(fun step -> if step.ConnectedCallbacks.Length>1 then change step else step)}
        not(List.isEmpty(replayFacade true changed))
    [ "second-callback-omitted",mutate(fun step -> {step with ConnectedCallbacks=step.ConnectedCallbacks |> List.take 1})
      "nested-termination-reordered",mutate(fun step ->
          match step.ConnectedEffects with
          | first::second::third::rest -> {step with ConnectedEffects=first::third::second::rest}
          | _ -> step) ]

// Runs against both public facades (and the installed package build). Transport
// is inert: this checks real reducer/effect/callback ordering without guest code.
let terminalRetirementControls (selectedSettings:HostSettings) =
    let require condition message = if not condition then failwith ("terminal retirement: " + message)
    let unwrap = function Ok value -> value | Error issues -> failwithf "%A" issues
    let identity (request:uint64) : HostIdentity = { WorkerInstance = "terminal-worker"; Request = request; Generation = 1UL }
    let correlation (id:HostIdentity) = OperationToken.create $"request-{id.Generation}-{id.Request}" |> unwrap
    let outcome (phase:InvocationPhase) (id:HostIdentity) : InvocationOutcome = { Identity=id; Phase=phase; State=Succeeded; Dispatch=Dispatched; CleanupFault=None; CopiedOutput=Array.empty; EffectEligibility=ProductAdapterMustDecide }
    let limits : RequestLimits = { MaximumDeadlineMilliseconds=100; MaximumOutputBytes=64; EnclosingDeadlineMilliseconds=None }
    for connected, rejected, completeFromPost, adapterRejected in [ for connected in [false;true] do for rejected in [false;true] do for sync in [false;true] do for adapter in [false;true] do yield connected,rejected,sync,adapter ] do
        let mutable host:Host option=None
        let mutable mechanics=[]
        let mutable results:BrowserResult list=[]
        let mutable reentered=false
        let mutable completed=false
        let mutable armed:Set<string>=Set.empty
        let reset=identity 3UL
        let follower=identity 4UL
        let fresh=identity 5UL
        let actual={ outcome Process reset with State=(if rejected then GuestRejected 27 else Succeeded); CopiedOutput=[|4uy;5uy|] }
        let completeTerminal () =
            if adapterRejected then host.Value.RejectAdapterOutput(1L,reset,"adapter rejection")
            else host.Value.Observe(1L,reset.WorkerInstance,InvocationObserved(actual,correlation reset))
        let observedPost request =
            if completeFromPost && request=reset.Request then completeTerminal ()
        let record value = mechanics <- mechanics @ [value]
        let settle (result:BrowserResult) =
            results <- results @ [result]
            if result.Identity=reset then
                require host.Value.Projection.Disposed "disposed state must precede callback"
                require armed.IsEmpty "all timers canceled before callback, including synchronous Post completion"
                require (mechanics |> List.contains "terminate") "worker retired before result"
                completed <- true
                let stateBefore=host.Value.Projection
                let mechanicsBefore=mechanics
                let resultCountBefore=results.Length
                let admission=host.Value.SubmitLimited(2L,LimitedInvoke(fresh,Ordinary,[|9uy|]),limits)
                require (admission=Error[RequestOwnerUnavailable;RequestRefused HostDisposed]) (sprintf "callback reentry refusal differs: %A" admission)
                require (host.Value.Projection=stateBefore) "refused callback reentry preserves retired state"
                require (mechanics=mechanicsBefore && results.Length=resultCountBefore) "refused callback reentry emits no effects"
                reentered <- true
        let transport:HostTransport =
            { Now=(fun () -> 2L)
              CreateWorker=(fun _ _ -> ())
              PostCommand=(fun _ command -> record ("post:"+command.Kind); observedPost (UInt64.Parse command.Identity.Request))
              TerminateWorker=(fun _ -> record "terminate")
              ArmTimer=(fun key _ _ -> armed <- armed.Add key; record "arm")
              CancelTimer=(fun key -> armed <- armed.Remove key; record "cancel") }
        let interpret effects =
            for effect in effects do
                match effect with
                | Settle result -> settle result
                | TerminateWorker _ -> record "terminate"
                | PostToWorker(_,command) ->
                    record (match command.Operation with ProcessGuest _ -> "post:process" | _ -> "post:other")
                    observedPost command.Identity.Request
                | ArmTimer(timer,_) -> armed <- armed.Add (string timer); record "arm"
                | CancelTimer timer -> armed <- armed.Remove (string timer); record "cancel"
                | _ -> ()
        let value = if connected then Host.CreateConnected(selectedSettings,transport,settle) else Host.Create(selectedSettings,interpret)
        host <- Some (value |> unwrap)
        let h=host.Value
        let load=identity 1UL
        let initialize=identity 2UL
        h.Load(0L,load,[|0uy|])
        let version=if (Validation.descriptor (HostSettings.configuration selectedSettings)).Path=BarProtected then 1u else 0x10000u
        h.Observe(0L,load.WorkerInstance,AbiVersionObserved(load,correlation load,version))
        h.Initialize(0L,initialize,[|1uy|])
        h.Observe(0L,initialize.WorkerInstance,InvocationObserved(outcome Initialize initialize,correlation initialize))
        mechanics <- [];results <- []
        h.SubmitLimited(0L,LimitedInvokeAndRetire(reset,[|8uy|]),limits) |> unwrap
        // BAR retains its single-pending refusal; SC2 queues this follower.
        let sc2=(Validation.descriptor (HostSettings.configuration selectedSettings)).Path=Sc2ImportedStrict
        if sc2 && not completeFromPost then h.SubmitLimited(0L,LimitedInvokeAndRetire(follower,[|7uy|]),limits) |> unwrap
        if not completeFromPost then completeTerminal ()
        require completed "terminal completion delivered"
        require reentered "synchronous callback attempted reentry"
        require armed.IsEmpty "outer admission cannot arm a timer after nested retirement"
        let terminalResult=results |> List.find(fun result -> result.Identity=reset)
        if adapterRejected then
            require (terminalResult.Outcome.IsNone && terminalResult.Reason=Some(AdapterRejected "adapter rejection")) "adapter first cause preserved without fabricated ABI"
        else require (terminalResult.Outcome=Some actual) "raw ABI evidence preserved"
        require ((mechanics |> List.filter(fun item -> item="post:process")).Length=1) "no precomputed following post survives"
        require ((mechanics |> List.filter(fun item -> item="arm")).Length=1) "no timer armed after retirement"
        if sc2 && not completeFromPost then
            let followerIndex=results |> List.findIndex(fun result -> result.Identity=follower)
            let resetIndex=results |> List.findIndex(fun result -> result.Identity=reset)
            require (followerIndex<resetIndex) "queued terminal follower settles before reset"
        let before=results.Length
        h.Observe(2L,reset.WorkerInstance,InvocationObserved(actual,correlation reset))
        h.Dispose(2L)
        require (results.Length=before) "duplicates and repeated disposal settle nothing twice"

// Exercise callbacks occurring BEFORE a selected terminal batch reaches Post.
// No guest execution: both actual public facades use their maintained reducer.
let terminalReentrantAdmissionControls (selectedSettings:HostSettings) =
    let require condition message = if not condition then failwith ("terminal pre-post reentry: " + message)
    let unwrap = function Ok value -> value | Error issues -> failwithf "%A" issues
    let identity (request:uint64) : HostIdentity = { WorkerInstance="terminal-worker";Request=request;Generation=1UL }
    let correlation (id:HostIdentity) = OperationToken.create $"request-{id.Generation}-{id.Request}" |> unwrap
    let outcome (phase:InvocationPhase) (id:HostIdentity) : InvocationOutcome = { Identity=id;Phase=phase;State=Succeeded;Dispatch=Dispatched;CleanupFault=None;CopiedOutput=Array.empty;EffectEligibility=ProductAdapterMustDecide }
    let limits : RequestLimits = { MaximumDeadlineMilliseconds=5;MaximumOutputBytes=64;EnclosingDeadlineMilliseconds=None }
    for connected, mode in [ for connected in [false;true] do for mode in ["timer-expiry";"previous-dispose";"previous-replace";"previous-phase";"ordinary-ancestor"] do yield connected,mode ] do
        let mutable host:Host option=None
        let mutable now=0L
        let mutable fired=false
        let mutable terminalPosts=0
        let mutable terminalArms=0
        let mutable armed:Set<string>=Set.empty
        let mutable terminated=false
        let mutable results:BrowserResult list=[]
        let reset=identity 4UL
        let previous=identity 3UL
        let terminalKey (timer:TimerIdentity) = $"{timer.Identity.WorkerInstance}:{timer.Identity.Generation}:{timer.Identity.Request}:{OperationToken.value timer.Correlation}:{timer.Phase}"
        let terminalTimer : TimerIdentity = { Identity=reset;Correlation=correlation reset;Phase=AllocateDescriptor }
        let settle (result:BrowserResult) =
            results <- results @ [result]
            if result.Identity=previous && not fired then
                fired <- true
                match mode with
                | "previous-dispose" -> host.Value.Dispose(1L)
                | "previous-replace" -> host.Value.Load(1L,{WorkerInstance="replacement";Request=9UL;Generation=2UL},[|0uy|])
                | "previous-phase" -> host.Value.Observe(1L,reset.WorkerInstance,PhaseObserved(reset,correlation reset,AllocateInput))
                | "ordinary-ancestor" -> host.Value.SubmitLimited(1L,LimitedInvokeAndRetire(reset,[|2uy|]),limits) |> unwrap
                | _ -> ()
            if result.Identity=reset && mode="timer-expiry" then
                require terminated "timeout retires worker before callback"
                require armed.IsEmpty "timeout cancels all owned timers before callback"
        let observedPost request =
            if mode="ordinary-ancestor" then
                if request=previous.Request && not fired then
                    host.Value.Observe(1L,previous.WorkerInstance,InvocationObserved(outcome Process previous,correlation previous))
                elif request=reset.Request then
                    host.Value.Observe(2L,reset.WorkerInstance,InvocationObserved(outcome Process reset,correlation reset))
        let arm key callback =
            armed <- armed.Add key
            if key.Contains(":4:") then
                terminalArms <- terminalArms+1
                if mode="timer-expiry" && not fired then
                    fired <- true
                    now <- 6L
                    callback ()
        let transport:HostTransport =
            { Now=(fun () -> now)
              CreateWorker=(fun _ _ -> ())
              PostCommand=(fun _ command ->
                  let request=UInt64.Parse command.Identity.Request
                  if request=reset.Request then terminalPosts <- terminalPosts+1
                  observedPost request)
              TerminateWorker=(fun _ -> terminated <- true)
              ArmTimer=(fun key _ callback -> arm key callback)
              CancelTimer=(fun key -> armed <- armed.Remove key) }
        let interpret effects =
            for effect in effects do
                match effect with
                | Settle result -> settle result
                | TerminateWorker _ -> terminated <- true
                | ArmTimer(timer,_) -> arm (terminalKey timer) (fun () -> host.Value.ObserveTimer(now,timer))
                | CancelTimer timer -> armed <- armed.Remove (terminalKey timer)
                | PostToWorker(_,command) ->
                    if command.Identity=reset then terminalPosts <- terminalPosts+1
                    observedPost command.Identity.Request
                | _ -> ()
        host <- Some((if connected then Host.CreateConnected(selectedSettings,transport,settle) else Host.Create(selectedSettings,interpret)) |> unwrap)
        let h=host.Value
        let load=identity 1UL
        let initialize=identity 2UL
        h.Load(0L,load,[|0uy|])
        h.Observe(0L,load.WorkerInstance,AbiVersionObserved(load,correlation load,0x10000u))
        h.Initialize(0L,initialize,[|1uy|])
        h.Observe(0L,initialize.WorkerInstance,InvocationObserved(outcome Initialize initialize,correlation initialize))
        results <- [];terminated <- false
        if mode<>"timer-expiry" then
            h.SubmitLimited(0L,LimitedInvoke(previous,Ordinary,[|1uy|]),{limits with MaximumDeadlineMilliseconds=100}) |> unwrap
        if mode<>"ordinary-ancestor" then
            h.SubmitLimited(0L,LimitedInvokeAndRetire(reset,[|2uy|]),limits) |> unwrap
            if mode<>"timer-expiry" then h.Observe(1L,previous.WorkerInstance,InvocationObserved(outcome Process previous,correlation previous))
        require fired "intended synchronous callback occurred"
        if mode="ordinary-ancestor" then
            require (terminalPosts=1 && terminalArms=1) "nested terminal dispatch occurs once"
            require h.Projection.Disposed "nested terminal retires whole session"
            require armed.IsEmpty "ordinary ancestor cannot arm its old timer after nested terminal retirement"
            require ((results |> List.filter(fun result -> result.Identity=reset)).Length=1) "nested terminal settles exactly once"
        elif mode="previous-phase" then
            require (terminalPosts=1 && terminalArms=1) "current terminal posts once with only its new phase timer"
            require (armed=Set.singleton(terminalKey {terminalTimer with Phase=AllocateInput})) "old precomputed phase timer cannot be armed"
        else
            require (terminalPosts=0) "retired/replaced terminal must never reach PostCommand"
            require (terminalArms=(if mode="timer-expiry" then 1 else 0)) "earlier callback cannot arm stale terminal timer"
            let terminalResults=results |> List.filter(fun result -> result.Identity=reset)
            require (terminalResults.Length=1) "terminal settles exactly once through nested callback"
            if mode="timer-expiry" then
                require (terminalResults.Head.Outcome |> Option.exists(fun value -> value.State=TimedOut && value.Dispatch=NotDispatched)) "pre-post timeout keeps undispatched original evidence"
            else require terminalResults.Head.Outcome.IsNone "canceled terminal invents no ABI outcome"
        h.Dispose(now)
