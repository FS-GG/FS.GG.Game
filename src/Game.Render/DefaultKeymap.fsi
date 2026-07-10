namespace FS.GG.Game.Render

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The **default keymap**: the shipped command→key binding for a fresh game, and the *policy* half
/// of the input-config split (ADR-0022, epic FS-GG/FS.GG.Rendering#330). `FS.GG.Game.Core` owns the
/// abstract command vocabulary (`Command`, device-free intents); `FS.GG.UI.KeyboardInput` owns the
/// *mechanism* — a command-id-agnostic `Keymap` that indexes bindings by raw key (Rendering#331).
/// THIS module is the one place those two meet: it binds each `Command` to a sensible default key,
/// producing a `Keymap` a host starts from and a config screen rebinds.
///
/// The render edge (ADR-0022 §2): `FS.GG.Game.Core` reaches up to nothing; this is the second place
/// (after `Adapter`) that reaches UP to `FS.GG.UI.*` — here `FS.GG.UI.KeyboardInput` — and it adds
/// the `FS.GG.Game.Render -> FS.GG.UI.KeyboardInput` package edge (registered in FS-GG/.github#365).
///
/// Deterministic and pure: `value` is a constant, and the keymap is keyed by the canonical `KeyId`
/// (`ViewerKeyboard.toKeyId`) and the frozen command token (`Command.id`), so it round-trips a saved
/// keymap and agrees row-for-row with the config screen's enumeration of `Command.all`.
[<RequireQualifiedAccess>]
module DefaultKeymap =

    /// Public contract value exposed by the FS.GG.Game.Render package.
    /// The default command→key pairing as data, in `Command.all` order — one distinct key per command,
    /// so no two commands share a key and the built keymap has no conflicts. This *is* the policy: the
    /// keys a game ships with and a config screen shows before the player rebinds. Every `Command` is
    /// present exactly once (the source is a total match over the vocabulary).
    val bindings: (FS.GG.Game.Core.Command * FS.GG.UI.KeyboardInput.ViewerKey) list

    /// Public contract value exposed by the FS.GG.Game.Render package.
    /// The default keymap, ready to hand to `Keyboard.init` (via `Keymap.toBindings`) or to seed a
    /// config screen. Built from `bindings`: each entry keys the canonical `KeyId`
    /// (`ViewerKeyboard.toKeyId key`) to the command's frozen wire token (`Command.id command`). So
    /// `Keymap.resolve value (ViewerKeyboard.toKeyId key)` returns `Some (Command.id command)` for
    /// every default binding, and `Keymap.validate value` is empty (no key bound twice).
    val value: FS.GG.UI.KeyboardInput.Keymap
