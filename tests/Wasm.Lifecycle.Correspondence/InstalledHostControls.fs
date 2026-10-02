// Test-only facade seams over exactly installed public0.2.0, no producer runtime link.
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
let private blank state kind worker =
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

let replayFacade (connected:bool) (trace:ModelTrace) =
    let profile=if trace.Steps.Head.State.Profile="Bar" then BarProtected else Sc2ImportedStrict
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
        (if connected then Host.CreateConnected(settings profile,transport,fun result ->
            delivered <- delivered @ [result]
            append {record "settle" result.Identity.WorkerInstance with Request=result.Identity.Request;Generation=result.Identity.Generation;Disposition=string result.Disposition})
         else Host.Create(settings profile,fun effects -> emitted <- emitted @ effects))
        |> Result.defaultWith(fun issues -> failwithf "%A" issues)
    capturedHost <- Some host
    let mutable failures=[]
    for (previous,expected) in List.pairwise trace.Steps do
        emitted <- [];delivered <- [];mechanical <- [];terminations <- [];now <- expected.Input.Observed
        host.Dispatch(eventFor previous expected)
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
                let reason=match result.Reason with Some DeadlineExpired -> "DeadlineExpired" | Some HostDisposed -> "HostDisposed" | Some (WorkerFault diagnostic) -> "WorkerFault:"+diagnostic | _ -> ""
                let outcome=result.Outcome |> Option.map(fun value -> match value.State with TimedOut -> "TimedOut" | Faulted _ -> "Faulted" | _ -> "unexpected") |> Option.defaultValue ""
                let phase=result.Outcome |> Option.map(fun value -> rawPhase value.Phase) |> Option.defaultValue ""
                let dispatched=result.Outcome |> Option.exists(fun value -> value.Dispatch=Dispatched)
                if reason<>terminal.Reason || outcome<>terminal.Outcome || phase<>terminal.Phase || dispatched<>terminal.Dispatched then failures <- $"{trace.Name} raw facade cause/outcome/phase/dispatch differs" :: failures
    List.rev failures
