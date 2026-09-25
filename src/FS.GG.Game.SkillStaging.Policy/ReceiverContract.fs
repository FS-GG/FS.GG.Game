namespace FS.GG.Game.SkillStaging

open System

/// Pure receiver metadata expected from #644's mkdir/write_bytes transaction.
/// The caller supplies the effective owner and process umask; no filesystem is read.
module ReceiverContract =
    type Owner = { UserId: uint32; GroupId: uint32 }

    type EntryKind =
        | Directory
        | RegularFile

    type Entry = {
        Path: string
        Kind: EntryKind
        Mode: uint32
        UserId: uint32
        GroupId: uint32
    }

    type Payload = { Path: string; Bytes: byte[] }

    type Contract internal (entries: Entry list, payloads: Payload list) =
        let payloads = payloads |> List.map (fun item -> { item with Bytes = Array.copy item.Bytes })
        member _.Entries = entries
        member internal _.Payloads = payloads

    type Refusal =
        | InvalidUmask of uint32
        | DuplicateObservedPath of string
        | MissingPath of string
        | UnexpectedPath of string
        | WrongKind of string
        | WrongMode of string
        | WrongOwner of string
        | DuplicateObservedPayload of string
        | MissingPayload of string
        | UnexpectedPayload of string
        | WrongBytes of string

    let private parentDirectories (file: string) =
        let parts = file.Split '/'
        [ for count in 1 .. parts.Length - 1 ->
            parts |> Array.take count |> String.concat "/" ]

    /// The root is represented by ".". The contract is scoped to a receiver
    /// created under the supplied umask; it does not claim the process pins it.
    let expected (plan: Policy.StagePlan) (owner: Owner) (umask: uint32) : Result<Contract, Refusal> =
        if umask &&& (~~~0o777u) <> 0u then Error(InvalidUmask umask)
        else
            let files = "skill-manifest.json" :: (plan.Skills |> List.map _.Destination)
            let directories =
                "." :: (files |> List.collect parentDirectories |> List.distinct)
            let entry kind path =
                { Path = path
                  Kind = kind
                  Mode = (if kind = Directory then 0o777u else 0o666u) &&& (~~~umask)
                  UserId = owner.UserId
                  GroupId = owner.GroupId }
            (directories |> List.map (entry Directory))
            @ (files |> List.map (entry RegularFile))
            |> List.sortWith (fun left right -> StringComparer.Ordinal.Compare(left.Path, right.Path))
            |> fun entries ->
                let payloads =
                    { Path = "skill-manifest.json"; Bytes = plan.ManifestBytes }
                    :: (plan.Skills |> List.map (fun skill ->
                        { Path = skill.Destination; Bytes = skill.Bytes }))
                Contract(entries, payloads)
            |> Ok

    /// Compare independently observed receiver metadata with the planned set.
    /// Path, type, mode, uid, and gid must all match; byte checks are separate.
    let verify (contract: Contract) (observed: Entry list) : Result<unit, Refusal> =
        let expected = contract.Entries
        match observed |> List.groupBy _.Path |> List.tryFind (fun (_, rows) -> rows.Length > 1) with
        | Some(path, _) -> Error(DuplicateObservedPath path)
        | None ->
            match expected |> List.tryFind (fun want ->
                not (observed |> List.exists (fun item -> item.Path = want.Path))) with
            | Some missing -> Error(MissingPath missing.Path)
            | None ->
                match observed |> List.tryFind (fun item ->
                    not (expected |> List.exists (fun want -> want.Path = item.Path))) with
                | Some extra -> Error(UnexpectedPath extra.Path)
                | None ->
                    expected
                    |> List.tryPick (fun want ->
                        let item = observed |> List.find (fun item -> item.Path = want.Path)
                        if item.Kind <> want.Kind then Some(WrongKind want.Path)
                        elif item.Mode <> want.Mode then Some(WrongMode want.Path)
                        elif item.UserId <> want.UserId || item.GroupId <> want.GroupId then
                            Some(WrongOwner want.Path)
                        else None)
                    |> function Some issue -> Error issue | None -> Ok ()

    /// Require both the metadata roster and the exact planned bytes. Observed
    /// payloads must come from an independent receiver observation; this pure
    /// check does not establish a stable physical snapshot or safe file opening.
    let verifyComplete (contract: Contract) (observed: Entry list)
                       (payloads: Payload list) : Result<unit, Refusal> =
        match verify contract observed with
        | Error issue -> Error issue
        | Ok () ->
            let expected = contract.Payloads
            match payloads |> List.groupBy _.Path |> List.tryFind (fun (_, rows) -> rows.Length > 1) with
            | Some(path, _) -> Error(DuplicateObservedPayload path)
            | None ->
                match expected |> List.tryFind (fun want ->
                    not (payloads |> List.exists (fun item -> item.Path = want.Path))) with
                | Some missing -> Error(MissingPayload missing.Path)
                | None ->
                    match payloads |> List.tryFind (fun item ->
                        not (expected |> List.exists (fun want -> want.Path = item.Path))) with
                    | Some extra -> Error(UnexpectedPayload extra.Path)
                    | None ->
                        expected
                        |> List.tryPick (fun want ->
                            let item = payloads |> List.find (fun item -> item.Path = want.Path)
                            if obj.ReferenceEquals(item.Bytes, null) || item.Bytes <> want.Bytes then
                                Some(WrongBytes want.Path)
                            else None)
                        |> function Some issue -> Error issue | None -> Ok ()
