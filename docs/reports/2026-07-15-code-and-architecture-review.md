# Code & Architecture Review — FS.GG.Game

- **Date:** 2026-07-15
- **Repo:** FS.GG.Game
- **Scope:** whole repo — `src/Game.Core` (19 modules), `src/Game.Render`, the test suite, the build/CI system, and the governance/tooling apparatus
- **Method:** five independent deep-reads (collision/physics, navigation/AI, foundation+render, tests, build/CI/governance), each reading `.fsi` contract + `.fs` implementation, cross-checked against the module contracts and this repo's own claims
- **Status:** review only — no source changed

## 1. Verdict

The F# code is genuinely excellent — among the more rigorous small libraries this review has seen. The
"pure and total, deterministic" claims are not marketing: they hold up under adversarial reading across
all 19 modules, and the test suite verifies them as *documented degradation*, not merely "does not
throw." Across five independent deep-reads the reviewers found **one real HIGH correctness bug**, a
handful of Mediums, and otherwise Low / documentation-level items.

The tension in this repo is not code quality. It is **proportionality**: the governance / tooling / docs
apparatus (~27k lines) outweighs the simulation code (~4.3k lines of `.fs`) by roughly **6:1**.

## 2. Architecture — sound and actually enforced

The core design is correct and its invariants are enforced, not merely asserted.

- **The one-way dependency rule holds.** `Game.Core` references `FS.GG.UI.*` / Scene / Skia *only in
  comments*. Verified: `FS.GG.Game.Core.fsproj` takes FSharp.Core and nothing else; `Game.Render` is the
  sole component reaching *up* to Scene / KeyboardInput. This is the load-bearing claim of ADR-0022 and
  it is clean.
- **`.fsi`-as-sole-surface plus committed surface baselines** (type-level *and* member-level under
  `readiness/surface-baselines/members/`) is a strong, well-built drift detector — a deleted or
  re-signatured public function drifts the baseline and reds CI.
- **Detection / response separation** (`Geometry` vs `Resolution`), **opt-in `Physics`** kept orthogonal
  to the default arcade `Resolution`, and **policy-stays-with-the-game** seams (LOS as a caller oracle in
  `Ai`, walkability predicates in `Pathfinding`) are principled boundaries that keep the core reusable.
- **The render adapter is genuinely thin and pure** — field-relabels, or delegations to `Grids` that
  inherit its totality, preserving caller enumeration order for determinism.

The compile-order rationale in the `.fsproj` and the overall layering are coherent.

## 3. Correctness findings (prioritized)

### HIGH — `Pathfinding.heuristic` throws `OverflowException` on in-domain input

`src/Game.Core/Pathfinding.fs:76-77`

```fsharp
let dx = abs (c.Col - goal.Col)   // int subtraction wraps; abs(Int32.MinValue) THROWS
let dy = abs (c.Row - goal.Row)
```

A genuine totality violation in a module that advertises "unbounded integer cell space" and "pure and
total." `c.Col - goal.Col` can wrap to `Int32.MinValue` for far-apart cells, and `abs(Int32.MinValue)`
throws — reproduced end-to-end (`start = {0,0}`, `goal = {Int32.MinValue,0}`). The smoking gun: the
sibling modules already guard this exact class — `Los.fs:16-17` and `Ai.fs:149-150` compute deltas in
`int64` — and `Pathfinding.heuristic` simply was not given the same treatment. Affects `astar` only
(BFS / flow / distance never call `heuristic`). Fix is a one-liner: compute deltas in `int64`. Rated
High rather than Critical only because triggering it needs coordinates spanning nearly the full `int32`
range.

### MEDIUM — `astar` g-score accumulates in unguarded `int32`

`src/Game.Core/Pathfinding.fs:80,84,116,140` — the same overflow theme. The many-to-one `dijkstra`
engine deliberately accumulates in `int64` and drops relaxations exceeding `Int32.MaxValue` (documented
in the `.fsi`); `astar` does not. Two silent-corruption modes: (a) `baseStep * (dx + dy)` overflows at
coordinates only ~1e9 apart, producing a non-admissible heuristic and hence a **silently non-optimal**
path; (b) accumulated `g` on a very long path wraps negative, corrupting the frontier key. The `int64`
fix for the HIGH finding closes both.

### MEDIUM — `Rng.split` overstates independence

`src/Game.Core/Rng.fs:49-51` and `Rng.fsi:35`. Sub-streams share a single `gamma`, so parent and child
are phase-shifted copies of one sequence, not statistically independent. Canonical SplitMix64 `split()`
derives a *distinct per-child gamma* precisely to avoid this. The contract word "independent" claims more
than the code delivers. Practical overlap risk is ~2⁻⁶⁴ (negligible for a game), but for a library whose
selling point is a rigorously-specified PRNG, either derive a distinct child gamma or soften the contract
to "practically decorrelated (shared gamma; not the paper's independent-gamma split)."

