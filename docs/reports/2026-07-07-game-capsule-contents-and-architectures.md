# Game capsule — contents, architectures, and example-code plan

> **New design (2026-07-07).** Authored here, not relocated. Companion to
> [the capability-capsule product-type proposal](2026-07-07-capability-capsule-product-type.md):
> that doc defines *what a capsule is* (rationale / contract / constraint faces, tiering, schema);
> this doc defines *what the first game capsule contains* — the algorithms, the simulation
> architectures they plug into, the example code that ships them, and the governance mechanisms
> developed alongside. Pre-ADR; feeds the same ADR-0023 graduation.

- **Date:** 2026-07-07
- **Owner:** FS.GG.Game
- **Status:** Design proposal (pre-ADR)
- **Scope:** The concrete build plan for the FS.GG.Game capsule — the capability × architecture
  matrix, the four in-scope simulation strategies, the test/sample project layout, and the
  governance checks bound in `capabilities.yml`.
- **Grounding assets:** the game-logic [design corpus](README.md) (collision, LOS, FOV,
  pathfinding, grids); the private reference architecture
  `EHotwagner/GameEngineDesignV1` (`Design.md` — the array-DAG engine; `tanks.md` — a worked
  example); the existing `FS.GG.Game.Core` primitives (`Geometry`, `Rng`, `FixedStep`,
  `Pathfinding`, `SpatialGrid`).

---

## 1. The organizing insight — a capsule is a matrix, not a list

What a game capsule ships splits along **two orthogonal axes**:

- **Capabilities / algorithms** — collision, line-of-sight, field-of-view, pathfinding, spatial
  partitioning. These are **architecture-neutral pure functions over predicates** (`walkable`,
  `opaque`, `cost`). They do not care how world state is stored.
- **Architecture strategies / simulation shells** — how world state is *held and advanced*
  (SoA arrays, ECS components, immutable records). The scaffold the algorithms plug into.

**Decision (D4, agreed):** capabilities stay **strictly architecture-neutral**. Each algorithm is
written once as a pure core (contract/reference tier) with FsCheck + golden invariants (constraint
face); each architecture provides only a **thin adapter + a runnable sample** that wires the core
in. This keeps the matrix at *capabilities + architectures* cost, not *capabilities × architectures*
cost.

```
                 array-DAG   records+lens/DU   ECS(Garnet)   MVU
 collision          adapter      adapter          adapter    (via DU)
 LOS                adapter      adapter          adapter    (via DU)
 FOV                adapter      adapter          adapter    (via DU)
 pathfinding        adapter      adapter          adapter    (via DU)
 grids/spatial      adapter      adapter          adapter    (via DU)
   └── each cell = a thin wiring + a sample; the algorithm body is shared and pure ──┘
```

## 2. The architecture menu

**Decision (D1, agreed):** first-class = **array-DAG** and **records+lenses+DU**; **ECS/Garnet**
second-class; **MVU** as a thin variant of the DU architecture. "First-class" = full sample +
governance; "second-class" = sample + link, lighter governance; "thin variant" = documented as a
delta from its parent.

| Strategy | Class | When the agent picks it | State shape | Determinism approach | Governance it exposes |
|---|---|---|---|---|---|
| **Array-oriented double-buffered DAG** (`GameEngineDesignV1`) | first | perf-critical real-time, physics, 10k+ entities | SoA parallel arrays; `Front`/`Back` swap; swap-and-pop | fixed dt + threaded PRNG + input snapshot; typed event arrays (not DUs) | full §9–10 apparatus (DAG extraction, drift gate, access contracts, executable spec) |
| **Records + lenses + DU** | first | turn-based, puzzle, tactics, small state, non-perf | deeply nested immutable records; DU variants | trivially deterministic (pure `world→world`, immutable) | reducer property tests; DU exhaustiveness |
| **ECS (Garnet)** | second | dynamic composition, many heterogeneous entity types | components in 64-elem segments; queries | message-log **replay** (state determined by incoming message sequence) | replay-log golden; system read/write component sets |
| **MVU / Elmish** | thin (of DU) | UI-coupled sims, undo/replay, Elmish front ends | immutable model; `Msg` DU | pure `update: Msg → Model → Model` | corpus's "physics step as deterministic `world→world` reducer" |

The two first-class choices deliberately bracket the spectrum: array-DAG at the performance/ceremony
end, records+lenses+DU at the simplicity/immutability end. The agent chooses by the game's needs;
the capsule shows both honestly, including the ceremony cost of the array-DAG path.

## 3. The array-DAG design is the governance exemplar

The most important reuse in `GameEngineDesignV1` is not the engine — it is that **§9–§10 is a
fully-worked capsule constraint face**:

