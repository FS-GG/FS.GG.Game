/// The gameplay-FR manifest: the shape WI-7 hand-authored (Id, gameplay facet, CoversAc, Summary),
/// serialized as a stable, line-based, comment-tolerant format that round-trips.
module FS.GG.Playtest.Manifest

open System

type EvidenceLevel =
    | SimulationInput
    | ProductionJourney

/// One gameplay requirement of a game: an id, the `gameplay` classifier facet, a one-line summary, and
/// the TestSpec §14 acceptance-criteria numbers it covers.
type GameplayFr =
    { Id: string
      Facet: string
      RequiredEvidence: EvidenceLevel
      Summary: string
      CoversAc: int list }

/// Render `GP-### | gameplay | requires=<level> | covers=<csv> | <summary>` records.
let render (frs: GameplayFr list) : string =
    let header = "# fsgg-playtest gameplay-FR manifest"

    let line (fr: GameplayFr) =
        let covers = fr.CoversAc |> List.map string |> String.concat ","
        let required =
            match fr.RequiredEvidence with
            | SimulationInput -> "simulation-input"
            | ProductionJourney -> "production-journey"
        sprintf "%s | %s | requires=%s | covers=%s | %s" fr.Id fr.Facet required covers fr.Summary

    String.concat "\n" (header :: (frs |> List.map line))

let private parseCovers (covers: string) : int list =
    covers.Replace("covers=", "").Split(',')
    |> Array.toList
    |> List.choose (fun s ->
        match Int32.TryParse(s.Trim()) with
        | true, n -> Some n
        | _ -> None)

let private parseLine (trimmed: string) : GameplayFr option =
    match trimmed.Split([| '|' |], 5) |> Array.map (fun s -> s.Trim()) with
    | [| id; facet; required; covers; summary |] ->
        let level =
            match required.ToLowerInvariant() with
            | "requires=simulation-input" -> Some SimulationInput
            | "requires=production-journey" -> Some ProductionJourney
            | _ -> None
        level
        |> Option.map (fun evidence ->
            { Id = id
              Facet = facet
              RequiredEvidence = evidence
              Summary = summary
              CoversAc = parseCovers covers })
    | [| id; facet; covers; summary |] ->
        Some
            { Id = id
              Facet = facet
              RequiredEvidence = SimulationInput
              Summary = summary
              CoversAc = parseCovers covers }
    | _ -> None

/// Parse a manifest **fail-closed**: blank and `#`-comment lines are ignored, but any other line that
/// is not a well-formed gameplay requirement record is an `Error` (never a
/// silent drop — a dropped GP would make its cited ACs vanish and a broken manifest read as covered).
let tryParse (text: string) : Result<GameplayFr list, string> =
    let mutable error: string option = None

    let recs =
        text.Replace("\r\n", "\n").Split('\n')
        |> Array.toList
        |> List.choose (fun raw ->
            let trimmed = raw.Trim()

            if trimmed = "" || trimmed.StartsWith("#") then
                None
            else
                match parseLine trimmed with
                | Some fr -> Some fr
                | None ->
                    error <- Some(sprintf "malformed manifest line: %s" trimmed)
                    None)

    match error with
    | Some e -> Error e
    | None -> Ok recs

/// Parse a manifest leniently (blank/`#`-comment and malformed lines skipped), for round-tripping
/// tool-emitted manifests. The CLI edge uses `tryParse` instead, which fails closed on a malformed line.
let parse (text: string) : GameplayFr list =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.choose (fun raw ->
        let trimmed = raw.Trim()
        if trimmed = "" || trimmed.StartsWith("#") then None else parseLine trimmed)
