---
schemaVersion: 1
workId: 019-hex-grid
title: Hexagonal Grid Module
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Hexagonal Grid Module Specification

Prose status: specified

## User Value
Game-logic code gets a full **hexagonal-grid** coordinate module — cube/axial integer coordinates with
distance, neighbours, arithmetic, rotation, `range`/`ring`/`spiral`, line drawing, rounding, and
offset/doubled converters, plus hex pathfinding — unlocking an entire genre (hex tactics, hex board
games) the square-grid-only library cannot express today. Everything is integer, total, and
byte-deterministic; hexes are stored in whatever offset/doubled form the map wants and converted to
cube for the algorithms.

## Scope
- SB-001: A new `Hex` module in `FS.GG.Game.Core` — an integer cube coordinate type (`q + r + s = 0`)
  with `create`, `add`, `subtract`, `scale`, `neighbours`, `distance`, `rotateLeft`, `rotateRight`,
  `range`, `ring`, `spiral`, `lineDraw`, `round`, offset (`toOffset`/`ofOffset`) and doubled
  (`toDoubled`/`ofDoubled`) converters, and `Hex.astar`/`Hex.bfs` over the hex neighbour set.
- SB-002: A full property suite: the cube invariant, distance/neighbour laws, rotation cycles,
  range/ring/spiral cardinalities, line contiguity, converter round-trips, and hex-pathfinding
  optimality + determinism.

## Non-Goals
- SB-003: No pixel↔hex or hex↔pixel conversion (a render-adapter float boundary, out of Core), no hex
  JPS/ALT/edge features, no change to the square `Pathfinding`/`Grids` surface, and none of the other
  M2 item (tile edges & vertices 2.3).

## User Stories
- US-001 (P1): As game-logic code, I can work in integer hex cube coordinates — distance, neighbours,
  arithmetic, rotation, range/ring/spiral, lines — so I can build hex-grid game logic.
- US-002 (P1): As game-logic code, I can store hexes in offset or doubled form and round-trip them to
  cube, and I can path over a hex grid with `Hex.astar`/`Hex.bfs`.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given `Hex.create q r`, when the hex is built, then `q + r + s = 0` holds and the value is a total, structurally-equal record.
- AC-002 [US-001] [FR-002]: Given two hexes, when `distance` is called, then it returns the integer cube distance `(|dq| + |dr| + |ds|) / 2`, and `distance a (neighbour a i) = 1` for each of the 6 neighbours.
- AC-003 [US-001] [FR-003]: Given a hex, when `neighbours` is called, then it returns the 6 adjacent hexes in a fixed documented order; `add`/`subtract`/`scale` are integer and satisfy `subtract (add a b) b = a` and `scale h 0 = origin`.
- AC-004 [US-001] [FR-004]: Given a hex, when `rotateLeft`/`rotateRight` is applied 6 times, then the hex returns to itself, and one application is a 60° cube rotation preserving `distance` from the origin.
- AC-005 [US-001] [FR-005]: Given a radius `n >= 0`, when `range`/`ring`/`spiral` are called, then `range` has `3n(n+1)+1` hexes, `ring n` has `6n` (and `1` when `n = 0`), and `spiral n` is `range n` in ring order, each in a fixed deterministic order.
- AC-006 [US-001] [FR-006]: Given two hexes, when `lineDraw` is called, then it returns a contiguous hex path (each consecutive pair adjacent) including both endpoints, of length `distance + 1`, deterministically; `round` maps a fractional cube to a valid `Hex` with a documented tie-break.
- AC-007 [US-002] [FR-007]: Given any hex, when converted to offset (or doubled) and back, then the original hex is recovered (`ofOffset (toOffset h) = h`, `ofDoubled (toDoubled h) = h`).
- AC-008 [US-002] [FR-008]: Given a walkable hex predicate, when `Hex.astar`/`Hex.bfs` are called, then they return a shortest hop path over the hex neighbour set (length `distance + 1` on an open grid), byte-deterministically, agreeing on reachability.

## Functional Requirements
- FR-001: `Hex.create q r` MUST build a cube hex with `s = -q - r` so the invariant `q + r + s = 0` always holds; the type is an integer record with structural equality. (covers AC-001)
- FR-002: `Hex.distance` MUST return the integer cube distance `(|dq| + |dr| + |ds|) / 2`, total over the coordinate space (int64 internally so a wide span does not overflow), and equal to 1 between any hex and each of its 6 neighbours. (covers AC-002)
- FR-003: `Hex.neighbours` MUST return the 6 adjacent hexes in a fixed documented order, and `add`/`subtract`/`scale` MUST be integer with `subtract (add a b) b = a`. (covers AC-003)
- FR-004: `Hex.rotateLeft`/`rotateRight` MUST be 60° cube rotations (`[q,r,s] -> [-r,-s,-q]` and its inverse) such that six applications are the identity and one preserves distance from the origin. (covers AC-004)
- FR-005: `Hex.range`/`ring`/`spiral` MUST enumerate the correct hex sets — `range n` of cardinality `3n(n+1)+1`, `ring n` of `6n` (`1` at `n=0`), `spiral n = range n` — each in a fixed deterministic order. (covers AC-005)
- FR-006: `Hex.lineDraw` MUST return a contiguous, endpoint-inclusive hex line of length `distance + 1` deterministically, and `Hex.round` MUST map a fractional cube coordinate to a valid `Hex` (`q + r + s = 0`) resetting the largest-error component, with a documented tie-break. (covers AC-006)
- FR-007: `Hex.toOffset`/`ofOffset` and `Hex.toDoubled`/`ofDoubled` MUST be exact inverses — `ofOffset (toOffset h) = h` and `ofDoubled (toDoubled h) = h` for every hex. (covers AC-007)
- FR-008: `Hex.astar`/`Hex.bfs` MUST return a shortest hop path over the hex neighbour set (endpoint-inclusive, `maxVisited`-bounded, `None` when unreachable), byte-deterministic, agreeing with each other on reachability. (covers AC-008)

## Ambiguities
- AMB-001: Coordinate representation — a cube record `{ Q; R; S }` with `S` derived and validated, or axial `{ Q; R }` with `S` computed on demand?
- AMB-002: Offset layout convention — even-q, odd-q, even-r, or odd-r? (A documented default is needed for the converters.)
- AMB-003: `round` tie-break — when two components tie for the largest rounding error, which one is reset to preserve `q + r + s = 0`?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a new public `Hex` module (type + functions) to `FS.GG.Game.Core`, a new
  `Hex.fs`/`.fsi` in the compile order — so the `.fsi`, both surface baselines, and tests land
  together, and the surface-baseline-drift gate must stay green. No existing signature changes.

## Lifecycle Notes
- Determinism/property tests MUST carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd clarify --work 019-hex-grid`.
