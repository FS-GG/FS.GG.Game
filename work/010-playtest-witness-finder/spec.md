---
schemaVersion: 1
workId: 010-playtest-witness-finder
title: Playtest Witness Finder
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Playtest Witness Finder Specification

Prose status: specified

## User Value
A game author finds the shortest non-synthetic input script from `Init` that reaches a target game state via `Explore.findScript`, and measures a fixture's reachable state space via `Explore.reachable`, instead of hand-building initial states and hoping they are reachable and valid. Because the witness is a real path from `Init` driven through the game's own step, the evidence it yields is `InputDriven` by construction — the reachable-vs-synthetic tension is dissolved.

## Scope
- SB-001: A new pure `Explore` module in `FS.GG.Game.Harness` doing a bounded breadth-first search over a `Playable`.
- SB-002: `findScript` returns the shortest command script from `Init` reaching a goal predicate; `reachable` returns the distinct fingerprints reachable within a bound.
- SB-003: A fail-closed result: a distinct `Truncated` signal (with the visited count) when the search hits its depth or visited-set bound before exhausting the frontier.
- SB-004: A config for a restricted move alphabet and a visited-set cap; `PongSim` dogfood proofs and an updated surface baseline.

## Non-Goals
- SB-010: The FsCheck property runner (Phase 3), bot combinators (Phase 4), and the `fsgg-playtest` CLI (Phases 5–6).
- SB-011: A general game-playing or competitive AI — the search produces test inputs, not a winning agent.
- SB-012: A theorem prover — a negative result within a bound is "not found within N", never a proof of impossibility.
- SB-013: Any change to `FS.GG.Game.Core` or to the existing behavior of `Trace`/`Driver`/`Matrix`.

## User Stories
- US-001 (P1): As a game author, I ask for the shortest input script from `Init` that reaches a target state, and get a real, replayable, non-synthetic witness.
- US-002 (P1): As a game author, when a state is not reachable within my bound, I get a clear "not found within N" that is distinct from "the search gave up early", so I never read a truncated search as a proof of impossibility.
- US-003 (P2): As a game author, I measure the set of distinct states a fixture can reach within a bound, to spot a degenerate fixture.
- US-004 (P2): As a game author, I restrict the search to a subset of the keymap alphabet or search over a projected fingerprint, to control state explosion.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given a `Playable` and a goal predicate satisfiable within `maxDepth`, when `findScript` runs, then it returns `Found` with the shortest command script (fewest frames) whose replay from `Init` reaches a goal-satisfying state.
- AC-002 [US-001] [FR-002]: Given a `Found` witness, when its script is replayed through `Driver.runCommands` from `Init`, then the resulting trace is `Origin.InputDriven` and its final state satisfies the goal — the evidence is non-synthetic by construction.
- AC-003 [US-002] [FR-003]: Given a goal unreachable within `maxDepth` but the frontier fully explored within the visited cap, when `findScript` runs, then it returns the `NotFound` case, distinct from `Truncated`.
- AC-004 [US-002] [FR-004]: Given a search whose reachable frontier exceeds the visited-set cap (or a goal beyond the depth bound with an unexhausted frontier), when `findScript` runs, then it returns `Truncated` carrying the number of states visited — never a silent `NotFound`.
- AC-005 [US-003] [FR-005]: Given a `Playable`, a fingerprint, and `maxDepth`, when `reachable` runs, then it returns the set of distinct fingerprints reachable within the bound, with a truncation flag set when the visited cap is hit.
- AC-006 [US-003] [FR-006]: Given the search, when it deduplicates states, then it does so by `fingerprint world` (the visited-set key), so two worlds with equal fingerprints are the same node.
- AC-007 [US-004] [FR-007]: Given a config with a restricted move alphabet, when the search runs, then it explores only those moves; and the default move set is the empty frame plus each single command in the `Playable.Keymap` alphabet.
- AC-008 [US-001] [FR-008]: Given the harness assembly after this change, when its dependencies are inspected, then it still references only `FS.GG.Game.Core` and the BCL and performs no I/O or wall-clock read, and the surface baseline is regenerated to include the new `Explore` surface.

## Functional Requirements
- FR-001: `Explore.findScript` MUST return the shortest command script (fewest frames) from `Init` that drives the world to a goal-satisfying state, via breadth-first search bounded by `maxDepth`. (covers AC-001)
- FR-002: A returned witness MUST be non-synthetic by construction: replaying its script through `Driver.runCommands` from `Init` MUST produce an `Origin.InputDriven` trace whose final state satisfies the goal. (covers AC-002)
- FR-003: When the reachable frontier is fully explored within the visited cap and no state satisfies the goal, `findScript` MUST return a `NotFound` result distinct from truncation. (covers AC-003)
- FR-004: When the search hits its depth bound or visited-set cap before exhausting the frontier, `findScript` MUST return a distinct `Truncated` result carrying the number of states visited — it MUST NOT return a silent `NotFound`/empty (fail closed, #266). (covers AC-004)
- FR-005: `Explore.reachable` MUST return the set of distinct fingerprints reachable within `maxDepth`, with a truncation flag set when the visited cap is hit. (covers AC-005)
- FR-006: The search MUST deduplicate states by `fingerprint world` (the visited-set key), so equal fingerprints are the same node. (covers AC-006)
- FR-007: A caller MUST be able to restrict the search to a supplied move alphabet; absent that, the default move set MUST be the empty frame plus each single command in the `Playable.Keymap` alphabet. (covers AC-007)
- FR-008: The change MUST remain additive and pure: the harness assembly MUST still reference only `FS.GG.Game.Core` and the BCL with no I/O or wall-clock read, and the surface baseline MUST be regenerated to include the new `Explore` surface. (covers AC-008)

## Ambiguities
- AMB-001: Whether `findScript` returns `Command list list option` (the design sketch) or a richer three-case result (`Found`/`NotFound`/`Truncated`) so truncation is not conflated with not-found.
- AMB-002: Whether `findScript` takes an explicit fingerprint for the visited key, or dedups by whole-world comparison — bearing on whether the world type must be comparable.

## Public Or Tool-Facing Impact
- Adds public package surface: a new `Explore` module plus its result and config types. Tier-1 change — `.fsi`, surface baseline, tests, and docs land together; existing surface unchanged (purely additive).

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 010-playtest-witness-finder`.
