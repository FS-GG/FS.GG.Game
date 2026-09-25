open System
open System.IO
open System.Text
open System.Text.Json
open FS.GG.Game.SkillStaging

let bytes (text: string) = Encoding.UTF8.GetBytes text
let body = bytes "# skill\n"
let digest = match Policy.canonicalDigest body with Ok value -> value | Error () -> failwith "invalid fixture"
let row: Policy.ManifestRow = { Id = "fs-gg-ai"; Scope = "product"; SuppliedBy = "template/product-skills/fs-gg-ai/"; Sha256 = digest }
let source: Policy.Source = { RelativePath = "template/product-skills/fs-gg-ai/SKILL.md"; Bytes = body; IsRegularFile = true; IsSymlink = false }
let manifest = bytes "{\"skills\":[]}\n"
let manifestFor (rows: Policy.ManifestRow list) =
    let skills =
        rows
        |> List.map (fun item ->
            dict [ "id", box item.Id; "scope", box item.Scope;
                   "supplied-by", box item.SuppliedBy; "sha256", box item.Sha256;
                   "files", box [ dict [ "path", box "SKILL.md"; "sha256", box item.Sha256 ] ] ])
    JsonSerializer.SerializeToUtf8Bytes(dict [ "schemaVersion", box 2; "skills", box skills ])
let prepare rows sources = Policy.prepare (manifestFor rows) rows sources
let check name expected actual =
    if expected <> actual then failwithf "%s: expected %A, got %A" name expected actual
    printfn "PASS %s" name

