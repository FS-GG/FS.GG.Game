/// The fsgg-playtest CLI entry point: dispatch and I/O at the edge; parsing/coverage stay pure. Every
/// subcommand fails closed — an unreadable/malformed input, or any uncovered cited AC, exits non-zero.
module FS.GG.Playtest.Program

open System.IO
open FS.GG.Playtest

type private ProofInputs =
    { Provenance: Map<string, Proofs.Provenance>
      Journeys: Map<string, Proofs.ValidatedJourneyProof> }

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

let private writeBytes (path: string) (bytes: byte[]) : Result<unit, string> =
    try
        File.WriteAllBytes(path, bytes)
        Ok()
    with ex ->
        Error(sprintf "cannot write %s: %s" path ex.Message)

let private canonicalPath (path: string) =
    let full = Path.GetFullPath path
    let root = Path.GetPathRoot full |> Option.ofObj |> Option.defaultValue ""
    let relative = full.Substring(root.Length)
    let components =
        relative.Split(
            [| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |],
            System.StringSplitOptions.RemoveEmptyEntries
        )

    components
    |> Array.fold
        (fun current segment ->
            let candidate = Path.Combine(current, segment)
            let fileInfo = FileInfo(candidate)

            match fileInfo.LinkTarget |> Option.ofObj with
            | Some linkTarget ->
                if Path.IsPathRooted linkTarget then
                    Path.GetFullPath linkTarget
                else
                    let parent =
                        fileInfo.DirectoryName
                        |> Option.ofObj
                        |> Option.defaultValue root
                    Path.Combine(parent, linkTarget) |> Path.GetFullPath
            | None when Directory.Exists candidate || File.Exists candidate ->
                let info =
                    if Directory.Exists candidate then
                        DirectoryInfo(candidate) :> FileSystemInfo
                    else
                        FileInfo(candidate) :> FileSystemInfo

                match info.ResolveLinkTarget(true) with
                | null -> candidate
                | target -> target.FullName
            | None -> candidate)
        root

let private samePath left right =
    try
        let comparison =
            if System.OperatingSystem.IsWindows() then
                System.StringComparison.OrdinalIgnoreCase
            else
                System.StringComparison.Ordinal

        Ok(System.String.Equals(canonicalPath left, canonicalPath right, comparison))
    with ex ->
        Error(sprintf "cannot resolve output paths: %s" ex.Message)

/// The value after `--name`, if present.
let private flag (name: string) (argv: string[]) : string option =
    argv
    |> Array.tryFindIndex (fun a -> a = name)
    |> Option.bind (fun i -> if i + 1 < argv.Length then Some argv.[i + 1] else None)

let private parseProofInputs (argv: string[]) (manifest: Manifest.GameplayFr list) (proofText: string) =
    match Proofs.parse proofText with
    | Error e -> Error e
    | Ok simulation ->
        let productionRequired =
            manifest
            |> List.exists (fun requirement ->
                requirement.RequiredEvidence = Manifest.EvidenceLevel.ProductionJourney)

        match
            flag "--journey-proof-assembly" argv,
            flag "--journey-authority-assembly" argv,
            flag "--critic" argv
        with
        | None, None, None when not productionRequired ->
            Ok
                { Provenance = simulation
                  Journeys = Map.empty }
        | Some assemblyPath, Some authorityAssemblyPath, Some criticPath ->
            match readFile criticPath with
            | Error e -> Error e
            | Ok criticText ->
                match
                    Proofs.loadJourneyReceiptsWithAuthority assemblyPath authorityAssemblyPath,
                    Critic.parse criticText
                with
                | Error e, _
                | _, Error e -> Error e
                | Ok journeys, Ok criticRows ->
                    match Critic.validate manifest criticRows with
                    | Error e -> Error e
                    | Ok () ->
                        let provenance =
                            journeys
                            |> Map.fold
                                (fun combined key value -> Map.add key value.Provenance combined)
                                simulation

                        Ok
                            { Provenance = provenance
                              Journeys = journeys }
        | _ when productionRequired ->
            Error(
                "production-journey coverage requires --journey-proof-assembly, "
                + "--journey-authority-assembly, and --critic"
            )
        | _ ->
            Error(
                "--journey-proof-assembly, --journey-authority-assembly, and --critic "
                + "must be supplied together"
            )

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
                match parseProofInputs argv manifest pText with
                | Error e ->
                    eprintfn "coverage-lint: %s" e
                    1
                | Ok inputs ->
                    let specAcs =
                        match flag "--spec" argv with
                        | Some s ->
                            match readFile s with
                            | Ok t -> Some(TestSpec.parseSection14 t |> List.map fst)
                            | Error _ -> None
                        | None -> None

                    let report = Coverage.lint manifest inputs.Provenance specAcs

                    printfn
                        "cited ACs: %d; covered: %d; uncovered: %d"
                        (List.length report.CitedAcs)
                        (List.length report.CoveredAcs)
                        (List.length report.UncoveredAcs)

                    if not (List.isEmpty report.SpecGap) then
                        printfn "advisory: spec §14 ACs not cited by any GP (completeness gap): %A" report.SpecGap

                    if Coverage.passed report then
                        printfn "coverage-lint: PASS — every cited AC has its required evidence level"
                        0
                    else
                        eprintfn "coverage-lint: FAIL — cited AC(s) without their required evidence level: %A" report.UncoveredAcs
                        1
    | _ ->
        eprintfn "coverage-lint: --manifest <m> and --proofs <p> required; production rows also require --journey-proof-assembly <dll> --journey-authority-assembly <producer.dll> --critic <assessment>"
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
                match parseProofInputs argv manifest pText, Trx.parse tText tBytes with
                | Error e, _ ->
                    eprintfn "emit-evidence: %s" e
                    1
                | _, Error e ->
                    eprintfn "emit-evidence: %s" e
                    1
                | Ok inputs, Ok run ->
                    let journeyReport =
                        if Map.isEmpty inputs.Journeys then
                            Ok Map.empty
                        else
                            match flag "--journey-report-out" argv with
                            | None ->
                                Error(
                                    "production-journey evidence requires --journey-report-out <junit.xml>; "
                                    + "the same-execution report is generated output, never caller input"
                                )
                            | Some reportPath ->
                                match flag "--out" argv with
                                | Some evidencePath ->
                                    match samePath reportPath evidencePath with
                                    | Error error -> Error error
                                    | Ok true ->
                                        Error(
                                            "--journey-report-out and --out must resolve to different files"
                                        )
                                    | Ok false ->
                                        let generated = JourneyReceiptExport.generate reportPath inputs.Journeys

                                        match writeBytes reportPath generated.Bytes with
                                        | Error error -> Error error
                                        | Ok() -> Ok generated.Receipts
                                | _ ->
                                    let generated = JourneyReceiptExport.generate reportPath inputs.Journeys

                                    match writeBytes reportPath generated.Bytes with
                                    | Error error -> Error error
                                    | Ok() -> Ok generated.Receipts

                    match journeyReport with
                    | Error error ->
                        eprintfn "emit-evidence: %s" error
                        1
                    | Ok journeys ->
                        let rows =
                            Evidence.rowsWithJourneyReceipts
                                run
                                journeys
                                inputs.Provenance
                                manifest
                        let rendered = Evidence.renderWithJourneyReceipts tPath run journeys rows

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
        eprintfn "emit-evidence: --manifest <m>, --proofs <p>, and --trx <t> required; production rows also require --journey-proof-assembly <dll> --journey-authority-assembly <producer.dll> --critic <assessment> --journey-report-out <junit.xml>"
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
