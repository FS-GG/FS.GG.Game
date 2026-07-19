# Headless playtest authoring machinery — design

- **Date**: 2026-07-19
- **Repo**: FS.GG.Game
- **Origin**: [#422](https://github.com/FS-GG/FS.GG.Game/issues/422) — WI-7, the reference-game obligation-chain proof (epic [FS-GG/.github#1190](https://github.com/FS-GG/.github/issues/1190))
- **Package**: `FS.GG.Game.Harness` (additive), plus a small `fsgg-playtest`-adjacent tool surface
- **Status**: proposed

---

## 1. TL;DR

`FS.GG.Game.Harness` gives an agent everything it needs to *drive* a game headlessly — `Playable`,
`Driver.runScript`/`runBot`, `Bot`, `Matrix`, value-fingerprinted `Trace`, and the typed `Synthetic`
hatch. What it does **not** give is help *authoring the proofs* in the first place. Writing the WI-7
reference proof for a single, already-existing integer-grid Pong fixture surfaced four separate
manual steps, none of which the harness assists with today:

1. **Discovering the sim's dynamics** — I could not predict a trace's values, so I wrote a throwaway
   `dotnet fsi` probe to read them off the running sim before I could assert on them.
2. **Discovering reachability** — the single-seat `playable` turned out to be degenerate (the ball
   never reached the paddle under my control). I only found this by running it.
3. **Reaching an isolated state non-synthetically** — to prove a deflection or a wall bounce I
   hand-built initial world states (`{ I with BallX = 1; … }`) and *hoped* they were reachable and
   valid, when the whole point of the exercise is that only `InputDriven` evidence satisfies.
4. **Calibrating goldens** — run, read the failure, transcribe the literal, repeat.

Every one of these scales badly to an *arbitrary* game. This document proposes six pieces of
machinery — one keystone and five supports — that turn "prove this game plays correctly" from a
manual `fsi`-probe exercise into a mostly-mechanical one, and it sequences them in a roadmap (§6).

The keystone is a **witness-finder**: a bounded search over `Playable` that returns the shortest
*input script from `Init`* reaching a caller's goal predicate. It collapses problems 2 and 3 at once
— and because its witness is a real path from `Init`, the evidence it produces is `InputDriven` **by
construction**, dissolving the reachable-vs-synthetic tension that is the whole reason the proof is
hard.

## 2. The bugs and frictions this machinery exists to remove

Grounded in the WI-7 authoring session, because a tool justified by a real failure is a tool that
gets used.

**The unpredictable trace.** A discrete sim is deterministic, but "deterministic" is not "obvious".
`PongSim.step` folds wall bounces, paddle reflection, scoring, and a seeded re-serve into one
transition; the resulting frame sequence is not something an author reads off the source. I wrote an
`fsi` probe (`#r` the built dlls, dump frames) to *see* the dynamics. That probe is disposable,
untracked, and rebuilt from scratch by the next author of the next game. It should be a first-class,
reusable capability.

**The degenerate fixture, discovered late.** The single-seat `playable` sends the ball rightward
forever; the left paddle it exposes never sees the ball, so `chaseBot` chasing it is a no-op and the
"left player" racks up points by the ball escaping the *right* wall. Nothing about the `Playable`
value announces this. An author who trusts the fixture writes a "deflection" test that never
exercises a deflection. There is today no way to ask "is state Y reachable from `Init` under any
input?" — the question whose answer would have flagged this immediately.

**The hand-built state that might be a lie.** To isolate a mechanic I constructed
`{ I with BallX = 1; BallY = 5; BallDX = -1; … }`. This is *not* the synthetic hatch — I drove it
through the real `step` — but it is a hand-authored **initial** state, and for a richer world type it
is easy to build a state the game's own rules can never produce (an invalid `Rng`, an inconsistent
score/serve pairing, two entities in one cell). A proof that starts from an unreachable state proves
nothing about the shipped game, and nothing catches it.

**The transcribed golden.** `"9,5,1,-1,3,0,0|10,4,…"` — I read these off the probe and pasted them
into an `Expect.equal`. Every transcription is an opportunity to pin the wrong value, and every sim
change is a manual re-transcription with no diff to guide it.

