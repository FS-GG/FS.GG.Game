---
name: fs-gg-collision
description: Detect and resolve collisions in a generated FS.GG.UI product — narrow-phase Contact manifolds over Geometry, a broad-phase loop you write over SpatialGrid, and kinematic response over Resolution.
---

# Collision (Detection + Response) Capability

## Scope

Use this skill for the **collision** half of a game/sim product: testing which bodies overlap and by how
much (narrow-phase manifolds), pruning the pairs you bother to test (broad-phase), and **resolving** those
overlaps (separating penetrating bodies, killing the into-wall velocity, knocking a unit back over tiles).
Every function here is pure, total, NaN-safe, and byte-deterministic — safe under a replayed `update`.
Advancing the world on a fixed step is [[fs-gg-game-core]]'s job; firing the projectiles that *cause* a
hit is [[fs-gg-ballistics]]'s; rendering the resolved world is [[fs-gg-rendering:fs-gg-scene]]'s. This
skill materializes for the `game` and `sample-pack` profiles.

## Public Contract

The signatures you consume are bundled with this product; there is no product-owned collision source —
you compose framework functions in your own `update`:

- `docs/api-surface/Game.Core/Geometry.fsi` — the narrow-phase detectors `aabbContact`, `circleContact`,
  `circleAabbContact`, `polygonContact` (each a `Contact option`), plus `obbPolygon`, `sweptIntersects`,
  `intersects`, `contains`, `containsPoint`, `center`, `ofCenter`.
- `docs/api-surface/Game.Core/Resolution.fsi` — the response layer `pushOut`, `slide`, `knockback`.
- `docs/api-surface/Game.Core/SpatialGrid.fsi` — the broad-phase `build` / `query` / `queryRadius`.
- `docs/api-surface/Game.Core/Primitives.fsi` — the `Point`/`Rect`/`Circle`/`ConvexPolygon` shapes and
  the `Contact = { Normal: Point; Depth: float }` manifold every detector produces.

All are **total**: a degenerate rect, a NaN coordinate, or a non-positive radius returns a documented
value (usually `None`), never a throw.

## The one rule: detection is separate from response

`Geometry` **detects** and hands back a `Contact` — a minimum-translation vector (`Normal`, a unit axis)
and a penetration `Depth`. It never moves anything. `Resolution` **responds** — it consumes that `Contact`
and produces a new position or velocity. Nothing in `Game.Core` fuses the two, and you should not either:
a `collide` that both tests overlap *and* mutates both bodies makes a physics bug impossible to localise,
because you can no longer ask "did we mis-*detect* or mis-*respond*?" separately.

```fsharp
open FS.GG.Game.Core
match Geometry.aabbContact player wall with          // Normal points player → wall along the MTV
| Some contact -> Resolution.pushOut (Geometry.center player) contact   // subtracts Normal × Depth
| None -> Geometry.center player                      // no positive-area overlap: nothing to do
```

## Narrow-phase: pick the manifold for the shape pair

One detector per shape pair; all return a `Contact option`, so response stays shape-agnostic:

- `Geometry.aabbContact a b` — box vs box; `Some` exactly when `intersects a b` (positive-area overlap,
  strict edges — corner/edge touching is **not** a contact).
- `Geometry.circleContact a b` — circle vs circle (coincident centres degenerate to `Normal = (1,0)`).
- `Geometry.circleAabbContact c box` — circle vs box, by clamping the centre to the box.
- `Geometry.polygonContact a b` — arbitrary convex vs convex, by SAT. Build the rotated-box case with
  `Geometry.obbPolygon centre halfExtents rotation`; at `rotation = 0` it agrees with `aabbContact`.

A body fast enough to **tunnel** a thin wall in one step slips past a same-frame `aabbContact`; test the
whole displacement with `Geometry.sweptIntersects mover velocity wall` (box), or [[fs-gg-ballistics]]'s
segment casts (a swept *point* such as a bullet), instead.

## Broad-phase: write the SpatialGrid loop yourself, honestly

Do not test every body against every other — that is O(n²). There is no `collide` helper that hides the
loop; you own it, and it is short. Bucket once with `SpatialGrid`, then narrow-phase only near neighbours,
widening each query region by the largest body half-extent so no straddling pair is missed.

`pushOut` moves **exactly one** body by the full MTV, so decide which body yields *before* you write the
loop. The kinematic answer: a dynamic body separates itself from whatever it overlaps; a static body never
moves. Do **not** dedupe pairs by id or index — that hands the push to whichever body happens to sort
first, which lets a wall shove the player.

```fsharp
open FS.GG.Game.Core

type Body = { Id: int; Bounds: Rect; Static: bool }   // walls are Static; movers are not

let resolvePass (cellSize: float) (pad: float) (bodies: Body list) : Body list =
    // Broad-phase: one bucketing keyed by each body's centre, insertion order preserved.
    let grid = SpatialGrid.build cellSize [ for b in bodies -> Geometry.center b.Bounds, b ]
    [ for b in bodies ->
        if b.Static then b                                       // a wall is never displaced
        else
            let c = Geometry.center b.Bounds
            // Query a region grown by `pad` (>= the largest half-extent) so no overlap is missed.
            let region = Geometry.ofCenter c (b.Bounds.Width + pad) (b.Bounds.Height + pad)
            (b, SpatialGrid.query region grid)
            ||> List.fold (fun acc other ->
                if other.Id = acc.Id then acc                     // never test a body against itself
                else
                    match Geometry.aabbContact acc.Bounds other.Bounds with
                    | Some contact ->                            // Normal points acc → other
                        let sep = Resolution.pushOut (Geometry.center acc.Bounds) contact
                        { acc with Bounds = Geometry.ofCenter sep acc.Bounds.Width acc.Bounds.Height }
                    | None -> acc) ]
```

