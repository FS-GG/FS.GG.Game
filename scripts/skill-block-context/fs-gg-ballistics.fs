// Typecheck fixtures for fs-gg-ballistics (see scripts/typecheck-md-blocks.fsx).
// Blocks 1, 2 and 3 are self-contained. #140 fixed the `Pos` crossing in block 5.

//#block 4
// Velocity inheritance, and the block is precisely ABOUT the two label sets: `ship.Vel` is *model*
// state, so it is a collision-safe `Geometry.Vec2` (`Vx`/`Vy`), while `aim` came back from
// `Ballistics.intercept` and is a sim `Point` (`X`/`Y`). Typing `Vel` as a `Point` here would make
// the block compile for the wrong reason and quietly retire the crossing it teaches.
type Ship = { Vel: Geometry.Vec2 }
let ship : Ship = { Vel = { Vx = 0.0; Vy = 0.0 } }
let aim : Point = { X = 1.0; Y = 0.0 }
let shotSpeed = 900.0

//#block 5
//#rec
// Splash over a SpatialGrid. The block declares its OWN `Enemy` (storing the collision-safe
// `Geometry.Vec2`, per #140/#143) and its own `simPoint` crossing — so the fixture must NOT declare
// an `Enemy` of its own. It binds the free VALUES only and forward-references the block's type,
// which is what //#rec is for. A fixture that redeclared `Enemy` would collide with the block's
// (FS0037) and, worse, would be the type actually typechecked — so the gate would be reading a
// fixture's idea of the world instead of the skill's.
let enemies : Enemy list = []
let blast : Geometry.Vec2 = { Vx = 100.0; Vy = 100.0 }
let baseDamage = 40
