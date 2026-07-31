# Independent review and material filing

Every item gets one independent critique cycle before merge. The implementer and critic are different
agents. The critic receives the issue, acceptance criteria, declared `Paths:`, exact PR head SHA,
complete diff, and test evidence; it does not receive the implementer's conclusions. The critic may
read code, history, issues, PRs, and the board, but **must not edit the implementation or push commits**.

The host reserves a slot for the critic and keeps the implementing worker alive until confirmation.
The critic reviews requirements coverage, correctness, regressions, tests/evidence, architecture and
ownership boundaries, release obligations, and touch-set honesty.

## Root cause, dedupe, and materiality

For every candidate finding, the critic searches the relevant code and history for the cause, then
searches open and closed issues, PRs, comments, and the board for that cause rather than only the
surface symptom. Reuse an existing item when it already carries the cause and add the new evidence
there.

A finding is **material** only when the evidence shows at least one of:

- acceptance criteria are unmet, or observable correctness, compatibility, security, data integrity,
  performance intent, or releaseability is at risk;
- a test or gate can report green without checking its declared subject;
- an architecture or ownership violation creates a concrete defect or blocks safe evolution; or
- bounded hardening prevents a measured recurring failure, retry, operational burden, or meaningful
  maintenance cost.

Style, naming taste, speculative edge cases, optional refactors, “could be cleaner” observations, and
findings already repaired in the current PR are not material new work. Record them in the review
comment when useful, but **never create an issue, board row, blocker edge, or follow-up queue entry for
them**. Uncertainty is not materiality; measure or omit.

## Disposition and repair bounds

These machine-readable literals are part of the review contract:

- `max-automated-repair-rounds: 3`
- `round-numbering: 1-based`
- `round-four-action: human-escalation`
- `human-escalation-sentinel: Blocked on: human/action`

The critic posts one durable PR comment beginning with
`<!-- fsgg:independent-review:v1 -->`. It names the reviewed head SHA, critic identity, verdict, and
each finding's evidence, root cause (or explicitly bounded unknown plus measurements), duplicate-search
result, materiality, and disposition.

The implementing worker repairs material findings that belong in the current PR. The same critic
reviews each repaired head in a reply beginning with
`<!-- fsgg:independent-review-confirmation:v1 -->` and naming the initial review comment URL and
confirmation SHA, the 1-based `round` number, the preceding review or confirmation URL, and every
remaining material finding. There is exactly one initial marker and at most three ordered confirmation
markers. Each confirmation must advance the round by one and review the exact head produced by that
repair; duplicate round numbers, skipped rounds, competing markers, a changed critic, or a fourth
automated repair fail closed. When no repair is required, an initial `pass` whose reviewed SHA equals the
candidate head is itself the confirmation; no repair round or second marker is required. Allow at most
three repair-and-confirmation rounds. Every round addresses material findings only; do not iterate on
minor observations. Before routing any repair, the host validates the current chain and permits it only
when the latest round is less than three; this count-before-routing gate prevents a failed third
confirmation from racing into repair four while the escalation writes settle.

If the third confirmation still reports any unresolved material finding, automation is exhausted. The
critic posts one durable comment beginning with
`<!-- fsgg:independent-review-escalation:v1 -->` that names the current head SHA, all three ordered
confirmation URLs, the unresolved material findings and attempted repairs, and the concrete decision or
action required from a human. The worker or host then:

1. adds `Blocked on: human/action` to the issue body without disturbing its `Paths:` declaration;
2. records who, when, and why in an issue comment that links the escalation marker;
3. sets `Status: Blocked` and releases the claim; and
4. stops without merging, filing a replacement review issue, or starting round four.

Only a human may retire that sentinel and decide whether to take ownership or change the acceptance
boundary. The exhausted PR cannot reset its counter or begin another automated cycle. If a human later
directs renewed automation, close the exhausted PR without merging and start a separately scoped PR
with a fresh critic and initial marker that links the escalation; absent that explicit instruction,
automation remains stopped. An agent must not infer permission from a new commit or a passing check.

The critic may file new work only when all of these are true:

1. the finding is material by the definition above;
2. it is a distinct root cause that cannot remain reviewably inside the current PR;
3. no existing issue already carries that cause; and
4. the evidence and acceptance boundary are sufficient for another worker to act.

The critic—not the implementer—owns filing for review-discovered findings. It files directly in the
root-cause repository, adds observed behavior, root cause or measured unknown, impact, acceptance,
verification, a narrow `Paths:`, `Class:` and `Phase`, adds the item to the correct board, and sets
`Status: Backlog` unless it is a genuine blocker. Cross-repo work follows
[cross-repo-coordination](../../cross-repo-coordination/SKILL.md). Review findings never enter the
critic's or worker's private follow-up queue.

Class the filed cause from evidence: `defect` when observed behavior violates a current contract or
acceptance boundary; `hardening` when no current contract is broken but bounded preventative work
addresses a measured recurring risk or cost. A finding that still needs human judgement is not
actionable enough for critic filing; surface it to the host.

If a filed material issue blocks the current item, the critic reports it to the host; the worker sets
the real `Blocked by` edge, parks the item `Blocked`, releases the claim, and stops. Otherwise the
critic returns `pass` only after every material finding is repaired, deduplicated, or filed. The host
verifies the marker, ordered round/URL/SHA chain, critic independence, dispositions, and every filed
issue against GitHub before merge or terminal acceptance. An exhausted three-round chain is a human
escalation, never a passing terminal acceptance. After verification of a passing chain, the host posts
`<!-- fsgg:review-accepted:v1 -->` with the accepted head SHA, initial review URL, and confirmation URL
when a repair occurred.
The worker must observe that exact-SHA host marker before calling `landable` or merging.
