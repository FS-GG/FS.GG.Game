// Typecheck fixtures for the turn-based-tactics TestSpec (see scripts/typecheck-md-blocks.fsx).

//#block 1 "let enterCost (board: Board) (unit: Unit) (tile: Tile) : int = 1"
// §4.4's reachability sketch is the document's FIRST block and stands ~180 lines AHEAD of the §5.4
// type sketch (block 2) that declares `Tile` and `Unit` — so the cumulative opens have nothing to
// give it and the fixture stands them up here, narrowed to the two fields this block actually
// reads. Block 2 re-declares both and shadows these downstream, so the document still binds to ITS
// OWN types everywhere after §5.4.
//
// `Tile` is Game.Core's `Cell`, exactly as §5.4 now declares it (`type Tile = Cell`) — an
// ABBREVIATION, not a lookalike record. It has to be: §4.4 hands tiles straight to
// `Pathfinding.reachable`, which speaks `Cell`, and a structurally-identical `{ Col; Row }` of our
// own would be a different nominal type the framework rejects. Getting this wrong here would let
// the fixture certify a block that cannot compile in a real product — the fixture must model the
// document's types, never a friendlier version of them.
//
// `enterCost` and `canEndOn` are NOT declared here any more: since §4.4 stopped hand-rolling the
// Dijkstra and started calling `Pathfinding.reachable`, the block DECLARES both itself (they are
// the two predicates the primitive takes, and their shapes are the thing a reader copies). A `let`
// here as well would be FS0037 — a duplicate definition in the same module — not a shadow.
// `neighbors4` is gone with the hand-rolled search that was the only thing asking for it.
type Tile = Cell
type Unit = { Pos: Tile; MoveRange: int }

// Opaque ON PURPOSE. The block never INSPECTS a board — it threads it straight through to
// `enterCost`/`canEndOn` — so giving it fields here would invent a second, contradictory notion of
// "board" and park it in front of the reader: §7's `Model` carries `Board: Map<Tile, Terrain>`, and
// `Terrain` is not declared until block 2. A fixture may supply what the prose left unbound; it may
// not quietly decide what the document meant.
type Board = class end

//#block 3 "type Phase ="
// The spec marks this "cosmetic; not authoritative" and never declares it — the animation state is
// the reader's, and the rules must not read it.
type AnimState = { Elapsed: float; Playing: string option }
