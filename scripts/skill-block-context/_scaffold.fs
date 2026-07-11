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
// THE SCENE EDGE (#165). The real `Vec2` also ships `toPoint`/`toRect`, which cross into
// `FS.GG.UI.Scene.Point`/`Rect`. They were omitted while `FS.GG.UI.Scene` was off this gate's
// reference graph; #150 put it on (FS.GG.UI.Canvas/SkiaViewer both depend on Scene), so the edge is
// reconstructed here against the REAL Scene types.
//
// They return SCENE types, never `FS.GG.Game.Core.Point`/`Rect`. The two are structurally
// IDENTICAL — both `{ X; Y }` / `{ X; Y; Width; Height }` — and nominally distinct, which is the
// whole #129/#132/#140 bug class: a `toPoint` returning the SIM point would typecheck everywhere,
// look right, and be a lie of exactly the shape this gate exists to catch. It would also make the
// missing `Vec2 -> Game.Core.Point` crossing (the subject of the *Spatial queries* section) silently
// compile. Scene's types or nothing.
//
// Both corpora therefore declare `FS.GG.UI.Scene` in their PackageRefs: this file is a prelude to
// BOTH, so the moment it binds Scene, both must be able to resolve it.
//
// FIDELITY, and the contract this rests on. The generated product that owns the real `Vec2.fs` does
// not exist yet — FS.GG.Templates ships no game template (the `dotnet new fs-gg-game` package is
// deferred, ADR-0022 §2.1), so there is no upstream definition to copy and none to diff against.
// The signatures below are reconstructed from the only authority there is: the published blocks that
// call them, and Scene's own convention. `toRect` takes the CENTRE and half-extends it, because
//   - `Rect.X`/`Y` is the TOP-LEFT corner — Scene's own `circleEvidence` builds its bounds as
//     `{ X = center.X - radius; Y = center.Y - radius; … }`; and
//   - every caller passes a centre: pong's `Geometry.toRect { Vx = cx; Vy = p.TopY + 55.0 } 18.0
//     110.0` on a 110-tall paddle lands `Y = TopY` (its actual top edge), flappy-bird passes
//     `bird.CenterY`, and fs-gg-model-swap annotates the call "centered size".
// If Templates ever publishes the geometry, DELETE this and reference it — re-declaring a
// cross-repo type is what makes this file an unenforced contract in the first place (see above).

namespace FsGg.SkillCheck.Scaffold

open FS.GG.UI.Scene

module Geometry =

    /// The scaffold's collision-safe position/velocity vector. `Vx`/`Vy` — deliberately zero label
    /// overlap with `FS.GG.UI.Scene.Point`/`Rect` (`X`/`Y`/`Width`/`Height`) and with the sim
    /// `FS.GG.Game.Core.Point`, which is why a product stores positions in it and must CROSS into
    /// the sim `Point` at the `SpatialGrid`/`Geometry` boundary rather than passing a bare `.Pos`.
    type Vec2 = { Vx: float; Vy: float }

    /// Cross a position into the SCENE's coordinate space. Returns `FS.GG.UI.Scene.Point` — not the
    /// sim `FS.GG.Game.Core.Point`, which is a different type with the same labels.
    let toPoint (v: Vec2) : Point = { X = v.Vx; Y = v.Vy }

    /// Cross a CENTRED position plus a size into a SCENE rect, so a product expresses extent through
    /// this edge instead of putting `Width`/`Height` labels on its own record (where they would
    /// mis-resolve against `Scene.Rect` in any file that opens both). `v` is the centre; the result's
    /// `X`/`Y` is the top-left corner, per Scene's convention.
    let toRect (v: Vec2) (width: float) (height: float) : Rect =
        { X = v.Vx - width / 2.0
          Y = v.Vy - height / 2.0
          Width = width
          Height = height }
