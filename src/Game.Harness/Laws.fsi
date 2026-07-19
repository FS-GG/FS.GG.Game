namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// The outcome of checking one metamorphic law over a `Playable`. `DivergenceStep` is populated only
/// when a comparison law (determinism, replay) fails, carrying the first step index at which the two
/// traces diverged (`Trace.firstDivergence`), so a failure is actionable rather than "not equal".
type LawResult =
    { /// The law's name — e.g. "determinism", "replay", "fixed-step", "provenance".
      Law: string
      /// Whether the law held.
      Passed: bool
      /// The first divergence step index when a comparison law fails; `None` on pass or for a law
      /// with no step dimension.
      DivergenceStep: int option
      /// A human-readable detail line for a report.
      Detail: string }

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// A per-law pass/fail report. Every trace the runner builds is `Origin.InputDriven`, so provenance
/// is a law the report *checks*, not an assumption it makes.
type LawReport =
    { /// One result per law checked, in a stable order (determinism, replay, fixed-step, provenance).
      Results: LawResult list }

/// Public contract module exposed by the FS.GG.Game.Harness package.
[<RequireQualifiedAccess>]
module LawReport =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// `true` when every law in the report passed.
    val allPassed: report: LawReport -> bool

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The failing results (empty when `allPassed`).
    val failures: report: LawReport -> LawResult list

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// The reusable metamorphic laws every deterministic `Playable` shares, so an author gets them for
/// one call instead of re-deriving them per game. Every trace built here is `Origin.InputDriven`.
[<RequireQualifiedAccess>]
module Laws =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Check the four script-drivable metamorphic laws over `playable` and `sampleScripts`:
    /// **determinism** (two runs of a script produce equal frames), **replay** (resolving a script's
    /// keys and running them through `Driver.runCommands` reproduces `Driver.runScript`),
    /// **fixed-step** (an n-frame script yields exactly n recorded frames), and **provenance** (every
    /// trace is `Origin.InputDriven`). Traces are built with `Driver.identityFingerprint`, so frames
    /// are whole worlds and `'world` needs equality. A failing determinism/replay result carries its
    /// first divergence step. Matrix order-independence is a separate runner (`matrixOrderIndependent`)
    /// because it needs match inputs, not scripts.
    val check:
        playable: Playable<'world, 'key> ->
        sampleScripts: 'key list list list ->
            LawReport when 'world: equality and 'key: comparison

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Check the matrix order-independence law: running `matches`, then a permutation of them, yields
    /// the same outcome for each match regardless of order. A separate runner because the law needs
    /// a `MatchSetup`, an `outcome` projection, and a set of `Match` values — inputs a scripts-only
    /// runner does not carry.
    val matrixOrderIndependent:
        setup: MatchSetup<'world, 'view> ->
        outcome: ('world -> 'o) ->
        matches: Match<'view> list ->
            LawResult when 'o: equality
