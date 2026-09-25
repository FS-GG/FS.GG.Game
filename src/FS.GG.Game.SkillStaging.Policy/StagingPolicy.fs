namespace FS.GG.Game.SkillStaging

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// Pure preparation for owner-authored product skill staging. The caller supplies filesystem facts;
/// this module neither reads paths nor writes package content.
module Policy =
    type ManifestFile = { Path: string; Sha256: string }

    type ManifestRow = {
        Id: string
        Scope: string
        SuppliedBy: string
        Sha256: string
        Files: ManifestFile list
    }

    type Source = {
        RelativePath: string
        Bytes: byte[]
        IsRegularFile: bool
        IsSymlink: bool
    }

    type StagedSkill internal (id: string, destination: string, bytes: byte[]) =
        let snapshot = Array.copy bytes
        member _.Id = id
        member _.Destination = destination
        member _.Bytes = Array.copy snapshot

    type StagePlan internal (manifestBytes: byte[], skills: StagedSkill list) =
        let snapshot = Array.copy manifestBytes
        member _.ManifestBytes = Array.copy snapshot
        member _.Skills = skills

    type Refusal =
        | EmptyProductSet
        | InvalidId of string
        | DuplicateId of string
        | InvalidSourcePath of string
        | MissingSource of string
        | UnexpectedSource of string
        | NonRegularSource of string
        | SymlinkSource of string
        | InvalidUtf8 of string
        | InvalidDigest of string
        | DigestMismatch of string
        | InvalidManifest
        | ManifestRowsMismatch

    let private utf8 = UTF8Encoding(false, true)

    /// Matches the F# manifest generator: ReadAllText drops the leading UTF-8 BOM and its
    /// sha256Text replaces CRLF with LF before hashing. Lone CR is preserved.
    let canonicalDigest (raw: byte[]) : Result<string, unit> =
        if obj.ReferenceEquals(raw, null) then Error ()
        else
            try
                let text = utf8.GetString raw
                let body = if text.StartsWith("\uFEFF", StringComparison.Ordinal) then text.Substring 1 else text
                let normalized = body.Replace("\r\n", "\n")
                let bytes = utf8.GetBytes normalized
                SHA256.HashData bytes
                |> Convert.ToHexString
                |> fun digest -> Ok(digest.ToLowerInvariant())
            with :? DecoderFallbackException -> Error ()

    let private isAsciiLetter c = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
    let private isAsciiDigit c = c >= '0' && c <= '9'

    let private validId (id: string) =
        not (String.IsNullOrEmpty id)
        && (isAsciiLetter id.[0] || isAsciiDigit id.[0])
        && id |> Seq.forall (fun c -> isAsciiLetter c || isAsciiDigit c || c = '.' || c = '_' || c = '-')

    let private validFilePath (path: string) =
        not (String.IsNullOrEmpty path)
        && not (path.StartsWith("/", StringComparison.Ordinal))
        && not (path.Contains '\\')
        // Python's live stager uses Unicode casefold for path collisions.
        // ToLowerInvariant below cannot reproduce multi-character folds such
        // as ß -> ss. Until this candidate has exact casefold parity, refuse
        // non-ASCII declarations rather than accept an aliased closed set.
        && path |> Seq.forall (fun c -> int c >= 32 && int c <= 126)
        && path.Split('/') |> Array.forall (fun part -> part <> "" && part <> "." && part <> "..")

    let private validDigest (value: string) =
        not (String.IsNullOrEmpty value)
        && value.Length = 64
        && value |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

    let private validManifestFiles (row: ManifestRow) =
        let paths = row.Files |> List.map (fun file -> file.Path)
        not (List.isEmpty row.Files)
        && row.Files |> List.forall (fun file -> validFilePath file.Path && validDigest file.Sha256)
        && (paths |> List.map _.ToLowerInvariant() |> List.distinct |> List.length) = paths.Length
        && not (paths |> List.exists (fun path ->
            paths |> List.exists (fun other ->
                other <> path && other.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))))
        && row.Files |> List.tryFind (fun file -> file.Path = "SKILL.md")
           = Some { Path = "SKILL.md"; Sha256 = row.Sha256 }

    let private expectedSource id = sprintf "template/product-skills/%s/SKILL.md" id

    let private manifestRows (raw: byte[]) =
        try
            use document = JsonDocument.Parse(ReadOnlyMemory<byte>(raw))
            let root = document.RootElement
            let rec requireUniqueProperties (item: JsonElement) =
                match item.ValueKind with
                | JsonValueKind.Object ->
                    let seen = HashSet<string>(StringComparer.Ordinal)
                    for property in item.EnumerateObject() do
                        if not (seen.Add property.Name) then
                            raise (JsonException $"duplicate JSON property {property.Name}")
                        requireUniqueProperties property.Value
                | JsonValueKind.Array ->
                    for element in item.EnumerateArray() do
                        requireUniqueProperties element
                | _ -> ()
            requireUniqueProperties root
            let required (item: JsonElement) (name: string) =
                item.GetProperty(name).GetString()
                |> Option.ofObj
                |> Option.defaultWith (fun () -> raise (JsonException $"null {name}"))
            if root.GetProperty("schemaVersion").GetInt32() <> 2 then Error InvalidManifest
            else
                let skills = root.GetProperty("skills")
                if skills.ValueKind <> JsonValueKind.Array then Error InvalidManifest
                else
                    skills.EnumerateArray()
                    |> Seq.map (fun item ->
                        if item.ValueKind <> JsonValueKind.Object then raise (JsonException "skill row must be an object")
                        let row =
                            { Id = required item "id"
                              Scope = required item "scope"
                              SuppliedBy = required item "supplied-by"
                              Sha256 = required item "sha256"
                              Files =
                                let mutable files = Unchecked.defaultof<JsonElement>
                                if item.TryGetProperty("files", &files) then
                                    if files.ValueKind <> JsonValueKind.Array then
                                        raise (JsonException "files must be an array")
                                    files.EnumerateArray()
                                    |> Seq.map (fun file ->
                                        if file.ValueKind <> JsonValueKind.Object then
                                            raise (JsonException "file row must be an object")
                                        { Path = required file "path"; Sha256 = required file "sha256" })
                                    |> Seq.toList
                                else [] }
                        if row.Scope = "product" then
                            if not (validManifestFiles row) then
                                raise (JsonException "product files are not a closed, valid set")
                        row)
                    |> Seq.toList
                    |> Ok
        with
        | :? JsonException
        | :? InvalidOperationException
        | :? KeyNotFoundException
        | :? ArgumentException
        | :? NullReferenceException -> Error InvalidManifest

    /// All product rows must resolve to one regular owner-authored source; duplicate or malformed
    /// rows fail the whole plan. Non-product rows belong to other delivery channels.
    let prepare (manifestBytes: byte[]) (rows: ManifestRow list) (sources: Source list) : Result<StagePlan, Refusal> =
        match manifestRows manifestBytes with
        | Error issue -> Error issue
        | Ok authoredRows when authoredRows <> rows -> Error ManifestRowsMismatch
        | Ok _ ->
            let manifestSnapshot = Array.copy manifestBytes
            let sourceSnapshots =
                sources
                |> List.map (fun source ->
                    { source with
                        Bytes = if obj.ReferenceEquals(source.Bytes, null) then source.Bytes else Array.copy source.Bytes })
            let productRows = rows |> List.filter (fun row -> row.Scope = "product")
            if List.isEmpty productRows then Error EmptyProductSet else
            let rec collect (seen: Set<string>) (staged: StagedSkill list) (remaining: ManifestRow list) =
                match remaining with
                | [] -> Ok(StagePlan(manifestSnapshot, List.rev staged))
                | row :: tail ->
                    let prefix = sprintf "template/product-skills/%s/" row.Id
                    let expected = expectedSource row.Id
                    if not (validId row.Id) then Error(InvalidId row.Id)
                    elif Set.contains (row.Id.ToLowerInvariant()) seen then Error(DuplicateId row.Id)
                    elif row.SuppliedBy <> prefix then
                        Error(InvalidSourcePath row.SuppliedBy)
                    elif not (validDigest row.Sha256) then Error(InvalidDigest row.Id)
                    elif not (validManifestFiles row) then
                        Error InvalidManifest
                    else
                        let actual =
                            sourceSnapshots
                            |> List.filter (fun source ->
                                not (obj.ReferenceEquals(source.RelativePath, null))
                                && source.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
                        let expectedPaths = row.Files |> List.map (fun file -> prefix + file.Path)
                        match actual |> List.tryFind (fun source -> source.IsSymlink) with
                        | Some source -> Error(SymlinkSource source.RelativePath)
                        | None ->
                            match actual |> List.tryFind (fun source -> not source.IsRegularFile) with
                            | Some source -> Error(NonRegularSource source.RelativePath)
                            | None ->
                                match actual |> List.groupBy (fun source -> source.RelativePath.ToLowerInvariant())
                                      |> List.tryFind (fun (_, entries) -> entries.Length > 1) with
                                | Some (_, duplicate :: _) -> Error(InvalidSourcePath duplicate.RelativePath)
                                | Some (_, []) -> Error InvalidManifest
                                | None ->
                                    match expectedPaths |> List.tryFind (fun path ->
                                        not (actual |> List.exists (fun source -> source.RelativePath = path))) with
                                    | Some missing -> Error(MissingSource missing)
                                    | None ->
                                        match actual |> List.tryFind (fun source ->
                                            not (expectedPaths |> List.contains source.RelativePath)) with
                                        | Some extra -> Error(UnexpectedSource extra.RelativePath)
                                        | None ->
                                            let rec addFiles staged files =
                                                match files with
                                                | [] -> collect (Set.add (row.Id.ToLowerInvariant()) seen) staged tail
                                                | file :: rest ->
                                                    let path = prefix + file.Path
                                                    let source = actual |> List.find (fun item -> item.RelativePath = path)
                                                    match canonicalDigest source.Bytes with
                                                    | Error () -> Error(InvalidUtf8 path)
                                                    | Ok digest when digest <> file.Sha256 -> Error(DigestMismatch row.Id)
                                                    | Ok _ ->
                                                        let stagedFile =
                                                            StagedSkill(row.Id, sprintf "skills/%s/%s" row.Id file.Path, source.Bytes)
                                                        addFiles (stagedFile :: staged) rest
                                            addFiles staged row.Files
            collect Set.empty [] productRows
