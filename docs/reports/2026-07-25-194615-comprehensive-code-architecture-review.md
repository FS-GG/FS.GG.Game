# Comprehensive code and architecture review

- Repository: `FS-GG/FS.GG.Game`
- Reviewed revision: `1ff4f5a8dfba41810f635d4fabd8f375f42f5b1b`
- Review completed: 2026-07-25 19:46:15 UTC (21:46:15 CEST)
- Scope: core game/domain libraries, public signatures, harness/render/playtest layers, tests, and current GitHub checks

## Executive assessment

Game has a healthy layered design and exceptionally broad green tests: all 847 Release tests passed and all 29 current GitHub checks succeeded. Two public claims in the hex-grid API are nevertheless stronger than the implementation: callers can construct invalid `Hex` records directly, and “exact inverse for every hex” conversions overflow for valid extreme `int` coordinates.

Overall risk: **medium**. Ordinary gameplay paths are well covered; the concerns are public invariant integrity and arithmetic contract accuracy at boundary values.

## Architecture

`Game.Core` contains domain models and algorithms, while harness, render, and playtest CLI projects sit outward of it. Rendering is an adapter rather than a core dependency. Explicit `.fsi` files document the intended public API, and test projects cover core behavior, integration harnesses, rendering, and CLI behavior.

## Evidence

| Suite, Release | Result |
|---|---|
| Game.Core.Tests | 724 passed |
| Game.Harness.Tests | 81 passed |
| Game.Render.Tests | 23 passed |
| Game.Playtest.Cli.Tests | 19 passed |
| Total | 847 passed, 0 failed, 0 skipped |
| Current-revision GitHub checks | 29 succeeded, 0 failed |

This review was not a gameplay balance assessment, long-running simulation, or rendering performance benchmark.

## Findings

### 1. High — the public `Hex` representation does not enforce its documented invariant

`src/Game.Core/Hex.fsi` exposes `Hex` as a record with public `Q`, `R`, and `S` fields while its documentation says only `Hex.create` can construct values and that `Q + R + S = 0` holds by construction.

F# consumers can use a record expression to create any triple, bypassing `Hex.create`. Algorithms can therefore receive values the public contract says are impossible.

Recommendation: make the representation private (`type Hex = private { ... }`) while exposing read-only members/accessors and validated factories. If source compatibility prevents that immediately, validate at public algorithm boundaries and correct the documentation until the next major version.

### 2. High — offset/doubled conversion “exact inverse” claims fail at integer extremes

`Hex.fs` computes expressions such as `2 * Q + R` using unchecked `int` arithmetic. `Hex.fsi` states the conversions are exact inverses for every hex. Valid invariant-preserving coordinates near `Int32` limits can overflow, so the inverse claim is false for part of the public domain.

Current property generators exercise ordinary values and do not cover arithmetic extremes.

Recommendation: either use checked arithmetic and return a typed overflow result, widen intermediates and constrain outputs, or explicitly document a safe coordinate range. Add `Int32.MinValue`/`MaxValue` neighborhood tests for every conversion pair.

### 3. Medium — physics and pathfinding remain large change hotspots

The physics module is roughly 1,425 lines and pathfinding roughly 944 lines. Each combines several algorithmic concerns and invariant transitions.

Recommendation: split by algorithm/state responsibility behind internal modules, retaining the current public facade. Add performance and determinism baselines before restructuring.

### 4. Low — invariant-focused generators should include malformed and extreme values

The strong test count mostly validates constructed/ordinary-domain values. Public representation leakage and overflow demonstrate that generator domains are part of the contract.

Recommendation: maintain separate generators for valid ordinary, valid extreme, and deliberately malformed values; assert either correct handling or explicit rejection.

## Strengths

- Clear Core → Harness/Render/CLI dependency direction.
- All 847 Release tests and 29 live checks are green.
- Public F# signatures make intended contracts reviewable.
- Prior arithmetic hardening is visible in other game-domain paths.
- Rendering concerns remain outside core game semantics.

## Recommended order

1. Seal the `Hex` representation or correct and enforce the public construction contract.
2. Define overflow semantics and test conversion extremes.
3. Add malformed/extreme property generators.
4. Decompose physics and pathfinding behind stable facades.
