# FS.GG product type — the **capability capsule**

> **New design (2026-07-07).** Unlike the game-logic corpus in this directory, this document was
> authored here, not relocated from `.github`. It names and formalizes the *kind of thing*
> FS.GG.Game ships to its consumers — a question ADR-0022 left implicit. It is written as a
> pre-ADR design proposal and is intended to graduate to an org-level ADR (next free number:
> **ADR-0023**, `FS-GG/.github/docs/adr/`), because the type is platform-wide, not FS.GG.Game-local.

- **Date:** 2026-07-07
- **Owner:** FS.GG.Game (proposer); platform-wide once graduated
- **Status:** Design proposal (pre-ADR)
- **Scope:** Define, name, and give a schema to the product type that FS.GG.Game (and its sibling
  providers) deliver to an **agent + SDD** consumer — the thing that is *not* a library, *not*
  docs, and *more than* an agent skill.
- **Relationship:** consumes the packaging model of [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md)
  (the extraction), the skill-registry discipline of ADR-0014/ADR-0017 (`registry = manifest =
  bytes`), and the advisory governance overlay of ADR-0022 §10 decision #4. The game-logic
  [design corpus](README.md) is the first body of content this type packages.

---

## 1. Context — the packaging question ADR-0022 left open

FS.GG.Game's consumer is **an agent driven by the SDD lifecycle**, not a human calling an API.
That inverts the economics that justify a conventional library:

- A human reuses a library because *writing the code* is expensive. You hand them a finished DLL
  and you have saved them the typing — so the natural unit is "the largest coherent pre-built
  block."
- An agent writes code cheaply. What is expensive and *unreliable* for an agent is
  **re-deriving the decisions and re-discovering the traps** — that `HashSet` iteration order
  breaks byte-determinism, that circle–AABB needs a center-inside fallback, that A* cost must
  equal Dijkstra cost or the tie-break is wrong.

