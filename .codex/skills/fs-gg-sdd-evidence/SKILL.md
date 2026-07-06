---
name: fs-gg-sdd-evidence
description: Stage 8 of the FS.GG SDD lifecycle — fsgg-sdd evidence authors work/<id>/evidence.yml declaring how each obligation is satisfied. Carries the load-bearing satisfaction rule (only result:pass AND synthetic:false satisfies) and the kind/result vocabularies. Use after implementing, before verify.
---

# Evidence (stage 8)

`evidence` is where you declare what **proves** each obligation your tasks created
— after the code and tests exist. It is one of the two load-bearing authoring
contracts: a subtly wrong declaration leaves an obligation unsatisfied even though
the work is done.

## Command

```text
fsgg-sdd evidence --work <id>
fsgg-sdd evidence --work <id> --from-tests <path>   # pre-map new obligations to a proving test file
```

## Produces / consumes

- **Consumes:** `readiness/<id>/analysis.json` and the task obligations.
- **You author:** `work/<id>/evidence.yml`.
- **Tool refreshes:** `readiness/<id>/work-model.json`.
- **Next:** `verify` ([[fs-gg-sdd-verify]]).

## Auto-scaffolded obligations carry their origin refs

Each scaffolded obligation already records the `requirementRefs` and `planDecisionRefs` it
descends from — routed from the originating task's source lineage (so a `PD-###` plan-decision
obligation carries its `PD-###` **and** the `FR-###` it traces to). You can classify each
obligation honestly from its `evidence.yml` entry alone, with **no join back to `tasks.yml` by
task title**. (Other ref buckets stay empty on scaffolds; add them by hand if you want.)

`--from-tests <path>` additionally seeds each **newly scaffolded** obligation with a
verification-kind source pointing at `<path>` (a declared pointer; its existence is checked at
`verify`). It is inert without the flag. Neither behavior overwrites an obligation you have
already authored (no-clobber).

## The satisfaction rule (load-bearing)

> An obligation is **satisfied** only by a matching declaration whose `result` is
> `pass` **and** whose `synthetic` is `false`.

- `synthetic: true` + `result: pass` → disposition **synthetic**: discloses a
  stand-in, does **not** satisfy.
- `result: deferred` (or `kind: deferral`) → an accepted **deferral**, not a
  satisfaction.
- `result: fail | missing | stale | blocked` → not satisfied.

## Deferrals are first-class, not failures

A **deferral** (`result: deferred`, or `kind: deferral`) is an honest, accepted
disposition — *not* a failure and *not* something to hide. A work item can ship with
declared deferrals and still be coherent: it truthfully says "this obligation is not
satisfied yet, and here is why." That is strictly better than dressing an unfinished
obligation up as done.

The one thing never to do is convert a deferral into a **synthetic pass** to make a
count look greener. Only `result: pass ∧ synthetic: false` satisfies, so a synthetic
pass buys you nothing at `verify` and costs you the honesty the model exists to
protect. When an obligation genuinely can't be satisfied in this cut (blocked
upstream, out of scope for now, awaiting a dependency), **defer it and say why** —
that is the honest, first-class outcome. A run that ends *N real pass / M deferred /
0 synthetic* is a healthy run.

## Vocabularies

- **`kind`:** `implementation · verification · review · generated-view ·
  synthetic · deferral · note · missing` (`generatedview` also accepted). An
  **unrecognized `kind` silently becomes `verification`** — a typo does not fail
  the build, it just records a different kind than you wrote. Spell it exactly.
- **`result`:** `pass · fail · deferred · missing · stale · advisory · blocked`
  (trimmed and lowercased before matching).

## Example: a declaration that SATISFIES

<!-- fsgg-sdd:example corpus=evidence.yml mode=ref -->
```yaml
schemaVersion: 1
evidence:
  - id: EV001
    kind: verification
    subject:
      type: task
      id: T001
    artifacts: [tests/Product.Tests/InputMapTests.fs]
    result: pass
    synthetic: false
```

## Example: an accepted DEFERRAL (all four fields are REQUIRED)

A deferral (`result: deferred`, or `kind: deferral`) is a first-class, accepted
outcome — not a failure. The evidence gate **requires every deferral to carry all
four fields**, or it blocks with `evidence.missingDeferralRationale`:

- **`rationale`** — why this obligation is deferred rather than met now.
- **`owner`** — who owns the deferred work.
- **`scope`** — what, precisely, is deferred.
- **`laterLifecycleVisibility`** — how/when the deferral resurfaces downstream.

This is the canonical deferral shape (a verbatim fragment of
`docs/examples/lifecycle-artifacts/evidence.yml`, run through the evidence gate by
the skill↔gate doctest):

