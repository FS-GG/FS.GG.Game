namespace FS.GG.Game.Core

type AuthoredState<'content> =
    {
        ContentId: string
        Revision: uint64
        Value: 'content
    }

type AcceptedState<'state> =
    {
        SessionId: string
        Revision: uint64
        StateDigest: string
        Value: 'state
    }

type PredictedState<'state> =
    {
        ScenarioId: string
        BasisSessionId: string
        BasisRevision: uint64
        BasisDigest: string
        StepCount: uint64
        StateDigest: string
        Value: 'state
    }

[<RequireQualifiedAccess>]
type PlanningScenarioStatus =
    | Draft
    | Cancelled
    | Stale

type PlanningScenario<'state, 'intent> =
    {
        Prediction: PredictedState<'state>
        Intents: 'intent list
        Status: PlanningScenarioStatus
    }

type PlanningSession<'content, 'state, 'intent> =
    {
        Authored: AuthoredState<'content>
        Accepted: AcceptedState<'state>
        Scenarios: PlanningScenario<'state, 'intent> list
    }

type ScenarioAdapter<'state, 'intent> =
    {
        Apply: 'intent -> 'state -> Result<'state, SessionFailure>
        StateDigest: 'state -> string
    }

[<RequireQualifiedAccess>]
type PlanningIssue =
    | MissingContentId
    | MissingSessionId
    | MissingScenarioId
    | MissingStateDigest
    | DuplicateScenarioId of string
    | ScenarioNotFound of string
    | ScenarioNotDraft of string * PlanningScenarioStatus
    | AcceptedBasisChanged of scenarioId: string
    | ContractFailure of scenarioId: string * SessionFailure

type PlanningComparison =
    {
        ScenarioId: string
        BasisRevision: uint64
        BasisDigest: string
        PredictedDigest: string
        PredictedSteps: uint64
        IsUnchanged: bool
    }

type PlanningCommitIntent<'intent> =
    {
        ScenarioId: string
        BasisSessionId: string
        BasisRevision: uint64
        BasisDigest: string
        Intents: 'intent list
        PredictedDigest: string
    }

[<RequireQualifiedAccess>]
module Planning =
    let private missing value = System.String.IsNullOrWhiteSpace value

    let private validate authored accepted =
        [
            if missing authored.ContentId then
                PlanningIssue.MissingContentId
            if missing accepted.SessionId then
                PlanningIssue.MissingSessionId
            if missing accepted.StateDigest then
                PlanningIssue.MissingStateDigest
        ]

    let create authored accepted =
        match validate authored accepted with
        | [] ->
            Ok
                {
                    Authored = authored
                    Accepted = accepted
                    Scenarios = []
                }
        | issues -> Error issues

    let private find scenarioId session =
        session.Scenarios
        |> List.tryFind (fun scenario -> scenario.Prediction.ScenarioId = scenarioId)
        |> function
            | Some value -> Ok value
            | None -> Error(PlanningIssue.ScenarioNotFound scenarioId)

    let private replace scenario session =
        { session with
            Scenarios =
                session.Scenarios
                |> List.map (fun value ->
                    if value.Prediction.ScenarioId = scenario.Prediction.ScenarioId then
                        scenario
                    else
                        value)
        }

    let private requireDraft scenario =
        if scenario.Status = PlanningScenarioStatus.Draft then
            Ok scenario
        else
            Error(PlanningIssue.ScenarioNotDraft(scenario.Prediction.ScenarioId, scenario.Status))

    let beginScenario scenarioId session =
        if missing scenarioId then
            Error PlanningIssue.MissingScenarioId
        elif
            session.Scenarios
            |> List.exists (fun scenario -> scenario.Prediction.ScenarioId = scenarioId)
        then
            Error(PlanningIssue.DuplicateScenarioId scenarioId)
        else
            let accepted = session.Accepted

            let prediction =
                {
                    ScenarioId = scenarioId
                    BasisSessionId = accepted.SessionId
                    BasisRevision = accepted.Revision
                    BasisDigest = accepted.StateDigest
                    StepCount = 0UL
                    StateDigest = accepted.StateDigest
                    Value = accepted.Value
                }

            Ok
                { session with
                    Scenarios =
                        session.Scenarios
                        @ [
                            {
                                Prediction = prediction
                                Intents = []
                                Status = PlanningScenarioStatus.Draft
                            }
                        ]
                }

    let apply adapter scenarioId intent session =
        find scenarioId session
        |> Result.bind requireDraft
        |> Result.bind (fun scenario ->
            match adapter.Apply intent scenario.Prediction.Value with
            | Error failure -> Error(PlanningIssue.ContractFailure(scenarioId, failure))
            | Ok state ->
                let digest = adapter.StateDigest state

                if missing digest then
                    Error PlanningIssue.MissingStateDigest
                else
                    let prediction =
                        { scenario.Prediction with
                            StepCount = scenario.Prediction.StepCount + 1UL
                            StateDigest = digest
                            Value = state
                        }

                    Ok(
                        replace
                            { scenario with
                                Prediction = prediction
                                Intents = scenario.Intents @ [ intent ]
                            }
                            session
                    ))

    let compare scenarioId session =
        find scenarioId session
        |> Result.map (fun scenario ->
            let prediction = scenario.Prediction

            {
                ScenarioId = scenarioId
                BasisRevision = prediction.BasisRevision
                BasisDigest = prediction.BasisDigest
                PredictedDigest = prediction.StateDigest
                PredictedSteps = prediction.StepCount
                IsUnchanged = prediction.BasisDigest = prediction.StateDigest
            })

    let cancel scenarioId session =
        find scenarioId session
        |> Result.bind requireDraft
        |> Result.map (fun scenario ->
            replace
                { scenario with
                    Status = PlanningScenarioStatus.Cancelled
                }
                session)

    let proposeCommit scenarioId session =
        find scenarioId session
        |> Result.bind requireDraft
        |> Result.bind (fun scenario ->
            let prediction = scenario.Prediction

            if
                prediction.BasisSessionId <> session.Accepted.SessionId
                || prediction.BasisRevision <> session.Accepted.Revision
                || prediction.BasisDigest <> session.Accepted.StateDigest
            then
                Error(PlanningIssue.AcceptedBasisChanged scenarioId)
            else
                Ok
                    {
                        ScenarioId = scenarioId
                        BasisSessionId = prediction.BasisSessionId
                        BasisRevision = prediction.BasisRevision
                        BasisDigest = prediction.BasisDigest
                        Intents = scenario.Intents
                        PredictedDigest = prediction.StateDigest
                    })

    let replaceAccepted accepted session =
        match validate session.Authored accepted with
        | _ :: _ as issues -> Error issues
        | [] ->
            let stale scenario =
                if scenario.Status = PlanningScenarioStatus.Draft then
                    { scenario with
                        Status = PlanningScenarioStatus.Stale
                    }
                else
                    scenario

            Ok
                { session with
                    Accepted = accepted
                    Scenarios = session.Scenarios |> List.map stale
                }
