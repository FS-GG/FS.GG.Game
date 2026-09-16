namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
type ReplayEventKind<'input> =
    | Input of SessionInput<'input>
    | Advance of stepCount: uint64

type ReplayEvent<'input> =
    {
        Index: uint64
        Kind: ReplayEventKind<'input>
        StateDigest: string
    }

type ReplayCheckpoint<'snapshot> =
    {
        NextEventIndex: uint64
        Snapshot: SessionSnapshot<'snapshot>
        StateDigest: string
    }

type ReplayRecording<'input, 'snapshot> =
    {
        FormatVersion: int
        SessionId: string
        Compatibility: SessionCompatibility
        InitialSnapshot: SessionSnapshot<'snapshot>
        InitialStateDigest: string
        Events: ReplayEvent<'input> list
        Checkpoints: ReplayCheckpoint<'snapshot> list
    }

[<RequireQualifiedAccess>]
type ReplayIssue =
    | UnsupportedFormatVersion of int
    | InvalidSession of SessionContractIssue list
    | WrongSession of expected: string * actual: string
    | IncompatibleSnapshot of SessionCompatibilityIssue list
    | InvalidEventIndex of expected: uint64 * actual: uint64
    | MissingStateDigest of eventIndex: uint64 option
    | InvalidAdvance of eventIndex: uint64
    | StaleInputSequence of eventIndex: uint64 * accepted: uint64 * candidate: uint64
    | InvalidCheckpointIndex of uint64
    | CheckpointDigestMismatch of checkpointIndex: uint64 * expected: string * actual: string
    | TargetBeyondRecording of target: uint64 * eventCount: uint64

type ReplayDivergence =
    {
        EventIndex: uint64
        ExpectedDigest: string
        ActualDigest: string
    }

[<RequireQualifiedAccess>]
type ReplayRunOutcome<'state> =
    | Completed of nextEventIndex: uint64 * state: 'state
    | Cancelled of nextEventIndex: uint64 * state: 'state
    | Diverged of ReplayDivergence
    | ContractRefused of eventIndex: uint64 * SessionFailure

[<RequireQualifiedAccess>]
module Replay =
    let private present value =
        not (System.String.IsNullOrWhiteSpace value)

    let validate recording =
        let issues = ResizeArray<ReplayIssue>()

        if recording.FormatVersion <> 1 then
            issues.Add(ReplayIssue.UnsupportedFormatVersion recording.FormatVersion)

        let snapshotIssues = SessionEnvelope.validateSnapshot recording.InitialSnapshot

        if not snapshotIssues.IsEmpty then
            issues.Add(ReplayIssue.InvalidSession snapshotIssues)

        if recording.InitialSnapshot.SessionId <> recording.SessionId then
            issues.Add(ReplayIssue.WrongSession(recording.SessionId, recording.InitialSnapshot.SessionId))

        let compatibilityIssues =
            SessionCompatibility.compare recording.Compatibility recording.InitialSnapshot.Compatibility

        if not compatibilityIssues.IsEmpty then
            issues.Add(ReplayIssue.IncompatibleSnapshot compatibilityIssues)

        if not (present recording.InitialStateDigest) then
            issues.Add(ReplayIssue.MissingStateDigest None)

        let mutable expectedIndex = 0UL
        let mutable lastSequence = None

        for event in recording.Events do
            if event.Index <> expectedIndex then
                issues.Add(ReplayIssue.InvalidEventIndex(expectedIndex, event.Index))

            if not (present event.StateDigest) then
                issues.Add(ReplayIssue.MissingStateDigest(Some event.Index))

            match event.Kind with
            | ReplayEventKind.Advance 0UL -> issues.Add(ReplayIssue.InvalidAdvance event.Index)
            | ReplayEventKind.Advance _ -> ()
            | ReplayEventKind.Input input ->
                let inputIssues = SessionEnvelope.validateInput input

                if not inputIssues.IsEmpty then
                    issues.Add(ReplayIssue.InvalidSession inputIssues)

                if input.SessionId <> recording.SessionId then
                    issues.Add(ReplayIssue.WrongSession(recording.SessionId, input.SessionId))

                match lastSequence with
                | Some accepted when input.Sequence <= accepted ->
                    issues.Add(ReplayIssue.StaleInputSequence(event.Index, accepted, input.Sequence))
                | _ -> lastSequence <- Some input.Sequence

            expectedIndex <- expectedIndex + 1UL

        let eventCount = uint64 recording.Events.Length
        let mutable previousCheckpoint = None

        for checkpoint in recording.Checkpoints do
            if checkpoint.NextEventIndex = 0UL || checkpoint.NextEventIndex > eventCount then
                issues.Add(ReplayIssue.InvalidCheckpointIndex checkpoint.NextEventIndex)

            match previousCheckpoint with
            | Some previous when checkpoint.NextEventIndex <= previous ->
                issues.Add(ReplayIssue.InvalidCheckpointIndex checkpoint.NextEventIndex)
            | _ -> previousCheckpoint <- Some checkpoint.NextEventIndex

            let checkpointIssues = SessionEnvelope.validateSnapshot checkpoint.Snapshot

            if not checkpointIssues.IsEmpty then
                issues.Add(ReplayIssue.InvalidSession checkpointIssues)

            if checkpoint.Snapshot.SessionId <> recording.SessionId then
                issues.Add(ReplayIssue.WrongSession(recording.SessionId, checkpoint.Snapshot.SessionId))

            let compatibilityIssues =
                SessionCompatibility.compare recording.Compatibility checkpoint.Snapshot.Compatibility

            if not compatibilityIssues.IsEmpty then
                issues.Add(ReplayIssue.IncompatibleSnapshot compatibilityIssues)

            if not (present checkpoint.StateDigest) then
                issues.Add(ReplayIssue.MissingStateDigest(Some checkpoint.NextEventIndex))

        List.ofSeq issues

    let seek contract stateDigest shouldCancel targetEventCount recording =
        let issues = validate recording
        let eventCount = uint64 recording.Events.Length

        if targetEventCount > eventCount then
            Error(issues @ [ ReplayIssue.TargetBeyondRecording(targetEventCount, eventCount) ])
        elif not issues.IsEmpty then
            Error issues
        else
            let startIndex, snapshot, expectedStartDigest =
                recording.Checkpoints
                |> List.filter (fun checkpoint -> checkpoint.NextEventIndex <= targetEventCount)
                |> List.tryLast
                |> function
                    | Some checkpoint -> checkpoint.NextEventIndex, checkpoint.Snapshot, checkpoint.StateDigest
                    | None -> 0UL, recording.InitialSnapshot, recording.InitialStateDigest

            match contract.Restore snapshot with
            | Error failure -> Ok(ReplayRunOutcome.ContractRefused(startIndex, failure))
            | Ok initial when stateDigest initial <> expectedStartDigest ->
                Ok(
                    ReplayRunOutcome.Diverged
                        {
                            EventIndex = startIndex
                            ExpectedDigest = expectedStartDigest
                            ActualDigest = stateDigest initial
                        }
                )
            | Ok initial ->
                let selected =
                    recording.Events
                    |> List.filter (fun event -> event.Index >= startIndex && event.Index < targetEventCount)

                let rec execute state remaining =
                    match remaining with
                    | [] -> Ok(ReplayRunOutcome.Completed(targetEventCount, state))
                    | event :: _ when shouldCancel event.Index -> Ok(ReplayRunOutcome.Cancelled(event.Index, state))
                    | event :: tail ->
                        let result =
                            match event.Kind with
                            | ReplayEventKind.Input input -> contract.AdmitInput input state
                            | ReplayEventKind.Advance steps ->
                                contract.Advance
                                    {
                                        SessionId = recording.SessionId
                                        StepCount = steps
                                    }
                                    state

                        match result with
                        | Error failure -> Ok(ReplayRunOutcome.ContractRefused(event.Index, failure))
                        | Ok next ->
                            let actual = stateDigest next

                            if actual <> event.StateDigest then
                                Ok(
                                    ReplayRunOutcome.Diverged
                                        {
                                            EventIndex = event.Index
                                            ExpectedDigest = event.StateDigest
                                            ActualDigest = actual
                                        }
                                )
                            else
                                execute next tail

                execute initial selected

