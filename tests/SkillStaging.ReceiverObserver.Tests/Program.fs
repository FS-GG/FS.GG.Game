open System
open System.IO
open System.Runtime.InteropServices
open System.Text
open FS.GG.Game.SkillStaging

[<DllImport("libc", EntryPoint = "geteuid")>]
extern uint32 geteuid()

[<DllImport("libc", EntryPoint = "getegid")>]
extern uint32 getegid()

[<DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)>]
extern int mkfifo(string path, uint32 mode)

let utf8 = UTF8Encoding(false, true)
let body = utf8.GetBytes "# audio\n"
let digest bytes =
    match Policy.canonicalDigest bytes with
    | Ok value -> value
    | Error () -> failwith "fixture digest failed"
let manifest =
    $"{{\"schemaVersion\":2,\"skills\":[{{\"id\":\"audio\",\"scope\":\"product\",\"supplied-by\":\"template/product-skills/audio/\",\"sha256\":\"{digest body}\",\"files\":[{{\"path\":\"SKILL.md\",\"sha256\":\"{digest body}\"}}]}}]}}"
    |> utf8.GetBytes
let row: Policy.ManifestRow =
    { Id = "audio"; Scope = "product"; SuppliedBy = "template/product-skills/audio/"
      Sha256 = digest body; Files = [ { Path = "SKILL.md"; Sha256 = digest body } ] }
let source: Policy.Source =
    { RelativePath = "template/product-skills/audio/SKILL.md"
      Bytes = body; IsRegularFile = true; IsSymlink = false }
let plan =
    match Policy.prepare manifest [ row ] [ source ] with
    | Ok value -> value
    | Error issue -> failwithf "fixture plan refused: %A" issue
let owner: ReceiverContract.Owner = { UserId = geteuid(); GroupId = getegid() }
let contract =
    match ReceiverContract.expected plan owner 0o022u with
    | Ok value -> value
    | Error issue -> failwithf "fixture contract refused: %A" issue

let mutable count = 0
let check name action =
    action ()
    count <- count + 1
    printfn "ok %s" name

let fixture action =
    let parent = Path.Combine(Path.GetTempPath(), "game-receiver-observer-" + Guid.NewGuid().ToString("N"))
    let root = Path.Combine(parent, "receiver")
    Directory.CreateDirectory(Path.Combine(root, "skills/audio")) |> ignore
    File.WriteAllBytes(Path.Combine(root, "skill-manifest.json"), manifest)
    File.WriteAllBytes(Path.Combine(root, "skills/audio/SKILL.md"), body)
    for directory in [ root; Path.Combine(root, "skills"); Path.Combine(root, "skills/audio") ] do
        File.SetUnixFileMode(directory, enum<UnixFileMode> 0o755)
    for file in [ Path.Combine(root, "skill-manifest.json"); Path.Combine(root, "skills/audio/SKILL.md") ] do
        File.SetUnixFileMode(file, enum<UnixFileMode> 0o644)
    try action root
    finally Directory.Delete(parent, true)

let expect expected actual =
    if expected <> actual then failwithf "expected %A; got %A" expected actual

let expectRefusal predicate actual =
    match actual with
    | Error issue when predicate issue -> ()
    | Error issue -> failwithf "unexpected refusal: %A" issue
    | Ok () -> failwith "unexpected acceptance"

