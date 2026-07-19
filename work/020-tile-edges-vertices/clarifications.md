---
schemaVersion: 1
workId: 020-tile-edges-vertices
stage: clarify
sourceSpec: work/020-tile-edges-vertices/spec.md
---

# Clarifications

## Source Specification
- work/020-tile-edges-vertices/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Edge representation — canonical cell pair or (Cell, Dir)?
- **CQ-002** (AMB-002): Direction convention for North?
- **CQ-003** (AMB-003): Vertex representation?

## Answers
- CQ-001 → A canonical `Edge` holding the two separated cells sorted by `(Col, Row)` (`Lo <= Hi`), built only via `edgeBetween`/`edgeOf`. Sorting IS the dedupe — the shared edge has one representative regardless of which side you name it from — and `edgeOf cell dir` maps `(Cell, Dir)` onto the same value, so both addressings are available (resolves AMB-001).
- CQ-002 → North is `Row - 1`, matching the module's existing orthogonal offset order `(0,-1)` first; East `Col + 1`, South `Row + 1`, West `Col - 1` (resolves AMB-002).
- CQ-003 → A canonical `Vertex` lattice point `(VCol, VRow)` where vertex `(c, r)` is the NW corner of cell `(c, r)`; a cell's corners are `(c,r)`,`(c+1,r)`,`(c,r+1)`,`(c+1,r+1)`. The lattice point is inherently shared and deduped (resolves AMB-003).

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-001]: `Edge` is the two separated cells sorted `(Lo <= Hi)`; `edgeBetween a b` returns `Some` iff orthogonally adjacent and is order-independent; `edgeOf cell dir` yields the same value.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-002]: North = `Row - 1`, East = `Col + 1`, South = `Row + 1`, West = `Col - 1`, matching the existing neighbour offsets.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-002]: `Vertex = { VCol; VRow }` lattice point; vertex `(c,r)` is cell `(c,r)`'s NW corner, so a cell's four corners and a vertex's ≤4 cells are fixed integer formulas.

## Accepted Deferrals
- None — all three ambiguities are resolved above.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 020-tile-edges-vertices`.
