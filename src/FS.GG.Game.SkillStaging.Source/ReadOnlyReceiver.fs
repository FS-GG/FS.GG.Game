namespace FS.GG.Game.SkillStaging

open System
open System.IO

/// Linux-only read-only observation of a staged receiver. Paths below the
/// receiver are opened relative to held directories with O_NOFOLLOW.
module ReadOnlyReceiver =
    type Observation = {
        Entries: ReceiverContract.Entry list
        Payloads: ReceiverContract.Payload list
    }

    type Refusal =
        | UnsupportedPlatform
        | Unreadable of string
        | Symlink of string
        | NonRegular of string
        | Unstable of string
        | ContractRefusal of ReceiverContract.Refusal

    let private mapFailure path = function
        | LinuxDescriptors.Link -> Symlink path
        | LinuxDescriptors.NonRegular -> NonRegular path
        | LinuxDescriptors.Unreadable -> Unreadable path
        | LinuxDescriptors.Changed -> Unstable path

    let private captureCore (afterOpen: string -> unit) (receiverRoot: string) =
        if not (OperatingSystem.IsLinux()) || not BitConverter.IsLittleEndian then
            Error UnsupportedPlatform
        elif String.IsNullOrWhiteSpace receiverRoot then Error(Unreadable "receiver root")
        else
            try
                let root = Path.GetFullPath receiverRoot
                match LinuxDescriptors.openRoot root with
                | Error issue -> Error(mapFailure "." issue)
                | Ok receiver ->
                    use receiver = receiver
                    let entries = ResizeArray<ReceiverContract.Entry>()
                    let payloads = ResizeArray<ReceiverContract.Payload>()
                    let rec visit handle kind path =
                        match LinuxDescriptors.metadata handle with
                        | Error issue -> Error(mapFailure path issue)
                        | Ok before when before.Kind <> kind -> Error(Unstable path)
                        | Ok before ->
                            afterOpen path
                            let entryKind =
                                match kind with
                                | LinuxDescriptors.Directory -> ReceiverContract.Directory
                                | LinuxDescriptors.Regular -> ReceiverContract.RegularFile
                                | LinuxDescriptors.Special -> ReceiverContract.RegularFile
                            let entry: ReceiverContract.Entry =
                                { Path = path; Kind = entryKind; Mode = before.Mode
                                  UserId = before.UserId; GroupId = before.GroupId }
                            match kind with
                            | LinuxDescriptors.Special -> Error(NonRegular path)
                            | LinuxDescriptors.Regular ->
                                match LinuxDescriptors.readBytesStable ignore handle with
                                | Error issue -> Error(mapFailure path issue)
                                | Ok bytes ->
                                    match LinuxDescriptors.metadata handle with
                                    | Error issue -> Error(mapFailure path issue)
                                    | Ok after when after <> before -> Error(Unstable path)
                                    | Ok _ ->
                                        entries.Add entry
                                        payloads.Add { Path = path; Bytes = bytes }
                                        Ok ()
                            | LinuxDescriptors.Directory ->
                                match LinuxDescriptors.stamp handle with
                                | Error issue -> Error(mapFailure path issue)
                                | Ok beforeStamp ->
                                    match LinuxDescriptors.namesStable ignore handle with
                                    | Error issue -> Error(mapFailure path issue)
                                    | Ok names ->
                                        let result =
                                            names
                                            |> List.fold (fun state name ->
                                                match state with
                                                | Error _ -> state
                                                | Ok () ->
                                                    let childPath = if path = "." then name else path + "/" + name
                                                    match LinuxDescriptors.openChild handle name with
                                                    | Error issue -> Error(mapFailure childPath issue)
                                                    | Ok(child, childKind) ->
                                                        use child = child
                                                        visit child childKind childPath) (Ok ())
                                        match result with
                                        | Error issue -> Error issue
                                        | Ok () ->
                                            match LinuxDescriptors.stamp handle, LinuxDescriptors.metadata handle with
                                            | Error issue, _ | _, Error issue -> Error(mapFailure path issue)
                                            | Ok afterStamp, Ok after when afterStamp <> beforeStamp || after <> before ->
                                                Error(Unstable path)
                                            | Ok _, Ok _ ->
                                                entries.Add entry
                                                Ok ()
                    match visit receiver LinuxDescriptors.Directory "." with
                    | Error issue -> Error issue
                    | Ok () ->
                        Ok { Entries = List.ofSeq entries; Payloads = List.ofSeq payloads }
            with
            | :? IOException as issue -> Error(Unreadable issue.Message)
            | :? UnauthorizedAccessException as issue -> Error(Unreadable issue.Message)
            | :? ArgumentException as issue -> Error(Unreadable issue.Message)
            | :? DllNotFoundException
            | :? EntryPointNotFoundException -> Error UnsupportedPlatform

    /// Return independent metadata and bytes from held receiver descriptors.
    let observe receiverRoot = captureCore ignore receiverRoot

    /// Capture without writing, then compare exact paths, metadata, and bytes.
    let verify contract receiverRoot =
        match observe receiverRoot with
        | Error issue -> Error issue
        | Ok observed ->
            ReceiverContract.verifyComplete contract observed.Entries observed.Payloads
            |> Result.mapError ContractRefusal

    // Test seam runs after a descriptor has been opened and typed, before its
    // children or file bytes are observed.
    let internal verifyWithOpenHook afterOpen contract receiverRoot =
        match captureCore afterOpen receiverRoot with
        | Error issue -> Error issue
        | Ok observed ->
            ReceiverContract.verifyComplete contract observed.Entries observed.Payloads
            |> Result.mapError ContractRefusal
