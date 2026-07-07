# FS.GG.Game — game-skill design corpus

Design proposals for the FS.GG.Game skill subsystem, relocated here from `FS-GG/.github/docs/reports`
per **[ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md)**
(open-decision #3 — the designs now live with the code they describe).

Authored **2026-07-05** in `.github`, *before* the extraction. For the **game-logic** designs, where
the text homes primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, read that as
**`FS.GG.Game.Core`** (each file carries a provenance banner; direct surface references were updated).
The **audio** design is the exception — its `fs-gg-audio` skill is FS.GG.Game-owned, but the `Audio`
request surface stayed in `FS.GG.UI.Canvas`, so its `Canvas.Audio` references remain accurate.

| Design | Primitive(s) / surface |
|---|---|
| [Skills overview](2026-07-05-game-logic-skills-design-overview.md) | the corpus map — determinism-as-tested-property, pure-function discipline |
| [Collision detection & resolution](2026-07-05-game-logic-collision-detection-design.md) | `Geometry` (AABB) + `SpatialGrid` broad-phase → `FS.GG.Game.Core` |
| [Pathfinding & navigation](2026-07-05-game-logic-pathfinding-navigation-design.md) | `Pathfinding` (A*/BFS) → `FS.GG.Game.Core` |
| [Grids & spatial partitioning](2026-07-05-game-logic-grids-spatial-partitioning-design.md) | `SpatialGrid` + grid-parts (`Cell`) → `FS.GG.Game.Core` |
| [Field of view & visibility](2026-07-05-game-logic-field-of-view-visibility-design.md) | FOV / fog (proposed) → `FS.GG.Game.Core` |
| [Line of sight](2026-07-05-game-logic-line-of-sight-design.md) | LOS over a transparency map (proposed) → `FS.GG.Game.Core` |
| [Game audio library](2026-07-05-game-audio-library-architecture.md) | `fs-gg-audio` skill (game-owned); `Audio` surface stays in `FS.GG.UI.Canvas` |

The corresponding **TestSpecs** stay in `FS-GG/.github` (cross-repo tests reference them there).
The StarCraft2 unit-symbology design stays in `.github` too — it designs the `fs-gg-symbology`
skill, which is owned by FS.GG.Rendering, not FS.GG.Game.

## Cross-cutting design (authored here, post-extraction)

Not part of the relocated 2026-07-05 corpus above — these define the *kind of thing* the corpus
content ships as, and its build plan. Both are pre-ADR, pointed at org-level **ADR-0023**.

| Design | Concern |
|---|---|
| [Capability-capsule product type](2026-07-07-capability-capsule-product-type.md) | names/formalizes the artifact FS.GG.Game ships to an agent + SDD consumer — three faces (rationale/contract/constraint), tiering, schema |
| [Game-capsule contents & architectures](2026-07-07-game-capsule-contents-and-architectures.md) | the build plan — capability × architecture matrix, the four sim strategies, per-fit samples, governance checks |
