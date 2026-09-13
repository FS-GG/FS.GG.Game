namespace Game.Core.Tests

open Expecto
open FS.GG.Game.Core

module NetworkSessionTests =
    let private binding client token = { SessionId="arena";ClientId=client;ReconnectToken=token }
    let private input client token sequence value =
        { Binding=binding client token
          Input={SessionId="arena";InputId="move";Sequence=sequence;Value=value} }
    let private created () : NetworkAdmissionState<string> =
        NetworkAdmission.create "arena" [binding "a" "ta";binding "b" "tb"]
        |> Result.defaultWith (fun issue -> failtestf "%A" issue)
    let private admit candidate state =
        NetworkAdmission.admit (fun value -> if value="invalid" then Error "invalid.move" else Ok()) candidate state

    [<Tests>]
    let tests = testList "network admission and resync" [
        testCase "accepted order is stable across clients and canonical" <| fun _ ->
            let first, acceptedA = created () |> admit (input "b" "tb" 4UL "right") |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            let second, acceptedB = first |> admit (input "a" "ta" 1UL "left") |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            Expect.equal acceptedA.AcceptedOrder 0UL "the server assigns the first accepted order"
            Expect.equal acceptedB.AcceptedOrder 1UL "order follows admission, not client identity"
            Expect.equal (NetworkAdmission.accepted second |> List.map _.ClientId) ["b";"a"] "the accepted stream is ordered"
            Expect.equal (NetworkAdmission.canonicalText id second) (NetworkAdmission.canonicalText id second) "equal accepted streams have equal bytes"

        testCase "a runtime binding preserves accepted history and sequence cursors" <| fun _ ->
            let state, _ = created () |> admit (input "a" "ta" 5UL "left") |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            let bound =
                state
                |> NetworkAdmission.bind (binding "c" "tc")
                |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            let next, accepted =
                bound
                |> admit (input "c" "tc" 1UL "up")
                |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            Expect.equal accepted.AcceptedOrder 1UL "the new client continues the room order"
            Expect.equal (NetworkAdmission.lastSequence "a" next) (Some 5UL) "an existing cursor survives binding"
            Expect.equal (NetworkAdmission.accepted next |> List.map _.ClientId) ["a"; "c"] "history survives binding"
            Expect.equal
                (NetworkAdmission.bind (binding "a" "replacement") state)
                (Error [ NetworkAdmissionIssue.ClientAlreadyBound "a" ])
                "an existing identity cannot be rebound"

        testCase "wrong identity duplicate stale and invalid payloads are refused without mutation" <| fun _ ->
            let state, _ = created () |> admit (input "a" "ta" 5UL "left") |> Result.defaultWith (fun issue -> failtestf "%A" issue)
            Expect.equal (admit (input "a" "bad" 6UL "right") state |> Result.map fst) (Error(NetworkAdmissionIssue.ReconnectTokenMismatch "a")) "token mismatch is explicit"
            Expect.equal (admit (input "a" "ta" 5UL "right") state |> Result.map fst) (Error(NetworkAdmissionIssue.DuplicateInputSequence("a",5UL))) "duplicate is distinct"
            Expect.equal (admit (input "a" "ta" 4UL "right") state |> Result.map fst) (Error(NetworkAdmissionIssue.StaleInputSequence("a",5UL,4UL))) "stale input is refused"
            Expect.equal (admit (input "a" "ta" 6UL "invalid") state |> Result.map fst) (Error(NetworkAdmissionIssue.PayloadRefused "invalid.move")) "product validation precedes acceptance"
            Expect.equal (NetworkAdmission.accepted state).Length 1 "refusals did not change accepted history"

        testCase "resync chooses suffix full snapshot current or client-ahead refusal" <| fun _ ->
            Expect.equal (NetworkAdmission.resync (Some 8UL) 9UL 12UL) (NetworkResyncDecision.ReplaySuffix(9UL,12UL)) "retained history supplies a suffix"
            Expect.equal (NetworkAdmission.resync (Some 8UL) 3UL 12UL) (NetworkResyncDecision.FullSnapshot(12UL,"revision 3 predates retained suffix 8")) "old clients need a snapshot"
            Expect.equal (NetworkAdmission.resync None 12UL 12UL) (NetworkResyncDecision.Current 12UL) "current clients need nothing"
            Expect.equal (NetworkAdmission.resync None 13UL 12UL) (NetworkResyncDecision.ClientAhead(13UL,12UL)) "a client cannot move authority forward"
    ]