<!-- fsgg-sdd:example corpus=evidence.yml mode=contains -->
```yaml
  - id: EV003
    kind: deferral
    subject:
      type: task
      id: T002
    requirementRefs: [FR-002]
    clarificationDecisionRefs: [DEC-002]
    result: deferred
    synthetic: false
    rationale: A match-end/win condition is out of scope for this work item; rally scoring ships without it.
    owner: codex
    scope: match-end condition and win detection
    laterLifecycleVisibility: Re-open as a follow-on work item when match play is specified.
```

## Example: declarations that DO NOT satisfy

```yaml
schemaVersion: 1
evidence:
  - id: EV001
    kind: synthetic
    subject: { type: task, id: T001 }
    result: pass
    synthetic: true       # discloses a stand-in → disposition synthetic, NOT satisfied
  - id: EV002
    kind: verification
    subject: { type: task, id: T002 }
    result: fail
    synthetic: false      # not satisfied
```

A subject may be a `task` or a `requirement` (e.g.
`subject: { type: requirement, id: FR-002 }`); `artifacts`/`source` point at the
real test or proof, and `note` documents context.

## At scale: classifying an auto-expanded obligation graph

`tasks` fans your authored tasks out (per-FR, per-plan-decision, per-contract, and
keep-deferral-visible tasks), so a couple dozen authored tasks routinely become
**scores of obligations** — a real run went 18 tasks → 85 obligations. `evidence`
scaffolds one entry per obligation. Don't panic at the count and don't blanket-`pass`
it: classify the set **honestly, one obligation at a time**. Each scaffolded entry
already carries the `requirementRefs`/`planDecisionRefs` it descends from, so its own
`evidence.yml` line tells you what it's for — no join back to `tasks.yml` by task
title.

A repeatable sweep over the graph:

1. **Group by origin ref.** Read each entry's `requirementRefs`/`planDecisionRefs`;
   obligations for the same FR/PD classify together.
2. **Point real work at real proof.** For each obligation whose code + test exist, set
   `result: pass`, `synthetic: false`, and `artifacts`/`source` to the actual test or
   proof file. This is the only state that satisfies.
3. **Defer what isn't done — honestly.** For each obligation you can't satisfy in this
   cut, record a deferral (`result: deferred` / `kind: deferral`) with a `note` saying
   why. Deferrals are first-class (above); leaving some is expected, not a failure.
4. **Never synthesize to fill a gap.** If you're tempted to mark something
   `synthetic: true` just to clear it, defer it instead — a synthetic pass doesn't
   satisfy anyway.

The end state is every obligation in a truthful disposition — a mix of real passes and
declared deferrals, zero synthetic stand-ins.

## Bulk-authoring a large obligation set

Filling scores of entries by hand is error-prone, and you should **not** reach for a
throwaway script that stamps a blanket `result: pass`. Author in bulk with the
affordances the tool already gives you, then keep each classification honest:

- **Pre-map proofs at scaffold time.** `fsgg-sdd evidence --work <id> --from-tests
  <path>` seeds each *newly scaffolded* obligation with a verification-kind `source`
  pointing at `<path>` (a declared pointer, checked at `verify`). It never overwrites
  an obligation you've already authored (no-clobber), so it's a safe first pass over a
  large graph before you refine.
- **Drive edits from the origin refs.** Because every entry carries its
  `requirementRefs`/`planDecisionRefs`, a scripted or editor-macro pass can set the
  right `artifacts`/`result` per obligation *by the ref it descends from* — the same
  grouping the sweep above uses — instead of matching on task title.
- **Then classify honestly, per obligation.** Bulk authoring speeds the typing, not
  the judgement: every obligation still needs an individual, truthful
  `result`/`synthetic` (a real `pass` only where the proof exists; a `deferred`
  otherwise). A blanket `pass` across the set is exactly the dishonesty `verify` and
  the satisfaction rule are built to catch.

## Disambiguation

This lifecycle `evidence.yml` is the **SDD evidence contract**. A product
scaffolded by SDD may ship a *separate, unrelated* "evidence" document of its own
(e.g. a rendering product's visual-evidence report) — that is **not** this file.

## Real-vs-synthetic discipline

Prefer real filesystem/process/schema artifacts. When a synthetic stand-in is
unavoidable, set `synthetic: true` and disclose what real path it stands in for —
the tool will correctly record it as not-satisfying rather than letting a stand-in
masquerade as proof.

## Pitfalls

- A typo in `kind` (e.g. `test`) silently becomes `verification` — your declared
  kind is lost. Use the exact vocabulary.
- Marking work `synthetic: true` and expecting it to satisfy — it never does.

## Next

- `verify` — evaluate verification readiness over your evidence: [[fs-gg-sdd-verify]].

## Related

- [[fs-gg-sdd-authoring-contracts]] (full grammar + drift guard),
  [[fs-gg-sdd-tasks]] (the obligations), [[fs-gg-sdd-lifecycle]].

## Sources

- `docs/reference/authoring-contracts.md` (§ evidence.yml declarations);
  `docs/quickstart.md`.
