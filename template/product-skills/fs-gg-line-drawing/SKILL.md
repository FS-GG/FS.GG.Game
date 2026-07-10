---
name: fs-gg-line-drawing
description: Walk the straight line between two grid cells in a generated FS.GG.UI product — the thin Bresenham line, the no-diagonal-gap supercover, and a symmetric point-to-point line-of-sight, all integer and replay-identical.
---

# Grid Line-Drawing Capability

## Scope

Use this skill for **grid line-drawing**: answering *"which tiles does the straight line between these two
cells cross?"* and *"can A see/hit B across them?"* It covers the ordered **Bresenham** cell line, the
**supercover** variant that touches every cell with no diagonal gap, and the point-to-point
**line-of-sight** query built on the same walk. It is the workhorse behind tile line-of-sight, beam/ray
attacks, drawing walls/roads/rivers between two tiles, and shell travel over a grid. Everything here is
pure, total, and **integer** — no `atan2`, no `Math.Round`, no float lerp — so it is safe to call from a
replayed `update`. Routing the path a line complements is [[fs-gg-game-core]]'s `Pathfinding`; the naming
of the *wall between* two tiles is [[fs-gg-grids]]'s `Edge`; the *continuous* (float `Point`,
angular-sweep) sibling is [[fs-gg-visibility]], and the discrete **field of view** (`Fov`) is taught there
too. This skill materializes for the `game` and `sample-pack` profiles.

## Public Contract

The signatures you consume are bundled with this product:

- `docs/api-surface/Game.Core/Los.fsi` — the top-level `LineMode` type (`Thin | Supercover`) plus the
  `Los` module: `line`, `supercover`, `trace`, `lineOfSight`, `lineOfSightBy`. Shipped in
  `FS.GG.Game.Core`, referenced on the `game` and `sample-pack` profiles.
- `docs/api-surface/Game.Core/Pathfinding.fsi` — the shared integer `Cell` (`{ Col; Row }`) every line is
  expressed over, and the `Cell -> bool` walkability/opacity predicate convention.

All entry points are **total**: degenerate inputs (`a = b`, axis-aligned, diagonal, any octant) return a
documented value — they never throw. `a = b` yields `[a]` from the walks and `true` from the sight
queries.

## Cells, not points — the walk is expressed over the discrete atom

A line runs over the shared `Cell` — an **integer** tile index, deliberately not the float `Point`. `Cell`
is exactly what `Pathfinding` routes over, so a line and a path speak one vocabulary: route with
`Pathfinding.astar`, then draw or step along the result with `Los.line`. Build endpoints from your world
(the player's tile, the target's tile via `Grids.cellAt`) and pass them straight in.

```fsharp
open FS.GG.Game.Core

let a = { Col = 0; Row = 0 }
let b = { Col = 5; Row = 2 }
let tiles = Los.line a b        // ordered cells a..b — a road, a beam, a movement track
```

Do **not** re-roll a look-alike `(row, col)` record that shadows `Cell`, and do not conflate `Cell` (a
discrete tile) with the float `Point` ([[fs-gg-rendering:fs-gg-scene]]) — they are different atoms.

## Thin vs Supercover — the game's movement rules pick the winner

There are two walks, and the choice is **not** a matter of taste. They disagree exactly at a diagonal wall
join:

- `Los.line a b` — the **thin**, diagonal-connected line (integer Bresenham), ~`max(|dx|,|dy|)` cells. A
  step may advance both axes at once, so the line **cuts the corner**: it passes *between* two
  diagonally-touching cells without entering either. Sight can therefore leak through the gap where two
  diagonal walls meet.
- `Los.supercover a b` — the **supercover** walk, strictly 4-connected (~`|dx|+|dy|` cells; each
  consecutive pair differs by exactly 1 in exactly one axis). It emits **every** cell the real segment's
  area touches, so **nothing** leaks through a diagonal join.

Neither is universally correct. The rule: **if a unit may move between two diagonally-touching walls,
sight should leak there too** — use `Thin`. If it must not, use `Supercover`. Match the walk to your
movement neighbourhood, and note that `Pathfinding`'s `EightWay` already forbids corner-cutting, which
pairs naturally with `Supercover` sight.

Select at runtime with `Los.trace`:

```fsharp
let mode = Supercover           // LineMode is a top-level type; no qualifier needed
let cells = Los.trace mode a b  // = Los.line a b under Thin, Los.supercover a b under Supercover
```

`Supercover` is the default for sight and for shell travel — start there, and reach for `Thin` only when
your movement rules earn it.

## Line-of-sight — and why the symmetric one is the one you want

`Los.lineOfSight isTransparent a b` returns `true` when no tile **strictly between** `a` and `b` fails the
predicate. `isTransparent` is a `Cell -> bool` map — the same shape as `Pathfinding`'s `isWalkable`, so
**one map drives both routing and sight**. The endpoints are **never** tested (you may look FROM and AT an
opaque tile); `a = b` is `true`. It walks under `Supercover`, the default that leaks nothing.

```fsharp
open FS.GG.Game.Core

let wall = { Col = 3; Row = 1 }
let isTransparent (c: Cell) = c <> wall          // your fog/wall map (false = opaque)

Los.lineOfSight isTransparent a b                // false — the wall blocks sight
```

