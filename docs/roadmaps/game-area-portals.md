# GAME-PORTAL-01 — Area topology and rigid portal traversal

Status: proposed extension, 2026-10-04. Game owns topology and traversal; the Box2D
adapter owner implements local-world transfers; Rendering owns portal presentation.
fdev integrates their joins. Parent: [GAME-BOX2D-01](game-box2d-physics.md#area-topology-and-portals).
Implementation, package support and consumer adoption remain open.

## Local geometry, arbitrary connections

Each area has ordinary local 2D geometry. Directed connections between openings form
an arbitrary graph: looping corridors, impossible room arrangements and several entrances
to one area need no globally consistent room placement. Stable area, portal and game
entity IDs belong to Game's domain, independently of backend-specific body IDs.

Keep the immutable topology, rigid mappings and crossing decisions above local physics,
as BCL-only Game.Core functionality. The optional Box2D adapter owns one physics world
per active area and executes transfer decisions. Other physics backends can adopt the
topology later without importing Box2D or introducing a universal physics abstraction.

Box2D documents [independent, non-interacting worlds](https://box2d.org/documentation/md_simulation.html).
Its local solver cannot resolve a collision or joint across areas. Box2D.NET's
[body implementation](https://github.com/ikpil/Box2D.NET/blob/154bacd0fddc0e72e484d3061cdc76aa2d842994/src/Box2D.NET/B2Bodies.cs)
provides world-bound body creation/removal and local transform/velocity operations;
Game must own transfer and entity identity. Verify exact APIs against the selected
published dependency before implementation.

## First transfer contract

Select translation and rotation only, with whole-body transfers at fixed-tick boundaries.
Mirrors, scale changes and partial-body continuity are later contracts. A directed portal
declares source/destination area and opening, crossing direction and a finite rigid map.
Declared two-way pairs have inverse maps; one-way edges remain valid.

For rotation `R`, translation `t` and rotation angle `theta`, transfer uses
`position' = R position + t`, `velocity' = R velocity` and
`orientation' = orientation + theta`; angular velocity is unchanged. Shapes and physical
parameters keep their scale and mass. Destination gravity and subsequent contacts follow
that area's configuration. Declare supported body/fixture descriptors, material, filters,
damping and sleep-state handling explicitly; do not silently drop state during recreation.

The first implementation follows this order:

1. Step every active local world once, in a stable schedule, then collect crossing
   candidates before mutating any world. Transfers cannot give a body a second step in
   the destination during the same tick. Destination contacts begin on its next step;
   the first contract does not solve the remaining sub-tick across the boundary.
2. Test the previous-to-current movement segment against the directed opening, including
   fast movers whose endpoints miss a sensor. Require aperture clearance for the whole
   supported shape and a valid destination placement. Resolve competing crossings in
   a declared stable order; admit at most one transfer per entity per tick initially.
3. Validate destination availability, capacity and body descriptors. Reject connected
   joint assemblies in this first slice rather than breaking joints implicitly. Prepare
   the destination body and fixtures while retaining the source; failure cleans up the
   staged destination and preserves source ownership.
4. Commit source removal and the entity-to-area/body mapping before publishing success.
   The stable game entity ID survives; the engine body ID changes. Do not step staged
   duplicates. If commit/cleanup becomes uncertain, stop the affected simulation and
   retain the actual state instead of emitting a successful traversal.
5. Emit an immutable traversal event with tick, portal, entity, source/destination area
   and poses. Re-arm the destination opening only after the entity clears a declared
   spatial hysteresis region, preventing accidental immediate bounce-back. Snapshot
   interpolation uses the traversal marker to snap/reset the affected entity's buffer;
   it never blends positions across unrelated area coordinates. Other entities continue
   ordinary interpolation.

## One executable example and bounded follow-ups

The first outcome is **one unjointed moving body traversing between two headless areas
through a rotated portal**, retaining entity identity and transformed momentum. Topology
can be prepared independently; actual Box2D transfer waits only for the parent plan's
`.1` usable local runtime, not its later public release or browser support.

| Window and owner | Concrete outcome and success condition |
|---|---|
| `P1 Topology and crossing` — Game.Core owner | Define the immutable graph/map/crossing contract and qualify a loop, multiple entrances and a one-way edge. Verify forward/backward direction, rotated mapping, fast segments, missed/too-small openings, malformed maps and bounded competing crossings. This source window needs no Box2D runtime. |
| `P2 Headless whole-body transfer` — Box2D adapter owner | Run the two-area rotated-portal scene from its real entry point. Observe one live body/entity after commit, preserved descriptor/state, correct pose and velocities, one traversal event, one step per tick and exit re-arming. Missing area, blocked destination, capacity failure, staged creation failure and joint-connected body cases preserve source ownership or report explicit stopped/uncertain state. Retain actual cleanup and repeat-input replay results under the parent's pinned profile. |
| `P3 Presentation and installed opt-in` — Rendering/product owners | First prove traversal markers prevent cross-area interpolation; optionally show a bounded view through one portal with explicit recursion limits. Viewing a portal is rendering work and does not certify cross-boundary physics. After the parent adapter's public installed qualification, run a fresh package-only opt-in consumer and a preserving retained-adoption case. |
| `P4 Portal-aware queries` — selected Game subsystem owner | Separately select rays/projectiles and routes, then visibility or sound only when needed. Each query declares portal eligibility, transformed direction, distance accounting, maximum hops and cycle termination; qualify local obstructions and cyclic graphs before recording support. No subsystem implicitly traverses every edge. |

Stop preparation when each named example is ready to execute. Retain an actual failed
operation, repair its smallest demonstrated defect and make a fresh bounded attempt.
Replan if whole-body transfer or the ownership model proves inadequate; ordinary test
failures do not restart the Box2D design. Publication and runtime operations retain their
normal effect-specific safeguards.

## P1 source qualification — 2026-10-04

The local candidate implements `AreaTopology` in Game.Core with no additional runtime
package dependency. Immutable validated area/portal IDs and rigid maps permit cycles,
multiple entrances and one-way edges. Declared reverse pairs require reciprocal,
inverse mappings and opposite physical traversal. Apertures must have finite distinct
endpoints and match under the map within an absolute `1e-9` local-unit tolerance.

`tryCrossing` tests the previous-to-tick-end segment, requires conservative enclosing-disc
clearance at both openings, and returns the earliest crossing (ties by `PortalId`). It
returns at most one proposal per call and can exclude openings not yet re-armed. The
proposal carries mapped tick-end pose and velocities, preserving angular velocity.
Backend destination admission, once-per-entity/tick ownership and hysteresis remain P2
responsibilities; this pure decision is not an actual transfer.

Focused source qualification used the repository's `net10.0` Core test entry point:

```console
DOTNET_PROCESSOR_COUNT=1 dotnet run --project tests/Game.Core.Tests/Game.Core.Tests.fsproj --disable-build-servers -p:BuildInParallel=false -p:UseSharedCompilation=false -- --filter "Game.Core AreaTopology" --sequenced
```

All nine tests passed: cyclic/multiple/one-way topology, fast swept crossing, direction
and misses, enclosing-disc clearance, rotated pose/momentum and inverse mapping,
declared reverse validation, malformed topology/maps, finite input and overflow refusal,
and deterministic competing crossings with blocked openings. Core and its complete test
assembly compiled. An initial test import failed `FS0892` and was corrected to qualified
module references before the passing run. Native merge readback, broader repository gates,
actual P2 transfer, publication and installed/consumer qualification remain open.

## P2 local source qualification — 2026-10-04

A local candidate joins the exact qualified P1 topology and Box2D runtime sources; it
is not protected source acceptance. `AreaRuntime` owns active independent local worlds,
validates routed commands before any step, steps every world once in stable area order,
and collects every crossing proposal before staging or removing bodies. It preserves
Runtime's supported circle/box descriptors, material, density, kind, sensor flag and
current awake state. Other filters, damping, gravity scale and sleep configuration
remain Runtime's fixed defaults. Joint-connected bodies refuse before mutation.
Destination placement uses conservative enclosing-disc overlap with non-sensor bodies.
Self-area proposals refuse duplicate staging in this initial slice.

Known missing-area, capacity, blocked-placement and staging-validation refusals preserve
source ownership. Source removal follows real destination creation; transfer hooks do
not step. An engine or commit uncertainty stops the whole owner, retains its worlds,
and publishes no successful tick. `InspectStopped` offers best-effort actual copied
observations with unreadable worlds explicit as `None`; the last fully published
snapshot remains separate. Hidden solver state and contacts are not restored.

The actual `examples/Box2D.Portals` entry point ran a circle through the paired 90-degree
portal into a second headless world: one body/entity and one traversal at tick 1, copied
position `(9.9,5.15)`, velocity `(-1,4)` and angular velocity `2`. Both local worlds had
one step; five recorded ticks repeated exactly in fresh worlds under Box2D.NET `3.1.654`
and .NET `10.0.401`, 0.1-second ticks and four substeps. Pose and momentum checks use
absolute `2e-5` tolerance, with angle compared modulo one turn. Source-only replay scope
is the pinned local profile.

Eleven focused `Box2D AreaRuntime` tests passed: the real scene, asleep descriptor
recreation without stepping, missing/capacity/blocked/joint refusal, actual single-precision
staging-validation refusal, injected commit interruption with cleanup of a real staged
body and explicit stopped observations, exit re-arming after recorded clearance,
interpolation discontinuity and global identity prevalidation. Injected interruption
qualifies that failure path; it does not claim an observed native engine allocator fault.
Retained snapshots are immutable, and transferred entities snap while ordinary entities
continue interpolation.

The failed compiler/name-resolution attempts and real angle-drift observations were
retained privately. The engine's approximate rotation construction initially compounded
error when recreating a body; staging now installs a direct sine/cosine transform before
preserving awake state, and the real entry passed the original strict tolerance.
Repository gates, generated adapter baselines and native merge readback remain pending.
Publication, installed support, P3 product adoption and browser physics remain open.

## Later physics and workspace boundaries

A crate straddling an opening and colliding on both sides, or a rope/joint spanning
areas, requires a separately designed and measured solver/constraint strategy. Body
recreation does not preserve hidden contacts, warm-start state or exact solver rollback.
Mirrors and scale changes also require shape handedness, size, mass and velocity rules.
None of these features is a prerequisite for P1/P2 or claimed by the first example.

Portal snapshots/events inherit the parent's version/runtime replay limits; topology
alone establishes no cross-platform determinism. Browser rendering and Fable physics
still require separately qualified backends. This plan changes no workspace defaults:
public producer support precedes P3's fresh opt-in creation and separately observed
retained adoption/removal. Full-V2 and current BAR/SC2 preparation remain independent.
