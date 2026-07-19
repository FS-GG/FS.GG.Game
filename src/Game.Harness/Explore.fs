namespace FS.GG.Game.Harness

open System.Collections.Generic
open FS.GG.Game.Core

type Witness =
    { Script: Command list list
      Depth: int }

[<RequireQualifiedAccess>]
type FindResult =
    | Found of Witness
    | NotFound
    | Truncated of visited: int

type ReachResult<'f when 'f: comparison> =
    { States: Set<'f>
      Truncated: bool }

type ExploreConfig =
    { Moves: Command list list option
      MaxVisited: int }

[<RequireQualifiedAccess>]
module Explore =

    let defaultConfig: ExploreConfig = { Moves = None; MaxVisited = 200_000 }

    let defaultMoves (playable: Playable<'world, 'key>) : Command list list =
        // the empty frame plus each single command in the keymap alphabet, deduplicated.
        []
        :: (playable.Keymap
            |> Map.toList
            |> List.map snd
            |> List.distinct
            |> List.map (fun c -> [ c ]))

    // Apply a frame's commands in order, then advance exactly one whole fixed step — the same
    // per-frame transition `Driver.runCommands` uses, so a witness replays identically.
    let private stepWith (playable: Playable<'world, 'key>) (move: Command list) (world: 'world) : 'world =
        let applied = move |> List.fold (fun w c -> playable.Apply c w) world
        playable.Step applied playable.Dt

    let findScriptWith
        (config: ExploreConfig)
        (playable: Playable<'world, 'key>)
        (fingerprint: 'world -> 'f)
        (goal: 'world -> bool)
        (maxDepth: int)
        : FindResult =
        let moves = config.Moves |> Option.defaultValue (defaultMoves playable)
        let init = playable.Init

        if goal init then
            FindResult.Found { Script = []; Depth = 0 }
        else
            let visited = HashSet<'f>()
            visited.Add(fingerprint init) |> ignore
            let queue = Queue<'world * Command list list * int>()
            queue.Enqueue(init, [], 0)

            let mutable result: FindResult option = None
            let mutable depthCutoff = false

            while result.IsNone && queue.Count > 0 do
                let struct (world, revScript, depth) =
                    let w, r, d = queue.Dequeue()
                    struct (w, r, d)

                for move in moves do
                    if result.IsNone then
                        let child = stepWith playable move world
                        let key = fingerprint child

                        if not (visited.Contains key) then
                            if goal child then
                                result <- Some(FindResult.Found { Script = List.rev (move :: revScript); Depth = depth + 1 })
                            elif depth + 1 < maxDepth then
                                visited.Add key |> ignore
                                queue.Enqueue(child, move :: revScript, depth + 1)

                                if visited.Count > config.MaxVisited then
                                    result <- Some(FindResult.Truncated visited.Count)
                            else
                                // a new frontier node beyond the depth bound: the frontier is not
                                // exhausted, so a negative answer must fail closed (Truncated).
                                visited.Add key |> ignore
                                depthCutoff <- true

            match result with
            | Some r -> r
            | None -> if depthCutoff then FindResult.Truncated visited.Count else FindResult.NotFound

    let findScript
        (playable: Playable<'world, 'key>)
        (fingerprint: 'world -> 'f)
        (goal: 'world -> bool)
        (maxDepth: int)
        : FindResult =
        findScriptWith defaultConfig playable fingerprint goal maxDepth

    let reachableWith
        (config: ExploreConfig)
        (playable: Playable<'world, 'key>)
        (fingerprint: 'world -> 'f)
        (maxDepth: int)
        : ReachResult<'f> =
        let moves = config.Moves |> Option.defaultValue (defaultMoves playable)
        let init = playable.Init
        let visited = HashSet<'f>()
        visited.Add(fingerprint init) |> ignore
        let queue = Queue<'world * int>()
        queue.Enqueue(init, 0)
        let mutable truncated = false

        while not truncated && queue.Count > 0 do
            let struct (world, depth) =
                let w, d = queue.Dequeue()
                struct (w, d)

            if depth < maxDepth then
                for move in moves do
                    if not truncated then
                        let child = stepWith playable move world
                        let key = fingerprint child

                        if not (visited.Contains key) then
                            visited.Add key |> ignore

                            if visited.Count > config.MaxVisited then
                                truncated <- true
                            else
                                queue.Enqueue(child, depth + 1)

        { States = Set.ofSeq visited
          Truncated = truncated }

    let reachable
        (playable: Playable<'world, 'key>)
        (fingerprint: 'world -> 'f)
        (maxDepth: int)
        : ReachResult<'f> =
        reachableWith defaultConfig playable fingerprint maxDepth
