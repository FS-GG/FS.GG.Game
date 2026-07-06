---
name: fs-gg-sdd-authoring-contracts
description: Reference for the load-bearing FS.GG SDD authoring grammars that silently block a stage if mis-formatted — the FR→AC checklist coverage line, the evidence.yml kind/result/synthetic satisfaction rule, the specify --input intent facts, the clarify [AMB:AMB-###] decision-tag resolution rule, and the per-stage front-matter required-field sets. Use when a checklist/evidence/specify/clarify stage blocks unexpectedly or a front-matter block reports "incomplete".
---

# Authoring Contracts (the gating grammars)

Several SDD inputs are **load-bearing**: a small grammar decides whether the tool
accepts what you authored, and a subtly wrong form produces a blocking gate. This
skill is the quick reference; the durable, drift-guarded source is
`docs/reference/authoring-contracts.md` (every example below is run through the
**live parser** on each build by `AuthoringDocsContractTests`, so it cannot drift).

The three body grammars — the checklist **coverage line** (§1), the `evidence.yml`
**satisfaction rule** (§2), and the `specify --input` **intent facts** (§3) — plus
two cross-cutting ones: the **clarify decision-tag** resolution (§4) and the
**per-stage front matter** required-field sets (§5). If a stage blocks and you
"know" the content is right, it is almost always one of these.

## 1. Checklist coverage line (used by `checklist`)

A functional requirement is **covered** only when a strict-scan parser finds a list
item that leads with `- FR-###:` and carries its acceptance reference **on the same
line**:

- literal `- `, then `FR-` + **three or more digits** (case-insensitive), then a
  literal `:`, then prose, with `AC-###` (optionally `US-###`) on the same line.

**Accepted:**

```text
- FR-001: W/S move the left paddle. (covers AC-002)
- FR-014: Ball serves toward the loser. (Stories: US-003; Acceptance: AC-009)
```

**Counted but NOT covered** (a loose scan `\bFR-\d{3,}\b` lists it, so it looks
present but establishes no coverage):

```text
**FR-001** W/S move the left paddle. (AC-002)     ← bold id, not a "- FR-001:" item
- FR-001 — moves the paddle (AC-002)              ← no colon after the id
(covers AC-002)                                    ← AC ref on its own line
```

See [[fs-gg-sdd-checklist]].

## 2. `evidence.yml` satisfaction (used by `evidence`/`verify`)

> An obligation is **satisfied** only by a matching declaration whose `result` is
> `pass` **and** whose `synthetic` is `false`.

- `synthetic: true` + `result: pass` → disposition **synthetic**, does not satisfy.
- `result: deferred` (or `kind: deferral`) → accepted deferral, does not satisfy.
- `result: fail | missing | stale | blocked` → not satisfied.

**`kind`:** `implementation · verification · review · generated-view · synthetic ·
deferral · note · missing` (`generatedview` also accepted). **An unrecognized
`kind` silently becomes `verification`** — spell it exactly.
**`result`:** `pass · fail · deferred · missing · stale · advisory · blocked`.

**Satisfies:**

```yaml
schemaVersion: 1
evidence:
  - id: EV001
    kind: verification
    subject: { type: task, id: T001 }
    artifacts: [tests/Product.Tests/InputMapTests.fs]
    result: pass
    synthetic: false
```

See [[fs-gg-sdd-evidence]].

## 3. `specify --input` intent facts (used by `specify`)

`specify` drafts a spec only when `--input` supplies three labeled facts; an
unlabeled line is read as the user value only, so free prose reports `scope` and
`measurable requirement` missing.

```text
value: A player can rally against a second human at one keyboard.
scope: Two-player local volley; no AI opponent, no online play.
requirement: A rally of 20 consecutive volleys completes without a dropped frame.
```

See [[fs-gg-sdd-specify]].

## 4. Clarify decision-tag resolution (used by `clarify`)

A carried `AMB-###` ambiguity is resolved only when **both** hold:

- its id sits on a `DEC-###` line under `## Decisions` **or** `## Accepted
  Deferrals` — authored as the canonical tagged form `[AMB:AMB-001]` (the bracket
  is a convention; the parser needs only the bare `AMB-001` token on the line);
- it is **not** left as a blocking bullet under `## Remaining Ambiguity` (write a
  `None.`/disclaimer there).

An **answer** under `## Answers` does **not** resolve — resolution is the decision
**tag**, not the answer. And each `DEC-###` may be **declared once**: a line whose
leading id is a `DEC-###` under `## Decisions` or `## Accepted Deferrals` is a
declaration, the two sections are pooled, and a second declaration raises
`duplicateClarificationId`. See [[fs-gg-sdd-clarify]].

## 5. Per-stage front matter (used by every authored stage)

Each stage blocks with an *incomplete/malformed front matter* diagnostic only when
a field it **gates on** is absent; other template fields are **defaulted**.

| Stage | Gating fields | Defaulted (not gating) |
|---|---|---|
| charter | `schemaVersion, workId, title, stage, changeTier, status` | — (strict) |
| specify | `schemaVersion, workId, stage` | `title`, `changeTier`→`tier1`, `status`→`draft` |
| clarify | `schemaVersion, workId, stage, sourceSpec` | `title`, `changeTier`, `status`→`needsAnswers` |
| checklist | `schemaVersion, workId, stage, sourceSpec, sourceClarifications` | `title`, `changeTier`, `status`→`needsReview` |
| plan | `schemaVersion, workId, stage, sourceSpec, sourceClarifications, sourceChecklist` | `title`, `changeTier`, `status`→`planned` |
| tasks (`tasks.yml`) | `schemaVersion` (workId derivable) | `stage`→`Tasks`, `status`→`tasksReady` |
| evidence (`evidence.yml`) | `schemaVersion`, valid `workId` | `status`→`draft` |

- `stage` is the only **closed** vocabulary: `charter · specify · clarify ·
  checklist · plan · tasks · analyze · implement · evidence · verify · ship`.
  `schemaVersion` major must be `1`.
- `changeTier` and `status` are **free strings** — the parser does not validate them
  (`tier1`/`tier2` and the status words are conventions/defaults, not an enforced
  vocabulary); expect no "bad tier"/"bad status" rejection.
- **`Source Snapshot`/`sha256` is not a clarify concept.** It belongs to
  `checklist`/`plan` (and `tasks`/`evidence` `sources[].digest`); the digest is
  **optional**, format-checked (64 hex) only when present, used only for staleness
  detection — a placeholder is silently ignored and a real digest is never required
  to author.

## Why these are strict

SDD's doctrine is that **structured artifacts are the machine contract** — the
strict scans exist so coverage and evidence are facts a tool can trust, not prose
a reader has to interpret. The strictness is the feature; these few forms are the
price.

## Regeneration semantics (re-running `checklist`/`tasks`)

`checklist.md` and `tasks.yml` are generated gate artifacts you also author against.
Re-running the stage **re-derives the tool-owned content from current sources and never
re-ingests its own prior output** (feature 082):

- Tool-owned rows are recomputed every run — a `CHK-###` blocking row clears once the
  source covers the FR (coverage lives in `spec.md`, not `checklist.md`); `tasks`
  re-derives the graph so a new plan decision disposition appears instead of the run
  reporting "stale" and doing nothing.
- Authored content is preserved: `checklist`'s `Advisory`/`Lifecycle Notes`, and in
  `tasks.yml` a task's `status`, `owner`, and hand-added **live** disposition refs
  (`requirements`/`decisions`/`sourceIds`, e.g. `decisions: [DEC-001]`) — plus a wholly
  hand-authored task that uniquely covers a live disposition. Stale refs/tasks (sources
  gone, or already derived) are dropped.
- Hand-edited generated rows are reclaimed (overwritten). Unchanged sources → byte-identical
  `noChange`. The only re-run that blocks is the `<!-- fsgg-sdd: unsafe-overwrite -->`
  opt-out, whose diagnostic names the file and command — never guess an `rm`.

See `docs/reference/authoring-contracts.md` → *Regeneration semantics*.

## Related

- [[fs-gg-sdd-checklist]], [[fs-gg-sdd-evidence]], [[fs-gg-sdd-specify]],
  [[fs-gg-sdd-lifecycle]].

## Sources

- `docs/reference/authoring-contracts.md` (whole file; drift-guarded by
  `AuthoringDocsContractTests`).
