---
name: check-board
description: Reconcile the org-level FS-GG "Coordination" Projects v2 board against the repos' real issue state, and re-verify that every recorded blocker still holds. Use when the board looks wrong or stale, before a planning pass, before fanning workers out with intra-repo-parallel-work, or when `next`/`take` says "nothing to do" and you doubt it. Reports every discrepancy; with --apply it fixes the board-side ones. Never edits an issue. Canonical protocol lives in FS-GG/.github.
---

# check-board (FS-GG)

The Coordination board is a **projection**. GitHub issues, their `state`, and the `fsgg:claim`
markers are the **ground truth**; the board's `Status`, `Phase`, and `Blocked by` are a cached
view of it that humans and `fsgg-coord next`/`take` read to decide what happens next. The board
drifts, and the ADR-0034/ADR-0040 engine drifts it in three ways this pass hunts. **A refused write
is not replayed by anything.** The bash client used to queue an exhausted write and replay it on
`fsgg-coord flush`; the typed engine has **no queue and no `flush`** — exhaustion is `EX_RATE`
(exit **75**), an instruction to *back off and retry*, and a write nobody retries is a write that
never landed. Second, `done --flip` flips `Status` only once it sees a merged PR. Third, and the
one that rots quietly: **nothing re-checks a `Blocked by` edge when its blocker closes.** So a
drifted board hands out work that is already done, hides work that is startable, and keeps items
"blocked" behind issues that closed weeks ago.

This skill is the **reconcile pass** over that drift. It answers two questions:

1. **Is the board in sync with the issues?** (and if not, fix the board — never the issue)
2. **Do the recorded blockers still hold?** (a `Blocked by` edge is a claim about the *present*)

Related: [cross-repo-coordination](../cross-repo-coordination/SKILL.md) owns the protocol the board
implements; [intra-repo-parallel-work](../intra-repo-parallel-work/SKILL.md) owns claims and
touch-sets. This skill only *reconciles*. Decisions: ADR-0001 (the board), ADR-0027 (the claim).

## The one rule: the issue is the truth, the board is the copy

**Fixes only ever write to the board.** Auto-remediation may set `Status` or release an expired
claim. It may **never** close or reopen an issue, delete a `Blocked by` edge, merge a PR, or touch
a working tree. Those need judgement, so they are **reported and left for a human**. When ground
truth and the projection disagree, the projection is what changes. (It used to add a missing item
to the board too; that is report-only while the engine has no idempotent `add` — §2.)

Corollary: an issue is not "done" because the board says `Done`. `fsgg-coord done` exists precisely
to make the stamp *earned* (PR merged **and** `Status: Done`), and this skill never fakes it.

## Run it

```sh
scripts/fsgg-coord budget                      # free; are we near the GraphQL cap?
/check-board                                   # DRY RUN — report every finding, write nothing
/check-board --apply                           # ...and fix the board-side ones
/check-board --repo rendering                  # scope to one repo (registry short-id)
```

Dry run is the default, as it is for `reap` and `coordination-sync --check`. Read the findings
before you let anything write.

## 1. Snapshot (three reads, whole board)

Take the snapshot **once** and classify from it. Do not re-query per item — a scan is a full-board
read, and the whole point of `fsgg-coord` is that it costs ~3 points instead of ~2,500.

```sh
scripts/fsgg-coord scan --fresh --include-backlog > /tmp/scan.json  # EVERY item, incl. Done — AND its blockers
scripts/fsgg-coord lint --json            > /tmp/lint.json   # epic invariants + the Done/open note
scripts/fsgg-coord who  --repo <r> --json > /tmp/who.json    # live claims, per repo
```

