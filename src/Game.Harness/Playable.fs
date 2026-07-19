namespace FS.GG.Game.Harness

open FS.GG.Game.Core

type Playable<'world, 'key when 'key: comparison> =
    { Init: 'world
      Keymap: Map<'key, Command>
      Apply: Command -> 'world -> 'world
      Step: 'world -> float -> 'world
      Dt: float }

[<RequireQualifiedAccess>]
module Playable =

    let resolve (playable: Playable<'world, 'key>) (key: 'key) : Command option =
        Map.tryFind key playable.Keymap

type Bot<'view> =
    { Decide: 'view -> Rng -> struct (Command list * Rng) }
