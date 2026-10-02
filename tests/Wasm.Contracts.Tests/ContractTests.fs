module Wasm.Contracts.Tests.ContractTests

open Expecto
open FS.GG.Wasm.Contracts
open System.IO
open System.Text.Json

let private sha character = String.replicate 64 character

let private configuration (descriptor: CompatibilityDescriptor) : CandidateConfiguration =
    { Path = descriptor.Path
      ArtifactSha256 = sha "a"
      ConfigurationSha256 = sha "b"
      Limits = descriptor.Limits
      Deadline = descriptor.Deadline
      Scheduling = descriptor.Scheduling
      Replacement = descriptor.Replacement }

let private boundary (descriptor: CompatibilityDescriptor) : CandidateConfigurationBoundary =
    { Path =
        match descriptor.Path with
        | BarProtected -> "bar-protected"
        | Sc2ImportedStrict -> "sc2-imported-strict"
        | Sc2LegacyDirectUrl -> "sc2-legacy-direct-url"
      ArtifactSha256 = sha "a"
      ConfigurationSha256 = sha "b"
      MaximumArtifactBytes = string descriptor.Limits.MaximumArtifactBytes
      MaximumMemoryPages = string descriptor.Limits.MaximumMemoryPages
      MaximumInputBytes = string descriptor.Limits.MaximumInputBytes
      MaximumOutputBytes = string descriptor.Limits.MaximumOutputBytes
      MaximumDeadlineMilliseconds = string descriptor.Limits.MaximumDeadlineMilliseconds
      Deadline =
        match descriptor.Deadline with
        | PhaseWatchdog -> "phase-watchdog"
        | EndToEndFromEnqueue -> "end-to-end-from-enqueue"
      Scheduling =
        match descriptor.Scheduling with
        | RefuseWhileBusy -> "refuse-while-busy"
        | BoundedFifo maximum -> $"bounded-fifo:{maximum}"
      Replacement =
        match descriptor.Replacement with
        | DestructiveLoad -> "destructive-load"
        | TransactionalCandidateWithRecoveryFreeze -> "transactional-candidate-with-recovery-freeze" }

let private signature parameters results =
    { Parameters = List.replicate parameters I32
      Results = List.replicate results I32 }

let private requireOk result =
    match result with
    | Ok value -> value
    | Error issues -> failtestf "expected Ok but got %A" issues

let private jsonString (value: JsonElement) (property: string) : string =
    match value.GetProperty(property).GetString() with
    | null -> failtestf "JSON property %s was null" property
    | text -> text

