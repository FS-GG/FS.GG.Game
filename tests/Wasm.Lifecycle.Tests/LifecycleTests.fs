module Wasm.Lifecycle.Tests.LifecycleTests

open Expecto
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser

let private sha character = String.replicate 64 character

let private settings path =
    let descriptor = Profiles.tryFind path |> Option.get

    let configuration =
        Validation.validateConfiguration
            {
                Path = path
                ArtifactSha256 = sha "a"
                ConfigurationSha256 = sha "b"
                Limits = descriptor.Limits
                Deadline = descriptor.Deadline
                Scheduling = descriptor.Scheduling
                Replacement = descriptor.Replacement
            }
        |> function
            | Ok value -> value
            | Error issues -> failtestf "configuration failed: %A" issues

    HostSettings.create configuration (if path = BarProtected then None else Some Controller)
    |> function
        | Ok value -> value
        | Error issues -> failtestf "settings failed: %A" issues

let private identity worker request generation : HostIdentity =
    {
        WorkerInstance = worker
        Request = request
        Generation = generation
    }

let private tokenFor (identity: HostIdentity) =
    OperationToken.create $"request-{identity.Generation}-{identity.Request}"
    |> Result.defaultWith (fun issue -> failtestf "%A" issue)

let private success phase identity =
    {
        Identity = identity
        Phase = phase
        State = Succeeded
        Dispatch = Dispatched
        CleanupFault = None
        CopiedOutput = Array.empty
        EffectEligibility = ProductAdapterMustDecide
    }

/// The fresh Worker's readiness report: its first Compile receipt for the load.
let private ready (load: HostIdentity) =
    WorkerObserved(load.WorkerInstance, PhaseObserved(load, tokenFor load, Compile))

let private update at input state =
    Lifecycle.update
        {
            MonotonicMilliseconds = at
            Input = input
        }
        state

let private next at input state = update at input state |> fst

let private loaded path =
    let state =
        Lifecycle.create (settings path)
        |> Result.defaultWith (fun issues -> failtestf "%A" issues)

    let load = identity "worker-1" 100UL 1UL
    let initialize = identity "worker-1" 101UL 1UL
    let version = if path = BarProtected then 1u else 0x10000u

    state
    |> next 0L (LoadRequested(load, ReplaceCurrent, [| 0uy |]))
    |> next 0L (ready load)
    |> next 0L (WorkerObserved(load.WorkerInstance, AbiVersionObserved(load, tokenFor load, version)))
    |> next 0L (InitializeRequested(initialize, [| 1uy |]))
    |> next
        0L
        (WorkerObserved(
            initialize.WorkerInstance,
            InvocationObserved(success Initialize initialize, tokenFor initialize)
        ))