Shipping a fixed DLL therefore packages the cheap thing (the code) and discards the expensive
thing (the decisions, and the freedom to shape the solution to *this* game's spec). It also
over-constrains the consumer into one specific solution, negating the reason they adopted an
agent + SDD workflow in the first place.

But the opposite extreme — ship nothing, let the agent reinvent each wheel — repeats the same
mistakes every time and throws away hard-won correctness (determinism, symmetry, optimality).

The right granularity is **neither a fixed library nor a blank page**, and it differs **per
capability**. We have been describing this artifact throughout the FS.GG.Game design work
without naming it. This document names it.

## 2. Decision — the capability capsule

A **capability capsule** (henceforth *capsule*) is:

> a unit of reusable capability authored for a **generative (agent) consumer**, which
> co-packages three faces of one capability and is **materialized** (regenerated to the
> consumer's spec) rather than **linked**.

The three faces:

| Face | What it carries | Delivery tier |
|---|---|---|
| **Rationale** | the decisions and gotchas — *why*, and what not to redo | knowledge (source-of-truth prose + design) |
| **Contract** | the parts that must be reproduced byte/behavior-exact | fixed code / `.fsi` surface + golden bytes |
| **Constraint** | executable invariants + a governance maturity dial that enforces them | tests/properties + `capabilities.yml` checks |

A capsule is not required to be uniform across its sub-parts: each sub-capability is assigned a
**tier** (§4), and the capsule is the coherent bundle of them plus the two cross-cutting faces
(rationale, constraint).

## 3. What makes it a distinct type (differentia)

| Confused with | The difference |
|---|---|
| **library** | not linked — *regenerated*; ships decisions, not just code |
| **docs** | executable and *enforced*, not passive prose |
| **agent skill** (`.claude/skills/*`) | authoritative + verifiable + tiered, not merely advisory workflow knowledge for the agent's own operation |
| **template / scaffold** | carries *ongoing* enforcement (the constraint face), not a one-shot file drop |

Naming the type also resolves a real ambiguity this platform already suffers: "skill" today
means both the agent-workflow packages under `.claude/skills/` **and** the product capabilities
in `template/product-skills/` + `skill-manifest.json`. This proposal fixes the vocabulary:

- **agent skill** — advisory workflow knowledge for the agent's own operation (`.claude/skills/`).
- **capability capsule** — the governed, tiered, materializable product the consumer builds into
  their game (`template/product-skills/` graduates into this).

## 4. The tiering rule — one question, asked per sub-capability

> *Is the value the exact bytes, or the shape of the solution?*

- **Tier 1 — hard contract (fixed code, byte/behavior-exact).** Reinvention here is a *bug*, not
  flexibility: `Rng` (SplitMix64 — different bytes means determinism is gone), `FixedStep`,
  coordinate hashing, serialization formats. The capsule denies flexibility on purpose.
- **Tier 2 — reference implementation (vendored source the agent adapts).** Strong default shape,
  but the agent may specialize: `SpatialGrid`, `Pathfinding`. Source beats binary so the agent
  can inline, prune, retune.
- **Tier 3 — knowledge / pattern (design + invariants only).** The right shape is spec-dependent:
  collision *response* policy, fog-of-war rules, what "blocks" LOS. Ship the rationale, the
  gotchas, and the properties the implementation must satisfy; let the agent build to its own
  TestSpec. This is where the flexibility lives and where a DLL would hurt most.

## 5. Governance — the constraint face, and why it is the enabler

Tiering decides *how much freedom* the agent gets. Governance decides *what is non-negotiable
regardless of the freedom*. They are duals: **governance is what makes it safe to ship tier-3
flexibility.** The `capabilities.yml` `checks:` schema is already generic (`domain`, `command`,
`owner`, `cost`, `environment`, `maturity`, `tier`), so a `determinism` domain with a
`golden-hash` check, or a `performance` domain with a `frame-budget` check, is *using* the
mechanism, not extending it.

Three classes of custom constraint, with their fit:

- **Correctness / invariants** (determinism byte-match, LOS↔FOV symmetry, no-tunnel) — *excellent
  fit*; provable properties bound to a boundary + maturity.
- **Performance** (frame budget, allocation ceiling, hot-loop big-O) — *good, with a caveat*;
  measured not proven, so keep advisory or tolerance-banded, never a hard laptop gate.
- **Game-design rules** (gold never negative, every level completable) — *powerful and dangerous*;
  only govern once *executable*, and start advisory. You cannot govern "fun."

**The unlock specific to an agent consumer:** for a human, a failing advisory check is a nag they
ignore. For an agent in an SDD loop, an advisory check is an **objective function** — it optimizes
against it during assembly. Governance stops being an end-of-line gate and becomes the **fitness
landscape the agent searches**. The ramp is therefore: *advisory (light profile) during
generation → agent self-corrects → `block-on-ship` at the merge boundary only for the invariants
that stabilized* — exactly the advisory overlay ADR-0022 §10 decision #4 chose, now with a stated
reason for *this* consumer.

**Two authorship layers** (mirroring `constitution.md` vs `capabilities.yml`): the **provider
baseline** (universal invariants — determinism, symmetry — that travel *with* the capsule at
advisory maturity) and **customer-authored** constraints (their economy rules, their perf budget)
layered on top.

## 6. Schema — the fields that make a capsule well-formed

A capsule instance is reviewable against this shape (a superset of today's
`skill-manifest.json` entry):

```
id:               fs-gg-<capability>
capability-scope: <one coherent capability>
tier-map:                       # per sub-capability
  - part: <name>
    tier: contract | reference | knowledge
invariants:                     # executable properties (the constraint face)
  - id: <name>
    check: <command / property>
    kind: golden | property | benchmark
governance:                     # binding of invariants to boundaries
  - invariant: <id>
    maturity: advisory | block-on-ship
    profile: light | release
materializes-when: <profile predicate>     # ADR-0017 grammar
rationale: <path to the design that carries the decisions + gotchas>
provenance: <sha256 / source>              # registry = manifest = bytes
```

A capsule missing its **constraint face** (invariants + governance) is not a capsule — it is a
plain skill or a doc. That failure mode is precisely what the schema makes visible.

## 7. Naming — decision and alternatives considered

**Chosen: "capability capsule."** Self-contained and insertable; the "capsule walls = governance"
metaphor lands; cartridge-adjacent, which is quietly on-theme for a game platform.

Considered and rejected as the primary term:

- **"capability"** (bare) — collides with `capabilities.yml`'s coarse build/test domains and would
  re-create the very two-meanings ambiguity this proposal exists to kill.
- **"faculty"** — precise and uncollided, but reads as literary/obscure for a package unit.
- **"governed skill"** — zero learning curve, but leans on the overloaded "skill" and is a
  two-word compound.

The *definition and schema* (§2, §6) are the load-bearing part; the label is the replaceable part.
Any of the alternatives works under the same definition if the org prefers it at graduation.

## 8. Consequences

- **Positive.** One vocabulary across the platform; a reviewable schema; a stated reason for the
  advisory→enforcing ramp; a clean seam between provider-baseline and customer-authored
  constraints; the DLL-vs-blank-page false dichotomy dissolved into a per-capability tier choice.
- **Cost.** Each capability now owes *three* faces, not one — authoring a capsule is more work than
  dropping a DLL. The constraint face in particular is only as real as its executable checks;
  aspirational constraints are theater and must be rejected in review.
- **Migration.** `template/product-skills/*` are proto-capsules today: they carry rationale
  (partial) and a materialization rule, but not an explicit tier-map or constraint face.
  `fs-gg-game-core` in particular lumps tier-1 (`Rng`), tier-2 (`SpatialGrid`), and tier-3
  (collision policy) into one undifferentiated unit; splitting by tier is the first concrete
  migration.

## 9. Graduation path

1. Circulate this proposal in FS.GG.Game (dogfood consumer of its own output).
2. Prototype **one** full capsule end-to-end — collision is the richest candidate: tier-map,
   invariants as governed checks, the advisory→block-on-ship ramp.
3. Promote to **ADR-0023** in `FS-GG/.github`, generalized across the sibling providers
   (Rendering `fs-gg-symbology`, audio `fs-gg-audio`), and reconcile the `skill-manifest.json`
   schema (§6) into the org registry validator.
