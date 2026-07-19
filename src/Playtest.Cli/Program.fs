/// The fsgg-playtest CLI entry point: dispatch and I/O at the edge; parsing/coverage stay pure. Every
/// subcommand fails closed — an unreadable/malformed input, or any uncovered cited AC, exits non-zero.
module FS.GG.Playtest.Program

open System.IO
open FS.GG.Playtest

let private readFile (path: string) : Result<string, string> =
    try
        Ok(File.ReadAllText path)
    with ex ->
        Error(sprintf "cannot read %s: %s" path ex.Message)

let private readBytes (path: string) : Result<byte[], string> =
    try
        Ok(File.ReadAllBytes path)
    with ex ->
        Error(sprintf "cannot read %s: %s" path ex.Message)

/// The value after `--name`, if present.
let private flag (name: string) (argv: string[]) : string option =
    argv
    |> Array.tryFindIndex (fun a -> a = name)
    |> Option.bind (fun i -> if i + 1 < argv.Length then Some argv.[i + 1] else None)

let private scaffoldManifest (argv: string[]) : int =
    match flag "--spec" argv with
    | None ->
        eprintfn "scaffold-manifest: --spec <testspec.md> required"
        2
    | Some spec ->
        match readFile spec with
        | Error e ->
            eprintfn "%s" e
            1
        | Ok text ->
            match TestSpec.parseSection14 text with
            | [] ->
                eprintfn "scaffold-manifest: no section-14 acceptance criteria found in %s" spec
                1
            | acs ->
                let frs = TestSpec.scaffold acs
                let rendered = Manifest.render frs

                match flag "--out" argv with
                | Some out ->
                    File.WriteAllText(out, rendered)
                    printfn "wrote %d GP stub(s) to %s" (List.length frs) out
                    0
                | None ->
                    printfn "%s" rendered
                    0

let private coverageLint (argv: string[]) : int =
    match flag "--manifest" argv, flag "--proofs" argv with
    | Some mPath, Some pPath ->
        match readFile mPath, readFile pPath with
        | Error e, _
        | _, Error e ->
            eprintfn "%s" e
            1
        | Ok mText, Ok pText ->
            match Manifest.tryParse mText with
            | Error e ->
                eprintfn "coverage-lint: %s" e
                1
            | Ok [] ->
                eprintfn "coverage-lint: manifest %s parsed no GP records" mPath
                1
            | Ok manifest ->
                match Proofs.parse pText with
                | Error e ->
                    eprintfn "coverage-lint: %s" e
                    1
                | Ok proofs ->
                    let specAcs =
                        match flag "--spec" argv with
                        | Some s ->
                            match readFile s with
                            | Ok t -> Some(TestSpec.parseSection14 t |> List.map fst)
                            | Error _ -> None
                        | None -> None

                    let report = Coverage.lint manifest proofs specAcs

                    printfn
                        "cited ACs: %d; covered: %d; uncovered: %d"
                        (List.length report.CitedAcs)
                        (List.length report.CoveredAcs)
                        (List.length report.UncoveredAcs)

                    if not (List.isEmpty report.SpecGap) then
                        printfn "advisory: spec §14 ACs not cited by any GP (completeness gap): %A" report.SpecGap

                    if Coverage.passed report then
                        printfn "coverage-lint: PASS — every cited AC has an InputDriven proof"
                        0
                    else
                        eprintfn "coverage-lint: FAIL — cited AC(s) without an InputDriven proof: %A" report.UncoveredAcs
                        1
    | _ ->
        eprintfn "coverage-lint: --manifest <m> and --proofs <p> required"
        2

let private emitEvidence (argv: string[]) : int =
    match flag "--manifest" argv, flag "--proofs" argv, flag "--trx" argv with
    | Some mPath, Some pPath, Some tPath ->
        match readFile mPath, readFile pPath, readFile tPath, readBytes tPath with
        | Error e, _, _, _
        | _, Error e, _, _
        | _, _, Error e, _
        | _, _, _, Error e ->
            eprintfn "%s" e
            1
        | Ok mText, Ok pText, Ok tText, Ok tBytes ->
            match Manifest.tryParse mText with
            | Error e ->
                eprintfn "emit-evidence: %s" e
                1
            | Ok [] ->
                eprintfn "emit-evidence: manifest %s parsed no GP records" mPath
                1
            | Ok manifest ->
                match Proofs.parse pText, Trx.parse tText tBytes with
                | Error e, _ ->
                    eprintfn "emit-evidence: %s" e
                    1
                | _, Error e ->
                    eprintfn "emit-evidence: %s" e
                    1
                | Ok proofs, Ok run ->
                    let rows = Evidence.rows run proofs manifest
                    let rendered = Evidence.render tPath run rows

                    match flag "--out" argv with
                    | Some out ->
                        File.WriteAllText(out, rendered)
                        let satisfying = rows |> List.filter (fun r -> r.Result = "pass" && not r.Synthetic) |> List.length
                        printfn "wrote %d evidence row(s) to %s (%d satisfying)" (List.length rows) out satisfying
                        0
                    | None ->
                        printf "%s" rendered
                        0
    | _ ->
        eprintfn "emit-evidence: --manifest <m>, --proofs <p>, and --trx <t> required"
        2

[<EntryPoint>]
let main argv =
    match Array.toList argv with
    | "scaffold-manifest" :: _ -> scaffoldManifest argv
    | "coverage-lint" :: _ -> coverageLint argv
    | "emit-evidence" :: _ -> emitEvidence argv
    | cmd :: _ ->
        eprintfn "unknown command '%s'; expected scaffold-manifest | coverage-lint | emit-evidence" cmd
        2
    | [] ->
        eprintfn "usage: fsgg-playtest <scaffold-manifest|coverage-lint|emit-evidence> [flags]"
        2