**`--fresh` is not optional, and it is the one flag `scan` needs that `ready` never did.** `scan` is
the **scheduler's** read, so it is served from a 90s shared cache (`FSGG_COORD_SCAN_TTL_SEC`) — that
cache is why N looping workers cost one board scan instead of N. But this pass is a **truth** read,
and the kit is explicit about the difference: the cached reads are `next`/`take`, while
`ready`/`lint`/`who`/`reap` and *the `/check-board` snapshot* scan fresh, **"because a reconciler
serving a cached 'truth' reports drift that was already fixed."** Omit `--fresh` and you can
reconcile against a board up to 90 seconds stale — inventing findings a co-worker just repaired, and
missing an item added moments ago. That is not theoretical: a cached scan taken seconds after an
issue was boarded did not contain it, and the same scan with `--fresh` did.

**Take the snapshot from `scan`, NOT from `ready` — this is the trap that silently disarms the
whole pass.** `ready --all --json` looks like the right read and is not: it emits **only**
`number, repo, state, status, title`. It carries **no `blockers`**, no `blockedBy`, no `blocked`,
no `phase`. So `select(.blockers | length > 0)` over a `ready` snapshot matches **nothing**, §3
finds zero blocker findings, and the pass reports a *clean board* precisely when blockers have gone
stale. That is a **false clean** — the worst output this skill has, because it buys confidence in
the projection instead of correcting it. It is not hypothetical: it hid three cleared blockers on a
1,059-item board while `ready` reported everything in order.

`scan --include-backlog` is a strict superset for this pass — same item count, plus blockers, plus
each issue's `body` (which §2's touch-set note needs). It emits an **envelope**, not a bare array,
so every jq starts at `.items`:

```json
{ "schema": "fsgg.coord.snapshot/1", "allowBacklog": true, "leaseMinutes": 90, "limit": null,
  "inFlight": [ ... ],
  "items": [
    { "owner": "FS-GG", "repo": "FS.GG.Rendering", "number": 752, "status": "Blocked",
      "state": "OPEN", "body": "…Paths: …",
      "blockers": [ { "owner": "FS-GG", "repo": "FS.GG.Game",  "number": 321,
                      "raw": "FS.GG.Game#321",  "state": "closed" },
                    { "owner": "FS-GG", "repo": "FS.GG.Audio", "number": 106,
                      "raw": "FS.GG.Audio#106", "state": "closed" } ] } ] }
```

Two shape details that will bite you if you skim:

- **`blockers[].state` is lower case** — `open | closed | merged | unknown | unparseable`, the five
  cases of the engine's `BlockerState`. Compare against `"CLOSED"` and it never matches, so every
  blocker classifies as still-holding and every finding vanishes. (`ascii_upcase` in §3 is for
  **REST**, which is a different read with the opposite convention. Do not "unify" them.)
- **The ref field is `raw`, not `ref`** — `.blockers[].raw` is `"FS.GG.Game#321"`. `.ref` yields
  `null`, which prints as a finding you cannot act on.

**A blocker is RESOLVED iff it is `closed` *or* `merged`.** The engine spells this rule once and
`fsgg-coord facts` will recite it to you; the scheduler, the `BLOCKED BY` column and `take`'s
diagnostic all consume that one copy. If you are re-spelling it in jq, spell it once.

`merged` is not pedantry. `Blocked by` may name a **pull request**, whose state is `open | closed |
merged` — so a rule that only clears on `closed` unblocks when the PR is **abandoned** and blocks
forever once it is **finished**. The gate opened precisely when the blocking work was thrown away,
and shut precisely when it was done (`.github#476`). If a blocker is an issue rather than a PR the
distinction cannot arise — but you do not know which it is until you look, so do not assume.

`scan` resolves an off-board ref **over REST itself**, in the scan, and says how many on stderr
(`scan: 1059 candidate(s); 0 off-board blocker(s) resolved`) — you no longer have to. What it
cannot resolve stays `unknown`, and an `unknown` **blocks**: "I could not look" is not "I looked
and it is fine" (epic `#266`). Same for `unparseable`.

## 2. The findings

Each finding has a code, a ground truth, and a fix — or an explicit refusal to fix.

