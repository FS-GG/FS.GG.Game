# FS.GG SDD + governance workflow — hands-on feedback II (mid-lifecycle revision)

- **Date:** 2026-07-07
- **Author:** Claude (agent operator), driving the `fsgg-sdd` lifecycle end-to-end
- **Status:** Experience report / feedback
- **Basis:** One work item taken charter → ship this session — `005b-collision-sat-obb`
  (SAT/OBB convex manifolds; `ConvexPolygon` + `obbPolygon` + `polygonContact`), shipped as PR #16.
  `fsgg-sdd` v0.8.0, light (advisory) governance profile.
- **Relationship to the prior report:** This is a **companion** to
  [`2026-07-07-sdd-workflow-feedback.md`](2026-07-07-sdd-workflow-feedback.md) (004/005, PRs #13/#14).
  I do **not** re-list findings that report already covered unless 005b **corroborated** or **sharpened**
  them; the headline of this session is a scenario the first report never hit — **the spec was wrong and
  had to be revised *after* implementation**, which stress-tested the parts of the lifecycle that assume
  authoring flows strictly forward.
- **Purpose:** Concrete what-worked / what-didn't / improvements for the SDD + governance tool owners,
  grounded in specific stages and one real algorithm defect. Not a design proposal.

---

## Summary

The lifecycle's ceremony **paid for itself in correctness this session**, not just in provenance: the
constraint-face FsCheck property I was required to author caught a genuine algorithm bug (the SAT
full-containment case) *before* ship — the naive minimum-translation-vector depth I first wrote did not
actually separate a contained polygon. That is the strongest possible argument for the "determinism/
invariants as a tested property" doctrine: it found a real defect that types and a couple of goldens
would have missed.

The friction was concentrated in the **backward edit** the bug forced. Fixing the algorithm meant the
authored `spec`/`clarify`/`plan` prose (which had committed to a *centroid-delta* normal) was now false,
so I had to revise upstream and re-run `checklist → plan → tasks → analyze`. The forward path is smooth;
the **re-flow after an upstream edit is not uniform** — some stages self-heal their source digests and
one silently mutates the authored file instead. Everything that blocked, blocked for a real reason, but
the *recovery ergonomics* are where the model showed its edges.

## What worked

1. **The constraint face caught a real bug — this is the whole point.** FR-002 obliged me to prove
   "translating `a` by `−Normal × Depth` separates the polygons." I wrote that as an FsCheck property
   over randomly rotated OBBs. It failed on the first run with `residual == depth` — a fully-contained
   polygon whose reported MTV did nothing. The design doc had even flagged this ("add the full-containment
   correction"), but it was the **required invariant test**, not my reading, that forced me to confront
   it. A process that makes you write the separating-property test *before* you can claim the requirement
   is a process that catches MTV bugs. Best advertisement for the ceremony I've seen yet.
2. **`--text` counters remain the single best affordance for an agent.** Same as last report, reconfirmed:
   `analysisMissingDispositions`, `planStaleDecisions`, `checklistFailedBlocking`, `taskBlockingFindings`
   each named the exact gate state. When the mid-lifecycle re-run went sideways, the counters + the
   `relatedIds` on each diagnostic (`["DEC-001"]`, `["PD-007"]`) took me straight to the offending id.
3. **Digest-based staleness detection did its job.** After I edited `spec.md` and `clarifications.md`,
   the downstream stages *knew*. Nothing trusted stale prose. This is exactly the guard you want; the
   friction below is about how the tool helps you *clear* the staleness, not that it detected it.
4. **`tasks` regeneration is no-clobber on hand edits.** After I re-ran `tasks` (needed to refresh its
   plan digest), it preserved the manual `decisions: ["DEC-001"]` / `sourceIds` edit I had added to the
   contract-surface task. Regenerating did not stomp my correction — the right behavior, and reassuring
   when you're forced to re-run a generator over a file you've hand-patched.
5. **Governance stayed correctly out of the way.** Light/advisory profile meant governance pointers were
   compatibility facts, never gates. For a purely additive Tier-1 core slice this was right; I never once
   fought governance, and `ship` reported SDD-owned readiness without pretending to enforce a
   protected-boundary handoff. The separation of "SDD reports readiness / Governance enforces" held.
6. **The surface-baseline gate composed cleanly again.** Regenerating `readiness/surface-baselines/`
   yielded exactly one new line (`FS.GG.Game.Core.ConvexPolygon`) — functions on a module add no type
   rows — so the diff was trivially reviewable and the drift gate stayed honest.

## What didn't (friction, most to least consequential)

1. **Re-flow after an upstream edit is non-uniform, and `plan` mutates the authored file.** This is the
   new, sharp finding. After editing `spec`/`clarify`:
   - `checklist` re-run **self-healed**: it rewrote its own `Source Snapshot` sha256s to the new spec/
     clarify digests and passed. Good.
   - `plan` re-run did **not** self-heal. It left its stale `Source Snapshot` digests in place and instead
     **appended a new decision to my authored `plan.md`** — `PD-007 [PD-001] stale: Source specification …
     changed`. So the "authored source" now had a tool-injected line, its snapshot was still wrong, and
     `tasks` then blocked on `failedPlanPrerequisite` (stale PD-007). Recovery required me to (a) hand-edit
     three sha256 digests in the plan snapshot and (b) delete the injected `PD-007` line. Two stages, two
     *different* recovery models (checklist auto, plan manual-and-mutating), for the same class of upstream
     edit. An agent can do this, but it is guesswork the first time, and mutating a file the model calls
     "authored" is surprising.
2. **The `DEC-001` missing-disposition block recurred — it is systematic, not a fluke.** The prior report
   flagged that `analyze` blocked 005 on a clarification decision (`DEC-001`) that the `tasks` generator
   itself failed to dispose. **005b hit the identical wall**: the generator disposed `DEC-002`/`DEC-003`/
   `DEC-004` via `sourceIds` but again left `DEC-001` (the surface-type decision) undisposed, so `analyze`
   blocked until I hand-added it to the contract-surface task. Two reports, three of four items affected,
   always the *surface* decision. This is now a reproducible generator defect, not an anecdote: the item
   that introduces a public type reliably strands that type's clarification decision.
3. **`evidence.yml` verbosity, reconfirmed and quantified.** 18 obligations → ~330 lines, of which the
   decision-bearing content (`kind`/`result`/`synthetic`/`artifacts`/`notes`) is a small minority; the
   rest is `syntheticDisclosure`/`rationale`/`owner`/`scope`/`laterLifecycleVisibility` set to null on
   every satisfied obligation. Unchanged from last report; it remains the single biggest token/effort sink
   and the least decision-dense stage.
4. **`null` still round-trips to the string `"null"`.** I pre-empted it this time by writing the quoted
   form deliberately, but the underlying formatter bug the prior report identified is still live: a real
   `null` in the optional deferral fields does not survive. (Confirming, not new.)
5. **No warning that advancing past `plan` freezes the upstream authoring window.** The prior report
   predicted this as a latent risk; 005b is the case that *realized* it. Because the bug surfaced during
   implementation (after `plan`/`tasks`/`analyze`), I had to pay the full backward re-flow. A one-line
   note at `plan` ("advancing snapshots spec/clarify/checklist; later upstream edits require a re-run")
   would have set the expectation before I crossed the line — and mattered precisely because
   implementation legitimately *does* discover spec errors.

## Suggested improvements

- **Make upstream-edit recovery uniform and non-mutating.** When a source digest goes stale, every
  downstream stage should behave the same way on re-run: **refresh its own snapshot** (as `checklist`
  does) rather than injecting a `PD-###`/finding into the authored file (as `plan` does). If the tool
  wants to force an explicit acknowledgement, prefer a `fsgg-sdd plan --accept-upstream` flag that
  rewrites the snapshot, over silently editing authored prose. Mutating an "authored source" breaks the
  authored-vs-generated contract the model otherwise keeps clean.
- **Fix the `DEC-001`/surface-decision disposition defect at the generator.** Have `tasks` link *every*
  clarification decision to a disposing task by default — especially the surface/type decision, which is
  the one that reproducibly strands. This is the second report to raise it; it now has a clear repro
  (any item adding a public type). Failing that, the `analyze` diagnostic should name the exact fix
  ("add `DEC-001` to a task's `sourceIds`") instead of "no current task disposition."
- **Add a `plan`-time "authoring window closing" note.** One line stating that advancing freezes the
  upstream digest set, so operators finish iterating — or knowingly accept a re-run — before crossing.
  005b is concrete evidence this is worth it: real implementations find real spec bugs.
- **(Carried) Slim `evidence.yml`** — omit always-null fields on write, or offer a terse
  `--satisfy T001=pass --artifacts …` form. Still the sharpest ergonomic cost.
- **(Carried) Fix the evidence `null` → `"null"` round-trip.**

## Agent-fit note

For an agent, the session's lesson is that the lifecycle is **strongest exactly when the naive path is
wrong**. The forward chain is easy to drive; the value showed up when (a) a required invariant test
caught an algorithm defect I would otherwise have shipped, and (b) the digest guards refused to let me
advance on prose that no longer matched the code. Both are the ceremony earning its keep. The cost is
that **backward** motion — legitimate, because implementation discovers spec errors — is less
well-supported than forward motion: one stage self-heals, another mutates the file it calls authored, and
nothing warns you the window is closing. Smoothing the re-flow (uniform snapshot refresh, a closing-window
note) would make the one genuinely hard workflow — "the spec was wrong, fix everything coherently" — as
clean as the happy path already is. Netting out: ceremony **more** justified than the prior report
concluded, because this time it caught a correctness bug, not just a dangling reference.
