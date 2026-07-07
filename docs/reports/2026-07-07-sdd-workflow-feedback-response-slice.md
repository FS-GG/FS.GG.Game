# FS.GG SDD + governance workflow — hands-on feedback III (new-module response slice)

- **Date:** 2026-07-07
- **Author:** Claude (agent operator), driving the `fsgg-sdd` lifecycle end-to-end
- **Status:** Experience report / feedback
- **Basis:** One work item taken charter → ship and merged this session —
  `006-collision-resolution-layer` (the `Resolution` module: `pushOut` / `slide` / `knockback`),
  shipped as PR #18. `fsgg-sdd` v0.8.0, light (advisory) governance profile.
- **Relationship to the prior reports:** Third in a series. See
  [`2026-07-07-sdd-workflow-feedback.md`](2026-07-07-sdd-workflow-feedback.md) (I — 004/005) and
  [`2026-07-07-sdd-workflow-feedback-mid-lifecycle-revision.md`](2026-07-07-sdd-workflow-feedback-mid-lifecycle-revision.md)
  (II — 005b, the mid-lifecycle spec revision). This report does **not** re-litigate their findings;
  it records what **006** added: it was the **first new-module and first response-layer slice**, it ran
  the **cleanest forward path of the three** (zero manual intervention), and it is where I **pre-empted**
  the disposition defect the first two reports flagged — turning a two-report complaint into a concrete,
  reusable operator technique.
- **Purpose:** Concrete what-worked / what-didn't / improvements for the SDD + governance tool owners,
  grounded in specific stages. Not a design proposal.

---

## Summary

006 is the **control case** for the series: I got the spec right up front, applied the lesson from
reports I/II about clarification-decision disposition, and the ten stages ran charter → ship **without a
single manual fix** — no `analyze` block, no digest surgery, no re-flow. That is worth recording as much
as the friction was: it isolates that the pain in the earlier reports was **specific and avoidable**
(a spec that was wrong; a generator that strands one decision), not pervasive ceremony. The one genuinely
new observation is about **testing a response layer**: the meaningful invariant is the cross-layer
*consumer contract* (detection → response), so the constraint face naturally — and correctly — pulls the
integration test across the module boundary.

## What worked

1. **The disposition defect is now pre-emptable — this is the actionable headline.** Reports I and II
   both blocked at `analyze` because the `tasks` generator failed to dispose a clarification decision
   (always the surface/type one), forcing a hand-edit to a task's `sourceIds`. This session I **referenced
   every `DEC-###` in a plan-decision (`PD-###`) line** in `plan.md` (e.g. `PD-002 … (DEC-001) (DEC-002)`).
   The generator then routed each decision onto the corresponding task's `sourceIds`, and `analyze`
   reported `analysisMissingDispositions: 0` **first try**. So the reliable operator technique is: *make
   sure each DEC appears in at least one PD line before running `tasks`.* It works, and it kept 006 clean
   — but see "what didn't": that the operator has to know this at all is the residual defect.
2. **The clean forward path proves the ceremony is not the cost.** Ten stages, zero manual intervention,
   ~one command per stage. When the inputs are right the lifecycle is genuinely low-friction to drive; the
   `next:`-line progression and `--text` counters carried the whole run. The earlier reports' friction was
   real but *localized* — this run is the counterfactual that shows it.
3. **The constraint face pulled the right cross-layer test for a response module.** A response function
   has no meaningful *intrinsic* invariant — `pushOut` is just subtraction. The invariant that matters is
   the **consumer contract**: `pushOut (aabbContact a b)` must actually separate `a` from `b`. Writing that
   FsCheck property forced the `Resolution` test to depend on `Geometry` and exercise the **detection →
   response handoff**, not the function in isolation. The "invariants as a tested property" doctrine
   naturally produced the integration test the layering split most needs. Good emergent behavior.
4. **A new module dropped into the surface/baseline machinery with no friction.** First time a slice added
   a whole `.fs`/`.fsi` module (not just functions on an existing one) plus a new `.fsproj` compile entry.
   The surface-baseline refresh picked it up as exactly one new line (`FS.GG.Game.Core.Resolution`), the
   drift gate stayed green, and nothing in the lifecycle treated a new module differently from a new
   function. The authored-vs-generated split scaled to the coarser change cleanly.
