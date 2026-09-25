namespace FS.GG.Game.SkillStaging

open System
open System.Security.Cryptography
open System.Text

/// Pure preparation for owner-authored product skill staging. The caller supplies filesystem facts;
/// this module neither reads paths nor writes package content.
module Policy =
    type ManifestRow = {
        Id: string
        Scope: string
        SuppliedBy: string
        Sha256: string
    }

    type Source = {
        RelativePath: string
        Bytes: byte[]
        IsRegularFile: bool
        IsSymlink: bool
    }

    type StagedSkill = {
        Id: string
        Destination: string
        Bytes: byte[]
    }

    type StagePlan = {
        ManifestBytes: byte[]
        Skills: StagedSkill list
    }

    type Refusal =
        | EmptyProductSet
        | InvalidId of string
        | DuplicateId of string
        | InvalidSourcePath of string
        | MissingSource of string
        | NonRegularSource of string
        | SymlinkSource of string
        | InvalidUtf8 of string
        | InvalidDigest of string
        | DigestMismatch of string

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

    let private validId (id: string) =
        not (String.IsNullOrEmpty id)
        && id |> Seq.forall (fun c -> (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c = '-')
        && id.[0] <> '-'
        && id.[id.Length - 1] <> '-'

    let private validDigest (value: string) =
        not (String.IsNullOrEmpty value)
        && value.Length = 64
        && value |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

    let private expectedSource id = sprintf "template/product-skills/%s/SKILL.md" id

    /// All product rows must resolve to one regular owner-authored source; duplicate or malformed
    /// rows fail the whole plan. Non-product rows belong to other delivery channels.
    let prepare (manifestBytes: byte[]) (rows: ManifestRow list) (sources: Source list) : Result<StagePlan, Refusal> =
        let productRows = rows |> List.filter (fun row -> row.Scope = "product")
        if List.isEmpty productRows then Error EmptyProductSet else
        let rec collect (seen: Set<string>) (staged: StagedSkill list) (remaining: ManifestRow list) =
            match remaining with
            | [] -> Ok { ManifestBytes = manifestBytes; Skills = List.rev staged }
            | row :: tail ->
                let expected = expectedSource row.Id
                if not (validId row.Id) then Error(InvalidId row.Id)
                elif Set.contains row.Id seen then Error(DuplicateId row.Id)
                elif row.SuppliedBy <> sprintf "template/product-skills/%s/" row.Id then
                    Error(InvalidSourcePath row.SuppliedBy)
                elif not (validDigest row.Sha256) then Error(InvalidDigest row.Id)
                else
                    match sources |> List.filter (fun source -> source.RelativePath = expected) with
                    | [] -> Error(MissingSource expected)
                    | [ source ] when source.IsSymlink -> Error(SymlinkSource expected)
                    | [ source ] when not source.IsRegularFile -> Error(NonRegularSource expected)
                    | [ source ] ->
                        match canonicalDigest source.Bytes with
                        | Error () -> Error(InvalidUtf8 expected)
                        | Ok digest when digest <> row.Sha256 -> Error(DigestMismatch row.Id)
                        | Ok _ ->
                            let skill = { Id = row.Id; Destination = sprintf "skills/%s/SKILL.md" row.Id; Bytes = source.Bytes }
                            collect (Set.add row.Id seen) (skill :: staged) tail
                    | _ -> Error(InvalidSourcePath expected)
        collect Set.empty [] productRows
