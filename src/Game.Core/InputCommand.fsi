namespace FS.GG.Game.Core

/// Public contract type exposed by the FS.GG.Game.Core package.
/// The abstract vocabulary of input *intents* a game reacts to — Move, Fire, Pause — as pure data,
/// independent of any device. This is the **policy** half of the input-config split (ADR-0022, epic
/// FS-GG/FS.GG.Rendering#330): `Game.Core` owns *which commands exist*; the render/input mechanism
/// (`FS.GG.UI.KeyboardInput`) owns *how a device produces one* and never learns a game action. A
/// command is a device-free intent, so it names no key, button, or axis — the default keymap in
/// `FS.GG.Game.Render` binds each of these to a key, and Rendering's config screen rebinds them.
///
/// **Qualified.** `Fire`, `Pause`, and the `Move*` cases are ordinary words a consumer near-certain
/// to have its own of, so `open FS.GG.Game.Core` must not inject them. Write `Command.Fire`.
///
/// Reaches up to nothing (BCL/FSharp.Core-only, ADR-0022 §2). Structural equality makes a command a
/// stable, hashable map key for a keymap; `Command.id` gives the wire/serialization token.
[<RequireQualifiedAccess>]
type Command =
    /// Step one cell toward −Y (north / up).
    | MoveNorth
    /// Step one cell toward +Y (south / down).
    | MoveSouth
    /// Step one cell toward −X (west / left).
    | MoveWest
    /// Step one cell toward +X (east / right).
    | MoveEast
    /// The primary action — attack / shoot / use.
    | Fire
    /// Toggle the simulation between running and paused.
    | Pause

/// Public contract module exposed by the FS.GG.Game.Core package.
[<RequireQualifiedAccess>]
module Command =

    /// Public contract value exposed by the FS.GG.Game.Core package.
    /// The whole vocabulary, in a stable, deterministic order — this **is** the rebindable command
    /// list a config screen enumerates and a default keymap binds. Adding a command appends here; the
    /// order is part of the contract, so a persisted keymap and a screen agree row-for-row.
    val all: Command list

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// The stable string token for a command — its identity on the wire and in a persisted keymap,
    /// and the key Rendering's *command-id-agnostic* keymap type reads (it holds this string, never a
    /// `Command`). These tokens are a serialization **contract**: they are total, unique across `all`,
    /// and must not change once shipped, or a saved keymap silently rebinds. Round-trips with `ofId`.
    val id: command: Command -> string

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// Parse a command back from its `id` token. `Some` for exactly the tokens `id` emits (over `all`),
    /// `None` for anything else — an unknown token in a loaded keymap is dropped, not a crash. Total.
    val ofId: id: string -> Command option

    /// Public contract function exposed by the FS.GG.Game.Core package.
    /// A human-readable name for a command, for a config screen's row label. Distinct from `id`: the
    /// label is display text that may be reworded freely, the `id` is a frozen wire token. Non-empty
    /// and total.
    val label: command: Command -> string
