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
        Operation: WorkerOperation
    }

type WorkerObservation =
    | WorkerCreated of identity: HostIdentity * correlation: OperationToken
    | PhaseObserved of identity: HostIdentity * correlation: OperationToken * phase: InvocationPhase
    | ArtifactDigestObserved of identity: HostIdentity * correlation: OperationToken * sha256: string
    | AbiVersionObserved of identity: HostIdentity * correlation: OperationToken * version: uint32
    | InvocationObserved of outcome: InvocationOutcome * correlation: OperationToken
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
    | LoadRequested of identity: HostIdentity * intent: LoadIntent * artifact: byte array
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
