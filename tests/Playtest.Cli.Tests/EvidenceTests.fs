module Playtest.Cli.Tests.EvidenceTests

open System.Text
open Expecto
open FS.GG.Playtest
open FS.GG.Playtest.Manifest
open FS.GG.Playtest.Proofs

// A small gameplay-FR manifest (GP-001..GP-004) for the mapping tests.
let private manifest: GameplayFr list =
    [ { Id = "GP-001"; Facet = "gameplay"; Summary = "replay"; CoversAc = [ 13 ] }
      { Id = "GP-002"; Facet = "gameplay"; Summary = "keymap"; CoversAc = [ 2; 3 ] }
      { Id = "GP-003"; Facet = "gameplay"; Summary = "bounce"; CoversAc = [ 4 ] }
      { Id = "GP-004"; Facet = "gameplay"; Summary = "deflect"; CoversAc = [ 5 ] } ]

// A minimal TRX with the given (testName, outcome) results and pass/fail counts.
let private trxXml (results: (string * string) list) (passed: int) (failed: int) : string =
    let body =
        results
        |> List.map (fun (name, outcome) -> sprintf "    <UnitTestResult testName=\"%s\" outcome=\"%s\" />" name outcome)
        |> String.concat "\n"

    sprintf
        "<?xml version=\"1.0\"?>\n<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">\n  <ResultSummary><Counters passed=\"%d\" failed=\"%d\" /></ResultSummary>\n  <Results>\n%s\n  </Results>\n</TestRun>"
        passed
        failed
        body

let private parseTrx (xml: string) =
    match Trx.parse xml (Encoding.UTF8.GetBytes xml) with
    | Ok run -> run
    | Error e -> failtestf "TRX must parse, got %s" e

// A passing TRX naming a proof test per GP.
let private allGreenTrx =
    trxXml
        [ "ReferenceProof.gate GP-001 replay", "Passed"
          "ReferenceProof.gate GP-002 keymap", "Passed"
          "ReferenceProof.gate GP-003 bounce", "Passed"
          "ReferenceProof.gate GP-004 deflect", "Passed" ]
        4
        0

let private allInputDriven =
    manifest |> List.map (fun fr -> fr.Id, InputDriven) |> Map.ofList

