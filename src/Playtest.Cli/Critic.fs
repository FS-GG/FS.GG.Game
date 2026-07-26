module FS.GG.Playtest.Critic

open System
open FS.GG.Playtest.Manifest

type Disposition =
    | Supported
    | Unsupported
    | Ambiguous

type Row =
    { Ac: int
      Disposition: Disposition
      Checkpoints: string
      Terminal: string
      Route: string
      Reason: string }

let private field prefix (value: string) =
    if value.StartsWith(prefix + "=", StringComparison.Ordinal) then
        Some(value.Substring(prefix.Length + 1).Trim())
    else
        None

let private parseLine (line: string) =
    match line.Split('|') |> Array.map (fun part -> part.Trim()) with
    | [| ac; disposition; checkpoints; terminal; route; reason |] ->
        let acNumber =
            if ac.StartsWith("AC-", StringComparison.Ordinal) then
                match Int32.TryParse(ac.Substring(3)) with
                | true, value -> Some value
                | _ -> None
            else
                None
        let parsedDisposition =
            match disposition.ToLowerInvariant() with
            | "supported" -> Some Supported
            | "unsupported" -> Some Unsupported
            | "ambiguous" -> Some Ambiguous
            | _ -> None

        match acNumber, parsedDisposition, field "checkpoints" checkpoints, field "terminal" terminal, field "route" route, field "reason" reason with
        | Some acValue, Some dispositionValue, Some checkpointsValue, Some terminalValue, Some routeValue, Some reasonValue
            when not (String.IsNullOrWhiteSpace routeValue) && not (String.IsNullOrWhiteSpace reasonValue) ->
            Ok
                { Ac = acValue
                  Disposition = dispositionValue
                  Checkpoints = checkpointsValue
                  Terminal = terminalValue
                  Route = routeValue
                  Reason = reasonValue }
        | _ -> Error(sprintf "malformed critic row: %s" line)
    | _ -> Error(sprintf "malformed critic row: %s" line)

let parse (text: string) : Result<Map<int, Row>, string> =
    let lines =
        text.Replace("\r\n", "\n").Split('\n')
        |> Array.map (fun line -> line.Trim())
        |> Array.filter (fun line -> line <> "" && not (line.StartsWith("#", StringComparison.Ordinal)))

    lines
    |> Array.fold
        (fun state line ->
            match state, parseLine line with
            | Error error, _ -> Error error
            | _, Error error -> Error error
            | Ok rows, Ok row when Map.containsKey row.Ac rows ->
                Error(sprintf "duplicate critic row for AC-%03d" row.Ac)
            | Ok rows, Ok row -> Ok(Map.add row.Ac row rows))
        (Ok Map.empty)

let validate (manifest: GameplayFr list) (rows: Map<int, Row>) : Result<unit, string> =
    let expected =
        manifest
        |> List.filter (fun requirement -> requirement.RequiredEvidence = EvidenceLevel.ProductionJourney)
        |> List.collect (fun requirement -> requirement.CoversAc)
        |> Set.ofList

    let actual = rows |> Map.keys |> Set.ofSeq
    let missing = Set.difference expected actual |> Set.toList
    let extra = Set.difference actual expected |> Set.toList

    if not missing.IsEmpty then
        Error(sprintf "critic assessment is missing required AC row(s): %A" missing)
    elif not extra.IsEmpty then
        Error(sprintf "critic assessment has mismatched AC row(s): %A" extra)
    else
        let vetoes =
            rows
            |> Map.values
            |> Seq.filter (fun row -> row.Disposition <> Supported)
            |> Seq.map (fun row -> sprintf "AC-%03d=%A" row.Ac row.Disposition)
            |> Seq.toList

        if vetoes.IsEmpty then Ok()
        else Error(sprintf "critic veto: %s" (String.concat ", " vetoes))
