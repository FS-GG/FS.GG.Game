namespace FS.GG.Wasm.Browser

open System
open FS.GG.Wasm.Contracts

type RuntimeIssue =
    | UnsupportedRuntimeProfile of CompatibilityPath
    | InvalidRuntimeState of detail: string

type RequestProjection =
    {
        Id: uint64
        Generation: uint64
        DeadlineMilliseconds: int64
        Submission: string
        Bytes: int
    }

type EffectProjection =
    {
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

type private WorkerRef =
    { Instance: string; Generation: uint64 }

type private PendingRequest =
    {
        Identity: HostIdentity
        Submission: SubmissionClass
        Input: byte array
        DeadlineMilliseconds: int64
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
        Clock: int64
        NextRequest: uint64
        NextGeneration: uint64
        Active: WorkerRef option
        Candidate: CandidateState option
        Retiring: Map<string, WorkerRef>
        Current: PendingRequest option
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

[<RequireQualifiedAccess>]
module Lifecycle =
    let private emptyRequest =
        {
            Id = 0UL
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
                        Clock = 0L
                        NextRequest = 1UL
                        NextGeneration = 1UL
                        Active = None
                        Candidate = None
                        Retiring = Map.empty
                        Current = None
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
        match effect with
        | CreateWorker(identity, slot) ->
            {
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
                Kind = "post"
                Request = command.Identity.Request
                Generation = command.Identity.Generation
                Worker = worker
            }
        | TerminateWorker worker ->
            {
                Kind = "terminate"
                Request = 0UL
                Generation = 0UL
                Worker = worker
            }
        | ArmTimer(timer, _) ->
            {
                Kind = "armTimer"
                Request = timer.Identity.Request
                Generation = timer.Identity.Generation
                Worker = timer.Identity.WorkerInstance
            }
        | CancelTimer timer ->
            {
                Kind = "cancelTimer"
                Request = timer.Identity.Request
                Generation = timer.Identity.Generation
                Worker = timer.Identity.WorkerInstance
            }
        | Settle result ->
            let kind =
                match result.Disposition, result.Reason with
                | Current, _ -> "settleCurrent"
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
                Kind = kind
                Request = result.Identity.Request
                Generation = result.Identity.Generation
                Worker = result.Identity.WorkerInstance
            }

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
                    Operation = ProcessGuest(request.Submission, request.Input)
                }

            let timer =
                {
                    Identity = request.Identity
                    Correlation = command.Correlation
                    Phase = Process
                }

            { state with Current = Some request },
            [
                PostToWorker(worker.Instance, command)
                ArmTimer(timer, request.DeadlineMilliseconds)
            ]

    let private dispatchNext state =
        match state.Ordinary, state.Ordered, state.Snapshot with
        | request :: remaining, _, _ -> dispatch { state with Ordinary = remaining } request
        | [], request :: remaining, _ -> dispatch { state with Ordered = remaining } request
        | [], [], Some request -> dispatch { state with Snapshot = None } request
        | [], [], None -> { state with Current = None }, []

    let private refuse state request reason action =
        let settled, effects =
            settle { state with LastAction = action } request None Refused (Some reason)

        settled, effects

    let private request state identity submission input =
        let maximum =
            (HostSettings.configuration state.Settings |> Validation.configuration).Limits.MaximumDeadlineMilliseconds

        {
            Identity = identity
            Submission = submission
            Input = input
            DeadlineMilliseconds = state.Clock + int64 maximum
        }

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

    let private expireAt now state =
        let expired, retained =
            (state.Ordinary @ state.Ordered @ (state.Snapshot |> Option.toList))
            |> List.partition (fun request -> request.DeadlineMilliseconds < now)

        let ordinary =
            retained |> List.filter (fun request -> request.Submission = Ordinary)

        let ordered =
            retained |> List.filter (fun request -> request.Submission = OrderedEvent)

        let snapshot =
            retained
            |> List.tryFind (fun request -> request.Submission = ReplaceableSnapshot)

        let baseState =
            { state with
                Ordinary = ordinary
                Ordered = ordered
                Snapshot = snapshot
                Clock = now
                LastAction =
                    if expired.IsEmpty then
                        state.LastAction
                    else
                        "expireOrdinary"
            }

        let afterQueued, queuedEffects =
            settleList baseState expired Discarded (Some DeadlineExpired)

        match afterQueued.Current with
        | Some current when current.DeadlineMilliseconds < now ->
            let afterCurrent, currentEffects =
                settle
                    { afterQueued with
                        Current = None
                        LastAction = "expireCurrent"
                    }
                    current
                    None
                    Discarded
                    (Some DeadlineExpired)

            let advanced, dispatchEffects = dispatchNext afterCurrent
            advanced, queuedEffects @ currentEffects @ dispatchEffects
        | _ -> afterQueued, queuedEffects

    let private submit identity submission input state =
        let pending = request state identity submission input
        let descriptor = HostSettings.configuration state.Settings |> Validation.descriptor

        if state.Disposed then
            refuse state pending HostDisposed "refuse"
        elif state.FrozenToken.IsSome then
            refuse state pending HostFrozen "refuse"
        elif
            state.Active.IsNone
            || state.Active
               |> Option.exists (fun worker -> worker.Generation <> identity.Generation)
        then
            refuse state pending GenerationRetired "refuse"
        elif state.Settled.Contains(key identity) then
            state, []
        elif input.Length > descriptor.Limits.MaximumInputBytes then
            refuse state pending QueueFull "refuse"
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

    let private applyLoad (identity: HostIdentity) (intent: LoadIntent) (state: LifecycleState) =
        let worker: WorkerRef =
            {
                Instance = identity.WorkerInstance
                Generation = identity.Generation
            }

        match intent with
        | ReplaceCurrent ->
            let pending =
                (state.Current |> Option.toList)
                @ state.Ordinary
                @ state.Ordered
                @ (state.Snapshot |> Option.toList)

            let cleared =
                { state with
                    Current = None
                    Ordinary = []
                    Ordered = []
                    Snapshot = None
                }

            let settled, settleEffects =
                settleList cleared pending Discarded (Some GenerationRetired)

            let terminateEffects =
                settled.Active
                |> Option.map (fun value -> TerminateWorker value.Instance)
                |> Option.toList

            let next =
                { settled with
                    Active = Some worker
                    Candidate = None
                    NextGeneration = max settled.NextGeneration (bump identity.Generation)
                    LastAction = "loadInitial"
                }

            next, terminateEffects @ [ CreateWorker(identity, Active) ] @ settleEffects
        | PrepareCandidate(transaction, expectedActiveGeneration) ->
            if state.Disposed || state.Candidate.IsSome || liveWorkers state >= capacity state then
                state, []
            elif expectedActiveGeneration <> (state.Active |> Option.map _.Generation) then
                state, []
            else
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
                },
                [ CreateWorker(identity, Candidate transaction) ]

    let private observe workerInstance observation state =
        match observation with
        | InvocationObserved(outcome, _) ->
            match state.Candidate with
            | Some candidate when
                candidate.Worker.Instance = workerInstance
                && outcome.Phase = Initialize
                && outcome.State = Succeeded
                ->
                { state with
                    Candidate = Some { candidate with Initialized = true }
                    LastAction = "candidateInitialized"
                },
                []
            | _ ->
                match state.Current with
                | Some current when
                    current.Identity = outcome.Identity
                    && current.Identity.WorkerInstance = workerInstance
                    ->
                    let disposition =
                        if
                            state.FrozenToken.IsSome
                            || state.Active
                               |> Option.forall (fun active -> active.Generation <> current.Identity.Generation)
                        then
                            HistoricalOnly
                        else
                            Current

                    let reason =
                        if disposition = HistoricalOnly then
                            Some GenerationRetired
                        else
                            None

                    let after, settleEffects =
                        settle
                            { state with
                                Current = None
                                LastAction = "complete"
                            }
                            current
                            (Some outcome)
                            disposition
                            reason

                    let advanced, dispatchEffects = dispatchNext after

                    let terminateEffects =
                        if disposition = HistoricalOnly && after.Retiring.ContainsKey workerInstance then
                            [ TerminateWorker workerInstance ]
                        else
                            []

                    advanced, settleEffects @ dispatchEffects @ terminateEffects
                | _ -> state, []
        | WorkerFailed(identity, _, diagnostic) ->
            match state.Candidate with
            | Some candidate when candidate.Worker.Instance = workerInstance ->
                { state with
                    Candidate = None
                    LastAction = "candidateFailed"
                },
                [ TerminateWorker workerInstance ]
            | _ when state.Active |> Option.exists (fun active -> active.Instance = workerInstance) ->
                let pending =
                    (state.Current |> Option.toList)
                    @ state.Ordinary
                    @ state.Ordered
                    @ (state.Snapshot |> Option.toList)

                let cleared =
                    { state with
                        Active = None
                        Current = None
                        Ordinary = []
                        Ordered = []
                        Snapshot = None
                        LastAction = "workerFailed"
                    }

                let settled, effects =
                    settleList cleared pending Discarded (Some(WorkerFault diagnostic))

                settled, effects @ [ TerminateWorker identity.WorkerInstance ]
            | _ -> state, []
        | WorkerTerminated instance ->
            { state with
                Retiring = state.Retiring.Remove instance
                LastAction = "terminateRetiring"
            },
            []
        | WorkerCreated _
        | PhaseObserved _
        | ArtifactDigestObserved _
        | AbiVersionObserved _ -> state, []

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
            { state with
                Candidate = None
                LastAction = "abortCandidate"
            },
            [ TerminateWorker candidate.Worker.Instance ]
        | _ -> state, []

    let private dispose state =
        if state.Disposed then
            { state with
                LastAction = "disposeAgain"
            },
            []
        else
            let pending =
                (state.Current |> Option.toList)
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

            settled, settleEffects @ workers

    let update event (HostState original) =
        let expired, expiryEffects = expireAt event.MonotonicMilliseconds original

        let withIds (input: HostInput) (state: LifecycleState) =
            match input with
            | LoadRequested(identity, _, _)
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
            | LoadRequested(identity, intent, _) -> applyLoad identity intent state
            | InvocationRequested(identity, submission, input) -> submit identity submission input state
            | ShutdownRequested identity -> submit identity Ordinary Array.empty state
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
                    { state with
                        FrozenToken = Some freezeToken
                        LastAction = "freeze"
                    },
                    []
            | ResumeRequested freezeToken ->
                if state.FrozenToken = Some freezeToken then
                    { state with
                        FrozenToken = None
                        LastAction = "resume"
                    },
                    []
                else
                    state, []
            | WorkerObserved(worker, observation) -> observe worker observation state
            | TimerObserved _ ->
                if expiryEffects.IsEmpty then
                    { state with LastAction = "stutter" }, []
                else
                    state, []
            | DisposeRequested -> dispose state

        HostState
            { next with
                Clock = event.MonotonicMilliseconds
            },
        expiryEffects @ effects
