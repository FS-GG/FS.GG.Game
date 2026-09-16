module Game.Core.Tests.SessionContractTests

open Expecto
open FS.GG.Game.Core

let private identity =
    {
        ContractVersion = 1
        EngineId = "example.engine"
        EngineVersion = "1.2.3"
        ProfileId = "example.profile/1"
        SchemaId = "example.state"
        SchemaVersion = 2
    }

type private State =
    {
        SessionId: string
        Compatibility: SessionCompatibility
        Value: int
        Revision: uint64
    }

let private failure code message =
    Error { Code = code; Message = message }

let private getOk result =
    match result with
    | Ok value -> value
    | Error error -> failtestf "expected Ok, got %s: %s" error.Code error.Message

let private contract: SessionContract<int, State, int, int, int> =
    {
        Initialize =
            fun request ->
                match SessionEnvelope.validateInitialization request with
                | [] ->
                    Ok
                        {
                            SessionId = request.SessionId
                            Compatibility = request.Compatibility
                            Value = request.Configuration
                            Revision = 0UL
                        }
                | _ -> failure "invalid-initialization" "invalid session initialization"
        AdmitInput =
            fun input state ->
                if input.SessionId <> state.SessionId then
                    failure "wrong-session" "input belongs to another session"
                else
                    Ok
                        { state with
                            Value = state.Value + input.Value
                            Revision = state.Revision + 1UL
                        }
        Advance =
            fun observation state ->
                if observation.SessionId <> state.SessionId then
                    failure "wrong-session" "advance belongs to another session"
                else
                    Ok
                        { state with
                            Value = state.Value + int observation.StepCount
                            Revision = state.Revision + 1UL
                        }
        Project =
            fun state ->
                {
                    SessionId = state.SessionId
                    Revision = state.Revision
                    Value = state.Value
                }
        Snapshot =
            fun state ->
                {
                    SessionId = state.SessionId
                    Revision = state.Revision
                    Compatibility = state.Compatibility
                    Value = state.Value
                }
        Restore =
            fun snapshot ->
                if not (SessionCompatibility.isCompatible identity snapshot.Compatibility) then
                    failure "incompatible-snapshot" "snapshot identity differs"
                else
                    Ok
                        {
                            SessionId = snapshot.SessionId
                            Compatibility = snapshot.Compatibility
                            Value = snapshot.Value
                            Revision = snapshot.Revision
                        }
    }

let private runtime () =
    match
        SessionRuntime.initialize
            {
                StepMicroseconds = 10_000UL
                MaxCatchUpSteps = 3u
            }
            contract
            {
                SessionId = "session-1"
                Compatibility = identity
                Configuration = 10
            }
    with
    | Ok value -> value
    | Error error -> failtestf "runtime initialization failed: %A" error