Every pair is deliberately visited from **both** sides. A dynamic/static pair resolves once — the mover
yields the full `Depth`, which is exactly right. A dynamic/dynamic pair resolves twice, each body backing
off the full `Depth`, which over-separates by 2×; halve `contact.Depth` before `pushOut` for the 50/50
split. That lever is yours precisely because `Game.Core` ships no `ResponseRule` enum to pick it for you.

`SpatialGrid.query` returns the **exact** set in the region (no false positives or negatives) in
insertion order, so the pass is replay-identical. For a radial query — a shockwave, an aggro bubble — use
`SpatialGrid.queryRadius centre radius grid`, whose squared-distance test matches [[fs-gg-ballistics]]'s
splash membership exactly (no rim `sqrt` to disagree by one ulp).

## Response: separate, slide, or knock back

`Resolution` is the whole response vocabulary, and it is deliberately small:

- `Resolution.pushOut position contact` — move a body out of penetration along the MTV. A zero-`Depth`
  contact returns `position` unchanged; for anti-jitter slop, pass a `Contact` with reduced `Depth`.
- `Resolution.slide velocity normal` — kinematic wall-slide: strip the component of `velocity` along the
  (unit) `contact.Normal`, keep the tangential part. No internal normalization to fold a `sqrt` into.
- `Resolution.knockback start step distance blocked` — discrete **tile** knockback over integer
  `Cell = { Col; Row }` (from `Pathfinding`): step from `start` up to `distance` times, stopping in the
  last free cell before the first `blocked` one. Tile displacement, not continuous.

```fsharp
open FS.GG.Game.Core

// Continuous slide-along-wall: separate the body, then strip the into-wall velocity component.
let slideAlong (box: Rect) (vel: Point) (contact: Contact) =
    Geometry.ofCenter (Resolution.pushOut (Geometry.center box) contact) box.Width box.Height,
    Resolution.slide vel contact.Normal

// Discrete tile knockback: three cells east, stopping in the last free cell before the first wall.
let knocked = Resolution.knockback { Col = 2; Row = 5 } { Col = 1; Row = 0 } 3 (fun c -> c.Col >= 6)
```

**Impulse physics is explicitly out of scope.** Mass, restitution, and friction are a separate, heavier
layer this core does not model — `Resolution.fsi` says so outright. `slide` stops and grazes, it does not
*bounce*; build any momentum exchange on top, deliberately, in your own product.

## Common pitfalls

- **Fusing detection and response.** A `collide` that overlaps-tests *and* moves both bodies hides which
  half is wrong, and swapping the `aabbContact` args flips `Normal`. Keep detection and `Resolution.*`
  separate, and feed the body you are separating first.
- **O(n²) proximity scans.** Bucket once with `SpatialGrid.build`, then `query`/`queryRadius` the
  neighbourhood — never a nested loop over all bodies.
- **Deduping pairs by id or index.** `pushOut` displaces one body, so `if j <= i then skip` silently
  elects the lower-indexed body as the one that yields — and a wall with a low id will shove the player.
  Skip on *staticness*, not ordering.
- **Testing the post-move position of a fast body.** It tunnels the wall. Use `Geometry.sweptIntersects`
  (box) or a [[fs-gg-ballistics]] segment cast (point). Also don't hand-roll a look-alike `Rect`/`Point`:
  a bare `{ X = …; Y = … }` binds to whichever record is last in scope — reuse the framework shapes.
- **Expecting one pass to de-stack a pile, or reaching for restitution/friction.** `resolvePass` is a
  single kinematic sweep; iterate it for dense stacks, and build any impulse physics yourself — it is
  out of this core's scope.

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned collision examples (the `Contact`s your detectors
produce, the position/velocity your `Resolution` calls return, and determinism replays).

## Evidence

Record collision evidence (manifold cases, separation/slide results, determinism replays) under this
product's `readiness/` paths. Do not copy framework readiness reports into the product.

## Package Boundary

`Geometry`, `Resolution`, `SpatialGrid`, and the `Point`/`Rect`/`Circle`/`ConvexPolygon`/`Contact` shapes
all live in `FS.GG.Game.Core` (referenced only on the `game`/`sample-pack` profiles) — the BCL-only bottom
layer that depends on nothing and pulls in no viewer, layout, or widget machinery. Keep rendering the
resolved world in [[fs-gg-rendering:fs-gg-scene]] and host wiring in [[fs-gg-rendering:fs-gg-skiaviewer]].

## Generated Product

Each fixed step, build the current bodies from your `Model`, run one `resolvePass` (broad-phase
`SpatialGrid` + narrow-phase `Geometry.*Contact` + `Resolution.*` response), fold the separated bodies and
adjusted velocities back into your `Model`, then hand the world to your `View` as a `Scene`.

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the driven library's own reference), then
community sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-game-core]] — the fixed-step loop, spatial queries, and pathfinding (source of the `Cell`
  `Resolution.knockback` displaces over) that drive each collision pass.
- [[fs-gg-ballistics]] — fires the projectiles whose hit you resolve here; owns the swept segment casts.
- [[fs-gg-visibility]] — the sibling per-frame geometry pass, over the same `Point`/`Segment` vocabulary.
- [[fs-gg-grids]] — the grid vocabulary `Resolution.knockback` steps a body across.
- [[fs-gg-rendering:fs-gg-scene]] — owns the render `Rect`/`Point`; draws the resolved world.
- [[fs-gg-rendering:fs-gg-skiaviewer]] — drives the fixed-step loop from the host window.

## Sources / links

- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- AABB collision + minimum-translation vector background: https://gamedev.stackexchange.com/q/29786
- Separating Axis Theorem (convex polygon) background: https://dyn4j.org/2010/01/sat/
