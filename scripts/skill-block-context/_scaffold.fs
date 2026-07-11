// The generated PRODUCT's context — the part of it the skill blocks compile against.
//
// `Geometry.Vec2` is the scaffold's collision-safe vector (`src/<ProductDir>/Vec2.fs`). It lives in
// the generated product, which FS.GG.Templates owns — it is NOT in FS.GG.Game.Core and there is no
// copy of it in this repo. So to compile a block that says `type Creep = { Pos: Geometry.Vec2 }`
// against the real FS.GG.Game.Core, the gate has to stand the product side up itself. This file is
// that reconstruction, and nothing else in the harness fabricates anything.
//
// This IS a cross-repo contract, and an unenforced one: if FS.GG.Templates renames `Vx`/`Vy` or
// reshapes `Vec2`, this file keeps compiling and the gate keeps passing over skills that now teach
// a type the scaffold no longer ships. Filed as FS-GG/FS.GG.Templates#… (see the PR for #141) —
// the fix is for Templates to publish the scaffold's geometry so this can reference it instead of
// re-declaring it.
//
// Why `Geometry` and not some neutral name: the skills write `Geometry.Vec2`, unqualified, because
// in the real product BOTH this module and `FS.GG.Game.Core.Geometry` are in scope and F# MERGES
// same-named modules from two opened namespaces. `Geometry.Vec2` (product) and `Geometry.intersects`
// (Game.Core) therefore both resolve — and reproducing that merge is the point: the #129/#132/#140
// bug class is precisely a value crossing between the two halves of that merged namespace.
//
// DELIBERATELY OMITTED: the real `Vec2` also ships `toPoint`/`toRect`, which cross into
// `FS.GG.UI.Scene.Point`/`Rect` — a package this gate does not reference. They are left out rather
// than re-pointed at `FS.GG.Game.Core.Point`/`Rect`, because a `toPoint` that returned the SIM
// point would be a lie of exactly the shape this gate exists to catch, and it would make the
// missing `Vec2 -> Game.Core.Point` crossing (the whole subject of the *Spatial queries* section)
// silently typecheck. A block that reaches for `toPoint`/`toRect` fails here — correctly, and
// loudly — until the scaffold's scene edge is available to compile against.

namespace FsGg.SkillCheck.Scaffold

module Geometry =

    /// The scaffold's collision-safe position/velocity vector. `Vx`/`Vy` — deliberately zero label
    /// overlap with `FS.GG.UI.Scene.Point`/`Rect` (`X`/`Y`/`Width`/`Height`) and with the sim
    /// `FS.GG.Game.Core.Point`, which is why a product stores positions in it and must CROSS into
    /// the sim `Point` at the `SpatialGrid`/`Geometry` boundary rather than passing a bare `.Pos`.
    type Vec2 = { Vx: float; Vy: float }
