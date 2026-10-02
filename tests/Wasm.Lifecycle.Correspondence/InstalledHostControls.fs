// Test-only facade seams over exactly installed public0.2.0, no producer runtime link.
module Wasm.Lifecycle.Correspondence.InstalledHostControls
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
open Wasm.Lifecycle.Correspondence.CorrespondenceTypes
open Wasm.Lifecycle.Correspondence.Correspondence

let replayFacade (connected: bool) (trace: ModelTrace) =
    let profile = if trace.Steps.Head.State.Profile = "Bar" then BarProtected else Sc2ImportedStrict
    let mutable emitted: HostEffect list = []
    let mutable delivered: BrowserResult list = []
    let mutable now = 0L
    let transport: HostTransport =
        { Now = (fun () -> now)
          CreateWorker = (fun _ _ -> ())
          PostCommand = (fun _ _ -> ())
          TerminateWorker = ignore
          ArmTimer = (fun _ _ _ -> ())
          CancelTimer = ignore }
    let host =
        (if connected then Host.CreateConnected(settings profile, transport, fun result -> delivered <- delivered @ [result])
         else Host.Create(settings profile, fun effects -> emitted <- emitted @ effects))
        |> Result.defaultWith (fun issues -> failwithf "%A" issues)
    let mutable failures = []
    for (previous,expected) in List.pairwise trace.Steps do
        emitted <- []; delivered <- []; now <- expected.Input.Observed
        host.Dispatch(eventFor previous expected)
        if host.Projection <> expected.State then failures <- $"{trace.Name} facade connected={connected} state differs original={expected.Input.Name}" :: failures
        if not connected && Lifecycle.projectEffects emitted <> expected.Effects then failures <- $"{trace.Name} ordered facade effects differ" :: failures
        let results = if connected then delivered else emitted |> List.choose(function Settle result -> Some result | _ -> None)
        let ids = results |> List.map(fun result -> result.Identity.Request)
        let expectedIds = expected.Effects |> List.filter(fun effect -> effect.Kind.StartsWith("settle") || effect.Kind.StartsWith("refused")) |> List.map _.Request
        if ids <> expectedIds then failures <- $"{trace.Name} ordered facade terminal identity differs" :: failures
        for terminal in expected.Terminals do
            match results |> List.tryFind(fun result -> result.Identity.Request = terminal.Request) with
            | None -> failures <- $"{trace.Name} terminal callback missing" :: failures
            | Some result ->
                let reason = match result.Reason with Some DeadlineExpired -> "DeadlineExpired" | Some HostDisposed -> "HostDisposed" | _ -> ""
                let state = result.Outcome |> Option.map(fun outcome -> if outcome.State = TimedOut then "TimedOut" else "unexpected") |> Option.defaultValue ""
                let dispatched = result.Outcome |> Option.exists(fun outcome -> outcome.Dispatch = Dispatched)
                if reason <> terminal.Reason || state <> terminal.Outcome || dispatched <> terminal.Dispatched then failures <- $"{trace.Name} raw facade cause/outcome/dispatch differs" :: failures
    List.rev failures
