open System
open System.IO
open System.Text
open FS.GG.Game.SkillStaging

let utf8 = UTF8Encoding(false, true)
let body = utf8.GetBytes "# audio\n"
let guide = utf8.GetBytes "guide\n"
let digest bytes =
    match Policy.canonicalDigest bytes with
    | Ok value -> value
    | Error () -> failwith "invalid fixture bytes"

let manifest =
    $"{{\"schemaVersion\":2,\"skills\":[{{\"id\":\"audio\",\"scope\":\"product\",\"supplied-by\":\"template/product-skills/audio/\",\"sha256\":\"{digest body}\",\"files\":[{{\"path\":\"SKILL.md\",\"sha256\":\"{digest body}\"}},{{\"path\":\"docs/guide.md\",\"sha256\":\"{digest guide}\"}}]}}]}}"
    |> utf8.GetBytes
let row: Policy.ManifestRow =
    { Id = "audio"; Scope = "product"; SuppliedBy = "template/product-skills/audio/"
      Sha256 = digest body
      Files = [ { Path = "SKILL.md"; Sha256 = digest body }
                { Path = "docs/guide.md"; Sha256 = digest guide } ] }
let source relative bytes: Policy.Source =
    { RelativePath = "template/product-skills/audio/" + relative
      Bytes = bytes; IsRegularFile = true; IsSymlink = false }
let plan =
    match Policy.prepare manifest [ row ] [ source "SKILL.md" body; source "docs/guide.md" guide ] with
    | Ok value -> value
    | Error issue -> failwithf "fixture plan refused: %A" issue

let owner: ReceiverContract.Owner = { UserId = 1001u; GroupId = 1002u }
let metadata path kind mode: ReceiverContract.Entry =
    { Path = path; Kind = kind; Mode = mode; UserId = owner.UserId; GroupId = owner.GroupId }
let baseline =
    [ metadata "." ReceiverContract.Directory 0o755u
      metadata "skill-manifest.json" ReceiverContract.RegularFile 0o644u
      metadata "skills" ReceiverContract.Directory 0o755u
      metadata "skills/audio" ReceiverContract.Directory 0o755u
      metadata "skills/audio/SKILL.md" ReceiverContract.RegularFile 0o644u
      metadata "skills/audio/docs" ReceiverContract.Directory 0o755u
      metadata "skills/audio/docs/guide.md" ReceiverContract.RegularFile 0o644u ]
let payload path bytes: ReceiverContract.Payload = { Path = path; Bytes = bytes }
let received =
    [ payload "skill-manifest.json" (Array.copy manifest)
      payload "skills/audio/SKILL.md" (Array.copy body)
      payload "skills/audio/docs/guide.md" (Array.copy guide) ]

let mutable count = 0
let check name expected actual =
    if expected <> actual then failwithf "%s: expected %A; got %A" name expected actual
    count <- count + 1
    printfn "ok %s" name

let change path replacement entries =
    entries |> List.map (fun (entry: ReceiverContract.Entry) ->
        if entry.Path = path then replacement entry else entry)

