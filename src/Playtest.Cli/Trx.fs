/// Reading a VS TeamTest TRX: the run's pass/fail/skip counts, the names of the passed tests (so a GP
/// can be matched to its proof test), and the file digest for the evidence `observedRun` receipt.
module FS.GG.Playtest.Trx

open System
open System.Security.Cryptography
open System.Text
open System.Xml.Linq

/// The observed run extracted from a TRX.
type TrxRun =
    { Passed: int
      Failed: int
      Skipped: int
      PassedTestNames: string list
      /// Every test name in the run, regardless of outcome (to tell a failing test from an absent one).
      AllTestNames: string list
      /// Lowercase sha256 hex of the TRX file bytes.
      Digest: string }

let private ns = XNamespace.Get "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"

/// The lowercase sha256 hex digest of the TRX bytes.
let digest (bytes: byte[]) : string =
    use sha = SHA256.Create()
    sha.ComputeHash bytes |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""

let private attr (name: string) (e: XElement) : string option =
    match e.Attribute(XName.Get name) with
    | null -> None
    | a -> Some a.Value

let private intAttr (name: string) (e: XElement) : int =
    match attr name e with
    | Some v ->
        match Int32.TryParse v with
        | true, n -> n
        | _ -> 0
    | None -> 0

/// Parse a TRX string. `Error` on malformed XML or a missing `<Counters>` element (fail closed).
let parse (xml: string) (bytes: byte[]) : Result<TrxRun, string> =
    try
        let doc = XDocument.Parse xml

        match doc.Descendants(ns + "Counters") |> Seq.tryHead with
        | None -> Error "TRX has no <Counters> element"
        | Some counters ->
            let results = doc.Descendants(ns + "UnitTestResult") |> List.ofSeq

            let passedNames =
                results
                |> List.choose (fun r ->
                    match attr "outcome" r, attr "testName" r with
                    | Some "Passed", Some name -> Some name
                    | _ -> None)

            let allNames = results |> List.choose (attr "testName")

            Ok
                { Passed = intAttr "passed" counters
                  Failed = intAttr "failed" counters
                  Skipped = (intAttr "notExecuted" counters) + (intAttr "inconclusive" counters)
                  PassedTestNames = passedNames
                  AllTestNames = allNames
                  Digest = digest bytes }
    with ex ->
        Error(sprintf "malformed TRX: %s" ex.Message)
