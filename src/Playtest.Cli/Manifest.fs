/// The gameplay-FR manifest: the shape WI-7 hand-authored (Id, gameplay facet, CoversAc, Summary),
/// serialized as a stable, line-based, comment-tolerant format that round-trips.
module FS.GG.Playtest.Manifest

open System

/// One gameplay requirement of a game: an id, the `gameplay` classifier facet, a one-line summary, and
/// the TestSpec §14 acceptance-criteria numbers it covers.
type GameplayFr =
    { Id: string
      Facet: string
      Summary: string
      CoversAc: int list }

/// Render a manifest as `GP-### | gameplay | covers=<csv> | <summary>` lines under a comment header.
let render (frs: GameplayFr list) : string =
    let header = "# fsgg-playtest gameplay-FR manifest"

    let line (fr: GameplayFr) =
        let covers = fr.CoversAc |> List.map string |> String.concat ","
        sprintf "%s | %s | covers=%s | %s" fr.Id fr.Facet covers fr.Summary

    String.concat "\n" (header :: (frs |> List.map line))

/// Parse a manifest, ignoring blank and `#`-comment lines. A well-formed line has four `|`-separated
/// fields; the summary may itself contain `|` (only the first three separators are significant).
let parse (text: string) : GameplayFr list =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.choose (fun raw ->
        let trimmed = raw.Trim()

        if trimmed = "" || trimmed.StartsWith("#") then
            None
        else
            let parts = trimmed.Split([| '|' |], 4) |> Array.map (fun s -> s.Trim())

            match parts with
            | [| id; facet; covers; summary |] ->
                let nums =
                    covers.Replace("covers=", "").Split(',')
                    |> Array.toList
                    |> List.choose (fun s ->
                        match Int32.TryParse(s.Trim()) with
                        | true, n -> Some n
                        | _ -> None)

                Some
                    { Id = id
                      Facet = facet
                      Summary = summary
                      CoversAc = nums }
            | _ -> None)