[<EntryPoint>]
let main _ =
    check "exact receiver accepted" (fun () -> fixture (fun root ->
        expect (Ok ()) (ReadOnlyReceiver.verify contract root)))
    check "path-probe/read swap can accept foreign byte-identical file" (fun () -> fixture (fun root ->
        let before =
            match ReadOnlyReceiver.observe root with
            | Ok value -> value
            | Error issue -> failwithf "baseline refused: %A" issue
        let skill = Path.Combine(root, "skills/audio/SKILL.md")
        let foreign = Path.Combine(root, "..", "foreign.md")
        File.WriteAllBytes(foreign, body)
        File.SetUnixFileMode(foreign, enum<UnixFileMode> 0o600)
        File.Move(skill, skill + ".held")
        File.CreateSymbolicLink(skill, foreign) |> ignore
        let pathRead = File.ReadAllBytes skill
        if pathRead <> body then failwith "path reader did not return foreign bytes"
        let mixed =
            before.Payloads |> List.map (fun item ->
                if item.Path = "skills/audio/SKILL.md" then { item with Bytes = pathRead }
                else item)
        // Old pathname workflow combines pre-swap metadata with post-swap bytes.
        expect (Ok ()) (ReceiverContract.verifyComplete contract before.Entries mixed)))
    check "path swap after opened file refuses changed parent roster" (fun () -> fixture (fun root ->
        let skill = Path.Combine(root, "skills/audio/SKILL.md")
        let foreign = Path.Combine(root, "..", "foreign.md")
        File.WriteAllBytes(foreign, utf8.GetBytes "# foreign\n")
        let mutable swapped = false
        let hook path =
            if path = "skills/audio/SKILL.md" then
                File.Move(skill, skill + ".held")
                File.CreateSymbolicLink(skill, foreign) |> ignore
                swapped <- true
        ReadOnlyReceiver.verifyWithOpenHook hook contract root
        |> expectRefusal (function ReadOnlyReceiver.Unstable "skills/audio" -> true | _ -> false)
        if not swapped then failwith "swap hook did not run"))
    check "late roster addition refuses" (fun () -> fixture (fun root ->
        let hook path =
            if path = "skills/audio/SKILL.md" then
                File.WriteAllBytes(Path.Combine(root, "skills/audio/late.md"), body)
        ReadOnlyReceiver.verifyWithOpenHook hook contract root
        |> expectRefusal (function ReadOnlyReceiver.Unstable "skills/audio" -> true | _ -> false)))
    check "symlink before child open refuses" (fun () -> fixture (fun root ->
        let skill = Path.Combine(root, "skills/audio/SKILL.md")
        let foreign = Path.Combine(root, "..", "foreign.md")
        File.WriteAllBytes(foreign, body)
        let hook path =
            if path = "skills/audio" then
                File.Move(skill, skill + ".held")
                File.CreateSymbolicLink(skill, foreign) |> ignore
        ReadOnlyReceiver.verifyWithOpenHook hook contract root
        |> expectRefusal (function ReadOnlyReceiver.Symlink "skills/audio/SKILL.md" -> true | _ -> false)))
    check "changed bytes refuse" (fun () -> fixture (fun root ->
        File.WriteAllBytes(Path.Combine(root, "skills/audio/SKILL.md"), utf8.GetBytes "# changed\n")
        ReadOnlyReceiver.verify contract root
        |> expectRefusal (function
            | ReadOnlyReceiver.ContractRefusal (ReceiverContract.WrongBytes "skills/audio/SKILL.md") -> true
            | _ -> false)))
    check "wrong mode refuses" (fun () -> fixture (fun root ->
        File.SetUnixFileMode(Path.Combine(root, "skills/audio/SKILL.md"), enum<UnixFileMode> 0o600)
        ReadOnlyReceiver.verify contract root
        |> expectRefusal (function
            | ReadOnlyReceiver.ContractRefusal (ReceiverContract.WrongMode "skills/audio/SKILL.md") -> true
            | _ -> false)))
    check "wrong owner refuses" (fun () -> fixture (fun root ->
        let otherOwner: ReceiverContract.Owner =
            { UserId = owner.UserId + 1u; GroupId = owner.GroupId }
        let wrongContract =
            match ReceiverContract.expected plan otherOwner 0o022u with
            | Ok value -> value
            | Error issue -> failwithf "wrong-owner fixture refused: %A" issue
        ReadOnlyReceiver.verify wrongContract root
        |> expectRefusal (function
            | ReadOnlyReceiver.ContractRefusal (ReceiverContract.WrongOwner _) -> true
            | _ -> false)))
    check "extra receiver entry refuses" (fun () -> fixture (fun root ->
        File.WriteAllBytes(Path.Combine(root, "skills/audio/extra.md"), body)
        ReadOnlyReceiver.verify contract root
        |> expectRefusal (function
            | ReadOnlyReceiver.ContractRefusal (ReceiverContract.UnexpectedPath "skills/audio/extra.md") -> true
            | _ -> false)))
    check "FIFO refuses without reading" (fun () -> fixture (fun root ->
        let skill = Path.Combine(root, "skills/audio/SKILL.md")
        File.Delete skill
        if mkfifo(skill, 0o644u) <> 0 then failwith "mkfifo failed"
        ReadOnlyReceiver.verify contract root
        |> expectRefusal (function ReadOnlyReceiver.NonRegular "skills/audio/SKILL.md" -> true | _ -> false)))
    check "receiver-root symlink refuses" (fun () -> fixture (fun root ->
        let alias = Path.Combine(root, "..", "alias")
        Directory.CreateSymbolicLink(alias, root) |> ignore
        ReadOnlyReceiver.verify contract alias
        |> expectRefusal (function ReadOnlyReceiver.Symlink "." -> true | _ -> false)))
    printfn "%d/%d receiver observer controls passed" count count
    0
