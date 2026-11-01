namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene

/// The input host the canvas adapters (Pixi, Babylon) attach to an interactive mount (FS.GG.Game#751): the
/// `SceneInteraction` contract on a canvas, with the backend's own hit test.
module internal CanvasInput =

    /// One mount's input host: listeners on the canvas and on its accessible mirror, and the mirror itself.
    [<Sealed>]
    type Host =

        /// Present the frame's objects and selection: the mirror follows at once.
        member Update: frame: InteractiveScene -> unit

        /// Remove every listener the host added and the mirror. Idempotent.
        member Dispose: unit -> unit

    /// Attach an input host to `canvas` (already in `container`): the canvas becomes focusable, a primary pointer press
    /// asks `hitTest` (the backend's own, in scene coordinates, which are the canvas's pixels) which object it is on,
    /// the keyboard follows `SceneInteraction.key`, and the mirror is appended to `container`. Every request goes to
    /// `report`, from the host's own event handlers.
    val attach:
        container: HTMLElement ->
        canvas: HTMLCanvasElement ->
        backend: RendererBackend ->
        label: string ->
        width: float ->
        height: float ->
        hitTest: (Point -> string option) ->
        report: (SelectionRequest -> unit) ->
        frame: InteractiveScene ->
            Host

    /// The mirror of an input host as the retained SVG surface writes it: a group carrying `data-scene-controls` and
    /// `data-renderer`, a polite status, and one button per object carrying `data-scene-control-id`. The SVG surface
    /// tags its own mirror with this so every backend's mirror is found the same way.
    val tagMirror: mirror: Element -> backend: RendererBackend -> unit
