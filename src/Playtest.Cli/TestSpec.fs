/// Parsing a TestSpec's §14 acceptance criteria and scaffolding a gameplay-FR manifest from them.
module FS.GG.Playtest.TestSpec

open System.Text.RegularExpressions
open FS.GG.Playtest.Manifest

/// Extract `(number, title)` for each numbered §14 acceptance criterion. The corpus writes each AC as
/// `N. **Title.**` at column 0 under the `## 14. Acceptance …` heading, ending at the next `## `.
let parseSection14 (text: string) : (int * string) list =
    let lines = text.Replace("\r\n", "\n").Split('\n')

    match lines |> Array.tryFindIndex (fun l -> Regex.IsMatch(l, @"^##\s+14\.\s+Acceptance")) with
    | None -> []
    | Some s ->
        let endIdx =
            lines.[s + 1 ..]
            |> Array.tryFindIndex (fun l -> Regex.IsMatch(l, @"^##\s"))
            |> Option.map (fun i -> s + 1 + i)
            |> Option.defaultValue lines.Length

        lines.[s + 1 .. endIdx - 1]
        |> Array.toList
        |> List.choose (fun l ->
            let m = Regex.Match(l, @"^(\d+)\.\s+\*\*(.+?)\*\*")

            if m.Success then
                let n = int m.Groups.[1].Value
                let title = m.Groups.[2].Value.TrimEnd('.').Trim()
                Some(n, title)
            else
                None)

/// Scaffold one `GP-###` gameplay-FR stub per acceptance criterion: `CoversAc = [n]`, the `gameplay`
/// facet, and the AC title as the summary.
let scaffold (acs: (int * string) list) : GameplayFr list =
    acs
    |> List.map (fun (n, title) ->
        { Id = sprintf "GP-%03d" n
          Facet = "gameplay"
          Summary = title
          CoversAc = [ n ] })
