/// Versioned serialization of validated production-journey receipts, bound to the exact observed
/// test report which made the proof eligible for evidence.
module FS.GG.Playtest.JourneyReceiptExport

open System
open System.Text.RegularExpressions
open FS.GG.Game.Core
open FS.GG.Game.Harness
open FS.GG.Playtest.Proofs
open FS.GG.Playtest.Trx

/// Schema v1 binds runner-issued journey facts to one exact passing test and its report bytes.
type BoundReceipt =
    { Receipt: JourneyReceipt
      TestName: string
      ReportSource: string
      ReportDigest: string }

let private mentions (testId: string) (name: string) =
    let pattern = "(?<![0-9A-Za-z])" + Regex.Escape testId + "(?![0-9A-Za-z])"
    Regex.IsMatch(name, pattern)

/// Bind every validated receipt to exactly one passing test in this report. Missing, failed, or
/// ambiguous identities fail closed; a report-level digest is always the digest of the supplied
/// bytes, so a stale or byte-divergent report produces a different binding.
let bind
    (reportSource: string)
    (run: TrxRun)
    (proofs: Map<string, ValidatedJourneyProof>)
    : Result<Map<string, BoundReceipt>, string> =
    proofs
    |> Map.fold
        (fun state testId proof ->
            match state with
            | Error error -> Error error
            | Ok bound ->
                let allMatches = run.AllTestNames |> List.filter (mentions testId)
                let passingMatches = run.PassedTestNames |> List.filter (mentions testId)

                match allMatches, passingMatches with
                | [], _ ->
                    Error(sprintf "production journey receipt %s has no matching test in the observed report" testId)
                | _, [] ->
                    Error(sprintf "production journey receipt %s is bound to a non-passing test" testId)
                | _, [ testName ] when allMatches = [ testName ] ->
                    Ok(
                        Map.add
                            testId
                            { Receipt = proof.Receipt
                              TestName = testName
                              ReportSource = reportSource
                              ReportDigest = run.Digest }
                            bound
                    )
                | _ ->
                    Error(sprintf "production journey receipt %s matches more than one observed test" testId))
        (Ok Map.empty)

let private yamlString (value: string) =
    "\""
    + value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n")
    + "\""

let private digest value = yamlString("sha256:" + value)

/// Render the schema-v1 map at an evidence-row indentation. Consumers must reject unknown versions
/// and any origin other than `production-journey`.
let renderYaml (indent: string) (bound: BoundReceipt) : string list =
    let receipt = bound.Receipt
    let inputKind =
        match JourneyReceipt.inputKind receipt with
        | JourneyInputKind.FixedScript -> "fixed-script"
        | JourneyInputKind.SeededPolicy -> "seeded-policy"

    let outcome, failure =
        match JourneyReceipt.result receipt with
        | JourneyResult.Passed -> "passed", None
        | JourneyResult.Failed reason -> "failed", Some reason

    [ indent + "schemaVersion: " + string (JourneyReceipt.schemaVersion receipt)
      indent + "runner:"
      indent + "  identity: " + yamlString (JourneyReceipt.runnerIdentity receipt)
      indent + "  version: " + yamlString (JourneyReceipt.runnerVersion receipt)
      indent
      + "origin: "
      + (match JourneyReceipt.origin receipt with
         | Origin.ProductionJourney -> "production-journey"
         | Origin.InputDriven -> "input-driven"
         | Origin.Synthetic -> "synthetic")
      indent + "routeId: " + yamlString (JourneyReceipt.routeId receipt)
      indent + "scenarioId: " + yamlString (JourneyReceipt.scenarioId receipt)
      indent + "testId: " + yamlString (JourneyReceipt.testId receipt)
      indent + "input:"
      indent + "  kind: " + inputKind
      indent + "  digest: " + digest (JourneyReceipt.inputDigest receipt)
      indent + "replayDigest: " + digest (JourneyReceipt.scriptDigest receipt)
      indent + "traceDigest: " + digest (JourneyReceipt.traceDigest receipt)
      indent + "initialFingerprint: " + digest (JourneyReceipt.initialFingerprintDigest receipt)
      indent + "terminalFingerprint: " + digest (JourneyReceipt.terminalFingerprintDigest receipt)
      indent + "terminalPredicate:"
      indent
      + "  reached: "
      + ((JourneyReceipt.terminalPredicateReached receipt).ToString().ToLowerInvariant())
      indent + "outcome: " + outcome
      yield!
          failure
          |> Option.map (fun reason -> [ indent + "failure: " + yamlString reason ])
          |> Option.defaultValue []
      indent + "maximumSteps: " + string (JourneyReceipt.maxSteps receipt)
      indent + "actualSteps: " + string (JourneyReceipt.steps receipt)
      indent + "observedTestReport:"
      indent + "  source: " + yamlString bound.ReportSource
      indent + "  digest: " + digest bound.ReportDigest
      indent + "  testName: " + yamlString bound.TestName
      indent + "  outcome: passed" ]
