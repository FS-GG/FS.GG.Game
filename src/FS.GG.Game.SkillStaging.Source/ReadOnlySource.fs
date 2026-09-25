namespace FS.GG.Game.SkillStaging

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Text.Json

/// Reads a physical product-skills tree and passes its observed files to the pure staging policy.
/// This is a path-based, read-only snapshot; concurrent path replacement can invalidate its facts.
module ReadOnlySource =
    open Policy

    type Refusal =
        | InvalidManifest
        | MissingTree of string
        | UnexpectedRoot of string
        | EmptyDirectory of string
        | DuplicatePath of string
        | Symlink of string
        | NonRegular of string
        | Unreadable of string
        | UnsupportedPlatform
        | PolicyRefusal of Policy.Refusal

    type private Kind = Regular | Directory | Link | Special

    [<DllImport("libc", EntryPoint = "statx", SetLastError = true, CharSet = CharSet.Ansi)>]
    extern int private statx(int dirfd, string path, int flags, uint32 mask, [<Out>] byte[] buffer)

    let private classify path =
        // Linux statx with AT_SYMLINK_NOFOLLOW rejects FIFO/device/socket sources before any read.
        // The path can still change between this probe and the later enumeration or read.
        let buffer = Array.zeroCreate<byte> 256
        if statx(-100, path, 0x100, 0x3u, buffer) <> 0 then
            Error(Unreadable path)
        else
            match int (BitConverter.ToUInt16(buffer, 28)) &&& 0xF000 with
            | 0x8000 -> Ok Regular
            | 0x4000 -> Ok Directory
            | 0xA000 -> Ok Link
            | _ -> Ok Special

    let private parseRows (raw: byte[]) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(raw))
            let property (item: JsonElement) (name: string) =
                item.GetProperty(name).GetString() |> Option.ofObj |> Option.defaultValue ""
            let rows =
                document.RootElement.GetProperty("skills").EnumerateArray()
                |> Seq.map (fun item ->
                    let files: Policy.ManifestFile list =
                        let mutable entries = Unchecked.defaultof<JsonElement>
                        if item.TryGetProperty("files", &entries) then
                            entries.EnumerateArray()
                            |> Seq.map (fun file ->
                                { Path = (property file "path"); Sha256 = (property file "sha256") })
                            |> Seq.toList
                        else []
                    ({ Id = (property item "id"); Scope = (property item "scope")
                       SuppliedBy = (property item "supplied-by"); Sha256 = (property item "sha256")
                       Files = files }: Policy.ManifestRow))
                |> Seq.toList
            Ok rows
        with
        | :? JsonException
        | :? InvalidOperationException
        | :? KeyNotFoundException
        | :? ArgumentException
        | :? NullReferenceException -> Error InvalidManifest

    let private sortedEntries path =
        Directory.EnumerateFileSystemEntries path
        |> Seq.sortWith (fun a b -> StringComparer.Ordinal.Compare(a, b))
        |> Seq.toList

    let private checkAncestors path =
        let rec loop current =
            match classify current with
            | Error issue -> Error issue
            | Ok Link -> Error(Symlink current)
            | Ok Directory ->
                match Path.GetDirectoryName current |> Option.ofObj with
                | Some parent when parent <> current -> loop parent
                | _ -> Ok ()
            | Ok _ -> Error(NonRegular current)
        loop path

    /// Snapshot selected product roots under repoRoot. There is no destination argument or write.
    let capture (repoRoot: string) (manifestBytes: byte[]) : Result<Policy.StagePlan, Refusal> =
        if not (OperatingSystem.IsLinux()) then Error UnsupportedPlatform
        elif String.IsNullOrWhiteSpace repoRoot then Error(MissingTree "repository root")
        else
            match parseRows manifestBytes with
            | Error issue -> Error issue
            | Ok rows ->
                try
                    let root = Path.GetFullPath repoRoot
                    let template = Path.Combine(root, "template")
                    let tree = Path.Combine(template, "product-skills")
                    let requireDirectory path =
                        match classify path with
                        | Ok Directory -> Ok ()
                        | Ok Link -> Error(Symlink path)
                        | Ok _ -> Error(NonRegular path)
                        | Error _ -> Error(MissingTree path)
                    let result =
                        match checkAncestors root with
                        | Error issue -> Error issue
                        | Ok () ->
                            match requireDirectory template with
                            | Error issue -> Error issue
                            | Ok () -> requireDirectory tree
                    match result with
                    | Error issue -> Error issue
                    | Ok () ->
                        let expected =
                            rows |> List.filter (fun row -> row.Scope = "product")
                            |> List.map (fun row -> row.Id) |> Set.ofList
                        let observed = ResizeArray<Policy.Source>()
                        let seen = HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        let relative path = Path.GetRelativePath(root, path).Replace('\\', '/')
                        let rec walk path =
                            let rel = relative path
                            if not (seen.Add rel) then Error(DuplicatePath rel)
                            else
                                match classify path with
                                | Error issue -> Error issue
                                | Ok Link -> Error(Symlink rel)
                                | Ok Special -> Error(NonRegular rel)
                                | Ok Regular ->
                                    let bytes = File.ReadAllBytes path
                                    observed.Add
                                        { RelativePath = rel; Bytes = bytes
                                          IsRegularFile = true; IsSymlink = false }
                                    Ok ()
                                | Ok Directory ->
                                    let entries = sortedEntries path
                                    if List.isEmpty entries then Error(EmptyDirectory rel)
                                    else
                                        entries
                                        |> List.fold (fun state entry ->
                                            match state with Error _ -> state | Ok () -> walk entry) (Ok ())
                        let roots = sortedEntries tree
                        let rootsResult =
                            roots
                            |> List.fold (fun state path ->
                                match state with
                                | Error _ -> state
                                | Ok () ->
                                    let rel = relative path
                                    let id = Path.GetFileName path
                                    if not (seen.Add rel) then Error(DuplicatePath rel)
                                    elif not (Set.contains id expected) then Error(UnexpectedRoot rel)
                                    else
                                        match classify path with
                                        | Ok Directory ->
                                            let entries = sortedEntries path
                                            if List.isEmpty entries then Error(EmptyDirectory rel)
                                            else
                                                entries
                                                |> List.fold (fun state entry ->
                                                    match state with Error _ -> state | Ok () -> walk entry) (Ok ())
                                        | Ok Link -> Error(Symlink rel)
                                        | Ok _ -> Error(NonRegular rel)
                                        | Error issue -> Error issue) (Ok ())
                        match rootsResult with
                        | Error issue -> Error issue
                        | Ok () ->
                            match Policy.prepare manifestBytes rows (List.ofSeq observed) with
                            | Ok plan -> Ok plan
                            | Error issue -> Error(PolicyRefusal issue)
                with
                | :? IOException as issue -> Error(Unreadable issue.Message)
                | :? UnauthorizedAccessException as issue -> Error(Unreadable issue.Message)
                | :? ArgumentException as issue -> Error(Unreadable issue.Message)
