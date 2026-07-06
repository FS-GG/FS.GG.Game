---
name: fs-gg-sdd-plan
description: Stage 5 of the FS.GG SDD lifecycle — fsgg-sdd plan authors the technical implementation plan (work/<id>/plan.md, plus work/<id>/contracts/ when contracts are authored) from the covered spec. Use after checklist, before tasks.
---

# Plan (stage 5)

`plan` is where WHAT becomes HOW. With a covered, clarified spec in hand, you
author the technical approach: the design, the public surface to declare, the
schema/migration posture, the structured contracts, and the tests that will cover
the change. Per the doctrine, signatures come before implementation — the plan
names the surface you will declare.

## Command

```text
fsgg-sdd plan --work <id>
```

## Produces / consumes

- **Consumes:** `spec.md` + `clarifications.md` + `checklist.md` (the upstream
  cascade — a missing/malformed upstream starves this stage).
- **You author:** `work/<id>/plan.md`, and `work/<id>/contracts/` when you author
  interface/schema contracts.
- **Tool refreshes:** `readiness/<id>/work-model.json`.
- **Next:** `tasks` ([[fs-gg-sdd-tasks]]).

## What a plan covers

For a lifecycle/contracted (Tier 1) change, the plan identifies:

- the **public surface** to declare (signatures first — `.fsi` where the language
  supports it) and the surface baseline to maintain;
- the **structured contracts** (under `contracts/`) and their schema/migration
  posture;
- the **generated views** the change touches and their currency/stale behavior;
- the **agent-facing behavior**, keeping Claude and Codex equivalent;
- any optional **Governance** integration;
- the **tests and fixtures** that cover stale or conflicting artifacts — prefer
  real fixtures over mocks.

A Tier 2 (internal) change needs a plan and tests, but signatures and baselines
stay unchanged.

## Example skeleton

<!-- fsgg-sdd:example corpus=plan.md mode=ref -->
```markdown
# Plan

## Technical Context
F# / Elmish-style command loop. Input mapping is a new public surface (Tier 1).

## Constitution Check
III Public Surface: declare `InputMap.fsi` before `.fs`. V MUE: key events are
messages; paddle motion is a pure transition; rendering is the edge effect.

## Design
- Pure `update : Msg -> Model -> Model * Effect list`.
- `contracts/input-map.md` defines the W/S → paddle-delta mapping.

## Tests
- Real-fixture transition tests (fail-before/pass-after) for FR-001, FR-002.
```

## Pitfalls

- Planning implementation without declaring the surface first (violates Principle
  III). Name the signatures in the plan.
- Authoring `contracts/` content that contradicts the spec — the work model
  surfaces prose-vs-structured conflicts as diagnostics rather than merging them.

## Next

- `tasks` — break the plan into a typed task graph: [[fs-gg-sdd-tasks]].

## Related

- [[fs-gg-sdd-lifecycle]], [[fs-gg-sdd-tasks]].

## Sources

- `docs/quickstart.md`; `.fsgg/constitution.md` (Principles III, V, VI).