[<EntryPoint>]
let main _ =
    let contract =
        match ReceiverContract.expected plan owner 0o022u with
        | Ok value -> value
        | Error issue -> failwithf "valid umask refused: %A" issue
    check "independently authored exact roster, modes, and owner"
        (baseline |> List.sortBy _.Path) contract.Entries
    check "valid observed metadata" (Ok ()) (ReceiverContract.verify contract baseline)
    let corruptBody = utf8.GetBytes "# changed\n"
    let corruptReceived =
        received |> List.map (fun item ->
            if item.Path = "skills/audio/SKILL.md" then { item with Bytes = corruptBody } else item)
    // Red-before characterization: #655's metadata-only API accepts the same
    // entries even though the independently observed receiver bytes changed.
    check "metadata-only false green for changed bytes" (Ok ())
        (ReceiverContract.verify contract baseline)
    check "complete receiver parity" (Ok ())
        (ReceiverContract.verifyComplete contract baseline received)
    check "changed SKILL bytes refuse with metadata unchanged"
        (Error(ReceiverContract.WrongBytes "skills/audio/SKILL.md"))
        (ReceiverContract.verifyComplete contract baseline corruptReceived)
    check "changed manifest bytes refuse with metadata unchanged"
        (Error(ReceiverContract.WrongBytes "skill-manifest.json"))
        (ReceiverContract.verifyComplete contract baseline
            (received |> List.map (fun item ->
                if item.Path = "skill-manifest.json" then
                    { item with Bytes = utf8.GetBytes "changed" }
                else item)))
    check "missing payload refuses"
        (Error(ReceiverContract.MissingPayload "skills/audio/docs/guide.md"))
        (ReceiverContract.verifyComplete contract baseline
            (received |> List.filter (fun item -> item.Path <> "skills/audio/docs/guide.md")))
    check "extra payload refuses"
        (Error(ReceiverContract.UnexpectedPayload "obsolete.txt"))
        (ReceiverContract.verifyComplete contract baseline
            (payload "obsolete.txt" body :: received))
    check "duplicate payload refuses"
        (Error(ReceiverContract.DuplicateObservedPayload "skill-manifest.json"))
        (ReceiverContract.verifyComplete contract baseline
            (payload "skill-manifest.json" manifest :: received))
    check "null payload refuses"
        (Error(ReceiverContract.WrongBytes "skills/audio/SKILL.md"))
        (ReceiverContract.verifyComplete contract baseline
            (received |> List.map (fun item ->
                if item.Path = "skills/audio/SKILL.md" then
                    { item with Bytes = Unchecked.defaultof<byte[]> }
                else item)))
    check "metadata mismatch still refuses in complete comparison"
        (Error(ReceiverContract.WrongMode "skills/audio/SKILL.md"))
        (ReceiverContract.verifyComplete contract
            (change "skills/audio/SKILL.md" (fun entry -> { entry with Mode = 0o600u }) baseline)
            received)
    check "wrong file mode refuses"
        (Error(ReceiverContract.WrongMode "skills/audio/SKILL.md"))
        (ReceiverContract.verify contract
            (change "skills/audio/SKILL.md" (fun entry -> { entry with Mode = 0o600u }) baseline))
    check "wrong directory mode refuses"
        (Error(ReceiverContract.WrongMode "skills/audio/docs"))
        (ReceiverContract.verify contract
            (change "skills/audio/docs" (fun entry -> { entry with Mode = 0o700u }) baseline))
    check "wrong uid refuses"
        (Error(ReceiverContract.WrongOwner "skill-manifest.json"))
        (ReceiverContract.verify contract
            (change "skill-manifest.json" (fun entry -> { entry with UserId = 2001u }) baseline))
    check "wrong gid refuses"
        (Error(ReceiverContract.WrongOwner "skills/audio/docs/guide.md"))
        (ReceiverContract.verify contract
            (change "skills/audio/docs/guide.md" (fun entry -> { entry with GroupId = 2002u }) baseline))
    check "symlink kind refuses"
        (Error(ReceiverContract.WrongKind "skills/audio/SKILL.md"))
        (ReceiverContract.verify contract
            (change "skills/audio/SKILL.md" (fun entry -> { entry with Kind = ReceiverContract.Directory }) baseline))
    check "missing receiver path refuses"
        (Error(ReceiverContract.MissingPath "skills/audio/docs/guide.md"))
        (ReceiverContract.verify contract
            (baseline |> List.filter (fun entry -> entry.Path <> "skills/audio/docs/guide.md")))
    check "extra receiver path refuses"
        (Error(ReceiverContract.UnexpectedPath "obsolete.txt"))
        (ReceiverContract.verify contract
            (metadata "obsolete.txt" ReceiverContract.RegularFile 0o644u :: baseline))
    check "duplicate receiver path refuses"
        (Error(ReceiverContract.DuplicateObservedPath "skill-manifest.json"))
        (ReceiverContract.verify contract
            (metadata "skill-manifest.json" ReceiverContract.RegularFile 0o644u :: baseline))
    check "invalid umask refuses" (Error(ReceiverContract.InvalidUmask 0o1000u))
        (ReceiverContract.expected plan owner 0o1000u |> Result.map (fun _ -> ()))
    match ReceiverContract.expected plan owner 0o077u with
    | Error issue -> failwithf "restricted umask refused: %A" issue
    | Ok restricted ->
        check "restricted umask file mode" 0o600u
            (restricted.Entries |> List.find (fun entry -> entry.Path = "skill-manifest.json") |> _.Mode)
        check "restricted umask directory mode" 0o700u
            (restricted.Entries |> List.find (fun entry -> entry.Path = "skills/audio") |> _.Mode)

    let repo = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
    let realManifest = File.ReadAllBytes(Path.Combine(repo, "template/skill-manifest/skill-manifest.json"))
    let realPlan =
        match ReadOnlySource.capture repo realManifest with
        | Ok value -> value
        | Error issue -> failwithf "real catalog source refused: %A" issue
    let realContract =
        match ReceiverContract.expected realPlan owner 0o022u with
        | Ok value -> value
        | Error issue -> failwithf "real catalog contract refused: %A" issue
    check "real 17-skill plan has manifest and 17 files" 18
        (realContract.Entries |> List.filter (fun entry -> entry.Kind = ReceiverContract.RegularFile) |> List.length)
    printfn "%d/%d receiver contract controls passed" count count
    0
