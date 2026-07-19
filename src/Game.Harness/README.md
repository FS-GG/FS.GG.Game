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
- **`Matrix`** — the multi-seed, bot-vs-bot match matrix. `Seat`, `MatchSetup<'world,'view>` and
  `Match<'view>` set up a seeded field of matches so a policy is exercised across many seeds, not one.
- **`Synthetic`** — the typed synthetic-state escape hatch: a *labeled* fallback that keeps its own
  evidence self-identifying, so a synthetic trace can never be mistaken for a real driven one.

## Guarantees

Headlessly testable — zero Skia, zero Scene, no wall-clock. It drives only through `Game.Core`'s
device-free `Command` vocabulary and advances only by whole fixed steps, so identical input replays
byte-identically across runs and the trace fingerprint is a stable value to diff against.

House style: `.fsi` is the sole public surface; `net10.0`; `-preview` channel.
See [FS-GG/FS.GG.Game](https://github.com/FS-GG/FS.GG.Game) and [ADR-0022](https://github.com/FS-GG/.github/blob/main/docs/adr/0022-extract-fs-gg-game-as-an-sdd-driven-component.md).
