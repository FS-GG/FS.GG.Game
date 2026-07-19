---
schemaVersion: 1
workId: 018-pathfinding-straighter-tiebreak
title: Straighter-Path Tie-Break
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

# Straighter-Path Tie-Break Charter

## Identity
Milestone M1 item 1.4 of the Red Blob Games algorithm roadmap
(`docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md`, §2.4). Adds an **opt-in**
A* variant `Pathfinding.astarStraight` whose frontier tie-break prefers, among equal-`f` nodes, those
nearer the straight line to the goal — an integer cross-product term folded into the ordering key
`(f, h, cross, Col, Row)`. Straighter paths *and* fewer expansions, at zero optimality cost. The
shipped `astar` is byte-identical and unchanged.

## Principles
- **Opt-in, no silent output change.** `astar`'s byte-identical outputs are untouched; the bias is a
  separate entry point, so current consumers migrate deliberately (constitution "no silent output
  changes").
- **Integer cross-product only.** The tie-break term is `|dx1*dy2 - dx2*dy1|` in int64 — a strict
  total order, no float, so determinism is preserved.
- **Optimality preserved.** The cross term is only a tie-break *after* `f` and `h`, so it never
  changes which cost is optimal — the differential against `astar` (equal cost) is the primary test.

## Scope Boundaries
- **In:** `Pathfinding.astarStraight` over a shared frontier key extended with an integer cross-product
  tie-break; a differential (equal-cost) oracle vs `astar`, a byte-identity check that `astar` is
  unchanged, a straightness-improvement test, and determinism; `.fsi` + surface baselines.
- **Out:** changing `astar`'s signature or output, any-angle/Theta* smoothing (that is 2.2), applying
  the bias to `bfs`/`reachable`/`jps`, and the other M1 items (JPS 1.1, Regions 1.2, ALT 1.3).

## Policy Pointers
- Honors constitution I, III (public surface), V (pure), VI (test evidence), and the "no silent output
  changes" rule (the bias is versioned/opt-in).
- Tier 1 (contracted): `.fsi`, both surface baselines, and tests land together; drift gate stays green.
- Governance pointers are optional compatibility facts, not evaluated by this command.

## Lifecycle Notes
- Determinism/differential/straightness tests carry a stable filterable name for the `gate.yml` guard.
- Next lifecycle action: `fsgg-sdd specify --work 018-pathfinding-straighter-tiebreak`.
