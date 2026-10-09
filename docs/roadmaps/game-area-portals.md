# GAME-PORTAL-01 — Area topology and rigid portal traversal

Status: P1/P2 source delivered and public installed support accepted in Game `0.17.0`,
2026-10-05. Game owns topology and traversal; the Box2D adapter owns local-world
transfers; Rendering owns portal presentation. fdev integrates their joins.
Parent: [GAME-BOX2D-01](game-box2d-physics.md#area-topology-and-portals).
P3 presentation/product adoption and P4 queries remain open.

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
module references before the passing run. Subsequent native source gates and merge
completed in [PR696](https://github.com/FS-GG/FS.GG.Game/pull/696), protected merge
`82cb46d8025e43a09d6bbac0ef7bbd517318d7d4`. Actual P2 transfer and public installed
support were qualified separately below.

## P2 local source qualification — 2026-10-04

The local candidate joined the exact qualified P1 topology and Box2D runtime sources.
These local observations preceded native source acceptance. `AreaRuntime` owns active
independent local worlds, validates routed commands before any step, steps every world once in stable area order,
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
and .NET SDK `10.0.401`, 0.1-second ticks and four substeps. Pose and momentum checks use
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
Repository gates and generated adapter baselines subsequently qualified, and
[PR698](https://github.com/FS-GG/FS.GG.Game/pull/698) merged at
`75ff1926f54b6cbaa050818ebeacd0a9aefd63cf`. The public installed proof below uses
a separately observed runtime profile; P3 product adoption and browser physics remain open.

## Window closure and public installed support

- [x] P1 topology and crossing: source delivered by PR696.
- [x] P2 headless whole-body transfer: source delivered by PR698.
- [ ] P3 presentation and installed opt-in: public-installed prerequisite met; explicit product adoption and preserving retained adoption/removal remain open.
- [ ] P4 portal-aware queries: separate follow-up, not selected.

Game's [accepted `0.17.0` release and fresh public consumer](game-box2d-physics.md#accepted-public-release-and-installed-example)
qualify this adapter from immutable source `8cd158db8cb0836968df3f695fad75366434886c`,
[release run37237849417](https://github.com/FS-GG/FS.GG.Game/actions/runs/37237849417).
The four-package custody/readback evidence establishes both-feed payload equality,
with NuGet repository signatures excluded from the payload comparison.

The outside-checkout consumer restored only public packages into a fresh cache;
its closure was Game adapter/Core `0.17.0`, Box2D.NET `3.1.654` and FSharp.Core `10.1.302`,
without repository ProjectReferences or rendering dependencies. On .NET `10.0.12` /
Ubuntu `24.04.5 LTS` / X64 it ran both the falling-body scene and five recorded portal ticks.
Portal assertions confirmed one live `traveller`, one committed traversal, one step per
local world/tick, transformed pose/momentum and exact fresh-world replay. The destination
position was `(9.899999618530273, 5.150000095367432)` and velocity `(-1, 4)`.

This establishes public producer support and P3's installed prerequisite. Presentation,
explicit fresh product/Template opt-in and preserving retained adoption/removal require
their own outcomes; P3 remains open. No browser physics, cross-platform determinism or
hidden-solver restoration is inferred.

## Later physics and workspace boundaries

### P3 opt-in presentation source qualification — 2026-10-09

The local example adds explicit `--presentation` selection while retaining its default
physics qualification. `Presentation.view` reuses `AreaPhysics.interpolate` once per
frame and `Game.Render.Adapter.drawPoints`, with deterministic area/entity bindings
and actual points-node inspection. Its declared structural workload is two areas,
two entities, at most five ticks and three alpha samples per consecutive pair;
focused Render tests reference the actual example assembly, including midpoint,
destination snap, ordering, refusal and missing/current-only/global-blend controls.

The retained source was recovered onto current main. Public NuGet restore genuinely
regenerated only the example and Render-test dependency locks, and locked restore
then passed. The first compilation exposed strict-indentation errors in a nested
test record update; the repaired test source builds under .NET SDK 10.0.401 with zero
warnings or errors. Assembly discovery found all eight `Portal presentation` cases,
and the focused assembly run passed all eight:

```console
dotnet tests/Game.Render.Tests/bin/Debug/net10.0/Game.Render.Tests.dll --filter-test-list "Portal presentation" --sequenced
```

This includes
the negative controls and the self-contained five-tick managed Box2D simulation;
it is ordinary source testing, not installed/native acceptance.

The compiled `Box2D.Portals.dll` entrypoint also passed in both default and
`--presentation` modes. Default mode reported one traversal and exact pinned-profile
replay; presentation mode reported five ticks, twelve frames, twenty-four area nodes and
twenty-four points. An invalid argument printed usage and exited with code 2.

At this source qualification, public-only rendering-closure qualification remained
pending; the separate fresh receiver below supplies that bounded evidence. P3 remains
open for runtime performance counters and fresh/retained product adoption. Historical
producer `0.17.0` acceptance and the retained
resume04 failure/resume05 unrun reservation are unchanged; this source attempt
does not retry those operations or transfer their custody.

### P3 fresh public presentation package receiver — 2026-10-10

The existing Box2D package-consumer runner has an explicit public `--presentation`
route and a separate exact-PackageReference project. It copies the delivered portal
presentation and actual command-line modules into a fresh outside-checkout receiver,
checks source/project inputs before restore and the expected seven-package rendering
closure before build, then requires unchanged locked restore and both entrypoint modes.
The original four-package physics consumer remains a separate no-presentation route.

Thirteen local source controls passed with synthetic assets and a stubbed `dotnet`:
both valid routes reached restore, missing/linked source and foreign direct pins or
ProjectReferences refused before restore, and missing Render, foreign packages,
project dependency nodes and disabled assertions refused at the boundary.

A separately admitted fresh public receiver then passed at source
`0a8665a9c8005f3edf2786ebd85bac311f1cd0d5`. Its new package and HTTP caches used
only nuget.org. Actual restored assets and nuspecs passed the exact seven-package
boundary: Game Core, Physics.Box2D and Render `0.17.0`, UI Scene and KeyboardInput
`0.31.0`, Box2D.NET `3.1.654`, and FSharp.Core `10.1.302`. The genuinely generated
lock remained byte-identical after locked restore. Release compilation reported no
warnings or errors. The copied actual default and presentation entrypoints exited 0;
an invalid argument printed usage and exited 2. Presentation reported five ticks,
twelve frames, twenty-four area nodes and twenty-four points.

The receiver finished in 6.224 seconds on CPU 1 with a 1.5 GiB managed heap cap;
this duration is an operation observation, not a performance qualification. A separate
post-terminal census found no remaining member of its owned process group/session;
detached descendants and total RSS were not independently measured. This proves the
bounded public package rendering closure and copied-source entrypoints, not generated
product installation or retained adoption. P3 remains open for performance and
fresh/retained product adoption. Original resume04/resume05 effects, caches and custody
are unchanged and were not reused.

### P3 current presentation performance smoke source — 2026-10-10

An explicit `--presentation-performance` mode requires a declared exact source revision
and emits raw stock counters plus its actual assembly and workload digests. It creates
and validates the existing five-tick scene once, disposes its worlds, warms up five
cycles, and then measures twenty samples of one hundred twelve-view cycles: 24,000
actual interpolation and scene-projection views over immutable copied snapshots.
Each returned frame is retained and checked outside timing against qualified bindings,
identity/point coverage and structural bounds. Default and presentation modes remain.

The mode reports elapsed timestamps, process CPU ticks and GC collection deltas,
and allocations on the synchronous measurement thread, with per-view normalization
and sample ranges/medians. Loop/storage/counter overhead is included; setup, validation
and JSON serialization are excluded. Process CPU includes runtime threads and has
coarse resolution. No subtraction, timing threshold or speedup verdict is invented.

Direct optimized F# compilation against the already qualified public assemblies passed
with warnings as errors. Six compiled refusal controls passed before world setup:
missing, short, uppercase and nonhex declared source revisions, an invalid argument,
and an extra performance argument all exited 2; the existing usage substring remains.
One separately admitted fresh offline receiver then passed at source
`13a8cdeb821e43bcd94701902d5b6b2a3e6f42d4`. It used only the seven newly owned,
previously public-qualified package payloads in an isolated local feed and empty new
caches. Locked restore preserved the genuine lock, the actual package boundary passed,
and Release compilation reported no warnings or errors. Default/presentation/invalid
entrypoints exited 0/0/2; the explicit performance entrypoint exited 0.

All twenty raw samples and 24,000 checked views were retained under .NET `10.0.12`,
SDK `10.0.401`, CPU 1 and a 1.5 GiB managed heap cap. Elapsed time per view had a
median of 886.82 ns and a range of 777.68–9,159.78 ns. Process CPU per view had a
median of 885 ns and a range of 776.67–9,117.5 ns. Measurement-thread allocations
per view had a median of 1,970.07 bytes and a range of 1,970.07–1,970.17 bytes.
Process collection deltas summed to 2/1/0 for generations 0/1/2. Outliers were retained;
there was no rerun. These are scoped current smoke observations, not a budget verdict,
speedup claim or proof of steady-state behavior. Workload SHA256 is
`66113918829ada39ae1a4fdfd32255e300e3986db4bbabc0b6e69807f34d4fd4`;
the measured assembly SHA256 is
`cd1fcc61c20cce56e37cbfcf0a382d27eb74642f39d8af79c61f69b76b2530d1`.

The original pre-edit performance baseline remains unknown. The receiver completed
in 3.32 seconds; that whole-operation duration is separate from the view samples.
A separate post-terminal census found no owned process-group/session member;
total RSS and detached descendants were not independently measured. Physics stepping,
raster/GPU timing and installed-product/adoption are outside the measured scope.
Performance release requirements and fresh/retained product adoption remain open;
P3 is not done.
Original resume04/resume05 effects and custody remain unchanged.

Native PR709 checks on Ubuntu and Windows subsequently failed compilation at the
nullable source-revision guard; the surface-baseline job stopped at the same build,
before baseline refresh. The guard now uses `Option.ofObj` to refine the environment
value before string operations. Direct compilation with null checking, latest language
version and warnings as errors passes, as do the six compiled refusal controls.
The measured computational body is byte-identical to source `13a8cdeb`; no measurement
was repeated. The raw observations above remain bound to that original source and
assembly, rather than certifying the successor assembly's exact timing.

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
