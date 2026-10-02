namespace FS.GG.Wasm.Contracts

open System

type WasmValueType =
    | I32

type FunctionSignature =
    { Parameters: WasmValueType list
      Results: WasmValueType list }

type AbiProfileId =
    | BarAbi1
    | Sc2Abi10000

type CompatibilityPath =
    | BarProtected
    | Sc2ImportedStrict
    | Sc2LegacyDirectUrl

type CompatibilitySupport =
    | SharedRuntimeCandidate
    | InventoryOnlyMigrationRequired

type AdmissionPolicy =
    | StrictBeforeInstantiation
    | LegacyAfterInstantiation

type TablePolicy =
    | NoTables
    | AtMostOneFiniteTable of maximumElements: int

type AdditionalExportPolicy =
    | OnlyNamedGlobals of names: string list
    | AdditionalGlobalsAllowed
    | LegacyRequiredPresence

type FeaturePolicy =
    | BarParserSubset
    | Sc2RestrictedWasm32
    | LegacyCompileValidationOnly

type EmptyOutputPolicy =
    | RejectEmptyOutput
    | PermitEmptyOutput

type AlignmentPolicy =
    | RequireFourByteAlignment
    | NoAdditionalAlignment

type DeadlinePolicy =
    | PhaseWatchdog
    | EndToEndFromEnqueue

type SchedulingPolicy =
    | RefuseWhileBusy
    | BoundedFifo of maximumQueuedRequests: int

type ReplacementPolicy =
    | DestructiveLoad
    | TransactionalCandidateWithRecoveryFreeze

type ProductRole =
    | Controller
    | Advisor
    | AggregateCandidate

type RoleCapacity =
    { Role: ProductRole
      MaximumInstances: int }

type ResourceLimits =
    { MaximumArtifactBytes: int
      MaximumMemoryPages: int
      MaximumInputBytes: int
      MaximumOutputBytes: int
      MaximumDeadlineMilliseconds: int }

type SpanPolicy =
    { DescriptorBytes: int
      DescriptorAlignment: AlignmentPolicy
      InputAlignment: AlignmentPolicy
      OutputAlignment: AlignmentPolicy
      EmptyOutput: EmptyOutputPolicy
      ZeroDescriptorBeforeCall: bool
      RefreshMemoryAfterEveryGuestCall: bool
      RejectHostSpanOverlap: bool
      CleanupRequiresValidatedOwnership: bool }

type AbiDescriptor =
    { Id: AbiProfileId
      Version: uint32
      MemoryExport: string
      VersionExport: string
      AllocateExport: string
      FreeExport: string
      InitializeExport: string
      ProcessExport: string
      ShutdownExport: string
      Signatures: (string * FunctionSignature) list }

type CompatibilityDescriptor =
    { Path: CompatibilityPath
      Abi: AbiDescriptor
      Support: CompatibilitySupport
      Admission: AdmissionPolicy
      ImportsForbidden: bool
      StartFunctionForbidden: bool
      FreeSignatureRequired: bool
      Tables: TablePolicy
      AdditionalExports: AdditionalExportPolicy
      Features: FeaturePolicy
      Limits: ResourceLimits
      Spans: SpanPolicy
      Deadline: DeadlinePolicy
      Scheduling: SchedulingPolicy
      Replacement: ReplacementPolicy
      RoleCapacities: RoleCapacity list }

type CandidateConfiguration =
    { Path: CompatibilityPath
      ArtifactSha256: string
      ConfigurationSha256: string
      Limits: ResourceLimits
      Deadline: DeadlinePolicy
      Scheduling: SchedulingPolicy
      Replacement: ReplacementPolicy }

type CandidateConfigurationBoundary =
    { Path: string
      ArtifactSha256: string
      ConfigurationSha256: string
      MaximumArtifactBytes: string
      MaximumMemoryPages: string
      MaximumInputBytes: string
      MaximumOutputBytes: string
      MaximumDeadlineMilliseconds: string
      Deadline: string
      Scheduling: string
      Replacement: string }

type ContractIssue =
    | UnsupportedCompatibilityPath
    | InventoryOnlyPathNotAdmissible of CompatibilityPath
    | InvalidSha256 of fieldName: string
    | NonPositiveLimit of fieldName: string
    | LimitExceedsProfile of fieldName: string * maximum: int * actual: int
    | PolicyDiffersFromProfile of fieldName: string
    | InvalidDescriptor of detail: string
    | MalformedBoundaryField of fieldName: string

