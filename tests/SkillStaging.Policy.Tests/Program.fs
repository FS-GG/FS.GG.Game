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
    let plan = Policy.prepare manifest [ row ] [ source ]
    match plan with
    | Ok value ->
        check "manifest bytes retained" manifest value.ManifestBytes
        check "skill bytes retained" body value.Skills.Head.Bytes
        check "destination derived" "skills/fs-gg-ai/SKILL.md" value.Skills.Head.Destination
    | Error error -> failwithf "valid plan failed: %A" error
    check "empty product set" (Error Policy.EmptyProductSet) (Policy.prepare manifest [] [ source ])
    check "unrelated scope skipped" plan (Policy.prepare manifest [ { row with Scope = "template" }; row ] [ source ])
    check "duplicate id" (Error(Policy.DuplicateId row.Id)) (Policy.prepare manifest [ row; row ] [ source ])
    check "traversal id" (Error(Policy.InvalidId "../other")) (Policy.prepare manifest [ { row with Id = "../other" } ] [ source ])
    check "wrong source root" (Error(Policy.InvalidSourcePath "template/product-skills/other/")) (Policy.prepare manifest [ { row with SuppliedBy = "template/product-skills/other/" } ] [ source ])
    check "missing source" (Error(Policy.MissingSource source.RelativePath)) (Policy.prepare manifest [ row ] [])
    check "symlink source" (Error(Policy.SymlinkSource source.RelativePath)) (Policy.prepare manifest [ row ] [ { source with IsSymlink = true } ])
    check "directory source" (Error(Policy.NonRegularSource source.RelativePath)) (Policy.prepare manifest [ row ] [ { source with IsRegularFile = false } ])
    check "wrong digest" (Error(Policy.DigestMismatch row.Id)) (Policy.prepare manifest [ row ] [ { source with Bytes = bytes "different" } ])
    check "invalid digest" (Error(Policy.InvalidDigest row.Id)) (Policy.prepare manifest [ { row with Sha256 = "ABC" } ] [ source ])
    check "invalid source UTF-8" (Error(Policy.InvalidUtf8 source.RelativePath)) (Policy.prepare manifest [ row ] [ { source with Bytes = [|0xffuy|] } ])
    check "duplicate source facts" (Error(Policy.InvalidSourcePath source.RelativePath)) (Policy.prepare manifest [ row ] [ source; source ])
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