[<Tests>]
let lifecycle =
    testList
        "lifecycle reducer"
        [
            testCase "BAR refuses a second request while busy"
            <| fun _ ->
                let state = loaded BarProtected

                let state =
                    next 2L (InvocationRequested(identity "worker-1" 1UL 1UL, Ordinary, [| 1uy |])) state

                let state, effects =
                    update 3L (InvocationRequested(identity "worker-1" 2UL 1UL, Ordinary, [| 2uy |])) state

                Expect.equal (Lifecycle.project state).Current.Id 1UL "the first request remains current"

                Expect.equal
                    (Lifecycle.projectEffects effects |> List.map _.Kind)
                    [ "refusedBusy" ]
                    "BAR is single-pending"

            testCase "SC2 retains only the latest pending snapshot"
            <| fun _ ->
                let state = loaded Sc2ImportedStrict

                let state =
                    next 2L (InvocationRequested(identity "worker-1" 1UL 1UL, Ordinary, [| 1uy |])) state

                let state =
                    next 3L (InvocationRequested(identity "worker-1" 2UL 1UL, ReplaceableSnapshot, [| 2uy |])) state

                let state, effects =
                    update 4L (InvocationRequested(identity "worker-1" 3UL 1UL, ReplaceableSnapshot, [| 3uy |])) state

                Expect.equal (Lifecycle.project state).Snapshot.Id 3UL "the latest snapshot is retained"

                Expect.equal
                    (Lifecycle.projectEffects effects |> List.map _.Kind)
                    [ "settleCoalesced" ]
                    "the replaced snapshot settles once"

            testCase "candidate commit requires exact initialized and validated identity"
            <| fun _ ->
                let state = loaded Sc2ImportedStrict
                let candidate = identity "worker-2" 0UL 2UL

                let state =
                    next 2L (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), Array.empty)) state

                let state = next 3L (CandidateCommitRequested("tx", 1UL, 2UL)) state
                Expect.equal (Lifecycle.project state).ActiveGeneration 1UL "uninitialized candidate cannot commit"

                let state =
                    next 3L (ready candidate) state
                    |> next
                        3L
                        (WorkerObserved("worker-2", AbiVersionObserved(candidate, tokenFor candidate, 0x10000u)))

                let initialized = identity "worker-2" 1UL 2UL
                let state = next 3L (InitializeRequested(initialized, [| 1uy |])) state
                let wrong = identity "worker-2" 9UL 2UL

                let ignored =
                    next
                        3L
                        (WorkerObserved("worker-2", InvocationObserved(success Initialize wrong, tokenFor initialized)))
                        state

                Expect.isFalse (Lifecycle.project ignored).CandidateInitialized "wrong request cannot ready a candidate"

                let state =
                    next
                        4L
                        (WorkerObserved(
                            "worker-2",
                            InvocationObserved(success Initialize initialized, tokenFor initialized)
                        ))
                        ignored

                let state = next 5L (CandidateValidated("wrong", 2UL)) state
                let state = next 6L (CandidateCommitRequested("tx", 1UL, 2UL)) state
                Expect.equal (Lifecycle.project state).ActiveGeneration 1UL "a different transaction cannot validate"
                let state = next 7L (CandidateValidated("tx", 2UL)) state
                let state = next 8L (CandidateCommitRequested("tx", 1UL, 2UL)) state
                let projection = Lifecycle.project state
                Expect.equal projection.ActiveGeneration 2UL "the exact validated candidate commits"
                Expect.equal projection.RetiringWorkers [ "worker-1" ] "old worker capacity remains retained"

            testCase "a frozen completion is historical and duplicate delivery is inert"
            <| fun _ ->
                let state = loaded Sc2ImportedStrict
                let request = identity "worker-1" 1UL 1UL
                let state = next 2L (InvocationRequested(request, Ordinary, [| 1uy |])) state
                let state = next 3L (FreezeRequested "freeze-1") state

                let observation =
                    WorkerObserved("worker-1", InvocationObserved(success Process request, tokenFor request))

                let state, effects = update 4L observation state

                Expect.equal
                    (Lifecycle.projectEffects effects |> List.map _.Kind)
                    [ "cancelTimer"; "settleHistorical" ]
                    "freeze suppresses current delivery"

                let state, duplicate = update 5L observation state
                Expect.isEmpty duplicate "a duplicate completion emits nothing"
                Expect.equal (Lifecycle.project state).Deliveries [ 100UL; 101UL; 1UL ] "request settles exactly once"

            testCase "disposal is terminal and repeat disposal is inert"
            <| fun _ ->
                let state = loaded Sc2ImportedStrict

                let state =
                    next 2L (InvocationRequested(identity "worker-1" 1UL 1UL, Ordinary, [| 1uy |])) state

                let state, first = update 3L DisposeRequested state
                let final, second = update 4L DisposeRequested state
                Expect.isNonEmpty first "first disposal settles and terminates"
                Expect.isEmpty second "repeat disposal emits no effects"
                Expect.isTrue (Lifecycle.project final).Disposed "host remains disposed"

            testCase "adapter rejection settles the current request once before disposal"
            <| fun _ ->
                let state = loaded Sc2ImportedStrict
                let request = identity "worker-1" 1UL 1UL
                let state = next 2L (InvocationRequested(request, Ordinary, [| 1uy |])) state

                let final, effects =
                    update 3L (AdapterOutputRejected(request, "invalid output")) state

                let settlements =
                    Lifecycle.projectEffects effects
                    |> List.filter (fun effect -> effect.Request = 1UL && effect.Kind.StartsWith("settle"))

                Expect.equal settlements.Length 1 "rejection cannot duplicate terminal delivery"

                Expect.equal
                    (Lifecycle.project final).Deliveries
                    [ 100UL; 101UL; 1UL ]
                    "the delivery ledger contains one terminal entry"

                Expect.isTrue (Lifecycle.project final).Disposed "adapter rejection closes the host"

            testCase "actual termination settles active work before readiness can be reused"
            <| fun _ ->
                let started, _ =
                    update
                        1L
                        (InvocationRequested(identity "worker-1" 1UL 1UL, Ordinary, [| 1uy |]))
                        (loaded Sc2ImportedStrict)

                let unchanged, foreign =
                    update 2L (WorkerObserved("wrong-worker", WorkerTerminated "worker-1")) started

                Expect.equal (Lifecycle.project unchanged).ActiveWorker "worker-1" "foreign termination is inert"
                Expect.isEmpty foreign "foreign observation cannot terminate another worker"

                let after, effects =
                    update 2L (WorkerObserved("worker-1", WorkerTerminated "worker-1")) started

                Expect.equal (Lifecycle.project after).ActiveWorker "" "terminated worker loses authority"
                Expect.equal (Lifecycle.project after).InitializedWorkers [] "terminated readiness cannot survive"

                Expect.isTrue
                    (effects
                     |> List.exists (function
                         | Settle result -> result.Identity.Request = 1UL
                         | _ -> false))
                    "active request is settled once"

                let _, again =
                    update 3L (WorkerObserved("worker-1", WorkerTerminated "worker-1")) after

                Expect.isEmpty again "duplicate termination is inert"

            testCase "backwards observed clock cannot rewind a live budget"
            <| fun _ ->
                let started, _ =
                    update
                        100L
                        (InvocationRequested(identity "worker-1" 1UL 1UL, Ordinary, [| 1uy |]))
                        (loaded BarProtected)

                let before = Lifecycle.project started

                let after, effects =
                    update
                        1L
                        (WorkerObserved(
                            "worker-1",
                            PhaseObserved(identity "worker-1" 9UL 1UL, tokenFor (identity "worker-1" 9UL 1UL), Free)
                        ))
                        started

                Expect.equal (Lifecycle.project after).Clock before.Clock "monotonic time never moves backwards"
                Expect.equal (Lifecycle.project after).Current before.Current "foreign phase does not renew a budget"
                Expect.isEmpty effects "foreign observation remains inert"

            testCase "candidate cannot reuse active Worker identity or generation"
            <| fun _ ->
                for candidate in [ identity "worker-1" 1UL 2UL; identity "worker-2" 1UL 1UL ] do
                    let after, effects =
                        update
                            1L
                            (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), [| 0uy |]))
                            (loaded Sc2ImportedStrict)

                    Expect.equal (Lifecycle.project after).ActiveWorker "worker-1" "active worker is preserved"
                    Expect.equal (Lifecycle.project after).CandidateWorker "" "ambiguous candidate is refused"

                    Expect.isFalse
                        (effects
                         |> List.exists (function
                             | CreateWorker _ -> true
                             | _ -> false))
                        "no mechanical handle can overwrite active"

            testCase "malformed wire observation is refused without throwing"
            <| fun _ ->
                match WorkerEntry.observationFromWire Unchecked.defaultof<WorkerWireObservation> with
                | Error _ -> ()
                | Ok _ -> failtest "malformed observation accepted"

            testCase "load forwards the artifact and refuses work before initialization"
            <| fun _ ->
                let initial =
                    Lifecycle.create (settings BarProtected)
                    |> Result.defaultWith (fun issues -> failtestf "%A" issues)

                let owner = identity "worker-1" 0UL 1UL

                let state, effects =
                    update 1L (LoadRequested(owner, ReplaceCurrent, [| 0uy; 97uy |])) initial

                Expect.isTrue
                    (effects
                     |> List.exists (function
                         | PostToWorker(_, command) ->
                             (match command.Operation with
                              | InspectAndCompile(_, bytes) -> bytes = [| 0uy; 97uy |]
                              | _ -> false)
                         | _ -> false))
                    "artifact must reach typed WorkerEntry"

                let _, refused =
                    update 2L (InvocationRequested(identity "worker-1" 1UL 1UL, Ordinary, [| 1uy |])) state

                Expect.isFalse
                    (refused
                     |> List.exists (function
                         | PostToWorker _ -> true
                         | _ -> false))
                    "uninitialized Worker cannot process"

            testCase "shutdown posts the real shutdown operation"
            <| fun _ ->
                let _, effects =
                    update 1L (ShutdownRequested(identity "worker-1" 1UL 1UL)) (loaded BarProtected)

                Expect.isTrue
                    (effects
                     |> List.exists (function
                         | PostToWorker(_, command) -> command.Operation = ShutdownGuest
                         | _ -> false))
                    "shutdown cannot be an empty process call"

            testCase "active deadline terminates before any queued work can dispatch"
            <| fun _ ->
                let request = identity "worker-1" 1UL 1UL

                let state =
                    loaded Sc2ImportedStrict
                    |> next 1L (InvocationRequested(request, Ordinary, [| 1uy |]))
                    |> next 2L (InvocationRequested(identity "worker-1" 2UL 1UL, Ordinary, [| 2uy |]))

                let timer =
                    {
                        Identity = request
                        Correlation = tokenFor request
                        Phase = AllocateDescriptor
                    }

                let final, effects = update 252L (TimerObserved timer) state
                Expect.equal (Lifecycle.project final).ActiveWorker "" "expired guest cannot remain active"

                Expect.isTrue
                    (match effects with
                     | TerminateWorker "worker-1" :: _ -> true
                     | _ -> false)
                    "termination is the first effect"

                Expect.isFalse
                    (effects
                     |> List.exists (function
                         | PostToWorker _ -> true
                         | _ -> false))
                    "queued request cannot run in an expired guest"

            testCase "BAR resets a real phase budget while SC2 retains enqueue deadline"
            <| fun _ ->
                for path in [ BarProtected; Sc2ImportedStrict ] do
                    let request = identity "worker-1" 1UL 1UL

                    let state =
                        loaded path |> next 1L (InvocationRequested(request, Ordinary, [| 1uy |]))

                    let wrong =
                        OperationToken.create "wrong"
                        |> Result.defaultWith (fun issue -> failtestf "%A" issue)

                    let unchanged =
                        next 200L (WorkerObserved("worker-1", PhaseObserved(request, wrong, AllocateInput))) state

                    Expect.equal
                        (Lifecycle.project unchanged).Current.DeadlineMilliseconds
                        251L
                        "wrong correlation cannot move the deadline"

                    let changed =
                        next
                            200L
                            (WorkerObserved("worker-1", PhaseObserved(request, tokenFor request, AllocateInput)))
                            unchanged

                    let due = if path = BarProtected then 450L else 251L

                    Expect.equal
                        (Lifecycle.project changed).Current.DeadlineMilliseconds
                        due
                        "profile selects phase versus enqueue budget"

                    let duplicate =
                        next
                            210L
                            (WorkerObserved("worker-1", PhaseObserved(request, tokenFor request, AllocateInput)))
                            changed

                    Expect.equal
                        (Lifecycle.project duplicate).Current.DeadlineMilliseconds
                        due
                        "duplicate phase cannot extend the budget"

                    let invalid =
                        next
                            220L
                            (WorkerObserved("worker-1", PhaseObserved(request, tokenFor request, Compile)))
                            duplicate

                    Expect.equal
                        (Lifecycle.project invalid).Current.Phase
                        "allocate-input"
                        "wrong operation phase is ignored"

            testCase "dispose settles a pending compile and initialize once"
            <| fun _ ->
                let initial =
                    Lifecycle.create (settings BarProtected)
                    |> Result.defaultWith (fun issues -> failtestf "%A" issues)

                let request = identity "worker-1" 0UL 1UL
                let state = next 1L (LoadRequested(request, ReplaceCurrent, [| 0uy |])) initial
                let final, effects = update 2L DisposeRequested state
                let _, duplicate = update 3L DisposeRequested final
                Expect.isEmpty duplicate "repeat disposal cannot settle twice"
                Expect.isEmpty (Lifecycle.project final).Controls "compile control is removed"
                Expect.equal (Lifecycle.project final).Deliveries [ 0UL ] "pending load settles once"

                Expect.equal
                    (effects
                     |> List.filter (function
                         | Settle _ -> true
                         | _ -> false))
                        .Length
                    1
                    "one terminal load result"
        ]

