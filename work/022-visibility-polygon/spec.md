---
schemaVersion: 1
workId: 022-visibility-polygon
title: 2D Visibility Polygon
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# 2D Visibility Polygon Specification

Prose status: specified

## User Value
Authoring/harness code can compute a **2D visibility polygon** — the continuous region visible from a
light/eye point over a set of wall segments — for light sources, guard vision cones, and fog-of-war
over the continuous segment world, complementing the grid shadowcasting in `Fov`/`Los`. It lives in
`FS.GG.Game.Harness` (the authoring layer) because it crosses the float boundary; the ordering is
deterministic (integer pseudo-angle, squared distance — no `atan2`), so the polygon is reproducible.

## Scope
- SB-001: `VisibilityPolygon.polygon origin bounds segments : Point list` in `FS.GG.Game.Harness` —
  the visible-region polygon from `origin`, clipped to `bounds`, over `(Point * Point)` wall segments,
  using pseudo-angle ordering and a squared-distance nearest test.
- SB-002: A property suite (origin inside, star-shaped, empty-room ≈ bounds, a wall casts a shadow,
  reproducibility, totality) and a recorded promotion-to-Core decision.

## Non-Goals
- SB-003: No promotion to `Game.Core`, no exact-rational intersection arithmetic, no soft/dynamic
  shadows or lighting attenuation, and none of the other M3 item (any-angle smoothing 2.2).

## User Stories
- US-001 (P1): As authoring/harness code, I can get the visibility polygon from a point over wall
  segments, for lighting and vision cones.
- US-002 (P1): As authoring/harness code, I can rely on the polygon being reproducible for identical
  inputs and total on degenerate inputs.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given an origin inside `bounds` and wall segments, when `polygon` is called, then it returns a polygon whose every vertex is visible from the origin (no wall strictly between origin and vertex) — the polygon is star-shaped about the origin.
- AC-002 [US-001] [FR-002]: Given the returned polygon, then the origin lies inside it.
- AC-003 [US-001] [FR-003]: Given an empty scene (no interior segments), when `polygon` is called, then the polygon covers the `bounds` interior (the origin sees the whole room), i.e. its area is approximately the bounds area.
- AC-004 [US-001] [FR-004]: Given a single interior wall segment, when `polygon` is called, then a point directly behind the wall from the origin lies outside the polygon (a shadow) while a point in open view lies inside.
- AC-005 [US-002] [FR-005]: Given identical `origin`/`bounds`/`segments`, when `polygon` is called repeatedly, then the result is identical — ordering is by integer pseudo-angle and squared distance, never `atan2`.
- AC-006 [US-002] [FR-006]: Given degenerate input (an origin on/outside `bounds`, an empty segment list, or a zero-length segment), when `polygon` is called, then it returns a polygon (possibly the bounds) without throwing.

## Functional Requirements
- FR-001: `VisibilityPolygon.polygon` MUST return a star-shaped polygon about the origin — every returned vertex is visible from the origin (no wall segment strictly crosses the open segment from origin to that vertex). (covers AC-001)
- FR-002: The origin MUST lie inside the returned polygon. (covers AC-002)
- FR-003: With no interior segments, the polygon MUST cover the `bounds` interior (area within a small tolerance of the bounds area). (covers AC-003)
- FR-004: A single interior wall segment MUST cast a shadow — a point directly behind it from the origin is outside the polygon and a point in open view is inside. (covers AC-004)
- FR-005: `polygon` MUST be reproducible: identical inputs yield an identical `Point list`, using integer pseudo-angle ordering (quadrant + slope) and squared-distance nearest tests, never `atan2`. (covers AC-005)
- FR-006: `polygon` MUST be total — an origin on or outside `bounds`, an empty segment list, or a zero-length segment yields a polygon without throwing. (covers AC-006)

## Ambiguities
- AMB-001: Output form — a float `Point list` polygon (harness) or exact-rational vertices?
- AMB-002: Algorithm — cast rays at each endpoint (with tiny ±angle offsets to see past corners), or a full endpoint sweep with an open-wall set?
- AMB-003: Unbounded scene — require a `bounds` Rect to close the polygon so rays always hit?

## Public Or Tool-Facing Impact
- Tier 1 (contracted). Adds a public `VisibilityPolygon` module to `FS.GG.Game.Harness`, a new
  `VisibilityPolygon.fs`/`.fsi` in the compile order — so the `.fsi`, the Harness surface baseline,
  and tests land together. No change to `Game.Core`.

## Lifecycle Notes
- Reproducibility/property tests MUST carry a stable filterable name for the `gate.yml` guard.
- The promotion-to-Core decision is recorded in this work item and deliberately deferred.
- Next lifecycle action: `fsgg-sdd clarify --work 022-visibility-polygon`.