| Code | Condition | Fix (`--apply`) |
|---|---|---|
| `CLOSED-ISSUE-NOT-DONE` | `state == CLOSED` and `status != Done` | `set-field --batch <i> Status=Done` |
| `DONE-STATUS-OPEN-ISSUE` | `status == Done` and `state == OPEN` | **report only** — is the work done, or was the flip premature? |
| `OFF-BOARD-ISSUE` | open `roadmap` issue in a rostered repo with no board item | **report only** while `fsgg-coord add` is gone — see the note below |
| `BLOCKER-CLEARED` | every blocker `closed` **or `merged`**, but `status == Blocked` | `set-field --batch <i> Status=Ready` |
| `BLOCKER-UNKNOWN` | a blocker ref `scan` could not resolve | resolve over REST (§3), then board the blocker if it is open |
| `BLOCKER-UNPARSEABLE` | a `Blocked by` token is not an issue ref | **report only** — hand-fix the field |
| `STATUS-NOT-BLOCKED` | an open blocker, but `status` is `Ready`/`Backlog` | `set-field --batch <i> Status=Blocked` |
| `STALE-CLAIM` | `who` says `state == "stale"` | `reap --repo <r> --apply` |
| `UNCLAIMED-IN-PROGRESS` | `who` says `state == "unclaimed"` | **report only** — someone is working outside the protocol |
| `CLAIM-STATUS-LAG` | held claim, but board `status != In progress` | `set-field --batch <i> "Status=In progress"` |
| `UNDECLARED-PATHS` | open, unclaimed, not `Done`, and the issue body declares no `Paths:` | **report only** — the fix is an *issue* edit, and this skill never writes to an issue |
| `EPIC-*` | from `lint --json` (severity `error`) | **report only** — a broken epic needs a human |

**Always write with `set-field --batch`, never the single-field form.** On the engine the fleet is
**pinned** to, `set-field <ref> <field> <value>` **cannot write any single-select**: it declares its
GraphQL variable `$optionId: ID!` while `ProjectV2FieldValue.singleSelectOptionId` is a `String`, so
the server refuses the query on a type mismatch (`.github#848`). `Status`, `Phase`, `Repo Scope`,
`Workstream` and `Effort` are **all** single-selects, which is every field this table writes. The
form still works for text fields (`Blocked by`, `Contract`) — which is why the break is easy to
miss, and why this table prescribed the broken spelling for every flip in it until `#848` was found.

`--batch` builds a different document and is **immune either way**, which is why it is the standing
instruction rather than a workaround to retire. `.github#857` **has** repaired the single-field form
at the source — and that repaired it for **nobody but `.github`**. This repo builds the engine from
source (ADR-0034 §4.3), so it picked the fix up on merge; every other repo restores the **pinned**
`FS.GG.Coord.Cli` from `dist/dotnet/.config/dotnet-tools.json` and keeps the bug until a release
carries `#857` and the pin flips (as `#852`/`#853` did for 0.2.0). So **"is it fixed?" has two
answers at once**, and which one is true depends only on where you are standing — a merged fix is
not a distributed one (`#846`). `--batch` is correct in both, and is the cheaper spend regardless:
it writes N fields in **one aliased mutation** (`#448`) against a budget every worker shares.

**`OFF-BOARD-ISSUE` is report-only, and there is currently NO sanctioned way to fix it.** Say that
plainly rather than hand anyone a recipe. The bash client wrapped the raw Projects v2 add in
something **idempotent** and metered; the typed engine never ported it, so `add`, `item-add` and
`flush` are all `unknown command` today (`.github#846`, the D.4 client-surface gap). What is left is
a **standoff**, and you should know it is there rather than rediscover it in a red check:

- The raw `gh project` add still works — but `check-graphql-monopoly` **fails the merge** of any
  skill or doc carrying it as a runnable line, because a recipe that reaches past the client is an
  unmetered principal on a budget the whole fleet shares (`#418`). That gate is right.
