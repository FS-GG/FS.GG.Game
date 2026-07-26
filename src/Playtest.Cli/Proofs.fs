/// Simulation/synthetic proof declarations plus validated production-journey receipts.
module FS.GG.Playtest.Proofs

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// The provenance of a gameplay-FR's proof. The manifest decides whether simulation input is enough
/// or a validated production journey is required.
type Provenance =
    | InputDriven
    | ProductionJourney
    | Synthetic
    | Missing

/// Parse a provenance token (case-insensitive), or `None` when unrecognized.
let parseProvenance (s: string) : Provenance option =
    match s.Trim().ToLowerInvariant() with
    | "inputdriven" -> Some InputDriven
    // ProductionJourney is intentionally absent: only a validated runner receipt can provide it.
    | "synthetic" -> Some Synthetic
    | "missing" -> Some Missing
    | _ -> None

/// Parse a proof report (`GP-### <provenance>` per line; `#`/blank lines ignored). Returns `Error` with
/// a message on a malformed line or an unknown provenance token — fail closed, never a silent skip.
let parse (text: string) : Result<Map<string, Provenance>, string> =
    let mutable error: string option = None

    let entries =
        text.Replace("\r\n", "\n").Split('\n')
        |> Array.toList
        |> List.choose (fun raw ->
            let line = raw.Trim()

            if line = "" || line.StartsWith("#") then
                None
            else
                match line.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries) with
                | [| gp; prov |] ->
                    match parseProvenance prov with
                    | Some p -> Some(gp, p)
                    | None ->
                        error <- Some(sprintf "unknown provenance '%s' for %s" prov gp)
                        None
                | _ ->
                    error <- Some(sprintf "malformed proof line: %s" line)
                    None)

    match error with
    | Some e -> Error e
    | None -> Ok(Map.ofList entries)

let private digest (value: string) =
    value
    |> Encoding.UTF8.GetBytes
    |> SHA256.HashData
    |> Convert.ToHexString
    |> fun s -> s.ToLowerInvariant()

/// Validate JSONL receipts emitted by `JourneyReceipt.render`.
let parseJourneyReceipts (text: string) : Result<Map<string, Provenance>, string> =
    let parseOne (line: string) =
        try
            use doc = JsonDocument.Parse line
            let root = doc.RootElement
            let str (name: string) =
                match root.GetProperty(name).GetString() with
                | null -> ""
                | value -> value
            let number (name: string) = root.GetProperty(name).GetInt32()
            let schema = number "schemaVersion"
            let kind = str "kind"
            let route = str "routeId"
            let scenario = str "scenarioId"
            let testId = str "testId"
            let scriptDigest = str "scriptDigest"
            let traceDigest = str "traceDigest"
            let result = str "result"
            let failure = str "failure"
            let steps = number "steps"
            let maxSteps = number "maxSteps"
            let integrity = str "integrity"
            let stripSha (value: string) =
                if value.StartsWith("sha256:", StringComparison.Ordinal) then value.Substring(7) else ""
            let payload =
                String.concat
                    "\n"
                    [ "production-journey-v1"
                      route
                      scenario
                      testId
                      stripSha scriptDigest
                      stripSha traceDigest
                      result
                      failure
                      string steps
                      string maxSteps ]
            let expected = digest payload

            if schema <> 1 || kind <> "production-journey" then
                Error "not a production-journey v1 receipt"
            elif String.IsNullOrWhiteSpace route || String.IsNullOrWhiteSpace scenario || String.IsNullOrWhiteSpace testId then
                Error "journey receipt has an empty route, scenario, or test identity"
            elif result <> "pass" || failure <> "" then
                Error(sprintf "journey receipt %s did not pass" testId)
            elif steps > maxSteps || steps < 0 || maxSteps <= 0 then
                Error(sprintf "journey receipt %s violates its declared step bound" testId)
            elif stripSha scriptDigest = "" || stripSha traceDigest = "" then
                Error(sprintf "journey receipt %s has a malformed script or trace digest" testId)
            elif stripSha integrity <> expected then
                Error(sprintf "journey receipt %s has a forged or stale integrity digest" testId)
            else
                Ok(testId, ProductionJourney)
        with ex ->
            Error(sprintf "malformed journey receipt: %s" ex.Message)

    let lines =
        text.Replace("\r\n", "\n").Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (String.IsNullOrWhiteSpace >> not)

    lines
    |> Array.fold
        (fun state line ->
            match state, parseOne line with
            | Ok entries, Ok entry -> Ok(entry :: entries)
            | Error e, _
            | _, Error e -> Error e)
        ((Ok []) : Result<(string * Provenance) list, string>)
    |> Result.map Map.ofList
