# FS.GG SDD workflow — hands-on feedback (agent operator)

- **Date:** 2026-07-07
- **Author:** Claude (agent operator), driving the `fsgg-sdd` lifecycle end-to-end
- **Status:** Experience report / feedback
- **Basis:** Two work items taken charter → ship and merged this session —
  `004-collision-circle-manifolds` (PR #13) and `005-collision-raycast` (PR #14) — plus the earlier
  design-doc work. `fsgg-sdd` v0.8.0, light governance profile.
- **Purpose:** Concrete what-worked / what-didn't / improvements, grounded in specific stages and
  failures, for the SDD tool owners. Not a design proposal.

---

## Summary

The lifecycle did its core job well: for each of 004 and 005, "is this ready to ship" became a
**deterministic, inspectable fact** rather than a judgement call, and the machine contract
(JSON/`--text`) made the whole thing drivable by an agent without guessing. The friction was almost
entirely in **authoring ergonomics** (verbosity, one opaque block, a linter round-trip bug), not in
the model itself. Nothing blocked me for long; everything that blocked, blocked *loudly and for a
real reason*.

## What worked

1. **Self-describing progression.** Every command ends with `next: fsgg-sdd <stage>` and a
   `stages:` line. I never had to remember the order — I asked the tool. The canonical
   `charter → … → ship` chain is short enough to hold in the head anyway.
2. **`--text` counters are genuinely diagnostic.** When `analyze` blocked on 005, the JSON `outcome`
   alone said little, but `--text` surfaced `analysisMissingDispositions: 1` and the diagnostic
   carried `relatedIds: ["DEC-001"]` — that pinpointed the exact fact. The named counters
   (`blockingAmbiguities`, `checklistFailedBlocking`, `missingDispositionCount`, …) are the right
   idea and paid off.
3. **Scaffolds lower the activation energy.** `charter`/`specify`/`plan`/`tasks`/`evidence` each
   emit a skeleton with the required headings/ids already present (and `evidence` enumerates *every*
   obligation). I filled structure in rather than inventing it, and the required-heading set was
   discoverable from the scaffold.
4. **The coverage grammar worked first try.** Writing FRs as `- FR-###: … (covers AC-###)` and ACs
   as `- AC-### [US-###] [FR-###]: …` passed `checklist` cleanly on both items (7/7 and 6/6). The
   grammar is fiddly but well-documented in the stage skills, and the failure mode (counted but
   uncovered) is explained up front.
5. **Authored-source vs generated-view split is clean and correct.** `work/<id>/` is mine and
   committed; `readiness/<id>/` is generated and gitignored. Source digests (sha256 in the snapshot
   blocks) make staleness detectable rather than a matter of trust.
6. **Evidence satisfaction is unambiguous.** "`result: pass` AND `synthetic: false` satisfies" is a
   single, testable rule; there was never doubt whether an obligation counted.
7. **Consistency checks actually bite.** `analyze` refusing to pass with a clarification decision
   that no task disposed (DEC-001) is exactly the kind of cross-artifact check that justifies the
   ceremony — it caught a real dangling reference, not a formatting nit.
8. **It composed with the repo's own gates.** The determinism goldens I authored per item slotted
   straight into the existing `gate.yml` zero-match guard (6 → 14 → 21 goldens) with no lifecycle
   friction. SDD stayed in its lane (readiness) and left enforcement to CI.

## What didn't (friction, most to least annoying)

1. **`evidence.yml` round-trips `null` into the string `"null"`.** On both 004 and 005, after I
   authored `rationale: null` (and `owner`/`scope`/`laterLifecycleVisibility: null`), a
   formatter/linter step rewrote them to `rationale: "null"` — a real null turned into the
   four-character string. It was non-blocking (ship still succeeded), but it is semantically wrong
   and would mislead any consumer that reads those fields. **This looks like a bug.**
2. **`evidence.yml` is very expensive to author by hand.** Each obligation is a ~20-line block with
   many almost-always-null fields (`syntheticDisclosure`, `rationale`, `owner`, `scope`,
   `laterLifecycleVisibility`). 004 had 19 obligations, 005 had 17 — ~340 lines of largely
   boilerplate YAML per item, and it was by far the most tedious stage. The signal (kind, result,
   synthetic, artifacts, notes) is a small fraction of the bytes.
3. **The `analyze` missing-disposition block was opaque about the fix.** The auto-generated
   `tasks.yml` disposed `DEC-002`/`DEC-003` via task `sourceIds`, but not `DEC-001`, and the
   diagnostic said only "no current task disposition." I had to reverse-engineer that (a) disposition
   comes from `sourceIds`, not the `decisions:` field — which was `[]` on every task and appears
   **vestigial** — and (b) the fix was to add `DEC-001` to the surface task's `sourceIds`. Worse, it
   was **inconsistent**: 004 sailed through with structurally similar decisions; 005 blocked. The
   generator produced tasks that its own downstream `analyze` then rejected.
4. **Overlapping ambiguity counters.** `unresolvedAmbiguities` stayed at `4` even after I resolved
   everything and `blockingAmbiguities`/`remainingAmbiguities` were `0`. Three counters with
   different semantics, only one of which gates — it took a beat to trust that `blocking = 0` meant
   "go."
5. **`clarify` writes no scaffold when it blocks.** Unlike the other early stages, a blocked
   `clarify` produced `changedArtifacts: 0` and no `clarifications.md`, so I authored it from
   scratch against the skill example. A skeleton (even a blocked one) would match the rest of the
   lifecycle.
6. **The "author fully before advancing" constraint is implicit.** Because `plan`/`tasks`/`evidence`
   embed upstream sha256 digests, iterating on `spec.md` after `plan` would stale them. That is the
   right design, but nothing warns you at `plan` time that the authoring window upstream is now
   "closed"; you learn it by having to re-run.
7. **`specify`'s seeded story is lifecycle-boilerplate.** The scaffold's `US-001` ("As a maintainer,
   I can specify … after chartering the work item") and its single meta `AC-001` are about the
   process, not the feature — they must be fully overwritten. Minor, but the seed reads as filler
   rather than a starting point.
8. **`outcome: noChange` needs a second look to interpret.** A validated-clean `clarify` reported
   `noChange`, which reads as neutral/negative until you also check `diagnostics: 0` and the `next:`
   line. The outcome vocabulary alone doesn't tell you "good vs stuck."

## Suggested improvements

- **Fix the evidence null-quoting** so `null` survives the round-trip (highest-value, clearly a bug).
- **Slim the evidence obligation shape:** omit always-null optional fields on write, or add a terser
  satisfied form (e.g. `fsgg-sdd evidence --satisfy T001=pass --artifacts …`), or infer obligation
  results from declared test/build artifacts. Authoring 17–19 near-identical blocks is the sharpest
  ergonomic cost.
- **Make dispositions self-healing or self-explaining:** have the `tasks` generator link *every*
  clarification decision to a task by default (so `analyze` never blocks on an artifact the
  generator itself emitted), and if it still blocks, have the diagnostic name the concrete fix
  ("add `DEC-001` to a task's `sourceIds`"). Also: reconcile the `decisions:` vs `sourceIds:` task
  fields — one of them looks unused.
- **Scaffold `clarifications.md`** on a blocked `clarify`, like the other early stages.
- **Collapse or clearly rank the ambiguity counters** so it is obvious which one gates.
- **Warn at `plan`** that advancing freezes the upstream digest set (a one-line "upstream authoring
  window closing" note), so agents/humans finish iterating before crossing that line.
- **Give `specify` a feature-shaped seed** (or no seed story at all) rather than a
  process-about-the-process `US-001`.

## Agent-fit note

For an **agent** operator specifically, the strongest features were the deterministic JSON/`--text`
contract, the `next:`-driven progression, and the fact that "ready" is a computed fact — those let
me drive 20+ stages across two items without a human in the loop and still produce a reviewable,
provenance-carrying result. The weakest point for an agent is evidence verbosity: it is pure token
cost with little decision content. Netting out, the lifecycle earned its ceremony on both items —
the one place it *blocked* me (the DEC-001 disposition) was a genuine defect it caught, which is the
best possible advertisement for the checks.
