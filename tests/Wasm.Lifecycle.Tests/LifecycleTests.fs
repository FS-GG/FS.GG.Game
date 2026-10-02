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
                    next
                        3L
                        (WorkerObserved("worker-2", AbiVersionObserved(candidate, tokenFor candidate, 0x10000u)))
                        state

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
