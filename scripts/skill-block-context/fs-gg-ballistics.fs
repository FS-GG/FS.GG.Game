// Typecheck fixtures for fs-gg-ballistics (see scripts/typecheck-skill-blocks.fsx).
// Blocks 1, 2 and 3 are self-contained. #140 fixed the `Pos` crossing in block 5.

//#block 4
// Velocity inheritance. `ship.Vel` and `aim` are sim-space vectors; `shotSpeed` a scalar.
type Ship = { Vel: Point }
let ship : Ship = { Vel = { X = 0.0; Y = 0.0 } }
let aim : Point = { X = 1.0; Y = 0.0 }
let shotSpeed = 900.0

//#block 5
// Splash over a SpatialGrid. `Pos` is the sim `Point` here — the grid buckets by `Point`, and the
// block hands `e.Pos` to `SpatialGrid.build` with no crossing, which is only correct because this
// entity stores a sim `Point`. (This is the block #140 repaired.)
type Enemy = { Pos: Point; Hp: int }
let enemies : Enemy list = []
let blast : Point = { X = 100.0; Y = 100.0 }
let baseDamage = 40