To choose the walk, use `Los.lineOfSightBy mode isTransparent a b`. **Prefer it — and prefer its
symmetry — for any combat check.** `Thin` is **not commutative on its own**: its error-tie break is
resolved in a fixed direction, so `line a b` is not `List.rev (line b a)` and the two argument orders
visit *different* intermediate cells. A wall in a sometimes-visited cell would then block one direction and
not the other — a unit could shoot one that cannot shoot back. `lineOfSightBy` fixes this **in every
mode**: it traces the *canonical* ordered pair (`min(a,b) → max(a,b)` under `Cell`'s structural
`(Col,Row)` order), so both orders test one identical sequence.

```fsharp
// Symmetric by construction, even under Thin:
Los.lineOfSightBy Thin isTransparent a b = Los.lineOfSightBy Thin isTransparent b a   // always true
```

`Los.lineOfSight` is exactly `Los.lineOfSightBy Supercover`.

## Determinism is by design, not float lerp

The walk is **integer Bresenham** — an integer error accumulator, deltas taken in `int64` so the
arithmetic is total across the whole coordinate domain (an `int` subtraction would wrap, and the doubled
error term would overflow negative near `2^30` and loop forever). Identical endpoints yield a
byte-identical cell list across runs, platforms, and architectures. The Red Blob Games article presents a
`lerp`-and-`round` form first for clarity; that last-bit rounding differs across runtimes and can flip a
cell — keep the integer form for anything replayed.

## Common pitfalls

- **Using the thin `line` for sight.** A `Thin` line cuts corners and leaks through a diagonal wall join —
  the design doc's "#1 LOS bug". Use `supercover`/`lineOfSight` unless your movement rules genuinely allow
  moving through that join.
- **Calling `lineOfSightBy Thin` and expecting symmetry from `line`.** The *query* is symmetric because it
  canonicalises the pair; the raw `line`/`trace Thin` walk is **not**. Never hand-roll a directional sight
  check over `Thin`.
- **Testing the endpoints in LOS.** `lineOfSight` deliberately never tests `a`/`b`, so you can see from and
  at an opaque tile. Preserve that convention or a wall you stand on blocks all sight.
- **Re-rolling a grid coordinate, or sampling by float `lerp`.** Reuse `Cell` (not a look-alike
  `(row, col)`, not the float `Point`), and keep the integer walk — a `lerp`+`round` sample's last bit can
  flip a cell across runtimes and break replay.
- **Building an FOV by running `lineOfSight` to every cell in a radius.** That is the slow path *and* the
  buggy path (asymmetric, artifact-ridden vision). Use the `Fov` shadowcaster taught by [[fs-gg-visibility]].

## Build Commands

Run `./fake.sh build -t Dev` then `./fake.sh build -t Verify` in this product.

## Test Commands

Run `./fake.sh build -t Test` to exercise product-owned line-drawing examples (assert the line connects
its endpoints and each step is adjacent, a blocked tile hides a target and removing it restores sight,
`lineOfSightBy` is symmetric in both modes, determinism replays, and degenerate/all-octant totality).

## Evidence

Record line-drawing evidence (connectivity/endpoint cases, LOS blocked/clear, symmetry across argument
order, determinism replays) under this product's `readiness/` paths. Do not copy framework readiness
reports into the product.

## Package Boundary

`LineMode` and the `Los` module live in `FS.GG.Game.Core` (referenced only on the `game`/`sample-pack`
profiles), alongside the `Cell` and `Pathfinding` predicate convention they share. `FS.GG.Game.Core` is
the BCL-only bottom layer — it depends on nothing and pulls in no viewer, layout, or widget machinery.
Keep rendering the tiles a line produces in [[fs-gg-rendering:fs-gg-scene]] and host wiring in
[[fs-gg-rendering:fs-gg-skiaviewer]].

## Generated Product

Build endpoints from your world each fixed step, call `Los.line`/`supercover`/`trace`/`lineOfSight` from
your `update`/`view`, and hand the result to your `View` — draw the tiles, gate a shot, or reveal fog.
Pair it with [[fs-gg-collision]] and [[fs-gg-visibility]] for a full geometry pass, and [[fs-gg-game-core]]
for the routing the line complements (route a path, then draw or step along a line).

## Persistent problems

When a problem outlasts reasonable in-repo attempts, extensive external research is **mandatory** —
consult **official online docs first** (the F#/.NET docs and the Red Blob Games reference), then community
sources. If your product uses Spec Kit, record findings and resolving links under the feature's
`specs/<feature>/feedback/`; otherwise record them in this skill's **Sources** line and any product-local
`docs/`. Offline, the mandate degrades to recording "research blocked — <why>" rather than hard-failing.

## Related

- [[fs-gg-visibility]] — the *continuous* (float `Point`, angular-sweep) line-of-sight sibling, and the
  discrete `Fov` shadowcaster; this skill is the point-to-point grid counterpart.
- [[fs-gg-grids]] — names the `Edge` (the wall *between* two tiles) a sight line is blocked by, and maps
  a continuous position back to the `Cell` a line runs over.
- [[fs-gg-game-core]] — the fixed step, the seeded `Rng`, and the `Cell` **pathfinding** the line
  complements.
- [[fs-gg-collision]] — the per-frame geometry pass (detection + response) over the shared vocabulary.
- [[fs-gg-ballistics]] — shell travel that traces its path over these same cells.
- [[fs-gg-rendering:fs-gg-scene]] — owns the float `Point`/`Rect`; renders the tiles a line produces.
- [[fs-gg-rendering:fs-gg-skiaviewer]] — drives the fixed-step loop from the host window.

## Sources / links

- Red Blob Games, "Line Drawing on a Grid": https://www.redblobgames.com/grids/line-drawing/
- F#/.NET docs: https://learn.microsoft.com/en-us/dotnet/fsharp/
- Bresenham's line algorithm background: https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm
