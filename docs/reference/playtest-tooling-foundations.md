# Playtest tooling foundations — relied-upon contract

> **Load-bearing.** The four `FS.GG.Game.Harness` primitives pinned below are relied upon by the
> headless-playtest-authoring machinery (the laws library, witness-finder, property runner, bot
> combinators, and the `fsgg-playtest` CLI — Phases 1–6 of
> [`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`](../reports/2026-07-19-headless-playtest-authoring-machinery-design.md)).
> Do not change any of them without updating the tooling that depends on it. The `.fsi` files remain
> the authoritative contract; this note is a relied-upon pointer, not a second source of truth.

This was the Phase 0 audit deliverable. The later production-journey boundary is additive and records
the distinction the original primitives could not express:

- `Playable` + `Driver` proves simulation/component behavior, including useful constructed-state and
  helper-level tests.
- `ProductionJourney` starts from the product boot function and crosses raw event mapping, dispatch,
  update, fixed ticks, and deterministic effect results.
- only its opaque `JourneyReceipt`, returned in-process by an executable
  `IProductionJourneyProof` and bound to route/scenario/test/script/trace/result/TRX, satisfies a manifest row marked
  `requires=production-journey`.

The original audit records
which already-shipped surface the tooling leans on, and why, so Phases 1–6 specify against a fixed base
and a future change to `Playable`/`Driver`/`Trace` is not made blind. See design report §3 ("What we
build on — the primitives already in place").

## 1. `Playable.Keymap` is the command alphabet

- **Surface:** `Playable.Keymap : Map<'key, Command>` — `src/Game.Harness/Playable.fsi`.
- **Invariant relied upon:** the *value-set* of the keymap is the complete set of `Command`s an input
  can produce for a game. A search or a generator needs no extra declaration to know the move set —
  it reads the alphabet straight off `Keymap` values (plus the empty frame). `Playable.resolve` is the
  raw-key → `Command` resolution; an unbound token yields `None` and is dropped, never applied raw.
- **Dependents:** the witness-finder (Phase 2) enumerates edges from this alphabet; the FsCheck
  property runner (Phase 3) generates random valid scripts over it. If `Keymap` stopped being the
  authoritative command alphabet (e.g. commands reachable by some route *not* in the keymap), both
  would explore an incomplete move set and silently under-cover.

## 2. The fingerprint `'world -> 'f` is the canonical visited-set key

- **Surface:** the fingerprint projection `'world -> 'f` threaded through the drivers, with
  `Driver.identityFingerprint : 'world -> 'world` as the full-world default —
  `src/Game.Harness/Driver.fsi`. A `Trace<'f>` records `fingerprint world` after each step.
- **Invariant relied upon:** `'f : comparison` is a canonical key for a world state — two worlds with
  the same fingerprint are "the same state" for dedup purposes, and the projection form lets an author
  collapse render-irrelevant fields out of the key. This is exactly the visited-set key a graph search
  needs, for free.
- **Dependent:** the witness-finder / `Explore.reachable` (Phase 2) deduplicates its BFS frontier by
  fingerprint and measures a fixture's reachable state space as a `Set<'f>`. Without a canonical,
  caller-controllable fingerprint key the search could neither terminate on revisits nor let the author
  tune the granularity of "same state".

## 3. Determinism is total and ambient-free

- **Surface / enforcement:** no wall clock, no ambient RNG — randomness is a threaded value `Rng`
  (seeded SplitMix64 from `Game.Core`), and all time is the caller's fixed `Playable.Dt`. This is
  enforced by `tests/Game.Harness.Tests/DependencyTests.fs`, which scans the harness assembly's IL
  type references and fails on any `System.IO.*`, `System.Net.*`, `System.Diagnostics.Stopwatch`,
  `System.DateTime`/`DateTimeOffset`/`Random`/`Environment`/`TimeProvider` reference (FR-007 of WI-007).
- **Invariant relied upon:** any search, replay, or property run over a `Playable` is itself
  reproducible — the same search returns the same witness every time, and a captured run replays
  byte-identically.
- **Dependents:** the laws library (Phase 1) *checks* determinism/replay as metamorphic laws rather
  than assuming it, and `Trace.firstDivergence` is only meaningful because a re-run is expected to be
  identical; the property runner (Phase 3) relies on reproducibility to shrink to a stable minimal
  counter-script. If an ambient clock or RNG leaked in, every one of these would become flaky.

## 4. FsCheck is already available to the test project

- **Surface:** `<PackageReference Include="FsCheck" />` in
  `tests/Game.Harness.Tests/Game.Harness.Tests.fsproj`.
- **Invariant relied upon:** property-based generation is available in the harness test project today.
- **Dependent:** the FsCheck property runner (Phase 3) generates random valid scripts over the `Keymap`
  alphabet and needs **no new package** — it builds on this existing reference.

## Load-bearing summary

| # | Primitive | Surface | Enforced/located by | Phase dependents |
|---|-----------|---------|---------------------|------------------|
| 1 | Keymap-as-command-alphabet | `Playable.Keymap` | `Playable.fsi` | 2, 3 |
| 2 | Fingerprint-as-visited-key | `'world -> 'f` / `Driver.identityFingerprint` | `Driver.fsi` | 2 |
| 3 | Ambient-free determinism | no clock / no ambient RNG | `DependencyTests.fs` | 1, 3 |
| 4 | FsCheck availability | `FsCheck` `PackageReference` | `Game.Harness.Tests.fsproj` | 3 |

## Production-journey trust boundary

`Origin.InputDriven` is retained for compatibility but means simulation/component input evidence.
`Origin.ProductionJourney` is minted only by `Journey.runScript`/`runPolicy`. The shipped
the separate `FS.GG.Game.Reference` application supplies the positive reference
boot/map/update/tick/effect composition root; the test suite imports it instead of rebuilding a
purpose-made adapter. A displayed event which
the production mapper does not bind fails explicitly, and a bounded run which misses its terminal
predicate fails with final-state and captured-input digests. `fsgg-playtest` validates the rendered
executable proof's opaque in-memory receipt and matching TRX identity; JSON, caller keys, and
hand-authored provenance text cannot upgrade a trace. Script and trace digests length-frame every encoded value, so an embedded
newline cannot collide with an event boundary. A complete critic artifact is also mandatory for
production rows; missing, mismatched, unsupported, or ambiguous AC rows veto completion.

### Serialized journey receipt schema v1

`fsgg-playtest emit-evidence` embeds a `journeyReceipt` map only after it has loaded an
`IProductionJourneyProof`, executed it in-process, validated the opaque receipt, and found exactly one
matching passing test in the supplied TRX. The CLI has no receipt-file or provenance-token input.
Consequently caller-authored JSON/YAML, a `Playable` trace, or a constructed mid-game state cannot
acquire runner-issued disposition by copying these fields.

Schema v1 contains:

| Field | Binding |
| --- | --- |
| `schemaVersion` | integer `1`; consumers fail closed on every other value |
| `runner.identity`, `runner.version` | `FS.GG.Game.Harness.Journey` and the issuing assembly version |
| `origin` | literal `production-journey`; every other value is non-satisfying |
| `routeId`, `scenarioId`, `testId` | producer route, scenario, and exact proof-test identity |
| `input.kind`, `input.digest` | `fixed-script` or `seeded-policy` and its SHA-256 definition binding |
| `replayDigest`, `traceDigest` | captured event replay and emitted fingerprint-trace SHA-256 digests |
| `initialFingerprint`, `terminalFingerprint` | SHA-256 identities of boot and terminal lifecycle states |
| `terminalPredicate.reached`, `outcome` | terminal predicate result and runner outcome |
| `maximumSteps`, `actualSteps` | declared bound and consumed event count |
| `observedTestReport` | source, byte-exact SHA-256 digest, exact test name, and passing outcome |

All digests render as `sha256:<lowercase-hex>`. SDD/lifecycle consumers must reject a missing field,
unknown schema, non-production origin, non-passing or unreached terminal outcome, `actualSteps` beyond
`maximumSteps`, an unexpected route/scenario/test identity, any digest mismatch, a stale or
non-matching report, or more than one report test matching `testId`. They must never treat the map's
presence alone as authenticity: provenance is issued by this executable runner route and the map is
the transport binding for that already-validated receipt.