let run () =
    check "LF digest" (Ok digest) (Policy.canonicalDigest body)
    check "CRLF digest" (Ok digest) (Policy.canonicalDigest (bytes "# skill\r\n"))
    check "BOM and CRLF digest" (Ok digest) (Policy.canonicalDigest (Array.concat [ [|0xefuy;0xbbuy;0xbfuy|]; bytes "# skill\r\n" ]))
    check "lone CR differs" true (Policy.canonicalDigest (bytes "# skill\r") <> Ok digest)
    check "invalid UTF-8" (Error ()) (Policy.canonicalDigest [|0xffuy|])
    check "missing byte buffer" (Error ()) (Policy.canonicalDigest Unchecked.defaultof<byte[]>)
    match Policy.prepare manifest [ row ] [ source ] with
    | Ok _ -> failwith "manifest with zero skills authorized a staged product row"
    | Error _ -> printfn "PASS manifest bytes bind the selected rows"
    check "unsupported manifest schema" (Error Policy.InvalidManifest) (Policy.prepare manifest [ row ] [ source ])
    check "extra caller row refuses" (Error Policy.ManifestRowsMismatch) (Policy.prepare (manifestFor []) [ row ] [ source ])
    check "missing caller row refuses" (Error Policy.ManifestRowsMismatch) (Policy.prepare (manifestFor [ row ]) [] [ source ])
    let wrongFile =
        (manifestFor [ row ] |> Encoding.UTF8.GetString).Replace("\"SKILL.md\"", "\"../escape.md\"")
        |> bytes
    check "escaping manifest file refuses" (Error Policy.InvalidManifest) (Policy.prepare wrongFile [ row ] [ source ])
    let plan = prepare [ row ] [ source ]
    match plan with
    | Ok value ->
        check "manifest bytes retained" (manifestFor [ row ]) value.ManifestBytes
        check "skill bytes retained" body value.Skills.Head.Bytes
        check "destination derived" "skills/fs-gg-ai/SKILL.md" value.Skills.Head.Destination
    | Error error -> failwithf "valid plan failed: %A" error
    let manifestInput = manifestFor [ row ]
    let bodyInput = Array.copy body
    let snapshot =
        match Policy.prepare manifestInput [ row ] [ { source with Bytes = bodyInput } ] with
        | Ok value -> value
        | Error refusal -> failwithf "snapshot fixture refused: %A" refusal
    let originalManifest = Array.copy manifestInput
    manifestInput.[0] <- 0uy
    bodyInput.[0] <- byte 'X'
    check "manifest input mutation cannot alter plan" originalManifest snapshot.ManifestBytes
    check "source input mutation cannot alter plan" body snapshot.Skills.Head.Bytes
    let manifestGetter = snapshot.ManifestBytes
    let bodyGetter = snapshot.Skills.Head.Bytes
    manifestGetter.[0] <- 0uy
    bodyGetter.[0] <- byte 'X'
    check "plan getters return byte copies" true (snapshot.ManifestBytes = originalManifest && snapshot.Skills.Head.Bytes = body)
    check "empty product set" (Error Policy.EmptyProductSet) (prepare [] [ source ])
    let withOtherScope = prepare [ { row with Scope = "template" }; row ] [ source ]
    match withOtherScope with
    | Ok value -> check "unrelated scope skipped" [ row.Id ] (value.Skills |> List.map _.Id)
    | Error refusal -> failwithf "unrelated scope refused: %A" refusal
    check "duplicate id" (Error(Policy.DuplicateId row.Id)) (prepare [ row; row ] [ source ])
    check "traversal id" (Error(Policy.InvalidId "../other")) (prepare [ { row with Id = "../other" } ] [ source ])
    check "wrong source root" (Error(Policy.InvalidSourcePath "template/product-skills/other/")) (prepare [ { row with SuppliedBy = "template/product-skills/other/" } ] [ source ])
    check "missing source" (Error(Policy.MissingSource source.RelativePath)) (prepare [ row ] [])
    check "symlink source" (Error(Policy.SymlinkSource source.RelativePath)) (prepare [ row ] [ { source with IsSymlink = true } ])
    check "directory source" (Error(Policy.NonRegularSource source.RelativePath)) (prepare [ row ] [ { source with IsRegularFile = false } ])
    check "wrong digest" (Error(Policy.DigestMismatch row.Id)) (prepare [ row ] [ { source with Bytes = bytes "different" } ])
    check "invalid digest" (Error(Policy.InvalidDigest row.Id)) (prepare [ { row with Sha256 = "ABC" } ] [ source ])
    check "invalid source UTF-8" (Error(Policy.InvalidUtf8 source.RelativePath)) (prepare [ row ] [ { source with Bytes = [|0xffuy|] } ])
    check "duplicate source facts" (Error(Policy.InvalidSourcePath source.RelativePath)) (prepare [ row ] [ source; source ])
    let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
    let realManifest = File.ReadAllBytes(Path.Combine(repoRoot, "template/skill-manifest/skill-manifest.json"))
    use document = JsonDocument.Parse realManifest
    let realRows: Policy.ManifestRow list =
        document.RootElement.GetProperty("skills").EnumerateArray()
        |> Seq.map (fun item ->
            ({ Id = item.GetProperty("id").ToString();
              Scope = item.GetProperty("scope").ToString();
              SuppliedBy = item.GetProperty("supplied-by").ToString();
              Sha256 = item.GetProperty("sha256").ToString() }: Policy.ManifestRow))
        |> Seq.toList
    let realSources: Policy.Source list =
        realRows
        |> List.filter (fun item -> item.Scope = "product")
        |> List.map (fun item ->
            let relative = item.SuppliedBy + "SKILL.md"
            let path = Path.Combine(repoRoot, relative)
            let attributes = File.GetAttributes path
            { RelativePath = relative
              Bytes = File.ReadAllBytes path
              IsRegularFile = File.Exists path
              IsSymlink = attributes.HasFlag FileAttributes.ReparsePoint })
    match Policy.prepare realManifest realRows realSources with
    | Error refusal -> failwithf "real catalog rejected: %A" refusal
    | Ok value -> check "real authored product catalog" 17 value.Skills.Length
    0

[<EntryPoint>]
let main _ = run ()
