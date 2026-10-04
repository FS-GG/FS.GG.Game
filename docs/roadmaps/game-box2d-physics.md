# GAME-BOX2D-01 — Optional Box2D physics backend

Status: runtime/replay source delivered and public package-only qualification accepted,
2026-10-05. FS.GG.Game owns the adapter and qualification; fdev integrates the result.
Explicit product/Template adoption remains open. This track is independent of full-V2
acceptance and the BAR/SC2 preparation lanes.

Programme links: [Unified Roadmap §9.8](https://github.com/FS-GG/.github/blob/main/docs/2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)
and [V2 roadmap](https://github.com/FS-GG/.github/blob/main/docs/github-substrate-v2-roadmap.md#optional-box2d-game-physics--2026-10-04).

## Package and simulation boundary

The opt-in package **`FS.GG.Game.Physics.Box2D`** is available alongside
`FS.GG.Game.Core`. The adapter references Core and a pinned Box2D.NET package;
Core retains its FSharp.Core/BCL runtime dependency boundary. The adapter has no
rendering dependency, so headless servers and playtests can use it independently.
Game.Render or another presentation adapter consumes its snapshots.

| Simulation option | Intended use |
|---|---|
| Existing `Geometry` + `Resolution` | Arcade movement, tile collisions, sliding and knockback |
| Existing `Physics` | Lightweight rigid bodies, friction, bounce, sleeping and simple shapes |
| Optional Box2D adapter | Joints, connected mechanisms and more elaborate physical interactions |

The [current Core project](https://github.com/FS-GG/FS.GG.Game/blob/8de4c2747d40e9993cc9a08cd50e1e2d599f69fb/src/Game.Core/FS.GG.Game.Core.fsproj)
and [Physics contract](https://github.com/FS-GG/FS.GG.Game/blob/8de4c2747d40e9993cc9a08cd50e1e2d599f69fb/src/Game.Core/Physics.fsi)
establish the existing boundary and simulation options. Box2D.NET is a C# port of
Box2D; its [project configuration](https://github.com/ikpil/Box2D.NET/blob/154bacd0fddc0e72e484d3061cdc76aa2d842994/src/Box2D.NET/Box2D.NET.csproj)
includes `net10.0`, aligning with Game's .NET target. That upstream source observation
did not select or qualify a published dependency version. The actual dependency pin and
observed runtime qualification are recorded below.

## Own mutation; publish immutable observations

The [existing loop contract](https://github.com/FS-GG/FS.GG.Game/blob/8de4c2747d40e9993cc9a08cd50e1e2d599f69fb/src/Game.Core/Loop.fsi)
retains distinct `Previous` and `Current` values. Box2D.NET's
[world implementation](https://github.com/ikpil/Box2D.NET/blob/154bacd0fddc0e72e484d3061cdc76aa2d842994/src/Box2D.NET/B2Worlds.cs)
uses mutable worlds addressed by IDs. Returning the same mutable handle after stepping
would make both buffers observe the updated world and invalidate interpolation.

The adapter therefore owns the mutable runtime and its lifetime. Game logic
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

The first delivered outcome is **one headless example with falling bodies, one joint
and one sensor**, using the optional adapter. The following windows retain their
acceptance conditions; current closure is recorded below.

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

The [GAME-PORTAL-01 extension](game-area-portals.md) adds an immutable area graph and
rigid portal mappings above local physics. Its first runtime example transfers one
whole body between two independent Box2D worlds through a rotated opening, preserving
entity identity and momentum while marking the interpolation discontinuity. Pure
topology preparation is independent; P2 now uses the delivered local adapter.
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

## Window closure

- [x] `.1` Runtime and headless example: source delivered in [PR697](https://github.com/FS-GG/FS.GG.Game/pull/697), merged at `75df459ebe7dae755eacba3850a5074eba9ae3c2`.
- [x] `.2` Retained snapshots, interpolation and pinned input replay: delivered with the same qualified source.
- [x] `.3` Public package and fresh installed example: coherent `0.17.0` publication and public-only two-scene consumer accepted from [release run37237849417](https://github.com/FS-GG/FS.GG.Game/actions/runs/37237849417).
- [ ] `.4` Explicit consumer adoption: not selected.

The optional project is `src/Game.Physics.Box2D/FS.GG.Game.Physics.Box2D.fsproj`.
Its package identity is `FS.GG.Game.Physics.Box2D` and it inherits Game's coherent
version convention. Core, Render, Harness and Physics.Box2D were published together at
`0.17.0`; publication evidence is recorded separately from the source results below.

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
sensor event attribution and shortest-arc interpolation. These checks passed locally on 2026-10-04 and subsequently passed the native source
delivery gates before PR697 merged.

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
registered initial type/member baselines (27 types, 52 members). The native source
gates completed before merge. These local results alone did not establish publication,
installed support or portal transfer; the later joins and release evidence follow below.
Browser and wider-platform acceptance remain outside this qualification.

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
Local candidate preflight passed on source `2eaca0271431a1239f9e1ba2640005687b3bd63d`:
one coherent preparation produced exactly the four `0.17.0` packages, with existing-three
API compatibility against `0.16.0` and first-release adapter validation. The source-bound
metadata/API/dependency checks and independent Journey consumer passed. A fresh copied
Box2D package-only consumer cold-restored the candidate packages into an empty cache,
proved an exact four-package dependency closure (adapter, Core, Box2D.NET and FSharp.Core),
and ran the real 240-tick falling/joint/sensor scene with exact replay. It reported
.NET 10.0.12 / Arch Linux / X64, final falling height `0.39992859959602356` m and maximum
joint length error `0.00008517808537522598` m, with sensor enter/exit and floor contact.
The original candidate bytes and consumer inputs were retained; no package was published.
This candidate evidence binds the named source, independently of later documentation edits.
At that local checkpoint, native source delivery, both-feed publication/readback and
public-only installed qualification were pending. The accepted release below uses its own
fresh preparation, rather than these historical candidate bytes.

## Portal transfer source join for the selected release

The isolated successor joins the release source with the qualified P2 adapter/real headless
portal scene (`10f63b469ec8b63635a8f4c5fe6e66f11010b3c9`), delivered by
[PR698](https://github.com/FS-GG/FS.GG.Game/pull/698) at protected merge
`75ff1926f54b6cbaa050818ebeacd0a9aefd63cf`. The package-only consumer now copies that
scene into its own `PortalScene.fs` and compiles it only against restored package APIs, alongside
the existing falling-body scene. It requires one live traveller after the rotated transfer,
transformed pose/momentum, one step per local world/tick, one traversal, no repeated transfer and
exact fresh-world replay. The package metadata gate also requires the packed `AreaRuntime.fsi`.

The earlier immutable `2eaca027` preparation and its four artifacts remain historical; they do
not contain or qualify the expanded area-transfer API. The joined successor completed a fresh
version-consistent restore (including the new portal example lock), existing-three API/package
validation, one fresh four-package preparation and independent candidate smoke for both scenes.
[PR699](https://github.com/FS-GG/FS.GG.Game/pull/699) delivered that release source at
`080713373edafacc2945d089beb38fa75d6a6267`. Product/Template opt-in remains separate.
No old candidate was substituted for the final release preparation.

## Accepted public release and installed example

The immutable `v0.17.0` tag names `8cd158db8cb0836968df3f695fad75366434886c`.
[Release run37237849417](https://github.com/FS-GG/FS.GG.Game/actions/runs/37237849417)
completed successfully on 2026-10-04 UTC: full native verification, one coherent pack,
candidate qualification, original custody retention, GitHub-first publication, OIDC
nuget.org publication, both-feed payload comparison and fresh public-only qualification.
All four packages are `0.17.0`, including the first
[Physics.Box2D public package](https://www.nuget.org/packages/FS.GG.Game.Physics.Box2D/0.17.0).

The downloaded [original custody artifact11315758497](https://github.com/FS-GG/FS.GG.Game/actions/runs/37237849417/artifacts/11315758497)
matched ZIP SHA256 `2691e04b592858e9bf913fe0b623dd8355f92fbce1dcf8f2b45cd0baba1f11ac`;
the [readback artifact11316676562](https://github.com/FS-GG/FS.GG.Game/actions/runs/37237849417/artifacts/11316676562)
matched `97c52e492052a78ad92018f16e1e5b4f15f96d3c16b8e346688130f1ad347ce3`.
The custody manifest and every package's version/source metadata agreed. Native GitHub
archive hashes equalled original custody. Independently downloaded public packages matched
the recorded public hashes and every non-signature ZIP member equalled custody. NuGet
repository signing changes archive hashes; this establishes payload equality, without
claiming full signed-archive byte equality or independent signature authentication.

The fresh consumer copied independent source outside the checkout, restored from nuget.org
into an empty cache, and built with zero warnings/errors. It had no ProjectReference,
repository import or linked source. Its exact closure was adapter/Core `0.17.0`, Box2D.NET
`3.1.654` and FSharp.Core `10.1.302`; Core retained only FSharp.Core and the adapter had no
rendering dependency. Actual execution used .NET `10.0.12`, Ubuntu `24.04.5 LTS`, X64.

The 240-tick scene passed retained-snapshot/interpolation and exact pose/event replay checks,
observed sensor enter/exit and floor contact, and reported final falling height
`0.39992859959602356` m with maximum joint error `0.00008517808537522598` m.
The five-tick portal scene passed exact replay, one live `traveller`, one traversal and
one step per local world/tick. Its rotated destination pose was
`(9.899999618530273, 5.150000095367432)` with velocity `(-1, 4)`.
This closes `.3` and supplies Portal P3's public-installed prerequisite. It does not
record `.4` product/Template opt-in, preserving retained adoption/removal, browser support,
solver rollback or cross-platform determinism.