let private terminalLimits =
    { MaximumDeadlineMilliseconds = 100
      MaximumOutputBytes = 64
      EnclosingDeadlineMilliseconds = None }

let private loadedSelected policy =
    let baseline = settings Sc2ImportedStrict
    let selected =
        HostSettings.createCompatible (HostSettings.configuration baseline) (Some Controller) policy
        |> Result.defaultWith (fun issues -> failtestf "%A" issues)
    let load = identity "worker-1" 100UL 1UL
    let initialize = identity "worker-1" 101UL 1UL
    Lifecycle.create selected
    |> Result.defaultWith (fun issues -> failtestf "%A" issues)
    |> next 0L (LoadRequested(load, ReplaceCurrent, [| 0uy |]))
    |> next 0L (ready load)
    |> next 0L (WorkerObserved("worker-1", AbiVersionObserved(load, tokenFor load, 0x10000u)))
    |> next 0L (InitializeRequested(initialize, [| 1uy |]))
    |> next 0L (WorkerObserved("worker-1", InvocationObserved(success Initialize initialize, tokenFor initialize)))

let private limited at requested limits state =
    let decision = Lifecycle.submitLimited at requested limits state
    Expect.equal decision.Result (Ok ()) "typed request admitted"
    decision.State

let private terminalResults effects = effects |> List.choose (function Settle result -> Some result | _ -> None)

let private expectRetired state effects terminal expectedWorkers =
    let projection = Lifecycle.project state
    Expect.isTrue projection.Disposed "whole session disposed before effects are interpreted"
    Expect.equal projection.ActiveWorker "" "active cleared"
    Expect.equal projection.CandidateWorker "" "candidate cleared"
    Expect.isEmpty projection.RetiringWorkers "retiring workers cleared"
    Expect.isEmpty projection.Controls "controls cleared"
    Expect.isEmpty projection.Ordinary "ordinary queue cleared"
    Expect.isEmpty projection.Ordered "ordered queue cleared"
    Expect.equal projection.Current.Id 0UL "no current request"
    Expect.equal projection.Snapshot.Id 0UL "held snapshot cleared"
    Expect.isFalse (effects |> List.exists (function PostToWorker _ | ArmTimer _ -> true | _ -> false)) "retirement cannot dispatch or arm work"
    let terminated = effects |> List.choose (function TerminateWorker worker -> Some worker | _ -> None)
    Expect.equal (Set.ofList terminated) (Set.ofList expectedWorkers) "all session workers retire"
    Expect.equal terminated.Length (Set.count (Set.ofList terminated)) "workers terminate once"
    let results = terminalResults effects
    Expect.equal results.[results.Length - 1].Identity terminal "followers destroyed before actual terminal result"
    Expect.equal results.Length (results |> List.map _.Identity |> Set.ofList |> Set.count) "each request settles once"

