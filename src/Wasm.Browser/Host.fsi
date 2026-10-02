namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type EffectInterpreter = HostEffect list -> unit

/// Stateful browser facade over the pure Lifecycle reducer. The supplied interpreter
/// performs effects mechanically; all policy decisions remain in Lifecycle.update.
[<Sealed>]
type Host =
    static member CreateConnected:
        HostSettings * HostTransport * (BrowserResult -> unit) -> Result<Host, RuntimeIssue list>

    static member Create: HostSettings * EffectInterpreter -> Result<Host, RuntimeIssue list>
    member Projection: HostProjection
    member Dispatch: HostEvent -> unit
    member Load: monotonicMilliseconds: int64 * identity: HostIdentity * artifact: byte array -> unit

    member LoadConfigured:
        monotonicMilliseconds: int64 *
        identity: HostIdentity *
        configuration: ValidatedConfiguration *
        artifact: byte array ->
            unit

    member Initialize: monotonicMilliseconds: int64 * identity: HostIdentity * input: byte array -> unit

    member PrepareConfiguredCandidate:
        monotonicMilliseconds: int64 *
        identity: HostIdentity *
        transaction: string *
        expectedActiveGeneration: uint64 option *
        configuration: ValidatedConfiguration *
        artifact: byte array ->
            unit

    member PrepareCandidate:
        monotonicMilliseconds: int64 *
        identity: HostIdentity *
        transaction: string *
        expectedActiveGeneration: uint64 option *
        artifact: byte array ->
            unit

    member ValidateCandidate: monotonicMilliseconds: int64 * transaction: string * candidateGeneration: uint64 -> unit

    member CommitCandidate:
        monotonicMilliseconds: int64 *
        transaction: string *
        expectedActiveGeneration: uint64 *
        candidateGeneration: uint64 ->
            unit

    member AbortCandidate: monotonicMilliseconds: int64 * transaction: string -> unit

    member Invoke:
        monotonicMilliseconds: int64 * identity: HostIdentity * submission: SubmissionClass * input: byte array -> unit

    member Shutdown: monotonicMilliseconds: int64 * identity: HostIdentity -> unit
    member Freeze: monotonicMilliseconds: int64 * token: string -> unit
    member Resume: monotonicMilliseconds: int64 * token: string -> unit
    member RejectAdapterOutput: monotonicMilliseconds: int64 * identity: HostIdentity * diagnostic: string -> unit
    member Observe: monotonicMilliseconds: int64 * workerInstance: string * observation: WorkerObservation -> unit
    member ObserveTimer: monotonicMilliseconds: int64 * timer: TimerIdentity -> unit
    member Dispose: monotonicMilliseconds: int64 -> unit