[<Tests>]
let tests =
    testList
        "Game.Core portable session contract"
        [
            test "a consumer can implement and traverse every contract seam" {
                let initialized =
                    contract.Initialize
                        {
                            SessionId = "session-1"
                            Compatibility = identity
                            Configuration = 10
                        }
                    |> getOk

                let admitted =
                    contract.AdmitInput
                        {
                            SessionId = "session-1"
                            InputId = "game.add"
                            Sequence = 1UL
                            Value = 4
                        }
                        initialized
                    |> getOk

                let advanced =
                    contract.Advance
                        {
                            SessionId = "session-1"
                            StepCount = 2UL
                        }
                        admitted
                    |> getOk

                let projection = contract.Project advanced
                let snapshot = contract.Snapshot advanced
                let restored = contract.Restore snapshot |> getOk

                Expect.equal projection.Value 16 "projection reads the accepted state"
                Expect.equal projection.Revision 2UL "input and advancement each create one revision"
                Expect.equal restored advanced "snapshot restoration preserves the consumer state"
            }

            test "compatibility compares every identity axis in stable order" {
                let actual =
                    {
                        ContractVersion = 2
                        EngineId = "other.engine"
                        EngineVersion = "2.0.0"
                        ProfileId = "other.profile/1"
                        SchemaId = "other.state"
                        SchemaVersion = 3
                    }

                Expect.equal
                    (SessionCompatibility.compare identity actual)
                    [
                        SessionCompatibilityIssue.ContractVersion(1, 2)
                        SessionCompatibilityIssue.EngineId("example.engine", "other.engine")
                        SessionCompatibilityIssue.EngineVersion("1.2.3", "2.0.0")
                        SessionCompatibilityIssue.ProfileId("example.profile/1", "other.profile/1")
                        SessionCompatibilityIssue.SchemaId("example.state", "other.state")
                        SessionCompatibilityIssue.SchemaVersion(2, 3)
                    ]
                    "no compatibility axis is silently ignored"
            }

            test "malformed identities and envelope identifiers are rejected" {
                let malformed =
                    {
                        ContractVersion = 0
                        EngineId = " "
                        EngineVersion = ""
                        ProfileId = ""
                        SchemaId = ""
                        SchemaVersion = -1
                    }

                let initialization =
                    {
                        SessionId = ""
                        Compatibility = malformed
                        Configuration = ()
                    }

                let input =
                    {
                        SessionId = " "
                        InputId = ""
                        Sequence = 0UL
                        Value = ()
                    }

                Expect.equal
                    (SessionEnvelope.validateInitialization initialization)
                    [
                        SessionContractIssue.MissingSessionId
                        SessionContractIssue.InvalidContractVersion 0
                        SessionContractIssue.MissingEngineId
                        SessionContractIssue.MissingEngineVersion
                        SessionContractIssue.MissingProfileId
                        SessionContractIssue.MissingSchemaId
                        SessionContractIssue.InvalidSchemaVersion -1
                    ]
                    "all invalid initialization fields are reported"

                Expect.equal
                    (SessionEnvelope.validateInput input)
                    [ SessionContractIssue.MissingSessionId; SessionContractIssue.MissingInputId ]
                    "semantic input requires both identities"
            }

            test "bounded integer time produces deterministic whole steps and explicit clamp evidence" {
                let initial = runtime ()

                let first, firstEffects =
                    SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 25_000UL) initial

                let second, secondEffects =
                    SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 45_000UL) first

                Expect.equal first.Current.Value 12 "two whole steps advance"
                Expect.equal first.AccumulatorMicroseconds 5_000UL "the sub-step remainder is retained"

                Expect.equal
                    firstEffects
                    [
                        SessionRuntimeEffect.Advanced 2UL
                        SessionRuntimeEffect.ProjectionReady(contract.Project first.Current)
                    ]
                    "advance effects are ordered"

                Expect.equal second.Current.Value 15 "catch-up work is bounded to three steps"
                Expect.equal second.AccumulatorMicroseconds 9_999UL "bounded catch-up keeps only a sub-step remainder"

                Expect.equal
                    secondEffects.Head
                    (SessionRuntimeEffect.CatchUpClamped 10_001UL)
                    "dropped suspension time is explicit"
            }

            test "runtime policy and portable envelopes are validated before product execution" {
                let badClock =
                    SessionRuntime.initialize
                        {
                            StepMicroseconds = 0UL
                            MaxCatchUpSteps = 3u
                        }
                        contract
                        {
                            SessionId = "session-1"
                            Compatibility = identity
                            Configuration = 10
                        }

                let badEnvelope =
                    SessionRuntime.initialize
                        {
                            StepMicroseconds = 10_000UL
                            MaxCatchUpSteps = 3u
                        }
                        contract
                        {
                            SessionId = ""
                            Compatibility = identity
                            Configuration = 10
                        }

                Expect.equal
                    badClock
                    (Error(SessionRuntimeRefusal.InvalidStepMicroseconds 0UL))
                    "a zero tick is refused"

                Expect.equal
                    badEnvelope
                    (Error(SessionRuntimeRefusal.InvalidEnvelope [ SessionContractIssue.MissingSessionId ]))
                    "the generic boundary validates before the product contract"
            }

            test "pause step and resume keep the deterministic clock separate from presentation time" {
                let initial = runtime ()

                let paused, _ =
                    SessionRuntime.update contract SessionRuntimeObservation.Pause initial

                let ignored, effects =
                    SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 50_000UL) paused

                let stepped, _ =
                    SessionRuntime.update contract SessionRuntimeObservation.StepOnce ignored

                let resumed, _ =
                    SessionRuntime.update contract SessionRuntimeObservation.Resume stepped

                Expect.equal ignored paused "elapsed host time does not mutate a paused session"
                Expect.isEmpty effects "a paused clock emits no projection"
                Expect.equal stepped.Current.Value 11 "manual step advances exactly once"
                Expect.equal resumed.Status SessionRuntimeStatus.Running "resume is explicit"
            }

            test "input admission is monotonic and refusals preserve accepted state" {
                let initial = runtime ()

                let accepted, _ =
                    SessionRuntime.update
                        contract
                        (SessionRuntimeObservation.AdmitInput
                            {
                                SessionId = "session-1"
                                InputId = "game.add"
                                Sequence = 4UL
                                Value = 2
                            })
                        initial

                let stale, effects =
                    SessionRuntime.update
                        contract
                        (SessionRuntimeObservation.AdmitInput
                            {
                                SessionId = "session-1"
                                InputId = "game.add"
                                Sequence = 4UL
                                Value = 99
                            })
                        accepted

                Expect.equal stale accepted "a stale sequence cannot mutate product or runtime state"

                Expect.equal
                    effects
                    [
                        SessionRuntimeEffect.Refused(SessionRuntimeRefusal.StaleInputSequence(4UL, 4UL))
                    ]
                    "staleness is precise"
            }

            test "reset and compatible restore clear transient clock state" {
                let initial = runtime ()

                let changed, _ =
                    SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 25_000UL) initial

                let reset, _ =
                    SessionRuntime.update contract SessionRuntimeObservation.Reset changed

                let restored, _ =
                    SessionRuntime.update
                        contract
                        (SessionRuntimeObservation.Restore(contract.Snapshot changed.Current))
                        reset

                Expect.equal reset.Current initial.Current "reset restores the initialization snapshot"
                Expect.equal reset.AccumulatorMicroseconds 0UL "reset clears carried host time"
                Expect.equal restored.Current changed.Current "compatible snapshots restore exactly"
            }

            test "wrong-session and incompatible snapshots are rejected before product restore" {
                let initial = runtime ()

                let wrong =
                    { contract.Snapshot initial.Current with
                        SessionId = "other"
                    }

                let same, wrongEffects =
                    SessionRuntime.update contract (SessionRuntimeObservation.Restore wrong) initial

                let incompatible =
                    { contract.Snapshot initial.Current with
                        Compatibility =
                            { identity with
                                EngineVersion = "2.0.0"
                            }
                    }

                let sameAgain, incompatibleEffects =
                    SessionRuntime.update contract (SessionRuntimeObservation.Restore incompatible) initial

                Expect.equal same initial "wrong-session restore preserves state"

                Expect.equal
                    wrongEffects
                    [
                        SessionRuntimeEffect.Refused(SessionRuntimeRefusal.WrongSession("session-1", "other"))
                    ]
                    "session ownership is checked"

                Expect.equal sameAgain initial "incompatible restore preserves state"

                Expect.equal
                    incompatibleEffects
                    [
                        SessionRuntimeEffect.Refused(
                            SessionRuntimeRefusal.IncompatibleSnapshot
                                [ SessionCompatibilityIssue.EngineVersion("1.2.3", "2.0.0") ]
                        )
                    ]
                    "compatibility axes are returned"
            }

            test "disposal is idempotent and makes later observations inert" {
                let initial = runtime ()

                let disposed, effects =
                    SessionRuntime.update contract SessionRuntimeObservation.Dispose initial

                let again, repeated =
                    SessionRuntime.update contract SessionRuntimeObservation.Dispose disposed

                let inert, refused =
                    SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 10_000UL) disposed

                Expect.equal effects [ SessionRuntimeEffect.Disposed ] "one disposal effect is emitted"
                Expect.equal again disposed "repeated disposal is idempotent"
                Expect.isEmpty repeated "repeated disposal emits nothing"
                Expect.equal inert disposed "disposed runtime cannot advance"

                Expect.equal
                    refused
                    [ SessionRuntimeEffect.Refused SessionRuntimeRefusal.RuntimeDisposed ]
                    "inert refusal is explicit"
            }

            test "support classification includes the portable runtime" {
                Expect.equal
                    SessionSupport.current
                    SessionSupport.PortableRuntime
                    "the package exports the runtime reducer"

                Expect.equal (SessionSupport.id SessionSupport.current) "portable-runtime" "support identity is stable"
            }
        ]
