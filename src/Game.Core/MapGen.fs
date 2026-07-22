namespace FS.GG.Game.Core

type Grid<'T> =
    { Width: int
      Height: int
      Cells: 'T[] }

[<Struct>]
type Tile =
    | Wall
    | Floor

type TileMap = Grid<Tile>

type Region = { Id: int; Cells: Cell[] }

type CaveParams =
    { WallChance: float
      SmoothingPasses: int
      Neighbourhood: Neighbourhood }

type BspParams =
    { MinLeaf: int
      MaxLeaf: int
      RoomPadding: int }

type Room = { Id: int; Bounds: Rect }

type RoomGraph =
    { Rooms: Room[]
      Corridors: (int * int)[] }

type RoomKind =
    | Normal
    | Start
    | Boss
    | Treasure
    | Shop
    | Secret

type FloorRoom =
    { Cell: Cell
      Kind: RoomKind
      TemplateId: int }

type FloorLayout =
    { Rooms: FloorRoom[]
      Adjacency: (Cell * Cell)[] }

type FloorParams =
    { RoomCount: int
      MaxRooms: int
      SpecialRooms: RoomKind list }

type NoiseParams =
    { Octaves: int
      Frequency: float
      Persistence: float }

open System.Collections.Generic

[<RequireQualifiedAccess>]
module MapGen =

    // ---------------------------------------------------------------------------------------------
    // Dense grid container and total addressing. Index i = Row * Width + Col, row-major. Every function
    // is total: a non-positive dimension clamps to an empty grid, an out-of-bounds address reads as
    // `ValueNone` / writes as a no-op, so no generation step can throw on a degenerate map.
    // ---------------------------------------------------------------------------------------------

    let filled (width: int) (height: int) (value: 'T) : Grid<'T> =
        let w = max 0 width
        let h = max 0 height
        { Width = w
          Height = h
          Cells = Array.create (w * h) value }

    let inBounds (grid: Grid<'T>) (cell: Cell) : bool =
        cell.Col >= 0 && cell.Row >= 0 && cell.Col < grid.Width && cell.Row < grid.Height

    let private indexOf (grid: Grid<'T>) (cell: Cell) : int = cell.Row * grid.Width + cell.Col

    let get (grid: Grid<'T>) (cell: Cell) : 'T voption =
        if inBounds grid cell then
            ValueSome grid.Cells.[indexOf grid cell]
        else
            ValueNone

    let set (grid: Grid<'T>) (cell: Cell) (value: 'T) : Grid<'T> =
        if inBounds grid cell then
            let cells = Array.copy grid.Cells
            cells.[indexOf grid cell] <- value
            { grid with Cells = cells }
        else
            grid

    // ---------------------------------------------------------------------------------------------
    // Connectivity toolkit. The neighbour offsets are in a fixed documented order, so a flood fill that
    // enqueues in that order is byte-deterministic; region ids are then fixed by the row-major scan that
    // seeds the components, independent of the fill order and of the generating seed.
    // ---------------------------------------------------------------------------------------------

    let private offsets (neighbourhood: Neighbourhood) : struct (int * int)[] =
        match neighbourhood with
        | FourWay -> [| struct (0, -1); struct (-1, 0); struct (1, 0); struct (0, 1) |]
        | EightWay ->
            [| struct (-1, -1)
               struct (0, -1)
               struct (1, -1)
               struct (-1, 0)
               struct (1, 0)
               struct (-1, 1)
               struct (0, 1)
               struct (1, 1) |]

    let regions (neighbourhood: Neighbourhood) (map: TileMap) : Region list =
        let w = map.Width
        let h = map.Height

        if w <= 0 || h <= 0 then
            []
        else
            let offs = offsets neighbourhood
            let visited = Array.zeroCreate<bool> (w * h)
            let isFloor col row =
                col >= 0 && row >= 0 && col < w && row < h && map.Cells.[row * w + col] = Floor

            let acc = List<Region>()
            let mutable nextId = 0

            // Row-major scan seeds each component in first-cell order, so `Id` is a stable function of
            // the map. The BFS fill order does not affect the labelling — `Cells` is re-sorted row-major.
            for row in 0 .. h - 1 do
                for col in 0 .. w - 1 do
                    let idx = row * w + col

                    if not visited.[idx] && map.Cells.[idx] = Floor then
                        let cells = List<Cell>()
                        let queue = Queue<struct (int * int)>()
                        visited.[idx] <- true
                        queue.Enqueue(struct (col, row))

                        while queue.Count > 0 do
                            let struct (c, r) = queue.Dequeue()
                            cells.Add { Col = c; Row = r }

                            for struct (dc, dr) in offs do
                                let nc = c + dc
                                let nr = r + dr

                                // Match the router's no-corner-cutting rule: under EightWay a diagonal
                                // adjacency counts only when both shared orthogonal cells are floor.
                                // Otherwise `regions` would call a corner-cut pair "connected" while
                                // `Pathfinding.bfs EightWay` refuses to step across it — the fill/router
                                // desync DEC-001 exists to prevent.
                                let cornerCut =
                                    neighbourhood = EightWay
                                    && dc <> 0
                                    && dr <> 0
                                    && not (isFloor (c + dc) r && isFloor c (r + dr))

                                if isFloor nc nr && not cornerCut then
                                    let nidx = nr * w + nc

                                    if not visited.[nidx] then
                                        visited.[nidx] <- true
                                        queue.Enqueue(struct (nc, nr))

                        let ordered =
                            cells
                            |> Seq.sortBy (fun cell -> struct (cell.Row, cell.Col))
                            |> Array.ofSeq

                        acc.Add { Id = nextId; Cells = ordered }
                        nextId <- nextId + 1

            List.ofSeq acc

    /// Fold a region list to its largest member: most cells, ties broken by lowest `Id`. The `Id`
    /// tie-break is what keeps the choice deterministic when two regions are the same size.
    let private pickLargest (rs: Region list) : Region voption =
        match rs with
        | [] -> ValueNone
        | head :: tail ->
            tail
            |> List.fold
                (fun best r ->
                    if r.Cells.Length > best.Cells.Length then r
                    elif r.Cells.Length = best.Cells.Length && r.Id < best.Id then r
                    else best)
                head
            |> ValueSome

    let largestRegion (neighbourhood: Neighbourhood) (map: TileMap) : Region voption =
        pickLargest (regions neighbourhood map)

    let connect (neighbourhood: Neighbourhood) (rng: Rng) (map: TileMap) : struct (TileMap * Rng) =
        let rs = regions neighbourhood map

        match rs with
        // 0 or 1 region is already a single connected floor — nothing to carve.
        | []
        | [ _ ] -> struct (map, rng)
        | _ ->
            let w = map.Width

            let target =
                match pickLargest rs with
                | ValueSome t -> t
                | ValueNone -> List.head rs // unreachable: rs has ≥ 2 members here

            let others =
                rs |> List.filter (fun r -> r.Id <> target.Id) |> List.sortBy (fun r -> r.Id)

            let cells = Array.copy map.Cells

            // Axis-then-axis L: carve the horizontal run at a.Row, then the vertical run at b.Col. Every
            // step changes one axis by one, so the corridor is 4-connected (hence connected under either
            // neighbourhood) and joins `a`'s region to `b`'s.
            let carve (a: Cell) (b: Cell) =
                let stepC = sign (b.Col - a.Col)
                let mutable c = a.Col

                while c <> b.Col do
                    cells.[a.Row * w + c] <- Floor
                    c <- c + stepC

                let stepR = sign (b.Row - a.Row)
                let mutable r = a.Row

                while r <> b.Row do
                    cells.[r * w + b.Col] <- Floor
                    r <- r + stepR

                cells.[b.Row * w + b.Col] <- Floor

            for other in others do
                // Nearest cell pair between `other` and the (original) target: minimum squared Cell
                // distance, ties broken by the smallest (from, to) pair — the same total (Col, Row) order
                // `Cell` uses everywhere, so the choice is deterministic.
                let mutable bestDist = System.Int64.MaxValue
                let mutable bestKey = Unchecked.defaultof<struct (int * int * int * int)>
                let mutable bestPair = ValueNone

                for fromCell in other.Cells do
                    for toCell in target.Cells do
                        let dc = int64 (toCell.Col - fromCell.Col)
                        let dr = int64 (toCell.Row - fromCell.Row)
                        let d = dc * dc + dr * dr
                        let key = struct (fromCell.Col, fromCell.Row, toCell.Col, toCell.Row)

                        let better =
                            d < bestDist
                            || (d = bestDist && (bestPair.IsNone || key < bestKey))

                        if better then
                            bestDist <- d
                            bestKey <- key
                            bestPair <- ValueSome(struct (fromCell, toCell))

                match bestPair with
                | ValueSome(struct (a, b)) -> carve a b
                | ValueNone -> ()

            struct ({ map with Cells = cells }, rng)

    // ---------------------------------------------------------------------------------------------
    // M2 — cellular-automata caves. Random-fill → 4-5 Moore smoothing → forced wall border → connect.
    // Every step is a pure grid pass and the fill threads the Rng row-major, so the cavern is
    // byte-identical for a seed; connect (M1) makes it a single traversable region.
    // ---------------------------------------------------------------------------------------------

    let caves (width: int) (height: int) (parameters: CaveParams) (rng: Rng) : struct (TileMap * Rng) =
        let w = max 0 width
        let h = max 0 height

        if w = 0 || h = 0 then
            struct (filled w h Wall, rng)
        else
            let wallChance =
                if System.Double.IsFinite parameters.WallChance then
                    max 0.0 (min 1.0 parameters.WallChance)
                else
                    0.45

            let passes = max 0 parameters.SmoothingPasses

            // 1) random fill, row-major, threading the Rng
            let mutable cur = Array.create (w * h) Wall
            let mutable r = rng

            for row in 0 .. h - 1 do
                for col in 0 .. w - 1 do
                    let struct (f, next) = Rng.nextFloat r
                    r <- next
                    cur.[row * w + col] <- if f < wallChance then Wall else Floor

            // 2) 4-5 Moore smoothing; out-of-bounds counts as wall
            let wallNeighbours (src: Tile[]) col row =
                let mutable n = 0

                for dr in -1 .. 1 do
                    for dc in -1 .. 1 do
                        if dc <> 0 || dr <> 0 then
                            let nc = col + dc
                            let nr = row + dr

                            if nc < 0 || nr < 0 || nc >= w || nr >= h || src.[nr * w + nc] = Wall then
                                n <- n + 1

                n

            for _ in 1..passes do
                let nxt = Array.create (w * h) Wall

                for row in 0 .. h - 1 do
                    for col in 0 .. w - 1 do
                        nxt.[row * w + col] <- if wallNeighbours cur col row >= 5 then Wall else Floor

                cur <- nxt

            // 3) force a solid wall border, so the cavern is enclosed
            for col in 0 .. w - 1 do
                cur.[col] <- Wall
                cur.[(h - 1) * w + col] <- Wall

            for row in 0 .. h - 1 do
                cur.[row * w] <- Wall
                cur.[row * w + (w - 1)] <- Wall

            // 4) one traversable cavern
            let filledMap: TileMap = { Width = w; Height = h; Cells = cur }
            connect parameters.Neighbourhood r filledMap

    // ---------------------------------------------------------------------------------------------
    // M3 — BSP room-and-corridor dungeons. Recursive partition (longer side, split position from Rng) →
    // one padded room per leaf → sibling subtrees joined by L-corridors → connect safety net. Recursion
    // is left-before-right and every draw threads the Rng, so the TileMap and the RoomGraph are both
    // byte-identical for a seed.
    // ---------------------------------------------------------------------------------------------

    let bspDungeon
        (width: int)
        (height: int)
        (parameters: BspParams)
        (rng: Rng)
        : struct (TileMap * RoomGraph * Rng) =
        let w = max 0 width
        let h = max 0 height
        let minLeaf = max 1 parameters.MinLeaf
        let maxLeaf = max minLeaf parameters.MaxLeaf
        let pad = max 0 parameters.RoomPadding

        if w = 0 || h = 0 then
            struct (filled w h Wall, { Rooms = [||]; Corridors = [||] }, rng)
        else
            let cells = Array.create (w * h) Wall
            let rooms = List<Room>()
            let corridors = List<int * int>()
            let mutable r = rng

            let drawInt lo hi =
                let struct (v, next) = Rng.nextInt lo hi r
                r <- next
                v

            let inline setFloor col row =
                if col >= 0 && row >= 0 && col < w && row < h then
                    cells.[row * w + col] <- Floor

            let carveRect x y rw rh =
                for row in y .. y + rh - 1 do
                    for col in x .. x + rw - 1 do
                        setFloor col row

            // Axis-then-axis L between two cells, same corridor style as connect.
            let carveL (a: Cell) (b: Cell) =
                let stepC = sign (b.Col - a.Col)
                let mutable c = a.Col

                while c <> b.Col do
                    setFloor c a.Row
                    c <- c + stepC

                setFloor b.Col a.Row
                let stepR = sign (b.Row - a.Row)
                let mutable rr = a.Row

                while rr <> b.Row do
                    setFloor b.Col rr
                    rr <- rr + stepR

                setFloor b.Col b.Row

            let roomCenter (rm: Room) : Cell =
                { Col = int rm.Bounds.X + int rm.Bounds.Width / 2
                  Row = int rm.Bounds.Y + int rm.Bounds.Height / 2 }

            let placeRoom x y nw nh : Room voption =
                let ax = x + pad
                let ay = y + pad
                let aw = nw - 2 * pad
                let ah = nh - 2 * pad

                if aw < 1 || ah < 1 then
                    ValueNone
                else
                    let rw = drawInt (max 1 (aw / 2)) aw
                    let rh = drawInt (max 1 (ah / 2)) ah
                    let rx = ax + drawInt 0 (aw - rw)
                    let ry = ay + drawInt 0 (ah - rh)
                    carveRect rx ry rw rh

                    let room =
                        { Id = rooms.Count
                          Bounds =
                            { X = float rx
                              Y = float ry
                              Width = float rw
                              Height = float rh } }

                    rooms.Add room
                    ValueSome room

            // Join the representative rooms of two subtrees; the left/first is the parent's representative.
            let joinSubtrees (a: Room voption) (b: Room voption) : Room voption =
                match a, b with
                | ValueSome ra, ValueSome rb ->
                    carveL (roomCenter ra) (roomCenter rb)
                    corridors.Add(ra.Id, rb.Id)
                    a
                | ValueSome _, ValueNone -> a
                | ValueNone, ValueSome _ -> b
                | ValueNone, ValueNone -> ValueNone

            let rec split x y nw nh : Room voption =
                let canSplitW = nw > maxLeaf && nw >= 2 * minLeaf
                let canSplitH = nh > maxLeaf && nh >= 2 * minLeaf

                if canSplitW || canSplitH then
                    let splitVert = if canSplitW && canSplitH then nw >= nh else canSplitW

                    if splitVert then
                        let cut = drawInt minLeaf (nw - minLeaf)
                        let left = split x y cut nh
                        let right = split (x + cut) y (nw - cut) nh
                        joinSubtrees left right
                    else
                        let cut = drawInt minLeaf (nh - minLeaf)
                        let top = split x y nw cut
                        let bottom = split x (y + cut) nw (nh - cut)
                        joinSubtrees top bottom
                else
                    placeRoom x y nw nh

            split 0 0 w h |> ignore

            // Connectivity safety net: BSP joins already connect every room, so this is a no-op on a
            // well-formed dungeon; it deterministically repairs the rare leaf-too-small-for-a-room gap.
            let struct (connected, r2) = connect FourWay r { Width = w; Height = h; Cells = cells }

            let graph =
                { Rooms = rooms.ToArray()
                  Corridors = corridors.ToArray() }

            struct (connected, graph, r2)

    // ---------------------------------------------------------------------------------------------
    // M4 — room-graph / branching-walk floors (roguelike §4.8). A branching walk from a Start room grows a
    // connected graph; special rooms land on the farthest dead-ends. Expansion is round-based over a
    // placement-order snapshot with the Rng threaded, so the whole FloorLayout is byte-identical for a seed.
    // ---------------------------------------------------------------------------------------------

    let floorSeed (runSeed: uint64) (floorIndex: int) : uint64 =
        // splitmix64 finalizer over (runSeed scattered by the index). A bijection, so distinct indices give
        // distinct, well-separated seeds without a per-index loop. Same constants as the Rng splitmix mixer.
        let z0 = runSeed + 0x9E3779B97F4A7C15UL * (uint64 (uint32 floorIndex) + 1UL)
        let z1 = (z0 ^^^ (z0 >>> 30)) * 0xBF58476D1CE4E5B9UL
        let z2 = (z1 ^^^ (z1 >>> 27)) * 0x94D049BB133111EBUL
        z2 ^^^ (z2 >>> 31)

    let floorLayout (parameters: FloorParams) (rng: Rng) : struct (FloorLayout * Rng) =
        let maxRooms = max 1 parameters.MaxRooms
        let target = max 1 (min parameters.RoomCount maxRooms)
        let mutable r = rng

        let startCell: Cell = { Col = 0; Row = 0 }
        let placed = HashSet<Cell>()
        let order = List<Cell>() // placement order — the deterministic iteration order, not `placed`
        placed.Add startCell |> ignore
        order.Add startCell

        let dirs: Cell[] =
            [| { Col = 0; Row = -1 }
               { Col = 1; Row = 0 }
               { Col = 0; Row = 1 }
               { Col = -1; Row = 0 } |]

        let neighboursOf (c: Cell) : Cell[] =
            dirs |> Array.map (fun d -> { Col = c.Col + d.Col; Row = c.Row + d.Row })

        let placedNeighbourCount (c: Cell) =
            neighboursOf c |> Array.sumBy (fun n -> if placed.Contains n then 1 else 0)

        // Round-based growth: each round expands every current room once (in placement order), gated by a
        // fair coin. Stops at the target or when a full round adds nothing. Every new room is adjacent to
        // the room that spawned it, so the graph stays connected.
        let mutable growing = true

        while order.Count < target && growing do
            let before = order.Count
            let frontier = order.ToArray()
            let mutable i = 0

            while i < frontier.Length && order.Count < target do
                let c = frontier.[i]

                for d in dirs do
                    if order.Count < target then
                        let n: Cell = { Col = c.Col + d.Col; Row = c.Row + d.Row }

                        if not (placed.Contains n) && placedNeighbourCount n < 2 then
                            let struct (b, next) = Rng.nextBool r
                            r <- next

                            if b then
                                placed.Add n |> ignore
                                order.Add n

                i <- i + 1

            growing <- order.Count > before

        // Template ids, in placement order.
        let templateOf = Dictionary<Cell, int>()

        for c in order do
            let struct (t, next) = Rng.nextInt 0 255 r
            r <- next
            templateOf.[c] <- t

        // Graph distance from Start (BFS over 4-adjacency of placed rooms).
        let dist = Dictionary<Cell, int>()
        dist.[startCell] <- 0
        let bfsq = Queue<Cell>()
        bfsq.Enqueue startCell

        while bfsq.Count > 0 do
            let c = bfsq.Dequeue()

            for n in neighboursOf c do
                if placed.Contains n && not (dist.ContainsKey n) then
                    dist.[n] <- dist.[c] + 1
                    bfsq.Enqueue n

        // Dead-ends (non-Start rooms with one placed neighbour), farthest first, ties by ascending Cell.
        let deadEnds =
            order
            |> Seq.filter (fun c -> c <> startCell && placedNeighbourCount c = 1)
            |> Seq.sortByDescending (fun c -> struct (dist.[c], -c.Col, -c.Row))
            |> Array.ofSeq

        let specialAt = Dictionary<Cell, RoomKind>()

        parameters.SpecialRooms
        |> List.iteri (fun i kind ->
            if i < deadEnds.Length then
                specialAt.[deadEnds.[i]] <- kind)

        let rooms =
            [| for c in order ->
                   let kind =
                       if c = startCell then Start
                       else
                           match specialAt.TryGetValue c with
                           | true, k -> k
                           | _ -> Normal

                   { Cell = c; Kind = kind; TemplateId = templateOf.[c] } |]

        // Adjacency: each 4-adjacent placed pair once (right/down probes dedupe), sorted for a fixed order.
        let adjacency =
            [ for c in order do
                  for d in [| { Col = 1; Row = 0 }; { Col = 0; Row = 1 } |] do
                      let n: Cell = { Col = c.Col + d.Col; Row = c.Row + d.Row }

                      if placed.Contains n then
                          yield (c, n) ]
            |> List.sortBy (fun (a, b) -> struct (a.Col, a.Row, b.Col, b.Row))
            |> Array.ofList

        struct ({ Rooms = rooms; Adjacency = adjacency }, r)

    // ---------------------------------------------------------------------------------------------
    // M5 — maze (recursive backtracker), value-noise height field + classify, and Poisson-disk scatter.
    // All integer-threaded from the Rng in a fixed order; the only float is the hashed unit-interval noise
    // value and its fixed smootherstep interpolation. Byte-identical for a seed.
    // ---------------------------------------------------------------------------------------------

    let maze (width: int) (height: int) (rng: Rng) : struct (TileMap * Rng) =
        let w = max 0 width
        let h = max 0 height

        if w < 3 || h < 3 then
            struct (filled w h Wall, rng)
        else
            let cells = Array.create (w * h) Wall
            let cw = (w - 1) / 2 // maze cells across
            let ch = (h - 1) / 2 // maze cells down
            let visited = Array.zeroCreate<bool> (cw * ch)
            let mutable r = rng

            let dirs =
                [| struct (0, -1); struct (1, 0); struct (0, 1); struct (-1, 0) |]

            let stack = Stack<struct (int * int)>()
            visited.[0] <- true
            cells.[1 * w + 1] <- Floor // maze cell (0,0) is tile (1,1)
            stack.Push(struct (0, 0))

            while stack.Count > 0 do
                let struct (cx, cy) = stack.Peek()
                let unvisited = ResizeArray<struct (int * int * int * int)>()

                for struct (dx, dy) in dirs do
                    let ncx = cx + dx
                    let ncy = cy + dy

                    if ncx >= 0 && ncy >= 0 && ncx < cw && ncy < ch && not visited.[ncy * cw + ncx] then
                        unvisited.Add(struct (ncx, ncy, dx, dy))

                if unvisited.Count = 0 then
                    stack.Pop() |> ignore
                else
                    let struct (pick, next) = Rng.nextInt 0 (unvisited.Count - 1) r
                    r <- next
                    let struct (ncx, ncy, dx, dy) = unvisited.[pick]
                    visited.[ncy * cw + ncx] <- true
                    let ctx = 2 * cx + 1
                    let cty = 2 * cy + 1
                    cells.[(cty + dy) * w + (ctx + dx)] <- Floor // the wall tile between the two cells
                    cells.[(2 * ncy + 1) * w + (2 * ncx + 1)] <- Floor // the neighbour cell
                    stack.Push(struct (ncx, ncy))

            struct ({ Width = w; Height = h; Cells = cells }, r)

    let heightField (width: int) (height: int) (parameters: NoiseParams) (rng: Rng) : Grid<int> =
        let w = max 0 width
        let h = max 0 height

        if w = 0 || h = 0 then
            { Width = w; Height = h; Cells = [||] }
        else
            let struct (saltBits, _) = Rng.nextInt System.Int32.MinValue System.Int32.MaxValue rng
            let salt = uint64 (uint32 saltBits)
            let octaves = max 1 parameters.Octaves

            let freq =
                if System.Double.IsFinite parameters.Frequency && parameters.Frequency > 0.0 then
                    parameters.Frequency
                else
                    0.1

            let persistence =
                if System.Double.IsFinite parameters.Persistence then
                    max 0.0 (min 1.0 parameters.Persistence)
                else
                    0.5

            // Hash a lattice point (per octave) to a unit-interval value, top-53-bits like Rng.nextFloat.
            let latticeValue ix iy oct =
                let mutable z = salt + 0x9E3779B97F4A7C15UL * uint64 (uint32 ix)
                z <- z + 0x632BE59BD9B4E019UL * uint64 (uint32 iy)
                z <- z + 0xC2B2AE3D27D4EB4FUL * uint64 (uint32 oct)
                let z1 = (z ^^^ (z >>> 30)) * 0xBF58476D1CE4E5B9UL
                let z2 = (z1 ^^^ (z1 >>> 27)) * 0x94D049BB133111EBUL
                let bits = z2 ^^^ (z2 >>> 31)
                float (bits >>> 11) * (1.0 / 9007199254740992.0)

            let smootherstep (t: float) = t * t * t * (t * (t * 6.0 - 15.0) + 10.0)

            let sampleOctave fx fy oct =
                let x0 = int (floor fx)
                let y0 = int (floor fy)
                let tx = smootherstep (fx - float x0)
                let ty = smootherstep (fy - float y0)
                let v00 = latticeValue x0 y0 oct
                let v10 = latticeValue (x0 + 1) y0 oct
                let v01 = latticeValue x0 (y0 + 1) oct
                let v11 = latticeValue (x0 + 1) (y0 + 1) oct
                let a = v00 + (v10 - v00) * tx
                let b = v01 + (v11 - v01) * tx
                a + (b - a) * ty

            let cells = Array.zeroCreate<int> (w * h)

            for row in 0 .. h - 1 do
                for col in 0 .. w - 1 do
                    let mutable amp = 1.0
                    let mutable f = freq
                    let mutable sum = 0.0
                    let mutable norm = 0.0

                    for oct in 0 .. octaves - 1 do
                        sum <- sum + amp * sampleOctave (float col * f) (float row * f) oct
                        norm <- norm + amp
                        amp <- amp * persistence
                        f <- f * 2.0

                    let v = if norm > 0.0 then sum / norm else 0.0
                    cells.[row * w + col] <- max 0 (min 255 (int (v * 255.0)))

            { Width = w; Height = h; Cells = cells }

    let classify (table: (int * 'T) list) (field: Grid<int>) : Grid<'T> =
        match table with
        | [] -> { Width = 0; Height = 0; Cells = [||] }
        | _ ->
            let sorted = table |> List.sortBy fst |> Array.ofList

            let classifyOne heightAt =
                let mutable chosen = snd sorted.[0]

                for (threshold, value) in sorted do
                    if heightAt >= threshold then
                        chosen <- value

                chosen

            { Width = field.Width
              Height = field.Height
              Cells = field.Cells |> Array.map classifyOne }

    let poissonScatter (mask: Grid<bool>) (minDist: int) (rng: Rng) : struct (Cell list * Rng) =
        let w = mask.Width
        let h = mask.Height
        let d = max 1 minDist

        if w <= 0 || h <= 0 then
            struct ([], rng)
        else
            let d2 = d * d
            let mutable r = rng
            let occupied = HashSet<Cell>()
            let samples = List<Cell>()
            let active = List<Cell>()

            let isEligible (c: Cell) =
                c.Col >= 0
                && c.Row >= 0
                && c.Col < w
                && c.Row < h
                && mask.Cells.[c.Row * w + c.Col]

            // No existing sample strictly within d (integer squared-distance scan of the d-neighbourhood).
            let farEnough (c: Cell) =
                let mutable ok = true
                let mutable dr = -d

                while ok && dr <= d do
                    let mutable dc = -d

                    while ok && dc <= d do
                        if dc * dc + dr * dr < d2 then
                            let s: Cell = { Col = c.Col + dc; Row = c.Row + dr }

                            if occupied.Contains s then
                                ok <- false

                        dc <- dc + 1

                    dr <- dr + 1

                ok

            // Seed: first eligible cell in row-major order (seed-independent, deterministic).
            let mutable seed = ValueNone
            let mutable idx = 0

            while seed.IsNone && idx < w * h do
                let c: Cell = { Col = idx % w; Row = idx / w }

                if isEligible c then
                    seed <- ValueSome c

                idx <- idx + 1

            match seed with
            | ValueNone -> struct ([], r)
            | ValueSome s0 ->
                occupied.Add s0 |> ignore
                samples.Add s0
                active.Add s0

                while active.Count > 0 do
                    let struct (ai, r1) = Rng.nextInt 0 (active.Count - 1) r
                    r <- r1
                    let p = active.[ai]
                    let mutable placed = false
                    let mutable k = 0

                    while k < 30 && not placed do
                        let struct (dc, r2) = Rng.nextInt (-2 * d) (2 * d) r
                        r <- r2
                        let struct (dr, r3) = Rng.nextInt (-2 * d) (2 * d) r
                        r <- r3
                        let dist2 = dc * dc + dr * dr

                        if dist2 >= d2 && dist2 <= 4 * d2 then
                            let cand: Cell = { Col = p.Col + dc; Row = p.Row + dr }

                            if isEligible cand && not (occupied.Contains cand) && farEnough cand then
                                occupied.Add cand |> ignore
                                samples.Add cand
                                active.Add cand
                                placed <- true

                        k <- k + 1

                    if not placed then
                        active.RemoveAt ai

                struct (List.ofSeq samples, r)
