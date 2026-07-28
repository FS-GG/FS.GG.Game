# Fable lockstep compatibility proposal

**Status:** accepted for the bounded packaging spike  
**Owner:** FS.GG.Game  
**Consumer:** [EHotwagner/S.I.R](https://github.com/EHotwagner/S.I.R.)  
**Tracking:** [FS.GG.Game#526](https://github.com/FS-GG/FS.GG.Game/issues/526)  
**Contract identity:** `fs-gg-game-fable-lockstep`  
**Decision date:** 2026-07-28

## Decision

The first compatibility spike will augment the existing
`FS.GG.Game.Core` NuGet package with a Fable source-package view produced
through `Fable.Package.SDK`. The package remains the one versioned producer
artifact for .NET and Fable consumers. The Fable view must compile the same
implementation files that build the .NET assembly; it must not contain copied
algorithms or a consumer-owned fork.

Compatibility is declared per stable public function or indivisible group of
functions, not per assembly or module. A declaration has one of three grades:

| Grade | Meaning | Authoritative S.I.R. use |
|---|---|---|
| `LockstepExact` | The declared canonical input bytes produce byte-identical canonical output bytes under the pinned .NET and Fable/Node profiles | Allowed behind S.I.R.'s own semantic adapters |
| `Portable` | The surface compiles for both targets but only a semantic or tolerance contract is promised | Presentation and analysis only |
| `DotNetOnly` | The surface is not available through the supported Fable package path | Not allowed in shared authority |

The package does not acquire a blanket “Fable compatible” or “deterministic
across runtimes” label. Only entries present in the versioned profile and
backed by the shared fixture corpus carry a cross-runtime promise.

This decision accepts the package strategy and fixture protocol for a bounded
spike. It does not pre-certify a function. The spike either produces the
evidence required for the initial `LockstepExact` entries or returns to this
decision with a reduced compiler or packaging failure.

## Why this route

Fable's supported package-authoring route uses `Fable.Package.SDK` to place
ordered F# source and project metadata in the NuGet package's `fable/` view.
That matches the contract S.I.R. needs: restore a published artifact and
compile producer-owned source with Fable.

The existing package identity is retained because the .NET assembly and Fable
source are two target forms of one public contract. One version then identifies
the API surface, source body, compatibility profile, fixtures, and toolchain
manifest together. It also lets a clean consumer prove that no sibling
checkout participates in its build.

The existing `api-surface/*.fsi` payload remains an inert documentation and
scaffolding surface. It is not renamed or reinterpreted as Fable input. The
new `fable/` payload is separately generated and validated.

## Package-layout options

| Option | Result | Decision |
|---|---|---|
| Add a supported Fable source view to `FS.GG.Game.Core` with `Fable.Package.SDK` | One package/version and one source body; a normal .NET restore remains unchanged | **Selected for M3** |
| Publish a second `FS.GG.Game.Core.Fable` package over linked producer sources | Can isolate package mechanics, but introduces two independently publishable identities and a version-coherence obligation | Reserve only if the selected package shape fails with a reduced reproduction |
| Let S.I.R. link files or reference the sibling project | Quick locally, but the consumer build depends on checkout state and bypasses artifact qualification | Rejected |
| Copy compatible functions into a Fable-specific implementation | Packaging is simple, but behavior can drift and no longer proves one shared kernel substrate | Rejected |
| Publish generated JavaScript only | Useful as a deployment artifact, but cannot support a consumer compiling the shared F# kernel or auditing the exact source contract | Rejected as the library contract |

If `Fable.Package.SDK` cannot express a safe partial view without compiling
unsupported source, the spike may introduce a producer-owned internal packing
project. Such a project must link the canonical files in `src/Game.Core/`,
publish no second algorithm body, and still place its source view in the
`FS.GG.Game.Core` package. Creating a second public package is a decision
change, not an implementation detail.

## Initial compatibility-profile proposal

The profile is stored with the producer and versioned with the package. M3
tests only the minimum rows below. M4 expands the table and turns supported
rows into a published compatibility profile.

| Surface | Proposed grade | M3 evidence or boundary |
|---|---|---|
| `Cell` structural value and `(Col, Row)` total order | `LockstepExact` candidate | Boundary coordinates and adversarial ordering vectors |
| `Dir`, `Edge`, and `Edges.edgeBetween` | `LockstepExact` candidate | Forward/reverse adjacency, non-adjacency, and coordinate-edge vectors |
| `Edges.edgeOf`, `edgeCells`, `borders`, `corners`, `edgeEndpoints`, `vertexCells`, `vertexEdges`, `neighbours` | `LockstepExact` candidates after the minimum spike | Fixed order and boundary arithmetic vectors |
| `Edges.isEdgePassable`, `bfs`, and `astar` | `LockstepExact` candidates after the minimum spike | Canonical set ordering, bounds, ties, and degenerate inputs |
| Integer `Los.trace`/`lineOfSightBy` behavior selected by the spike | `LockstepExact` candidate | Thin/supercover corner cases, reverse endpoints, and occluder order |
| Bounded integer `Pathfinding` operation selected by the spike | `LockstepExact` candidate | Equal-cost tie, unreachable, exhausted bound, and start-equals-goal cases |
| `Rng` | Unclassified | A sequential stream is not S.I.R.'s authoritative counter-addressed random contract |
| `Point`, `Rect`, continuous `Geometry`, `Visibility`, `Ballistics`, and `Physics` | `Portable` or `DotNetOnly`, function by function | Floating-point presentation or simulation cannot enter S.I.R. lockstep authority |
| `FixedStep` and `Loop` | `Portable` until separately qualified | Wall-clock and floating accumulator behavior are outside authoritative tick advancement |
| `MapGen`, `MapAnalysis`, `Ai`, `Effects`, `Resolution`, `Fov`, `Grids`, `Hex`, `Dice`, and remaining pathfinding functions | Unclassified | No cross-runtime promise until individually inventoried and evidenced |

“Unclassified” is equivalent to unavailable for S.I.R. authority. It is not a
weaker informal promise.

## Bounded M3 spike

### Required source surface

The package-derived Fable consumer must exercise:

1. `Cell` plus a stable integer scenario identifier owned by the fixture
   schema;
2. `Edges.edgeBetween`;
3. one integer LOS operation;
4. one bounded pathfinding operation; and
5. package restore and Fable compilation in a clean directory that has no
   sibling source checkout.

The implementation may factor mixed source files so the selected subset has a
coherent compile order. Factoring cannot change the normal .NET public surface
or duplicate an implementation.

### Toolchain manifest

The evidence records:

- package id and version;
- package archive SHA-256;
- .NET SDK and target framework;
- FSharp.Core version;
- Fable compiler and `Fable.Package.SDK` versions;
- Node major version and JavaScript module target;
- compatibility-profile schema/version;
- fixture-schema version; and
- source commit and repository.

The checked-in .NET tool manifest and package lock files pin .NET/Fable
dependencies. The JavaScript host commits its package-manager lock file. CI
does not install an unbounded `latest`.

### Clean consumer test

The test packs `FS.GG.Game.Core`, copies only the `.nupkg` into an isolated
local feed, and creates or restores the consumer outside the repository source
tree. The consumer must compile using the package's supported Fable view and
execute under Node. The test fails if a project reference, repository-relative
source include, or undeclared feed is required.

The package archive is also inspected directly. At minimum it must contain the
ordered Fable project/source metadata, the compatibility profile, fixture
schema, and toolchain manifest expected for that package version.

## Canonical fixture protocol

### Principle

Both runners decode the same input corpus and independently emit canonical
binary records. CI compares the bytes. Human-readable JSON may describe a
failure, but formatted text is never the equality oracle.

### Corpus layout

```text
tests/Game.Core.Fable.Tests/
  fixtures/
    v1/
      cases.json
      expected.bin
  dotnet/
  fable/
  package-consumer/
```

`cases.json` is an authoring format with integer values only. It has an
explicit schema version and case order. The .NET runner produces
`actual-dotnet.bin`; the Node runner produces `actual-fable.bin`. Each must
equal `expected.bin`, and they must equal each other.

### Canonical record encoding v1

All integers use explicit widths and little-endian two's-complement encoding.
Strings are UTF-8 preceded by an unsigned 32-bit byte count. Lists are
preceded by an unsigned 32-bit element count and preserve the operation's
declared order. Optional values use a one-byte tag (`0` absent, `1` present).
Union cases use fixed unsigned 16-bit tags declared by the fixture schema.

Each output record is:

```text
u32 record-byte-count
u32 case-id
u16 operation-id
u16 outcome-tag
payload
```

The initial operation ids are fixed in the v1 schema:

| Id | Operation |
|---:|---|
| `1` | Cell ordering |
| `2` | `Edges.edgeBetween` |
| `3` | Selected LOS operation |
| `4` | Selected bounded pathfinding operation |

Adding a case does not change an existing record. Changing a tag, field order,
integer width, byte order, or the meaning of an existing operation requires a
new fixture-schema version.

### Boundary corpus

The minimum corpus covers:

- zero, negative, positive, minimum accepted, and maximum accepted coordinates;
- adjacent and non-adjacent cells in both argument orders;
- endpoint reversal and diagonal corner behavior for LOS;
- blocked, clear, unreachable, start-equals-goal, and visit-limit exhaustion;
- two equal-cost path choices whose winner depends on the documented total
  tie-break;
- walkability and wall sets inserted in adversarial orders;
- empty and degenerate collections; and
- arithmetic immediately inside and outside any declared safe coordinate
  range.

If a public function accepts all `int` values but cannot preserve its contract
at `Int32.MinValue` or `Int32.MaxValue`, M3 must either harden the producer
function or declare and enforce a narrower valid domain. A fixture must not
silently avoid the boundary.

## CI proposal

The producer gate runs these stages in order:

1. locked restore and normal .NET build/test;
2. pack `FS.GG.Game.Core`;
3. inspect the package and restore the isolated consumer;
4. execute canonical fixtures with the .NET runner;
5. compile the package consumer with the pinned Fable compiler;
6. execute the generated JavaScript under pinned Node;
7. compare both byte streams with `expected.bin`; and
8. upload the first-divergence diagnostics and toolchain manifest on failure.

A fixture mismatch reports the case id, operation id, first differing byte
offset, expected/actual hex around the offset, and each runner's toolchain
identity. It does not continue and report only a final digest.

The eventual M4 gate also tests the packed artifact that will be published,
not merely a project reference or repository source build.

## Compatibility and version policy

Adding the Fable package view and initial profile is a new consumer-observable
capability and therefore at least a SemVer minor while the package remains on
the `0.x` line. A normal .NET consumer must retain source and binary
compatibility within that declared release policy.

The compatibility profile has its own identity embedded in the package. An
output change to a `LockstepExact` row changes that identity even when the .NET
signature is unchanged. Retained replay engines must continue to bind to the
old identity; a current bundle cannot silently reinterpret them.

Changes to `Portable`, `DotNetOnly`, or unclassified functions follow the
normal package policy unless they also affect a profiled exact function.

## Producer and consumer acceptance

The producer promise is accepted only after:

- the supported package path compiles under pinned .NET and Fable toolchains;
- the packed artifact works without a sibling checkout;
- every exact row has canonical cross-runtime vectors;
- unsupported and floating-point surfaces are graded honestly;
- boundary, ordering, overflow, degenerate, and restore behavior is covered;
- normal .NET compatibility remains within policy; and
- a versioned package is live before the registry advertises it.

S.I.R. adopts only after:

- central package management pins the published producer version;
- its Fable build consumes the package's supported source path;
- S.I.R.'s Chebyshev movement, semantic-edge, footprint, and addressed-random
  adapters pass their own tests;
- canonical S.I.R. scenarios match in .NET and Fable; and
- restore, build, test, and documentation generation pass with the sibling
  repository absent.

## Rollout

1. M2 records this accepted spike strategy and tracks it on the Coordination
   board.
2. M3 implements the bounded package/compile/Node spike.
3. A failed package shape returns to M2 with a reduced reproduction; it does
   not grow the scope opportunistically.
4. M4 expands the inventory, publishes the versioned compatibility profile,
   and releases the producer package.
5. After publication, the FS-GG registry records the package/profile identity.
6. S.I.R. re-pins the published artifact and begins its numeric foundation.

Producer publication always precedes registry activation and consumer
adoption.

## Explicit non-decisions

This proposal does not:

- certify the whole assembly for lockstep use;
- make sequential `Rng` authoritative for S.I.R.;
- require floating-point equality;
- choose S.I.R.'s canonical state serialization or hash algorithm;
- make Fable or Node part of the authoritative server;
- promise browser execution of player WASM; or
- allow a permanent cross-repository project reference.

## References

- [Fable: Author a Fable package](https://fable.io/docs/your-fable-project/author-a-fable-library.html)
- [S.I.R. Fable client and documentation architecture](https://github.com/EHotwagner/S.I.R./blob/main/docs/fable-client-and-documentation.md)
- [FS.GG.Game#526](https://github.com/FS-GG/FS.GG.Game/issues/526)
