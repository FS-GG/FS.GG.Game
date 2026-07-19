# Playtest tooling foundations — relied-upon contract

> **Load-bearing.** The four `FS.GG.Game.Harness` primitives pinned below are relied upon by the
> headless-playtest-authoring machinery (the laws library, witness-finder, property runner, bot
> combinators, and the `fsgg-playtest` CLI — Phases 1–6 of
> [`docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`](../reports/2026-07-19-headless-playtest-authoring-machinery-design.md)).
> Do not change any of them without updating the tooling that depends on it. The `.fsi` files remain
> the authoritative contract; this note is a relied-upon pointer, not a second source of truth.

This is the Phase 0 audit deliverable. It builds nothing and changes no public surface — it records
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
