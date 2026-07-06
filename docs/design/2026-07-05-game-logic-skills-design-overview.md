# Game logic skills — comprehensive design overview

> **Provenance (relocated 2026-07-06, ADR-0022 open-decision #3).** This game-logic library design was authored **2026-07-05 in FS-GG/.github** — *before* the FS.GG.Game extraction — and relocated here to live with the code it designs. Where the original homed these primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, that home is now **`FS.GG.Game.Core`** — the BCL-only bottom layer; ADR-0022 P0–P5 moved `Rng`/`FixedStep`/`Pathfinding`/`SpatialGrid` out of `FS.GG.UI.Canvas` and the `Geometry` collision module out of `FS.GG.UI.Scene`. Direct surface references below were updated to `FS.GG.Game.Core.*`; the surrounding design narrative is preserved as the dated record.

- **Date:** 2026-07-05
- **Owner:** `.github` (cross-repo coordination); **implementation home:** `FS.GG.Game` (FS.GG.Game.Core)
- **Status:** Design proposal (pre-ADR). Index + shared design contract for a batch of five
  game-logic skill deepenings.
- **Why:** The game-logic primitives shipped recently into Rendering (`FS.GG.Game.Core.SpatialGrid`,
  `FS.GG.Game.Core.Pathfinding`, `FS.GG.Game.Core.Geometry`, and the #132 collision / grids /
  line-drawing / visibility fragments; skill `fs-gg-game-core`) are **thin primitives**. The
  game TestSpecs already lean on far richer semantics — hex grids, symmetric LOS, cover, FOV,
  Dijkstra reachability, knockback. Each of the five documents below specifies what the
  corresponding **comprehensive** skill should contain, in **docs and source code**, grounded
  in intensive per-topic research.

---

## The five designs

| # | Skill | Grows from | Document |
|---|---|---|---|
| 1 | **Grids & spatial partitioning** | `Canvas.SpatialGrid` + grids fragment | [design](2026-07-05-game-logic-grids-spatial-partitioning-design.md) |
| 2 | **Collision detection & resolution** | `Scene.Geometry` (AABB) + collision fragment | [design](2026-07-05-game-logic-collision-detection-design.md) |
| 3 | **Line of sight (LOS)** | line-drawing / visibility fragment | [design](2026-07-05-game-logic-line-of-sight-design.md) |
| 4 | **Field of view / visibility & fog of war** | visibility fragment | [design](2026-07-05-game-logic-field-of-view-visibility-design.md) |
| 5 | **Pathfinding & navigation** | `Canvas.Pathfinding` (A*/BFS) | [design](2026-07-05-game-logic-pathfinding-navigation-design.md) |

---

## Shared design contract (applies to all five)

These invariants are the reason the batch is one coherent piece of work, not five unrelated docs:

1. **Integer logic, float presentation.** All game-logic math is integer or Q-format
   fixed-point; every irrational (`√2` diagonals, `√3` hex layout, Euclidean distance, FOV
   slopes) is either an integer-scaled surrogate or pushed to the Elmish **presentation edge**.
   This is what makes byte-determinism achievable and mirrors the framework's
   `Json-is-contract` / `Rich-is-projection` rule.
2. **Byte-identical determinism is a tested property, not a hope.** Same input → identical
   output across runs and OS/arch, proven by golden files in a cross-platform CI matrix. The
   recurring enemy is **ordering nondeterminism** — `HashSet`/`Dictionary` iteration order,
   unstable priority-queue tie-breaks, ambiguous diagonal/corner cases. Each doc names its
   fix (ordered `Map`/`Set`, a strict total-order key, a single documented tie-break rule).
3. **Pure functions over oracles.** Every skill's core is a pure function of an abstract
   predicate (`walkable`, `blocked`/`opaque`, `cost`) — never a mutable map. This makes them
   memoizable Elmish selectors and keeps game policy (what blocks, what costs) out of the
   engine.
4. **Detection/query separate from response/policy.** Collision detection returns manifests as
   values; resolution is a separate transform. LOS returns a predicate; cover/unit-blocking is
   game policy. FOV returns a set; fog-of-war state layers on top.
5. **Property-based + golden testing (FsCheck).** Round-trips (grid coordinate conversions),
   symmetry (LOS/FOV/collision normals), optimality (A* cost == Dijkstra cost), no-tunnel /
   no-corner-cut invariants, and cross-platform determinism hashes.

## How the skills compose

They are not independent — the batch shares one substrate:

```
                    ┌───────────────────────────┐
                    │ 1. Grids & spatial part.   │  coordinate algebra + broad-phase index
                    └─────────────┬─────────────┘
        ┌──────────────┬──────────┼───────────────┬──────────────┐
        ▼              ▼          ▼                ▼              ▼
  2. Collision    3. LOS      4. FOV          5. Pathfinding
  (broad phase =  (grid line  (shadowcasting;  (neighbours/cost
   grid index;     traversal;  FOV↔LOS          over the grid;
   tile coords)    A&W DDA)    contract) ◄──────┘ smoothing reuses LOS)
```

- **Grids** provide the coordinate systems, storage, and the spatial-hash broad phase that
  **collision** consumes and that **FOV/pathfinding** query for neighbours.
- **LOS ↔ FOV** share a stated contract: symmetric shadowcasting FOV agrees with Bresenham LOS
  (see if-you-can-shoot-it-you-can-see-it) — a cross-skill tested property.
- **Pathfinding** reuses the **LOS** predicate for path smoothing (waypoint reduction), and the
  Amanatides–Woo tile DDA is shared between **LOS**, **collision** (tile raycast), and grid queries.

## Placement & process

- **Home:** all five live in `FS.GG.Rendering` under `FS.GG.Game.Core.*` / `FS.GG.UI.Scene.*`,
  depend *down* only, and never touch Governance.
- **Contract discipline:** each deepening is an **additive Rendering library-surface bump** that
  the `.github` **skill-registry** reconciles (`registry = manifest = bytes`), the same
  discipline as the 13→16 `fs-gg-audio`/`fs-gg-persistence`/`fs-gg-model-swap` catch-up. The
  two determinism-critical choices (pathfinding's ordering-key tie-break; collision's
  fixed-point core) each warrant a **Rendering-local ADR**.
- **Sequencing:** these seed a Rendering epic ("Comprehensive game-logic skills") with the
  per-doc phasing (P0…) as children, tracked on the Coordination board.

## Research basis

Each document synthesizes intensive per-topic web research from the authoritative sources —
Red Blob Games / Amit Patel (grids, hex, A*, visibility, flow fields), Bob Nystrom's *Game
Programming Patterns* (spatial partition), RogueBasin (FOV, Dijkstra maps, LOS), Adam Milazzo
& Albert Ford (symmetric shadowcasting), Harabor & Grastien (JPS), Christer Ericson (collision
geometry), and Gaffer On Games (fixed timestep / determinism). Full source lists are in each
document.
