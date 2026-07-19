namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// A witness produced by `Explore.findScript`: the shortest per-frame command script from `Playable.Init`
/// that reaches the goal, and its `Depth` (frame count). Replaying `Script` through `Driver.runCommands`
/// from `Init` reproduces the reaching run — the witness is `Origin.InputDriven` by construction.
type Witness =
    { /// The shortest command script (one `Command list` per frame) from `Init` reaching the goal.
      Script: Command list list
      /// The number of frames in the witness (0 when `Init` itself satisfies the goal).
      Depth: int }

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// The result of a bounded witness search. `Truncated` is the **fail-closed** signal (#266): the search
/// hit its depth bound or visited-set cap before exhausting the reachable frontier, so it is NOT a proof
/// of absence — it is distinct from `NotFound`, which means the frontier was fully explored within the
/// bound and no state satisfied the goal. A negative result is never a silent `None`.
[<RequireQualifiedAccess>]
type FindResult =
    /// The goal was reached; the shortest witness from `Init`.
    | Found of Witness
    /// The reachable frontier was fully explored within the bound and no state satisfied the goal.
    | NotFound
    /// The search stopped at its depth/visited cap before exhausting the frontier; carries the number
    /// of distinct states visited. Not a proof the goal is unreachable.
    | Truncated of visited: int

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// The reachable frontier within a bound: the set of distinct fingerprints reached, and whether the
/// search was truncated at its visited cap (in which case `States` is a lower bound, not the whole
/// reachable space).
type ReachResult<'f when 'f: comparison> =
    { /// Every distinct fingerprint reachable within `maxDepth` (or up to the visited cap).
      States: Set<'f>
      /// `true` when the visited cap was hit before the frontier was exhausted (fail-closed).
      Truncated: bool }

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// Search configuration. `Moves = None` derives the default alphabet (the empty frame plus each single
/// command in `Playable.Keymap`); `Some moves` restricts the search to those per-frame moves.
/// `MaxVisited` is a hard cap on distinct visited states; exceeding it fails closed (`Truncated`).
type ExploreConfig =
    { /// The per-frame moves to try at each node; `None` derives them from the keymap alphabet.
      Moves: Command list list option
      /// The hard cap on distinct visited states before the search fails closed.
      MaxVisited: int }

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// A bounded breadth-first **witness finder** over a `Playable`: the shortest input script from `Init`
/// reaching a goal, and the reachable-fingerprint frontier. It is a witness finder, **not a prover** —
/// a `NotFound`/`Truncated` within a bound is "not found within N", never "impossible". Pure: reaches
/// only `Game.Core` + the BCL, no I/O, no wall clock.
[<RequireQualifiedAccess>]
module Explore =

    /// Public contract value exposed by the FS.GG.Game.Harness package.
    /// The default config: derive the move alphabet from the keymap, with a generous visited cap.
    val defaultConfig: ExploreConfig

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The default move alphabet for a `Playable`: the empty frame plus each single command in
    /// `Playable.Keymap`, deduplicated.
    val defaultMoves: playable: Playable<'world, 'key> -> Command list list

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The shortest command script from `Init` reaching `goal`, by breadth-first search bounded by
    /// `maxDepth` and deduplicated by `fingerprint` (the visited-set key — search over a projection to
    /// control granularity). See `FindResult` for the fail-closed `Truncated` vs `NotFound` distinction.
    val findScript:
        playable: Playable<'world, 'key> ->
        fingerprint: ('world -> 'f) ->
        goal: ('world -> bool) ->
        maxDepth: int ->
            FindResult when 'f: comparison

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// `findScript` with an explicit `ExploreConfig` (restricted move alphabet and/or visited cap).
    val findScriptWith:
        config: ExploreConfig ->
        playable: Playable<'world, 'key> ->
        fingerprint: ('world -> 'f) ->
        goal: ('world -> bool) ->
        maxDepth: int ->
            FindResult when 'f: comparison

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The set of distinct fingerprints reachable from `Init` within `maxDepth`, with a fail-closed
    /// truncation flag. Answers "can the game ever be in a state like this?" and measures a fixture's
    /// reachable state space.
    val reachable:
        playable: Playable<'world, 'key> ->
        fingerprint: ('world -> 'f) ->
        maxDepth: int ->
            ReachResult<'f> when 'f: comparison

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// `reachable` with an explicit `ExploreConfig`.
    val reachableWith:
        config: ExploreConfig ->
        playable: Playable<'world, 'key> ->
        fingerprint: ('world -> 'f) ->
        maxDepth: int ->
            ReachResult<'f> when 'f: comparison
