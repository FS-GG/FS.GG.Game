// Typecheck fixtures for the turn-based-tactics TestSpec (see scripts/typecheck-md-blocks.fsx).

//#block 1
// §4.4's reachability sketch is the document's FIRST block and stands ~180 lines AHEAD of the §5.4
// type sketch (block 2) that declares `Tile` and `Unit` — so the cumulative opens have nothing to
// give it and the fixture stands them up here, field-for-field as §5.4 states them, narrowed to the
// two fields this block actually reads. Block 2 re-declares both and shadows these downstream, so
// the document still binds to ITS OWN types everywhere after §5.4.
//
// `Board`, `neighbors4`, `enterCost` and `canEndOn` are named by §4.4's prose and declared as TYPES
// nowhere in the document — they are the sketch's free vocabulary, which is exactly what a fixture
// is for. Their signatures are the contract §4.4 states in prose: `enterCost` answers "what does it
// cost this unit to step onto that tile, if it may at all" (None = impassable/blocked), and
// `canEndOn` is the separate, stricter question §4.4 turns on ("path through allied units but not
// end on them"). The BODIES are inert on purpose — this gate typechecks the block, it never runs it.
type Tile = { Col: int; Row: int }
type Unit = { Pos: Tile; MoveRange: int }

// Opaque ON PURPOSE. The block never INSPECTS a board — it threads it straight through to
// `enterCost`/`canEndOn` — so giving it fields here would invent a second, contradictory notion of
// "board" and park it in front of the reader: §7's `Model` carries `Board: Map<Tile, Terrain>`, and
// `Terrain` is not declared until block 2. A fixture may supply what the prose left unbound; it may
// not quietly decide what the document meant.
type Board = class end

let neighbors4 (tile: Tile) : Tile seq = Seq.empty
let enterCost (board: Board) (unit: Unit) (tile: Tile) : int option = None
let canEndOn (board: Board) (unit: Unit) (tile: Tile) : bool = true

//#block 3
// The spec marks this "cosmetic; not authoritative" and never declares it — the animation state is
// the reader's, and the rules must not read it.
type AnimState = { Elapsed: float; Playing: string option }

//#block 5
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =
