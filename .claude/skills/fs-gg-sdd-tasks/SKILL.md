---
name: fs-gg-sdd-tasks
description: Stage 6 of the FS.GG SDD lifecycle — fsgg-sdd tasks generates the typed, dependency-ordered task graph in work/<id>/tasks.yml (NOT tasks.md), where each task back-references requirements, decisions, required skills, and required evidence. Use after plan, before analyze.
---

# Tasks (stage 6)

`tasks` turns the plan into a **typed task graph**. Unlike legacy Spec Kit's
Markdown `tasks.md`, the native SDD source is `work/<id>/tasks.yml` — a
schema-versioned file where each task carries stable cross-references that the work
model resolves into links. This is what makes "is this task done?" a checkable
fact later.

## Command

```text
fsgg-sdd tasks --work <id>
```

## Produces / consumes

- **Consumes:** the plan cascade (`spec` → `clarify` → `checklist` → `plan`).
- **You author:** `work/<id>/tasks.yml`.
- **Tool refreshes:** `readiness/<id>/work-model.json`.
- **Next:** `analyze` ([[fs-gg-sdd-analyze]]).

## `tasks.yml` schema

Each task has: a stable `id`, `status`, `owner` (the agent/role assumed),
back-references to `requirements` and `decisions`, `dependencies`, `requiredSkills`
(capability tags the implementer must have loaded), and `requiredEvidence` (the
obligation ids that close the task).

<!-- fsgg-sdd:example corpus=tasks.yml mode=ref -->
```yaml
schemaVersion: 1
tasks:
  - id: T001
    title: Declare the input-map public surface
    status: pending
    owner: codex
    requirements: [FR-001]
    decisions: [DEC-001]
    dependencies: []
    requiredSkills: [fs-gg-sdd-project]
    requiredEvidence: [EV001]
  - id: T002
    title: Implement W/S paddle motion and serve targeting
    status: pending
    owner: codex
    requirements: [FR-001, FR-002]
    decisions: [DEC-001]
    dependencies: [T001]
    requiredSkills: [fs-gg-sdd-project]
    requiredEvidence: [EV002]
```

## Notes

- `requirements`/`decisions` must reference ids that exist in `spec.md` /
  `clarifications.md`; the work model resolves them into `LinkedTaskIds` and
  surfaces dangling references as diagnostics.
- `requiredEvidence` ids (`EV###`) are the obligations you will later satisfy in
  `evidence.yml` — see [[fs-gg-sdd-evidence]].
- `dependencies` define the order `analyze`/`verify` read as the task graph; keep
  them acyclic.

## Pitfalls

- Writing `tasks.md` instead of `tasks.yml` — the native SDD source is the typed
  YAML. (`tasks.md` is the legacy Spec Kit artifact.)
- Referencing an `FR-###` or `DEC-###` that was renumbered upstream — fix the spec
  ids first, then the tasks.

## Next

- `analyze` — check cross-artifact readiness, then implement: [[fs-gg-sdd-analyze]].

## Worked example

A complete, valid, copy-adaptable `tasks.yml` (with a matching `clarifications.md`,
`checklist.md`, and `evidence.yml` for one coherent work item) ships at
`docs/examples/lifecycle-artifacts/`. Every example there is validated against the
live parser on each build, so it never drifts from the tool.

## Related

- [[fs-gg-sdd-evidence]] (the `requiredEvidence` obligations), [[fs-gg-sdd-lifecycle]].

## Sources

- `docs/quickstart.md`; the shipped worked example
  `docs/examples/lifecycle-artifacts/tasks.yml`.