type ValidatedConfiguration = private ValidatedConfiguration of CandidateConfiguration * CompatibilityDescriptor

module private ContractData =
    let i32 parameters results =
        { Parameters = List.replicate parameters I32
          Results = List.replicate results I32 }

    let signatures prefix =
        [ $"{prefix}_abi_version", i32 0 1
          $"{prefix}_alloc", i32 1 1
          $"{prefix}_free", i32 2 0
          $"{prefix}_initialize", i32 3 1
          $"{prefix}_process", i32 3 1
          $"{prefix}_shutdown", i32 0 1 ]

    let abi id version prefix =
        { Id = id
          Version = version
          MemoryExport = "memory"
          VersionExport = $"{prefix}_abi_version"
          AllocateExport = $"{prefix}_alloc"
          FreeExport = $"{prefix}_free"
          InitializeExport = $"{prefix}_initialize"
          ProcessExport = $"{prefix}_process"
          ShutdownExport = $"{prefix}_shutdown"
          Signatures = signatures prefix }

    let barAbi = abi BarAbi1 1u "barc"
    let sc2Abi = abi Sc2Abi10000 0x00010000u "sc2c"

    let safeSpans emptyOutput alignment =
        { DescriptorBytes = 8
          DescriptorAlignment = alignment
          InputAlignment = alignment
          OutputAlignment = alignment
          EmptyOutput = emptyOutput
          ZeroDescriptorBeforeCall = true
          RefreshMemoryAfterEveryGuestCall = true
          RejectHostSpanOverlap = true
          CleanupRequiresValidatedOwnership = true }

    let barLimits =
        { MaximumArtifactBytes = 8 * 1024 * 1024
          MaximumMemoryPages = 1024
          MaximumInputBytes = 64 * 1024
          MaximumOutputBytes = 64 * 1024
          MaximumDeadlineMilliseconds = 250 }

    let sc2Limits =
        { MaximumArtifactBytes = 8 * 1024 * 1024
          MaximumMemoryPages = 128
          MaximumInputBytes = 256 * 1024
          MaximumOutputBytes = 64 * 1024
          MaximumDeadlineMilliseconds = 250 }

    let sc2Roles =
        [ { Role = Controller; MaximumInstances = 1 }
          { Role = Advisor; MaximumInstances = 2 }
          { Role = AggregateCandidate; MaximumInstances = 1 } ]

    let bar =
        { Path = BarProtected
          Abi = barAbi
          Support = SharedRuntimeCandidate
          Admission = StrictBeforeInstantiation
          ImportsForbidden = true
          StartFunctionForbidden = true
          FreeSignatureRequired = true
          Tables = AtMostOneFiniteTable 4096
          AdditionalExports = OnlyNamedGlobals [ "__data_end"; "__heap_base" ]
          Features = BarParserSubset
          Limits = barLimits
          Spans = safeSpans RejectEmptyOutput RequireFourByteAlignment
          Deadline = PhaseWatchdog
          Scheduling = RefuseWhileBusy
          Replacement = DestructiveLoad
          RoleCapacities = [] }

    let sc2Strict =
        { Path = Sc2ImportedStrict
          Abi = sc2Abi
          Support = SharedRuntimeCandidate
          Admission = StrictBeforeInstantiation
          ImportsForbidden = true
          StartFunctionForbidden = true
          FreeSignatureRequired = true
          Tables = NoTables
          AdditionalExports = AdditionalGlobalsAllowed
          Features = Sc2RestrictedWasm32
          Limits = sc2Limits
          Spans = safeSpans PermitEmptyOutput NoAdditionalAlignment
          Deadline = EndToEndFromEnqueue
          Scheduling = BoundedFifo 4
          Replacement = TransactionalCandidateWithRecoveryFreeze
          RoleCapacities = sc2Roles }

    let sc2Legacy =
        { sc2Strict with
            Path = Sc2LegacyDirectUrl
            Support = InventoryOnlyMigrationRequired
            Admission = LegacyAfterInstantiation
            StartFunctionForbidden = false
            FreeSignatureRequired = false
            Tables = AtMostOneFiniteTable Int32.MaxValue
            AdditionalExports = LegacyRequiredPresence
            Features = LegacyCompileValidationOnly }