[<Tests>]
let profiles =
    testList
        "profiles"
        [ testCase "the two immutable ABIs have exact names, versions, and signatures" <| fun _ ->
              Expect.equal Profiles.abiProfiles.Length 2 "only BAR and SC2 ABI identities are shared"
              let bar = Profiles.abiProfiles |> List.find (fun profile -> profile.Id = BarAbi1)
              let sc2 = Profiles.abiProfiles |> List.find (fun profile -> profile.Id = Sc2Abi10000)
              Expect.equal bar.Version 1u "BAR ABI version is preserved"
              Expect.equal sc2.Version 0x00010000u "SC2 ABI version is preserved"
              Expect.equal
                  bar.Signatures
                  [ "barc_abi_version", signature 0 1
                    "barc_alloc", signature 1 1
                    "barc_free", signature 2 0
                    "barc_initialize", signature 3 1
                    "barc_process", signature 3 1
                    "barc_shutdown", signature 0 1 ]
                  "BAR exports are exact"
              Expect.equal
                  (sc2.Signatures |> List.map fst)
                  [ "sc2c_abi_version"; "sc2c_alloc"; "sc2c_free"; "sc2c_initialize"; "sc2c_process"; "sc2c_shutdown" ]
                  "SC2 exports are exact"

          testCase "every built-in compatibility descriptor is internally valid" <| fun _ ->
              for descriptor in Profiles.compatibilityPaths do
                  Expect.isEmpty (Validation.validateDescriptor descriptor) $"{descriptor.Path} remains valid"

          testCase "BAR retains its strict bounded profile" <| fun _ ->
              let bar = Profiles.tryFind BarProtected |> Option.get
              Expect.equal bar.Admission StrictBeforeInstantiation "admission precedes instantiation"
              Expect.equal bar.Tables (AtMostOneFiniteTable 4096) "BAR finite table allowance remains"
              Expect.equal bar.Limits.MaximumMemoryPages 1024 "BAR retains 64 MiB linear memory maximum"
              Expect.equal bar.Limits.MaximumInputBytes (64 * 1024) "BAR input maximum remains"
              Expect.equal bar.Spans.EmptyOutput RejectEmptyOutput "BAR success requires output"
              Expect.equal bar.Deadline PhaseWatchdog "BAR watchdog remains per phase"
              Expect.equal bar.Scheduling RefuseWhileBusy "BAR remains single-pending"
              Expect.equal bar.Replacement DestructiveLoad "BAR load remains destructive"

          testCase "strict SC2 retains admission, capacity, and transactional policy" <| fun _ ->
              let sc2 = Profiles.tryFind Sc2ImportedStrict |> Option.get
              Expect.equal sc2.Tables NoTables "strict imported SC2 rejects tables"
              Expect.equal sc2.Limits.MaximumMemoryPages 128 "SC2 retains 8 MiB linear memory maximum"
              Expect.equal sc2.Limits.MaximumInputBytes (256 * 1024) "SC2 input maximum remains"
              Expect.equal sc2.Spans.EmptyOutput PermitEmptyOutput "SC2 worker layer permits empty output"
              Expect.equal sc2.Deadline EndToEndFromEnqueue "queue residence is charged"
              Expect.equal sc2.Scheduling (BoundedFifo 4) "SC2 bounded FIFO remains"
              Expect.equal sc2.Replacement TransactionalCandidateWithRecoveryFreeze "SC2 replacement stays transactional"
              Expect.equal
                  sc2.RoleCapacities
                  [ { Role = Controller; MaximumInstances = 1 }
                    { Role = Advisor; MaximumInstances = 2 }
                    { Role = AggregateCandidate; MaximumInstances = 1 } ]
                  "roles express capacity only"

          testCase "legacy SC2 is inventoried but cannot configure a shared runtime" <| fun _ ->
              let legacy = Profiles.tryFind Sc2LegacyDirectUrl |> Option.get
              Expect.equal legacy.Admission LegacyAfterInstantiation "legacy validation occurs after instantiation"
              Expect.isFalse legacy.StartFunctionForbidden "legacy start may already have executed"
              Expect.isFalse legacy.FreeSignatureRequired "legacy required-export check omits free"
              match Validation.validateConfiguration (configuration legacy) with
              | Error issues -> Expect.contains issues (InventoryOnlyPathNotAdmissible Sc2LegacyDirectUrl) "migration inventory cannot become an unsafe bypass"
              | Ok _ -> failtest "legacy path unexpectedly admitted"

          testCase "valid candidate keeps artifact and configuration digests separate" <| fun _ ->
              let descriptor = Profiles.tryFind Sc2ImportedStrict |> Option.get
              match Validation.validateConfiguration (configuration descriptor) with
              | Error issues -> failtestf "valid configuration was rejected: %A" issues
              | Ok validated ->
                  Expect.equal (Validation.configuration validated).ArtifactSha256 (sha "a") "artifact digest remains"
                  Expect.equal (Validation.configuration validated).ConfigurationSha256 (sha "b") "configuration digest remains"
                  Expect.equal (Validation.descriptor validated).Path Sc2ImportedStrict "profile remains bound"

          testCase "configuration refuses malformed digests, oversized limits, and policy substitution" <| fun _ ->
              let descriptor = Profiles.tryFind BarProtected |> Option.get
              let candidate =
                  { configuration descriptor with
                      ArtifactSha256 = "ABC"
                      Limits = { descriptor.Limits with MaximumMemoryPages = 1025 }
                      Deadline = EndToEndFromEnqueue
                      Replacement = TransactionalCandidateWithRecoveryFreeze }
              match Validation.validateConfiguration candidate with
              | Ok _ -> failtest "inconsistent configuration unexpectedly admitted"
              | Error issues ->
                  Expect.contains issues (InvalidSha256 "artifactSha256") "digest format is closed"
                  Expect.contains issues (LimitExceedsProfile("maximumMemoryPages", 1024, 1025)) "profile ceiling is enforced"
                  Expect.contains issues (PolicyDiffersFromProfile "deadline") "deadline semantics cannot drift"
                  Expect.contains issues (PolicyDiffersFromProfile "replacement") "replacement semantics cannot drift"

          testCase "descriptor validation returns issues for short, long, and wrong-shaped signature lists" <| fun _ ->
              let descriptor = Profiles.tryFind BarProtected |> Option.get
              let issue = InvalidDescriptor "all ABI signatures must use the frozen i32 shapes"
              let short = { descriptor with Abi = { descriptor.Abi with Signatures = descriptor.Abi.Signatures |> List.take 5 } }
              let long = { descriptor with Abi = { descriptor.Abi with Signatures = descriptor.Abi.Signatures @ [ "extra", signature 0 0 ] } }
              let wrongShape =
                  { descriptor with
                      Abi =
                        { descriptor.Abi with
                            Signatures =
                                descriptor.Abi.Signatures
                                |> List.mapi (fun index (name, value) -> if index = 2 then name, signature 1 0 else name, value) } }
              Expect.contains (Validation.validateDescriptor short) issue "short lists are rejected without List.zip throwing"
              Expect.contains (Validation.validateDescriptor long) issue "long lists are rejected without List.zip throwing"
              Expect.contains (Validation.validateDescriptor wrongShape) issue "wrong i32 shapes remain rejected"

          testCase "raw JavaScript boundary fields fail closed before typed configuration construction" <| fun _ ->
              let descriptor = Profiles.tryFind Sc2ImportedStrict |> Option.get
              Expect.isOk (Validation.validateBoundary (boundary descriptor)) "complete raw fields validate"
              let missing = { boundary descriptor with Path = Unchecked.defaultof<string> }
              Expect.equal (Validation.validateBoundary missing) (Error [ MalformedBoundaryField "path" ]) "missing fields are rejected"
              let malformed = { boundary descriptor with MaximumMemoryPages = "128.0" }
              Expect.equal
                  (Validation.validateBoundary malformed)
                  (Error [ MalformedBoundaryField "maximumMemoryPages" ])
                  "numeric coercion is rejected"
              Expect.equal
                  (Validation.validateBoundary Unchecked.defaultof<CandidateConfigurationBoundary>)
                  (Error [ MalformedBoundaryField "configuration" ])
                  "null forged objects are rejected" ]

