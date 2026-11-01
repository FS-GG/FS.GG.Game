namespace FS.GG.Game.Render.Bench

open FS.GG.UI.Scene

// GAME-RENDER-ADOPT-01.2 (FS.GG.Game#746): what a game supplies to the rendering benchmark.
//
// The benchmark is generic in the game-state type, exactly like Game.Render's `RendererRegistry<'State>`: a game
// plugs in its OWN state type, a deterministic per-frame script over that state, and one or more scenes, each a
// `view` from a scene size to the `FS.GG.UI.Scene` the three backends draw. The runner (Bench.fs) registers that view
// with the SVG, Pixi and Babylon adapters through `RendererRegistry`, selects one backend through the Elmish
// `RendererMsg.SelectBackend` message, and presents every stepped state through `RendererMsg.StateChanged`, so what
// is measured is the production renderer-selection route, not a benchmark-only renderer.

/// One scripted scene of a game: how the game's state is drawn at a requested size.
[<NoEquality; NoComparison>]
type BenchScene<'State> =
    {
        /// Stable identifier, used on the command line and in results (lowercase, `-` separated).
        Id: string
        /// What the scene exercises, in one line, recorded in the results.
        Description: string
        /// Project a state onto a scene with EXACTLY `size` drawable nodes (Rectangle, Circle or FilledEllipse), so
        /// the size axis of the results is the drawable-node count every backend is asked to draw. The runner checks
        /// the count on the first frame and refuses a scene that does not keep it.
        View: int -> 'State -> Scene
    }

/// A game's plug-in: its state type, a deterministic script and its scenes.
[<NoEquality; NoComparison>]
type BenchGame<'State> =
    {
        /// Stable identifier, used on the command line and in results (lowercase, `-` separated).
        Id: string
        /// The full name of the game-state type, recorded in the results.
        StateType: string
        /// The canvas (and SVG root) size every backend draws into, in CSS pixels; scenes draw in these units.
        Width: float
        Height: float
        /// The state the benchmark starts from.
        Init: unit -> 'State
        /// The scripted step for one frame: frame index (from 0) and current state to next state. It must be
        /// deterministic, so every backend, size and repeat is shown the same sequence of states.
        Step: int -> 'State -> 'State
        /// The scenes this game offers. The first is the default.
        Scenes: BenchScene<'State> list
    }

/// Counting helpers for scene views.
[<RequireQualifiedAccess>]
module BenchScene =

    /// The drawable nodes in a scene (Rectangle, Circle, FilledEllipse), through Group and Translate. This is the
    /// count the size axis means; the canvas adapters draw exactly these node kinds.
    let rec drawableNodes (scene: Scene) : int =
        scene.Nodes
        |> List.sumBy (fun node ->
            match node with
            | SceneNode.Rectangle _
            | SceneNode.Circle _
            | SceneNode.FilledEllipse _ -> 1
            | SceneNode.Group inner -> inner |> List.sumBy drawableNodes
            | SceneNode.Translate(_, inner) -> drawableNodes inner
            | _ -> 0)