[<RequireQualifiedAccess>]
module Profiles =
    let abiProfiles = [ ContractData.barAbi; ContractData.sc2Abi ]
    let compatibilityPaths = [ ContractData.bar; ContractData.sc2Strict; ContractData.sc2Legacy ]
    let tryFind path = compatibilityPaths |> List.tryFind (fun descriptor -> descriptor.Path = path)

[<RequireQualifiedAccess>]
module Validation =
    let private positiveLimits (limits: ResourceLimits) =
        [ "maximumArtifactBytes", limits.MaximumArtifactBytes
          "maximumMemoryPages", limits.MaximumMemoryPages
          "maximumInputBytes", limits.MaximumInputBytes
          "maximumOutputBytes", limits.MaximumOutputBytes
          "maximumDeadlineMilliseconds", limits.MaximumDeadlineMilliseconds ]

    let validateDescriptor (descriptor: CompatibilityDescriptor) =
        let issues = ResizeArray<ContractIssue>()
        let requiredNames =
            [ descriptor.Abi.VersionExport
              descriptor.Abi.AllocateExport
              descriptor.Abi.FreeExport
              descriptor.Abi.InitializeExport
              descriptor.Abi.ProcessExport
              descriptor.Abi.ShutdownExport ]

        if descriptor.Abi.MemoryExport <> "memory" then
            issues.Add(InvalidDescriptor "memory export must be named memory")

        if descriptor.Abi.Signatures |> List.map fst <> requiredNames then
            issues.Add(InvalidDescriptor "ABI signatures do not match the six required exports in order")

        let expectedArities = [ 0, 1; 1, 1; 2, 0; 3, 1; 3, 1; 0, 1 ]

        if descriptor.Abi.Signatures.Length <> expectedArities.Length then
            issues.Add(InvalidDescriptor "all ABI signatures must use the frozen i32 shapes")
        elif
            List.forall2 (fun (_, signature) (parameters, results) ->
                signature.Parameters = List.replicate parameters I32
                && signature.Results = List.replicate results I32)
                descriptor.Abi.Signatures
                expectedArities
            |> not
        then
            issues.Add(InvalidDescriptor "all ABI signatures must use the frozen i32 shapes")

        for name, value in positiveLimits descriptor.Limits do
            if value <= 0 then issues.Add(NonPositiveLimit name)

        if descriptor.Spans.DescriptorBytes <> 8 then
            issues.Add(InvalidDescriptor "output descriptor must contain two little-endian u32 values")

        if not descriptor.Spans.ZeroDescriptorBeforeCall then
            issues.Add(InvalidDescriptor "new shared execution must zero the descriptor before invocation")

        if not descriptor.Spans.RefreshMemoryAfterEveryGuestCall then
            issues.Add(InvalidDescriptor "memory views must refresh after every guest call")

        if not descriptor.Spans.RejectHostSpanOverlap then
            issues.Add(InvalidDescriptor "host-owned spans must remain disjoint")

        if not descriptor.Spans.CleanupRequiresValidatedOwnership then
            issues.Add(InvalidDescriptor "cleanup must require validated non-aliased ownership")

        for capacity in descriptor.RoleCapacities do
            if capacity.MaximumInstances <= 0 then
                issues.Add(InvalidDescriptor "role capacities must be positive")

        List.ofSeq issues

    let private isSha256 (value: string) =
        not (String.IsNullOrEmpty value)
        && value.Length = 64
        && value
           |> Seq.forall (fun character ->
               (character >= '0' && character <= '9')
               || (character >= 'a' && character <= 'f'))

    let private compareLimit (issues: ResizeArray<ContractIssue>) (field: string) (maximum: int) (actual: int) =
        if actual <= 0 then issues.Add(NonPositiveLimit field)
        elif actual > maximum then issues.Add(LimitExceedsProfile(field, maximum, actual))

    let validateConfiguration (candidate: CandidateConfiguration) =
        match Profiles.tryFind candidate.Path with
        | None -> Error [ UnsupportedCompatibilityPath ]
        | Some descriptor ->
            let issues = ResizeArray<ContractIssue>()

            if not (isSha256 candidate.ArtifactSha256) then
                issues.Add(InvalidSha256 "artifactSha256")

            if not (isSha256 candidate.ConfigurationSha256) then
                issues.Add(InvalidSha256 "configurationSha256")

            compareLimit issues "maximumArtifactBytes" descriptor.Limits.MaximumArtifactBytes candidate.Limits.MaximumArtifactBytes
            compareLimit issues "maximumMemoryPages" descriptor.Limits.MaximumMemoryPages candidate.Limits.MaximumMemoryPages
            compareLimit issues "maximumInputBytes" descriptor.Limits.MaximumInputBytes candidate.Limits.MaximumInputBytes
            compareLimit issues "maximumOutputBytes" descriptor.Limits.MaximumOutputBytes candidate.Limits.MaximumOutputBytes
            compareLimit issues "maximumDeadlineMilliseconds" descriptor.Limits.MaximumDeadlineMilliseconds candidate.Limits.MaximumDeadlineMilliseconds

            if candidate.Deadline <> descriptor.Deadline then
                issues.Add(PolicyDiffersFromProfile "deadline")

            if candidate.Scheduling <> descriptor.Scheduling then
                issues.Add(PolicyDiffersFromProfile "scheduling")

            if candidate.Replacement <> descriptor.Replacement then
                issues.Add(PolicyDiffersFromProfile "replacement")

            if descriptor.Support = InventoryOnlyMigrationRequired then
                issues.Add(InventoryOnlyPathNotAdmissible descriptor.Path)

            issues.AddRange(validateDescriptor descriptor)

            if issues.Count = 0 then
                Ok(ValidatedConfiguration(candidate, descriptor))
            else
                Error(List.ofSeq issues)

    let validateBoundary (boundary: CandidateConfigurationBoundary) =
        let malformed field = Error [ MalformedBoundaryField field ]

        if obj.ReferenceEquals(boundary, null) then
            malformed "configuration"
        else
            let parseInt field (value: string) =
                match Int32.TryParse value with
                | true, parsed when string parsed = value -> Ok parsed
                | _ -> malformed field

            let path =
                match boundary.Path with
                | "bar-protected" -> Ok BarProtected
                | "sc2-imported-strict" -> Ok Sc2ImportedStrict
                | "sc2-legacy-direct-url" -> Ok Sc2LegacyDirectUrl
                | _ -> malformed "path"

            let deadline =
                match boundary.Deadline with
                | "phase-watchdog" -> Ok PhaseWatchdog
                | "end-to-end-from-enqueue" -> Ok EndToEndFromEnqueue
                | _ -> malformed "deadline"

            let scheduling =
                match boundary.Scheduling with
                | "refuse-while-busy" -> Ok RefuseWhileBusy
                | value when not (String.IsNullOrEmpty value) && value.StartsWith("bounded-fifo:", StringComparison.Ordinal) ->
                    parseInt "scheduling" (value.Substring("bounded-fifo:".Length)) |> Result.map BoundedFifo
                | _ -> malformed "scheduling"

            let replacement =
                match boundary.Replacement with
                | "destructive-load" -> Ok DestructiveLoad
                | "transactional-candidate-with-recovery-freeze" -> Ok TransactionalCandidateWithRecoveryFreeze
                | _ -> malformed "replacement"

            match
                path,
                parseInt "maximumArtifactBytes" boundary.MaximumArtifactBytes,
                parseInt "maximumMemoryPages" boundary.MaximumMemoryPages,
                parseInt "maximumInputBytes" boundary.MaximumInputBytes,
                parseInt "maximumOutputBytes" boundary.MaximumOutputBytes,
                parseInt "maximumDeadlineMilliseconds" boundary.MaximumDeadlineMilliseconds,
                deadline,
                scheduling,
                replacement
            with
            | Ok parsedPath, Ok maximumArtifactBytes, Ok maximumMemoryPages, Ok maximumInputBytes,
              Ok maximumOutputBytes, Ok maximumDeadlineMilliseconds, Ok parsedDeadline,
              Ok parsedScheduling, Ok parsedReplacement ->
                let candidate: CandidateConfiguration =
                    { Path = parsedPath
                      ArtifactSha256 = boundary.ArtifactSha256
                      ConfigurationSha256 = boundary.ConfigurationSha256
                      Limits =
                        { MaximumArtifactBytes = maximumArtifactBytes
                          MaximumMemoryPages = maximumMemoryPages
                          MaximumInputBytes = maximumInputBytes
                          MaximumOutputBytes = maximumOutputBytes
                          MaximumDeadlineMilliseconds = maximumDeadlineMilliseconds }
                      Deadline = parsedDeadline
                      Scheduling = parsedScheduling
                      Replacement = parsedReplacement }
                validateConfiguration candidate
            | Error issues, _, _, _, _, _, _, _, _
            | _, Error issues, _, _, _, _, _, _, _
            | _, _, Error issues, _, _, _, _, _, _
            | _, _, _, Error issues, _, _, _, _, _
            | _, _, _, _, Error issues, _, _, _, _
            | _, _, _, _, _, Error issues, _, _, _
            | _, _, _, _, _, _, Error issues, _, _
            | _, _, _, _, _, _, _, Error issues, _
            | _, _, _, _, _, _, _, _, Error issues -> Error issues

    let configuration (ValidatedConfiguration(configuration, _)) = configuration
    let descriptor (ValidatedConfiguration(_, descriptor)) = descriptor

