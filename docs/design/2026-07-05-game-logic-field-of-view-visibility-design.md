# Game logic skill — Field of view, visibility & fog of war

> **Provenance (relocated 2026-07-06, ADR-0022 open-decision #3).** This game-logic library design was authored **2026-07-05 in FS-GG/.github** — *before* the FS.GG.Game extraction — and relocated here to live with the code it designs. Where the original homed these primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, that home is now **`FS.GG.Game.Core`** — the BCL-only bottom layer; ADR-0022 P0–P5 moved `Rng`/`FixedStep`/`Pathfinding`/`SpatialGrid` out of `FS.GG.UI.Canvas` and the `Geometry` collision module out of `FS.GG.UI.Scene`. Direct surface references below were updated to `FS.GG.Game.Core.*`; the surrounding design narrative is preserved as the dated record.

- **Date:** 2026-07-05
- **Owner:** `.github` (cross-repo coordination); **implementation home:** `FS.GG.Game` (FS.GG.Game.Core)
- **Status:** Design proposal (pre-ADR). Deepens an existing thin surface into a comprehensive skill.
- **Scope:** The **field-of-view (FOV) / visibility / fog-of-war** game-logic skill — the
  one-origin-to-many problem ("given an origin and opaque cells, which *set* of cells is
  visible within a radius?") plus the fog-of-war state on top. What the `visibility` fragment
  should grow into, in **docs** and **source code**. Distinct from **point-to-point line of
  sight** (sibling doc).
- **Language/target:** F# on `net10.0`, functional-first, **byte-deterministic**, integer-grid,
  immutable, Elmish/MVU-friendly.
- **Sibling designs (this batch):** [grids](2026-07-05-game-logic-grids-spatial-partitioning-design.md) ·
  [collision](2026-07-05-game-logic-collision-detection-design.md) ·
  [line of sight](2026-07-05-game-logic-line-of-sight-design.md) ·
  [pathfinding](2026-07-05-game-logic-pathfinding-navigation-design.md).
- **Grounding:** roguelike-style exploration in the game specs; the tactics spec's LoS/fog
  interplay; fits `fs-gg-game-core`'s deterministic-sim charter.

---

## 1. FOV vs LOS, and the cross-skill contract

**FOV = one origin → a set of visible cells**, computed once per observer per turn. **LOS =
two known points → one boolean** (sibling skill). The docs must open with the boundary, then
state the **cross-skill contract** that ties the two together:

> A well-designed FOV guarantees: *if you can draw an unobstructed line between two floor
> tiles, they are in each other's FOV; if you can't, they aren't.* So **ranged combat (LOS)
> matches vision (FOV)** — no "I can see it but can't shoot it" and vice-versa.

The only algorithm family that delivers this *and* stays deterministic is **exact-integer
symmetric shadowcasting**. That is the recommended default, and the contract is a **tested
property** (§8), not an aspiration.

**Hard rule:** never compute FOV by running LOS to every cell in radius — it is O(r³)-ish and
produces asymmetric, artifact-ridden vision (the historical roguelike mistake). Document it
as a fallback only.

---

## 2. FOV algorithm families

| Algorithm | Symmetric | Artifacts (pillars/corners) | Expansive walls | Speed | Difficulty |
|---|---|---|---|---|---|
| Bresenham raycast ("BASIC") | no | holes/pillars; permissive | no | fast | easy |
| Recursive shadowcasting (Bergström) | no (mild) | mild asymmetry; blind corners | partial | **fastest** (esp. indoors) | medium |
| **Symmetric shadowcasting (Ford/Milazzo)** ✅ default | **yes** | **none** (rational math) | **yes** | fast (rational slower than float) | medium-hard |
| Milazzo "A Better FOV" | yes (full/partial) | none | yes | ~53% of shadowcast | hard |
| Permissive / Precise-Permissive (PPFOV) | yes | over-permissive; no pillar shadows | yes | slower, O(r²) | hard |
| MRPAS (restrictive) | no | tight/natural shape | — | very fast | medium |
| Digital FOV | yes | few | — | slowest | hard |
| Red Blob 2D visibility (continuous) | yes (geometric) | none (polygonal) | n/a (not grid) | fast (few walls) | medium-hard |

**Recommendation:** default to **exact-integer symmetric shadowcasting** — the only family
that is simultaneously symmetric, artifact-free, expansive-walled, determinism-friendly
(rational slope math), and provably consistent with Bresenham LOS. Offer **plain
shadowcasting** (speed) and **MRPAS** (tight cosmetic cone) as documented alternates.

### 2.1 Why symmetric shadowcasting
It reframes the classic 8-octant recursion into **4 quadrants × row scans**, with slopes as
**exact rational numbers** `Fraction(2·col−1, 2·depth)` (zero floating-point error). Floors
are modeled as **center points**, walls as **inscribed diamonds**: a floor is revealed only
if its center lies within `[depth·startSlope, depth·endSlope]` (the `is_symmetric` test →
guaranteed floor-to-floor symmetry), while walls use a relaxed edge-slope rule so a convex
room reveals *all* its wall tiles (**expansive walls**). This is exactly what plain
center-to-center symmetry destroys — Ford/Milazzo's contribution is getting symmetry *and*
expansive walls together. An iterative (queue/stack) variant avoids deep recursion.

### 2.2 The classic recursive-shadowcasting skeleton (for the alternates)
Divide the disc into 8 octants (each an `(xx,xy,yx,yy)` coordinate transform of one scanned
octant); scan rows nearest-to-farthest; carry a running `[startSlope, endSlope]` window; on
hitting an opaque cell, **recurse one row deeper** with a narrowed `endSlope` (near edge of
the blocker) and continue the row past it with a new `startSlope` (far edge). Cells fully in
shadow are never visited — which is why it is fast in corridors. **For determinism, replace
the `±0.5` corner offsets with the exact-integer cross-multiplied slope form.**

---

## 3. Radius, shape, direction

The radius test just changes the per-cell distance check on the same scan:

- **Euclidean** `row²+col² ≤ r²` (integer-safe squared form) → **circular** FOV (most natural).
- **Chebyshev** `max(|dx|,|dy|)` → **square** FOV.
- **Manhattan** `|dx|+|dy|` → **diamond** FOV.

**Circular clipping** is orthogonal to the scan (run to `radius`, drop cells failing the
squared-distance test). **Light levels / falloff**: output an integer fixed-point intensity
(`1 − dist/radius` or a LUT) for torch dimming. **Directional / cone FOV** (guards,
flashlights): intersect the full FOV with an angular wedge `[facing ± halfAngle]`, or restrict
scanned quadrants + a per-cell angle test — a pre/post-filter on the same core, not a new
algorithm.

---

## 4. Blocking model

- **Opacity and walkability are independent bits** — a cell may be transparent+unwalkable
  (chasm/lava) or opaque+walkable (secret door). FOV consults **opacity only**; movement
  consults walkability. Model as two predicates/bitsets, never one "wall" flag.
- **Expansive walls:** a visible wall is marked visible (you see its near face) even though it
  blocks everything beyond — its shadow starts at its far edge.
- **Corner/wall model** is the central design decision (point-model vs diamond/beveled). It
  determines every edge case (blind corners, corner-peeking); **document the chosen rule as a
  contract.** Symmetric shadowcasting's diamond-wall rule is the clean default.
- **Blind corners:** a good FOV lets you see ≥2 tiles around a corner so a monster can't sit
  one step out of view; naïve shadowcasting fails this, symmetric fixes it.

---

## 5. Fog of war (built on FOV)

**Three-state per cell** (the universal roguelike model):

| State | Meaning | Render | Entities |
|---|---|---|---|
| **Unseen** | never in FOV | black | none |
| **Remembered** | was in FOV, not now | dimmed | remembered *terrain* only (mobs may have moved) |
| **Visible** | in current FOV | full | live |

Mechanics: each turn compute the FOV set → cells in it become `Visible` and set `explored=true`
(the `false→true` transition is the "reveal" event, useful for triggers/scoring); cells that
were `Visible` and aren't now drop to `Remembered`. Optional **last-seen memory** snapshots the
glyph/entity per remembered cell. **Multi-agent shared vision** keeps a *count* per cell (union
of friendly FOVs) so adding/removing an observer is incremental. **Performance tricks:**
**FOV-ID stamping** (store a per-cell integer "generation"; a cell is visible iff its stamp ==
current id — advancing the id invalidates the whole map in O(1), no per-frame clear);
**dirty-region** recompute only when an observer moved or the map inside its radius changed.

Functional state layers: `explored : Set<Cell>` (monotonically grows), `visible : Set<Cell>`
(recomputed each turn), `memory : Map<Cell, Remembered>`.

---

## 6. Performance

Octant/quadrant symmetry (implement one octant; the other 7 are sign-flip/axis-swap
transforms — 8× less code); shadowcasting **only visits lit + blocking cells**, skipping whole
shadowed regions (sublinear indoors); radius bounding + squared-distance circular clip;
full recompute per move is cheap for radius ≈ 8–20 (only reach for dirty-region / composite
counts at Cogmind scale); rational slope math is slower than float but is what buys byte-identical
output — the right trade here.

---

## 7. Determinism & functional design (F#)

| Rule | Why |
|---|---|
| **Integer / rational slope math, no floats** (`Fraction(2·col−1, 2·depth)` or cross-multiplied integer slope compares) | float rounding differs by CPU/JIT → breaks determinism *and* symmetry |
| **Pure signature** `fov : opaque:(Cell→bool) → origin:Cell → radius:int → Set<Cell>` | no ambient state; memoizable Elmish selector |
| **Stable ordering** — `Set<Cell>` over a total cell order; never hash-set iteration | deterministic enumeration |
| **Persistent maps with structural sharing** (`Set`/`Map`) | `explored` grows by sharing; each turn's `visible` is cheap; MVU-friendly |
| **Bresenham-agreement as a property** (§8), by choosing symmetric shadowcasting | FOV and the LOS sibling never disagree |

Elmish fit: FOV is a **pure projection of model state** (`map + observerPos → visibleSet`) —
a memoized selector; fog state (`explored`, `memory`) lives in the model and updates via pure
`update` messages (`Moved → recompute`).

---

## 8. Testing

- **Symmetry property (must-have):** ∀ random map, all pairs within radius,
  `B ∈ fov(A) ⇔ A ∈ fov(B)`.
- **FOV↔LOS agreement:** `B ∈ fov(A) ⇔ los(A,B)` — locks the cross-skill contract.
- **Golden visibility maps:** known dungeons (pillar field, convex room, corridor, corner)
  with expected visible sets; doubles as the **cross-platform determinism** test (hash + diff
  on Linux/Windows/macOS CI).
- **Artifact regressions:** **no expanding pillars** (a single pillar casts a clean sector, not
  a widening lattice of holes), **expansive walls** (convex room → all walls visible), **no
  blind corners** (≥2 cells around a corner), **no diagonal-wall leakage**.
- **Radius correctness:** cell at exactly `radius` visible, `radius+1` not; circle/square/diamond
  shape snapshots.
- **Octant-seam integrity:** cells on 45°/cardinal seams neither missed nor double-counted
  (half-open ownership per octant).

---

## 9. API surface (what "comprehensive" means)

```fsharp
// Core
fov            : opaque:(Cell -> bool) -> origin:Cell -> radius:int -> Set<Cell>
fovWithLevels  : ... -> Map<Cell, LightLevel>                 // falloff / torch dimming
isVisible      : FovResult -> Cell -> bool
fovCone        : ... -> facing:Dir -> halfAngle:int -> Set<Cell>
type DistanceMetric = Euclidean | Chebyshev | Manhattan       // radius shape

// Fog of war (state machine over FOV)
type Visibility = Unseen | Remembered | Visible
type Fog = { explored: Set<Cell>; visible: Set<Cell>; memory: Map<Cell, Remembered> }
updateFog      : Fog -> newVisible:Set<Cell> -> Fog           // pure transition
compositeFov   : observers:(Cell * int) list -> ... -> Map<Cell, int>   // multi-agent count
```

Config knobs (documented defaults): algorithm (`SymmetricShadowcast` default, plus
`Shadowcast`, `MRPAS`, `Permissive`), corner/wall model, distance metric, radius, cone params.

---

## 10. Pitfalls to design out

| Pitfall | Mitigation |
|---|---|
| Asymmetric vision (the fairness bug) | symmetric shadowcasting + symmetry property test |
| Pillar / expanding-pillar artifacts | rational (non-float) slope math; edge-rounding care |
| Blind corners | symmetric/diamond-wall model; ≥2-cells-around-corner test |
| Double-count across octants | consistent half-open ownership per octant |
| Radius rounding (jagged circles / off-by-one) | integer squared-distance |
| Float nondeterminism across hardware | rational/integer slopes only |
| Opaque ≠ walkable confusion | two independent predicates/bitsets |
| Building FOV from per-cell LOS | use shadowcasting |

---

## 11. Effort & placement

- **Home:** `FS.GG.Game.Core` (a `FS.GG.Game.Core.Fov` module + a `Fog` module beside
  `LineOfSight`/`Pathfinding`); depends *down* only.
- **Phasing:** P0 exact-integer symmetric shadowcasting core + circular clip + tests (M);
  P1 fog-of-war three-state + last-seen memory (S); P2 light levels + directional cone (S–M);
  P3 multi-agent composite FOV + FOV-ID stamping / dirty regions for scale (M).
- **Contract note:** growing the `visibility` skill body is a Rendering library-surface bump the
  `.github` skill-registry reconciles (`registry = manifest = bytes`); pairs naturally with the
  LOS skill (shared cell/opacity types).

---

## 12. Sources

[Björn Bergström — FOV using recursive shadowcasting (RogueBasin)](https://www.roguebasin.com/index.php/FOV_using_recursive_shadowcasting) ·
[RogueBasin — recursive shadowcasting improved](https://www.roguebasin.com/index.php/FOV_using_recursive_shadowcasting_-_improved) ·
[Albert Ford — Symmetric Shadowcasting](https://www.albertford.com/shadowcasting/) ·
[symmetric-shadowcasting reference impl (CC0)](https://github.com/370417/symmetric-shadowcasting) ·
[Adam Milazzo — Roguelike Vision Algorithms / "A Better FOV"](http://www.adammil.net/blog/v125_Roguelike_Vision_Algorithms.html) ·
[RogueBasin — comparative study of FOV algorithms](https://www.roguebasin.com/index.php/Comparative_study_of_field_of_view_algorithms_for_2D_grid_based_worlds) ·
[RogueBasin — Field of Vision](https://www.roguebasin.com/index.php/Field_of_Vision) ·
[RogueBasin — Permissive FOV](https://www.roguebasin.com/index.php/Permissive_Field_of_View) ·
[RogueBasin — Precise Permissive FOV](https://www.roguebasin.com/index.php/Precise_Permissive_Field_of_View) ·
[MRPAS (Marczuk) — Godot port](https://github.com/matt-kimball/godot-mrpas) ·
[Red Blob Games — 2D Visibility](https://www.redblobgames.com/articles/visibility/) ·
[Bob Nystrom — What the Hero Sees: FOV for Roguelikes](https://journal.stuffwithstuff.com/2015/09/07/what-the-hero-sees/) ·
[Grid Sage / Cogmind — Fog of War](https://www.gridsagegames.com/blog/2013/11/fog-war/) ·
[Roguelike tutorial — FOV & exploration (three-state fog)](https://tomassedovic.github.io/roguelike-tutorial/part-4-fov-exploration.html) ·
[Hexworks — vision and fog of war](https://hexworks.org/posts/tutorials/2019/04/27/how-to-make-a-roguelike-vision-and-fog-of-war.html) ·
[UWO thesis — New Algorithms for Computing FOV over 2D Grids](https://ir.lib.uwo.ca/cgi/viewcontent.cgi?article=8883&context=etd).