- **DAG-as-`.fsi`-attributes** — a machine-extractable contract (the same "`.fsi` as sole public
  surface" discipline this repo already enforces via surface baselines).
- **DAG snapshot diffing in CI** — a governance *drift gate*.
- **Runtime access contracts** (dev builds) — advisory enforcement of declared reads/writes.
- **Executable spec (`.fsx`)** — "is the documentation true?" as one agent-runnable command.

This is the concrete answer to "governance developed side-by-side with mechanisms": the mechanisms
already exist in a design we favor. The array-DAG sample becomes the **reference capsule** that
demonstrates the constraint face end-to-end; lighter architectures adopt a subset.

**Decision (D3, agreed — experimental, low conviction):** package the **DAG drift gate + the
executable-spec runner** as a *reusable capsule mechanism* (not merely an illustrative property of
the one sample), so other capsules can adopt "extract → snapshot → diff in CI" and "one `.fsx` that
asserts the whole doc/contract chain." Treated as an experiment: if it does not earn its keep beyond
the array-DAG sample, it demotes back to sample-local with no further cost.

## 4. Example-code plan — three layers of test/sample projects

1. **Per-capability pure core + property/golden tests** (architecture-neutral). Grows the existing
   `FS.GG.Game.Core` primitives toward the corpus designs: AABB/SAT + swept, Bresenham/DDA LOS,
   symmetric shadowcasting FOV, A*/JPS, spatial hash. Each ships FsCheck properties (symmetry,
   no-tunnel, optimality == Dijkstra) and cross-platform golden hashes. **This layer is the
   constraint face.**
2. **Per-fit canonical samples** (**Decision D2, revised** — see §7). Architectures have different
   sweet spots, so a single game forced into all four teaches the wrong lesson. Instead, reuse the
   org TestSpecs (`FS-GG/.github` → `docs/TestSpecs/Games/`) and implement each in the architectures
   it genuinely fits, with the direct A/B diff done between architectures that *both* plausibly fit
   the game:

   | Sample (existing TestSpec) | Architectures | Capabilities exercised |
   |---|---|---|
   | **Breachpoint Tactics** (`turn-based-tactics`) | records+lens/DU, MVU | grid, reachability/pathfinding, **discrete knockback-collision** |
   | **Real-time** (`asteroids` or `tower-defense`) | array-DAG, ECS | **AABB/swept collision**, spatial grid, LOS (targeting) |
   | **Roguelike dungeon crawler** (`roguelike-dungeon-crawler`) | records+DU, MVU | **FOV/fog + LOS**, pathfinding |

   **Fair A/B pairs** (same game, two fitting architectures): the real-time sample in **array-DAG
   vs ECS**; Breachpoint in **DU vs MVU**. `tanks` (from `GameEngineDesignV1/tanks.md`) and `tetris`
   (`tetris` TestSpec) remain single-architecture showcases (array-DAG and DU respectively).
   Breachpoint's spec is perfect-information / no-fog and natively Elmish/MVU — which is *why* it is
   not forced into the array-DAG engine and does not carry the FOV/LOS load (the roguelike does).
3. **Benchmarks (BenchmarkDotNet)** — the perf evidence feeding the advisory frame-budget /
   allocation checks.

Mapping to tiers (per the capsule-type doc): pure cores = contract/reference tier; samples =
knowledge tier; property suites = constraint face.

## 5. Governance mechanisms, bound in `capabilities.yml`

| Mechanism | Check id | Start maturity | Applies to |
|---|---|---|---|
| Cross-platform determinism golden hash | `determinism / golden-hash` | block-on-ship | all capabilities |
| FsCheck invariants (symmetry, no-tunnel, optimality) | `property / fscheck` | advisory → block | all capabilities |
| DAG snapshot drift | `architecture / dag-drift` | block-on-ship | array-DAG arch |
| Runtime access-contract match | `architecture / access-contract` | advisory (dev only) | array-DAG arch |
| Frame budget / allocation ceiling | `performance / benchmark` | **advisory, tolerance-banded** | perf-critical archs |
| Replay-log equivalence | `determinism / replay` | advisory → block | ECS/Garnet arch |

The advisory→block-on-ship ramp mirrors `GameEngineDesignV1`'s dev-contracts-vs-CI-gate split, now
expressed as governance maturity on the light profile (ADR-0022 §10 decision #4). Perf checks stay
advisory/banded — never a hard laptop gate.

**The ramp is free.** `capabilities.yml` already states *"block-on-ship maturity only bites on the
`release` profile, never on `light`."* So the **light↔release profile switch *is* the
advisory→block ramp** — set each check's maturity honestly (determinism/property/dag-drift =
block-on-ship; performance = advisory) and the profile decides enforcement. During agent generation
the repo is on `light`, so every check is advisory feedback (the objective function); at the
merge/release boundary the block-on-ship checks bite. No per-check ramp machinery is needed — only
the checks themselves.

The additive schema extension (leaves `build`/`test` untouched) adds four domains —
`determinism`, `property`, `architecture`, `performance` — each with the check ids above; the
`architecture` domain's `dag-drift` check runs the array-DAG sample's `PipelineSpec.fsx` (§3).

## 6. Research link pack

**Algorithms**
- Red Blob Games / Amit Patel — [hex grids](https://www.redblobgames.com/grids/hexagons/) ·
  [A*](https://www.redblobgames.com/pathfinding/a-star/introduction.html) ·
  [visibility](https://www.redblobgames.com/articles/visibility/)
- Albert Ford — [symmetric shadowcasting](https://www.albertford.com/shadowcasting/) ·
  Adam Milazzo — [roguelike vision algorithms](http://www.adammil.net/blog/v125_Roguelike_Vision_Algorithms.html)
- RogueBasin — [FOV](http://roguebasin.com/index.php/Field_of_Vision) ·
  [Dijkstra maps](http://www.roguebasin.com/index.php/The_Incredible_Power_of_Dijkstra_Maps)
- Harabor & Grastien — [Jump Point Search](https://harablog.wordpress.com/2011/09/07/jump-point-search/)
- Christer Ericson — *Real-Time Collision Detection* + [realtimecollisiondetection.net](https://realtimecollisiondetection.net/)
- Amanatides & Woo — "A Fast Voxel Traversal Algorithm for Ray Tracing" (the shared tile DDA)

**Architecture & determinism**
- Bob Nystrom — [Game Programming Patterns](https://gameprogrammingpatterns.com/) (double buffer, game loop, spatial partition)
- Gaffer On Games — [fix your timestep](https://gafferongames.com/post/fix_your_timestep/) ·
  [deterministic lockstep](https://gafferongames.com/post/deterministic_lockstep/)
- [Garnet](https://github.com/bcarruthers/garnet) · Sander Mertens — [ECS FAQ](https://github.com/SanderMertens/ecs-faq)
- Richard Fabian — [Data-Oriented Design](https://www.dataorienteddesign.com/dodbook/)
- [Bepu Physics 2](https://github.com/bepu/bepuphysics2) · [Elmish](https://elmish.github.io/elmish/)

## 7. Decisions recorded (this session)

- **D1** — First-class architectures: **array-DAG** + **records+lenses+DU**; ECS/Garnet
  second-class; MVU a thin variant of DU. *Agreed.*
- **D2** — *Revised 2026-07-07.* **Per-fit canonical samples** reusing org TestSpecs (Breachpoint
  Tactics → DU/MVU; a real-time sample → array-DAG/ECS; roguelike → DU/MVU for FOV/LOS), with the
  A/B diff done between architectures that both fit the game; `tanks`/`tetris` single-arch
  showcases. Supersedes the original "one tactics skirmish across all four" after the
  `turn-based-tactics` spec proved to be perfect-info / MVU-native (no fog, poor array-DAG fit).
  *Agreed.*
- **D3** — Package the DAG drift gate + executable-spec runner as a reusable capsule mechanism.
  *Agreed, experimental / low conviction — demotes to sample-local if it doesn't earn its keep.*
- **D4** — Capabilities stay strictly architecture-neutral; thin per-arch adapters. *Agreed.*

## 8. Resolved (2026-07-07) + remaining

**Resolved this session:**

- **Q1 — first capability: collision.** Confirmed. Chosen because it spans two architecture shapes
  — discrete grid knockback (Breachpoint / DU) *and* continuous AABB/swept (real-time / array-DAG) —
  so a single neutral core serving both is the hardest test of the D4 neutrality claim. Slice:
  pure core → FsCheck+golden invariants → governed checks → wired into both a DU and an array-DAG
  sample.
- **Q2 — reuse org TestSpecs: yes.** `FS-GG/.github/docs/TestSpecs/Games/` already carries
  Breachpoint Tactics, tetris, roguelike-dungeon-crawler, tower-defense, asteroids, and ~12 more,
  with determinism baked in. Reused per the cross-repo reference discipline; drove the D2 revision.
- **Q3 — schema extension: additive, four new domains.** `determinism` / `property` /
  `architecture` / `performance` added alongside `build` / `test` (see §5). The advisory→block ramp
  is supplied for free by the existing `light`↔`release` profile switch — no per-check ramp
  machinery.
- **Q4 — Garnet: do not pin.** Garnet is 0.5.3 (Oct 2021, dormant, net5/netstandard2.0, only
  computed net10 compat). Injecting a dead netstandard2.0 package into a net10 +
  warnings-as-errors + locked-restore + Renovate build is not worth it for a second-class showcase.
  The ECS sample ships a **self-contained ~100–150-line ECS shim** (component store, `Query<'a,'b>`,
  message-log `replay`) — fully readable by the agent — plus a link to Garnet as the canonical real
  F# ECS. Keeps ECS "sketch, not shipped dep" per D1.

**Remaining:**

1. Pick the concrete real-time sample for the array-DAG↔ECS A/B: `asteroids` (simplest, pure
   collision + wrap) vs `tower-defense` (adds LOS targeting + pathfinding). Leaning `tower-defense`
   for capability coverage.
2. Whether `determinism-golden` hashes are shared cross-repo with the upstream TestSpec (one golden
   per spec, referenced from `.github`) or regenerated capsule-local.
3. Exact home/naming of the reusable drift-gate + `PipelineSpec.fsx` mechanism (D3, experimental) —
   sample-local first, promoted only if a second capsule adopts it.
