namespace FS.GG.Wasm.Contracts

/// The value types used by the frozen BAR and SC2 guest ABI functions.
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

/// Capacity only. A role declaration never grants a native command capability.
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

/// Raw values accepted at a JSON/JavaScript boundary. Every value remains text until
/// the complete envelope has been checked and converted to a validated configuration.
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

[<RequireQualifiedAccess>]
module Profiles =
    val abiProfiles: AbiDescriptor list
    val compatibilityPaths: CompatibilityDescriptor list
    val tryFind: CompatibilityPath -> CompatibilityDescriptor option

[<RequireQualifiedAccess>]
module Validation =
    val validateDescriptor: CompatibilityDescriptor -> ContractIssue list
    val validateConfiguration: CandidateConfiguration -> Result<ValidatedConfiguration, ContractIssue list>
    val validateBoundary: CandidateConfigurationBoundary -> Result<ValidatedConfiguration, ContractIssue list>
    val configuration: ValidatedConfiguration -> CandidateConfiguration
    val descriptor: ValidatedConfiguration -> CompatibilityDescriptor

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
    val validate: SpanPolicy -> memoryBytes: uint64 -> SpanPurpose -> GuestSpan -> Result<ValidatedSpan, SpanIssue list>
    val value: ValidatedSpan -> GuestSpan
    val overlap: ValidatedSpan -> ValidatedSpan -> bool
    val cleanup: CleanupOwnership -> CleanupDecision

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
