module Game.Core.Tests.SessionContractTests

open Expecto
open FS.GG.Game.Core

let private identity =
    { ContractVersion = 1
      EngineId = "example.engine"
      EngineVersion = "1.2.3"
      ProfileId = "example.profile/1"
      SchemaId = "example.state"
      SchemaVersion = 2 }

type private State =
    { SessionId: string
      Compatibility: SessionCompatibility
      Value: int
      Revision: uint64 }

let private failure code message = Error { Code = code; Message = message }

let private getOk result =
    match result with
    | Ok value -> value
    | Error error -> failtestf "expected Ok, got %s: %s" error.Code error.Message

let private contract: SessionContract<int, State, int, int, int> =
    { Initialize = fun request ->
        match SessionEnvelope.validateInitialization request with
        | [] -> Ok { SessionId = request.SessionId; Compatibility = request.Compatibility; Value = request.Configuration; Revision = 0UL }
        | _ -> failure "invalid-initialization" "invalid session initialization"
      AdmitInput = fun input state ->
        if input.SessionId <> state.SessionId then failure "wrong-session" "input belongs to another session"
        else Ok { state with Value = state.Value + input.Value; Revision = state.Revision + 1UL }
      Advance = fun observation state ->
        if observation.SessionId <> state.SessionId then failure "wrong-session" "advance belongs to another session"
        else Ok { state with Value = state.Value + int observation.StepCount; Revision = state.Revision + 1UL }
      Project = fun state -> { SessionId = state.SessionId; Revision = state.Revision; Value = state.Value }
      Snapshot = fun state -> { SessionId = state.SessionId; Revision = state.Revision; Compatibility = state.Compatibility; Value = state.Value }
      Restore = fun snapshot ->
        if not (SessionCompatibility.isCompatible identity snapshot.Compatibility) then
            failure "incompatible-snapshot" "snapshot identity differs"
        else
            Ok { SessionId = snapshot.SessionId; Compatibility = snapshot.Compatibility; Value = snapshot.Value; Revision = snapshot.Revision } }

[<Tests>]
let tests =
    testList "Game.Core portable session contract" [
        test "a consumer can implement and traverse every contract seam" {
            let initialized =
                contract.Initialize { SessionId = "session-1"; Compatibility = identity; Configuration = 10 }
                |> getOk
            let admitted =
                contract.AdmitInput { SessionId = "session-1"; InputId = "game.add"; Sequence = 1UL; Value = 4 } initialized
                |> getOk
            let advanced = contract.Advance { SessionId = "session-1"; StepCount = 2UL } admitted |> getOk
            let projection = contract.Project advanced
            let snapshot = contract.Snapshot advanced
            let restored = contract.Restore snapshot |> getOk

            Expect.equal projection.Value 16 "projection reads the accepted state"
            Expect.equal projection.Revision 2UL "input and advancement each create one revision"
            Expect.equal restored advanced "snapshot restoration preserves the consumer state"
        }

        test "compatibility compares every identity axis in stable order" {
            let actual =
                { ContractVersion = 2
                  EngineId = "other.engine"
                  EngineVersion = "2.0.0"
                  ProfileId = "other.profile/1"
                  SchemaId = "other.state"
                  SchemaVersion = 3 }

            Expect.equal
                (SessionCompatibility.compare identity actual)
                [ SessionCompatibilityIssue.ContractVersion(1, 2)
                  SessionCompatibilityIssue.EngineId("example.engine", "other.engine")
                  SessionCompatibilityIssue.EngineVersion("1.2.3", "2.0.0")
                  SessionCompatibilityIssue.ProfileId("example.profile/1", "other.profile/1")
                  SessionCompatibilityIssue.SchemaId("example.state", "other.state")
                  SessionCompatibilityIssue.SchemaVersion(2, 3) ]
                "no compatibility axis is silently ignored"
        }

        test "malformed identities and envelope identifiers are rejected" {
            let malformed =
                { ContractVersion = 0
                  EngineId = " "
                  EngineVersion = ""
                  ProfileId = ""
                  SchemaId = ""
                  SchemaVersion = -1 }
            let initialization = { SessionId = ""; Compatibility = malformed; Configuration = () }
            let input = { SessionId = " "; InputId = ""; Sequence = 0UL; Value = () }

            Expect.equal
                (SessionEnvelope.validateInitialization initialization)
                [ SessionContractIssue.MissingSessionId
                  SessionContractIssue.InvalidContractVersion 0
                  SessionContractIssue.MissingEngineId
                  SessionContractIssue.MissingEngineVersion
                  SessionContractIssue.MissingProfileId
                  SessionContractIssue.MissingSchemaId
                  SessionContractIssue.InvalidSchemaVersion -1 ]
                "all invalid initialization fields are reported"
            Expect.equal
                (SessionEnvelope.validateInput input)
                [ SessionContractIssue.MissingSessionId; SessionContractIssue.MissingInputId ]
                "semantic input requires both identities"
        }

        test "support classification states that no runtime is included" {
            Expect.equal SessionSupport.current SessionSupport.ContractEnvelopeOnly "the package exports contracts only"
            Expect.equal (SessionSupport.id SessionSupport.current) "contract-envelope-only" "support identity is stable"
        }
    ]
