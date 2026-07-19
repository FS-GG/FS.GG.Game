namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// A small combinator library over `Bot<'view>`, so a policy is a one-liner instead of a re-invented
/// `chaseBot`/`sitterBot` per game. Four combinators (`sitter`, `random`, `chase`, `greedyToward`) are
/// pure and preserve the `Bot` `(view, seed)` determinism contract; `scripted` is the one stateful,
/// single-use playback bot. All produce *test-input* policies, not a competitive agent.
[<RequireQualifiedAccess>]
module Bots =

    /// Public contract value exposed by the FS.GG.Game.Harness package.
    /// A bot that never moves: it issues no commands and leaves the generator untouched.
    val sitter<'view> : Bot<'view>

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// A **single-use, stateful** playback bot: it emits the given command script one frame per decision
    /// in order, then idles (`[]`) once the script is exhausted; it never touches the generator. Because
    /// it closes over a mutable playback index, construct a **fresh** `scripted script` per run — a
    /// shared instance carries its position across runs and does not satisfy `(view, seed)` determinism.
    val scripted: script: Command list list -> Bot<'view>

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// A bot that decides purely from the generator via a caller-supplied `draw`, threading it back.
    /// Deterministic in the seed because `draw` is a function of the `Rng` value.
    val random: draw: (Rng -> struct (Command list * Rng)) -> Bot<'view>

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// A bot that chases a target along one axis: given `(target, self)` axis projections of the view,
    /// it issues `up` when the target is below the self axis, `down` when above, and idles on a tie. It
    /// never draws from the generator, so it is deterministic in `(view, seed)`. The random tie-break
    /// that a game might add is a caller composition of `chase` and `random`, not built in here.
    val chase: axes: (('view -> int) * ('view -> int)) -> up: Command -> down: Command -> Bot<'view>

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// A one-step greedy bot: it issues the single move maximizing `score view move` (the earliest on a
    /// tie), or no command when `moves` is empty. A heuristic for *reaching* states, not for winning.
    val greedyToward: score: ('view -> Command -> float) -> moves: Command list -> Bot<'view>

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Adapt a `Bot<'inner>` to a `Bot<'outer>` by projecting the wider view to the narrower one the
    /// policy reads, keeping it blind to everything outside the projection (the `Ai.TeamView` fog
    /// boundary). The wrapped bot decides exactly as it would on `project outer`.
    val on: project: ('outer -> 'inner) -> bot: Bot<'inner> -> Bot<'outer>
