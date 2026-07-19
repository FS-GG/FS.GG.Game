namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// Which of the two seats a bot occupies in a match. Seats are applied in the fixed order `A` then
/// `B` every step, so a match never depends on `Map`/`HashSet` iteration order.
///
/// **Qualified.** `A`/`B` are ordinary one-letter names, so `open FS.GG.Game.Harness` must not inject
/// them. Write `Seat.A`.
[<RequireQualifiedAccess>]
type Seat =
    /// The first seat. Observed and applied before `B` each step.
    | A
    /// The second seat.
    | B

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// Everything a two-seat match needs, independent of any concrete world. `Init` seeds the world from
/// the match's generator; `Observe` and `Apply` are per-seat so each bot sees only its own view and
/// drives only its own seat; `Step` is the fixed step; `IsOver` and `MaxSteps` bound the match (the
/// `MaxSteps` cap guarantees termination and keeps every match deterministic in length).
type MatchSetup<'world, 'view> =
    { /// The fixed step interval in seconds.
      Dt: float
      /// Build the match's starting world from its seeded generator.
      Init: Rng -> 'world
      /// The view a given seat perceives of the world.
      Observe: Seat -> 'world -> 'view
      /// Apply a seat's command to the world.
      Apply: Seat -> Command -> 'world -> 'world
      /// Advance the world by one whole fixed step of `dt` seconds.
      Step: 'world -> float -> 'world
      /// Whether the match has reached a terminal state.
      IsOver: 'world -> bool
      /// The hard cap on fixed steps, so a non-terminating policy still ends deterministically.
      MaxSteps: int }

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// One `(bot, bot, seed)` match: the two seated policies and the seed that determines the world's
/// starting state and the shared decision stream. Equal `Match` values run to equal outcomes.
type Match<'view> =
    { /// The seed for this match's world init and decision generator.
      Seed: uint64
      /// The policy in seat `A`.
      A: Bot<'view>
      /// The policy in seat `B`.
      B: Bot<'view> }

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// The multi-seed, bot-vs-bot matrix runner — the mini-tanks balance shape. Each match runs
/// independently and deterministically from its own seed, so the runner is order-free: permuting the
/// matches permutes the results identically and changes no match's outcome.
[<RequireQualifiedAccess>]
module Matrix =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Run a single match to `IsOver` or `MaxSteps` and project the final world to an outcome. The
    /// world type never leaks into the result: the caller's `outcome` extracts what a match means
    /// (winner, score, survival), keeping the runner game-agnostic.
    val runMatch: setup: MatchSetup<'world, 'view> -> outcome: ('world -> 'o) -> game: Match<'view> -> 'o

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Run a set of matches, returning one outcome per match in input order. Each outcome depends only
    /// on its own match, so the result is independent of the order in which matches are supplied.
    val runMatrix:
        setup: MatchSetup<'world, 'view> ->
        outcome: ('world -> 'o) ->
        matches: Match<'view> list ->
            (Match<'view> * 'o) list

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The fraction of outcomes for which `isWin` holds — the win-rate a balance band asserts on.
    /// An empty outcome list is `0.0`.
    val winRate: isWin: ('o -> bool) -> outcomes: 'o list -> float