5. **Advisory governance: three-for-three clean.** Across 005b, the two docs reports, and 006, the
   light/advisory profile has never once produced a false gate or required attention. The
   "SDD reports readiness / Governance enforces" separation is holding at zero operator cost for
   additive core work — exactly what an advisory posture should feel like.

## What didn't (friction, most to least consequential)

1. **The disposition pre-empt is a workaround, not a fix — the operator should not need to know it.**
   I only ran clean because I *remembered from two prior reports* to thread every `DEC` into a `PD`
   line. An operator who writes a perfectly good plan that happens to mention a decision only in prose
   (or only in the `## Design` section, not on a `PD-###` line) will still hit the `analyze` block. The
   generator should dispose every clarification decision by default; failing that, `analyze`'s diagnostic
   should say *"DEC-00X is not on any task — add it to a PD line or a task's sourceIds,"* not the generic
   "no current task disposition." The knowledge that fixes this lives in feedback reports, not in the tool.
2. **`evidence` verbosity, third confirmation — and now clearly the dominant cost of a clean run.** With
   the forward path otherwise frictionless, authoring 16 ~20-line obligation blocks (mostly null optional
   fields) was, by wall-clock and tokens, **the majority of the whole work item's lifecycle effort**. When
   everything else is one-command-per-stage, the evidence stage's hand-authoring stands out even more
   starkly than in reports I/II. This is the single highest-value ergonomic fix remaining.
3. **A transient `evidence` warning that cleared on re-run.** The first `evidence` run reported
   `succeededWithWarnings` with `diagnostics: 1`; re-running the exact same command reported `diagnostics: 0`
   with no edit in between (a linter had reformatted my inline `subject: { type; id }` YAML maps to block
   form between the runs). A diagnostic that appears and then vanishes with no authored change is confusing
   — it reads as a race between the linter and the validator. Non-blocking, but it briefly cost trust in
   the counter.
4. **(Carried) `null` → `"null"` round-trip** in the evidence optional fields, and the general point that
   the evidence obligation shape is far more verbose than its decision content. Unchanged from I/II.

## Suggested improvements

- **Dispose every clarification decision at the generator (the top fix, now raised by all three reports).**
  Have `tasks` route each `DEC-###` to a disposing task by default. If a decision genuinely maps to no
  task, make `analyze` name it and the concrete remedy. Until then, document the "put every DEC on a PD
  line" technique **in the `plan`/`tasks` stage guidance**, so operators don't have to rediscover it from
  feedback reports.
- **Slim `evidence` (carried, now the clear #1 ergonomic cost).** On a clean run the evidence stage is the
  only stage that isn't roughly one command — omit always-null fields on write, or add a terse
  `--satisfy T001=pass --artifacts …` form, or infer obligation results from declared test/build artifacts.
- **Make the `evidence` warning deterministic.** Either run the linter before validation (so the reported
  diagnostics reflect the final file) or suppress the transient reformat-induced warning — a diagnostic
  that disappears on an identical re-run undermines the counter's credibility.
- **(Carried) Fix the evidence `null` → `"null"` round-trip.**

## Agent-fit note

The three reports now bracket the workflow: I showed the happy path with friction in authoring, II showed
the hard case (spec wrong after implementation) and where recovery is uneven, and III shows the happy path
executed *cleanly* once the two known traps (a wrong spec; an undisposed decision) are avoided. For an
agent, the lesson is that the lifecycle is **highly drivable and cheap when its two sharp edges are known
in advance** — and both edges are things the tool could round off itself (auto-dispose decisions; slim the
evidence shape). The constraint-face discipline continues to earn its keep: in II it caught a real
algorithm bug, and in III it pulled the cross-layer detection→response test that the module split most
needed. Net across three items: the model's *checks* are consistently the best part; the remaining cost is
concentrated in **evidence authoring** and **one generator gap the operator currently has to work around
by hand.**