[<Tests>]
let tests =
    testList
        "PlaytestEvidence"
        [ testCase "FR-001 Trx.parse extracts counts, passed test names, and a digest"
          <| fun _ ->
              let run = parseTrx allGreenTrx
              Expect.equal run.Passed 4 "passed count"
              Expect.equal run.Failed 0 "failed count"
              Expect.equal (List.length run.PassedTestNames) 4 "four passed test names"
              Expect.equal (String.length run.Digest) 64 "sha256 hex digest is 64 chars"

          testCase "FR-002/003/007 an InputDriven proof with a passing test emits pass & not-synthetic"
          <| fun _ ->
              let rows = Evidence.rows (parseTrx allGreenTrx) allInputDriven manifest
              Expect.equal (List.length rows) 4 "one row per GP"
              Expect.isTrue (rows |> List.forall (fun r -> r.Result = "pass" && not r.Synthetic)) "every row satisfies"
              let gp2 = rows |> List.find (fun r -> r.RequirementRefs = [ "GP-002" ])
              Expect.equal gp2.CoversAc [ 2; 3 ] "the covered ACs are carried"

          testCase "FR-003/007 a synthetic proof is emitted synthetic:true and does not satisfy"
          <| fun _ ->
              let proofs = allInputDriven |> Map.add "GP-004" Synthetic
              let rows = Evidence.rows (parseTrx allGreenTrx) proofs manifest
              let gp4 = rows |> List.find (fun r -> r.RequirementRefs = [ "GP-004" ])
              Expect.equal gp4.Result "pass" "a synthetic proof's test may pass"
              Expect.isTrue gp4.Synthetic "but it is disclosed synthetic"
              let satisfying = rows |> List.filter (fun r -> r.Result = "pass" && not r.Synthetic)
              Expect.equal (List.length satisfying) 3 "the synthetic row does not satisfy"

          testCase "FR-004 a missing proof and a failing test are emitted not-satisfying (fail closed)"
          <| fun _ ->
              // GP-003 has no proof entry; GP-004's test failed.
              let proofs = [ "GP-001", InputDriven; "GP-002", InputDriven; "GP-004", InputDriven ] |> Map.ofList
              let trx =
                  trxXml
                      [ "gate GP-001 replay", "Passed"
                        "gate GP-002 keymap", "Passed"
                        "gate GP-004 deflect", "Failed" ]
                      2
                      1
              let rows = Evidence.rows (parseTrx trx) proofs manifest
              let resultOf id = (rows |> List.find (fun r -> r.RequirementRefs = [ id ])).Result
              Expect.equal (resultOf "GP-003") "missing" "no proof entry -> missing"
              Expect.equal (resultOf "GP-004") "fail" "a matching test that failed -> fail"
              let satisfying = rows |> List.filter (fun r -> r.Result = "pass" && not r.Synthetic) |> List.map (fun r -> r.RequirementRefs)
              Expect.equal satisfying [ [ "GP-001" ]; [ "GP-002" ] ] "only the green InputDriven GPs satisfy"

          testCase "FR-005 the emitted document is valid evidence.yml grammar with the observedRun receipt"
          <| fun _ ->
              let run = parseTrx allGreenTrx
              let rendered = Evidence.render "artifacts/testresults/playtest.trx" run (Evidence.rows run allInputDriven manifest)
              Expect.stringContains rendered "schemaVersion: 1" "carries a schemaVersion"
              Expect.stringContains rendered "evidence:" "carries the evidence list"
              Expect.stringContains rendered "type: requirement" "requirement subject"
              Expect.stringContains rendered "requirementRefs: [GP-001]" "requirement refs"
              Expect.stringContains rendered (sprintf "digest: \"sha256:%s\"" run.Digest) "the TRX digest receipt"
              Expect.stringContains rendered "result: pass" "a satisfying result"

          testCase "FR-005 a synthetic row renders synthetic: true"
          <| fun _ ->
              let proofs = allInputDriven |> Map.add "GP-004" Synthetic
              let run = parseTrx allGreenTrx
              let rendered = Evidence.render "t.trx" run (Evidence.rows run proofs manifest)
              Expect.stringContains rendered "synthetic: true" "the synthetic disposition is emitted"

          testCase "FR-006 a malformed TRX is an error"
          <| fun _ ->
              match Trx.parse "<not-a-trx>" (Encoding.UTF8.GetBytes "<not-a-trx>") with
              | Error _ -> ()
              | Ok _ -> failtest "a TRX with no <Counters> must error"

          testCase "FR-003 GP id is matched as a whole token, not a substring (GP-1 vs GP-10)"
          <| fun _ ->
              // A manifest with unpadded ids and a TRX where only GP-10 passed. GP-1 must NOT be
              // classified green off the 'GP-10' substring.
              let mf =
                  [ { Id = "GP-1"; Facet = "gameplay"; Summary = "a"; CoversAc = [ 1 ] }
                    { Id = "GP-10"; Facet = "gameplay"; Summary = "b"; CoversAc = [ 10 ] } ]
              let proofs = [ "GP-1", InputDriven; "GP-10", InputDriven ] |> Map.ofList
              let trx = trxXml [ "gate GP-10 moves", "Passed" ] 1 0
              let rows = Evidence.rows (parseTrx trx) proofs mf
              let resultOf id = (rows |> List.find (fun r -> r.RequirementRefs = [ id ])).Result
              Expect.equal (resultOf "GP-10") "pass" "GP-10 has a passing test"
              Expect.notEqual (resultOf "GP-1") "pass" "GP-1 must not borrow GP-10's passing test"

          testCase "FR-004 an all-errored TRX (failed=0, error>0) is not read as passed"
          <| fun _ ->
              let trx =
                  "<?xml version=\"1.0\"?>\n<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">\n  <ResultSummary><Counters passed=\"0\" failed=\"0\" error=\"3\" /></ResultSummary>\n  <Results></Results>\n</TestRun>"
              let run = parseTrx trx
              Expect.isGreaterThan run.Failed 0 "error-count runs must not report zero failures" ]
