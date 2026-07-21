// Typecheck fixtures for fs-gg-game-core (see scripts/typecheck-md-blocks.fsx).
//
// Each //#block N section is prepended to block N of the SKILL.md inside a `module rec`, so a
// binding here MAY forward-reference a type the block itself declares below it (block 7's `creeps`
// is exactly that). Blocks 2, 3, 5 and 8 are self-contained and need no section.
//
// These bindings stand in for the reader's world. Keep them the SHAPE the prose promises — a
// binding that is wrong in a way the block cannot see (say, an `enemies` whose `Pos` is already a
// sim `Point`) would quietly excuse the very Vec2 -> Point crossing the block exists to teach.

//#block 1 "let simInterval = 1.0 / 60.0"
// The fixed-step section: `integrate` is "your game step: world -> dt -> world", and the model
// carries a StepState. `lerpWorld` is the reader's own interpolation.
type World = { Tick: int }
let integrate (w: World) (_dt: float) : World = { w with Tick = w.Tick + 1 }
let dtSeconds = 1.0 / 144.0
let model = {| Sim = Loop.init { Tick = 0 } |}
let lerpWorld (_previous: World) (current: World) (_t: float) : World = current

//#block 4 "let reach = Pathfinding.reachable FourWay 4096 cost canEndOn (Pathfinding.budgetFor unit.Move) unit.Cell"
// The weighted move-range one-liner: `cost`/`canEndOn` are the game's predicates, `unit` the reader's.
type Mover = { Cell: Cell; Move: int }
let cost (_c: Cell) = 1
let canEndOn (_c: Cell) = true
let unit : Mover = { Cell = { Col = 0; Row = 0 }; Move = 4 }

//#block 6 "let grid = SpatialGrid.build 32.0 [ for e in enemies -> simPoint e.Pos, e.Id ]"
// Spatial queries. `enemies` positions are stored in the scaffold's collision-safe Vec2 — that is
// the whole premise of the section, and it is what forces the `simPoint` crossing the block
// teaches. Giving `Pos` a sim `Point` here would make the block typecheck for the wrong reason.
type Enemy = { Id: int; Pos: Geometry.Vec2 }
let enemies : Enemy list = []
let blast : Geometry.Vec2 = { Vx = 64.0; Vy = 48.0 }

//#block 7 "type Creep = { Pos: Geometry.Vec2; Hp: int }"
//#rec
// The grid-sim recipe (the #132 defect lived here). `creeps` FORWARD-REFERENCES `Creep`, which the
// block declares — legal because the generated unit is a `module rec`. Binding it any other way
// would mean either duplicating the block's own type (and then typechecking the duplicate instead
// of the subject) or leaving it inferred, which cannot resolve `.Pos`.
let cols = 32
let rows = 24
let walls : Set<Cell> = Set.empty
let spawn : Cell = { Col = 0; Row = 0 }
let goal : Cell = { Col = 31; Row = 23 }
let cellPx = 32.0
let creeps : Creep list = []
