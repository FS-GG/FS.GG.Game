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

let private token =
    OperationToken.create "lifecycle-test"
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

    next 1L (LoadRequested(identity "worker-1" 0UL 1UL, ReplaceCurrent, Array.empty)) state

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
                    next 4L (WorkerObserved("worker-2", InvocationObserved(success Initialize candidate, token))) state

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
                    WorkerObserved("worker-1", InvocationObserved(success Process request, token))

                let state, effects = update 4L observation state

                Expect.equal
                    (Lifecycle.projectEffects effects |> List.map _.Kind)
                    [ "settleHistorical" ]
                    "freeze suppresses current delivery"

                let state, duplicate = update 5L observation state
                Expect.isEmpty duplicate "a duplicate completion emits nothing"
                Expect.equal (Lifecycle.project state).Deliveries [ 1UL ] "request settles exactly once"

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
                    |> List.filter (fun effect -> effect.Request = 1UL)

                Expect.equal settlements.Length 1 "rejection cannot duplicate terminal delivery"

                Expect.equal
                    (Lifecycle.project final).Deliveries
                    [ 1UL ]
                    "the delivery ledger contains one terminal entry"

                Expect.isTrue (Lifecycle.project final).Disposed "adapter rejection closes the host"
        ]
