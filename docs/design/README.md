# FS.GG.Game — game-logic library design corpus

Design proposals for the game-logic subsystem, relocated here from `FS-GG/.github`
per **[ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md)**
open-decision #3 (the designs now live with the code they describe — `FS.GG.Game.Core`).

Authored **2026-07-05** in `.github`, *before* the extraction — where the text homes
primitives in `FS.GG.UI.Canvas` / `FS.GG.UI.Scene` / `FS.GG.Rendering`, read that as
**`FS.GG.Game.Core`** (each file carries a provenance banner). Direct surface references
were updated; the design narrative is preserved as the dated record.

| Design | Primitive(s) |
|---|---|
| [Skills overview](2026-07-05-game-logic-skills-design-overview.md) | the corpus map — determinism-as-tested-property, pure-function discipline |
| [Collision detection & resolution](2026-07-05-game-logic-collision-detection-design.md) | `Geometry` (AABB) + `SpatialGrid` broad-phase |
| [Pathfinding & navigation](2026-07-05-game-logic-pathfinding-navigation-design.md) | `Pathfinding` (A*/BFS) |
| [Grids & spatial partitioning](2026-07-05-game-logic-grids-spatial-partitioning-design.md) | `SpatialGrid` + grid-parts (`Cell`) |
| [Field of view & visibility](2026-07-05-game-logic-field-of-view-visibility-design.md) | FOV / fog (proposed) |
| [Line of sight](2026-07-05-game-logic-line-of-sight-design.md) | LOS over a transparency map (proposed) |

The corresponding **TestSpecs** stay in `FS-GG/.github` (cross-repo tests reference them there).
