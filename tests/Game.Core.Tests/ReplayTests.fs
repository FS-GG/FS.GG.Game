namespace Game.Core.Tests

open Expecto
open FS.GG.Game.Core

module ReplayTests =
    let private success =
        function
        | Ok value -> value
        | Error error -> failtestf "expected success: %A" error

    let private identity =
        {
            ContractVersion = 1
            EngineId = "counter"
            EngineVersion = "1"
            ProfileId = "portable/1"
            SchemaId = "counter"
            SchemaVersion = 1
        }

    let private snapshot value =
        {
            SessionId = "replay-1"
            Revision = uint64 value
            Compatibility = identity
            Value = value
        }

    let private contract: SessionContract<unit, int, int, int, int> =
        {
            Initialize = fun _ -> Ok 0
            AdmitInput = fun input state -> Ok(state + input.Value)
            Advance = fun advance state -> Ok(state + int advance.StepCount)
            Project =
                fun state ->
                    {
                        SessionId = "replay-1"
                        Revision = uint64 state
                        Value = state
                    }
            Snapshot = snapshot
            Restore = fun value -> Ok value.Value
        }

    let private recording () =
        ReplayRecorder.create (snapshot 0) "0"
        |> Result.bind (
            ReplayRecorder.appendInput
                {
                    SessionId = "replay-1"
                    InputId = "counter.add"
                    Sequence = 1UL
                    Value = 2
                }
                "2"
        )
        |> Result.bind (ReplayRecorder.appendAdvance 3UL "5")
        |> Result.bind (ReplayRecorder.addCheckpoint (snapshot 5) "5")
        |> Result.bind (
            ReplayRecorder.appendInput
                {
                    SessionId = "replay-1"
                    InputId = "counter.add"
                    Sequence = 2UL
                    Value = 4
                }
                "9"
        )

    [<Tests>]
    let tests =
        testList
            "portable replay"
            [
                testCase "record, seek and checkpoint use the product contract"
                <| fun _ ->
                    let value = recording () |> success

                    match Replay.seek contract string (fun _ -> false) 3UL value with
                    | Ok(ReplayRunOutcome.Completed(next, state)) ->
                        Expect.equal next 3UL "the resume cursor follows the last applied event"
                        Expect.equal state 9 "seek restores the checkpoint and applies only its suffix"
                    | other -> failtestf "unexpected replay result: %A" other

                testCase "cancellation preserves a resumable accepted state"
                <| fun _ ->
                    let value = recording () |> success

                    match Replay.seek contract string ((=) 1UL) 3UL { value with Checkpoints = [] } with
                    | Ok(ReplayRunOutcome.Cancelled(next, state)) ->
                        Expect.equal next 1UL "the unapplied event is the resume cursor"
                        Expect.equal state 2 "only accepted prefix state is returned"
                    | other -> failtestf "unexpected cancellation: %A" other

                testCase "the first divergent event is exact"
                <| fun _ ->
                    let value = recording () |> success

                    let mutated =
                        { value with
                            Checkpoints = []
                            Events =
                                value.Events
                                |> List.map (fun event ->
                                    if event.Index = 1UL then
                                        { event with StateDigest = "6" }
                                    else
                                        event)
                        }

                    match Replay.seek contract string (fun _ -> false) 3UL mutated with
                    | Ok(ReplayRunOutcome.Diverged divergence) ->
                        Expect.equal divergence.EventIndex 1UL "the first mismatching transition is named"
                        Expect.equal divergence.ExpectedDigest "6" "the retained expected digest is returned"
                        Expect.equal divergence.ActualDigest "5" "the actual product-state digest is returned"
                    | other -> failtestf "unexpected divergence result: %A" other

                testCase "malformed order and stale inputs are refused before restore"
                <| fun _ ->
                    let value = recording () |> success

                    let bad =
                        { value with
                            Events =
                                value.Events
                                |> List.mapi (fun index event ->
                                    if index = 0 then
                                        { event with Index = 4UL }
                                    elif index = 2 then
                                        { event with
                                            Kind =
                                                ReplayEventKind.Input
                                                    {
                                                        SessionId = "replay-1"
                                                        InputId = "counter.add"
                                                        Sequence = 1UL
                                                        Value = 1
                                                    }
                                        }
                                    else
                                        event)
                        }

                    let issues = Replay.validate bad
                    Expect.contains issues (ReplayIssue.InvalidEventIndex(0UL, 4UL)) "event positions are contiguous"

                    Expect.contains
                        issues
                        (ReplayIssue.StaleInputSequence(2UL, 1UL, 1UL))
                        "input sequences stay monotonic"

                testCase "canonical export is unambiguous and repeatable"
                <| fun _ ->
                    let value = recording () |> success
                    let first = ReplayExport.canonicalText string string value
                    let second = ReplayExport.canonicalText string string value
                    Expect.equal first second "equal recordings have equal canonical text"
                    Expect.isTrue ((success first).StartsWith("fsgg-replay/1")) "the format identity is explicit"
            ]
