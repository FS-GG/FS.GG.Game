namespace FS.GG.Game.Core

// The abstract input-command vocabulary — the policy half of the input-config split (ADR-0022, epic
// FS-GG/FS.GG.Rendering#330). Pure data, device-free: a command is an intent, and this module knows
// nothing of keys. `Game.Render` binds these to keys; `FS.GG.UI.KeyboardInput` produces one from a
// device. Reaches up to nothing.
//
// Qualified for the same reason `Source`/`StageResult` are: `Fire`/`Pause`/`Move*` are ordinary
// words, and a consumer's `open FS.GG.Game.Core` must not inject them.
[<RequireQualifiedAccess>]
type Command =
    | MoveNorth
    | MoveSouth
    | MoveWest
    | MoveEast
    | Fire
    | Pause

[<RequireQualifiedAccess>]
module Command =

    // The vocabulary, in the stable contract order.
    let all =
        [ Command.MoveNorth
          Command.MoveSouth
          Command.MoveWest
          Command.MoveEast
          Command.Fire
          Command.Pause ]

    // `id` and `label` are total matches over the closed DU, not lookups in a table — the compiler
    // then forces a new case to be given a token/label here rather than falling through to a run-time
    // miss. They are the single source of the token/label data; `ofId` derives its map FROM `id`, so
    // the two can never disagree. The id tokens are a frozen serialization contract (see the .fsi):
    // reword a label freely, never a token.
    let id (command: Command) =
        match command with
        | Command.MoveNorth -> "move.north"
        | Command.MoveSouth -> "move.south"
        | Command.MoveWest -> "move.west"
        | Command.MoveEast -> "move.east"
        | Command.Fire -> "fire"
        | Command.Pause -> "pause"

    let private byId = all |> List.map (fun c -> id c, c) |> Map.ofList

    let ofId (id: string) = Map.tryFind id byId

    let label (command: Command) =
        match command with
        | Command.MoveNorth -> "Move North"
        | Command.MoveSouth -> "Move South"
        | Command.MoveWest -> "Move West"
        | Command.MoveEast -> "Move East"
        | Command.Fire -> "Fire"
        | Command.Pause -> "Pause"
