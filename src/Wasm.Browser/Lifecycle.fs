namespace FS.GG.Wasm.Browser

open System
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

type private WorkerRef =
    {
        Instance: string
        Generation: uint64
        Configuration: ValidatedConfiguration
    }

type private PendingRequest =
    {
        Identity: HostIdentity
        Operation: WorkerOperation
        Configuration: ValidatedConfiguration
        Phase: InvocationPhase
        Submission: SubmissionClass
        Input: byte array
        DeadlineMilliseconds: int64
        SelectedLimits: RequestLimits
    }

type private CandidateState =
    {
        Worker: WorkerRef
        Transaction: string
        Initialized: bool
        Validated: bool
    }

type private LifecycleState =
    {
        Settings: HostSettings
        AdmissionLimits: RequestLimits option
        Clock: int64
        NextRequest: uint64
        NextGeneration: uint64
        Active: WorkerRef option
        Candidate: CandidateState option
        Retiring: Map<string, WorkerRef>
        Current: PendingRequest option
        Controls: Map<string, PendingRequest>
        Compiled: Set<string>
        Initialized: Set<string>
        Ordinary: PendingRequest list
        Ordered: PendingRequest list
        Snapshot: PendingRequest option
        FrozenToken: string option
        Disposed: bool
        Settled: Set<string>
        Deliveries: uint64 list
        LastAction: string
    }

type HostState = private HostState of LifecycleState

/// A decision carries clock-driven maintenance even when the requested action is refused.
type HostDecision = { State: HostState; Effects: HostEffect list; Result: Result<unit, RequestAdmissionIssue list> }

