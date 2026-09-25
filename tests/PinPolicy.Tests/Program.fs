open FS.GG.Game.PinPolicy

let ui =
    [ "FS.GG.UI.Scene"; "FS.GG.UI.KeyboardInput"; "FS.GG.UI.Canvas"
      "FS.GG.UI.Controls.Elmish"; "FS.GG.UI.SkiaViewer"
      "FS.GG.UI.Symbology"; "FS.GG.UI.Template" ]
let audio = [ "FS.GG.Audio.Core"; "FS.GG.Audio.Host" ]
let rows = (ui |> List.map (fun name -> name, "0.10.0")) @ (audio |> List.map (fun name -> name, "0.2.0"))
let pin (name, version) = sprintf "<PackageVersion Include=\"%s\" Version=\"%s\" />" name version
let xml lines = "<Project><ItemGroup>" + String.concat "" lines + "</ItemGroup></Project>"
let baseXml = rows |> List.map pin |> xml
let check (name: string) (expected: string option) (subject: string) =
    let actual = PinPolicy.evaluate subject
    match expected, actual with
    | None, Ok () -> printfn "ok %s" name
    | Some fragment, Error errors when errors |> List.exists (fun error -> error.Contains fragment) -> printfn "ok %s" name
    | _ -> failwithf "%s: expected %A, got %A" name expected actual

check "complete roster" None baseXml
check "reordered attribute" None (baseXml.Replace("Include=\"FS.GG.UI.Scene\" Version=\"0.10.0\"", "Version=\"0.10.0\" Include=\"FS.GG.UI.Scene\""))
check "missing template" (Some "missing pin FS.GG.UI.Template") (rows |> List.filter (fun (name, _) -> name <> "FS.GG.UI.Template") |> List.map pin |> xml)
check "missing audio host" (Some "missing pin FS.GG.Audio.Host") (rows |> List.filter (fun (name, _) -> name <> "FS.GG.Audio.Host") |> List.map pin |> xml)
check "duplicate scene" (Some "duplicate pin") (xml ((rows |> List.map pin) @ [ pin ("FS.GG.UI.Scene", "0.10.0") ]))
check "case collision" (Some "case collision") (xml ((rows |> List.map pin) @ [ pin ("fs.gg.ui.scene", "0.10.0") ]))
check "commented template" (Some "missing pin FS.GG.UI.Template") (xml ((rows |> List.map (fun row -> if fst row = "FS.GG.UI.Template" then "<!-- " + pin row + " -->" else pin row))))
check "wrong UI version" (Some "incoherent versions") (xml (rows |> List.map (fun row -> if fst row = "FS.GG.UI.Scene" then pin (fst row, "0.9.2") else pin row)))
check "extra UI identity" (Some "unexpected pin") (xml ((rows |> List.map pin) @ [ pin ("FS.GG.UI.Unknown", "0.10.0") ]))
check "conditional template" (Some "conditional pin") (baseXml.Replace("Include=\"FS.GG.UI.Template\"", "Include=\"FS.GG.UI.Template\" Condition=\"false\""))
check "conditional group" (Some "conditional pin") (baseXml.Replace("<ItemGroup>", "<ItemGroup Condition=\"false\">"))
check "missing version" (Some "missing version") (baseXml.Replace("Include=\"FS.GG.Audio.Host\" Version=\"0.2.0\"", "Include=\"FS.GG.Audio.Host\""))
check "malformed XML" (Some "malformed XML") "<Project><ItemGroup>"
printfn "13 pin-policy controls passed"