### Notable Lows (design-scoped, not bugs)

- **CW-wound polygons produce wrong contact *points*** (correct normal / depth) in
  `Geometry.polygonManifold` — `faceQuery` / incident-face use `edgeNormalAt`, outward-only for CCW
  (`Geometry.fs:345-384`). The trap: the same `Physics` module tolerates either winding for mass and
  circle-vs-poly contacts, so a CW body is accepted with correct mass but corrupted poly-vs-poly contact
  points / warm-start keys. CCW is a documented (unenforced) input assumption.
- **Circle-vs-poly speculative CCD can tunnel past a corner** — `speculativeContact`
  (`Physics.fs:877-934`) casts the mover *center* against the *bare* polygon; a fast circle clipping a
  corner without its center entering the interior gets no speculative contact. The `.fsi`'s "prevents
  tunneling through a thin wall" is slightly stronger than delivered; the discrete phase recovers next
  tick.
- **Two contact producers disagree on coincident circle centres** — `Geometry.circleContact`
  (`Geometry.fs:70-71`) invents `Normal = (1,0)`; `Physics.circleCircleManifold` (`Physics.fs:542-543`)
  returns `ValueNone`, calling the invented normal "unphysical bias." Both documented; worth a
  cross-reference.
- **`FixedStep` residual invariant**: when the step count saturates at `Int32.MaxValue`
  (`FixedStep.fs:35`), `newAccumulator` can exceed `interval`, violating the documented
  `0 <= acc < interval` (pathological tiny-interval case; stays finite and non-negative).
- **`Ai.known` non-tail-recursive merge** (`Ai.fs:74-80`) and **`nextInt` residual modulo bias**
  (`Rng.fs:40`) — both practically irrelevant; worth a doc note.
- **`Visibility.polygon` vertex order is input-order-dependent** for hits at identical angle *and*
  distance (`Visibility.fs:316-319`); within contract (no permutation-invariance is claimed) but worth a
  docstring note since the polygon is advertised as golden-testable.

