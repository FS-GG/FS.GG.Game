namespace FS.GG.Game.Harness

open FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Harness package.
/// The result of a bot-driven run: the recorded `Trace`, plus the per-step `Command` lists the bot
/// actually issued. `Captured` is the run turned into a replayable script — feed it back through
/// `Driver.runCommands` on the same `Playable` and the replay's frames are byte-identical, which is
/// how a bot (or agent) playthrough becomes a deterministic regression golden.
type Run<'f> =
    { /// The trace recorded during the run (`Origin.InputDriven`).
      Trace: Trace<'f>
      /// The commands issued at each step, in step order — a script for `runCommands`.
      Captured: Command list list }

/// Public contract module exposed by the FS.GG.Game.Harness package.
/// The scripted and bot single-seat drivers. Every driver advances the world by exactly one whole
/// fixed step (`Playable.Step world Playable.Dt`) per input frame at the constant `Dt` — never a
/// variable `dt`, and never a render interpolant fed back into the sim — and records `fingerprint`
/// of the world after each step. No I/O, no wall-clock read.
[<RequireQualifiedAccess>]
module Driver =

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The default fingerprint: the full `'world` itself. A trace of these compares whole worlds by
    /// structural equality (the default of DEC-001); pass a projection instead when the world is large
    /// or carries fields irrelevant to the assertion.
    val identityFingerprint: world: 'world -> 'world

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// The shared core: for each frame, fold `Apply` over that frame's commands onto the world, then
    /// advance one whole fixed step, recording `fingerprint`. Commands are already resolved intents,
    /// so this is also the replay entry point for a captured `Run.Captured`.
    val runCommands:
        playable: Playable<'world, 'key> -> fingerprint: ('world -> 'f) -> script: Command list list -> Trace<'f>

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Drive a script of raw key tokens: each frame's keys are resolved through `playable.Keymap`
    /// (unbound tokens dropped) into commands, then folded exactly as `runCommands` does. This is the
    /// real player route — raw key -> keymap -> `Command` -> step — so its evidence is non-synthetic.
    val runScript:
        playable: Playable<'world, 'key> -> fingerprint: ('world -> 'f) -> keyScript: 'key list list -> Trace<'f>

    /// Public contract function exposed by the FS.GG.Game.Harness package.
    /// Let a `Bot` play the game for `steps` fixed steps from `Playable.Init`, seeding its generator
    /// from `seed`. Each step: observe the world into a `'view`, decide commands, apply them, advance
    /// one fixed step, record `fingerprint`. Returns the trace and the captured command script.
    val runBot:
        playable: Playable<'world, 'key> ->
        observe: ('world -> 'view) ->
        bot: Bot<'view> ->
        seed: uint64 ->
        steps: int ->
        fingerprint: ('world -> 'f) ->
            Run<'f>
