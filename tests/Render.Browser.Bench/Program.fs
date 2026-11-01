module FS.GG.Game.Render.Bench.Program

// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#746): page entry. Reads one run's parameters from the URL
// (`?game=reference-game&scene=room-tiles&size=1000&backend=pixi&warmup=60&interactive=1`) and starts that game's runner. A new
// game plugs in by adding one line to `games` below with its own `BenchGame<'State>` (see tools/render-bench/README.md).

open Fable.Core
open FS.GG.Game.Render

[<Emit("new URLSearchParams(location.search).get($0) ?? ''")>]
let private param (name: string) : string = jsNative

[<Emit("window.__fsggBench.error = $0; window.__fsggBench.phase = 'error'")>]
let private refuse (message: string) : unit = jsNative

/// Every game the page can run, by id. Each entry closes over its own state type.
let games: (string * (Runner.BenchConfig -> unit)) list =
    [ ReferenceGameBench.game.Id, Runner.run ReferenceGameBench.game ]

let private positive (name: string) (fallback: int) =
    match param name with
    | "" -> Some fallback
    | text ->
        match System.Int32.TryParse text with
        | true, value when value > 0 -> Some value
        | _ -> None

let start () =
    let gameId = param "game"
    // `interactive=1` (the default) declares the game's SceneInteraction, as a game on Render.Browser 0.18.0 does;
    // `interactive=0` is the plain mountScene route.
    let interactive = param "interactive" <> "0"

    match List.tryFind (fun (id, _) -> id = gameId) games, RendererBackend.tryParse (param "backend"), positive "size" 0, positive "warmup" 60 with
    | None, _, _, _ -> refuse (sprintf "unknown game '%s' (known: %s)" gameId (games |> List.map fst |> String.concat ", "))
    | _, None, _, _ -> refuse (sprintf "unknown backend '%s'" (param "backend"))
    | _, _, None, _
    | _, _, Some 0, _ -> refuse "size must be a positive integer"
    | _, _, _, None -> refuse "warmup must be a positive integer"
    | Some(_, runGame), Some backend, Some size, Some warmup ->
        runGame
            {
                Backend = backend
                SceneId = param "scene"
                Size = size
                WarmupFrames = warmup
                Interactive = interactive
            }

start ()
