open System
open System.IO
open System.Text.Json
open FS.GG.Game.SkillStaging

[<EntryPoint>]
let main argv =
    if argv.Length <> 2 && argv.Length <> 5 then
        eprintfn "usage: SkillStaging.SourceParity.Observer <repo-root> <manifest-path> [uid gid umask-decimal]"
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
            if argv.Length = 2 then
                JsonSerializer.Serialize({| accepted = true; files = files |})
                |> printfn "%s"
                0
            else
                match UInt32.TryParse argv.[2], UInt32.TryParse argv.[3], UInt32.TryParse argv.[4] with
                | (true, uid), (true, gid), (true, umask) ->
                    let owner: ReceiverContract.Owner = { UserId = uid; GroupId = gid }
                    match ReceiverContract.expected plan owner umask with
                    | Error issue ->
                        eprintfn "invalid receiver contract: %A" issue
                        2
                    | Ok contract ->
                        let metadata =
                            contract.Entries
                            |> List.map (fun entry ->
                                {| path = entry.Path
                                   kind = if entry.Kind = ReceiverContract.Directory then "directory" else "file"
                                   mode = entry.Mode
                                   userId = entry.UserId
                                   groupId = entry.GroupId |})
                        JsonSerializer.Serialize({| accepted = true; files = files; metadata = metadata |})
                        |> printfn "%s"
                        0
                | _ ->
                    eprintfn "uid, gid, and umask must be unsigned decimal integers"
                    2
