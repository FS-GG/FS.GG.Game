namespace FS.GG.Wasm.Browser

open System
open System.Text
open FS.GG.Wasm.Contracts

type AdmissionIssue =
    | ArtifactTooLarge of maximum: int * actual: int
    | MalformedModule of offset: int * detail: string
    | UnsupportedFeature of detail: string
    | ImportForbidden of moduleName: string * itemName: string
    | StartFunctionForbidden
    | MissingExport of name: string
    | UnexpectedExport of name: string
    | ExportKindMismatch of name: string
    | ExportSignatureMismatch of name: string
    | FunctionCodeCountMismatch
    | MemoryDeclarationInvalid of detail: string
    | TableDeclarationInvalid of detail: string

type AdmissionEvidence =
    { Path: CompatibilityPath
      ArtifactBytes: int
      InitialMemoryPages: uint32
      MaximumMemoryPages: uint32
      ExportNames: string list
      FunctionCount: int }

module private Binary =
    type Reader = { Bytes: byte array; mutable Position: int; Limit: int }

    let fail reader detail = Error(MalformedModule(reader.Position, detail))

    let byte reader =
        if reader.Position >= reader.Limit then fail reader "unexpected end of input"
        else
            let value = reader.Bytes[reader.Position]
            reader.Position <- reader.Position + 1
            Ok value

    let u32 reader =
        let mutable value = 0u
        let mutable shift = 0
        let mutable count = 0
        let mutable complete = false
        let mutable issue = None
        while not complete && issue.IsNone && count < 5 do
            match byte reader with
            | Error error -> issue <- Some error
            | Ok part ->
                let payload = uint32 (part &&& 0x7fuy)
                if count = 4 && payload > 0x0fu then issue <- Some(MalformedModule(reader.Position - 1, "u32 LEB overflow"))
                else
                    value <- value ||| (payload <<< shift)
                    count <- count + 1
                    if part &&& 0x80uy = 0uy then
                        complete <- true
                    else shift <- shift + 7
        match issue with
        | Some error -> Error error
        | None when not complete -> fail reader "unterminated u32 LEB"
        | None -> Ok value

    let skipLeb reader maximumBytes =
        let mutable count = 0
        let mutable complete = false
        let mutable issue = None
        while not complete && issue.IsNone && count < maximumBytes do
            match byte reader with
            | Error error -> issue <- Some error
            | Ok part -> count <- count + 1; complete <- part &&& 0x80uy = 0uy
        match issue with
        | Some error -> Error error
        | None when not complete -> fail reader "oversized signed LEB"
        | None -> Ok()

    let skip reader count =
        if count < 0 || count > reader.Limit - reader.Position then fail reader "immediate exceeds function body"
        else reader.Position <- reader.Position + count; Ok()

    let private continuation value = value >= 0x80uy && value <= 0xbfuy

    let private strictUtf8 (reader: Reader) length =
        let start = reader.Position
        let finish = start + length
        let mutable position = start
        let mutable issue = None
        let require count =
            if position + count >= finish then
                issue <- Some(MalformedModule(position, "incomplete UTF-8 sequence"))
                false
            else true
        while position < finish && issue.IsNone do
            let first = reader.Bytes[position]
            if first <= 0x7fuy then
                position <- position + 1
            elif first >= 0xc2uy && first <= 0xdfuy then
                if require 1 then
                    if continuation reader.Bytes[position + 1] then position <- position + 2
                    else issue <- Some(MalformedModule(position + 1, "invalid UTF-8 continuation byte"))
            elif first = 0xe0uy then
                if require 2 then
                    let second, third = reader.Bytes[position + 1], reader.Bytes[position + 2]
                    if second < 0xa0uy || second > 0xbfuy then issue <- Some(MalformedModule(position + 1, "overlong UTF-8 sequence"))
                    elif not (continuation third) then issue <- Some(MalformedModule(position + 2, "invalid UTF-8 continuation byte"))
                    else position <- position + 3
            elif (first >= 0xe1uy && first <= 0xecuy) || (first >= 0xeeuy && first <= 0xefuy) then
                if require 2 then
                    if not (continuation reader.Bytes[position + 1]) then issue <- Some(MalformedModule(position + 1, "invalid UTF-8 continuation byte"))
                    elif not (continuation reader.Bytes[position + 2]) then issue <- Some(MalformedModule(position + 2, "invalid UTF-8 continuation byte"))
                    else position <- position + 3
            elif first = 0xeduy then
                if require 2 then
                    let second, third = reader.Bytes[position + 1], reader.Bytes[position + 2]
                    if second < 0x80uy || second > 0x9fuy then issue <- Some(MalformedModule(position + 1, "UTF-8 surrogate is forbidden"))
                    elif not (continuation third) then issue <- Some(MalformedModule(position + 2, "invalid UTF-8 continuation byte"))
                    else position <- position + 3
            elif first = 0xf0uy then
                if require 3 then
                    let second = reader.Bytes[position + 1]
                    if second < 0x90uy || second > 0xbfuy then issue <- Some(MalformedModule(position + 1, "overlong UTF-8 sequence"))
                    elif not (continuation reader.Bytes[position + 2]) then issue <- Some(MalformedModule(position + 2, "invalid UTF-8 continuation byte"))
                    elif not (continuation reader.Bytes[position + 3]) then issue <- Some(MalformedModule(position + 3, "invalid UTF-8 continuation byte"))
                    else position <- position + 4
            elif first >= 0xf1uy && first <= 0xf3uy then
                if require 3 then
                    if not (continuation reader.Bytes[position + 1]) then issue <- Some(MalformedModule(position + 1, "invalid UTF-8 continuation byte"))
                    elif not (continuation reader.Bytes[position + 2]) then issue <- Some(MalformedModule(position + 2, "invalid UTF-8 continuation byte"))
                    elif not (continuation reader.Bytes[position + 3]) then issue <- Some(MalformedModule(position + 3, "invalid UTF-8 continuation byte"))
                    else position <- position + 4
            elif first = 0xf4uy then
                if require 3 then
                    let second = reader.Bytes[position + 1]
                    if second < 0x80uy || second > 0x8fuy then issue <- Some(MalformedModule(position + 1, "UTF-8 code point exceeds U+10FFFF"))
                    elif not (continuation reader.Bytes[position + 2]) then issue <- Some(MalformedModule(position + 2, "invalid UTF-8 continuation byte"))
                    elif not (continuation reader.Bytes[position + 3]) then issue <- Some(MalformedModule(position + 3, "invalid UTF-8 continuation byte"))
                    else position <- position + 4
            else
                issue <- Some(MalformedModule(position, "invalid or overlong UTF-8 leading byte"))
        match issue with
        | Some error -> Error error
        | None -> Ok(Encoding.UTF8.GetString(reader.Bytes, start, length))

    let name reader =
        match u32 reader with
        | Error error -> Error error
        | Ok length when length > 65536u -> fail reader "name exceeds 65536 bytes"
        | Ok length ->
            let length = int length
            if length > reader.Limit - reader.Position then fail reader "name exceeds section"
            else
                match strictUtf8 reader length with
                | Error error -> Error error
                | Ok value -> reader.Position <- reader.Position + length; Ok value

    let vectorCount reader label =
        match u32 reader with
        | Ok count when count <= 4096u -> Ok(int count)
        | Ok _ -> fail reader (label + " count exceeds 4096")
        | Error error -> Error error

    let subReader reader length =
        if length > uint32 (reader.Limit - reader.Position) then fail reader "section length exceeds module"
        else
            let limit = reader.Position + int length
            let child = { Bytes = reader.Bytes; Position = reader.Position; Limit = limit }
            reader.Position <- limit
            Ok child

    let expectEnd reader =
        if reader.Position = reader.Limit then Ok()
        else fail reader "trailing bytes in section"

