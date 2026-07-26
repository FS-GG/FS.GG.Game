# FS.GG.Game.Harness

Deterministic, headless gameplay test harness on [`FS.GG.Game.Core`](https://www.nuget.org/packages/FS.GG.Game.Core).
Drives a game's world through the standard `Command` input frontier, folds whole fixed steps, and
fingerprints the world trace as a value — so a replay is a **comparison**, not a tolerance check.

Depends on nothing but `FS.GG.Game.Core` and the BCL — no render/input stack, no keymap type, no I/O
(WI-6, [Game#425](https://github.com/FS-GG/FS.GG.Game/issues/425)). Everything below lives in the
`FS.GG.Game.Harness` namespace.

## The vocabulary

- **`Trace`** — the provenance-carrying comparison surface. A `Trace<'f>` is the world's evolution
  fingerprinted as a value, tagged with its `Origin`, so two runs are compared as data rather than
  re-simulated and eyeballed.
- **`Playable`** — the driven-game contract (`Playable<'world,'key>`) plus `Bot<'view>`, the
  in-process policy that turns a view of the world into commands. This is what a game implements to
  be driven by the harness.
- **`Driver`** — the drivers that produce a `Run<'f>`: scripted raw-key → keymap → `Command` input
  folded over whole fixed steps, and an in-process bot policy driving the same `Command` frontier.
- **`Journey`** — the stronger boot-to-outcome route. A `ProductionJourney` starts from a product's
  real boot function, drives timestamp-free host events through production mapping/update/tick/effect
  seams, and emits an opaque, bounded `JourneyReceipt` authenticated by a release-gate issuer key.
  Its `Origin.ProductionJourney` provenance is distinct from `Playable`'s simulation/component
  evidence. `ReferenceJourney` is the shipped composition used by the package's own positive gate.
- **`Workload`** — named expected-workload scripts and a product cost adapter. It keeps Release-only
  timing and bounded work observations separate from the deterministic `Trace`, and renders the
  generated scaffold's performance-evidence shape.
- **`Matrix`** — the multi-seed, bot-vs-bot match matrix. `Seat`, `MatchSetup<'world,'view>` and
  `Match<'view>` set up a seeded field of matches so a policy is exercised across many seeds, not one.
- **`Synthetic`** — the typed synthetic-state escape hatch: a *labeled* fallback that keeps its own
  evidence self-identifying, so a synthetic trace can never be mistaken for a real driven one.

## Guarantees

Headlessly testable — zero Skia and zero Scene. It drives only through `Game.Core`'s device-free
`Command` vocabulary and whole fixed steps, so identical input replays byte-identically. The optional
`Workload.run` clock feeds only `WorkloadObservation`; it never contaminates trace frames.

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
