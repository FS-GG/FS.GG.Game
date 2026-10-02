namespace FS.GG.Wasm.Browser

open System
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
    let create value =
        if String.IsNullOrWhiteSpace value then
            Error InvalidOperationToken
        else
            Ok(OperationToken value)

    let value (OperationToken value) = value

[<RequireQualifiedAccess>]
module Identity =
    let private parseDecimal fieldName (value: string) =
        if String.IsNullOrWhiteSpace value then
            Error(MissingField fieldName)
        else
            match UInt64.TryParse value with
            | false, _ -> Error(InvalidUnsignedDecimal fieldName)
            | true, parsed when string parsed <> value -> Error(NonCanonicalUnsignedDecimal fieldName)
            | true, parsed -> Ok parsed

    let parse identity =
        if obj.ReferenceEquals(identity, null) then
            Error [ MissingField "identity" ]
        else
            let worker =
                if String.IsNullOrWhiteSpace identity.WorkerInstance then
                    Error(MissingField "workerInstance")
                else
                    Ok identity.WorkerInstance

            match worker, parseDecimal "request" identity.Request, parseDecimal "generation" identity.Generation with
            | Ok workerInstance, Ok request, Ok generation ->
                let parsed: HostIdentity =
                    {
                        WorkerInstance = workerInstance
                        Request = request
                        Generation = generation
                    }

                Ok parsed
            | values ->
                let issues =
                    [
                        match values with
                        | Error issue, _, _ -> yield issue
                        | _ -> ()
                        match values with
                        | _, Error issue, _ -> yield issue
                        | _ -> ()
                        match values with
                        | _, _, Error issue -> yield issue
                        | _ -> ()
                    ]

                Error issues

    let toWire (identity: HostIdentity) : WireHostIdentity =
        {
            WorkerInstance = identity.WorkerInstance
            Request = string identity.Request
            Generation = string identity.Generation
        }

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
    let create configuration role =
        let descriptor = Validation.descriptor configuration

        let roleSupported =
            match role with
            | None -> descriptor.RoleCapacities.IsEmpty
            | Some selected ->
                descriptor.RoleCapacities
                |> List.exists (fun capacity -> capacity.Role = selected)

        if not roleSupported then
            match role with
            | Some selected -> Error [ RoleNotSupportedByProfile selected ]
            | None -> Error [ InvalidSchedulingLimit "role" ]
        else
            let realtime =
                match descriptor.Path with
                | Sc2ImportedStrict ->
                    Some
                        {
                            MaximumOrderedIncludingActive = 256
                            MaximumRetainedBytesIncludingActive = 4 * 1024 * 1024
                            MaximumIndividualInputBytes = 256 * 1024
                            CoalescePendingSnapshots = true
                        }
                | BarProtected
                | Sc2LegacyDirectUrl -> None

            Ok(
                HostSettings(
                    configuration,
                    role,
                    {
                        Ordinary = descriptor.Scheduling
                        Realtime = realtime
                    }
                )
            )

    let configuration (HostSettings(configuration, _, _)) = configuration
    let role (HostSettings(_, role, _)) = role
    let scheduling (HostSettings(_, _, scheduling)) = scheduling

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
