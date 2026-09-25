namespace FS.GG.Game.SkillStaging

open System
open System.Collections.Generic
open System.IO
open System.Text.Json

/// Reads a physical product-skills tree and passes its observed files to the pure staging policy.
/// Opened source bytes are pinned to descriptors; concurrent changes can still alter the roster.
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

    let private mapFailure path = function
        | LinuxDescriptors.Link -> Symlink path
        | LinuxDescriptors.NonRegular -> NonRegular path
        | LinuxDescriptors.Unreadable -> Unreadable path

    let private captureCore (afterOpen: string -> unit) (repoRoot: string) (manifestBytes: byte[]) : Result<Policy.StagePlan, Refusal> =
        if not (OperatingSystem.IsLinux()) || not BitConverter.IsLittleEndian then Error UnsupportedPlatform
        elif String.IsNullOrWhiteSpace repoRoot then Error(MissingTree "repository root")
        else
            match parseRows manifestBytes with
            | Error issue -> Error issue
            | Ok rows ->
                try
                    let root = Path.GetFullPath repoRoot
                    match LinuxDescriptors.openRoot root with
                    | Error issue -> Error(mapFailure root issue)
                    | Ok repo ->
                        use repo = repo
                        let openRequired parent name label =
                            match LinuxDescriptors.openChild parent name with
                            | Error LinuxDescriptors.Unreadable -> Error(MissingTree label)
                            | Error issue -> Error(mapFailure label issue)
                            | Ok(handle, LinuxDescriptors.Directory) -> Ok handle
                            | Ok(handle, _) ->
                                handle.Dispose()
                                Error(NonRegular label)
                        match openRequired repo "template" "template" with
                        | Error issue -> Error issue
                        | Ok template ->
                            use template = template
                            match openRequired template "product-skills" "template/product-skills" with
                            | Error issue -> Error issue
                            | Ok tree ->
                                use tree = tree
                                let expected =
                                    rows |> List.filter (fun row -> row.Scope = "product")
                                    |> List.map (fun row -> row.Id) |> Set.ofList
                                let observed = ResizeArray<Policy.Source>()
                                let seen = HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                let rec visit (parent: Microsoft.Win32.SafeHandles.SafeFileHandle) name rel requireDirectory =
                                    if not (seen.Add rel) then Error(DuplicatePath rel)
                                    else
                                        match LinuxDescriptors.openChild parent name with
                                        | Error issue -> Error(mapFailure rel issue)
                                        | Ok(handle, kind) ->
                                            use handle = handle
                                            if requireDirectory && kind <> LinuxDescriptors.Directory then Error(NonRegular rel)
                                            else
                                                afterOpen rel
                                                match kind with
                                                | LinuxDescriptors.Special -> Error(NonRegular rel)
                                                | LinuxDescriptors.Regular ->
                                                    match LinuxDescriptors.readBytes handle with
                                                    | Error issue -> Error(mapFailure rel issue)
                                                    | Ok bytes ->
                                                        observed.Add
                                                            { RelativePath = rel; Bytes = bytes
                                                              IsRegularFile = true; IsSymlink = false }
                                                        Ok ()
                                                | LinuxDescriptors.Directory ->
                                                    match LinuxDescriptors.names handle with
                                                    | Error issue -> Error(mapFailure rel issue)
                                                    | Ok [] -> Error(EmptyDirectory rel)
                                                    | Ok names ->
                                                        names
                                                        |> List.fold (fun state child ->
                                                            match state with
                                                            | Error _ -> state
                                                            | Ok () -> visit handle child (rel + "/" + child) false) (Ok ())
                                let result =
                                    match LinuxDescriptors.names tree with
                                    | Error issue -> Error(mapFailure "template/product-skills" issue)
                                    | Ok roots ->
                                        roots
                                        |> List.fold (fun state id ->
                                            match state with
                                            | Error _ -> state
                                            | Ok () ->
                                                let rel = "template/product-skills/" + id
                                                if not (Set.contains id expected) then Error(UnexpectedRoot rel)
                                                else visit tree id rel true) (Ok ())
                                match result with
                                | Error issue -> Error issue
                                | Ok () ->
                                    match Policy.prepare manifestBytes rows (List.ofSeq observed) with
                                    | Ok plan -> Ok plan
                                    | Error issue -> Error(PolicyRefusal issue)
                with
                | :? IOException as issue -> Error(Unreadable issue.Message)
                | :? UnauthorizedAccessException as issue -> Error(Unreadable issue.Message)
                | :? ArgumentException as issue -> Error(Unreadable issue.Message)
                | :? DllNotFoundException
                | :? EntryPointNotFoundException -> Error UnsupportedPlatform

    /// Snapshot selected product roots under repoRoot. There is no destination argument or write.
    let capture repoRoot manifestBytes = captureCore ignore repoRoot manifestBytes

    // Test seam runs only after a descriptor is open and typed, before its bytes or children are read.
    let internal captureWithOpenHook afterOpen repoRoot manifestBytes =
        captureCore afterOpen repoRoot manifestBytes
