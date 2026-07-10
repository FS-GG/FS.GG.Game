namespace FS.GG.Game.Render

open FS.GG.Game.Core
open FS.GG.UI.KeyboardInput

[<RequireQualifiedAccess>]
module DefaultKeymap =

    /// The default key for a command. A *total* match over the whole vocabulary: adding a `Command`
    /// case without a default key here is a compile error, so `bindings` can never silently omit a
    /// command. Movement is the arrow cluster; `Fire` is the near-universal action key (Space);
    /// `Pause` is Escape (the menu/pause key). Every command gets a distinct key — no key is bound
    /// twice — so the assembled keymap carries no conflict (`Keymap.validate value` is empty).
    let private keyFor (command: Command) : ViewerKey =
        match command with
        | Command.MoveNorth -> ArrowUp
        | Command.MoveSouth -> ArrowDown
        | Command.MoveWest -> ArrowLeft
        | Command.MoveEast -> ArrowRight
        | Command.Fire -> Space
        | Command.Pause -> Escape

    let bindings: (Command * ViewerKey) list =
        Command.all |> List.map (fun command -> command, keyFor command)

    let value: Keymap =
        // Fold the declared pairing into a keymap: canonical KeyId (ViewerKeyboard.toKeyId) -> frozen
        // command token (Command.id). Keys are distinct, so add order does not matter.
        bindings
        |> List.fold
            (fun keymap (command, key) -> Keymap.add (ViewerKeyboard.toKeyId key) (Command.id command) keymap)
            Keymap.empty
