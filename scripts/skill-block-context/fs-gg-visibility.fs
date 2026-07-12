// Typecheck fixtures for fs-gg-visibility (see scripts/typecheck-md-blocks.fsx).
// Blocks 1 and 2 (the angular-sweep polygon and the isVisible oracle) are self-contained.

//#block 3 "let walls : Set<Cell> = model.Walls"
// Fov + fog-of-war: the walls and the ever-growing seen set live in the reader's Model.
type Model = { Walls: Set<Cell>; Seen: Set<Cell> }
let model : Model = { Walls = Set.empty; Seen = Set.empty }