module private Parser =
    open Binary

    type FuncType = byte list * byte list
    type Export = { Name: string; Kind: byte; Index: uint32 }
    type State =
        { Types: ResizeArray<FuncType>
          Functions: ResizeArray<uint32>
          Exports: ResizeArray<Export>
          mutable ImportedFunctions: uint32
          mutable Memories: (uint32 * uint32) list
          mutable Tables: (uint32 * uint32) list
          mutable CodeCount: int
          mutable HasStart: bool }

    let addError (issues: ResizeArray<AdmissionIssue>) result =
        match result with Ok value -> Some value | Error error -> issues.Add error; None

    let limits reader =
        match u32 reader with
        | Error error -> Error error
        | Ok 1u ->
            match u32 reader, u32 reader with
            | Ok minimum, Ok maximum when minimum <= maximum -> Ok(minimum, maximum)
            | Ok _, Ok _ -> fail reader "minimum exceeds maximum"
            | Error error, _ | _, Error error -> Error error
        | Ok 0u -> fail reader "finite maximum is required"
        | Ok _ -> fail reader "unsupported limits flags"

    let valType reader =
        match byte reader with
        | Ok (0x7fuy | 0x7euy | 0x7duy | 0x7cuy as value) -> Ok value
        | Ok value -> Error(UnsupportedFeature($"value type 0x{value:x2}"))
        | Error error -> Error error

    let parseType state reader =
        match vectorCount reader "type" with
        | Error error -> Error error
        | Ok count ->
            let mutable issue = None
            for _ in 1..count do
                if issue.IsNone then
                    match byte reader with
                    | Ok 0x60uy ->
                        match vectorCount reader "parameter" with
                        | Error error -> issue <- Some error
                        | Ok pc ->
                            let parameters = ResizeArray<byte>()
                            for _ in 1..pc do
                                if issue.IsNone then match valType reader with Ok v -> parameters.Add v | Error e -> issue <- Some e
                            match vectorCount reader "result" with
                            | Error error when issue.IsNone -> issue <- Some error
                            | Ok rc when issue.IsNone && rc <= 1 ->
                                let results = ResizeArray<byte>()
                                for _ in 1..rc do
                                    if issue.IsNone then match valType reader with Ok v -> results.Add v | Error e -> issue <- Some e
                                if issue.IsNone then state.Types.Add(List.ofSeq parameters, List.ofSeq results)
                            | Ok _ when issue.IsNone -> issue <- Some(UnsupportedFeature "multi-value results")
                            | _ -> ()
                    | Ok _ -> issue <- Some(UnsupportedFeature "non-function type")
                    | Error error -> issue <- Some error
            match issue with Some error -> Error error | None -> expectEnd reader

    let parseImports (state: State) (reader: Reader) (issues: ResizeArray<AdmissionIssue>) =
        match vectorCount reader "import" with
        | Error error -> Error error
        | Ok count ->
            for _ in 1..count do
                match name reader, name reader, byte reader with
                | Ok moduleName, Ok itemName, Ok kind ->
                    issues.Add(ImportForbidden(moduleName, itemName))
                    match kind with
                    | 0uy -> match u32 reader with Ok _ -> state.ImportedFunctions <- state.ImportedFunctions + 1u | Error e -> issues.Add e
                    | 1uy -> match byte reader, limits reader with Ok _, Ok _ -> () | Error e, _ | _, Error e -> issues.Add e
                    | 2uy -> match limits reader with Ok _ -> () | Error e -> issues.Add e
                    | 3uy -> match valType reader, byte reader with Ok _, Ok _ -> () | Error e, _ | _, Error e -> issues.Add e
                    | _ -> issues.Add(MalformedModule(reader.Position - 1, "invalid import kind"))
                | Error e, _, _ | _, Error e, _ | _, _, Error e -> issues.Add e
            expectEnd reader

    let parseFunctions state reader =
        match vectorCount reader "function" with
        | Error error -> Error error
        | Ok count ->
            let mutable issue = None
            for _ in 1..count do
                if issue.IsNone then match u32 reader with Ok index -> state.Functions.Add index | Error e -> issue <- Some e
            match issue with Some error -> Error error | None -> expectEnd reader

    let parseTables state reader =
        match vectorCount reader "table" with
        | Error error -> Error error
        | Ok count ->
            let mutable issue = None
            for _ in 1..count do
                if issue.IsNone then
                    match byte reader, limits reader with
                    | Ok 0x70uy, Ok bounds -> state.Tables <- bounds :: state.Tables
                    | Ok value, Ok _ -> issue <- Some(UnsupportedFeature($"reference type 0x{value:x2}"))
                    | Error e, _ | _, Error e -> issue <- Some e
            match issue with Some error -> Error error | None -> expectEnd reader

    let parseMemories state reader =
        match vectorCount reader "memory" with
        | Error error -> Error error
        | Ok count ->
            let mutable issue = None
            for _ in 1..count do
                if issue.IsNone then match limits reader with Ok bounds -> state.Memories <- bounds :: state.Memories | Error e -> issue <- Some e
            match issue with Some error -> Error error | None -> expectEnd reader

    let parseExports state reader =
        match vectorCount reader "export" with
        | Error error -> Error error
        | Ok count ->
            let mutable issue = None
            for _ in 1..count do
                if issue.IsNone then
                    match name reader, byte reader, u32 reader with
                    | Ok n, Ok k, Ok i when k <= 3uy -> state.Exports.Add { Name=n; Kind=k; Index=i }
                    | Ok _, Ok _, Ok _ -> issue <- Some(MalformedModule(reader.Position, "invalid export kind"))
                    | Error e, _, _ | _, Error e, _ | _, _, Error e -> issue <- Some e
            match issue with Some error -> Error error | None -> expectEnd reader

    let parseCode state reader =
        match vectorCount reader "code" with
        | Error error -> Error error
        | Ok count ->
            state.CodeCount <- count
            let mutable issue = None
            for _ in 1..count do
                if issue.IsNone then
                    match u32 reader with
                    | Error e -> issue <- Some e
                    | Ok size ->
                        match subReader reader size with
                        | Error e -> issue <- Some e
                        | Ok body ->
                            // Validate the bounded local declaration prefix. The browser
                            // compiler remains a second, independent syntax control.
                            match vectorCount body "local" with
                            | Error e -> issue <- Some e
                            | Ok groups ->
                                for _ in 1..groups do
                                    if issue.IsNone then
                                        match u32 body, valType body with
                                        | Ok n, Ok _ when n <= 65536u -> ()
                                        | Ok _, Ok _ -> issue <- Some(MalformedModule(body.Position, "local count exceeds 65536"))
                                        | Error e, _ | _, Error e -> issue <- Some e
                                if issue.IsNone then
                                    while issue.IsNone && body.Position < body.Limit do
                                        match byte body with
                                        | Error e -> issue <- Some e
                                        | Ok opcode when (opcode >= 0x06uy && opcode <= 0x0auy) || opcode=0x12uy || opcode=0x13uy || opcode=0x1cuy || opcode>=0xd0uy || opcode=0xfbuy || opcode=0xfcuy || opcode=0xfduy || opcode=0xfeuy ->
                                            issue <- Some(UnsupportedFeature($"instruction opcode 0x{opcode:x2}"))
                                        | Ok opcode when [0x0cuy;0x0duy;0x10uy;0x20uy;0x21uy;0x22uy;0x23uy;0x24uy] |> List.contains opcode ->
                                            match u32 body with Error e -> issue <- Some e | Ok _ -> ()
                                        | Ok 0x0euy ->
                                            match vectorCount body "branch table" with
                                            | Error e -> issue <- Some e
                                            | Ok count ->
                                                for _ in 0..count do if issue.IsNone then match u32 body with Error e -> issue <- Some e | Ok _ -> ()
                                        | Ok 0x11uy ->
                                            match u32 body, u32 body with Error e,_ | _,Error e -> issue <- Some e | _ -> ()
                                        | Ok opcode when opcode=0x02uy || opcode=0x03uy || opcode=0x04uy ->
                                            match byte body with
                                            | Error e -> issue <- Some e
                                            | Ok block when block=0x40uy || block=0x7fuy || block=0x7euy || block=0x7duy || block=0x7cuy -> ()
                                            | Ok block when block &&& 0x80uy <> 0uy -> match skipLeb body 4 with Error e -> issue <- Some e | Ok () -> ()
                                            | Ok _ -> ()
                                        | Ok opcode when opcode>=0x28uy && opcode<=0x3euy ->
                                            match u32 body, u32 body with Error e,_ | _,Error e -> issue <- Some e | _ -> ()
                                        | Ok (0x3fuy | 0x40uy) -> match u32 body with Error e -> issue <- Some e | Ok _ -> ()
                                        | Ok 0x41uy -> match skipLeb body 5 with Error e -> issue <- Some e | Ok () -> ()
                                        | Ok 0x42uy -> match skipLeb body 10 with Error e -> issue <- Some e | Ok () -> ()
                                        | Ok 0x43uy -> match skip body 4 with Error e -> issue <- Some e | Ok () -> ()
                                        | Ok 0x44uy -> match skip body 8 with Error e -> issue <- Some e | Ok () -> ()
                                        | Ok _ -> ()
            match issue with Some error -> Error error | None -> expectEnd reader

    let inspect (descriptor: CompatibilityDescriptor) configuredMaximumMemoryPages (artifact: byte array) =
        let reader = { Bytes=artifact; Position=0; Limit=artifact.Length }
        let issues = ResizeArray<AdmissionIssue>()
        let state = { Types=ResizeArray(); Functions=ResizeArray(); Exports=ResizeArray(); ImportedFunctions=0u; Memories=[]; Tables=[]; CodeCount=0; HasStart=false }
        let magic = [|0uy;97uy;115uy;109uy;1uy;0uy;0uy;0uy|]
        if artifact.Length < 8 || artifact[0..7] <> magic then issues.Add(MalformedModule(0, "invalid magic or version")); reader.Position <- reader.Limit
        else reader.Position <- 8
        let rank id = match id with 1uy->1 | 2uy->2 | 3uy->3 | 4uy->4 | 5uy->5 | 6uy->6 | 7uy->7 | 8uy->8 | 9uy->9 | 12uy->10 | 10uy->11 | 11uy->12 | _->0
        let mutable lastSection = 0
        while reader.Position < reader.Limit do
            match byte reader, u32 reader with
            | Ok id, Ok length ->
                match subReader reader length with
                | Error e -> issues.Add e
                | Ok section ->
                    let sectionRank = rank id
                    if id <> 0uy && sectionRank = 0 then issues.Add(MalformedModule(section.Position, "unknown section id"))
                    elif id <> 0uy && sectionRank <= lastSection then issues.Add(MalformedModule(section.Position, "section order or duplication"))
                    else
                        if id <> 0uy then lastSection <- sectionRank
                        let result =
                            match id with
                            | 0uy ->
                                match name section with
                                | Error e -> Error e
                                | Ok "target_features" ->
                                    match vectorCount section "target feature" with
                                    | Error e -> Error e
                                    | Ok count ->
                                        let restricted = set ["atomics";"simd128";"relaxed-simd";"memory64";"reference-types";"gc";"exception-handling";"tail-call";"multivalue";"multimemory";"bulk-memory"]
                                        let mutable featureIssue = None
                                        for _ in 1..count do
                                            if featureIssue.IsNone then
                                                match byte section, name section with
                                                | Ok 0x2buy, Ok feature when restricted.Contains(feature.ToLowerInvariant()) -> featureIssue <- Some(UnsupportedFeature($"target feature enabled: {feature}"))
                                                | Ok (0x2buy | 0x2duy), Ok _ -> ()
                                                | Ok _, Ok _ -> featureIssue <- Some(MalformedModule(section.Position, "invalid target feature prefix"))
                                                | Error e, _ | _, Error e -> featureIssue <- Some e
                                        match featureIssue with Some e -> Error e | None -> expectEnd section
                                | Ok _ -> section.Position <- section.Limit; Ok()
                            | 1uy -> parseType state section
                            | 2uy -> parseImports state section issues
                            | 3uy -> parseFunctions state section
                            | 4uy -> parseTables state section
                            | 5uy -> parseMemories state section
                            | 7uy -> parseExports state section
                            | 8uy -> state.HasStart <- true; match u32 section with Ok _ -> expectEnd section | Error e -> Error e
                            | 12uy -> Error(UnsupportedFeature "data-count section requires bulk-memory")
                            | 10uy -> parseCode state section
                            | _ -> section.Position <- section.Limit; Ok()
                        match result with Error e -> issues.Add e | Ok () -> ()
            | Error e, _ | _, Error e -> issues.Add e; reader.Position <- reader.Limit

        if state.Functions.Count <> state.CodeCount then issues.Add FunctionCodeCountMismatch
        if state.HasStart then issues.Add StartFunctionForbidden
        if state.Memories.Length <> 1 then issues.Add(MemoryDeclarationInvalid "exactly one defined memory is required")
        else
            let initial, maximum = state.Memories.Head
            if maximum > uint32 (min descriptor.Limits.MaximumMemoryPages configuredMaximumMemoryPages) then issues.Add(MemoryDeclarationInvalid "memory maximum exceeds configured limit")
            if initial > maximum then issues.Add(MemoryDeclarationInvalid "memory initial exceeds maximum")
        match descriptor.Tables, state.Tables with
        | NoTables, [] -> ()
        | NoTables, _ -> issues.Add(TableDeclarationInvalid "tables are forbidden")
        | AtMostOneFiniteTable maximum, tables ->
            if tables.Length > 1 then issues.Add(TableDeclarationInvalid "at most one table is allowed")
            elif tables |> List.exists (fun (_, m) -> m > uint32 maximum) then issues.Add(TableDeclarationInvalid "table maximum exceeds profile")

        let required = (descriptor.Abi.MemoryExport, 2uy, None) :: (descriptor.Abi.Signatures |> List.map (fun (name, signature) -> name, 0uy, Some signature))
        for name, kind, signature in required do
            match state.Exports |> Seq.tryFind (fun e -> e.Name = name) with
            | None -> issues.Add(MissingExport name)
            | Some export when export.Kind <> kind -> issues.Add(ExportKindMismatch name)
            | Some export ->
                match signature with
                | None -> if export.Index <> 0u then issues.Add(ExportKindMismatch name)
                | Some expected ->
                    let functionIndex = int64 export.Index - int64 state.ImportedFunctions
                    if functionIndex < 0L || functionIndex >= int64 state.Functions.Count then issues.Add(ExportSignatureMismatch name)
                    else
                        let typeIndex = state.Functions[int functionIndex]
                        if typeIndex >= uint32 state.Types.Count then issues.Add(ExportSignatureMismatch name)
                        else
                            let parameters, results = state.Types[int typeIndex]
                            let expectedParameters = expected.Parameters |> List.map (fun _ -> 0x7fuy)
                            let expectedResults = expected.Results |> List.map (fun _ -> 0x7fuy)
                            if parameters <> expectedParameters || results <> expectedResults then issues.Add(ExportSignatureMismatch name)

        let allowed =
            required |> List.map (fun (name,_,_) -> name) |> Set.ofList
            |> Set.union (match descriptor.AdditionalExports with OnlyNamedGlobals names -> Set.ofList names | _ -> Set.empty)
        match descriptor.AdditionalExports with
        | OnlyNamedGlobals _ ->
            for export in state.Exports do
                if not (allowed.Contains export.Name) then issues.Add(UnexpectedExport export.Name)
                elif required |> List.exists (fun (n,_,_) -> n=export.Name) |> not && export.Kind <> 3uy then issues.Add(ExportKindMismatch export.Name)
        | AdditionalGlobalsAllowed ->
            for export in state.Exports do
                if not (allowed.Contains export.Name) && export.Kind <> 3uy then issues.Add(UnexpectedExport export.Name)
        | LegacyRequiredPresence -> issues.Add(UnsupportedFeature "inventory-only legacy admission")
        state, List.ofSeq issues

[<RequireQualifiedAccess>]
module Admission =
    let inspect (configuration: ValidatedConfiguration) (artifact: byte array) =
        let descriptor = Validation.descriptor configuration
        let limits = (Validation.configuration configuration).Limits
        if obj.ReferenceEquals(artifact, null) then Error [ MalformedModule(0, "artifact is null") ]
        elif artifact.Length > limits.MaximumArtifactBytes then Error [ ArtifactTooLarge(limits.MaximumArtifactBytes, artifact.Length) ]
        elif descriptor.Support <> SharedRuntimeCandidate || descriptor.Admission <> StrictBeforeInstantiation then
            Error [ UnsupportedFeature "compatibility path is inventory-only" ]
        else
            let state, issues = Parser.inspect descriptor limits.MaximumMemoryPages artifact
            if not issues.IsEmpty then Error issues
            else
                let initial, maximum = state.Memories.Head
                Ok { Path=descriptor.Path; ArtifactBytes=artifact.Length; InitialMemoryPages=initial; MaximumMemoryPages=maximum; ExportNames=state.Exports |> Seq.map (fun e -> e.Name) |> List.ofSeq; FunctionCount=state.Functions.Count }