Everything else the reviewers traced by hand was **verified correct**: SplitMix64 constants and mixing,
the accumulator-drain guards (NaN / negative / spiral-of-death cap / step-count saturation), `alpha`
clamping, the impulse solver (accumulated-impulse clamping, warm-start linear merge, friction cone, sign
conventions), Fov symmetric shadowcasting (checked against Ford's reference), Los Bresenham / supercover,
the intercept quadratic (no division-by-zero, always-a-root guarantee), SpatialGrid false-negative
guards, `massProps` inertia, sleep / wake order-independence, and `checksum` canonicalization.

## 4. Test suite — a genuine strength

Expecto + FsCheck, unusually rigorous. The recurring discipline is what stands out: nearly every
behavioral assertion is paired with an **anti-tautology control that fails if the test passes for the
wrong reason** (Los asserts the *raw* walk is asymmetric before asserting the canonicalized one is
symmetric; Ai asserts the naive roster-position approach *fails*). Totality is tested as
documented-degradation with anti-vacuity guards; determinism via byte-identical repeats, literal goldens
for integer / grid modules, and cross-runtime checksum goldens for Physics. Public-surface coverage is
complete — every `val` in every `.fsi` is exercised.

**Highest-risk gaps**, all in the two most complex float modules:

- **HIGH:** Physics **Kinematic bodies are never `step`ped** — the whole contract ("moved by the game,
  unmoved by impulses, holds a load") is unverified through the solver; a body-kind's dynamics could
  regress silently.
- **HIGH:** Ballistics **intercept earliest-of-two-positive-roots selection is untested** (`.fsi:77`), as
  is "target already at shooter ⇒ zero root."
- **Medium:** Physics friction-combination rule untested with *differing* per-body μ (restitution's MAX
  rule *is* tested — an asymmetric hole); speculative CCD tested only with circles (box / poly tunneling —
  the feature's whole point — unverified); `step` totality on degenerate bodies unverified (only
  broad-phase `pairs` is).

Note the HIGH `Pathfinding.heuristic` overflow bug slipped through *despite* strong pathfinding tests —
the suite covers int32 *cost* overflow but never feeds near-`Int32.MinValue` *coordinates* to `astar`.

## 5. Build / CI / governance

**Build hygiene is excellent.** Central package management is done correctly, locked cold-restore
genuinely defends against record-vs-record false greens (the `DisableImplicitLibraryPacksFolder`
FSharp.Core-hash fix is real and correct), warnings-as-errors uses the correct append-form, and the lock
files are consistent across all four projects.

**Real technical gaps:**

- **[Medium] Cross-platform determinism is claimed but never tested.** Every CI job is `ubuntu-latest`;
  the README's strongest claim — "byte-identical across runs *and platforms*" — is exercised on no
  non-Linux runner.
- **[Medium] No `ContinuousIntegrationBuild=true`** — only `Deterministic=true`
  (`Directory.Build.props:17-19`). Released nupkgs skip full source-path reproducibility, weakening the
  dual-published artifact.
- **[Medium] Actions are tag-pinned, not SHA-pinned** — including `NuGet/login@v1` on the OIDC publish
  path and org reusable workflows pinned to mutable `@main`. Inconsistent with the meticulous
  checksum-pinning of the `actionlint` / `shellcheck` *binaries* in the same file — the supply-chain rigor
  stops exactly at the GitHub-hosted actions.
- **[Medium] The surface-baseline gate detects drift but never enforces the SemVer consequence** — it
  makes a break *visible in the diff* and relies on a human to bump major. The dedicated api-break gate is
  off by default *and a documented no-op on F#*. There is no automated SemVer-break enforcement; the
  mechanism is "visible diff," not "enforced bump."
- **[Low] `global.json` contradicts a load-bearing comment.** It pins SDK `10.0.301` with
  `rollForward: latestFeature`, but `Directory.Build.props:74-80` asserts as fact that "Game has NO
  global.json." That reasoning is now stale, and it justifies the hash-determinism fix — reconcile it.
- **[Low] No CodeQL / dependency-review / Dependabot** — low impact given the tiny dependency surface, but
  a gap.

### Proportionality — the honest assessment

| Layer | Lines |
|---|---|
| Implementation (`.fs`) | ~4,355 |
| Tests | 8,113 |
| Scripts (gate machinery) | 6,912 |
| CI workflows | 2,011 |
| Docs / work / readiness | 18,055 |

`gate.yml` is **1,123 lines, ~57% comments**, with individual comments running 40+ lines of PR-number
archaeology (#35 → #202 → #208 → #238 → …). There are **four descending meta-levels**: gates →
behavioural tests for the gates → a shared test-harness → a `harness-selftest` that black-box-tests the
harness.

**What is defensible — roughly half.** Much is org-shared and amortized across six FS-GG repos (the repo
pays a drift-check to stay in sync, not the full authoring cost). And this is not "just a library": it
publishes **12 teaching skills whose `fsharp` blocks readers copy verbatim**, so the
"compile the docs against the real assembly" gates guard a genuine documentation-as-code product a plain
library would not have. It is also explicit dogfooding of the org's SDD / governance stack (ADR-0022).

**What is genuinely over-engineered.** The fourth-level meta-testing (a selftest for the test-harness;
tests that PyYAML-parse a workflow to extract its own `run:` blocks) is watching-the-watchers past
marginal value for a 6.7k-line library, and the comment volume has become an artifact in its own right —
it signals a team that has turned CI hardening into an end. A leaner team would trim that tier.

## 6. Recommended actions, in priority order

1. **Fix `Pathfinding.heuristic` + `astar` g-score to `int64`** (one HIGH bug + one Medium, a single
   coherent fix mirroring `Los` / `Ai`). Add a regression test feeding near-`Int32.MinValue` coordinates
   to `astar`.
2. **Close the two HIGH test gaps:** step a Kinematic body through the solver; construct two-positive-root
   and zero-root intercept scenarios.
3. **Resolve `Rng.split`:** either derive a distinct child gamma (preferred, cheap) or soften the
   "independent" contract wording.
4. **Add a cross-platform (or at least Windows) CI leg** to actually exercise the platform-determinism
   claim, or scope the README claim down.
5. **SHA-pin actions on the publish path**, set `ContinuousIntegrationBuild=true`, and reconcile the
   `global.json`-vs-comment contradiction.
6. **Doc fixes:** the CW-winding limitation on `polygonManifold`; the corner-tunneling caveat on
   speculative CCD; the `circleContact` coincident-centre cross-reference; the `polygonContact`
   "centroid delta" prose (the code is actually more correct than the docstring); the `Visibility.polygon`
   ordering note.
7. **Consider trimming the meta-testing tier and comment archaeology** — a deliberate decision, given the
   dogfooding intent, but worth making consciously rather than by accretion.

## 7. Roadmap

Checkboxed and grouped by phase; each phase is independently shippable, and P0 → P2 are the load-bearing
ones for a released library. File references are the starting points, not the whole edit.

### P0 — correctness (ship first, one PR) — DONE 2026-07-15

- [x] Widen `Pathfinding.heuristic` deltas to `int64` to stop the `abs(Int32.MinValue)` throw —
      `src/Game.Core/Pathfinding.fs`, mirroring `Los`/`Ai`
- [x] Accumulate `astar` g-scores and the frontier key `f`/`h` in `int64` — `src/Game.Core/Pathfinding.fs`.
      (No Int32 cap needed, unlike `dijkstra`: astar never truncates g to `int` — it returns the path, not
      a cost — so the wider accumulator is the whole fix.)
- [x] Add regression tests feeding near-`Int32.MinValue` coordinates to `astar` — a far goal returns
      (no throw) and a reachable extreme-magnitude pair yields the optimal path — `PathfindingTests.fs`
- [x] Confirmed the surface baselines are unchanged (internal-only; no `.fsi` churn) — 577 core + 23
      render tests green, baseline regeneration reports zero drift

### P1 — close the HIGH test gaps — HIGH items DONE 2026-07-15

- [x] Step a `Kinematic` body through `Physics.step` — `PhysicsTests.fs`. Two tests: gravity/contacts do
      not move it (control: a Dynamic body falls), and it holds a dynamic load bit-identically without
      being pushed (control: a Dynamic floor moves). NB the "moved by the game" leg is unreachable through
      the public surface — there is no velocity setter; `addBody` seeds every body at rest and gravity is
      the only velocity source — so only its shadow (at-rest immunity) and the impulse/load legs are driven.
- [x] Intercept with two positive roots asserts the earliest is chosen — `BallisticsTests.fs`
      (`3t²−40t+100`, roots 10/3 vs 10, landing on opposite sides of the shooter so the choice is visible)
- [x] "target already at shooter ⇒ zero root" intercept case — `BallisticsTests.fs`
- [ ] (Medium, deferred to a follow-up) Friction-combination test with *differing* per-body μ;
      speculative-CCD test with a fast box/polygon (not just circles); `Physics.step` totality on
      degenerate/non-finite bodies

### P2 — contract truthfulness

- [ ] Resolve `Rng.split`: derive a distinct per-child gamma (preferred) **or** soften "independent" in
      `Rng.fsi:35` to "practically decorrelated (shared gamma)" — `src/Game.Core/Rng.fs:49-51`
- [ ] Note `nextInt`'s residual modulo bias in `Rng.fsi` (only `nextBool` claims unbiasedness)
- [ ] Document the `FixedStep` step-count-saturation case as the one exception to
      `0 <= newAccumulator < interval` — `FixedStep.fsi`

### P3 — build / CI hardening

- [ ] Add a Windows (or full cross-platform matrix) CI leg to actually exercise the
      "byte-identical across platforms" claim, **or** scope the README claim to Linux
- [ ] Set `ContinuousIntegrationBuild=true` for reproducible packed artifacts —
      `Directory.Build.local.props`
- [ ] SHA-pin third-party actions on the publish path (`NuGet/login@v1`, `actions/*`) and the org
      reusable workflows currently on `@main` — `.github/workflows/release.yml`, `gate.yml`
- [ ] Reconcile the `global.json` SDK pin against the "Game has NO global.json" comment —
      `Directory.Build.props:74-80`
- [ ] (Low) Evaluate CodeQL / dependency-review / Dependabot given the small dependency surface

### P4 — documentation fixes

- [ ] Document the CCW-winding requirement / CW contact-point limitation on `polygonManifold` —
      `Geometry.fsi`
- [ ] Add the corner-tunneling caveat to `speculativeContact`'s "prevents tunneling" prose —
      `Physics.fsi`
- [ ] Cross-reference `Geometry.circleContact` (invents `(1,0)`) vs `Physics.circleCircleManifold`
      (returns `ValueNone`) on coincident centres
- [ ] Fix the `polygonContact` "centroid delta" docstring to describe the exit-direction orientation the
      code actually uses — `Geometry.fsi`
- [ ] Note `Visibility.polygon`'s input-order dependence for coincident hits — `Visibility.fsi`

### P5 — governance right-sizing (deliberate decision, not a defect)

- [ ] Decide, consciously, whether the fourth-level meta-testing tier (`harness-selftest`,
      workflow-self-parsing sweep tests) earns its keep for a library this size
- [ ] Trim the PR-lineage comment archaeology in `gate.yml` to what a future reader needs, moving the
      history to commit messages / ADRs

## 8. Bottom line

The simulation core is high-integrity, well-tested engineering with one fixable overflow bug and a small
set of documented-scope caveats. The governance layer is impressively built but has crossed from thorough
into self-referential for a library this size — the code deserves more trust than the process weight
around it implies.
