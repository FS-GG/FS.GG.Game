namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// A minimal counter-script that violates an author invariant, found by `Properties.check` and reduced
/// by shrinking. Replaying `Script` through `Driver.runCommands` from `Init` reaches a step at which the
/// invariant is false. `Step = -1` with an empty `Script` means the invariant is already false at `Init`.
type Counterexample =
    { /// The seed of the property run that produced the violation (the run is reproducible from it).
      Seed: uint64
      /// The shrunk minimal script that still violates the invariant.
      Script: Command list list
      /// The 0-based post-step frame index of the first violation; `-1` when `Init` itself violates it.
      Step: int }

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// The result of a property run: the invariant `Held` across `runs` generated runs, or was `Falsified`
/// by a shrunk counterexample.
[<RequireQualifiedAccess>]
type PropertyResult =
    /// The invariant held at every step of every one of `runs` generated runs.
    | Held of runs: int
    /// The invariant was violated; a minimal counter-script.
    | Falsified of Counterexample

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// Property-run configuration. `Moves = None` derives the default alphabet (empty frame plus each single
/// command in `Playable.Keymap`); `Some moves` restricts generation to those per-frame moves.
type PropertyConfig =
    { /// The number of random runs to generate.
      Runs: int
      /// The maximum number of frames in a generated script.
      MaxLength: int
      /// The per-frame moves to draw from; `None` derives them from the keymap alphabet.
      Moves: Command list list option }

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// A pure, seeded property runner: generates random valid scripts over the `Playable.Keymap` alphabet
/// and asserts an author invariant (`'world -> bool`) holds at **every step of every run**, shrinking a
/// violation to a minimal counter-script. Pure over `Game.Core`'s `Rng` — the package references no
/// FsCheck (FsCheck stays a test-only tool), so the leaf-dependency invariant holds.
[<RequireQualifiedAccess>]
module Properties =

    /// Public contract value exposed by the FS.GG.Game.Harness package.
    /// The default config: 1000 runs, up to 32 frames each, alphabet derived from the keymap.
    val defaultConfig: PropertyConfig

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Generate random valid scripts over the keymap alphabet from `seed`, drive each through the game,
    /// and assert `invariant` holds at `Init` and after every fixed step of every run. Returns `Held`
    /// with the run count, or `Falsified` with a shrunk minimal counter-script on the first violation.
    val check:
        playable: Playable<'world, 'key> ->
        invariant: ('world -> bool) ->
        seed: uint64 ->
            PropertyResult

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// `check` with an explicit `PropertyConfig` (run count, max length, restricted move alphabet).
    val checkWith:
        config: PropertyConfig ->
        playable: Playable<'world, 'key> ->
        invariant: ('world -> bool) ->
        seed: uint64 ->
            PropertyResult
