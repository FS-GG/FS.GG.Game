# Game logic skill — Line of sight (LOS)

> **Provenance (relocated 2026-07-06, ADR-0022 open-decision #3).** This game-logic library design was authored **2026-07-05 in FS-GG/.github** — *before* the FS.GG.Game extraction — and relocated here to live with the code it designs. Where the original homed these primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, that home is now **`FS.GG.Game.Core`** — the BCL-only bottom layer; ADR-0022 P0–P5 moved `Rng`/`FixedStep`/`Pathfinding`/`SpatialGrid` out of `FS.GG.UI.Canvas` and the `Geometry` collision module out of `FS.GG.UI.Scene`. Direct surface references below were updated to `FS.GG.Game.Core.*`; the surrounding design narrative is preserved as the dated record.

- **Date:** 2026-07-05
- **Owner:** `.github` (cross-repo coordination); **implementation home:** `FS.GG.Game` (FS.GG.Game.Core)
- **Status:** Design proposal (pre-ADR). Deepens an existing thin surface into a comprehensive skill.
- **Scope:** The **point-to-point line-of-sight** game-logic skill — the "can A see/hit B?"
  and "trace the line between two cells" problem — what the `line-drawing` / `visibility`
  fragment should grow into, in **docs** and **source code**. Distinct from **field of
  view** (the one-origin-to-many-set problem; sibling doc).
- **Language/target:** F# on `net10.0`, functional-first, **byte-deterministic**, integer-grid,
  immutable, Elmish/MVU-friendly.
- **Sibling designs (this batch):** [grids](2026-07-05-game-logic-grids-spatial-partitioning-design.md) ·
  [collision](2026-07-05-game-logic-collision-detection-design.md) ·
  [field of view / visibility](2026-07-05-game-logic-field-of-view-visibility-design.md) ·
  [pathfinding](2026-07-05-game-logic-pathfinding-navigation-design.md).
- **Grounding:** `turn-based-tactics` §4.4–4.5 (`Ranged` obeys LoS; Forest/Mountain/Wall block;
  units don't block except `Massive`; melee ignores LoS; cover grading).

---

## 1. The LOS ↔ FOV boundary (state it first)

The single most important framing decision: **LOS and FOV are different problems** and the
skill docs must open by saying so. Conflating them produces both the slow path and the buggy
path.

| | **LOS (this skill)** | **FOV (sibling skill)** |
|---|---|---|
| Question | "Can A see/hit B?" (two known points) | "What can A see?" (one point → set) |
| Output | `bool` / cell path / first hit | set/region of visible cells |
| Cost/query | O(distance) | O(cells in radius) |
| Canonical algorithms | Bresenham, supercover, DDA / Amanatides-Woo, ray march | recursive/symmetric shadowcasting |
| Symmetry | must be **engineered in** (§3) | structural in symmetric shadowcasting |
| Uses | ranged-attack legality, line of fire, cover, projectile paths | rendered/explored map, stealth, fog of war |

**Hard rule for the docs:** *do not build FOV by running LOS to every cell in radius.* It is
slow and produces asymmetric, artifact-ridden vision — exactly the historical roguelike
mistake shadowcasting exists to fix. LOS-to-every-cell is a documented *fallback only*.

The tactics spec is a pure LOS consumer: ranged abilities "obey LoS," terrain blocks, melee
ignores it, and cover is graded along the sightline. That is this skill's job.

---

## 2. Grid line algorithms — "thin" vs "thick" is the whole ballgame

A "line on a grid" means two genuinely different things, and choosing wrong is the #1 LOS bug:

- **Thin line** (Bresenham / lerp-rounded): ~`max(dx,dy)` cells, one per major-axis step. A
  diagonal line **cuts corners** — it passes *between* two diagonally-touching wall cells.
- **Supercover / thick line**: **every** cell the real line's area overlaps (~`dx+dy` cells).
  Cannot leak through a diagonal wall gap.

Neither is universally correct — they give *different answers at corners*, and the game's
own movement rules decide which is right. The skill ships **three integer algorithms** behind
a `LineMode` policy:

| Algorithm | Cells | Arithmetic | Corner leak? | Commutative? | Use for |
|---|---|---|---|---|---|
| **Bresenham** (thin) | ~max(dx,dy) | integer | yes (cuts corners) | no (needs canonicalize) | generous LOS, projectile path |
| **Walk-grid / supercover** (thick) | ~dx+dy | integer | no (except exact corner) | yes | strict "touches" LOS, edge-walls |
| **Amanatides–Woo / DDA** | exact ray supercover | rational tMax | no | yes (endpoints fixed) | ray marching, first-hit, 3D |

Reference forms the skill must ship (all integer / rational — see §6):

- **Bresenham** (all-octant, error-tie generalized): integer add/sub only; documents the
  simultaneous x+y step that produces corner-cutting.
- **Walk-grid / supercover**: step one grid edge at a time via the cross-multiplied
  comparison `(1+2·ix)·ny  ?  (1+2·iy)·nx`; on `decision == 0` (exact corner) either take one
  diagonal step or force-emit both corner cells (a `DiagonalRule` choice).
- **Amanatides–Woo**: per-axis `tMax`/`tDelta`; handle `dir == 0` (`tDelta = ∞`), negative
  direction offset, and the `tMaxX == tMaxY` exact-corner tie explicitly. Use **rational**
  `tMax` (int64 numerator/denominator, compared by cross-multiplication — no division) so it
  is exact and portable.

Wu's antialiased line is **rendering only** — documented as out of scope.

---

## 3. Correctness: symmetry, permissiveness, diagonal gaps

### 3.1 Symmetry (A sees B ⇔ B sees A)
The invariant that makes combat fair — otherwise a unit can hit one that cannot hit back
(the tactics exploit). Naïve Bresenham is asymmetric for **two** independent reasons:
1. **Non-commutative traversal** — endpoint canonicalization + fixed error-tie direction mean
   `trace(A,B)` and `trace(B,A)` visit different intermediate cells; a wall in a
   "sometimes-visited" cell blocks one direction only.
2. **Region-endpoint asymmetry** — even a commutative line isn't enough if the rule is
   "point-to-any-part-of-target"; the sampled sub-region isn't mirror-symmetric under reversal.

**The skill guarantees symmetry by construction, not by accident:** trace the *canonical
ordered pair* (always from `min(A,B)` to `max(A,B)` under a total cell order) so `los(A,B)`
and `los(B,A)` test the identical cell sequence — and **cache on that ordered pair** so
symmetry is free and hot queries are O(1). This is a *tested invariant* (§7), not a hope.

### 3.2 Permissive vs strict
- **Strict**: sightline must pass clear cell-centers only; grazing a blocker fails. Fewer
  visible; pillars actually block.
- **Permissive**: visible if *any* line from any part of A reaches any part of B. Generous;
  peeks around corners.
Most tactics games use strict center-to-center for the *predicate* and sample a few offset
rays for *cover grading*.

### 3.3 Diagonal wall gaps (the signature edge case)
Two walls touching only at a corner: can you see/shoot between them? A thin line leaks; a
thick line does not (except the exact-corner case). There is no universal answer — it must
**match your movement rules** (if units can move between diagonal walls, vision should too).
The skill exposes an explicit **`DiagonalRule`**: `Permissive | BlockIfEitherBlocks |
BlockIfBothBlock`.

### 3.4 Blocking model — cells, corners, or edges
Three models, all named in the docs: **cell opacity** (blocked on entering an opaque cell;
pairs with supercover), **corner/vertex** (diamond/bevel walls; fixes diagonal leaks
precisely), **edge/border walls** (walls on the boundary *between* cells; pairs with
walk-grid; natural for thin interior walls).

---

## 4. Game-layer blocking policy (engine stays agnostic)

The core is a pure predicate over an opacity/cover oracle; all game policy lives in config:

- **Endpoint policy** — test the *open interval* of cells strictly between A and B by default;
  otherwise the viewer's own cell (always "opaque" — it's occupied) or the target's cell
  (a monster blocking the view of itself) give wrong answers. `IncludeEndpoints` is explicit.
- **Partial cover / attenuation** (tactics forest): cells carry a cover value; the trace
  *accumulates* cover instead of hard-stopping — LOS stays "clear" but the shot is graded.
- **Directional cover / elevation** (XCOM model): blocking is a function of
  `(cell, incomingDirection, elevation)`, not just cell.
- **Units block?** Design choice — the tactics spec: units don't block LOS *except* `Massive`
  units. The engine consults a dynamic-occupancy oracle; the policy is the game's.
- **Multi-cell blockers**: the game stamps all of an entity's cells into the oracle.

---

## 5. Performance

Early-out at the **first blocker** (never materialize the whole path — callback /
`firstBlocker` traversal); **range-gate before tracing** (`chebyshev(A,B) > maxRange` reject);
**symmetry cache** on the canonical ordered pair; **precomputation** for static maps
(symmetric pre-computed visibility) trading memory for O(1) queries on hot targeting UI;
**bit-packed opacity rows** for word-at-a-time skips over long clear runs; **allocation-free**
struct enumerator in the hot path.

---

## 6. Determinism & functional design (F#)

| Rule | Why |
|---|---|
| **Integer line tracing** (Bresenham / walk-grid / supercover are pure integer) | zero float ⇒ byte-identical across x86/ARM; the strong reason to prefer them for the predicate |
| **Rational `tMax`** for Amanatides–Woo (cross-multiplied, no division) | exact, portable ray behavior without float drift |
| **One fixed tie-break** on every corner/diagonal case (`e2` double-step, `decision==0`, `tMaxX==tMaxY`, `round(.5)`) — orientation-independent | determines *both* determinism and symmetry |
| **Pure predicate over an oracle** — the only coupling to game state | memoizable Elmish selector; engine never touches a mutable map |
| **Total cell order** for canonicalization | `los(A,B)` traces the same physical sequence regardless of argument order |
| **`[<Struct>]` cells + struct enumerator** | allocation-free hot path, value semantics |

Core signatures:

```fsharp
val trace        : Cell -> Cell -> IReadOnlyList<Cell>            // or struct enumerator
val losClear     : blocked:(Cell -> bool) -> Cell -> Cell -> bool // early-out
val firstBlocker : blocked:(Cell -> bool) -> Cell -> Cell -> Cell voption
val coverAlong   : cover:(Cell -> int)   -> Cell -> Cell -> int
```

---

## 7. Testing

- **Symmetry property test (highest value):** ∀ random map, A, B: `losClear m A B = losClear m B A`.
  Catches the entire asymmetry class.
- **Determinism / golden:** hash the traced cell list for fixed `(A,B)`; assert byte-stable
  across runs and OS/arch in CI (ties into the deterministic-render convention).
- **Golden ASCII maps:** hand-authored `@`-sees-`M`? fixtures.
- **Wall-corner cases:** diagonal-wall gap in all 4 rotations × 2 reflections; assert the
  chosen `DiagonalRule`.
- **Endpoint tests:** target standing in / adjacent to a wall; `A == B` degenerate.
- **Range-limit tests:** at `maxRange`, one beyond, range 0.
- **Monotonicity:** adding a wall on the path can only flip `true→false`, never the reverse.
- **Cross-algorithm oracle:** Bresenham vs lerp-rounded agree on axis-aligned & 45° lines;
  differences flag tie-break drift.

---

## 8. API surface (what "comprehensive" means)

- **Generators** (struct enumerator, endpoint/target-cell variants): `bresenham`, `walkGrid`,
  `supercover`, `amanatidesWoo`.
- **Predicates:** `losClear`, `losClearSymmetric` (canonicalized/cached), `firstBlocker`
  (projectile impact / "wall in the way" UI), `visibleWithin` (range-bounded).
- **Cover / tactics:** `coverAlong`, `losWithCover` → `{ Clear; HardBlock; Cover; Impact }`,
  `directionalCover` (direction + elevation).
- **Config record** (documented defaults): `LineMode` (Thin | Supercover | Ray),
  `DiagonalRule` (Permissive | EitherBlocks | BothBlock), `IncludeEndpoints`, `TargetBlocksSelf`,
  `UnitsBlock`, `MaxRange`, `RayFan/Tolerance`.
- **Precompute:** `precompute : Map -> VisibilityIndex` + `VisibilityIndex.los`.

---

## 9. Pitfalls to design out

| Pitfall | Mitigation |
|---|---|
| Asymmetric LOS (non-commutative + region-endpoint) | canonicalized/cached symmetric trace |
| Diagonal wall leaks | supercover or explicit `DiagonalRule` matched to movement rules |
| Endpoint off-by-one (viewer/target self-block) | test the open interval; explicit `IncludeEndpoints` |
| Rounding-tie nondeterminism (`round(.5)`, error-tie) | fix the tie rule; integer/rational arithmetic |
| Float drift in ray-march | Amanatides–Woo with rational `tMax`, or integer traversal |
| Exact-45° corner undefined | pin a symmetric tie-break |
| Building FOV from per-cell LOS | use shadowcasting (FOV sibling) |
| Target occupancy blocks its own view | exclude target (and viewer) from opacity |
| Unbounded traces | range-gate before tracing |
| Conflating line-of-sight with line-of-fire | same trace, different endpoint/cover policy — keep separate |

---

## 10. Effort & placement

- **Home:** `FS.GG.Game.Core` (a `FS.GG.Game.Core.LineOfSight` module beside `Pathfinding`);
  depends *down* only.
- **Phasing:** P0 integer Bresenham + walk-grid/supercover + `losClear`/`firstBlocker` (S);
  P1 symmetric canonicalization + cache + `DiagonalRule` + tests (S–M); P2 cover/directional/
  elevation for tactics (M); P3 Amanatides–Woo rational ray + precompute index (M).
- **Contract note:** growing the `visibility`/`line-drawing` skill body is a Rendering
  library-surface bump the `.github` skill-registry reconciles (`registry = manifest = bytes`).

---

## 11. Sources

[Red Blob Games — Line drawing on a grid](https://www.redblobgames.com/grids/line-drawing/) ·
[Red Blob Games — 2D Visibility](https://www.redblobgames.com/articles/visibility/) ·
[Amit Patel — notes on line drawing on grid maps](https://simblob.blogspot.com/2014/12/line-drawing-on-grid-maps.html) ·
[Sam Driver — Bresenham vs raycasting for vision](https://samdriver.xyz/article/line-of-sight-on-grid) ·
[Adam Milazzo — Roguelike Vision Algorithms](https://www.adammil.net/blog/v125_Roguelike_Vision_Algorithms.html) ·
[Albert Ford — Symmetric Shadowcasting](https://www.albertford.com/shadowcasting/) ·
[Deepnight — Bresenham magic: raycasting, LOS, pathfinding](https://deepnight.net/tutorial/bresenham-magic-raycasting-line-of-sight-pathfinding/) ·
[Amanatides & Woo — Fast Voxel Traversal (PDF)](http://www.cse.yorku.ca/~amana/research/grid.pdf) ·
[MΛX — Amanatides & Woo walkthrough](https://m4xc.dev/articles/amanatides-and-woo/) ·
[cgyurgyik — fast voxel traversal overview](https://github.com/cgyurgyik/fast-voxel-traversal-algorithm/blob/master/overview/FastVoxelTraversalOverview.md) ·
[Joel Schumacher — ray casting in 2D grids](https://joelschumacher.de/posts/ray-casting-in-2d-grids) ·
[Bresenham's line algorithm (Wikipedia)](https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm) ·
[Supercover of straight lines (ResearchGate)](https://www.researchgate.net/publication/225212869_Supercover_of_straight_lines_planes_and_triangles) ·
[denismr — Symmetric Pre-Computed Visibility Tries](https://github.com/denismr/SymmetricPCVT) ·
[RogueBasin — Line of sight](https://www.roguebasin.com/index.php/Line_of_sight) ·
[UFOpaedia — Cover (Long War)](https://www.ufopaedia.org/index.php/Cover_(Long_War)).