[<Tests>]
let terminalProcess =
    testList "terminal Process session retirement" [
        for policy in [ DefaultCompatibility; Sc2SupervisorV1 ] do
            for resultState in [ Succeeded; GuestRejected -17; Faulted "process trap"; TimedOut ] do
                testCase (sprintf "%A preserves actual %A and cancels followers" policy resultState) <| fun _ ->
                    let a = identity "worker-1" 1UL 1UL
                    let reset = identity "worker-1" 2UL 1UL
                    let follower = identity "worker-1" 3UL 1UL
                    let candidate = identity "worker-2" 4UL 2UL
                    let state = loadedSelected policy
                    let state = next 0L (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), [| 0uy |])) state
                    let state = limited 0L (LimitedInvoke(a, Ordinary, [| 1uy |])) terminalLimits state
                    let state = limited 0L (LimitedInvokeAndRetire(reset, [| 9uy; 8uy |])) terminalLimits state
                    let state = limited 0L (LimitedShutdown follower) terminalLimits state
                    let state, dispatch = update 1L (WorkerObserved("worker-1", InvocationObserved(success Process a, tokenFor a))) state
                    Expect.isTrue (dispatch |> List.exists (function PostToWorker(_, command) -> command.Identity = reset && command.Operation = ProcessGuest(Ordinary, [| 9uy; 8uy |]) | _ -> false)) "terminal request uses unchanged Process payload once"
                    let actual = { success Process reset with State = resultState; CopiedOutput = [| 7uy |] }
                    let state, effects = update 2L (WorkerObserved("worker-1", InvocationObserved(actual, tokenFor reset))) state
                    expectRetired state effects reset [ "worker-1"; "worker-2" ]
                    Expect.equal (terminalResults effects |> List.last).Outcome (Some actual) "raw result/output retained exactly"
                    let destroyed = terminalResults effects |> List.find (fun result -> result.Identity = follower)
                    Expect.equal destroyed.Outcome None "undispatched follower has no invented ABI outcome"
                    let _, duplicate = update 3L (WorkerObserved("worker-1", InvocationObserved(actual, tokenFor reset))) state
                    Expect.isEmpty duplicate "duplicate terminal callback has no effect"

        testCase "held snapshot promoted behind reset cannot resurrect" <| fun _ ->
            let snapshot = identity "worker-1" 1UL 1UL
            let reset = identity "worker-1" 2UL 1UL
            let held = identity "worker-1" 3UL 1UL
            let state = loadedSelected Sc2SupervisorV1
            let state = limited 0L (LimitedInvoke(snapshot, ReplaceableSnapshot, [| 1uy |])) terminalLimits state
            let state = limited 0L (LimitedInvokeAndRetire(reset, [| 2uy |])) terminalLimits state
            let state = limited 0L (LimitedInvoke(held, ReplaceableSnapshot, [| 3uy |])) terminalLimits state
            Expect.isSome (Lifecycle.projectCompatibility state).HeldSnapshot "snapshot is initially held"
            let state, _ = update 1L (WorkerObserved("worker-1", InvocationObserved(success Process snapshot, tokenFor snapshot))) state
            Expect.equal (Lifecycle.project state).Ordinary.Head.Id held.Request "existing pump promotes held behind reset"
            let state, effects = update 2L (WorkerObserved("worker-1", InvocationObserved(success Process reset, tokenFor reset))) state
            expectRetired state effects reset [ "worker-1" ]
            let _, resumed = update 3L (ResumeRequested "unused") state
            Expect.isEmpty resumed "resume cannot revive retired held work"

        for shutdownFollower in [ false; true ] do
            for observed in [ 4L; 5L; 6L ] do
                testCase (sprintf "Default follower strict due at %d shutdown=%b" observed shutdownFollower) <| fun _ ->
                    let reset = identity "worker-1" 1UL 1UL
                    let follower = identity "worker-1" 2UL 1UL
                    let candidate = identity "worker-2" 3UL 2UL
                    let state = loadedSelected DefaultCompatibility
                    let state = next 0L (LoadRequested(candidate, PrepareCandidate("due-candidate", Some 1UL), [| 0uy |])) state
                    let state = limited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) { terminalLimits with MaximumDeadlineMilliseconds = 3 } state
                    let following = if shutdownFollower then LimitedShutdown follower else LimitedInvokeAndRetire(follower, [| 2uy |])
                    let state = limited 0L following { terminalLimits with MaximumDeadlineMilliseconds = 5 } state
                    let state = next 1L (WorkerObserved("worker-1", PhaseObserved(reset, tokenFor reset, AllocateInput))) state
                    let state = next 2L (WorkerObserved("worker-1", PhaseObserved(reset, tokenFor reset, Process))) state
                    let before = Lifecycle.project state
                    Expect.equal before.Current.DeadlineMilliseconds 3L "phase callbacks keep original active due"
                    Expect.equal before.Ordinary.Head.Id follower.Request "follower is actually queued"
                    Expect.equal before.Ordinary.Head.DeadlineMilliseconds 5L "original admitted follower due"
                    let state, effects = update observed (TimerObserved { Identity = reset; Correlation = tokenFor reset; Phase = Process }) state
                    expectRetired state effects reset [ "worker-1"; "worker-2" ]
                    Expect.equal (Lifecycle.project state).Clock observed "settlement uses exact producer clock"
                    let results = terminalResults effects
                    let destroyed = results |> List.find (fun result -> result.Identity = follower)
                    Expect.equal destroyed.Outcome None "queued follower never invokes an ABI"
                    Expect.equal destroyed.Disposition Discarded "no follower delivery authority"
                    Expect.equal destroyed.Reason (Some(if 5L < observed then DeadlineExpired else HostDisposed)) "strict queued expiry precedes terminal retirement; exact due is unexpired"
                    Expect.equal (results |> List.filter (fun result -> result.Identity = follower)).Length 1 "follower settles once"
                    let actual = results |> List.last
                    Expect.equal actual.Identity reset "actual terminal follows follower settlement"
                    Expect.isTrue (actual.Outcome |> Option.exists (fun outcome -> outcome.State = TimedOut && outcome.Dispatch = Dispatched)) "active Process timeout keeps actual ABI dispatch"
                    let _, duplicate = update (observed + 1L) (TimerObserved { Identity = reset; Correlation = tokenFor reset; Phase = Process }) state
                    Expect.isEmpty duplicate "duplicate timer cannot settle or dispatch again"

        // Actual connected interpreter with declared inert mechanics. Termination
        // advances only this injected port clock, reproducing Observe(Now) reentry.
        // This is not a browser timing or ABI-execution claim.
        for observed in [ 4L; 5L; 6L ] do
            testCase (sprintf "connected retirement decision clock survives reentry at %d" observed) <| fun _ ->
                let mutable portClock = 0L
                let mutable connected: Host option = None
                let timers = System.Collections.Generic.Dictionary<string, int * (unit -> unit)>()
                let events = ResizeArray<string * int64>()
                let results = ResizeArray<BrowserResult * int64>()
                let posts = ResizeArray<WorkerWireCommand>()
                let transport: HostTransport =
                    { Now = fun () -> portClock
                      CreateWorker = fun _ _ -> ()
                      PostCommand = fun _ command -> posts.Add command
                      TerminateWorker = fun worker ->
                          events.Add(("terminate:" + worker, connected.Value.Projection.Clock))
                          portClock <- 6L
                      ArmTimer = fun key delay callback -> timers[key] <- (delay, callback)
                      CancelTimer = fun key -> timers.Remove key |> ignore }
                let receive (result: BrowserResult) =
                    let clock = connected.Value.Projection.Clock
                    events.Add(("settle:" + string result.Identity.Request, clock))
                    results.Add((result, clock))
                let host = Host.CreateConnected(settings Sc2ImportedStrict, transport, receive) |> Result.defaultWith (fun issues -> failtestf "%A" issues)
                connected <- Some host
                let load = identity "worker-1" 100UL 1UL
                let initialize = identity "worker-1" 101UL 1UL
                let reset = identity "worker-1" 1UL 1UL
                let follower = identity "worker-1" 2UL 1UL
                host.Load(0L, load, [| 0uy |])
                host.Observe(0L, "worker-1", PhaseObserved(load, tokenFor load, Compile))
                host.Observe(0L, "worker-1", AbiVersionObserved(load, tokenFor load, 0x10000u))
                host.Initialize(0L, initialize, [| 0uy |])
                host.Observe(0L, "worker-1", InvocationObserved(success Initialize initialize, tokenFor initialize))
                host.SubmitLimited(0L, LimitedInvokeAndRetire(reset, [| 1uy |]), { terminalLimits with MaximumDeadlineMilliseconds = 3 }) |> fun result -> Expect.equal result (Ok ()) "terminal admitted"
                host.SubmitLimited(0L, LimitedShutdown follower, { terminalLimits with MaximumDeadlineMilliseconds = 5 }) |> fun result -> Expect.equal result (Ok ()) "follower admitted"
                for at, phase in [ 1L, AllocateInput; 2L, Process ] do
                    portClock <- at
                    host.Observe(at, "worker-1", PhaseObserved(reset, tokenFor reset, phase))
                Expect.equal host.Projection.Current.Id reset.Request "actual current terminal identity"
                Expect.equal host.Projection.Current.Worker reset.WorkerInstance "actual current worker"
                Expect.equal host.Projection.Current.DeadlineMilliseconds 3L "active due remains original"
                Expect.equal host.Projection.Ordinary.Head.Id follower.Request "actual queued follower identity"
                Expect.equal host.Projection.Ordinary.Head.DeadlineMilliseconds 5L "actual queued due"
                Expect.equal timers.Count 1 "only actual active phase timer remains"
                let timer = timers |> Seq.exactlyOne
                Expect.equal timer.Key $"{reset.WorkerInstance}:{reset.Generation}:{reset.Request}:{OperationToken.value (tokenFor reset)}:Process" "exact current request/correlation/phase timer selected"
                let delay, fire = timer.Value
                Expect.equal delay 2 "actual strict timer uses due-now+1"
                events.Clear(); results.Clear(); posts.Clear()
                portClock <- observed
                fire ()
                let firstTermination = events |> Seq.find (fun (kind, _) -> kind.StartsWith("terminate:"))
                Expect.equal (snd firstTermination) observed "capture outer producer clock before termination reentry"
                let destroyed, callbackClock = results |> Seq.find (fun (result, _) -> result.Identity = follower)
                Expect.equal destroyed.Reason (Some(if 5L < observed then DeadlineExpired else HostDisposed)) "reason binds decision, not later callback clock"
                Expect.equal destroyed.Outcome None "no follower ABI"
                Expect.equal destroyed.Disposition Discarded "no follower authority"
                Expect.equal callbackClock 6L "connected termination advances callback clock even when queue was unexpired"
                let kinds = events |> Seq.map fst |> Seq.toList
                Expect.equal ((kinds |> List.findIndex ((=) "settle:2")) < (kinds |> List.findIndex ((=) "terminate:worker-1"))) (5L < observed) "queued expiry precedes termination; disposal follows it"
                Expect.equal kinds.[kinds.Length - 1] "settle:1" "terminal result follows retirement/follower"
                Expect.isTrue host.Projection.Disposed "whole session retired"
                Expect.isEmpty posts "no following post"
                let count = results.Count
                fire ()
                Expect.equal results.Count count "stale real timer callback cannot settle twice"

        for observed, expired in [ 4L, false; 5L, true; 6L, true ] do
            testCase (sprintf "queued terminal head boundary %d" observed) <| fun _ ->
                let a = identity "worker-1" 1UL 1UL
                let reset = identity "worker-1" 2UL 1UL
                let state = loadedSelected Sc2SupervisorV1
                let state = limited 0L (LimitedInvoke(a, Ordinary, [| 1uy |])) terminalLimits state
                let state = limited 0L (LimitedInvokeAndRetire(reset, [| 2uy |])) { terminalLimits with MaximumDeadlineMilliseconds = 5 } state
                let state, effects = update observed (WorkerObserved("worker-1", InvocationObserved(success Process a, tokenFor a))) state
                Expect.equal (Lifecycle.project state).Disposed expired "inclusive queue-head deadline preserved"
                if expired then
                    Expect.isFalse (effects |> List.exists (function PostToWorker _ -> true | _ -> false)) "expired reset never dispatches"
                    let result = terminalResults effects |> List.find (fun result -> result.Identity = reset)
                    Expect.isTrue (result.Outcome |> Option.exists (fun outcome -> outcome.State = TimedOut && outcome.Dispatch = NotDispatched)) "queued expiry keeps undispatched first cause"
                else Expect.equal (Lifecycle.project state).Current.Id reset.Request "before due reset dispatches"

        for failure in [ "timeout"; "worker-failed"; "worker-terminated"; "cleanup" ] do
            testCase ("executing terminal " + failure + " retires candidate and held state") <| fun _ ->
                let reset = identity "worker-1" 1UL 1UL
                let follower = identity "worker-1" 2UL 1UL
                let candidate = identity "worker-2" 4UL 2UL
                let state = loadedSelected Sc2SupervisorV1
                let state = next 0L (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), [| 0uy |])) state
                let state = limited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) { terminalLimits with MaximumDeadlineMilliseconds = 5 } state
                let state = limited 0L (LimitedInvoke(follower, ReplaceableSnapshot, [| 2uy |])) terminalLimits state
                let state = next 1L (WorkerObserved("worker-1", PhaseObserved(reset, tokenFor reset, AllocateInput))) state
                let state = next 2L (WorkerObserved("worker-1", PhaseObserved(reset, tokenFor reset, Process))) state
                Expect.equal (Lifecycle.project state).Current.DeadlineMilliseconds 5L "phase reports cannot renew selected terminal budget"
                let actual = { success Process reset with State = GuestRejected 93; CleanupFault = Some { Phase = Free; Diagnostic = "free trap" } }
                let at, event =
                    match failure with
                    | "timeout" -> 6L, TimerObserved { Identity = reset; Correlation = tokenFor reset; Phase = Process }
                    | "worker-failed" -> 3L, WorkerObserved("worker-1", WorkerFailed(reset, Some(tokenFor reset), "transport failed"))
                    | "worker-terminated" -> 3L, WorkerObserved("worker-1", WorkerTerminated "worker-1")
                    | _ -> 3L, WorkerObserved("worker-1", InvocationTerminated(actual, tokenFor reset))
                let state, effects = update at event state
                expectRetired state effects reset [ "worker-1"; "worker-2" ]
                let outcome = (terminalResults effects |> List.last).Outcome.Value
                Expect.equal outcome.Identity reset "failure retains exact terminal identity"
                Expect.equal outcome.Dispatch Dispatched "process-phase failure retains dispatch evidence"
                if failure = "cleanup" then Expect.equal outcome actual "cleanup fault and rejected return preserved"
                elif failure = "timeout" then Expect.equal outcome.State TimedOut "timeout retained"
                else Expect.isTrue (match outcome.State with Faulted _ -> true | _ -> false) "worker loss retains fault"

        testCase "invalid limits, freeze cancellation and wrong correlation cannot retire" <| fun _ ->
            let reset = identity "worker-1" 1UL 1UL
            let state = loadedSelected Sc2SupervisorV1
            let invalid = Lifecycle.submitLimited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) { terminalLimits with MaximumDeadlineMilliseconds = 0 } state
            Expect.isError invalid.Result "invalid terminal request refused before admission"
            Expect.equal (Lifecycle.project invalid.State) (Lifecycle.project state) "pure refusal leaves session unchanged"
            Expect.isEmpty invalid.Effects "no refusal invocation"
            let state = limited 0L (LimitedInvoke(identity "worker-1" 2UL 1UL, Ordinary, [| 2uy |])) terminalLimits state
            let state = limited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) terminalLimits state
            let state = next 1L (FreezeRequested "fence") state
            let state, effects = update 2L (WorkerObserved("worker-1", InvocationObserved(success Process reset, tokenFor reset))) state
            Expect.isFalse (Lifecycle.project state).Disposed "canceled queued reset never gains retirement authority"
            Expect.isEmpty effects "late canceled reset ignored"
            let state = loadedSelected Sc2SupervisorV1 |> limited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) terminalLimits
            let wrong = OperationToken.create "wrong" |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            let state, effects = update 1L (WorkerObserved("worker-1", InvocationObserved(success Process reset, wrong))) state
            Expect.isFalse (Lifecycle.project state).Disposed "wrong correlation cannot retire current request"
            Expect.isEmpty effects "forged callback ignored"

        testCase "terminal result from retired generation clears active candidate and retiring roles" <| fun _ ->
            let reset = identity "worker-1" 1UL 1UL
            let candidate = identity "worker-2" 2UL 2UL
            let initialize = identity "worker-2" 3UL 2UL
            let future = identity "worker-3" 4UL 3UL
            let state = loadedSelected Sc2SupervisorV1 |> limited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) terminalLimits
            let state = next 0L (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), [| 0uy |])) state
            let state = next 0L (ready candidate) state
            let state = next 0L (WorkerObserved("worker-2", AbiVersionObserved(candidate, tokenFor candidate, 0x10000u))) state
            let state = next 0L (InitializeRequested(initialize, [| 1uy |])) state
            let state = next 0L (WorkerObserved("worker-2", InvocationObserved(success Initialize initialize, tokenFor initialize))) state
            let state = next 0L (CandidateValidated("tx", 2UL)) state
            let state = next 0L (CandidateCommitRequested("tx", 1UL, 2UL)) state
            let state = next 0L (LoadRequested(future, PrepareCandidate("future", Some 2UL), [| 0uy |])) state
            Expect.equal (Lifecycle.project state).RetiringWorkers [ "worker-1" ] "terminal request still belongs to retiring generation"
            let actual = { success Process reset with CopiedOutput = [| 8uy |] }
            let state, effects = update 1L (WorkerObserved("worker-1", InvocationObserved(actual, tokenFor reset))) state
            expectRetired state effects reset [ "worker-1"; "worker-2"; "worker-3" ]
            let result = terminalResults effects |> List.last
            Expect.equal result.Outcome (Some actual) "historical actual result retained"
            Expect.equal result.Disposition HistoricalOnly "retired generation gains no current authority"

        testCase "terminal ordinary capacity refusal leaves existing session work intact" <| fun _ ->
            let state = loadedSelected Sc2SupervisorV1
            let state = limited 0L (LimitedInvoke(identity "worker-1" 1UL 1UL, Ordinary, [| 1uy |])) terminalLimits state
            let state = [ 2UL .. 5UL ] |> List.fold (fun current request -> limited 0L (LimitedInvoke(identity "worker-1" request 1UL, OrderedEvent, [| 1uy |])) terminalLimits current) state
            let decision = Lifecycle.submitLimited 0L (LimitedInvokeAndRetire(identity "worker-1" 6UL 1UL, [| 2uy |])) terminalLimits state
            Expect.equal decision.Result (Error [ RequestRefused QueueFull ]) "terminal uses existing ordinary queue cap"
            Expect.isFalse (Lifecycle.project decision.State).Disposed "producer does not invent product overflow policy"
            Expect.equal (Lifecycle.project decision.State).Current.Id 1UL "earlier work retained"
            Expect.equal (Lifecycle.project decision.State).Ordinary.Length 4 "no overflow request admitted"

        for shutdownFollower in [ false; true ] do
            testCase (sprintf "ordinary Shutdown ABI remains terminal before follower shutdown=%b" shutdownFollower) <| fun _ ->
                let first = identity "worker-1" 1UL 1UL
                let follower = identity "worker-1" 2UL 1UL
                let state = loadedSelected Sc2SupervisorV1
                let decision = Lifecycle.submitLimited 0L (LimitedShutdown first) terminalLimits state
                Expect.isTrue (decision.Effects |> List.exists (function PostToWorker(_,command) -> command.Operation = ShutdownGuest | _ -> false)) "Shutdown still selects actual shutdown ABI"
                let following = if shutdownFollower then LimitedShutdown follower else LimitedInvokeAndRetire(follower, [| 1uy |])
                let state = limited 0L following terminalLimits decision.State
                let state, effects = update 1L (WorkerObserved("worker-1", InvocationObserved(success Shutdown first, tokenFor first))) state
                Expect.isFalse (effects |> List.exists (function PostToWorker _ -> true | _ -> false)) "neither reset nor shutdown follower dispatches"
                Expect.equal ((terminalResults effects |> List.filter (fun result -> result.Identity = follower)).Length) 1 "undispatched terminal follower settles once"
                Expect.equal (Lifecycle.project state).ActiveWorker "" "ordinary Shutdown retains active-worker retirement"

        testCase "candidate initializing control is retired before terminal result" <| fun _ ->
            let reset = identity "worker-1" 1UL 1UL
            let candidate = identity "worker-2" 2UL 2UL
            let initialize = identity "worker-2" 3UL 2UL
            let state = loadedSelected Sc2SupervisorV1 |> limited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) terminalLimits
            let state = next 0L (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), [| 0uy |])) state
            let state = next 0L (ready candidate) state
            let state = next 0L (WorkerObserved("worker-2", AbiVersionObserved(candidate, tokenFor candidate, 0x10000u))) state
            let state = next 0L (InitializeRequested(initialize, [| 1uy |])) state
            let state, effects = update 1L (WorkerObserved("worker-1", InvocationObserved(success Process reset, tokenFor reset))) state
            expectRetired state effects reset [ "worker-1"; "worker-2" ]
            Expect.equal ((terminalResults effects |> List.filter (fun result -> result.Identity = initialize)).Length) 1 "initializing control settles once before reset"
            let state, late = update 2L (WorkerObserved("worker-2", InvocationObserved(success Initialize initialize, tokenFor initialize))) state
            Expect.isTrue (Lifecycle.project state).Disposed "late initialization cannot resurrect candidate"
            Expect.isEmpty late "late initialization settles nothing twice"

        testCase "adapter rejection retires terminal session before rejected result" <| fun _ ->
            let reset = identity "worker-1" 1UL 1UL
            let follower = identity "worker-1" 2UL 1UL
            let candidate = identity "worker-2" 3UL 2UL
            let state = loadedSelected Sc2SupervisorV1 |> limited 0L (LimitedInvokeAndRetire(reset, [| 1uy |])) terminalLimits
            let state = limited 0L (LimitedInvokeAndRetire(follower, [| 2uy |])) terminalLimits state
            let state = next 0L (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), [| 0uy |])) state
            let state, effects = update 1L (AdapterOutputRejected(reset, "adapter rejection")) state
            expectRetired state effects reset [ "worker-1"; "worker-2" ]
            let result = terminalResults effects |> List.last
            Expect.equal result.Reason (Some(AdapterRejected "adapter rejection")) "adapter first cause retained"
            Expect.equal result.Outcome None "adapter refusal invents no actual ABI outcome"
    ]

let private fresh path =
    Lifecycle.create (settings path) |> Result.defaultWith (fun issues -> failtestf "%A" issues)

let private settled effects =
    effects |> List.choose (function Settle result -> Some result | _ -> None)

let private startupOutcome load =
    Some
        { Identity = load
          Phase = Compile
          State = TimedOut
          Dispatch = NotDispatched
          CleanupFault = None
          CopiedOutput = Array.empty
          EffectEligibility = ProductAdapterMustDecide }

[<Tests>]
let workerStartup =
    testList "Worker startup is bounded separately from policy" [
        for path in [ BarProtected; Sc2ImportedStrict ] do
            testCase (sprintf "%A startup bound refuses distinctly and retires the Worker" path) <| fun _ ->
                let load = identity "worker-1" 1UL 1UL
                let state, effects = update 10L (LoadRequested(load, ReplaceCurrent, [| 0uy |])) (fresh path)
                let control = (Lifecycle.project state).Controls |> List.exactlyOne
                Expect.equal control.Phase "start" "a fresh Worker is starting until it reports readiness"
                Expect.equal control.DeadlineMilliseconds (10L + int64 HostSettings.defaultWorkerStartTimeoutMilliseconds) "startup has its own bound"
                Expect.equal (Lifecycle.projectEffects effects |> List.map _.Kind) [ "createActive"; "armTimer"; "post" ] "the command is posted at creation and queues until readiness"
                let atBound, none = update 5010L (TimerObserved { Identity = load; Correlation = tokenFor load; Phase = Compile }) state
                Expect.isEmpty (settled none) "the startup bound itself is still admissible"
                let expired, effects = update 5011L (TimerObserved { Identity = load; Correlation = tokenFor load; Phase = Compile }) atBound
                let result = settled effects |> List.exactlyOne
                Expect.equal result.Reason (Some WorkerStartTimeout) "startup expiry is not a policy DeadlineExpired"
                Expect.equal result.Disposition Discarded "no game authority on a startup refusal"
                Expect.equal result.Outcome (startupOutcome load) "no guest dispatch is claimed"
                Expect.contains (Lifecycle.projectEffects effects |> List.map _.Kind) "settleStartTimedOut" "projected distinctly"
                Expect.isTrue (effects |> List.exists (function TerminateWorker "worker-1" -> true | _ -> false)) "the unready Worker is terminated"
                Expect.equal (Lifecycle.project expired).ActiveWorker "" "no active authority remains"
                let late, lateEffects = update 5012L (WorkerObserved("worker-1", PhaseObserved(load, tokenFor load, Compile))) expired
                Expect.isEmpty lateEffects "a late readiness report cannot restore the Worker"
                Expect.equal (Lifecycle.project late).ActiveWorker "" "still no authority"

            testCase (sprintf "%A readiness starts the policy deadline" path) <| fun _ ->
                let load = identity "worker-1" 1UL 1UL
                let state = fresh path |> next 0L (LoadRequested(load, ReplaceCurrent, [| 0uy |]))
                let readyState, effects = update 4000L (ready load) state
                let control = (Lifecycle.project readyState).Controls |> List.exactlyOne
                Expect.equal control.Phase "compile" "readiness admits compilation"
                Expect.equal control.DeadlineMilliseconds 4250L "the policy budget starts at readiness"
                Expect.equal (Lifecycle.project readyState).LastAction "workerReady" "readiness is explicit"
                Expect.equal
                    (Lifecycle.projectEffects effects |> List.map (fun effect -> effect.Kind, effect.Due))
                    [ "cancelTimer", 0L; "armTimer", 4250L ]
                    "the startup timer is replaced by the policy timer"
                let duplicate, none = update 4100L (ready load) readyState
                Expect.isEmpty none "a repeated readiness report cannot renew the budget"
                Expect.equal ((Lifecycle.project duplicate).Controls |> List.exactlyOne).DeadlineMilliseconds 4250L "budget unchanged"
                let version = if path = BarProtected then 1u else 0x10000u
                let compiled = next 4250L (WorkerObserved("worker-1", AbiVersionObserved(load, tokenFor load, version))) duplicate
                Expect.equal (Lifecycle.project compiled).CompiledWorkers [ "worker-1" ] "compilation within the policy budget succeeds"
                let expired, expiry = update 4251L (TimerObserved { Identity = load; Correlation = tokenFor load; Phase = Compile }) duplicate
                Expect.equal ((settled expiry |> List.exactlyOne).Reason) (Some DeadlineExpired) "policy expiry after readiness remains DeadlineExpired"
                Expect.equal (Lifecycle.project expired).ActiveWorker "" "policy expiry retires the Worker"

        testCase "compilation and foreign readiness before readiness are inert" <| fun _ ->
            let load = identity "worker-1" 1UL 1UL
            let state = fresh Sc2ImportedStrict |> next 0L (LoadRequested(load, ReplaceCurrent, [| 0uy |]))
            let before = Lifecycle.project state
            let wrong = OperationToken.create "wrong" |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            for observation in
                [ AbiVersionObserved(load, tokenFor load, 0x10000u)
                  PhaseObserved(load, wrong, Compile)
                  PhaseObserved({ load with Generation = 2UL }, tokenFor load, Compile)
                  PhaseObserved(load, tokenFor load, Instantiate) ] do
                let after, effects = update 1L (WorkerObserved("worker-1", observation)) state
                Expect.isEmpty effects (sprintf "%A cannot advance a starting Worker" observation)
                Expect.equal { (Lifecycle.project after) with Clock = before.Clock } before "state unchanged"
            let foreign, effects = update 1L (WorkerObserved("worker-2", PhaseObserved(load, tokenFor load, Compile))) state
            Expect.isEmpty effects "another Worker's report is inert"
            Expect.equal ((Lifecycle.project foreign).Controls |> List.exactlyOne).Phase "start" "still starting"

        testCase "startup bound is configurable within a validated range" <| fun _ ->
            let baseline = settings Sc2ImportedStrict
            Expect.equal (HostSettings.workerStartTimeoutMilliseconds baseline) 5000 "default startup bound"
            for invalid in [ 0; -1; HostSettings.maximumWorkerStartTimeoutMilliseconds + 1 ] do
                Expect.equal (HostSettings.withWorkerStartTimeout invalid baseline) (Error [ InvalidSchedulingLimit "workerStartTimeoutMilliseconds" ]) "out of range refused"
            let selected = HostSettings.withWorkerStartTimeout 120 baseline |> Result.defaultWith (fun issues -> failtestf "%A" issues)
            let compatible =
                HostSettings.createCompatible (HostSettings.configuration baseline) (Some Controller) Sc2SupervisorV1
                |> Result.bind (HostSettings.withWorkerStartTimeout 120)
                |> Result.defaultWith (fun issues -> failtestf "%A" issues)
            Expect.equal (HostSettings.compatibility compatible) Sc2SupervisorV1 "compatibility retained"
            let load = identity "worker-1" 1UL 1UL
            for chosen in [ selected; compatible ] do
                let state =
                    Lifecycle.create chosen |> Result.defaultWith (fun issues -> failtestf "%A" issues)
                    |> next 3L (LoadRequested(load, ReplaceCurrent, [| 0uy |]))
                Expect.equal ((Lifecycle.project state).Controls |> List.exactlyOne).DeadlineMilliseconds 123L "selected bound applies"

        testCase "an enclosing deadline caps startup and the policy budget" <| fun _ ->
            let load = identity "worker-1" 1UL 1UL
            let configuration = HostSettings.configuration (settings Sc2ImportedStrict)
            let limits = { MaximumDeadlineMilliseconds = 250; MaximumOutputBytes = 64; EnclosingDeadlineMilliseconds = Some 100L }
            let state = limited 0L (LimitedLoad(load, configuration, [| 0uy |])) limits (fresh Sc2ImportedStrict)
            Expect.equal ((Lifecycle.project state).Controls |> List.exactlyOne).DeadlineMilliseconds 100L "caller budget caps startup"
            let readyState = next 40L (ready load) state
            Expect.equal ((Lifecycle.project readyState).Controls |> List.exactlyOne).DeadlineMilliseconds 100L "caller budget caps policy"
            let _, effects = update 101L (TimerObserved { Identity = load; Correlation = tokenFor load; Phase = Compile }) state
            Expect.equal ((settled effects |> List.exactlyOne).Reason) (Some WorkerStartTimeout) "unready at the caller bound is a startup refusal"

        testCase "candidate startup refusal preserves active authority" <| fun _ ->
            let candidate = identity "worker-2" 200UL 2UL
            let state = loaded Sc2ImportedStrict |> next 10L (LoadRequested(candidate, PrepareCandidate("tx", Some 1UL), [| 0uy |]))
            let after, effects = update 5011L (TimerObserved { Identity = candidate; Correlation = tokenFor candidate; Phase = Compile }) state
            Expect.equal ((settled effects |> List.exactlyOne).Reason) (Some WorkerStartTimeout) "candidate refused distinctly"
            let projection = Lifecycle.project after
            Expect.equal projection.ActiveWorker "worker-1" "active retained"
            Expect.equal projection.InitializedWorkers [ "worker-1" ] "active readiness retained"
            Expect.equal projection.CandidateWorker "" "candidate removed"

        testCase "connected interpreter re-arms a browser timer that fires before its due time" <| fun _ ->
            let mutable clock = 0L
            let timers = System.Collections.Generic.Dictionary<string, int * (unit -> unit)>()
            let results = ResizeArray<BrowserResult>()
            let transport: HostTransport =
                { Now = fun () -> clock
                  CreateWorker = fun _ _ -> ()
                  PostCommand = fun _ _ -> ()
                  TerminateWorker = fun _ -> ()
                  ArmTimer = fun key delay callback -> timers[key] <- (delay, callback)
                  CancelTimer = fun key -> timers.Remove key |> ignore }
            let host = Host.CreateConnected(settings Sc2ImportedStrict, transport, results.Add) |> Result.defaultWith (fun issues -> failtestf "%A" issues)
            let load = identity "worker-1" 1UL 1UL
            host.Load(0L, load, [| 0uy |])
            host.Observe(0L, "worker-1", PhaseObserved(load, tokenFor load, Compile))
            let key, (delay, fire) = timers |> Seq.exactlyOne |> fun pair -> pair.Key, pair.Value
            Expect.equal delay 251 "policy timer is armed from readiness"
            clock <- 250L
            fire ()
            Expect.isEmpty results "an early browser callback is not an expiry observation"
            Expect.equal (fst timers[key]) 1 "the remaining time is re-armed"
            clock <- 251L
            (snd timers[key]) ()
            Expect.equal (results |> Seq.exactlyOne |> _.Reason) (Some DeadlineExpired) "the re-armed timer expires the looping load"
            Expect.equal host.Projection.ActiveWorker "" "the Worker is retired"

        testCase "disposal during startup settles the starting load once" <| fun _ ->
            let load = identity "worker-1" 1UL 1UL
            let state = fresh Sc2ImportedStrict |> next 0L (LoadRequested(load, ReplaceCurrent, [| 0uy |]))
            let disposed, effects = update 1L DisposeRequested state
            Expect.equal ((settled effects |> List.exactlyOne).Reason) (Some HostDisposed) "disposal reason retained"
            Expect.isTrue (effects |> List.exists (function TerminateWorker "worker-1" -> true | _ -> false)) "starting Worker terminated"
            Expect.isTrue (effects |> List.exists (function CancelTimer _ -> true | _ -> false)) "startup timer canceled"
            let _, late = update 2L (ready load) disposed
            Expect.isEmpty late "readiness after disposal is inert"
    ]
