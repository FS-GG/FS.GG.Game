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

    let private appendI32 (bytes: ResizeArray<byte>) (value: int) =
        appendU32 bytes (uint32 value)

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
        | CellOrder (caseId, cells) ->
            record caseId 1 (fun bytes -> cells |> List.sort |> appendCells bytes)
        | EdgeBetween (caseId, a, b) ->
            record caseId 2 (fun bytes -> Edges.edgeBetween a b |> appendOptionalEdge bytes)
        | LineOfSight (caseId, mode, a, b, blocked) ->
            let blocked = Set.ofList blocked
            let transparent cell = not (Set.contains cell blocked)
            let visible = Los.lineOfSightBy mode transparent a b
            record caseId 3 (fun bytes -> bytes.Add(if visible then 1uy else 0uy))
        | Astar (caseId, neighbourhood, maxVisited, start, goal, (minCol, maxCol, minRow, maxRow), blocked) ->
            let blocked = Set.ofList blocked

            let walkable cell =
                cell.Col >= minCol
                && cell.Col <= maxCol
                && cell.Row >= minRow
                && cell.Row <= maxRow
                && not (Set.contains cell blocked)

            let path = Pathfinding.astar neighbourhood maxVisited walkable start goal
            record caseId 4 (fun bytes -> appendOptionalCells bytes path)
        | SessionCompatibilityCase (caseId, expected, actual, sessionId, inputId) ->
            let initialization =
                { SessionId = sessionId
                  Compatibility = actual
                  Configuration = () }
            let input =
                { SessionId = sessionId
                  InputId = inputId
                  Sequence = 0UL
                  Value = () }
            let issues = SessionCompatibility.compare expected actual
            record caseId 5 (fun bytes ->
                bytes.Add(if SessionCompatibility.isCompatible expected actual then 1uy else 0uy)
                appendU16 bytes issues.Length
                issues |> List.iter (compatibilityIssueTag >> bytes.Add)
                appendU16 bytes (SessionEnvelope.validateInitialization initialization).Length
                appendU16 bytes (SessionEnvelope.validateInput input).Length
                bytes.Add(if SessionSupport.id SessionSupport.current = "portable-runtime" then 1uy else 0uy))

    let private runtimeRecord () =
        let compatibility =
            { ContractVersion=1;EngineId="fixture.runtime";EngineVersion="1";ProfileId="fixture";SchemaId="fixture.state";SchemaVersion=1 }
        let contract: SessionContract<int,int,int,int,int> =
            { Initialize = fun value -> Ok value.Configuration
              AdmitInput = fun input state -> Ok(state + input.Value)
              Advance = fun value state -> Ok(state + int value.StepCount)
              Project = fun state -> { SessionId="fixture";Revision=uint64 state;Value=state }
              Snapshot = fun state -> { SessionId="fixture";Revision=uint64 state;Compatibility=compatibility;Value=state }
              Restore = fun value -> Ok value.Value }
        let initial =
            SessionRuntime.initialize
                { StepMicroseconds=10_000UL;MaxCatchUpSteps=3u }
                contract
                { SessionId="fixture";Compatibility=compatibility;Configuration=10 }
            |> Result.defaultWith (fun error -> failwithf "runtime fixture initialization failed: %A" error)
        let admitted, _ =
            SessionRuntime.update contract
                (SessionRuntimeObservation.AdmitInput { SessionId="fixture";InputId="add";Sequence=4UL;Value=2 }) initial
        let stale, staleEffects =
            SessionRuntime.update contract
                (SessionRuntimeObservation.AdmitInput { SessionId="fixture";InputId="add";Sequence=4UL;Value=99 }) admitted
        let advanced, _ = SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 25_000UL) stale
        let clamped, clampEffects = SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 45_000UL) advanced
        let paused, _ = SessionRuntime.update contract SessionRuntimeObservation.Pause clamped
        let stepped, _ = SessionRuntime.update contract SessionRuntimeObservation.StepOnce paused
        let disposed, _ = SessionRuntime.update contract SessionRuntimeObservation.Dispose stepped
        let inert, inertEffects = SessionRuntime.update contract (SessionRuntimeObservation.AdvanceElapsed 10_000UL) disposed
        record -2 6 (fun bytes ->
            appendI32 bytes inert.Current
            appendU32 bytes (uint32 clamped.AccumulatorMicroseconds)
            bytes.Add(if stale = admitted && staleEffects.Length = 1 then 1uy else 0uy)
            bytes.Add(if clampEffects.Length = 3 then 1uy else 0uy)
            bytes.Add(if inert = disposed && inertEffects.Length = 1 then 1uy else 0uy)
            bytes.Add(if disposed.Status = SessionRuntimeStatus.Disposed then 1uy else 0uy))

    let private operationRecord () =
        let initial : SessionOperationState<int> =
            SessionOperations.initialize
                { Target = SessionOperationTarget.LocalWorker
                  MaxPendingRequired = 2u }
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
            SessionOperations.update
                (SessionOperationObservation.CompleteRequired(second.Id, 200))
                secondState
        let committed, commits =
            SessionOperations.update
                (SessionOperationObservation.CompleteRequired(first.Id, 100))
                buffered
        let projected, projectionEffects =
            SessionOperations.update SessionOperationObservation.DemandProjection committed
        let projectionId =
            match projectionEffects with
            | [ SessionOperationEffect.DispatchProjection value ] -> value.Id
            | value -> failwithf "operation fixture expected projection dispatch, got %A" value
        let queued, coalesced =
            SessionOperations.update SessionOperationObservation.DemandProjection projected
        let refreshed, refreshEffects =
            SessionOperations.update
                (SessionOperationObservation.CompleteProjection(projectionId, 3UL, 300))
                queued
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
            bytes.Add(if early.IsEmpty && buffered.BufferedRequired.Count = 1 then 1uy else 0uy)
            bytes.Add(if coalesced.Length = 1 && queued.ProjectionDemandQueued then 1uy else 0uy)
            bytes.Add(if replacementEffects.Length = 1 && stale = replacement && staleEffects.Length = 1 then 1uy else 0uy)
            bytes.Add(if disposed.Status = SessionOperationStatus.Disposed then 1uy else 0uy))

    let encodeAll () : byte array =
        (GeneratedCases.all |> List.collect (run >> Array.toList))
        @ (runtimeRecord () |> Array.toList)
        @ (operationRecord () |> Array.toList)
        |> List.toArray

    let toLowerHex (bytes: byte array) =
        let digits = "0123456789abcdef"

        bytes
        |> Array.collect (fun value ->
            [| string digits[int value >>> 4]
               string digits[int value &&& 0x0f] |])
        |> String.concat ""
