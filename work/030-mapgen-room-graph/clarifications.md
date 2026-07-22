---
schemaVersion: 1
workId: 030-mapgen-room-graph
title: MapGen Room-Graph Branching-Walk Floors
stage: clarify
changeTier: tier1
status: needsAnswers
sourceSpec: work/030-mapgen-room-graph/spec.md
publicOrToolFacingImpact: true
---

# MapGen Room-Graph Branching-Walk Floors Clarifications

## Source Specification
- work/030-mapgen-room-graph/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): `FloorRoom` with an explicit doors field, or doors implied by `Adjacency`?
- **CQ-002** (AMB-002): When dead-ends are too few, omit extra special rooms or relax the target?
- **CQ-003** (AMB-003): `floorSeed` via the `Rng` splitmix mixing or a bespoke hash?

## Answers
- CQ-001 → Imply doors from `Adjacency`. A door is exactly a shared edge between two adjacent rooms, and
  `Adjacency` already lists every 4-adjacent room-cell pair — a separate `Doors` field on `FloorRoom` would
  be a second, deriveable copy that can fall out of sync with `Adjacency` (the same "one canonical name"
  discipline `Grids`/`Edges` follow). A game reads a room's doors by filtering `Adjacency` for pairs
  containing its `Cell`. `FloorRoom` stays `{ Cell; Kind; TemplateId }` (resolves AMB-001).
- CQ-002 → Omit the extras, and assign in `SpecialRooms` list order to dead-ends sorted by **descending**
  graph distance from Start (so the first listed special — conventionally Boss — lands on the farthest
  dead-end). Relaxing to any non-Start room would put a "secret/boss" room mid-corridor, breaking the
  roguelike §4.8 invariant that special rooms are leaves; a floor with too few dead-ends honestly gets fewer
  special rooms, which the caller can detect and regenerate with a higher `RoomCount`. Deterministic:
  dead-ends tie-break by `Cell` order (resolves AMB-002).
- CQ-003 → Reuse the `Rng` splitmix mixing: `floorSeed run i = state of (Rng.ofSeed run |> split i times)` is
  overkill; instead derive `floorSeed run i` by seeding an `Rng` from `run`, mixing in `i` via the same
  splitmix finalizer the module already trusts, and taking the resulting state. Reusing the vetted mixer
  (not a bespoke hash) keeps the derivation in one place and inherits its avalanche/decorrelation properties
  — distinct `i` give well-separated seeds (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-004]: `FloorRoom` is `{ Cell; Kind; TemplateId }`; doors are implied
  by `Adjacency`, no separate field.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-003]: Special rooms are assigned in `SpecialRooms` order to dead-ends
  by descending distance from Start (tie → `Cell`); extras that do not fit are omitted.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-005]: `floorSeed run i` derives its seed by mixing `i` into an `Rng`
  seeded from `run` using the module's splitmix mixer; distinct `i` give distinct seeds.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001 through AMB-003 are resolved by DEC-001 through DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 030-mapgen-room-graph`.