[<Tests>]
let spansAndCleanup =
    testList
        "span and cleanup decisions"
        [ testCase "BAR retains alignment while SC2 accepts unaligned descriptor and output spans" <| fun _ ->
              let bar = (Profiles.tryFind BarProtected |> Option.get).Spans
              let sc2 = (Profiles.tryFind Sc2ImportedStrict |> Option.get).Spans
              Expect.equal (Spans.validate bar 65536UL Output { Pointer = 0UL; Length = 0UL }) (Error [ EmptyOutputRejected ]) "BAR rejects empty success"
              Expect.isOk (Spans.validate sc2 65536UL Output { Pointer = 0UL; Length = 0UL }) "SC2 permits empty worker output"
              Expect.equal (Spans.validate bar 65536UL Output { Pointer = 3UL; Length = 4UL }) (Error [ MisalignedPointer ]) "BAR requires alignment"
              Expect.equal (Spans.validate bar 65536UL Descriptor { Pointer = 3UL; Length = 8UL }) (Error [ MisalignedPointer ]) "BAR descriptor alignment remains"
              Expect.isOk (Spans.validate sc2 65536UL Descriptor { Pointer = 3UL; Length = 8UL }) "SC2 descriptor has no extra alignment rule"
              Expect.isOk (Spans.validate sc2 65536UL Output { Pointer = 3UL; Length = 4UL }) "SC2 has no extra alignment rule"

          testCase "wasm32 range and memory bounds are independent guards" <| fun _ ->
              let policy = (Profiles.tryFind Sc2ImportedStrict |> Option.get).Spans
              let result = Spans.validate policy 1024UL Input { Pointer = 1000UL; Length = 50UL }
              Expect.equal result (Error [ SpanOutsideMemory ]) "bounded memory rejects the range"
              let overflow = Spans.validate policy 0x1_0000_0000UL Input { Pointer = 0xfffffff0UL; Length = 32UL }
              Expect.equal overflow (Error [ SpanEndExceedsWasm32 ]) "u32 addition cannot wrap"

          testCase "overlap and cleanup never free rejected or unknown ownership" <| fun _ ->
              let policy = (Profiles.tryFind Sc2ImportedStrict |> Option.get).Spans
              let left = Spans.validate policy 65536UL Input { Pointer = 64UL; Length = 32UL } |> requireOk
              let right = Spans.validate policy 65536UL Output { Pointer = 80UL; Length = 32UL } |> requireOk
              Expect.isTrue (Spans.overlap left right) "host-owned spans overlap"
              Expect.equal (Spans.cleanup (OwnedAndValidated left)) (FreeValidatedSpan { Pointer = 64UL; Length = 32UL }) "validated ownership may be freed"
              Expect.equal (Spans.cleanup RejectedOrAliased) SkipCleanupAndTerminate "rejected alias is never freed"
              Expect.equal (Spans.cleanup UnknownAllocatorOwnership) SkipCleanupAndTerminate "unknown allocator output is never freed" ]

[<Tests>]
let pinnedCorpus =
    testList
        "pinned compatibility corpus"
        [ testCase "baseline source and decision fixtures are versioned and attributed" <| fun _ ->
              let fixture name = Path.Combine(System.AppContext.BaseDirectory, "fixtures", name)
              use baselines = JsonDocument.Parse(File.ReadAllText(fixture "baselines.v1.json"))
              use decisions = JsonDocument.Parse(File.ReadAllText(fixture "expected-decisions.v1.json"))
              Expect.equal (jsonString baselines.RootElement "schema") "fsgg.wasm.compatibility-baselines/v1" "baseline schema is pinned"
              let products = baselines.RootElement.GetProperty("baselines").EnumerateArray() |> Seq.map (fun value -> jsonString value "product") |> Set.ofSeq
              Expect.equal products (set [ "BAR"; "SC2" ]) "both protected consumers are attributed"
              for baseline in baselines.RootElement.GetProperty("baselines").EnumerateArray() do
                  Expect.equal (String.length (jsonString baseline "revision")) 40 "revision is exact"
                  Expect.isGreaterThan (baseline.GetProperty("sources").GetArrayLength()) 0 "source inventory is nonempty"
                  for source in baseline.GetProperty("sources").EnumerateArray() do
                      Expect.equal (String.length (jsonString source "sha256")) 64 "source digest is pinned"
              Expect.isGreaterThanOrEqual (decisions.RootElement.GetProperty("cases").GetArrayLength()) 16 "positive and adversarial decisions remain explicit" ]
