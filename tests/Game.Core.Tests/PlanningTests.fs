namespace Game.Core.Tests

open Expecto
open FS.GG.Game.Core

module PlanningTests =
    let private success =
        function
        | Ok value -> value
        | Error error -> failtestf "expected success: %A" error

    let private create value digest : PlanningSession<string, 'state, 'intent> =
        Planning.create
            {
                ContentId = "map"
                Revision = 3UL
                Value = "authored"
            }
            {
                SessionId = "session"
                Revision = 7UL
                StateDigest = digest
                Value = value
            }
        |> success

    [<Tests>]
    let tests =
        testList
            "planning state separation"
            [
                testCase "grid scenario predicts without changing authored or accepted state"
                <| fun _ ->
                    let adapter =
                        {
                            Apply = fun (dc, dr) (col, row) -> Ok(col + dc, row + dr)
                            StateDigest = fun (col, row) -> sprintf "%d,%d" col row
                        }

                    let initial = create (2, 4) "2,4"

                    let planned =
                        initial
                        |> Planning.beginScenario "route-a"
                        |> success
                        |> Planning.apply adapter "route-a" (1, -2)
                        |> success

                    let intent = Planning.proposeCommit "route-a" planned |> success
                    Expect.equal planned.Authored initial.Authored "authored content is unchanged"
                    Expect.equal planned.Accepted initial.Accepted "accepted session state is unchanged"
                    Expect.equal intent.Intents [ (1, -2) ] "only a product-owned commit intent leaves planning"
                    Expect.equal intent.PredictedDigest "3,2" "the grid prediction is explicit"

                testCase "continuous scenario uses the same generic adapter"
                <| fun _ ->
                    let adapter =
                        {
                            Apply = fun delta position -> Ok(position + delta)
                            StateDigest = fun value -> sprintf "%.2f" value
                        }

                    let planned =
                        create 1.5 "1.50"
                        |> Planning.beginScenario "arc"
                        |> success
                        |> Planning.apply adapter "arc" 0.25
                        |> success

                    let comparison = Planning.compare "arc" planned |> success
                    Expect.equal comparison.PredictedDigest "1.75" "continuous coordinates require no tactical types"
                    Expect.isFalse comparison.IsUnchanged "changed predictions compare against their accepted basis"

                testCase "new accepted authority makes every live draft stale"
                <| fun _ ->
                    let initial = create 10 "10" |> Planning.beginScenario "candidate" |> success

                    let refreshed =
                        Planning.replaceAccepted
                            {
                                SessionId = "session"
                                Revision = 8UL
                                StateDigest = "11"
                                Value = 11
                            }
                            initial
                        |> success

                    Expect.equal
                        refreshed.Scenarios.Head.Status
                        PlanningScenarioStatus.Stale
                        "the old basis cannot be committed"

                    Expect.equal
                        (Planning.proposeCommit "candidate" refreshed)
                        (Error(PlanningIssue.ScenarioNotDraft("candidate", PlanningScenarioStatus.Stale)))
                        "stale prediction is refused"

                testCase "cancel retains evidence while refusing later commit"
                <| fun _ ->
                    let cancelled =
                        create 0 "0"
                        |> Planning.beginScenario "discard"
                        |> success
                        |> Planning.cancel "discard"
                        |> success

                    Expect.equal cancelled.Accepted.Value 0 "cancellation cannot alter accepted state"

                    Expect.equal
                        (Planning.proposeCommit "discard" cancelled)
                        (Error(PlanningIssue.ScenarioNotDraft("discard", PlanningScenarioStatus.Cancelled)))
                        "cancelled work cannot become a commit intent"
            ]