[<RequireQualifiedAccess>]
module ReplayRecorder =
    let create (initialSnapshot: SessionSnapshot<'snapshot>) initialStateDigest =
        let recording: ReplayRecording<'input, 'snapshot> =
            {
                FormatVersion = 1
                SessionId = initialSnapshot.SessionId
                Compatibility = initialSnapshot.Compatibility
                InitialSnapshot = initialSnapshot
                InitialStateDigest = initialStateDigest
                Events = []
                Checkpoints = []
            }

        match Replay.validate recording with
        | [] -> Ok recording
        | issues -> Error issues

    let private append kind digest recording =
        let event =
            {
                Index = uint64 recording.Events.Length
                Kind = kind
                StateDigest = digest
            }

        let candidate =
            { recording with
                Events = recording.Events @ [ event ]
            }

        match Replay.validate candidate with
        | [] -> Ok candidate
        | issues -> Error issues

    let appendInput input stateDigest recording =
        append (ReplayEventKind.Input input) stateDigest recording

    let appendAdvance stepCount stateDigest recording =
        append (ReplayEventKind.Advance stepCount) stateDigest recording

    let addCheckpoint snapshot stateDigest recording =
        let checkpoint =
            {
                NextEventIndex = uint64 recording.Events.Length
                Snapshot = snapshot
                StateDigest = stateDigest
            }

        let candidate =
            { recording with
                Checkpoints = recording.Checkpoints @ [ checkpoint ]
            }

        match Replay.validate candidate with
        | [] -> Ok candidate
        | issues -> Error issues

[<RequireQualifiedAccess>]
module ReplayExport =
    let private field (value: string) = string value.Length + ":" + value

    let private compatibility value =
        [
            string value.ContractVersion
            value.EngineId
            value.EngineVersion
            value.ProfileId
            value.SchemaId
            string value.SchemaVersion
        ]
        |> List.map field
        |> String.concat ""

    let canonicalText encodeInput encodeSnapshot recording =
        match Replay.validate recording with
        | _ :: _ as issues -> Error issues
        | [] ->
            let encodeEvent event =
                let kind =
                    match event.Kind with
                    | ReplayEventKind.Input input ->
                        "I"
                        + field input.SessionId
                        + field input.InputId
                        + field (string input.Sequence)
                        + field (encodeInput input.Value)
                    | ReplayEventKind.Advance steps -> "A" + field (string steps)

                field (string event.Index) + field kind + field event.StateDigest

            let encodeCheckpoint checkpoint =
                field (string checkpoint.NextEventIndex)
                + field (string checkpoint.Snapshot.Revision)
                + field checkpoint.StateDigest
                + field (encodeSnapshot checkpoint.Snapshot.Value)

            Ok(
                "fsgg-replay/1"
                + field recording.SessionId
                + field (compatibility recording.Compatibility)
                + field (string recording.InitialSnapshot.Revision)
                + field recording.InitialStateDigest
                + field (encodeSnapshot recording.InitialSnapshot.Value)
                + field (recording.Events |> List.map encodeEvent |> String.concat "")
                + field (recording.Checkpoints |> List.map encodeCheckpoint |> String.concat "")
            )
