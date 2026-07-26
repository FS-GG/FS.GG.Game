/// Versioned serialization of validated production-journey receipts, bound to the exact observed
/// test report which made the proof eligible for evidence.
module FS.GG.Playtest.JourneyReceiptExport

open System
open System.Security.Cryptography
open System.Text
open FS.GG.Game.Core
open FS.GG.Game.Harness
open FS.GG.Playtest.Proofs
open FS.GG.Playtest.Trx

/// Schema v1 binds runner-issued journey facts to one exact passing test and its report bytes.
type BoundReceipt =
    { Receipt: JourneyReceipt
      ExecutionId: string
      ReceiptBindingDigest: string
      TestName: string
      ReportSource: string
      ReportDigest: string
      ReportPassed: int }

type GeneratedReport =
    { Bytes: byte[]
      Receipts: Map<string, BoundReceipt> }

let private digestParts (values: string list) =
    let builder = StringBuilder()

    for value in values do
        builder.Append(Encoding.UTF8.GetByteCount value).Append(':').Append(value) |> ignore

    builder.ToString()
    |> Encoding.UTF8.GetBytes
    |> SHA256.HashData
    |> Convert.ToHexString
    |> fun value -> value.ToLowerInvariant()

let private receiptBindingDigest executionId receipt =
    let origin =
        match JourneyReceipt.origin receipt with
        | Origin.ProductionJourney -> "production-journey"
        | Origin.InputDriven -> "input-driven"
        | Origin.Synthetic -> "synthetic"
    let inputKind =
        match JourneyReceipt.inputKind receipt with
        | JourneyInputKind.FixedScript -> "fixed-script"
        | JourneyInputKind.SeededPolicy -> "seeded-policy"
    let outcome =
        match JourneyReceipt.result receipt with
        | JourneyResult.Passed -> "passed"
        | JourneyResult.Failed reason -> "failed:" + reason

    digestParts
        [ executionId
          string (JourneyReceipt.schemaVersion receipt)
          JourneyReceipt.runnerIdentity receipt
          JourneyReceipt.runnerVersion receipt
          JourneyReceipt.compositionAuthority receipt
          origin
          JourneyReceipt.routeId receipt
          JourneyReceipt.scenarioId receipt
          JourneyReceipt.testId receipt
          inputKind
          JourneyReceipt.inputIdentity receipt
          JourneyReceipt.inputDigest receipt
          JourneyReceipt.scriptDigest receipt
          JourneyReceipt.traceDigest receipt
          JourneyReceipt.initialFingerprintDigest receipt
          JourneyReceipt.terminalFingerprintDigest receipt
          JourneyReceipt.terminalPredicateIdentity receipt
          (JourneyReceipt.terminalPredicateReached receipt).ToString().ToLowerInvariant()
          outcome
          string (JourneyReceipt.maxSteps receipt)
          string (JourneyReceipt.steps receipt) ]

let private xmlEscape (value: string) =
    System.Security.SecurityElement.Escape value

/// Generate a JUnit report and its receipt bindings from the same in-memory proof executions. The
/// report is output, never input: a stale or fabricated caller report therefore cannot be upgraded
/// into production provenance.
let generate
    (reportSource: string)
    (proofs: Map<string, ValidatedJourneyProof>)
    : GeneratedReport =
    let properties, cases =
        proofs
        |> Map.toList
        |> List.map (fun (testId, proof) ->
            let binding = receiptBindingDigest proof.ExecutionId proof.Receipt

            [ sprintf
                  "    <property name=\"fsgg.%s.executionId\" value=\"%s\" />"
                  (xmlEscape testId)
                  (xmlEscape proof.ExecutionId)
              sprintf
                  "    <property name=\"fsgg.%s.receiptBinding\" value=\"sha256:%s\" />"
                  (xmlEscape testId)
                  binding ],
            sprintf
                "  <testcase classname=\"%s\" name=\"%s\" />"
                (xmlEscape (JourneyReceipt.routeId proof.Receipt))
                (xmlEscape testId))
        |> List.unzip

    let reportText =
        String.concat
            "\n"
            ([ "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
               sprintf
                   "<testsuite name=\"fsgg-playtest.production-journeys\" tests=\"%d\" failures=\"0\">"
                   proofs.Count
               "  <properties>" ]
             @ List.concat properties
             @ [ "  </properties>" ]
             @ cases
             @ [ "</testsuite>"; "" ])
    let bytes = Encoding.UTF8.GetBytes reportText
    let reportDigest = Trx.digest bytes
    let receipts =
        proofs
        |> Map.map (fun testId proof ->
            { Receipt = proof.Receipt
              ExecutionId = proof.ExecutionId
              ReceiptBindingDigest = receiptBindingDigest proof.ExecutionId proof.Receipt
              TestName = testId
              ReportSource = reportSource
              ReportDigest = reportDigest
              ReportPassed = proofs.Count })

    { Bytes = bytes
      Receipts = receipts }

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
      indent + "compositionAuthority: " + yamlString (JourneyReceipt.compositionAuthority receipt)
      indent
      + "origin: "
      + (match JourneyReceipt.origin receipt with
         | Origin.ProductionJourney -> "production-journey"
         | Origin.InputDriven -> "input-driven"
         | Origin.Synthetic -> "synthetic")
      indent + "routeId: " + yamlString (JourneyReceipt.routeId receipt)
      indent + "scenarioId: " + yamlString (JourneyReceipt.scenarioId receipt)
      indent + "testId: " + yamlString (JourneyReceipt.testId receipt)
      indent + "executionId: " + yamlString bound.ExecutionId
      indent + "receiptBinding: " + digest bound.ReceiptBindingDigest
      indent + "input:"
      indent + "  kind: " + inputKind
      indent + "  identity: " + yamlString (JourneyReceipt.inputIdentity receipt)
      indent + "  digest: " + digest (JourneyReceipt.inputDigest receipt)
      indent + "replayDigest: " + digest (JourneyReceipt.scriptDigest receipt)
      indent + "traceDigest: " + digest (JourneyReceipt.traceDigest receipt)
      indent + "initialFingerprint: " + digest (JourneyReceipt.initialFingerprintDigest receipt)
      indent + "terminalFingerprint: " + digest (JourneyReceipt.terminalFingerprintDigest receipt)
      indent + "terminalPredicate:"
      indent + "  identity: " + yamlString (JourneyReceipt.terminalPredicateIdentity receipt)
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
