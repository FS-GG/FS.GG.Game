namespace FS.GG.Game.Harness

open FS.GG.Game.Core

[<RequireQualifiedAccess>]
module Bots =

    let sitter<'view> : Bot<'view> =
        { Decide = fun _ rng -> struct ([], rng) }

    let scripted (script: Command list list) : Bot<'view> =
        // The sole stateful combinator: a captured playback index advances per decision. Single-use —
        // a fresh instance per run (see the .fsi). The generator is never consumed.
        let frames = List.toArray script
        let mutable i = 0

        { Decide =
            fun _ rng ->
                let commands = if i < frames.Length then frames.[i] else []
                i <- i + 1
                struct (commands, rng) }

    let random (draw: Rng -> struct (Command list * Rng)) : Bot<'view> =
        { Decide = fun _ rng -> draw rng }

    let chase (axes: ('view -> int) * ('view -> int)) (up: Command) (down: Command) : Bot<'view> =
        let target, self = axes

        { Decide =
            fun view rng ->
                let t = target view
                let s = self view

                if t < s then struct ([ up ], rng)
                elif t > s then struct ([ down ], rng)
                else struct ([], rng) }

    let greedyToward (score: 'view -> Command -> float) (moves: Command list) : Bot<'view> =
        { Decide =
            fun view rng ->
                match moves with
                | [] -> struct ([], rng)
                | _ ->
                    // List.maxBy returns the earliest element attaining the maximum, so ties resolve to
                    // the first move in the caller's order.
                    let best = moves |> List.maxBy (fun m -> score view m)
                    struct ([ best ], rng) }

    let on (project: 'outer -> 'inner) (bot: Bot<'inner>) : Bot<'outer> =
        { Decide = fun outer rng -> bot.Decide (project outer) rng }