- Its remediation tells you to use `fsgg-coord add` — **which no longer exists**. So the fabric
  forbids the spelling that works and mandates the one that does not (`.github#859`).

Until `add` returns, the honest move is the one this skill already prefers: **report the class, name
the issues, and stop.** Do not smuggle the raw call in behind an exemption marker — the marker means
*one-time board provisioning run by a human with admin rights*, and a reconcile pass is neither.

**And `--apply` must never board anything even once `add` is back — unless it is idempotent.**
Boarding an issue twice creates a **duplicate item**, and the only thing standing between this pass
and that duplicate is a snapshot being right about *absence*. A failed or partial scan makes a
boarded issue look off-board — and "I could not read the board" is not "the item is not on it"
(`#266`), which is precisely the confusion that had `#421` telling workers to add an issue that was
already there.

`CLAIM-STATUS-LAG` is the one class you cannot read off a single command: `who --json` does not
emit `inProgress`. Join it yourself — an item `who` reports as `held` whose board `status` is not
`In progress` is a lock whose `Status` write **never landed**. There is no queue to flush and
nothing will replay it: the engine's answer to an exhausted budget is `EX_RATE` (exit 75) and a
back-off, so a write refused mid-claim is simply lost, and this pass is what repairs it. Write it:

```sh
jq -r --slurpfile s /tmp/scan.json '
  ($s[0].items | map({ key: "\(.owner)/\(.repo)#\(.number)", value: .status }) | from_entries) as $st
  | .[] | select(.state == "held")
  | select($st["\(.repo)#\(.number)"] != "In progress")
  | "CLAIM-STATUS-LAG  \(.repo)#\(.number)  held by \(.worker), board says \($st["\(.repo)#\(.number)"] // "—")"
' /tmp/who.json
```

