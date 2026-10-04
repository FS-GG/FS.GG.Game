# GAME-BOX2D-01 — Optional Box2D physics backend

Status: runtime and replay source qualified locally, 2026-10-04. FS.GG.Game owns the
adapter and qualification; fdev integrates the result. Source delivery, publication
and adoption remain open. It is independent of full-V2
acceptance and the current BAR/SC2 preparation lanes.

Programme links: [Unified Roadmap §9.8](https://github.com/FS-GG/.github/blob/main/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)
and [V2 roadmap](https://github.com/FS-GG/.github/blob/main/docs/github-substrate-v2-roadmap.md#optional-box2d-game-physics--2026-10-04).

## Package and simulation boundary

Add an opt-in package, provisionally **`FS.GG.Game.Physics.Box2D`**, alongside
`FS.GG.Game.Core`. The adapter references Core and a pinned Box2D.NET package;
Core retains its FSharp.Core/BCL runtime dependency boundary. The adapter has no
rendering dependency, so headless servers and playtests can use it independently.
Game.Render or another presentation adapter consumes its snapshots.

| Simulation option | Intended use |
|---|---|
| Existing `Geometry` + `Resolution` | Arcade movement, tile collisions, sliding and knockback |
| Existing `Physics` | Lightweight rigid bodies, friction, bounce, sleeping and simple shapes |
| Proposed Box2D adapter | Joints, connected mechanisms and more elaborate physical interactions |

The [current Core project](https://github.com/FS-GG/FS.GG.Game/blob/8de4c2747d40e9993cc9a08cd50e1e2d599f69fb/src/Game.Core/FS.GG.Game.Core.fsproj)
and [Physics contract](https://github.com/FS-GG/FS.GG.Game/blob/8de4c2747d40e9993cc9a08cd50e1e2d599f69fb/src/Game.Core/Physics.fsi)
establish the existing boundary and simulation options. Box2D.NET is a C# port of
Box2D; its [project configuration](https://github.com/ikpil/Box2D.NET/blob/154bacd0fddc0e72e484d3061cdc76aa2d842994/src/Box2D.NET/Box2D.NET.csproj)
includes `net10.0`, aligning with Game's .NET target. That upstream source observation
does not select or qualify a published dependency version. The implementation owner
must pin the actual package and supported runtime before the first executable proof.

## Own mutation; publish immutable observations

The [existing loop contract](https://github.com/FS-GG/FS.GG.Game/blob/8de4c2747d40e9993cc9a08cd50e1e2d599f69fb/src/Game.Core/Loop.fsi)
retains distinct `Previous` and `Current` values. Box2D.NET's
[world implementation](https://github.com/ikpil/Box2D.NET/blob/154bacd0fddc0e72e484d3061cdc76aa2d842994/src/Box2D.NET/B2Worlds.cs)
uses mutable worlds addressed by IDs. Returning the same mutable handle after stepping
would make both buffers observe the updated world and invalidate interpolation.

The proposed adapter therefore owns the mutable runtime and its lifetime. Game logic
submits commands; each fixed simulation tick applies them and steps the engine; the
adapter then copies poses and events into immutable game-facing values. Mutable engine
IDs and borrowed event buffers do not become the loop's public world state.

- Commands create/remove bodies, apply forces and impulses, and configure joints.
- Fixed-step execution uses FS.GG.Game's simulation ticks, with an explicit command
  order and solver/substep configuration. Presentation time never feeds the solver.
- Immutable snapshots carry body positions and rotations keyed by game entity IDs.
  Retained snapshots remain unchanged after later steps or runtime disposal.
- Gameplay events copy contact and sensor observations into game entity IDs before
  engine buffers can change. Removed/stale body IDs have an explicit refusal policy.
- Conversion owns coordinate scale, axis conventions, angles and numeric precision;
  validate finite inputs and document the chosen units at the adapter boundary.

Keep the existing physics options. Defer a shared abstraction across engines until the
small example demonstrates which contracts are actually shared.

## First executable outcome and later joins

The next outcome is **one headless example with falling bodies, one joint and one
sensor**, using the optional adapter. Stop expanding preparation once this scene is
ready to compile and run under its pinned profile.

| Window | Concrete outcome and success condition |
|---|---|
| `.1 Runtime and headless example` — Game owner | Select exact dependency/runtime, implement world ownership, commands, fixed ticks, copied snapshots/events and disposal. Run the scene from its real entry point; observe falling bodies, a working joint and sensor enter/exit events with correct entity IDs. No renderer is required. |
| `.2 Snapshot and input replay qualification` — Game owner | Retain two snapshots and prove later steps do not mutate either; verify interpolation between them. Run the same recorded inputs in two fresh worlds under the pinned version/runtime/configuration and compare per-tick poses and events under an explicitly declared exact or tolerance-based criterion. Record actual results and supported scope. |
| `.3 Public package and installed example` — Game release owner, fdev integration | Publish the accepted adapter bytes through Game's existing release route. Compile and run a fresh package-only headless consumer without sibling source references; verify Core's runtime dependency boundary and the adapter's lack of rendering dependencies. Source tests alone do not establish installed support. |
| `.4 Explicit consumer adoption` — selected product/Template owner | After public installed qualification, qualify an opt-in consumer using the same pinned profile. Observe its actual physics behavior and preserving upgrade/removal path before recording adoption. Browser/Fable support requires its own selected backend and qualification. |

For a failed operation, retain the actual failure, repair the smallest demonstrated
defect and make a fresh bounded attempt. Replan only if the failure invalidates world
ownership, the package boundary or the selected runtime architecture. Existing Game
physics and release evidence remain reusable where their inputs and scope still match.
Normal source checks and effect-specific release/runtime safeguards apply; this plan
does not grant publication, deployment or product activation authority.

## Replay, rollback and platform limits

Fixed timesteps alone do not establish cross-platform determinism. Qualification in
`.2` covers only its pinned Box2D.NET version, runtime and solver configuration; wider
lockstep support needs its own measured platform matrix. A copied pose snapshot supports
presentation, not restoration of hidden solver state. Exact rollback requires a separate
state/reconstruction contract and fresh replay/recovery qualification before it is promised.

Fable JavaScript needs a separate backend or demonstrated compatibility strategy.
The .NET headless result cannot certify browser support. Keep both concerns outside
the first executable example's prerequisites.

## Area topology and portals

The [GAME-PORTAL-01 extension](https://github.com/FS-GG/.github/blob/main/docs/roadmaps/game-area-portals.md) adds an immutable area graph and
rigid portal mappings above local physics. Its first runtime example transfers one
whole body between two independent Box2D worlds through a rotated opening, preserving
entity identity and momentum while marking the interpolation discontinuity. Pure
topology preparation is independent; actual transfers wait for `.1`'s local adapter.
Portal-aware queries, viewing through openings, mirrored/scaled mappings and seamless
cross-boundary collisions/joints have separate follow-ups. This extension does not
delay the first falling-body/joint/sensor example or broaden its acceptance claim.

## Workspace impact

Affected families are opt-in .NET game/server consumers, with a later explicitly
selected template or product integration. Landing this plan changes no generated files
or physics defaults. `.3` first makes the optional package publicly usable; `.4` first
changes a selected fresh workspace's generated/runtime behavior after producer
publication and installed qualification. Existing arcade and Core Physics consumers
keep their current route. Fresh creation must demonstrate explicit opt-in; retained
adoption must preview and preserve user-owned files and record rollback/removal separately.

## Source window qualification

- [ ] `.1` Runtime and headless example: locally qualified; native source delivery pending.
- [ ] `.2` Retained snapshots, interpolation and pinned input replay: locally qualified; native source delivery pending.
- [ ] `.3` Public package and fresh installed example: successor release source and independent consumer prepared; actual package/publication/installed proof pending.
- [ ] `.4` Explicit consumer adoption: not selected.

The optional project is `src/Game.Physics.Box2D/FS.GG.Game.Physics.Box2D.fsproj`.
Its package identity is `FS.GG.Game.Physics.Box2D` and it inherits Game's coherent
version convention (selected successor 0.17.0); the producer release source prepares all four Game packages
at that scalar. Source preparation does not establish publication.

The dependency is [published Box2D.NET 3.1.654](https://www.nuget.org/packages/Box2D.NET/3.1.654),
whose package metadata records upstream revision
[`5efc96def866edbb4e5a9368d84de5bf8c2dcaca`](https://github.com/ikpil/Box2D.NET/tree/5efc96def866edbb4e5a9368d84de5bf8c2dcaca).
The downloaded nupkg SHA-256 is
`5778868236bd0513b1ffab13ac8950f9fca4bf338901d7a4c7f5c25a0f2fce4c`.
The nuspec provides net10.0 and no transitive dependencies; published API/source,
including the current joint base/local frames and event buffers, was checked before implementation.
The repository's dependency pin is in its owned `Directory.Packages.local.props`.

The real entry point is `examples/Box2D.Headless/Program.fs`. It drives 240 ticks from
ordered command input, checks falling motion, the distance joint (maximum length error
below 0.03 m), sensor enter/exit and floor contact IDs, midpoint interpolation, and exact
per-tick copied pose/event equality in two fresh worlds. The focused test suite also
checks retained observations after disposal, pre-mutation batch refusal, removed-shape
sensor event attribution and shortest-arc interpolation. These checks passed locally on 2026-10-04. Native source delivery is still pending.

The actual headless entry point was compiled with the repository SDK profile
(`global.json`: 10.0.401) and invoked with `dotnet exec --fx-version 10.0.12`. Its
reported environment was .NET 10.0.12, Arch Linux, X64. It observed sensor enter
and exit for `(sensor, falling)`, floor contact for `(falling, floor)`, final falling
height `0.39992859959602356` m, and maximum joint length error
`0.00008517808537522598` m over 240 ticks. Exact replay compares every copied pose
and event at every tick in two fresh worlds within that observed profile.

Both focused project builds passed with zero warnings and errors. The six focused
tests passed, including retained snapshots after disposal, full batch refusal before
mutation, removed-shape event IDs, and shortest-arc interpolation. A concurrent-lifetime regression also qualified
128 independent world lifetimes across up to eight workers using the native default
parallel test route. After a native Windows failure exposed concurrent slot allocation
in the pinned engine's shared world table, adapter allocation/disposal now use one
shared lifetime lock. Independent worlds may step concurrently; direct Box2D world
lifetime calls outside the adapter must not race its operations. The adapter has
registered type/member baselines (27 types, 52 members); whole-solution qualification
and the native source delivery gates remain the integrator's obligation. No public
package, installed consumer, portal transfer, browser or wider-platform acceptance
is established by these local results.

## 0.17.0 release source and preflight

The selected source successor is stable `0.17.0`, with Core, Render, Harness and the new
Physics.Box2D adapter forming one coherent release set. Existing packages use their published
`0.16.0` API baseline. The first adapter package has no predecessor baseline; current-package
validation and the exact package/API/dependency checks still apply. Both feed collision checks
must be refreshed before the release owner selects publication.

The existing `release.yml` route retains its verify dependency, exact-source version guard,
one coherent preparation, original custody bytes, GitHub Packages before nuget.org/OIDC,
byte comparison/readback, and authenticated retained-byte recovery. Successor fixtures check
four exact package identities, metadata, adapter .fsi/API entries, Core's FSharp.Core-only
runtime boundary and the adapter's absence of rendering dependencies. Candidate and public
smokes copy independent source into a fresh directory outside the checkout and restore only
PackageReferences; neither sibling projects nor repository imports/linked source qualify them.
The smoke executes the same complete 240-tick falling/joint/sensor and exact replay scene.
Candidate artifacts establish preflight only; the public-only fresh restore after genuine
both-feed readback is required before `.3` can close.

Pipeline-preflight selection is the existing-path static check plus focused mutation controls,
placed before release restore/build/tests. The changes alter scalar/roster and first-release
baseline selection while preserving the existing effect order and recovery route, so a custom
state model is deferred: its maintenance would duplicate unchanged controls. Investment is
bounded to the successor release fixture and isolated consumer; no new release framework or
registry authority is introduced. Source controls accepted the good workflow and rejected
substituted first/second/both push commands, a wrong scalar, foreign API baseline, missing
adapter custody, and missing/foreign/wrong-version artifacts before consumer launch. The
measured local source-only mutation run took about 0.22 seconds with no restore or downloads.
No billed runner savings or native publication acceptance are inferred from that measurement.
Actual packing, candidate consumer execution and public installed qualification remain pending.
