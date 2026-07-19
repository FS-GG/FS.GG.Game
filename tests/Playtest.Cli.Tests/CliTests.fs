module Playtest.Cli.Tests.CliTests

open System.IO
open Expecto
open FS.GG.Playtest
open FS.GG.Playtest.Manifest
open FS.GG.Playtest.Proofs

// The repo root, found by walking up from the test binary until FS.GG.Game.slnx is seen.
let private repoRoot =
    let rec up (d: DirectoryInfo | null) =
        match d with
        | null -> failwith "repo root (FS.GG.Game.slnx) not found"
        | dir ->
            if File.Exists(Path.Combine(dir.FullName, "FS.GG.Game.slnx")) then dir.FullName
            else up dir.Parent

    up (DirectoryInfo(System.AppContext.BaseDirectory))

let private specPath (name: string) = Path.Combine(repoRoot, "docs", "TestSpecs", "Games", name)

// A WI-7-style Pong manifest (the CoversAc mirror ReferenceProof.fs GP-001..GP-010).
let private wi7Manifest: GameplayFr list =
    [ { Id = "GP-001"; Facet = "gameplay"; Summary = "replay"; CoversAc = [ 13 ] }
      { Id = "GP-002"; Facet = "gameplay"; Summary = "keymap route"; CoversAc = [ 2; 3 ] }
      { Id = "GP-003"; Facet = "gameplay"; Summary = "paddle clamp"; CoversAc = [ 2 ] }
      { Id = "GP-004"; Facet = "gameplay"; Summary = "ball on field"; CoversAc = [ 4 ] }
      { Id = "GP-005"; Facet = "gameplay"; Summary = "wall bounce"; CoversAc = [ 4 ] }
      { Id = "GP-006"; Facet = "gameplay"; Summary = "deflection"; CoversAc = [ 5; 15 ] }
      { Id = "GP-007"; Facet = "gameplay"; Summary = "no double hit"; CoversAc = [ 8 ] }
      { Id = "GP-008"; Facet = "gameplay"; Summary = "scoring"; CoversAc = [ 9 ] }
      { Id = "GP-009"; Facet = "gameplay"; Summary = "serve"; CoversAc = [ 1; 16 ] }
      { Id = "GP-010"; Facet = "gameplay"; Summary = "match"; CoversAc = [ 10; 13 ] } ]

let private allInputDriven (frs: GameplayFr list) =
    frs |> List.map (fun fr -> fr.Id, InputDriven) |> Map.ofList

[<Tests>]
let tests =
    testList
        "PlaytestCli"
        [ testCase "FR-001 scaffold emits one GP stub per section-14 AC (snake=17, pong=19)"
          <| fun _ ->
              let count name =
                  specPath name |> File.ReadAllText |> TestSpec.parseSection14 |> TestSpec.scaffold |> List.length
              Expect.equal (count "snake.md") 17 "snake has 17 section-14 ACs"
              Expect.equal (count "pong.md") 19 "pong has 19 section-14 ACs"

          testCase "FR-001 a scaffolded stub carries the gameplay facet, its AC number, and the title"
          <| fun _ ->
              let frs = specPath "pong.md" |> File.ReadAllText |> TestSpec.parseSection14 |> TestSpec.scaffold
              let gp1 = frs |> List.find (fun fr -> fr.Id = "GP-001")
              Expect.equal gp1.Facet "gameplay" "the classifier facet"
              Expect.equal gp1.CoversAc [ 1 ] "covers its own AC number"
              Expect.stringContains gp1.Summary "Serve" "the summary is the AC title"

          testCase "FR-002 the manifest round-trips through render/parse"
          <| fun _ ->
              let parsed = wi7Manifest |> Manifest.render |> Manifest.parse
              Expect.equal parsed wi7Manifest "render then parse yields the same records"

          testCase "FR-003/007 coverage-lint reports the WI-7 manifest fully covered under InputDriven proofs"
          <| fun _ ->
              let report = Coverage.lint wi7Manifest (allInputDriven wi7Manifest) None
              Expect.isTrue (Coverage.passed report) "every cited AC has an InputDriven proof"
              Expect.isEmpty report.UncoveredAcs "no uncovered cited ACs"
              Expect.equal report.CitedAcs [ 1; 2; 3; 4; 5; 8; 9; 10; 13; 15; 16 ] "the cited AC set"

          testCase "FR-004/007 removing a proof leaves its uniquely-covered ACs uncovered (fail closed)"
          <| fun _ ->
              // Drop GP-006's proof: ACs 5 and 15 are covered only by it.
              let proofs = allInputDriven wi7Manifest |> Map.remove "GP-006"
              let report = Coverage.lint wi7Manifest proofs None
              Expect.isFalse (Coverage.passed report) "a missing proof fails the lint"
              Expect.equal report.UncoveredAcs [ 5; 15 ] "exactly the ACs only GP-006 covered are uncovered"

          testCase "FR-004 a synthetic proof does not cover"
          <| fun _ ->
              let proofs = allInputDriven wi7Manifest |> Map.add "GP-006" Synthetic
              let report = Coverage.lint wi7Manifest proofs None
              Expect.isFalse (Coverage.passed report) "synthetic never satisfies"
              Expect.equal report.UncoveredAcs [ 5; 15 ] "the synthetic-only ACs are uncovered"

          testCase "FR-005 --spec reports the completeness gap as advisory without failing"
          <| fun _ ->
              // The spec has 19 ACs; the WI-7 manifest cites 11 — the other 8 are the advisory gap.
              let specAcs = specPath "pong.md" |> File.ReadAllText |> TestSpec.parseSection14 |> List.map fst
              let report = Coverage.lint wi7Manifest (allInputDriven wi7Manifest) (Some specAcs)
              Expect.isTrue (Coverage.passed report) "the gap is advisory — it does not fail the lint"
              Expect.equal report.SpecGap [ 6; 7; 11; 12; 14; 17; 18; 19 ] "spec ACs no GP cites"

          testCase "FR-006 a malformed proof report is an error, not a silent skip"
          <| fun _ ->
              match Proofs.parse "GP-001 bogusprovenance" with
              | Error _ -> ()
              | Ok _ -> failtest "an unknown provenance token must be an error"
              match Proofs.parse "GP-001 inputDriven\nGP-002 synthetic" with
              | Ok m -> Expect.equal (Map.find "GP-002" m) Synthetic "a well-formed report parses"
              | Error e -> failtestf "a well-formed report must parse, got %s" e

          testCase "FR-006 a spec with no section-14 yields no ACs (an error at the CLI edge)"
          <| fun _ ->
              Expect.isEmpty (TestSpec.parseSection14 "# A doc with no section 14\n\nnothing here") "no ACs parsed"

          testCase "FR-002/006 tryParse fails closed on a malformed manifest line, parse stays lenient"
          <| fun _ ->
              let broken = "GP-001 | gameplay | covers=1 | ok\nthis line is broken\n"
              match Manifest.tryParse broken with
              | Error _ -> ()
              | Ok _ -> failtest "a malformed manifest line must be an error, not a silent drop"
              // The lenient parse keeps only the well-formed record (used for tool round-trip).
              Expect.equal (Manifest.parse broken |> List.length) 1 "lenient parse skips the broken line" ]
