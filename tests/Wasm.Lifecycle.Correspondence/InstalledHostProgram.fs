module Wasm.Lifecycle.Correspondence.InstalledHostProgram
open Wasm.Lifecycle.Correspondence.GeneratedTraces
open Wasm.Lifecycle.Correspondence.InstalledHostControls
[<EntryPoint>]
let main _ =
    let failures = traces |> List.collect(fun trace -> replayFacade false trace @ (if trace.Name.StartsWith("boundary-") then replayFacade true trace else []))
    if failures.IsEmpty then
        printfn "installed-host: traces=%d facades=Create(all),CreateConnected(boundaries) state=full effects=ordered callbacks=raw-qualified no-guest-execution" traces.Length
        0
    else
        failures |> List.iter(eprintfn "%s")
        1
