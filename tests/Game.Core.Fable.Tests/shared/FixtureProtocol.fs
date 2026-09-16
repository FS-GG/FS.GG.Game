namespace FS.GG.Game.Core.LockstepFixtures

open FS.GG.Game.Core

[<RequireQualifiedAccess>]
module FixtureProtocol =

    let private appendU16 (bytes: ResizeArray<byte>) (value: int) =
        bytes.Add(byte (value &&& 0xff))
        bytes.Add(byte ((value >>> 8) &&& 0xff))

    let private appendU32 (bytes: ResizeArray<byte>) (value: uint32) =
        bytes.Add(byte (value &&& 0xffu))
        bytes.Add(byte ((value >>> 8) &&& 0xffu))
        bytes.Add(byte ((value >>> 16) &&& 0xffu))
        bytes.Add(byte ((value >>> 24) &&& 0xffu))

    let private appendI32 (bytes: ResizeArray<byte>) (value: int) = appendU32 bytes (uint32 value)

    let private appendCell (bytes: ResizeArray<byte>) (cell: Cell) =
        appendI32 bytes cell.Col
        appendI32 bytes cell.Row

    let private appendCells (bytes: ResizeArray<byte>) (cells: Cell list) =
        appendU32 bytes (uint32 cells.Length)
        cells |> List.iter (appendCell bytes)

    let private appendOptionalEdge (bytes: ResizeArray<byte>) edge =
        match edge with
        | None -> bytes.Add 0uy
        | Some edge ->
            bytes.Add 1uy
            appendCell bytes edge.Lo
            appendCell bytes edge.Hi

    let private appendOptionalCells (bytes: ResizeArray<byte>) cells =
        match cells with
        | None -> bytes.Add 0uy
        | Some cells ->
            bytes.Add 1uy
            appendCells bytes cells

    let private compatibilityIssueTag issue =
        match issue with
        | SessionCompatibilityIssue.ContractVersion _ -> 1uy
        | SessionCompatibilityIssue.EngineId _ -> 2uy
        | SessionCompatibilityIssue.EngineVersion _ -> 3uy
        | SessionCompatibilityIssue.ProfileId _ -> 4uy
        | SessionCompatibilityIssue.SchemaId _ -> 5uy
        | SessionCompatibilityIssue.SchemaVersion _ -> 6uy

    let private record caseId operation appendPayload =
        let body = ResizeArray<byte>()
        appendU32 body (uint32 caseId)
        appendU16 body operation
        appendU16 body 0
        appendPayload body

        let encoded = ResizeArray<byte>()
        appendU32 encoded (uint32 body.Count)
        encoded.AddRange body
        encoded.ToArray()

    let private run fixture =
        match fixture with
        | CellOrder(caseId, cells) -> record caseId 1 (fun bytes -> cells |> List.sort |> appendCells bytes)
        | EdgeBetween(caseId, a, b) -> record caseId 2 (fun bytes -> Edges.edgeBetween a b |> appendOptionalEdge bytes)
        | LineOfSight(caseId, mode, a, b, blocked) ->
            let blocked = Set.ofList blocked
            let transparent cell = not (Set.contains cell blocked)
            let visible = Los.lineOfSightBy mode transparent a b
            record caseId 3 (fun bytes -> bytes.Add(if visible then 1uy else 0uy))
        | Astar(caseId, neighbourhood, maxVisited, start, goal, (minCol, maxCol, minRow, maxRow), blocked) ->
            let blocked = Set.ofList blocked

            let walkable cell =
                cell.Col >= minCol
                && cell.Col <= maxCol
                && cell.Row >= minRow
                && cell.Row <= maxRow
                && not (Set.contains cell blocked)

            let path = Pathfinding.astar neighbourhood maxVisited walkable start goal
            record caseId 4 (fun bytes -> appendOptionalCells bytes path)
        | SessionCompatibilityCase(caseId, expected, actual, sessionId, inputId) ->
            let initialization =
                {
                    SessionId = sessionId
                    Compatibility = actual
                    Configuration = ()
                }

            let input =
                {
                    SessionId = sessionId
                    InputId = inputId
                    Sequence = 0UL
                    Value = ()
                }

            let issues = SessionCompatibility.compare expected actual

            record caseId 5 (fun bytes ->
                bytes.Add(
                    if SessionCompatibility.isCompatible expected actual then
                        1uy
                    else
                        0uy
                )

                appendU16 bytes issues.Length
                issues |> List.iter (compatibilityIssueTag >> bytes.Add)
                appendU16 bytes (SessionEnvelope.validateInitialization initialization).Length
                appendU16 bytes (SessionEnvelope.validateInput input).Length

                bytes.Add(
                    if SessionSupport.id SessionSupport.current = "portable-runtime" then
                        1uy
                    else
                        0uy
                ))

    let private runtimeRecord () =
        let compatibility =
            {
                ContractVersion = 1
                EngineId = "fixture.runtime"
                EngineVersion = "1"
                ProfileId = "fixture"
                SchemaId = "fixture.state"
                SchemaVersion = 1
            }

        let contract: SessionContract<int, int, int, int, int> =
            {
                Initialize = fun value -> Ok value.Configuration
                AdmitInput = fun input state -> Ok(state + input.Value)
                Advance = fun value state -> Ok(state + int value.StepCount)
                Project =
                    fun state ->
                        {
                            SessionId = "fixture"
                            Revision = uint64 state
                            Value = state
                        }
                Snapshot =
                    fun state ->
                        {
                            SessionId = "fixture"
                            Revision = uint64 state
                            Compatibility = compatibility
                            Value = state
                        }
                Restore = fun value -> Ok value.Value
            }

        let initial =
            SessionRuntime.initialize
                {
                    StepMicroseconds = 10_000UL
                    MaxCatchUpSteps = 3u
                }
                contract
                {
                    SessionId = "fixture"
                    Compatibility = compatibility
                    Configuration = 10
                }
            |> Result.defaultWith (fun error -> failwithf "runtime fixture initialization failed: %A" error)

        let admitted, _ =
            SessionRuntime.update
                contract
                (SessionRuntimeObservation.AdmitInput
                    {
                        SessionId = "fixture"
                        InputId = "add"
                        Sequence = 4UL
                        Value = 2
                    })
                initial

        let stale, staleEffects =
            SessionRuntime.update
                contract
                (SessionRuntimeObservation.AdmitInput
                    {
                        SessionId = "fixture"
                        InputId = "add"
                        Sequence = 4UL
                        Value = 99
                    })
                admitted

        let advanced, _ =
            SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 25_000UL) stale

        let clamped, clampEffects =
            SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 45_000UL) advanced

        let paused, _ =
            SessionRuntime.update contract SessionRuntimeObservation.Pause clamped

        let stepped, _ =
            SessionRuntime.update contract SessionRuntimeObservation.StepOnce paused

        let disposed, _ =
            SessionRuntime.update contract SessionRuntimeObservation.Dispose stepped

        let inert, inertEffects =
            SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 10_000UL) disposed

        record -2 6 (fun bytes ->
            appendI32 bytes inert.Current
            appendU32 bytes (uint32 clamped.AccumulatorMicroseconds)

            bytes.Add(
                if stale = admitted && staleEffects.Length = 1 then
                    1uy
                else
                    0uy
            )

            bytes.Add(if clampEffects.Length = 3 then 1uy else 0uy)

            bytes.Add(
                if inert = disposed && inertEffects.Length = 1 then
                    1uy
                else
                    0uy
            )

            bytes.Add(
                if disposed.Status = SessionRuntimeStatus.Disposed then
                    1uy
                else
                    0uy
            ))

    let private operationRecord () =
        let initial: SessionOperationState<int> =
            SessionOperations.initialize
                {
                    Target = SessionOperationTarget.LocalWorker
                    MaxPendingRequired = 2u
                }
            |> Result.defaultWith (fun error -> failwithf "operation fixture initialization failed: %A" error)

        let firstState, firstEffects =
            SessionOperations.update (SessionOperationObservation.EnqueueRequired 10) initial

        let secondState, secondEffects =
            SessionOperations.update (SessionOperationObservation.EnqueueRequired 20) firstState

        let dispatch effect =
            match effect with
            | [ SessionOperationEffect.DispatchRequired value ] -> value
            | value -> failwithf "operation fixture expected required dispatch, got %A" value

        let first, second = dispatch firstEffects, dispatch secondEffects

        let buffered, early =
            SessionOperations.update (SessionOperationObservation.CompleteRequired(second.Id, 200)) secondState

        let committed, commits =
            SessionOperations.update (SessionOperationObservation.CompleteRequired(first.Id, 100)) buffered

        let projected, projectionEffects =
            SessionOperations.update SessionOperationObservation.DemandProjection committed

        let projectionId =
            match projectionEffects with
            | [ SessionOperationEffect.DispatchProjection value ] -> value.Id
            | value -> failwithf "operation fixture expected projection dispatch, got %A" value

        let queued, coalesced =
            SessionOperations.update SessionOperationObservation.DemandProjection projected

        let refreshed, refreshEffects =
            SessionOperations.update (SessionOperationObservation.CompleteProjection(projectionId, 3UL, 300)) queued

        let replacement, replacementEffects =
            SessionOperations.update SessionOperationObservation.Replace refreshed

        let stale, staleEffects =
            SessionOperations.update
                (SessionOperationObservation.CompleteProjection(projectionId, 4UL, 400))
                replacement

        let disposed, _ = SessionOperations.update SessionOperationObservation.Dispose stale

        record -3 7 (fun bytes ->
            appendU32 bytes (uint32 disposed.Generation)
            appendU16 bytes commits.Length
            appendU16 bytes refreshEffects.Length

            bytes.Add(
                if early.IsEmpty && buffered.BufferedRequired.Count = 1 then
                    1uy
                else
                    0uy
            )

            bytes.Add(
                if coalesced.Length = 1 && queued.ProjectionDemandQueued then
                    1uy
                else
                    0uy
            )

            bytes.Add(
                if replacementEffects.Length = 1 && stale = replacement && staleEffects.Length = 1 then
                    1uy
                else
                    0uy
            )

            bytes.Add(
                if disposed.Status = SessionOperationStatus.Disposed then
                    1uy
                else
                    0uy
            ))

    let private kinematicsRecord () =
        let moving =
            {
                Bounds =
                    {
                        X = 0.0
                        Y = 0.0
                        Width = 2.0
                        Height = 2.0
                    }
                Displacement = { X = 20.0; Y = 0.0 }
            }

        let colliders =
            [
                {
                    Id = "goal"
                    Shape =
                        KinematicShape.AxisAlignedBox
                            {
                                X = 4.0
                                Y = -2.0
                                Width = 1.0
                                Height = 6.0
                            }
                    Response = KinematicResponse.Trigger
                }
                {
                    Id = "wall"
                    Shape =
                        KinematicShape.AxisAlignedBox
                            {
                                X = 10.0
                                Y = -2.0
                                Width = 0.25
                                Height = 6.0
                            }
                    Response = KinematicResponse.Slide
                }
            ]

        let result = Kinematics.advance 4.0 moving colliders
        let scaled value = int (value * 1000.0)

        record -4 8 (fun bytes ->
            appendI32 bytes (scaled result.Bounds.X)
            appendI32 bytes (scaled result.Bounds.Y)
            appendI32 bytes (scaled result.Displacement.X)
            appendI32 bytes (scaled result.Displacement.Y)
            appendU16 bytes result.Hits.Length
            appendU16 bytes result.CandidateIds.Length
            bytes.Add(if result.Bounds.X < 10.0 then 1uy else 0uy)
            bytes.Add(if result.Hits |> List.exists _.IsTrigger then 1uy else 0uy)

            bytes.Add(
                if result.CandidateIds = [ "goal"; "wall" ] then
                    1uy
                else
                    0uy
            ))

    let private replayRecord () =
        let compatibility =
            {
                ContractVersion = 1
                EngineId = "fixture.replay"
                EngineVersion = "1"
                ProfileId = "fixture"
                SchemaId = "fixture.state"
                SchemaVersion = 1
            }

        let snapshot value =
            {
                SessionId = "fixture"
                Revision = uint64 value
                Compatibility = compatibility
                Value = value
            }

        let contract: SessionContract<unit, int, int, int, int> =
            {
                Initialize = fun _ -> Ok 0
                AdmitInput = fun input state -> Ok(state + input.Value)
                Advance = fun value state -> Ok(state + int value.StepCount)
                Project =
                    fun state ->
                        {
                            SessionId = "fixture"
                            Revision = uint64 state
                            Value = state
                        }
                Snapshot = snapshot
                Restore = fun value -> Ok value.Value
            }

        let recording =
            ReplayRecorder.create (snapshot 0) "0"
            |> Result.bind (
                ReplayRecorder.appendInput
                    {
                        SessionId = "fixture"
                        InputId = "add"
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
                        SessionId = "fixture"
                        InputId = "add"
                        Sequence = 2UL
                        Value = 4
                    }
                    "9"
            )
            |> Result.defaultWith (fun error -> failwithf "replay fixture recording failed: %A" error)

        let completed = Replay.seek contract string (fun _ -> false) 3UL recording

        let cancelled =
            Replay.seek contract string ((=) 1UL) 3UL { recording with Checkpoints = [] }

        let mutated =
            { recording with
                Checkpoints = []
                Events =
                    recording.Events
                    |> List.map (fun event ->
                        if event.Index = 1UL then
                            { event with StateDigest = "6" }
                        else
                            event)
            }

        let diverged = Replay.seek contract string (fun _ -> false) 3UL mutated

        let canonical =
            ReplayExport.canonicalText string string recording |> Result.defaultValue ""

        record -5 9 (fun bytes ->
            match completed with
            | Ok(ReplayRunOutcome.Completed(next, state)) ->
                appendU32 bytes (uint32 next)
                appendI32 bytes state
            | _ ->
                appendU32 bytes 0u
                appendI32 bytes -1

            match cancelled with
            | Ok(ReplayRunOutcome.Cancelled(next, state)) ->
                appendU32 bytes (uint32 next)
                appendI32 bytes state
            | _ ->
                appendU32 bytes 0u
                appendI32 bytes -1

            match diverged with
            | Ok(ReplayRunOutcome.Diverged value) ->
                appendU32 bytes (uint32 value.EventIndex)
                appendI32 bytes (int value.ExpectedDigest)
                appendI32 bytes (int value.ActualDigest)
            | _ ->
                appendU32 bytes 0u
                appendI32 bytes -1
                appendI32 bytes -1

            appendU32 bytes (uint32 canonical.Length))

    let private planningRecord () =
        let adapter: ScenarioAdapter<int, int> =
            {
                Apply = fun intent state -> Ok(state + intent)
                StateDigest = string
            }

        let initial: PlanningSession<string, int, int> =
            Planning.create
                {
                    ContentId = "map"
                    Revision = 3UL
                    Value = "authored"
                }
                {
                    SessionId = "fixture"
                    Revision = 7UL
                    StateDigest = "10"
                    Value = 10
                }
            |> Result.defaultWith (fun error -> failwithf "planning fixture initialization failed: %A" error)

        let planned =
            initial
            |> Planning.beginScenario "route"
            |> Result.bind (Planning.apply adapter "route" 4)
            |> Result.defaultWith (fun error -> failwithf "planning fixture failed: %A" error)

        let commit = Planning.proposeCommit "route" planned

        let refreshed =
            Planning.replaceAccepted
                {
                    SessionId = "fixture"
                    Revision = 8UL
                    StateDigest = "11"
                    Value = 11
                }
                planned
            |> Result.defaultWith (fun error -> failwithf "planning refresh failed: %A" error)

        record -6 10 (fun bytes ->
            appendI32 bytes planned.Accepted.Value
            appendI32 bytes planned.Scenarios.Head.Prediction.Value

            match commit with
            | Ok value ->
                appendU32 bytes (uint32 value.BasisRevision)
                appendU16 bytes value.Intents.Length
            | Error _ ->
                appendU32 bytes 0u
                appendU16 bytes 0

            bytes.Add(
                if refreshed.Scenarios.Head.Status = PlanningScenarioStatus.Stale then
                    1uy
                else
                    0uy
            )

            bytes.Add(if refreshed.Authored = initial.Authored then 1uy else 0uy))

    let private rulesRecord () =
        let evidence =
            {
                ModelId = "energy-rules/1"
                ModelSha256 = "sha"
                Tool = "quint"
                ToolVersion = "0.32.0"
                Invariants = [ "energyRulesSafe" ]
                ImplementationBinding = "fixture/v1"
            }

        let available: RuleDefinition<int, string> =
            {
                Metadata =
                    {
                        Id = "energy.available"
                        Version = 1
                        Title = "Energy"
                        Summary = "Energy is required"
                        DependsOn = []
                    }
                Evaluate =
                    fun energy ->
                        {
                            RuleId = "energy.available"
                            Applies = energy > 0
                            Explanation = string energy
                            Causes =
                                [
                                    {
                                        Code = "energy"
                                        Message = string energy
                                    }
                                ]
                            Effects = []
                        }
            }

        let enter: RuleDefinition<int, string> =
            {
                Metadata =
                    {
                        Id = "door.enter"
                        Version = 1
                        Title = "Enter"
                        Summary = "Enter a door"
                        DependsOn = [ "energy.available" ]
                    }
                Evaluate =
                    fun _ ->
                        {
                            RuleId = "door.enter"
                            Applies = true
                            Explanation = "closed"
                            Causes = [ { Code = "door"; Message = "closed" } ]
                            Effects = [ "enter" ]
                        }
            }

        let catalog =
            RuleCatalog.create evidence [ available; enter ]
            |> Result.defaultWith (fun error -> failwithf "rule fixture failed: %A" error)

        let accepted = RuleCatalog.inspect "door.enter" 2 catalog
        let refused = RuleCatalog.inspect "door.enter" 0 catalog

        record -7 11 (fun bytes ->
            match accepted with
            | Ok value ->
                appendU16 bytes value.Evaluations.Length
                bytes.Add(if value.Applies then 1uy else 0uy)
                appendU16 bytes value.Evaluations.Head.Causes.Length
            | Error _ ->
                appendU16 bytes 0
                bytes.Add 0uy
                appendU16 bytes 0

            match refused with
            | Ok value ->
                bytes.Add(if value.Applies then 1uy else 0uy)
                appendU16 bytes value.Evaluations.Length
            | Error _ ->
                bytes.Add 1uy
                appendU16 bytes 0

            appendU16 bytes (RuleCatalog.metadata catalog).Length
            appendU16 bytes (RuleCatalog.evidence catalog).Invariants.Length)

    let private networkRecord () =
        let binding client token =
            {
                SessionId = "fixture"
                ClientId = client
                ReconnectToken = token
            }

        let state: NetworkAdmissionState<int> =
            NetworkAdmission.create "fixture" [ binding "a" "ta"; binding "b" "tb" ]
            |> Result.defaultWith (fun error -> failwithf "network fixture initialization failed: %A" error)

        let candidate client token sequence value =
            {
                Binding = binding client token
                Input =
                    {
                        SessionId = "fixture"
                        InputId = "move"
                        Sequence = sequence
                        Value = value
                    }
            }

        let first, _ =
            NetworkAdmission.admit (fun _ -> Ok()) (candidate "b" "tb" 3UL 7) state
            |> Result.defaultWith (fun error -> failwithf "network fixture admission failed: %A" error)

        let accepted, second =
            NetworkAdmission.admit (fun _ -> Ok()) (candidate "a" "ta" 1UL -2) first
            |> Result.defaultWith (fun error -> failwithf "network fixture admission failed: %A" error)

        let stale =
            NetworkAdmission.admit (fun _ -> Ok()) (candidate "b" "tb" 2UL 99) accepted

        let canonical = NetworkAdmission.canonicalText string accepted

        record -8 12 (fun bytes ->
            appendU32 bytes (uint32 second.AcceptedOrder)
            appendU16 bytes (NetworkAdmission.accepted accepted).Length
            appendU32 bytes (uint32 canonical.Length)

            bytes.Add(
                match stale with
                | Error(NetworkAdmissionIssue.StaleInputSequence _) -> 1uy
                | _ -> 0uy
            )

            bytes.Add(
                match NetworkAdmission.resync (Some 4UL) 6UL 9UL with
                | NetworkResyncDecision.ReplaySuffix _ -> 1uy
                | _ -> 0uy
            )

            bytes.Add(
                match NetworkAdmission.resync (Some 4UL) 2UL 9UL with
                | NetworkResyncDecision.FullSnapshot _ -> 1uy
                | _ -> 0uy
            ))

    let encodeAll () : byte array =
        (GeneratedCases.all |> List.collect (run >> Array.toList))
        @ (runtimeRecord () |> Array.toList)
        @ (operationRecord () |> Array.toList)
        @ (kinematicsRecord () |> Array.toList)
        @ (replayRecord () |> Array.toList)
        @ (planningRecord () |> Array.toList)
        @ (rulesRecord () |> Array.toList)
        @ (networkRecord () |> Array.toList)
        |> List.toArray

    let toLowerHex (bytes: byte array) =
        let digits = "0123456789abcdef"

        bytes
        |> Array.collect (fun value -> [| string digits[int value >>> 4]; string digits[int value &&& 0x0f] |])
        |> String.concat ""