## 3. What we build on — the primitives already in place

The proposals below are deliberately **additive**: each is a thin layer over surface the harness
already exposes. That is what keeps them cheap and keeps `Game.Core` a pure vocabulary.

- **`Playable<'world,'key>`** already carries the *whole* drivable contract: `Init`, `Keymap`,
  `Apply`, `Step`, `Dt`. In particular `Keymap` *is* the command alphabet — the set of `Command`s an
  input can produce — so a search or a generator needs no extra declaration to know the move set.
- **The fingerprint** (`'world -> 'f`, default `identityFingerprint`) is exactly the **canonical key**
  a visited-set needs for graph search, and the projection form lets an author collapse
  render-irrelevant fields out of the key for free.
- **Determinism is total and ambient-free** (no clock, no ambient RNG — enforced by
  `DependencyTests`), so any search or replay over a `Playable` is itself reproducible: the same
  search returns the same witness, every time.
- **`Matrix`** is already a batch, order-free runner — the shape a property/law runner generalizes.
- **`Trace` + `Origin`** already separate provenance from frames, so a laws library can assert
  non-syntheticity as just another law.
- **FsCheck is already a referenced dependency** of the test project — property generation needs no
  new package.

## 4. The features

### 4.1 Witness-finder / reachability explorer — the keystone

**Surface (proposed, `Explore` module in `FS.GG.Game.Harness`):**

```fsharp
/// Search inputs from `Init` for the shortest raw-key (or command) script that drives the world to a
/// state satisfying `goal`. Bounded by `maxDepth`; `None` if no witness exists within the bound.
val findScript:
    playable: Playable<'world,'key> ->
    goal: ('world -> bool) ->
    maxDepth: int ->
        Command list list option

/// The reachable frontier: every distinct fingerprint reachable within `maxDepth`, for answering
/// "can the game ever be in a state like this?" and for measuring a fixture's real state space.
val reachable:
    playable: Playable<'world,'key> ->
    fingerprint: ('world -> 'f) ->
    maxDepth: int ->
        Set<'f> when 'f: comparison
```

**Mechanism.** Breadth-first (or iterative-deepening) search over the graph whose nodes are worlds,
whose edges are "apply a subset of the command alphabet, then one fixed `Step`", deduplicated by
`fingerprint`. The alphabet is read from `Keymap` values (plus the empty frame). BFS gives the
*shortest* witness, which is what an author wants — the minimal script to a mechanic.

**What it removes.** Problems 2 and 3 in one stroke:
- *"Is state Y reachable?"* → `findScript p (fun w -> …) n |> Option.isSome`. The degenerate-fixture
  discovery becomes a one-liner run at authoring time.
- *"Give me a non-synthetic path to a deflection."* → `findScript p (fun w -> w.BallDX = 1 && wasApproachingPaddle w) n`. The returned script drives from `Init` through the real frontier, so the
  evidence is `InputDriven` by construction — no hand-built state, no reachability gamble.

**Synergies.** The fingerprint is the visited-key for free; determinism makes the search
reproducible; the shortest-path property makes witnesses stable across re-runs (so a witness can be
committed as a golden). Because it is pure over `Playable`, it lives in the package with zero new
dependencies.

