namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type RuntimeIssue =
    | UnsupportedRuntimeProfile of CompatibilityPath
    | InvalidRuntimeState of detail: string

type RequestProjection =
    {
        Id: uint64
        Worker: string
        Operation: string
        Phase: string
        Correlation: string
        Generation: uint64
        DeadlineMilliseconds: int64
        Submission: string
        Bytes: int
    }

type EffectProjection =
    {
        Operation: string
        Phase: string
        Correlation: string
        Bytes: int
        Due: int64
        Kind: string
        Request: uint64
        Generation: uint64
        Worker: string
    }

type HostProjection =
    {
        Profile: string
        Clock: int64
        NextRequest: uint64
        NextGeneration: uint64
        ActiveWorker: string
        ActiveGeneration: uint64
        CandidateWorker: string
        CandidateGeneration: uint64
        CandidateTransaction: string
        CandidateInitialized: bool
        CandidateReady: bool
        RetiringWorkers: string list
        CompiledWorkers: string list
        InitializedWorkers: string list
        Controls: RequestProjection list
        Current: RequestProjection
        Ordinary: RequestProjection list
        Ordered: RequestProjection list
        Snapshot: RequestProjection
        Frozen: bool
        FreezeToken: string
        Disposed: bool
        Deliveries: uint64 list
        LastAction: string
    }

/// Additional state for selected compatibility; existing projections remain intact.
type RequestLimitProjection =
    { Identity: HostIdentity
      Limits: RequestLimits
      DeadlineMilliseconds: int64 }

type CompatibilityProjection =
    { Policy: HostCompatibility
      Current: RequestLimitProjection option
      Controls: RequestLimitProjection list
      MixedQueue: RequestLimitProjection list
      HeldSnapshot: RequestLimitProjection option }

type HostState

[<RequireQualifiedAccess>]
module Lifecycle =
    val create: HostSettings -> Result<HostState, RuntimeIssue list>
    val update: HostEvent -> HostState -> HostState * HostEffect list
    val project: HostState -> HostProjection
    val projectCompatibility: HostState -> CompatibilityProjection
    val projectEffects: HostEffect list -> EffectProjection list

    val submitLimited: int64 -> LimitedRequest -> RequestLimits -> HostState -> Result<HostState * HostEffect list, RequestAdmissionIssue list>
    val commitCandidateFrozen: int64 -> string -> string -> uint64 -> uint64 -> HostState -> Result<HostState * HostEffect list, RequestAdmissionIssue list>
