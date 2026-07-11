// Typecheck fixtures for fs-gg-collision (see scripts/typecheck-md-blocks.fsx).
// Block 2 (the broad-phase resolve pass) is self-contained.

//#block 1
let player : Rect = { X = 0.0; Y = 0.0; Width = 24.0; Height = 24.0 }
let wall : Rect = { X = 20.0; Y = 0.0; Width = 100.0; Height = 24.0 }

//#block 3
// The discrete tile push. `classify` is "the only coupling to your world", so the world predicates
// and the outcome handlers are the reader's.
let isWater (_c: Cell) = false
let moveTo (c: Cell) = c
let drown (c: Cell) = c
let collide (c: Cell) (_obstacle: Cell) = c

//#block 4
//#skip a before/after migration contrast: it binds `let landed` TWICE (the removed `Resolution.knockback`, then the `Resolution.push` replacement), which is a duplicate definition in one module by design. Compiling it would require restructuring the block — i.e. typechecking something other than what the reader reads.