[<RequireQualifiedAccess>]
module Lifecycle =
    let private emptyRequest =
        {
            Id = 0UL
            Worker = ""
            Operation = "none"
            Phase = "process"
            Correlation = ""
            Generation = 0UL
            DeadlineMilliseconds = 0L
            Submission = "Ordinary"
            Bytes = 0
        }

    let private key (identity: HostIdentity) =
        $"{identity.WorkerInstance}:{identity.Generation}:{identity.Request}"

    let private bump value =
        if value = UInt64.MaxValue then value else value + 1UL

    let private token (identity: HostIdentity) =
        OperationToken.create $"request-{identity.Generation}-{identity.Request}"
        |> function
            | Ok value -> value
            | Error _ -> failwith "internally generated operation token is nonempty"

    let private profileName state =
        match (HostSettings.configuration state.Settings |> Validation.descriptor).Path with
        | BarProtected -> "Bar"
        | Sc2ImportedStrict -> "Sc2"
        | Sc2LegacyDirectUrl -> "Legacy"

    let private requestProjection request =
        {
            Id = request.Identity.Request
            Worker = request.Identity.WorkerInstance
            Operation =
                (match request.Operation with
                 | InspectAndCompile _ -> "load"
                 | InitializeGuest _ -> "initialize"
                 | ProcessGuest _ -> "process"
                 | ShutdownGuest -> "shutdown"
                 | _ -> "other")
            Phase =
                (match request.Phase with
                 | Compile -> "compile"
                 | Instantiate -> "instantiate"
                 | AllocateDescriptor -> "allocate-descriptor"
                 | AllocateInput -> "allocate-input"
                 | Initialize -> "initialize"
                 | Process -> "process"
                 | Free -> "free"
                 | Shutdown -> "shutdown")
            Correlation = OperationToken.value (token request.Identity)
            Generation = request.Identity.Generation
            DeadlineMilliseconds = request.DeadlineMilliseconds
            Submission =
                match request.Submission with
                | Ordinary -> "Ordinary"
                | OrderedEvent -> "Ordered"
                | ReplaceableSnapshot -> "Snapshot"
            Bytes = request.Input.Length
        }

    let private workerProjection worker =
        worker |> Option.map (fun value -> value.Instance) |> Option.defaultValue ""

    let private generationProjection worker =
        worker |> Option.map (fun value -> value.Generation) |> Option.defaultValue 0UL

    let create settings =
        let path =
            HostSettings.configuration settings
            |> Validation.descriptor
            |> fun descriptor -> descriptor.Path

        match path with
        | Sc2LegacyDirectUrl -> Error [ UnsupportedRuntimeProfile path ]
        | BarProtected
        | Sc2ImportedStrict ->
            Ok(
                HostState
                    {
                        Settings = settings
                        AdmissionLimits = None
                        Clock = 0L
                        NextRequest = 1UL
                        NextGeneration = 1UL
                        Active = None
                        Candidate = None
                        Retiring = Map.empty
                        Current = None
                        Controls = Map.empty
                        Compiled = Set.empty
                        Initialized = Set.empty
                        Ordinary = []
                        Ordered = []
                        Snapshot = None
                        FrozenToken = None
                        Disposed = false
                        Settled = Set.empty
                        Deliveries = []
                        LastAction = "init"
                    }
            )

    let project (HostState state) =
        let candidate = state.Candidate |> Option.map _.Worker

        {
            Profile = profileName state
            Clock = state.Clock
            NextRequest = state.NextRequest
            NextGeneration = state.NextGeneration
            ActiveWorker = workerProjection state.Active
            ActiveGeneration = generationProjection state.Active
            CandidateWorker = workerProjection candidate
            CandidateGeneration = generationProjection candidate
            CandidateTransaction = state.Candidate |> Option.map _.Transaction |> Option.defaultValue ""
            CandidateInitialized = state.Candidate |> Option.exists _.Initialized
            CandidateReady =
                state.Candidate
                |> Option.exists (fun value -> value.Initialized && value.Validated)
            RetiringWorkers = state.Retiring |> Map.toList |> List.map fst |> List.sort
            CompiledWorkers = state.Compiled |> Set.toList
            InitializedWorkers = state.Initialized |> Set.toList
            Controls = state.Controls |> Map.toList |> List.map (snd >> requestProjection)
            Current =
                state.Current
                |> Option.map requestProjection
                |> Option.defaultValue emptyRequest
            Ordinary = state.Ordinary |> List.map requestProjection
            Ordered = state.Ordered |> List.map requestProjection
            Snapshot =
                state.Snapshot
                |> Option.map requestProjection
                |> Option.defaultValue emptyRequest
            Frozen = state.FrozenToken.IsSome
            FreezeToken = state.FrozenToken |> Option.defaultValue ""
            Disposed = state.Disposed
            Deliveries = state.Deliveries
            LastAction = state.LastAction
        }

    let private effectProjection effect =
        let projected =
            match effect with
            | CreateWorker(identity, slot) ->
                {
                    Operation = ""
                    Phase = ""
                    Correlation = ""
                    Bytes = 0
                    Due = 0L
                    Kind =
                        match slot with
                        | Active -> "createActive"
                        | Candidate _ -> "createCandidate"
                        | Retiring -> "createRetiring"
                    Request = identity.Request
                    Generation = identity.Generation
                    Worker = identity.WorkerInstance
                }
            | PostToWorker(worker, command) ->
                {
                    Operation = ""
                    Phase = ""
                    Correlation = ""
                    Bytes = 0
                    Due = 0L
                    Kind = "post"
                    Request = command.Identity.Request
                    Generation = command.Identity.Generation
                    Worker = worker
                }
            | TerminateWorker worker ->
                {
                    Operation = ""
                    Phase = ""
                    Correlation = ""
                    Bytes = 0
                    Due = 0L
                    Kind = "terminate"
                    Request = 0UL
                    Generation = 0UL
                    Worker = worker
                }
            | ArmTimer(timer, _) ->
                {
                    Operation = ""
                    Phase = ""
                    Correlation = ""
                    Bytes = 0
                    Due = 0L
                    Kind = "armTimer"
                    Request = timer.Identity.Request
                    Generation = timer.Identity.Generation
                    Worker = timer.Identity.WorkerInstance
                }
            | CancelTimer timer ->
                {
                    Operation = ""
                    Phase = ""
                    Correlation = ""
                    Bytes = 0
                    Due = 0L
                    Kind = "cancelTimer"
                    Request = timer.Identity.Request
                    Generation = timer.Identity.Generation
                    Worker = timer.Identity.WorkerInstance
                }
            | Settle result ->
                let kind =
                    match result.Disposition, result.Reason with
                    | Current, _ ->
                        match result.Outcome with
                        | Some outcome when outcome.CleanupFault.IsSome -> "settleFaulted"
                        | Some outcome ->
                            match outcome.State, outcome.Phase with
                            | Faulted _, _
                            | TimedOut, _ -> "settleFaulted"
                            | GuestRejected _, _ -> "settleRejected"
                            | Succeeded, Shutdown -> "settleShutdown"
                            | _ -> "settleCurrent"
                        | None -> "settleCurrent"
                    | HistoricalOnly, _ -> "settleHistorical"
                    | Coalesced, _ -> "settleCoalesced"
                    | Discarded, Some DeadlineExpired -> "settleTimedOut"
                    | Discarded, Some GenerationRetired -> "settleInvalidated"
                    | Discarded, _ -> "settleDiscarded"
                    | Refused, Some Busy -> "refusedBusy"
                    | Refused, Some QueueFull -> "refusedQueueFull"
                    | Refused, Some HostFrozen -> "refusedFrozen"
                    | Refused, Some HostDisposed -> "refusedDisposed"
                    | Refused, _ -> "refused"

                {
                    Operation = ""
                    Phase = ""
                    Correlation = ""
                    Bytes = 0
                    Due = 0L
                    Kind = kind
                    Request = result.Identity.Request
                    Generation = result.Identity.Generation
                    Worker = result.Identity.WorkerInstance
                }

        let phaseName =
            function
            | Compile -> "compile"
            | Instantiate -> "instantiate"
            | AllocateDescriptor -> "allocate-descriptor"
            | AllocateInput -> "allocate-input"
            | Initialize -> "initialize"
            | Process -> "process"
            | Free -> "free"
            | Shutdown -> "shutdown"

        match effect with
        | PostToWorker(_, command) ->
            let operation, bytes =
                match command.Operation with
                | InspectAndCompile(_, bytes) -> "load", bytes.Length
                | InitializeGuest bytes -> "initialize", bytes.Length
                | ProcessGuest(_, bytes) -> "process", bytes.Length
                | ShutdownGuest -> "shutdown", 0
                | _ -> "other", 0

            { projected with
                Operation = operation
                Bytes = bytes
                Correlation = OperationToken.value command.Correlation
            }
        | ArmTimer(timer, due) ->
            { projected with
                Phase = phaseName timer.Phase
                Correlation = OperationToken.value timer.Correlation
                Due = due
            }
        | CancelTimer timer ->
            { projected with
                Phase = phaseName timer.Phase
                Correlation = OperationToken.value timer.Correlation
            }
        | _ -> projected

    let projectEffects effects = effects |> List.map effectProjection

    let private result identity outcome disposition reason =
        Settle
            {
                Identity = identity
                Outcome = outcome
                Disposition = disposition
                Reason = reason
            }

    let private settle state request outcome disposition reason =
        let requestKey = key request.Identity

        if state.Settled.Contains requestKey then
            state, []
        else
            { state with
                Settled = state.Settled.Add requestKey
                Deliveries = state.Deliveries @ [ request.Identity.Request ]
            },
            [ result request.Identity outcome disposition reason ]

    let private dispatch (state: LifecycleState) (request: PendingRequest) =
        match state.Active with
        | None -> state, []
        | Some worker ->
            let command =
                {
                    Identity = request.Identity
                    Correlation = token request.Identity
                    Configuration = request.Configuration
                    Operation = request.Operation
                }

            let timer =
                {
                    Identity = request.Identity
                    Correlation = command.Correlation
                    Phase = request.Phase
                }

            { state with Current = Some request },
            [
                PostToWorker(worker.Instance, command)
                ArmTimer(timer, request.DeadlineMilliseconds)
            ]

    let private refuse state request reason action =
        let settled, effects =
            settle { state with LastAction = action } request None Refused (Some reason)

        settled, effects

    let private request state identity submission operation phase input =
        let configuration =
            match operation with
            | InspectAndCompile(configuration, _) -> configuration
            | _ ->
                state.Active
                |> Option.filter (fun w -> w.Instance = identity.WorkerInstance)
                |> Option.orElseWith (fun () ->
                    state.Candidate
                    |> Option.map _.Worker
                    |> Option.filter (fun w -> w.Instance = identity.WorkerInstance))
                |> Option.orElseWith (fun () -> state.Retiring.TryFind identity.WorkerInstance)
                |> Option.map _.Configuration
                |> Option.defaultValue (HostSettings.configuration state.Settings)

        let ceiling = Validation.configuration configuration
        let selected =
            state.AdmissionLimits |> Option.defaultValue
                { MaximumDeadlineMilliseconds = ceiling.Limits.MaximumDeadlineMilliseconds
                  MaximumOutputBytes = ceiling.Limits.MaximumOutputBytes
                  EnclosingDeadlineMilliseconds = None }
        let effective =
            match operation with
            | InitializeGuest _ | ProcessGuest _ ->
                Validation.validateConfiguration
                    { ceiling with Limits = { ceiling.Limits with MaximumDeadlineMilliseconds = selected.MaximumDeadlineMilliseconds; MaximumOutputBytes = selected.MaximumOutputBytes } }
                |> Result.defaultWith (fun _ -> failwith "validated request narrowing became invalid")
            | _ -> configuration
        let due =
            if state.Clock > Int64.MaxValue - int64 selected.MaximumDeadlineMilliseconds then Int64.MaxValue
            else state.Clock + int64 selected.MaximumDeadlineMilliseconds
        { Identity = identity
          Operation = operation
          Configuration = effective
          Phase = phase
          Submission = submission
          Input = Array.copy input
          DeadlineMilliseconds = selected.EnclosingDeadlineMilliseconds |> Option.map (min due) |> Option.defaultValue due
          SelectedLimits = selected }

    let private capacity state =
        HostSettings.configuration state.Settings
        |> Validation.descriptor
        |> fun descriptor -> descriptor.RoleCapacities |> List.sumBy _.MaximumInstances
        |> max 1

    let private liveWorkers state =
        (if state.Active.IsSome then 1 else 0)
        + (if state.Candidate.IsSome then 1 else 0)
        + state.Retiring.Count

    let private settleList state requests disposition reason =
        ((state, []), requests)
        ||> List.fold (fun (current, effects) request ->
            let next, emitted = settle current request None disposition reason
            next, effects @ emitted)

    let private timerFor pending =
        {
            Identity = pending.Identity
            Correlation = token pending.Identity
            Phase = pending.Phase
        }

    let private startControl (pending: PendingRequest) (state: LifecycleState) =
        let next =
            { state with
                Controls = state.Controls.Add(pending.Identity.WorkerInstance, pending)
            }

        let command =
            {
                Identity = pending.Identity
                Correlation = token pending.Identity
                Configuration = pending.Configuration
                Operation = pending.Operation
            }

        next,
        [
            ArmTimer(timerFor pending, pending.DeadlineMilliseconds)
            PostToWorker(pending.Identity.WorkerInstance, command)
        ]

    let private invalidateWorker worker reason action state =
        let control = state.Controls.TryFind worker |> Option.toList

        let active =
            state.Current
            |> Option.filter (fun p -> p.Identity.WorkerInstance = worker)
            |> Option.toList

        let queued =
            (state.Ordinary @ state.Ordered @ (state.Snapshot |> Option.toList))
            |> List.filter (fun p -> p.Identity.WorkerInstance = worker)

        let cleared =
            { state with
                Active = state.Active |> Option.filter (fun w -> w.Instance <> worker)
                Candidate = state.Candidate |> Option.filter (fun c -> c.Worker.Instance <> worker)
                Retiring = state.Retiring.Remove worker
                Current = state.Current |> Option.filter (fun p -> p.Identity.WorkerInstance <> worker)
                Controls = state.Controls.Remove worker
                Compiled = state.Compiled.Remove worker
                Initialized = state.Initialized.Remove worker
                Ordinary = state.Ordinary |> List.filter (fun p -> p.Identity.WorkerInstance <> worker)
                Ordered = state.Ordered |> List.filter (fun p -> p.Identity.WorkerInstance <> worker)
                Snapshot = state.Snapshot |> Option.filter (fun p -> p.Identity.WorkerInstance <> worker)
                LastAction = action
            }

        let next, settlements =
            ((cleared, []), control @ active @ queued)
            ||> List.fold (fun (current, effects) pending ->
                let outcome =
                    match reason with
                    | DeadlineExpired
                    | WorkerFault _ ->
                        let terminal =
                            match reason with
                            | DeadlineExpired -> TimedOut
                            | WorkerFault diagnostic -> Faulted diagnostic
                            | _ -> TimedOut

                        let dispatched =
                            (control @ active |> List.exists (fun p -> p.Identity = pending.Identity))
                            && (match pending.Phase with
                                | Initialize
                                | Process
                                | Free
                                | Shutdown -> true
                                | _ -> false)

                        Some
                            {
                                Identity = pending.Identity
                                Phase = pending.Phase
                                State = terminal
                                Dispatch = (if dispatched then Dispatched else NotDispatched)
                                CleanupFault = None
                                CopiedOutput = Array.empty
                                EffectEligibility = ProductAdapterMustDecide
                            }
                    | _ -> None

                let next, emitted = settle current pending outcome Discarded (Some reason)
                next, effects @ emitted)

        next,
        [ TerminateWorker worker ]
        @ ((control @ active) |> List.map (timerFor >> CancelTimer))
        @ settlements

    let private dispose state =
        if state.Disposed then
            { state with
                LastAction = "disposeAgain"
            },
            []
        else
            let pending =
                (state.Controls |> Map.toList |> List.map snd)
                @ (state.Current |> Option.toList)
                @ state.Ordinary
                @ state.Ordered
                @ (state.Snapshot |> Option.toList)

            let cleared =
                { state with
                    Active = None
                    Candidate = None
                    Retiring = Map.empty
                    Current = None
                    Ordinary = []
                    Ordered = []
                    Snapshot = None
                    Controls = Map.empty
                    Compiled = Set.empty
                    Initialized = Set.empty
                    Disposed = true
                    LastAction = "dispose"
                }

            let settled, settleEffects =
                settleList cleared pending Discarded (Some HostDisposed)

            let workers =
                [
                    yield! state.Active |> Option.map _.Instance |> Option.toList
                    yield!
                        state.Candidate
                        |> Option.map (fun value -> value.Worker.Instance)
                        |> Option.toList
                    yield! state.Retiring |> Map.toList |> List.map fst
                ]
                |> List.distinct
                |> List.map TerminateWorker

            let cancel = (pending |> List.map (timerFor >> CancelTimer))
            settled, workers @ cancel @ settleEffects

    let private expireAt now state =
        let expired, retained =
            (state.Ordinary @ state.Ordered @ (state.Snapshot |> Option.toList))
            |> List.partition (fun request -> HostSettings.compatibility state.Settings = DefaultCompatibility && request.DeadlineMilliseconds < now)

        let ordinary =
            retained |> List.filter (fun request -> request.Submission = Ordinary)

        let ordered =
            retained |> List.filter (fun request -> request.Submission = OrderedEvent)

        let snapshot =
            retained
            |> List.tryFind (fun request -> request.Submission = ReplaceableSnapshot)

        let baseState =
            { state with
                Ordinary = if HostSettings.compatibility state.Settings=Sc2SupervisorV1 then state.Ordinary else ordinary
                Ordered = if HostSettings.compatibility state.Settings=Sc2SupervisorV1 then state.Ordered else ordered
                Snapshot = if HostSettings.compatibility state.Settings=Sc2SupervisorV1 then state.Snapshot else snapshot
                Clock = now
                LastAction =
                    if expired.IsEmpty then
                        state.LastAction
                    else
                        "expireOrdinary"
            }

        let afterQueued, queuedEffects =
            settleList baseState expired Discarded (Some DeadlineExpired)

        let expiredControls =
            afterQueued.Controls
            |> Map.toList
            |> List.choose (fun (worker, pending) ->
                if pending.DeadlineMilliseconds < now then
                    Some worker
                else
                    None)

        let afterControls, controlEffects =
            ((afterQueued, []), expiredControls)
            ||> List.fold (fun (current, effects) worker ->
                let next, more = invalidateWorker worker DeadlineExpired "expireControl" current
                next, effects @ more)

        match afterControls.Current with
        | Some current when current.DeadlineMilliseconds < now ->
            let next, effects =
                invalidateWorker current.Identity.WorkerInstance DeadlineExpired "expireCurrent" afterControls

            next, queuedEffects @ controlEffects @ effects
        | _ -> afterControls, queuedEffects @ controlEffects

    let private compatible (state: LifecycleState) = HostSettings.compatibility state.Settings = Sc2SupervisorV1

    let private enqueueSnapshot (state: LifecycleState) (pending: PendingRequest) =
        if state.Ordinary.Length >= 4 then refuse state pending QueueFull "refuse"
        else
            let budget = int64 pending.SelectedLimits.MaximumDeadlineMilliseconds
            let due = if state.Clock > Int64.MaxValue-budget then Int64.MaxValue else state.Clock+budget
            let admitted = { pending with DeadlineMilliseconds = pending.SelectedLimits.EnclosingDeadlineMilliseconds |> Option.map (min due) |> Option.defaultValue due }
            { state with Ordinary = state.Ordinary @ [admitted]; LastAction = "queueSnapshot" }, []

    let private dispatchNext (state: LifecycleState) =
        if state.Current.IsSome || state.FrozenToken.IsSome || state.Disposed then state, []
        elif not (compatible state) then
            match state.Ordinary, state.Ordered, state.Snapshot with
            | request::remaining, _, _ -> dispatch { state with Ordinary=remaining } request
            | [], request::remaining, _ -> dispatch { state with Ordered=remaining } request
            | [], [], Some request -> dispatch { state with Snapshot=None } request
            | _ -> state, []
        else
            let pumped, effects =
                match state.Ordinary with
                | request::remaining when state.Clock >= request.DeadlineMilliseconds ->
                    let cleared = { state with Ordinary=remaining }
                    let outcome = { Identity=request.Identity; Phase=request.Phase; State=TimedOut; Dispatch=NotDispatched; CleanupFault=None; CopiedOutput=Array.empty; EffectEligibility=ProductAdapterMustDecide }
                    let timed, terminal = settle cleared request (Some outcome) Discarded (Some DeadlineExpired)
                    let destroyed, destruction = dispose timed
                    let mechanics, settlements = destruction |> List.splitAt (destruction |> List.takeWhile(function Settle _ -> false | _ -> true) |> List.length)
                    destroyed, mechanics @ terminal @ settlements
                | request::remaining -> dispatch { state with Ordinary=remaining } request
                | [] -> state, []
            let snapshotAdmitted =
                pumped.Current |> Option.exists (fun r -> r.Submission=ReplaceableSnapshot)
                || (pumped.Ordinary |> List.exists (fun r -> r.Submission=ReplaceableSnapshot))
            match pumped.Snapshot with
            | Some held when not pumped.Disposed && not snapshotAdmitted ->
                let queued, added = enqueueSnapshot { pumped with Snapshot=None } held
                // With no earlier FIFO work the newly admitted snapshot can dispatch now.
                if queued.Current.IsNone && not queued.Ordinary.IsEmpty then
                    let request = queued.Ordinary.Head
                    if queued.Clock >= request.DeadlineMilliseconds then
                        let outcome = { Identity=request.Identity; Phase=request.Phase; State=TimedOut; Dispatch=NotDispatched; CleanupFault=None; CopiedOutput=Array.empty; EffectEligibility=ProductAdapterMustDecide }
                        let timed, terminal = settle { queued with Ordinary=queued.Ordinary.Tail } request (Some outcome) Discarded (Some DeadlineExpired)
                        let destroyed, destruction = dispose timed
                        let mechanics, settlements = destruction |> List.splitAt (destruction |> List.takeWhile(function Settle _ -> false | _ -> true) |> List.length)
                        destroyed, effects @ added @ mechanics @ terminal @ settlements
                    else
                        let started, posts = dispatch { queued with Ordinary=queued.Ordinary.Tail } request
                        started, effects @ added @ posts
                else queued, effects @ added
            | _ -> pumped, effects

    let private submit identity submission operation phase input state =
        let pending = request state identity submission operation phase input
        let descriptor = HostSettings.configuration state.Settings |> Validation.descriptor

        if state.Disposed then
            refuse state pending HostDisposed "refuse"
        elif state.FrozenToken.IsSome then
            refuse state pending HostFrozen "refuse"
        elif
            state.Active.IsNone
            || state.Active
               |> Option.exists (fun worker ->
                   worker.Generation <> identity.Generation
                   || worker.Instance <> identity.WorkerInstance)
            || not (state.Initialized.Contains identity.WorkerInstance)
        then
            refuse state pending GenerationRetired "refuse"
        elif state.Settled.Contains(key identity) then
            state, []
        elif input.Length > (Validation.configuration pending.Configuration).Limits.MaximumInputBytes then
            refuse state pending QueueFull (if compatible state && submission=ReplaceableSnapshot then "refuseOversizedSnapshot" else "refuse")
        elif compatible state then
            let population = (state.Current |> Option.toList) @ state.Ordinary
            let scheduling = HostSettings.scheduling state.Settings |> _.Realtime |> Option.get
            let oversized = input.Length > min scheduling.MaximumIndividualInputBytes (Validation.configuration pending.Configuration).Limits.MaximumInputBytes
            let admittedSnapshot = population |> List.exists (fun r -> r.Submission=ReplaceableSnapshot)
            if submission=ReplaceableSnapshot && oversized then refuse state pending QueueFull "refuseOversizedSnapshot"
            elif submission=ReplaceableSnapshot && admittedSnapshot then
                let next = { state with Snapshot=Some {pending with DeadlineMilliseconds=0L}; LastAction="holdSnapshot" }
                match state.Snapshot with
                | Some replaced -> settle next replaced None Coalesced (Some SnapshotReplaced)
                | None -> next, []
            elif submission=OrderedEvent && (oversized || population.Length >= scheduling.MaximumOrderedIncludingActive || (population |> List.sumBy (fun r -> int64 r.Input.Length)) + int64 input.Length > int64 scheduling.MaximumRetainedBytesIncludingActive) then
                refuse state pending QueueFull "refuse"
            elif submission<>OrderedEvent && state.Ordinary.Length>=4 then refuse state pending QueueFull "refuse"
            else
                let queued = { state with Ordinary=state.Ordinary@[pending]; LastAction="queueMixed" }
                if queued.Current.IsNone then dispatchNext queued else queued, []
        elif state.Current.IsNone then
            let next, effects = dispatch { state with LastAction = "begin" } pending
            next, effects
        else
            match submission, descriptor.Path with
            | Ordinary, BarProtected -> refuse state pending Busy "refuse"
            | Ordinary, Sc2ImportedStrict when state.Ordinary.Length >= 4 -> refuse state pending QueueFull "refuse"
            | Ordinary, Sc2ImportedStrict ->
                { state with
                    Ordinary = state.Ordinary @ [ pending ]
                    LastAction = "queueOrdinary"
                },
                []
            | OrderedEvent, Sc2ImportedStrict ->
                let scheduling = HostSettings.scheduling state.Settings |> _.Realtime |> Option.get

                let activeCount =
                    state.Current
                    |> Option.filter (fun value -> value.Submission = OrderedEvent)
                    |> Option.map (fun _ -> 1)
                    |> Option.defaultValue 0

                let bytes = state.Ordered |> List.sumBy (fun value -> value.Input.Length)

                let activeBytes =
                    state.Current
                    |> Option.filter (fun value -> value.Submission <> Ordinary)
                    |> Option.map (fun value -> value.Input.Length)
                    |> Option.defaultValue 0

                let snapshotBytes =
                    state.Snapshot
                    |> Option.map (fun value -> value.Input.Length)
                    |> Option.defaultValue 0

                if
                    input.Length > scheduling.MaximumIndividualInputBytes
                    || state.Ordered.Length + activeCount >= scheduling.MaximumOrderedIncludingActive
                    || bytes + activeBytes + snapshotBytes + input.Length >
                        scheduling.MaximumRetainedBytesIncludingActive
                then
                    refuse state pending QueueFull "refuse"
                else
                    { state with
                        Ordered = state.Ordered @ [ pending ]
                        LastAction = "queueOrdered"
                    },
                    []
            | ReplaceableSnapshot, Sc2ImportedStrict ->
                let scheduling = HostSettings.scheduling state.Settings |> _.Realtime |> Option.get

                let retainedBytes =
                    (state.Ordered |> List.sumBy (fun value -> value.Input.Length))
                    + (state.Current
                       |> Option.filter (fun value -> value.Submission <> Ordinary)
                       |> Option.map (fun value -> value.Input.Length)
                       |> Option.defaultValue 0)

                if
                    input.Length > scheduling.MaximumIndividualInputBytes
                    || retainedBytes + input.Length > scheduling.MaximumRetainedBytesIncludingActive
                then
                    refuse state pending QueueFull "refuse"
                else
                    match state.Snapshot with
                    | None ->
                        { state with
                            Snapshot = Some pending
                            LastAction = "queueSnapshot"
                        },
                        []
                    | Some replaced ->
                        let after, effects =
                            settle
                                { state with
                                    Snapshot = Some pending
                                    LastAction = "coalesceSnapshot"
                                }
                                replaced
                                None
                                Coalesced
                                (Some SnapshotReplaced)

                        after, effects
            | _, _ -> refuse state pending QueueFull "refuse"

    let private applyLoad (identity: HostIdentity) (intent: LoadIntent) configuration artifact (state: LifecycleState) =
        let pending =
            request state identity Ordinary (InspectAndCompile(configuration, Array.copy artifact)) Compile artifact

        let worker: WorkerRef =
            {
                Instance = identity.WorkerInstance
                Generation = identity.Generation
                Configuration = configuration
            }

        if state.Disposed then
            refuse state pending HostDisposed "refuse"
        elif
            (Validation.descriptor configuration).Path
            <> (HostSettings.configuration state.Settings |> Validation.descriptor).Path
            || state.FrozenToken.IsSome
        then
            refuse state pending HostFrozen "refuse"
        elif
            state.Settled.Contains(key identity)
            || state.Controls.ContainsKey identity.WorkerInstance
        then
            refuse state pending Busy "refuse"
        elif
            (state.Active
             |> Option.exists (fun current -> identity.Generation <= current.Generation))
            || (state.Retiring
                |> Map.exists (fun _ current ->
                    current.Instance = identity.WorkerInstance
                    || current.Generation = identity.Generation))
            || (state.Candidate
                |> Option.exists (fun current ->
                    current.Worker.Instance = identity.WorkerInstance
                    || current.Worker.Generation = identity.Generation))
        then
            refuse state pending GenerationRetired "refuse"
        elif
            (match intent with
             | PrepareCandidate _ ->
                 state.Active
                 |> Option.exists (fun current -> current.Instance = identity.WorkerInstance)
             | _ -> false)
        then
            refuse state pending GenerationRetired "refuse"
        else
            match intent with
            | ReplaceCurrent ->
                let after, effects =
                    match state.Active with
                    | Some old -> invalidateWorker old.Instance GenerationRetired "loadInitial" state
                    | None -> state, []

                let after, effects =
                    match after.Candidate with
                    | Some old ->
                        let next, more =
                            invalidateWorker old.Worker.Instance GenerationRetired "loadInitial" after

                        next, effects @ more
                    | None -> after, effects

                let next =
                    { after with
                        Active = Some worker
                        NextGeneration = max after.NextGeneration (bump identity.Generation)
                        LastAction = "loadInitial"
                    }

                let started, commands = startControl pending next
                started, effects @ [ CreateWorker(identity, Active) ] @ commands
            | PrepareCandidate(transaction, expected) ->
                if
                    state.Candidate.IsSome
                    || liveWorkers state >= capacity state
                    || expected <> (state.Active |> Option.map _.Generation)
                then
                    refuse state pending QueueFull "refuse"
                else
                    let next =
                        { state with
                            Candidate =
                                Some
                                    {
                                        Worker = worker
                                        Transaction = transaction
                                        Initialized = false
                                        Validated = false
                                    }
                            NextGeneration = max state.NextGeneration (bump identity.Generation)
                            LastAction = "prepareCandidate"
                        }

                    let started, commands = startControl pending next
                    started, [ CreateWorker(identity, Candidate transaction) ] @ commands

    let private initialize identity input state =
        let pending =
            request state identity Ordinary (InitializeGuest(Array.copy input)) AllocateDescriptor input

        let owner =
            state.Active
            |> Option.filter (fun w -> w.Instance = identity.WorkerInstance && w.Generation = identity.Generation)

        let candidate =
            state.Candidate
            |> Option.filter (fun c ->
                c.Worker.Instance = identity.WorkerInstance
                && c.Worker.Generation = identity.Generation)

        if state.Disposed then
            refuse state pending HostDisposed "refuse"
        elif state.FrozenToken.IsSome then
            refuse state pending HostFrozen "refuse"
        elif
            state.Controls.ContainsKey identity.WorkerInstance
            || (state.Current
                |> Option.exists (fun p -> p.Identity.WorkerInstance = identity.WorkerInstance))
        then
            refuse state pending Busy "refuse"
        elif
            (owner.IsNone && candidate.IsNone)
            || not (state.Compiled.Contains identity.WorkerInstance)
        then
            refuse state pending GenerationRetired "refuse"
        elif state.Settled.Contains(key identity) then
            state, []
        else
            startControl pending { state with LastAction = "initialize" }

    let private observe workerInstance observation state =
        let matching identity correlation pending =
            pending.Identity = identity
            && identity.WorkerInstance = workerInstance
            && token identity = correlation

        let finishControl (pending: PendingRequest) outcome (state: LifecycleState) =
            let cleared =
                { state with
                    Controls = state.Controls.Remove workerInstance
                }

            let disposition =
                if
                    state.FrozenToken.IsSome
                    || (state.Candidate |> Option.exists (fun c -> c.Worker.Instance = workerInstance))
                then
                    HistoricalOnly
                else
                    Current

            let next, effects = settle cleared pending (Some outcome) disposition None
            next, CancelTimer(timerFor pending) :: effects

        match observation with
        | PhaseObserved(identity, correlation, phase) ->
            let selected =
                match state.Controls.TryFind workerInstance with
                | Some pending when matching identity correlation pending -> Some(pending, true)
                | _ ->
                    state.Current
                    |> Option.filter (matching identity correlation)
                    |> Option.map (fun p -> p, false)

            match selected with
            | None -> state, []
            | Some(pending, control) ->
                let permitted =
                    match pending.Operation, pending.Phase, phase with
                    | InspectAndCompile _, Compile, Instantiate -> true
                    | (InitializeGuest _ | ProcessGuest _), AllocateDescriptor, AllocateInput -> true
                    | InitializeGuest _, AllocateInput, Initialize -> true
                    | ProcessGuest _, AllocateInput, Process -> true
                    | (InitializeGuest _ | ProcessGuest _), (Initialize | Process), Free -> true
                    | _ -> false

                if not permitted || pending.Phase = phase then
                    state, []
                else
                    let deadlinePolicy = (Validation.configuration pending.Configuration).Deadline

                    let due =
                        if deadlinePolicy = PhaseWatchdog then
                            let budget = int64 pending.SelectedLimits.MaximumDeadlineMilliseconds
                            let relative = if state.Clock > Int64.MaxValue-budget then Int64.MaxValue else state.Clock+budget
                            pending.SelectedLimits.EnclosingDeadlineMilliseconds |> Option.map (min relative) |> Option.defaultValue relative
                        else pending.DeadlineMilliseconds

                    let nextPending =
                        { pending with
                            Phase = phase
                            DeadlineMilliseconds = due
                        }

                    let next =
                        if control then
                            { state with
                                Controls = state.Controls.Add(workerInstance, nextPending)
                                LastAction = "phase"
                            }
                        else
                            { state with
                                Current = Some nextPending
                                LastAction = "phase"
                            }

                    next, [ CancelTimer(timerFor pending); ArmTimer(timerFor nextPending, due) ]
        | AbiVersionObserved(identity, correlation, version) ->
            match state.Controls.TryFind workerInstance with
            | Some pending when
                matching identity correlation pending
                && (match pending.Operation with
                    | InspectAndCompile _ -> true
                    | _ -> false)
                ->
                let expected =
                    (HostSettings.configuration state.Settings |> Validation.descriptor).Abi.Version

                if version <> expected then
                    invalidateWorker workerInstance (WorkerFault "ABI version mismatch") "workerFailed" state
                else
                    let outcome =
                        {
                            Identity = identity
                            Phase = Instantiate
                            State = Succeeded
                            Dispatch = NotDispatched
                            CleanupFault = None
                            CopiedOutput = Array.empty
                            EffectEligibility = ProductAdapterMustDecide
                        }

                    finishControl
                        pending
                        outcome
                        { state with
                            Compiled = state.Compiled.Add workerInstance
                            LastAction = "compiled"
                        }
            | _ -> state, []
        | InvocationObserved(outcome, correlation)
        | InvocationTerminated(outcome, correlation) ->
            let terminated =
                match observation with
                | InvocationTerminated _ -> true
                | _ -> false

            match state.Controls.TryFind workerInstance with
            | Some pending when matching outcome.Identity correlation pending ->
                let isInitialize =
                    match pending.Operation with
                    | InitializeGuest _ -> true
                    | _ -> false

                if
                    terminated
                    || not isInitialize
                    || outcome.Phase <> Initialize
                    || outcome.State <> Succeeded
                    || outcome.Dispatch <> Dispatched
                    || outcome.CleanupFault.IsSome
                then
                    let after, effects = finishControl pending outcome state

                    let failed, termination =
                        invalidateWorker workerInstance (WorkerFault "initialization failed") "candidateFailed" after

                    failed, effects @ termination
                else
                    let candidate =
                        state.Candidate
                        |> Option.map (fun c ->
                            if c.Worker.Instance = workerInstance then
                                { c with Initialized = true }
                            else
                                c)

                    finishControl
                        pending
                        outcome
                        { state with
                            Initialized = state.Initialized.Add workerInstance
                            Candidate = candidate
                            LastAction = "candidateInitialized"
                        }
            | _ ->
                match state.Current with
                | Some current when matching outcome.Identity correlation current ->
                    let disposition =
                        if
                            state.FrozenToken.IsSome
                            || (state.Active
                                |> Option.forall (fun a ->
                                    a.Generation <> current.Identity.Generation || a.Instance <> workerInstance))
                        then
                            HistoricalOnly
                        else
                            Current

                    let reason =
                        if disposition = HistoricalOnly then
                            Some GenerationRetired
                        else
                            None

                    let after, effects =
                        settle
                            { state with
                                Current = None
                                LastAction =
                                    (if outcome.CleanupFault.IsSome then
                                         "cleanupFault"
                                     else
                                         "complete")
                            }
                            current
                            (Some outcome)
                            disposition
                            reason

                    let effects = CancelTimer(timerFor current) :: effects

                    let shutdown =
                        match current.Operation with
                        | ShutdownGuest -> true
                        | _ -> false

                    let faulted =
                        match outcome.State with
                        | Faulted _
                        | TimedOut -> true
                        | _ -> false

                    if
                        terminated
                        || faulted
                        || outcome.CleanupFault.IsSome
                        || shutdown
                        || (disposition = HistoricalOnly && after.Retiring.ContainsKey workerInstance)
                    then
                        let failed, termination =
                            invalidateWorker workerInstance (WorkerFault "guest stopped") after.LastAction after

                        failed, effects @ termination
                    else
                        let advanced, more = dispatchNext after
                        advanced, effects @ more
                | _ -> state, []
        | WorkerFailed(identity, Some correlation, diagnostic) ->
            let current = state.Current |> Option.exists (matching identity correlation)

            let control =
                state.Controls.TryFind workerInstance
                |> Option.exists (matching identity correlation)

            if current || control then
                invalidateWorker workerInstance (WorkerFault diagnostic) "workerFailed" state
            else
                state, []
        | WorkerTerminated instance when instance = workerInstance ->
            let pending =
                state.Controls.ContainsKey instance
                || (state.Current |> Option.exists (fun p -> p.Identity.WorkerInstance = instance))

            if
                pending
                || (state.Active |> Option.exists (fun p -> p.Instance = instance))
                || (state.Candidate |> Option.exists (fun p -> p.Worker.Instance = instance))
            then
                invalidateWorker instance (WorkerFault "worker terminated") "workerFailed" state
            else
                { state with
                    Retiring = state.Retiring.Remove instance
                    Compiled = state.Compiled.Remove instance
                    Initialized = state.Initialized.Remove instance
                    LastAction = "terminateRetiring"
                },
                []
        | _ -> state, []

    let private validateCandidate transaction generation state =
        match state.Candidate with
        | Some candidate when
            candidate.Transaction = transaction
            && candidate.Worker.Generation = generation
            && candidate.Initialized
            ->
            { state with
                Candidate = Some { candidate with Validated = true }
                LastAction = "candidateValidated"
            },
            []
        | _ -> state, []

    let private commitCandidate transaction expectedActive candidateGeneration state =
        match state.Candidate with
        | Some candidate when
            candidate.Transaction = transaction
            && candidate.Worker.Generation = candidateGeneration
            && candidate.Initialized
            && state.FrozenToken.IsNone
            && candidate.Validated
            && (state.Active |> Option.map _.Generation |> Option.defaultValue 0UL) = expectedActive
            ->
            let queued = state.Ordinary @ state.Ordered @ (state.Snapshot |> Option.toList)

            let cleared =
                { state with
                    Ordinary = []
                    Ordered = []
                    Snapshot = None
                }

            let settled, settleEffects =
                settleList cleared queued Discarded (Some GenerationRetired)

            let retiring =
                match settled.Active with
                | Some active -> settled.Retiring.Add(active.Instance, active)
                | None -> settled.Retiring

            let next =
                { settled with
                    Active = Some candidate.Worker
                    Candidate = None
                    Retiring = retiring
                    LastAction = "commitCandidate"
                }

            let terminate =
                match settled.Active, settled.Current with
                | Some active, None -> [ TerminateWorker active.Instance ]
                | _ -> []

            next, settleEffects @ terminate
        | _ -> state, []

    let private abortCandidate transaction state =
        match state.Candidate with
        | Some candidate when candidate.Transaction = transaction ->
            invalidateWorker candidate.Worker.Instance GenerationRetired "abortCandidate" state
        | _ -> state, []

    let update event (HostState original) =
        let now = max original.Clock event.MonotonicMilliseconds
        let expired, expiryEffects = expireAt now original

        let withIds (input: HostInput) (state: LifecycleState) =
            match input with
            | LoadRequested(identity, _, _)
            | ConfiguredLoadRequested(identity, _, _, _)
            | InitializeRequested(identity, _)
            | InvocationRequested(identity, _, _)
            | ShutdownRequested identity ->
                { state with
                    NextRequest = max state.NextRequest (bump identity.Request)
                    NextGeneration = max state.NextGeneration (bump identity.Generation)
                }
            | _ -> state

        let state = withIds event.Input expired

        let next, effects =
            match event.Input with
            | LoadRequested(identity, intent, artifact) ->
                applyLoad identity intent (HostSettings.configuration state.Settings) artifact state
            | ConfiguredLoadRequested(identity, intent, configuration, artifact) ->
                applyLoad identity intent configuration artifact state
            | InitializeRequested(identity, input) -> initialize identity input state
            | InvocationRequested(identity, submission, input) ->
                submit identity submission (ProcessGuest(submission, input)) AllocateDescriptor input state
            | ShutdownRequested identity -> submit identity Ordinary ShutdownGuest Shutdown Array.empty state
            | CandidateValidated(transaction, generation) -> validateCandidate transaction generation state
            | CandidateCommitRequested(transaction, expectedActive, candidateGeneration) ->
                commitCandidate transaction expectedActive candidateGeneration state
            | CandidateAbortRequested transaction -> abortCandidate transaction state
            | AdapterOutputRejected(identity, diagnostic) ->
                match state.Active with
                | Some active when active.Generation = identity.Generation ->
                    let afterRejected, rejectionEffects =
                        match state.Current with
                        | Some current when current.Identity = identity ->
                            settle
                                { state with
                                    Current = None
                                    LastAction = "adapterRejected"
                                }
                                current
                                None
                                Discarded
                                (Some(AdapterRejected diagnostic))
                        | _ -> state, []

                    let disposed, disposeEffects = dispose afterRejected
                    disposed, rejectionEffects @ disposeEffects
                | _ -> state, []
            | FreezeRequested freezeToken ->
                if state.Disposed || state.FrozenToken.IsSome then
                    state, []
                else
                    let queued = state.Ordinary @ state.Ordered @ (state.Snapshot |> Option.toList)
                    let frozen = { state with FrozenToken=Some freezeToken; LastAction="freeze" }
                    if compatible state then
                        settleList { frozen with Ordinary=[]; Ordered=[]; Snapshot=None } queued Discarded (Some GenerationRetired)
                    else frozen, []
            | ResumeRequested freezeToken ->
                if state.FrozenToken = Some freezeToken then
                    dispatchNext
                        { state with
                            FrozenToken = None
                            LastAction = "resume"
                        }
                else
                    state, []
            | WorkerObserved(worker, observation) -> observe worker observation state
            | TimerObserved _ ->
                if expiryEffects.IsEmpty then
                    { state with LastAction = "stutter" }, []
                else
                    state, []
            | DisposeRequested -> dispose state

        HostState { next with Clock = now }, expiryEffects @ effects

    let projectCompatibility (HostState state) =
        let projectRequest (request:PendingRequest) : RequestLimitProjection =
            { Identity=request.Identity; Limits=request.SelectedLimits; DeadlineMilliseconds=request.DeadlineMilliseconds }
        { Policy=HostSettings.compatibility state.Settings
          Current=state.Current |> Option.map projectRequest
          Controls=state.Controls |> Map.toList |> List.map(snd >> projectRequest)
          MixedQueue=state.Ordinary |> List.map projectRequest
          HeldSnapshot=if compatible state then state.Snapshot |> Option.map projectRequest else None }

    /// Typed admission captures a fresh request budget without mutating any loaded ceiling.
    let submitLimited now requested (limits: RequestLimits) (HostState original) =
        let identity, configuration, input =
            match requested with
            | LimitedLoad(identity, configuration, artifact) -> identity, Some configuration, ConfiguredLoadRequested(identity, ReplaceCurrent, configuration, artifact)
            | LimitedCandidate(identity, transaction, expected, configuration, artifact) -> identity, Some configuration, ConfiguredLoadRequested(identity, PrepareCandidate(transaction, expected), configuration, artifact)
            | LimitedInitialize(identity, bytes) -> identity, None, InitializeRequested(identity, bytes)
            | LimitedInvoke(identity, submission, bytes) -> identity, None, InvocationRequested(identity, submission, bytes)
            | LimitedShutdown identity -> identity, None, ShutdownRequested identity
        let owner =
            original.Active |> Option.filter (fun w -> w.Instance=identity.WorkerInstance && w.Generation=identity.Generation)
            |> Option.orElseWith (fun () -> original.Candidate |> Option.map _.Worker |> Option.filter (fun w -> w.Instance=identity.WorkerInstance && w.Generation=identity.Generation))
        let ceiling = configuration |> Option.orElseWith (fun () -> owner |> Option.map _.Configuration)
        let clock = max original.Clock now
        let issues = ResizeArray<RequestAdmissionIssue>()
        match requested, ceiling with
        | LimitedInvoke(_, ReplaceableSnapshot, bytes), Some configuration when compatible original ->
            let inputCeiling = min (256*1024) (Validation.configuration configuration).Limits.MaximumInputBytes
            if bytes.Length > inputCeiling then issues.Add OversizedSnapshotInput
        | _ -> ()
        if original.Settled.Contains(key identity) then issues.Add(RequestRefused Busy)
        match ceiling with
        | None -> issues.Add RequestOwnerUnavailable
        | Some ceiling ->
            let maximum = (Validation.configuration ceiling).Limits
            // int values arriving through erased Fable types must still be finite integers.
            let integral value = let number=float value in not(Double.IsNaN number || Double.IsInfinity number) && floor number=number
            if not(integral limits.MaximumDeadlineMilliseconds) || limits.MaximumDeadlineMilliseconds<1 || limits.MaximumDeadlineMilliseconds>maximum.MaximumDeadlineMilliseconds then issues.Add(InvalidRequestLimit "maximumDeadlineMilliseconds")
            if not(integral limits.MaximumOutputBytes) || limits.MaximumOutputBytes<1 || limits.MaximumOutputBytes>maximum.MaximumOutputBytes then issues.Add(InvalidRequestLimit "maximumOutputBytes")
        if issues.Count=0 && clock > Int64.MaxValue-int64 limits.MaximumDeadlineMilliseconds then issues.Add RequestDeadlineOverflow
        if limits.EnclosingDeadlineMilliseconds |> Option.exists (fun due -> due<=clock) then issues.Add EnclosingBudgetExpired
        if original.Disposed then issues.Add(RequestRefused HostDisposed)
        elif original.FrozenToken.IsSome then issues.Add(RequestRefused HostFrozen)
        if issues.Count>0 then {State=HostState original;Effects=[];Result=Error(List.ofSeq issues)}
        else
            let next, effects = update {MonotonicMilliseconds=now;Input=input} (HostState {original with AdmissionLimits=Some limits})
            let (HostState nextState)=next
            let result =
                match effects |> List.tryPick(function Settle value when value.Identity=identity && value.Disposition=Refused -> value.Reason | _->None) with
                | Some reason -> Error [RequestRefused reason]
                | None -> Ok ()
            {State=HostState {nextState with AdmissionLimits=None};Effects=effects;Result=result}

    /// Atomic matching-token promotion preserves the recovery fence throughout.
    let commitCandidateFrozen now freezeToken transaction expectedActive candidateGeneration (HostState original) =
        let valid =
            compatible original && not original.Disposed && original.FrozenToken=Some freezeToken
            && (original.Active |> Option.map _.Generation |> Option.defaultValue 0UL)=expectedActive
            && (original.Candidate |> Option.exists(fun c -> c.Transaction=transaction && c.Worker.Generation=candidateGeneration && c.Initialized && c.Validated))
        if not valid then {State=HostState original;Effects=[];Result=Error [RequestOwnerUnavailable]}
        else
            let clock=max original.Clock now
            let expired, expiryEffects=expireAt clock original
            // Expiry may remove the candidate or active; recheck the exact receipt.
            if (expired.Active |> Option.map _.Generation |> Option.defaultValue 0UL)<>expectedActive || not(expired.Candidate |> Option.exists(fun c -> c.Transaction=transaction && c.Worker.Generation=candidateGeneration && c.Initialized && c.Validated)) then
                {State=HostState expired;Effects=expiryEffects;Result=Error [RequestOwnerUnavailable]}
            else
                let promoted, effects=commitCandidate transaction expectedActive candidateGeneration {expired with FrozenToken=None}
                {State=HostState {promoted with FrozenToken=Some freezeToken;Clock=clock};Effects=expiryEffects@effects;Result=Ok ()}
