module Wasm.Lifecycle.Correspondence.InstalledHostProgram
open Wasm.Lifecycle.Correspondence.GeneratedTraces
open Wasm.Lifecycle.Correspondence.InstalledHostControls
[<EntryPoint>]
let main _ =
    let failures = traces |> List.collect(fun trace -> replayFacade false trace @ replayFacade true trace)
    let regression=traces |> List.find(fun trace -> trace.Name="pending-load-commit")
    for name,detected in nestedCallbackMutationsAreDetected regression do
        if not detected then failwithf "nested callback causal mutant accepted: %s" name
    if failures.IsEmpty then
        printfn "installed-host: traces=%d facades=Create(all),CreateConnected(all) state=full effects=ordered callbacks=raw-qualified no-guest-execution" traces.Length
        0
    else
        failures |> List.iter(eprintfn "%s")
        1
