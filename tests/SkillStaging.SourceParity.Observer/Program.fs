open System
open System.IO
open System.Text.Json
open FS.GG.Game.SkillStaging

[<EntryPoint>]
let main argv =
    if argv.Length <> 2 then
        eprintfn "usage: SkillStaging.SourceParity.Observer <repo-root> <manifest-path>"
        2
    else
        let manifestBytes = File.ReadAllBytes argv.[1]
        match ReadOnlySource.capture argv.[0] manifestBytes with
        | Error issue ->
            JsonSerializer.Serialize({| accepted = false; reason = sprintf "%A" issue |})
            |> printfn "%s"
            0
        | Ok plan ->
            let files =
                ("skill-manifest.json", plan.ManifestBytes)
                :: (plan.Skills |> List.map (fun skill -> skill.Destination, skill.Bytes))
                |> List.sortBy fst
                |> List.map (fun (path, bytes) ->
                    {| path = path; bytesBase64 = Convert.ToBase64String bytes |})
            JsonSerializer.Serialize({| accepted = true; files = files |})
            |> printfn "%s"
            0
