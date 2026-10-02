module Wasm.Lifecycle.Correspondence.Program

open Wasm.Lifecycle.Correspondence.Correspondence
open Wasm.Lifecycle.Correspondence.GeneratedTraces

[<EntryPoint>]
let main _ =
    let failures = traces |> List.collect replay

    if
        not (
            traces
            |> List.exists (fun trace -> trace.Name = "sc2" && staleGenerationMutationIsDetected trace)
        )
    then
        failwith "stale-generation negative mutation was not detected"

    match failures with
    | [] ->
        printfn
            "wasm-lifecycle-correspondence: traces=%d state=full effects=ordered negative=stale-generation-detected"
            traces.Length

        0
    | values ->
        values |> List.iter (eprintfn "%s")
        1