**Limits, stated honestly.** State explosion is real for a large world type. Three mitigations, all
using existing primitives: (a) search over a **projected fingerprint**, not the whole world, so the
author controls the granularity of "same state"; (b) a hard `maxDepth` and a visited-set cap that
*fails loudly* (`Explore` reports "frontier truncated at N" rather than silently returning `None` —
the #266 fail-closed rule); (c) allow a restricted alphabet (a subset of `Keymap`) when the author
knows only a few keys matter. The search is a **witness finder, not a prover** — a `None` within a
bound is "not found within N", never "impossible", and the API name and docs must say so.

### 4.2 Reusable law library — the free proofs every game shares

Several gameplay obligations are *identical* for every `Playable`. Package them so an author gets
them for one line instead of re-deriving them per game (as `DriverTests`/`MatrixTests`/`BotTests`
each do today):

```fsharp
/// The metamorphic laws every deterministic Playable must satisfy. Returns a per-law pass/fail
/// report; every trace it builds is Origin.InputDriven, so `nonSynthetic` is a law it checks, not an
/// assumption it makes.
val laws:
    playable: Playable<'world,'key> ->
    sampleScripts: 'key list list list ->
        LawReport when 'world: equality
```

Laws checked: **determinism** (same seed + script → equal frames), **replay** (a captured `runBot`
playthrough replays byte-identical through `runCommands`), **matrix order-independence** (permuting
matches permutes outcomes identically), **fixed-step discipline** (one whole step per frame, constant
`Dt`), and **provenance** (`not (Trace.isSynthetic t)` for every driven trace). Instantiating `laws`
proves 4–5 FRs an author would otherwise write by hand — exactly the FRs WI-7's GP-001/GP-010 restate
per game.

Pair it with FsCheck: generate random *valid* scripts over the `Keymap` alphabet and assert
author-supplied **invariants** (`'world -> bool`, e.g. "the ball is always on the playfield") hold at
*every step of every generated run* — turning WI-7's "bounds over one 16-step run" into "bounds over
thousands of runs".

### 4.3 Trace diff + golden management — kill the transcription loop

```fsharp
/// The first step at which two traces diverge, with the differing fingerprints — or None if equal.
val Trace.firstDivergence: a: Trace<'f> -> b: Trace<'f> -> (int * 'f * 'f) option when 'f: equality

/// A stable, human-readable dump of a trace's frames in step order (for a probe-free look).
val Trace.render: show: ('f -> string) -> trace: Trace<'f> -> string
```

Plus a **golden record/update mode** in the test harness (an env flag or a small helper) so a golden
is *captured from a green run* and rewritten on an intended change, never hand-transcribed. This is
the disposable `fsi` probe (problem 1) and the transcription step (problem 4), promoted to supported
API. `firstDivergence` also makes a failing determinism/replay law *actionable*: "diverged at step 7,
`LeftY` was 3, now 2" instead of "two lists were not equal".

### 4.4 FR-manifest scaffolding + coverage lint from the TestSpec corpus

The `docs/TestSpecs/Games/*.md` §14 acceptance criteria are structured Given/When/Then. A small
generator reads them and emits a `GameplayFr` manifest stub — the exact shape WI-7 hand-authored
(`Id`, `gameplay` facet, `CoversAc`, `Summary`) — turning the AC list into a checklist of
proofs-to-write:

```
fsgg-playtest scaffold-manifest --spec docs/TestSpecs/Games/snake.md  →  a GP-### stub per §14 AC
```

Paired with a **coverage lint** that, given the manifest and the test assembly, reports which cited
ACs have a backing `InputDriven` proof and which are still stubs — the completeness critic that stops
a proof from silently covering 6 of 19 ACs. This automates the FR→AC mapping I did by hand and makes
its totality machine-checkable (the WI-7 "manifest coverage is total" test, generalized).

### 4.5 The evidence bridge to the SDD gate

Today the harness (the *capability*) and the SDD per-FR gate (`fsgg-sdd evidence`/`verify`, the
*decision*) are stitched together by a human authoring `evidence.yml`. Close the seam:

```
fsgg-playtest emit-evidence --trx artifacts/testresults/playtest.trx --manifest …  →  evidence.yml rows
```

Run the playtest suite, read each proof test's `Trace.origin`, and generate the per-FR `evidence.yml`
entries with `result: pass`, **`synthetic: false`**, `requirementRefs`, and the TRX `observedRun`
receipt — the entries that make the ADR-0048 gate green. Crucially, an FR whose proof trace is
`Origin.Synthetic` is emitted `synthetic: true` (disclosed, non-satisfying), so the *tool*, not the
author, enforces the satisfaction rule. This is the piece that makes "the obligation chain is green"
a generated fact rather than a hand-maintained one.

### 4.6 Bot combinators — make a policy a one-liner

Bot authoring is repetitive (`chaseBot`/`sitterBot` are re-invented per game). A small combinator
library over the existing `Bot<'view>`:

```fsharp
val Bots.sitter: Bot<'view>
val Bots.scripted: Command list list -> Bot<'view>
val Bots.random: (Rng -> struct (Command list * Rng)) -> Bot<'view>
val Bots.chase: (('view -> int) * ('view -> int)) -> up: Command -> down: Command -> Bot<'view>
val Bots.greedyToward: score: ('view -> Command -> float) -> moves: Command list -> Bot<'view>
```

Plus view-projection helpers that keep a policy honestly blind (the `Ai.TeamView` fog boundary the
`fs-gg-playtest` skill already points at). Low risk, slots in anywhere, and shrinks every future
matrix/balance proof.

## 5. Cross-cutting concerns

- **Non-synthetic is the invariant, everywhere.** Every generator here (witness scripts, FsCheck
  scripts, bot playthroughs) yields `InputDriven` evidence because it drives from `Init` through the
  real frontier. `Synthetic` remains the single, typed, disclosed exception. No tool in this
  document may produce evidence that *looks* satisfying but is synthetic — that is the one line the
  whole obligation chain rests on.
- **Fail closed.** `Explore` truncation, an unreadable manifest, a missing TRX — each must report
  "could not determine", never a confident empty/pass (#266). A coverage lint that cannot see a
  proof reports it as *uncovered*, not covered.
- **Determinism is preserved, not assumed.** The laws library *checks* determinism rather than
  trusting it; the witness-finder is reproducible *because* the sim is clock/RNG-clean, and the laws
  library is exactly what catches a game that broke that.
- **Package boundary.** `Explore`, `laws`, `Bots`, and the `Trace` additions are pure and belong in
  `FS.GG.Game.Harness` (Core + BCL only). The corpus/evidence tooling (§4.4, §4.5) reads files and
  talks to `fsgg-sdd`, so it belongs in a **separate tool** (an `fsgg-playtest` CLI), not in the
  pure package — the same split the harness already keeps between capability and gate.
- **Dogfood target.** Every piece lands with `PongSim` as its first consumer and a second reference
  game (Snake, from the corpus) as the generalization check — the same "prove it on two reference
  sims" discipline WI-7 established.

## 6. Roadmap

Sequenced by dependency and by leverage-per-unit-effort. Each item names its deliverable, what it
depends on, its surface impact (all additive), a rough size, and an acceptance check that is itself a
dogfood proof. The ordering front-loads the cheap, immediately-useful pieces and puts the keystone
early enough that everything downstream can lean on it.

### Phase 0 — foundations (no build; audit only)
Confirm the primitives §3 relies on are stable contract: `Keymap`-as-alphabet, the fingerprint as a
visited-key, ambient-free determinism, FsCheck availability. **Deliverable:** a one-page contract
note pinning these as relied-upon (so a future change to `Playable` knows it is load-bearing for the
tooling). **Size:** S. **Acceptance:** the note lands; no surface change.

### Phase 1 — laws library + trace diff (independent, highest immediate value)
The cheapest win: the metamorphic laws (§4.2) and `Trace.firstDivergence`/`Trace.render` (§4.3). No
search, no corpus, no external tool — pure additions over existing drivers.
- **Depends on:** Phase 0.
- **Surface:** `Playtest.Laws` module + 2 `Trace` functions in `FS.GG.Game.Harness` (additive, tier 1).
- **Size:** M.
- **Acceptance:** `PongSim` proves determinism/replay/order-free/fixed-step/provenance via `laws`
  with ≤5 lines; a deliberately clock-poisoned `Step` is *caught* by the determinism law with a
  `firstDivergence` step index. Retrofit WI-7's GP-001/GP-010 onto `laws` and delete the hand rolls.

### Phase 2 — the witness-finder (the keystone)
`Explore.findScript`/`reachable` (§4.1). Everything about authoring an *arbitrary* game's mechanic
proof gets easier once this exists.
- **Depends on:** Phase 0 (fingerprint-as-key).
- **Surface:** `Explore` module in `FS.GG.Game.Harness` (additive, tier 1).
- **Size:** M–L (the search is small; the truncation/limit/fail-closed reporting and the depth/alphabet
  controls are where the care goes).
- **Acceptance:** on `PongSim`, `findScript` produces a non-synthetic script that reaches a genuine
  left-paddle deflection (the exact state WI-7 had to *hand-construct*); `reachable` measured on the
  single-seat `playable` demonstrates the degenerate frontier (the ball never on the left half),
  reproducing the fixture-degeneracy finding automatically.

### Phase 3 — FsCheck property runner
Random valid scripts over the `Keymap` alphabet + author invariants, checked at every step (§4.2,
second half). Builds directly on Phase 1's alphabet/laws.
- **Depends on:** Phase 1.
- **Surface:** `Playtest.Properties` module (additive, tier 1).
- **Size:** M.
- **Acceptance:** `PongSim`'s "ball always on the playfield" and "velocity magnitude is always unit"
  hold across ≥1000 generated runs; a seeded shrink produces a minimal counter-script when an
  invariant is deliberately broken.

### Phase 4 — bot combinators
`Bots.*` (§4.6). Independent of Phases 2–3; scheduled here because Phase 3's balance properties are
its most natural first consumer.
- **Depends on:** Phase 0.
- **Surface:** `Bots` module + view-projection helpers (additive, tier 1).
- **Size:** S–M.
- **Acceptance:** `PongSim`'s `chaseBot`/`sitterBot` re-expressed as `Bots.chase`/`Bots.sitter` with
  identical match outcomes; a second game reuses them with zero bespoke bot code.

### Phase 5 — FR-manifest scaffolding + coverage lint
`fsgg-playtest scaffold-manifest` + coverage lint over the TestSpec corpus (§4.4). First item that
lives in a **separate tool**, not the pure package.
- **Depends on:** Phase 1 (a proof is a driven trace it can inspect) and the `GameplayFr` shape WI-7
  established.
- **Surface:** new `fsgg-playtest` CLI (read-only over `docs/TestSpecs` + the test assembly).
- **Size:** M.
- **Acceptance:** scaffolding `snake.md` §14 emits one GP-### stub per AC; the coverage lint reports
  the WI-7 Pong manifest as fully covered and a deliberately-removed proof as an uncovered AC.

### Phase 6 — the evidence bridge
`fsgg-playtest emit-evidence` (§4.5), closing the loop to `fsgg-sdd verify`.
- **Depends on:** Phase 5 (the manifest) and a stable `fsgg-sdd evidence` grammar.
- **Surface:** an `fsgg-playtest` subcommand emitting `evidence.yml` rows (writes an authored artifact
  the SDD lifecycle then owns).
- **Size:** M–L (the care is in reading `Origin` → `synthetic:` faithfully and in the TRX receipt).
- **Acceptance:** running it on a reference game produces an `evidence.yml` that `fsgg-sdd verify`
  accepts as green, and a proof whose trace is forced `Origin.Synthetic` is emitted `synthetic: true`
  and *rejected* by `verify` — the full WI-7 chain, generated rather than hand-authored, with the
  synthetic shortcut refused end to end.

### Sequencing summary

```
Phase 0 ─┬─> Phase 1 (laws + trace diff) ─┬─> Phase 3 (FsCheck properties)
         │                                └─> Phase 5 (manifest + lint) ─> Phase 6 (evidence bridge)
         ├─> Phase 2 (witness-finder)  ← the keystone; unblocks arbitrary-game mechanic proofs
         └─> Phase 4 (bot combinators)
```

Phases 1, 2, and 4 are independent and can land in parallel; 1 is the fastest payoff, 2 is the
highest ceiling. 3 → 5 → 6 is the chain that carries a reference game all the way from "described as a
`Playable`" to "green in the SDD gate" with the human only writing the `Playable` and the invariants.

## 7. Non-goals

- **Not a general game-playing AI.** `Bots` and the witness-finder produce *test inputs*, not a
  competitive agent. `greedyToward` is a heuristic for reaching states, not for winning.
- **Not a theorem prover.** The witness-finder finds witnesses within a bound; a `None` is never a
  proof of impossibility, and the laws library checks metamorphic properties, not full functional
  correctness.
- **Not a physics/float story.** The corpus's float-physics ACs (sub-step tunneling, launch angles)
  are out of scope for the integer reference sims; this machinery serves the discrete, value-fingerprintable
  games the harness is built for. A float game brings tolerance back and is a separate design.
- **Not a change to `Game.Core`.** Every proposal is additive to `FS.GG.Game.Harness` or a new
  sibling tool; `Game.Core` stays a pure vocabulary.
