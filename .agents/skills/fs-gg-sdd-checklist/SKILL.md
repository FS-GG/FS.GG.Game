---
name: fs-gg-sdd-checklist
description: Stage 4 of the FS.GG SDD lifecycle — fsgg-sdd checklist reviews requirements quality and computes FR→AC coverage. Carries the load-bearing coverage-line grammar (- FR-###: … (covers AC-###)) that silently fails if mis-formatted. Use after clarify, before plan.
---

# Checklist (stage 4)

`checklist` is the requirements-quality gate before planning — "unit tests for the
spec." Its most important job is computing **FR→AC coverage**, and that depends on
a strict, easy-to-get-subtly-wrong grammar. Get the grammar right and coverage is
real; get it wrong and a requirement is silently reported uncovered.

## Command

```text
fsgg-sdd checklist --work <id>
```

## Produces / consumes

- **Consumes:** `work/<id>/spec.md`, `work/<id>/clarifications.md`.
- **You author:** `work/<id>/checklist.md`.
- **Tool refreshes:** `readiness/<id>/work-model.json`.
- **Next:** `plan` ([[fs-gg-sdd-plan]]).

## The coverage line (load-bearing grammar)

A functional requirement is marked **covered** only when a *strict-scan* parser
finds a list item that:

1. starts with a literal `- `,
2. then `FR-` followed by **three or more digits** (case-insensitive),
3. then a literal `:`,
4. then prose,
5. with the acceptance reference (`AC-###`, optionally `US-###`) **on the same
   line.**

**Accepted** (each line establishes coverage):

```text
- FR-001: W/S move the left paddle. (covers AC-002)
- FR-014: Ball serves toward the loser. (Stories: US-003; Acceptance: AC-009)
```

**Counted but NOT covered** (a separate loose scan `\bFR-\d{3,}\b` lists the
requirement, so it *looks* present but establishes no coverage):

```text
**FR-001** W/S move the left paddle. (AC-002)     ← bold id, not a "- FR-001:" item
- FR-001 — moves the paddle (AC-002)              ← no colon after the id
(covers AC-002)                                    ← AC ref on its own line
```

If `checklist` reports a requirement uncovered that you "know" you wrote, it is
almost always one of these three form errors.

## Required headings and id prefixes

Sections: **Source Specification, Source Clarifications, Source Snapshot, Checklist
Items, Review Results, Accepted Deferrals, Blocking Findings, Advisory Notes,
Lifecycle Notes.**

Id prefixes: `CHK-###` (checklist items), `CR-###` (review results).

## Example

<!-- fsgg-sdd:example corpus=checklist.md mode=ref -->
```markdown
# Checklist

## Source Specification
work/001-two-player-volley/spec.md

## Checklist Items
- **CHK-001**: Every FR has at least one acceptance reference on its coverage line.
- **CHK-002**: No FR is ambiguous about timing or input source.

## Review Results
- **CR-001**: Coverage complete — see lines below.

- FR-001: W/S move the left paddle. (covers AC-001)
- FR-002: Serve targets the prior-rally loser. (covers AC-002)
```

## Pitfalls

- The three rejected forms above — bold id, missing colon, off-line AC ref.
- Fewer than three digits in the id (`FR-1`) — needs `FR-###` (3+ digits).
- **`## Blocking Findings` empty-section rule.** A bullet here blocks `plan` unless
  it is a disclaimer — a bare `- None.` / `- No blocking findings.` is safe and does
  **not** block. But a `No …` bullet that names a real gap (`- No tests cover
  FR-003.`) is a genuine finding and *does* block, by design. Full grammar:
  `docs/reference/authoring-contracts.md`.

## Reaching `checklistReady`

There is **no manual status transition to author**. A clean `checklist` review
writes `status: checklistReady` directly; an unclean one writes `needsCorrection`.
If `plan` reports *"Checklist status '…' is not checklistReady"*, clear the blocking
findings / stale reviews and **re-run `fsgg-sdd checklist`** — it re-promotes the
status. Do not hand-edit the status field.

## Next

- `plan` — turn covered requirements into a technical plan: [[fs-gg-sdd-plan]].

## Related

- [[fs-gg-sdd-authoring-contracts]] (the full grammar + drift guard),
  [[fs-gg-sdd-lifecycle]].

## Sources

- `docs/reference/authoring-contracts.md` (§ Acceptance coverage line);
  `docs/quickstart.md`.
