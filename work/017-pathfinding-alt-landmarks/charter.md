---
schemaVersion: 1
workId: 017-pathfinding-alt-landmarks
title: ALT Landmark Heuristic
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# ALT Landmark Heuristic Charter

## Identity
Milestone M1 item 1.3 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §2.3). Adds
`Pathfinding.Landmarks` — precomputed exact distances from a handful of pivot ("landmark") cells —
and an ALT (A*, Landmarks, Triangle-inequality) heuristic that is admissible and far tighter than
octile on large/open maps, so A* expands fewer nodes. Exposed through `Landmarks.astar`, which
returns an **optimal-cost** path identical in cost to plain `astar`.

## Principles
- **Optimality preserved.** The ALT heuristic is admissible (triangle inequality over exact integer
  landmark distances), so `Landmarks.astar` returns a path of the same least cost as `astar`. The
  differential oracle against `astar` (equal cost + validity) is the primary test.
- **Determinism.** Landmark selection is a pure function of the map (farthest-point sampling from a
  fixed seed); all distances are integers; the heuristic and search are byte-deterministic.
- **`astar` unchanged.** The existing `astar` stays byte-identical — the ALT path is a new entry
  point over a shared, heuristic-parameterised core, not a change to `astar`'s signature or output.
- **Reuses `distanceField`.** Each landmark table is a `distanceField` from that landmark; no new
  search engine.

## Scope Boundaries
- **In:** `Pathfinding.Landmarks` (opaque) with `Landmarks.build` (deterministic farthest-point
  landmark selection + per-landmark distance table), `Landmarks.heuristic` (admissible int estimate),
  and `Landmarks.astar` (A* over max(octile, ALT)); a differential (equal-cost) oracle, an
  admissibility test, a fewer-expansions proxy; `.fsi` + surface baselines.
- **Out:** dynamic/incremental landmark updates, ALT for weighted terrain (heuristic serves the
  binary-`isWalkable` `astar`), changing `astar`'s public signature, and the other M1 items
  (JPS 1.1, Regions 1.2, tie-break 1.4).

## Policy Pointers
- Honors constitution I, III (public surface), V (pure), VI (test evidence — differential + admissibility
  fail before, pass after).
- Tier 1 (contracted): `.fsi`, both surface baselines, and tests land together; drift gate stays green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/differential/admissibility tests carry a stable filterable name for the `gate.yml`
  determinism guard.
- Next lifecycle action: `fsgg-sdd specify --work 017-pathfinding-alt-landmarks`.
