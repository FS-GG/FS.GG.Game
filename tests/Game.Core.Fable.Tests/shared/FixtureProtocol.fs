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

    let encodeAll () : byte array =
        GeneratedCases.all |> List.collect (run >> Array.toList) |> List.toArray

    let toLowerHex (bytes: byte array) =
        let digits = "0123456789abcdef"

        bytes
        |> Array.collect (fun value ->
            [| string digits[int value >>> 4]
               string digits[int value &&& 0x0f] |])
        |> String.concat ""