`UNDECLARED-PATHS` is the class that makes a **full queue look like an empty one**. An item with no
`Paths:` cannot be scheduled — `take`/`batch` refuse it, because an undeclared touch-set cannot be
proven disjoint from another worker's — so it sits on the board *looking* startable while every
worker who asks for work is told there is none. `.github` reached **twelve** such items at once, all
filed through the org's own recipe, and `/pnext-item` reported a dead queue over a full one
([#442](https://github.com/FS-GG/.github/issues/442)). The board is not wrong here — the *issue* is —
which is exactly why this is report-only: the remedy is an issue-body edit, and **this skill never
writes to an issue** (§*The one rule*). Report it, and let the claimant `claim` then `widen`, which
is the ordering ADR-0021 requires anyway (the marker is the CAS; the body is not).

The touch-set lives in the issue body, which the REST issue list already carries — so this costs
**no extra call**:

**Do not re-grep for `Paths:` — ask the tool.** A hand-rolled `test("(?m)^Paths:")` is a **fourth**
parser of a grammar that already has three, and it is the loosest: it is not fence-aware (a `Paths:`
line inside a code fence **declares nothing** — `#277`), it is case-sensitive where the tool accepts
up to three leading spaces and either case, and it counts the `Paths: none` **sentinel** as a
declaration when the whole point of `#496` was to tell those two apart. `lint` applies the real
grammar, and since `#520` it also reports a touch-set that is *declared but unusable*:

```sh
scripts/fsgg-coord lint --repo <r> --json \
  | jq -r '.[] | select(.code == "NO-TOUCH-SET" or .code == "BAD-TOUCH-SET")
           | "\(.code)  \(.id)  \(.detail)"'
```

`NO-TOUCH-SET` = nothing was declared. `BAD-TOUCH-SET` = something was, and **every token of it is
unmatchable** — a token that matches no file conflicts with nothing, so `batch` refuses the item and
no worker can ever pick it up. Both are the same death; only the diagnosis differs. Both are
**report-only** here: the fix is an *issue* edit, and this skill never edits an issue.

`DONE-STATUS-OPEN-ISSUE` is deliberately **not** auto-fixed, in either direction. `lint` calls it a
*note* rather than an error for the same reason: `Done` over an open issue is how a premature flip
looks, **and** how "merged, issue left open for the release note" looks. Closing the issue and
reverting the status are both destructive, and only a human knows which happened.

## 3. Re-verify the blockers (this is the half people skip)

A `Blocked by` edge is a claim about *now*, and nothing re-checks it when the blocker closes. Items
rot in `Blocked` behind issues that shipped. This is the class a **migration** manufactures in bulk:
a release that closes a batch of issues silently clears the edges that named them, and every item
those edges gated stays `Blocked` — advertised as unstartable, skipped by `next`, invisible as work.
For every item with `blockers`:

```sh
jq -r '.items[] | select(.blockers | length > 0)
  | "\(.repo)#\(.number)  [\(.status)]  " + ([.blockers[] | "\(.raw)=\(.state)"] | join(" "))' /tmp/scan.json
```

Then, per blocker state (lower case — see §1):

- **`closed`** / **`merged`** — does not hold. If *every* blocker is resolved, the item is startable:
  flip `Blocked` → `Ready`. **Leave the `Blocked by` field alone.** It is provenance, and a resolved
  ref costs nothing — the scan and `next` both ignore it. Deleting the edge destroys history to fix
  a `Status` that was the actual problem.
- **`open`** — holds. If the item's `status` is not `Blocked`, that is `STATUS-NOT-BLOCKED`; the
  board is lying to a human reading the column (`next` skips it correctly either way).
- **`unknown`** — *the scan could not resolve the blocker.* Do not trust it. Resolve it over
  **REST** — `gh issue view` is GraphQL, and spending the budget here is spending the budget this
  pass needs to write its own fixes in §4:

  A blocker ref reads `owner/repo#n`; REST wants the parts, so split it. Keep `html_url` — step 2
  of §4 (`gh project item-add --url`) needs it, and it is the field that tells an issue from a PR.
  The scan hands you the parts already (`.blockers[]` carries `owner`, `repo`, `number` beside
  `raw`), so you rarely have to parse the ref yourself:

  ```sh
  # FS-GG/.github#449  ->  owner=FS-GG  repo=.github  n=449
  gh api repos/FS-GG/.github/issues/449 \
    --jq '"\(.state | ascii_upcase)  is_pr=\(.pull_request != null)  \(.html_url)  \(.title)"'
  # OPEN  is_pr=true  https://github.com/FS-GG/.github/pull/449  [adr] …
  ```

  **Mind the two case conventions — they are opposite, and that is not a bug to "fix".** REST emits
  `open`/`closed` lower case for an *issue's* state, while an item's own `state` in the snapshot is
  `OPEN`/`CLOSED` upper case. `ascii_upcase` above normalises the REST answer to the item convention
  so it can be compared with `.state`. A **blocker's** state is the third convention — lower case
  (§1) — so compare it against `"closed"`, never `"CLOSED"`. Get any of these backwards and the
  comparison silently never matches: every blocker classifies as still-holding, and the pass reports
  a clean board.

  `is_pr` is not a curiosity either: an **off-board blocker is very often a PR** (an ADR PR gating
  an issue). `gh issue view` renders a PR as an issue without saying so — only the URL (`/pull/<n>`)
  gives it away — so it hides the one fact you need to read the blocker correctly. REST states it
  outright, and costs no GraphQL.

  `CLOSED` (or a merged PR) → treat as `BLOCKER-CLEARED`. `OPEN` → the blocker is genuine but
  **off-board**, so `next` will refuse this item forever and never say why in a way you can act on.
  Fix the *cause*: board the blocker (§2's `gh project item-add` note — `fsgg-coord add` is gone),
  turning `unknown` into `open`. The remedy is the same whether the blocker is an issue or a PR;
  only the diagnosis gets clearer.
- **`unparseable`** — prose or a placeholder leaked into the field before it was validated. Report
  it with the offending token. A human re-writes the field with refs, or clears it — and `Blocked
  by` is a **text** field, so the single-field form is the one that works here:
  `fsgg-coord set-field <i> 'Blocked by' ''`.

## 4. Apply, in this order

Order matters — later steps read state the earlier ones changed.

```sh
scripts/fsgg-coord budget                             # 0. is there budget to finish? a pass that dies
                                                      #    half-applied leaves the board worse than found
scripts/fsgg-coord reap --repo <r>                    # 1. dry run: whose lease expired?
scripts/fsgg-coord reap --repo <r> --apply            #    release them (tells the reaped worker)
                                                      # 2. (boarding is REPORT-ONLY today — §2)
scripts/fsgg-coord set-field --batch <i> Status=<V>   # 3. the status flips (--batch, always — #848)
scripts/fsgg-coord scan --fresh --include-backlog     # 4. RE-READ — --fresh, or you re-read the
                                                     #    90s cache you already have (§1)
```

Step 0 replaces the `flush` that used to lead this list: there is **no write queue** and no `flush`
in the engine, so nothing is pending and nothing will be replayed. What can still bite you is
running *out* of budget partway through, which strands the board between two consistent states —
so look before you start, and if `budget` is thin, do step 1 and stop.

Step 2 is empty on purpose while `add` is missing (§2), which also makes step 4 conditional rather
than mandatory: it exists because boarding a blocker re-resolves it from `unknown` to `open`/
`closed`, creating or clearing a `BLOCKER-CLEARED` that was not in your snapshot. Nothing this pass
now writes can move a blocker, so re-read **only** if a human boarded something alongside you — and
if they did, reclassify from the fresh scan before applying any newly-earned flip.

## 5. Confirm, and report what you did not touch

```sh
scripts/fsgg-coord lint --repo <r>       # exit 0
scripts/fsgg-coord ready --repo <r>      # the board a human will now read
scripts/fsgg-coord budget                # what the pass cost
```

Finish with a summary that separates **fixed** from **left alone**. The report-only classes
(`DONE-STATUS-OPEN-ISSUE`, `OFF-BOARD-ISSUE`, `UNCLAIMED-IN-PROGRESS`, `BLOCKER-UNPARSEABLE`,
`UNDECLARED-PATHS`, `EPIC-*`) are the ones a human must act on, and a pass that quietly "succeeded"
while leaving six of them standing is how they survive to the next pass. Name them, with their
issue refs.

**Never report a silent cap.** If you scoped to one repo, or stopped after N fixes, say so — a
partial reconcile that reads as a full one is worse than no reconcile, because it buys false
confidence in the projection.

## Setup

- Board writes need `gh auth refresh -s project,read:project`; `reap`/`release` need `issues: write`.
- `--repo` takes a registry short-id (`sdd`, `rendering`, `governance`, `templates`, `game`,
  `audio`, `.github`), `owner/repo`, or a bare repo name. The roster is `registry/repos.yml`;
  `scripts/repos.sh list --all` enumerates it (bare `list` is a usage error — it wants `--all` or
  `--receives <cap>`, so that a capability sweep cannot silently widen to the whole roster).
- Unscoped, this is a whole-org pass over every board item — one full scan, plus one `who` per repo.
- **In `.github` the engine is built from source, never restored from the feed** (ADR-0034 §4.3), and
  this repo keeps no `.config/dotnet-tools.json` of its own — the manifest it *distributes* lives at
  `dist/dotnet/.config/`. So the shim finds no engine here until you build one:
  `dotnet build src/FS.GG.Coord.Cli -c Release`, then point
  `FSGG_COORD_ENGINE_BIN` at the **apphost** (`…/net10.0/fsgg-coord-engine`, not the `.dll` — the
  shim requires an executable). Receivers restore the pinned engine instead and need none of this.
