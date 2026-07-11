// Typecheck fixtures for fs-gg-ai (see scripts/typecheck-md-blocks.fsx).

//#block 1
//#skip an .fsi SIGNATURE listing (the skill's Public Contract section: `val view: ...`, `type TeamView<'T>` with no definition). It is not implementation code and cannot be compiled as an .fs — the surface it declares is already gated, byte-for-byte, by the surface-baseline-drift job.

//#block 2
//#skip a signature sketch with an ELIDED body (`let decide ... : Command * Rng = ...`). `...` is not F#; there is nothing here to typecheck.

//#block 3
// Per-agent RNG substream. `AgentId` is a top-level type in FS.GG.Game.Core, not a member of the
// `Ai` module — the module is [<RequireQualifiedAccess>], its vocabulary is not.
type Agent = { Id: AgentId }
let agent : Agent = { Id = AgentId 1 }
let root : Rng = Rng.ofSeed 1UL

//#block 4
// The threat field is recomputed on a cadence OR on terrain change — the block is about when, so
// the field itself and its inputs are the reader's.
type Terrain = { Version: int }
let tick = 120
let terrain : Terrain = { Version = 3 }
let cachedVersion = 3
let hasLos (_a: Cell) (_b: Cell) = true
let sources : (Cell * float * int) list = []
let coarseCells : Cell list = []
let cached : Map<Cell, float> = Map.empty

//#block 5
// Flee field: navigation comes from the substrate (Pathfinding), policy from Ai.
let cost (_c: Cell) = 1
let threatCells : Cell list = []

//#block 6
//#rec
// Plan enumeration + the total tie-break. `Ability` and the scoring terms are the game's. The
// scoring functions take the block's OWN `Plan` type, declared below them — hence //#rec.
type Ability = Shoot | Ram
let positions : Cell list = []
let abilities : Ability list = []
let targetsOf (_pos: Cell) (_a: Ability) : Cell list = []
let expectedDamage (_p: Plan) = 0.0
let expectedBuildingDamage (_p: Plan) = 0.0
let isKillingBlow (_p: Plan) = false
let selfExposure (_p: Plan) = 0.0
