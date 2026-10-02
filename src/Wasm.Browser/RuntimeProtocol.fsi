namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type WireHostIdentity =
    {
        WorkerInstance: string
        Request: string
        Generation: string
    }

type ProtocolIssue =
    | MissingField of fieldName: string
    | InvalidUnsignedDecimal of fieldName: string
    | NonCanonicalUnsignedDecimal of fieldName: string
    | InvalidOperationToken
    | RoleNotSupportedByProfile of ProductRole
    | InvalidSchedulingLimit of fieldName: string

type OperationToken = private OperationToken of string

[<RequireQualifiedAccess>]
module OperationToken =
    val create: string -> Result<OperationToken, ProtocolIssue>
    val value: OperationToken -> string

[<RequireQualifiedAccess>]
module Identity =
    val parse: WireHostIdentity -> Result<HostIdentity, ProtocolIssue list>
    val toWire: HostIdentity -> WireHostIdentity

type SubmissionClass =
    | Ordinary
    | OrderedEvent
    | ReplaceableSnapshot

type RealtimeScheduling =
    {
        MaximumOrderedIncludingActive: int
        MaximumRetainedBytesIncludingActive: int
        MaximumIndividualInputBytes: int
        CoalescePendingSnapshots: bool
    }

type SchedulingSettings =
    {
        Ordinary: SchedulingPolicy
        Realtime: RealtimeScheduling option
    }

type HostSettings = private HostSettings of ValidatedConfiguration * ProductRole option * SchedulingSettings

[<RequireQualifiedAccess>]
module HostSettings =
    val create: ValidatedConfiguration -> ProductRole option -> Result<HostSettings, ProtocolIssue list>
    val configuration: HostSettings -> ValidatedConfiguration
    val role: HostSettings -> ProductRole option
    val scheduling: HostSettings -> SchedulingSettings

type WorkerSlot =
    | Active
    | Candidate of transaction: string
    | Retiring

type LoadIntent =
    | ReplaceCurrent
    | PrepareCandidate of transaction: string * expectedActiveGeneration: uint64 option

type WorkerOperation =
    | InspectAndCompile of configuration: ValidatedConfiguration * artifact: byte array
    | VerifyAbiVersion
    | InitializeGuest of input: byte array
    | ProcessGuest of submission: SubmissionClass * input: byte array
    | ShutdownGuest
    | DisposeGuest

type WorkerCommand =
    {
        Identity: HostIdentity
        Correlation: OperationToken
        Configuration: ValidatedConfiguration
        Operation: WorkerOperation
    }

type WorkerObservation =
    | WorkerCreated of identity: HostIdentity * correlation: OperationToken
    | PhaseObserved of identity: HostIdentity * correlation: OperationToken * phase: InvocationPhase
    | ArtifactDigestObserved of identity: HostIdentity * correlation: OperationToken * sha256: string
    | AbiVersionObserved of identity: HostIdentity * correlation: OperationToken * version: uint32
    | InvocationObserved of outcome: InvocationOutcome * correlation: OperationToken
    | InvocationTerminated of outcome: InvocationOutcome * correlation: OperationToken
    | WorkerFailed of identity: HostIdentity * correlation: OperationToken option * diagnostic: string
    | WorkerTerminated of workerInstance: string

type TimerIdentity =
    {
        Identity: HostIdentity
        Correlation: OperationToken
        Phase: InvocationPhase
    }

type DeliveryDisposition =
    | Current
    | HistoricalOnly
    | Discarded
    | Coalesced
    | Refused

type DeliveryReason =
    | Busy
    | QueueFull
    | DeadlineExpired
    | HostDisposed
    | HostFrozen
    | GenerationRetired
    | SnapshotReplaced
    | WorkerFault of diagnostic: string
    | AdapterRejected of diagnostic: string
    | ProtocolRejected of ProtocolIssue list

type BrowserResult =
    {
        Identity: HostIdentity
        Outcome: InvocationOutcome option
        Disposition: DeliveryDisposition
        Reason: DeliveryReason option
    }

type HostInput =
    | ConfiguredLoadRequested of
        identity: HostIdentity *
        intent: LoadIntent *
        configuration: ValidatedConfiguration *
        artifact: byte array
    | LoadRequested of identity: HostIdentity * intent: LoadIntent * artifact: byte array
    | InitializeRequested of identity: HostIdentity * input: byte array
    | InvocationRequested of identity: HostIdentity * submission: SubmissionClass * input: byte array
    | ShutdownRequested of identity: HostIdentity
    | CandidateValidated of transaction: string * candidateGeneration: uint64
    | CandidateCommitRequested of transaction: string * expectedActiveGeneration: uint64 * candidateGeneration: uint64
    | CandidateAbortRequested of transaction: string
    | AdapterOutputRejected of identity: HostIdentity * diagnostic: string
    | FreezeRequested of token: string
    | ResumeRequested of token: string
    | WorkerObserved of workerInstance: string * observation: WorkerObservation
    | TimerObserved of timer: TimerIdentity
    | DisposeRequested

/// Every external input carries one host-observed monotonic timestamp. Lifecycle
/// policy compares this value with deadlines even when a browser timer fires late.
type HostEvent =
    {
        MonotonicMilliseconds: int64
        Input: HostInput
    }

type HostEffect =
    | CreateWorker of identity: HostIdentity * slot: WorkerSlot
    | PostToWorker of workerInstance: string * command: WorkerCommand
    | TerminateWorker of workerInstance: string
    | ArmTimer of timer: TimerIdentity * dueMonotonicMilliseconds: int64
    | CancelTimer of timer: TimerIdentity
    | Settle of BrowserResult

/// Closed, primitive wire records preserve identity and policy across structured clone.
type WorkerWireCommand =
    {
        Identity: WireHostIdentity
        Correlation: string
        Kind: string
        Configuration: CandidateConfigurationBoundary
        Bytes: byte array
    }

type WorkerWireObservation =
    {
        Identity: WireHostIdentity
        Correlation: string
        Kind: string
        Phase: string
        Digest: string
        Version: uint32
        State: string
        Status: int
        Diagnostic: string
        CleanupDiagnostic: string
        Dispatched: bool
        Output: byte array
    }

/// These callbacks perform only Worker, clock and timer mechanics.
type HostTransport =
    {
        Now: unit -> int64
        CreateWorker: string -> (WorkerWireObservation -> unit) -> unit
        PostCommand: string -> WorkerWireCommand -> unit
        TerminateWorker: string -> unit
        ArmTimer: string -> int -> (unit -> unit) -> unit
        CancelTimer: string -> unit
    }
