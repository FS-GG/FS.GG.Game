---
schemaVersion: 1
workId: 010-playtest-witness-finder
title: Playtest Witness Finder
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/010-playtest-witness-finder/spec.md
sourceClarifications: work/010-playtest-witness-finder/clarifications.md
sourceChecklist: work/010-playtest-witness-finder/checklist.md
publicOrToolFacingImpact: true
---

# Playtest Witness Finder Plan

Prose status: planned

## Source Snapshot
- spec: work/010-playtest-witness-finder/spec.md sha256:ac276ff7c84f1f84909169de0f42f02651fb76cf70ebaa203e5ab09f44d3253b schemaVersion:1
- clarifications: work/010-playtest-witness-finder/clarifications.md sha256:921d51861ac834655a004bfb05dcbad70bd1ba071c512f36e2c4bfd9cd902eae schemaVersion:1
- checklist: work/010-playtest-witness-finder/checklist.md sha256:53014c4cba67275758a390005861a9aa99ffd87306cf5bd23a0d1475d1e080b0 schemaVersion:1

## Plan Scope
- Add a new pure `Explore` module (`Explore.fsi`/`Explore.fs`) to `FS.GG.Game.Harness`, compiled after
  `Driver` (which it drives) and beside `Laws`. Add `PongSim` dogfood proofs and regenerate the
  `FS.GG.Game.Harness` surface baseline.
- Requirement count: 8. Clarification decision count: 2. Checklist result count: 8.

## Technical Context
F# 10 / .NET 10. The search is a small BFS over the graph whose nodes are worlds, whose edges are
"apply a per-frame move (a `Command list`), then one fixed `Driver`-style `Step world Dt`",
deduplicated by `fingerprint world` (DEC-002; the Phase 0 visited-key contract). Pure — Core + BCL
only, so `DependencyTests` (WI-007 FR-007) stays green (FR-008). BFS visits states in depth order, so
the first goal hit is the shortest witness (FR-001). The move-and-step transition reuses the exact
fold `Driver.runCommands` uses, so a `Found` witness replays identically through `Driver.runCommands`
(FR-002).

## Design
### Types (DEC-001, DEC-002)
- `type Witness = { Script: Command list list; Depth: int }` — the shortest per-frame command script
  from `Init` and its frame count.
- `type FindResult = Found of Witness | NotFound | Truncated of visited: int` — `NotFound` = frontier
  exhausted within the bound with no goal state; `Truncated` = depth/visited cap hit first (fail-closed
  #266), carrying the visited count.
- `type ReachResult<'f when 'f: comparison> = { States: Set<'f>; Truncated: bool }`.
- `type ExploreConfig = { Moves: Command list list option; MaxVisited: int }` — `Moves = None` derives
  the default alphabet; a `Some` restricts it (FR-007). `Explore.defaultConfig` caps visited states.

### Search (`Explore` module)
- `defaultMoves playable = [] :: (playable.Keymap |> Map.toList |> List.map snd |> List.distinct |>
  List.map (fun c -> [c]))` — the empty frame plus each single command in the keymap alphabet (FR-007).
- `stepWith playable move world = move |> List.fold (fun w c -> playable.Apply c w) world |>
  fun w -> playable.Step w playable.Dt` — identical to `Driver`'s per-frame fold (FR-002).
- `findScriptWith config playable fingerprint goal maxDepth : FindResult`: BFS from `Init`. Check
  `Init` first (depth-0 empty witness). Maintain a `Set<'f>` visited (keyed by `fingerprint`) and a
  FIFO queue of `(world, revScript, depth)`. For each dequeued node, for each move: compute the child,
  key it; if unseen and goal holds → `Found { Script = List.rev (move :: revScript); Depth = depth+1 }`;
  else if `depth+1 < maxDepth` enqueue. If `visited.Count > config.MaxVisited` at any expansion → return
  `Truncated visited.Count`. Track whether the depth bound cut off any frontier: if the queue empties
  with no cutoff → `NotFound`; if a node was dropped only because it hit `maxDepth` (frontier not
  exhausted) → `Truncated visited.Count`. `findScript` = `findScriptWith defaultConfig`.
- `reachableWith config playable fingerprint maxDepth : ReachResult<'f>`: the same BFS collecting every
  keyed fingerprint; `Truncated = true` when the visited cap is hit. `reachable = reachableWith
  defaultConfig`.

### Dogfood (FR-002, FR-005, FR-008)
- `PongSim`: `findScript` finds the shortest non-synthetic, input-requiring witness (drive the left
  paddle to the top, `LeftY = 0`, in exactly four `MoveNorth` frames); its script replays
  `Origin.InputDriven` and satisfies the goal. **Reproducing the WI-7 degeneracy automatically:** the
  single-seat fixture sends the ball rightward forever (it escapes the right wall and re-serves right),
  so a left-paddle deflection is genuinely unreachable — `findScript` returns `NotFound`, and
  `reachable` over a projected fingerprint shows no state with the ball on the left half. An out-of-reach
  goal within a small depth returns `Truncated` (fail-closed), never a false `Found`. `DependencyTests`
  re-run green; the surface baseline is regenerated additively.

## Constitution Check
- **III Public Surface:** `Explore.fsi` before `Explore.fs`; surface baseline regenerated with tests.
- **IV Idiomatic simplicity:** a plain BFS with an F# `Set` visited key and a queue.
- **V Model-Update-Effect:** pure; no I/O, no wall clock.
- **VIII Safe failure:** the fail-closed `Truncated` signal never masquerades as a confident `NotFound`.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `findScript` runs a depth-ordered BFS from `Init`; the first goal-satisfying child found is the shortest witness (fewest frames), returned as `Found { Script; Depth }`.
- PD-002 [AC-002] [FR-002] complete: the BFS transition reuses the `Driver.runCommands` per-frame fold, so a `Found` witness replays through `Driver.runCommands` to an `Origin.InputDriven` trace whose final state satisfies the goal.
- PD-003 [AC-003] [FR-003] complete: when the queue empties with the frontier fully explored inside the bound and no goal state, `findScript` returns `NotFound`.
- PD-004 [AC-004] [FR-004] [DEC-001] complete: when the visited cap is exceeded, or a frontier node is dropped only because it hit `maxDepth`, `findScript` returns `Truncated visitedCount` — never a silent `NotFound`.
- PD-005 [AC-005] [FR-005] complete: `reachable` collects every keyed fingerprint within `maxDepth` into a `Set<'f>` and sets `Truncated` when the visited cap is hit.
- PD-006 [AC-006] [FR-006] [DEC-002] complete: the visited set is keyed by `fingerprint world`; `findScript`/`reachable` take the fingerprint explicitly and the goal reads the whole world.
- PD-007 [AC-007] [FR-007] complete: `ExploreConfig.Moves` restricts the move alphabet; the default is the empty frame plus each single command in `Playable.Keymap`.
- PD-008 [AC-008] [FR-008] complete: the module is pure Core+BCL; `DependencyTests` stays green and the `FS.GG.Game.Harness` surface baseline is regenerated to include `Explore`.

## Contract Impact
- PC-001 [PD-001] [PD-004] [PD-005] [PD-006] [PD-007] command report: Tier-1 additive surface — a new `Explore` module and its `Witness`/`FindResult`/`ReachResult`/`ExploreConfig` types. `.fsi`, baseline, tests, and docs land together; existing surface byte-unchanged.
- PC-002 [PD-008] command report: the harness's referenced-assembly set stays `{FSharp.Core, FS.GG.Game.Core, System.*}`; `DependencyTests` continues to enforce the leaf-dependency + no-I/O invariant after the addition.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-006] [PD-007] [PC-001] semanticTest: `PongSim` proves `findScript` returns the shortest input-requiring witness (paddle-to-top in four `MoveNorth` frames) whose script replays `Origin.InputDriven` to a goal-satisfying state; the fixture degeneracy is reproduced (a left-paddle deflection is `NotFound`, and `reachable` shows the ball never on the left half); dedup-by-fingerprint and a restricted-alphabet search are exercised.
- VO-002 [PD-003] [PD-004] [PD-005] semanticTest: tests for `NotFound` (small exhausted frontier, unreachable goal), `Truncated` (visited cap / depth cutoff → distinct non-empty signal with a count), and `reachable` (bounded non-empty frontier; truncation flag under a tight cap).
- VO-003 [PD-008] [PC-002] semanticTest: `DependencyTests` re-run green; `dotnet build` clean under warnings-as-errors; the surface baseline regenerated and committed; full harness suite green.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 accepted. Purely additive; nothing removed or renamed, so no consumer migration is required. The surface baseline is extended, not migrated.

## Generated View Impact
- GV-001 [PD-001] [PD-004] workModel: the work model (readiness/010-.../work-model.json) must reflect the eight FR→PD→VO chains once verify/ship build it; a plan edit that adds or drops a PD/VO restales it until `fsgg-sdd refresh` re-runs.
- GV-002 [PC-001] surfaceBaseline: `readiness/surface-baselines/FS.GG.Game.Harness.txt` is regenerated from the extended `.fsi` set; the surface-baseline-drift gate must stay green.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- This is Phase 2 (the keystone) of `docs/reports/2026-07-19-headless-playtest-authoring-machinery-design.md`;
  everything downstream that authors an arbitrary game's mechanic proof can lean on `findScript`.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 010-playtest-witness-finder`.
