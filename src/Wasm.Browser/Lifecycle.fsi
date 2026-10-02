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

type HostState

[<RequireQualifiedAccess>]
module Lifecycle =
    val create: HostSettings -> Result<HostState, RuntimeIssue list>
    val update: HostEvent -> HostState -> HostState * HostEffect list
    val project: HostState -> HostProjection
    val projectEffects: HostEffect list -> EffectProjection list
