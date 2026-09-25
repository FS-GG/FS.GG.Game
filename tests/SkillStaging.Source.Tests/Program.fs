open System
open System.IO
open System.Runtime.InteropServices
open System.Text
open FS.GG.Game.SkillStaging

let utf8 = UTF8Encoding(false, true)
let mutable passed = 0

let check name action =
    action ()
    passed <- passed + 1
    printfn "ok %s" name

let fixture action =
    let root = Path.Combine(Path.GetTempPath(), "game-physical-source-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory root |> ignore
    try action root
    finally Directory.Delete(root, true)

let write root relative (bytes: byte[]) =
    let path = Path.Combine(root, relative)
    Path.GetDirectoryName path |> Option.ofObj |> Option.iter (fun parent -> Directory.CreateDirectory parent |> ignore)
    File.WriteAllBytes(path, bytes)
    path

let sample bytes =
    let digest =
        match Policy.canonicalDigest bytes with
        | Ok value -> value
        | Error () -> failwith "sample bytes must be UTF-8"
    let manifest =
        $"{{\"schemaVersion\":2,\"skills\":[{{\"id\":\"audio\",\"scope\":\"product\",\"supplied-by\":\"template/product-skills/audio/\",\"sha256\":\"{digest}\",\"files\":[{{\"path\":\"SKILL.md\",\"sha256\":\"{digest}\"}}]}}]}}"
        |> utf8.GetBytes
    manifest

let setup root bytes =
    let path = write root "template/product-skills/audio/SKILL.md" bytes
    sample bytes, path

let expectRefusal expected result =
    match result with
    | Error issue when expected issue -> ()
    | Error issue -> failwithf "unexpected refusal: %A" issue
    | Ok _ -> failwith "physical source unexpectedly accepted"

[<DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)>]
extern int mkfifo(string path, uint32 mode)

[<DllImport("libc", EntryPoint = "statx", SetLastError = true, CharSet = CharSet.Ansi)>]
extern int statx(int parent, string path, int flags, uint32 mask, [<Out>] byte[] buffer)

