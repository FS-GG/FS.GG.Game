namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// Everything the harness needs to drive one game headlessly through the **real input frontier**:
/// an initial world, the game's keymap (raw key token -> device-free `Command`), the command-apply
/// transition, the fixed step function, and the fixed step interval. The world `'world` and the raw
/// key token `'key` are the caller's; the harness reaches up to nothing beyond `FS.GG.Game.Core`
/// (the keymap type lives in the render/input layer, deliberately not referenced here).
///
/// `Apply` and `Step` are pure transitions (Model-Update-Effect): no I/O, no wall-clock read. `Step`
/// is exactly the `integrate: 'world -> dt -> 'world` shape `Loop.advance` drives, so a game already
/// built on `FS.GG.Game.Core.Loop` hands its own step straight in.
type Playable<'world, 'key when 'key: comparison> =
    { /// The world the harness starts every run from.
      Init: 'world
      /// The game's keymap: which `Command` (if any) each raw key token produces. An absent key is an
      /// unbound token that produces no command.
      Keymap: Map<'key, Command>
      /// Apply one device-free command intent to the world — the input half of the game's update.
      Apply: Command -> 'world -> 'world
      /// Advance the world by one whole fixed step of `dt` seconds — the game's `integrate`.
      Step: 'world -> float -> 'world
      /// The fixed step interval in seconds. Constant across a run, so the sim only ever sees whole
      /// fixed steps and a scripted replay is deterministic.
      Dt: float }

/// Public contract module exposed by the FS.GG.Game.Harness package.
[<RequireQualifiedAccess>]
module Playable =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Resolve a raw key token through the game's keymap into a `Command`. `None` for an unbound
    /// token — the scripted driver drops it rather than applying anything, so an unmapped key is a
    /// no-op, never a raw application.
    val resolve: playable: Playable<'world, 'key> -> key: 'key -> Command option

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// An in-process decision policy. `Decide` sees only a caller-supplied `'view` and a seeded, value
/// `Rng` — **never** the full world model, which cannot appear in this signature — and threads the
/// advanced generator back. Determinism is a consequence of the shape: identical `(view, seed)` yield
/// identical commands, because `Rng` is a pure value and `Decide` is a function. Instantiate `'view`
/// as `Ai.TeamView<_>` to inherit the fog boundary, or as any projection the policy needs.
type Bot<'view> =
    { /// Decide the commands to issue this step from the observed view and the current generator,
      /// returning the commands and the advanced generator.
      Decide: 'view -> Rng -> struct (Command list * Rng) }
