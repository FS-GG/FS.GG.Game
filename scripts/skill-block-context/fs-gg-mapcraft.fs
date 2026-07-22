// Typecheck fixtures for fs-gg-mapgen (see scripts/typecheck-md-blocks.fsx).
// Each block is a sketch over the MapGen surface; the fixture supplies the free values a reader would
// have in hand (a seed `Rng`, an existing map to operate on) so the block compiles against the real
// FS.GG.Game.Core without the example having to first generate them.

//#block 1 "let g : TileMap = MapGen.filled 40 30 Wall"
// Substrate: address a grid, then find/connect its regions. `someMap` is any map the caller already has;
// `rng` is the seed stream the connect threads.
let someMap: TileMap = MapGen.filled 40 30 Wall
let rng: Rng = Rng.ofSeed 1UL

//#block 2 "let struct (cave, rng') = MapGen.caves 48 32 p rng"
// Caves: the seed stream the generator threads.
let rng: Rng = Rng.ofSeed 1UL

//#block 3 "let struct (dungeon, graph, rng') = MapGen.bspDungeon 64 48 p rng"
// BSP dungeon: the seed stream the generator threads.
let rng: Rng = Rng.ofSeed 1UL

//#block 4 "let p : FloorParams = { RoomCount = 15; MaxRooms = 20; SpecialRooms = [ Boss; Treasure; Shop ] }"
// Floors: the run seed and floor index a game derives each floor's seed from.
let runSeed: uint64 = 1UL
let floorIndex: int = 0

//#block 5 "let struct (mz, rng1) = MapGen.maze 31 21 rng"
// Maze/noise/scatter: the seed stream, and an existing `cave` map to draw the scatter mask from.
let rng: Rng = Rng.ofSeed 1UL
let cave: TileMap = MapGen.filled 64 64 Floor

//#block 6 "let reached   = MapAnalysis.reachable FourWay 4096 isFloor { Col = 1; Row = 1 }   // Set<Cell> reachable from a start"
// Analyze: an existing map to ask reachability/connectivity of (any producer would do).
let level: TileMap = MapGen.filled 48 32 Floor