type SpanPurpose =
    | Descriptor
    | Input
    | Output

type GuestSpan =
    { Pointer: uint64
      Length: uint64 }

type SpanIssue =
    | NullPointerForNonEmptySpan
    | PointerExceedsWasm32
    | LengthExceedsWasm32
    | SpanEndExceedsWasm32
    | SpanOutsideMemory
    | MisalignedPointer
    | EmptyOutputRejected

type ValidatedSpan = private ValidatedSpan of GuestSpan

type CleanupOwnership =
    | OwnedAndValidated of ValidatedSpan
    | RejectedOrAliased
    | UnknownAllocatorOwnership

type CleanupDecision =
    | FreeValidatedSpan of GuestSpan
    | SkipCleanupAndTerminate

[<RequireQualifiedAccess>]
module Spans =
    let private alignment policy purpose =
        match purpose with
        | Descriptor -> policy.DescriptorAlignment
        | Input -> policy.InputAlignment
        | Output -> policy.OutputAlignment

    let validate policy memoryBytes purpose span =
        let issues = ResizeArray<SpanIssue>()
        let wasm32End = 0x1_0000_0000UL

        if span.Pointer > uint64 UInt32.MaxValue then issues.Add PointerExceedsWasm32
        if span.Length > uint64 UInt32.MaxValue then issues.Add LengthExceedsWasm32
        if span.Length > 0UL && span.Pointer = 0UL then issues.Add NullPointerForNonEmptySpan

        if span.Pointer > wasm32End - min span.Length wasm32End then
            issues.Add SpanEndExceedsWasm32
        else
            let spanEnd = span.Pointer + span.Length
            if spanEnd > wasm32End then issues.Add SpanEndExceedsWasm32
            if spanEnd > memoryBytes then issues.Add SpanOutsideMemory

        if span.Length > 0UL && alignment policy purpose = RequireFourByteAlignment && span.Pointer % 4UL <> 0UL then
            issues.Add MisalignedPointer

        if purpose = Output && span.Length = 0UL && policy.EmptyOutput = RejectEmptyOutput then
            issues.Add EmptyOutputRejected

        if issues.Count = 0 then Ok(ValidatedSpan span) else Error(List.ofSeq issues)

    let value (ValidatedSpan span) = span

    let overlap (ValidatedSpan left) (ValidatedSpan right) =
        left.Length > 0UL
        && right.Length > 0UL
        && left.Pointer < right.Pointer + right.Length
        && right.Pointer < left.Pointer + left.Length

    let cleanup ownership =
        match ownership with
        | OwnedAndValidated(ValidatedSpan span) -> FreeValidatedSpan span
        | RejectedOrAliased
        | UnknownAllocatorOwnership -> SkipCleanupAndTerminate

type HostIdentity =
    { WorkerInstance: string
      Request: uint64
      Generation: uint64 }

type InvocationPhase =
    | Compile
    | Instantiate
    | AllocateDescriptor
    | AllocateInput
    | Initialize
    | Process
    | Free
    | Shutdown

type DispatchEvidence =
    | NotDispatched
    | Dispatched

type CleanupFault =
    { Phase: InvocationPhase
      Diagnostic: string }

type TerminalState =
    | Succeeded
    | GuestRejected of status: int
    | Faulted of diagnostic: string
    | TimedOut
    | Invalidated
    | Frozen

type EffectEligibility =
    | ProductAdapterMustDecide

type InvocationOutcome =
    { Identity: HostIdentity
      Phase: InvocationPhase
      State: TerminalState
      Dispatch: DispatchEvidence
      CleanupFault: CleanupFault option
      CopiedOutput: byte array
      EffectEligibility: EffectEligibility }
