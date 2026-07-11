// Typecheck fixtures for the sandbox-survival TestSpec (see scripts/typecheck-md-blocks.fsx).
//
// Two distinct needs here.
//
// BLOCK 1 (§5.1 entities) uses `Vec2`, `ItemStack` and `ItemId` — all of which the document declares
// in BLOCK 2 (§5.2), i.e. BELOW their first use. A cumulative corpus only lets a block see the ones
// BEFORE it, so block 1 cannot borrow them and they are reconstructed here instead, matching §5.2
// exactly (float32 throughout, and Vx/Vy — never X/Y, which collide with Scene's Point/Rect). Block 2
// then re-declares its own, which shadow these; the two agree by construction, and if §5.2 ever
// changes shape this file is the thing to change with it.
//
// BLOCK 2 needs //#rec: it declares `TileType` with a `Station of StationKind` case ABOVE its
// `StationKind` declaration, which only a recursive module accepts.

//#block 1
// Mirrors §5.2. This world is float32 throughout (see Hp/Hunger), so it declares its OWN vector
// rather than abbreviating the scaffold's float-based Geometry.Vec2 — but the LABELS are the thing
// that must not change.
type Vec2 = { Vx: float32; Vy: float32 }
type ItemId = ItemId of int
type ItemStack = { Item: ItemId; Count: int }

//#block 2
//#rec
// A generated column of tiles, cached per (chunkX, chunkY) — §3 worldgen.
type Chunk = { Cx: int; Cy: int; Tiles: Tile[,] }
type InputState = { Held: Set<Key>; MouseTile: (int * int) option; LeftDown: bool; RightDown: bool }
type UiState = Playing | InventoryOpen | Paused | Dead | Title
type RngState = { Seed: uint64 }
type WorldEvent =
    | TileBroken of int * int
    | TilePlaced of int * int
    | EnemyKilled of EntityId
    | PlayerHurt of float32

//#block 4
// A DU-CASE CONTINUATION. The prose above this block says "add these cases to your Msg"; the block
// is written as bare `| Case` lines with no `type ... =` header, so it cannot stand alone. The
// fixture supplies the header the prose left implicit, and the block's cases are then compiled
// verbatim below it — which is the point: the case SHAPES (`MenuAdjust of dir:int`) are what a
// reader copies, and they are what this checks.
type MenuMsg =