[<EntryPoint>]
let main _ =
    let bytes = utf8.GetBytes "# audio\n"
    check "directory rename/restore during enumeration refuses" (fun () -> fixture (fun root ->
        let manifest, path = setup root bytes
        let product = Path.GetDirectoryName path |> Option.ofObj |> Option.defaultWith (fun () -> failwith "missing parent")
        let mutable mutated = false
        let afterBatch (rel: string) =
            if rel = "template/product-skills/audio" then
                File.Move(path, path + ".held")
                File.Move(path + ".held", path)
                Directory.SetLastWriteTimeUtc(product, DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                mutated <- true
        ReadOnlySource.captureWithHooks ignore afterBatch root manifest
        |> expectRefusal (function ReadOnlySource.DirectoryUnstable path when mutated && path.EndsWith("/audio") -> true | _ -> false)))
    check "added entry during enumeration refuses" (fun () -> fixture (fun root ->
        let manifest, path = setup root bytes
        let product = Path.GetDirectoryName path |> Option.ofObj |> Option.defaultWith (fun () -> failwith "missing parent")
        let afterBatch (rel: string) =
            if rel = "template/product-skills/audio" then
                write root "template/product-skills/audio/extra.txt" bytes |> ignore
                Directory.SetLastWriteTimeUtc(product, DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        ReadOnlySource.captureWithHooks ignore afterBatch root manifest
        |> expectRefusal (function ReadOnlySource.DirectoryUnstable path when path.EndsWith("/audio") -> true | _ -> false)))
    check "removed entry during enumeration refuses" (fun () -> fixture (fun root ->
        let manifest, path = setup root bytes
        let product = Path.GetDirectoryName path |> Option.ofObj |> Option.defaultWith (fun () -> failwith "missing parent")
        let afterBatch (rel: string) =
            if rel = "template/product-skills/audio" then
                File.Delete path
                Directory.SetLastWriteTimeUtc(product, DateTime(2003, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        ReadOnlySource.captureWithHooks ignore afterBatch root manifest
        |> expectRefusal (function ReadOnlySource.DirectoryUnstable path when path.EndsWith("/audio") -> true | _ -> false)))
    check "path probe/read swap characterization" (fun () -> fixture (fun root ->
        let _, path = setup root bytes
        let foreignBytes = utf8.GetBytes "# foreign\n"
        let foreign = write root "foreign.txt" foreignBytes
        let stat = Array.zeroCreate<byte> 256
        if statx(-100, path, 0x100, 0x3u, stat) <> 0
           || (int (BitConverter.ToUInt16(stat, 28)) &&& 0xF000) <> 0x8000 then
            failwith "original path did not probe as regular"
        File.Move(path, path + ".held")
        File.CreateSymbolicLink(path, foreign) |> ignore
        if File.ReadAllBytes(path) <> foreignBytes then failwith "path read did not follow the swapped link"))
    check "opened file descriptor resists path swap" (fun () -> fixture (fun root ->
        let manifest, path = setup root bytes
        let foreign = write root "foreign.txt" (utf8.GetBytes "# foreign\n")
        let mutable swapped = false
        let hook (rel: string) =
            if rel.EndsWith("/SKILL.md", StringComparison.Ordinal) then
                File.Move(path, path + ".held")
                File.CreateSymbolicLink(path, foreign) |> ignore
                swapped <- true
        match ReadOnlySource.captureWithOpenHook hook root manifest with
        | Error issue -> failwithf "pinned file refused: %A" issue
        | Ok plan ->
            if not swapped || plan.Skills.Head.Bytes <> bytes then
                failwith "swapped path changed opened file bytes"))
    check "opened parent descriptor resists directory swap" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        let foreign = Path.Combine(root, "foreign")
        write root "foreign/SKILL.md" (utf8.GetBytes "# foreign\n") |> ignore
        let product = Path.Combine(root, "template/product-skills/audio")
        let mutable swapped = false
        let hook (rel: string) =
            if rel = "template/product-skills/audio" then
                Directory.Move(product, product + ".held")
                Directory.CreateSymbolicLink(product, foreign) |> ignore
                swapped <- true
        match ReadOnlySource.captureWithOpenHook hook root manifest with
        | Error issue -> failwithf "pinned directory refused: %A" issue
        | Ok plan ->
            if not swapped || plan.Skills.Head.Bytes <> bytes then
                failwith "swapped parent changed descendant bytes"))
    check "symlink introduced before child open refuses" (fun () -> fixture (fun root ->
        let manifest, path = setup root bytes
        let foreign = write root "foreign.txt" (utf8.GetBytes "# foreign\n")
        let hook (rel: string) =
            if rel = "template/product-skills/audio" then
                File.Move(path, Path.Combine(root, "held.md"))
                File.CreateSymbolicLink(path, foreign) |> ignore
        ReadOnlySource.captureWithOpenHook hook root manifest
        |> expectRefusal (function ReadOnlySource.Symlink path when path.EndsWith("/SKILL.md") -> true | _ -> false)))
    check "valid source preserves exact bytes" (fun () -> fixture (fun root ->
        let raw = utf8.GetBytes "\uFEFF# audio\r\n"
        let manifest, path = setup root raw
        match ReadOnlySource.capture root manifest with
        | Error issue -> failwithf "valid source refused: %A" issue
        | Ok plan ->
            if plan.Skills.Length <> 1 || plan.Skills.Head.Bytes <> raw then failwith "snapshot bytes changed"
            if plan.ManifestBytes <> manifest then failwith "manifest bytes changed"
            File.WriteAllBytes(path, bytes)
            if plan.Skills.Head.Bytes <> raw then failwith "snapshot changed after source mutation"))
    check "empty undeclared directory" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        Directory.CreateDirectory(Path.Combine(root, "template/product-skills/audio/unused")) |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.EmptyDirectory path when path.EndsWith("/unused") -> true | _ -> false)))
    check "extra source file" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        write root "template/product-skills/audio/extra.txt" bytes |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.PolicyRefusal (Policy.UnexpectedSource path) when path.EndsWith("/extra.txt") -> true | _ -> false)))
    check "extra empty product root" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        Directory.CreateDirectory(Path.Combine(root, "template/product-skills/unused")) |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.UnexpectedRoot path when path.EndsWith("/unused") -> true | _ -> false)))
    check "case alias product root" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        Directory.CreateDirectory(Path.Combine(root, "template/product-skills/AUDIO")) |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.DuplicatePath _ | ReadOnlySource.UnexpectedRoot _ -> true | _ -> false)))
    check "case alias nested path" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        write root "template/product-skills/audio/docs/a.txt" bytes |> ignore
        write root "template/product-skills/audio/DOCS/b.txt" bytes |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.DuplicatePath _ -> true | _ -> false)))
    check "source symlink" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        let link = Path.Combine(root, "template/product-skills/audio/linked.txt")
        File.CreateSymbolicLink(link, "/etc/hosts") |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.Symlink _ -> true | _ -> false)))
    check "directory symlink" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        let link = Path.Combine(root, "template/product-skills/audio/linked")
        Directory.CreateSymbolicLink(link, "/tmp") |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.Symlink _ -> true | _ -> false)))
    check "product root symlink" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        let link = Path.Combine(root, "template/product-skills/AUDIO")
        Directory.CreateSymbolicLink(link, Path.Combine(root, "template/product-skills/audio")) |> ignore
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.DuplicatePath _ | ReadOnlySource.UnexpectedRoot _ | ReadOnlySource.Symlink _ -> true | _ -> false)))
    check "repository root symlink" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        let link = root + "-link"
        try
            Directory.CreateSymbolicLink(link, root) |> ignore
            ReadOnlySource.capture link manifest
            |> expectRefusal (function ReadOnlySource.Symlink _ -> true | _ -> false)
        finally Directory.Delete link))
    check "FIFO is rejected before read" (fun () -> fixture (fun root ->
        let manifest, _ = setup root bytes
        let fifo = Path.Combine(root, "template/product-skills/audio/pipe")
        if mkfifo(fifo, 0x180u) <> 0 then failwith "mkfifo failed"
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.NonRegular _ -> true | _ -> false)))
    check "changed bytes fail digest" (fun () -> fixture (fun root ->
        let manifest, path = setup root bytes
        File.WriteAllBytes(path, utf8.GetBytes "# changed\n")
        ReadOnlySource.capture root manifest
        |> expectRefusal (function ReadOnlySource.PolicyRefusal (Policy.DigestMismatch "audio") -> true | _ -> false)))
    check "malformed manifest" (fun () -> fixture (fun root ->
        setup root bytes |> ignore
        ReadOnlySource.capture root (utf8.GetBytes "{")
        |> expectRefusal (function ReadOnlySource.InvalidManifest -> true | _ -> false)))
    check "actual product catalog" (fun () ->
        let repoRoot = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../.."))
        let manifest = File.ReadAllBytes(Path.Combine(repoRoot, "template/skill-manifest/skill-manifest.json"))
        match ReadOnlySource.capture repoRoot manifest with
        | Error issue -> failwithf "actual catalog refused: %A" issue
        | Ok plan -> if plan.Skills.Length <> 17 then failwithf "expected 17 files, got %d" plan.Skills.Length)
    printfn "%d/%d physical source controls passed" passed passed
    0
