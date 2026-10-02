module Wasm.Lifecycle.Correspondence.Program

open Wasm.Lifecycle.Correspondence.Correspondence
open Wasm.Lifecycle.Correspondence.GeneratedTraces

[<EntryPoint>]
let main _ =
    let failures = traces |> List.collect replay
    for name in ["expired-to-disposed"; "drop-termination"; "reorder-expiry"; "drop-current-cancel"; "late-completion-current"; "queued-dispatch-after-expiry"; "wrong-terminal-correlation"] do
        if not (traces |> List.exists(fun trace -> eventBoundaryMutationsAreDetected trace |> List.exists(fun (control,detected) -> control = name && detected))) then failwithf "event-boundary causal mutant accepted: %s" name

    for name in ["expired-cause-to-disposed"; "not-dispatched-to-dispatched"; "invalidation-outcome-dropped"; "queued-own-expiry-invents-outcome"; "terminal-phase-normalized-process"] do
        if not (traces |> List.exists(fun trace -> rawTerminalMutationsAreDetected trace |> List.exists(fun (control,detected) -> control = name && detected))) then failwithf "raw terminal causal mutant accepted: %s" name

    if
        not (
            traces
            |> List.exists (fun trace -> trace.Name = "sc2" && staleGenerationMutationIsDetected trace)
        )
    then
        failwith "stale-generation negative mutation was not detected"

    if
        not (
            traces
            |> List.exists (fun trace -> trace.Name = "bar" && commandPayloadMutationIsDetected trace)
        )
    then
        failwith "dropped artifact negative mutation was not detected"

    if
        not (
            traces
            |> List.exists (fun trace -> trace.Name = "bar" && shutdownOperationMutationIsDetected trace)
        )
    then
        failwith "wrong shutdown export operation negative mutation was not detected"

    if
        not (
            traces
            |> List.exists (fun trace -> trace.Name = "phase" && phaseDeadlineMutationIsDetected trace)
        )
    then
        failwith "wrong phase deadline negative mutation was not detected"

    match failures with
    | [] ->
        printfn
            "wasm-lifecycle-correspondence: traces=%d state=full effects=ordered negative=stale-generation,dropped-artifact,shutdown-operation,phase-deadline-detected"
            traces.Length

        0
    | values ->
        values |> List.iter (eprintfn "%s")
        1
