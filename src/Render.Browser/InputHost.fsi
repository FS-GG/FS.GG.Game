namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene

/// The input host every backend attaches to its input-host mount (FS.GG.Game#788, ADR 0001 §1 and §4): pointer,
/// touch, keyboard and mirror input reported as `InputEvent`s, the drawn cue layer, and the single-Tab-stop listbox or
/// grid the accessible mirror forms with the surface.
module internal InputHost =

    /// Where the mirror's items live.
    [<RequireQualifiedAccess>]
    type Ownership =
        /// In a visually hidden group appended to `parent`, owned by the surface through `aria-owns` (an SVG root).
        | Owns of parent: HTMLElement
        /// Inside the surface, as its fallback content (a canvas).
        | Fallback

    /// Which input host a canvas mount carries: the selection host of `withInteraction` (`CanvasInput`), or this one.
    [<RequireQualifiedAccess>]
    type CanvasHost =
        | Selection of report: (SelectionRequest -> unit)
        | Input of spec: InputHostSpec * report: (InputEvent -> unit)

    /// One mount's input host.
    [<Sealed>]
    type Host =

        /// Present `frame`: the mirror follows, the focus and hover are dropped when their object is gone, and the
        /// frame is drawn with the cue layer.
        member Update: frame: InteractiveScene -> unit

        /// Remove every listener and the mirror, and cancel a pending long-press. Idempotent.
        member Dispose: unit -> unit

    /// Attach a host to `surface` (the SVG root or the canvas, already in the page and drawn at `width` x `height` scene
    /// units). `hitTest` is the backend's own, in scene coordinates; `draw scene cues` draws the frame's Scene with the
    /// cue nodes above it (unlit on Babylon). Every event goes to `report`, from the host's own event handlers. The
    /// listeners are capture-phase listeners on the surface; `stopPointer` also stops a pointer press and a wheel there,
    /// so that a surface's own built-in handling (the retained SVG root's pan and zoom) never runs.
    val attach:
        surface: Element ->
        ownership: Ownership ->
        backend: RendererBackend ->
        label: string ->
        width: float ->
        height: float ->
        stopPointer: bool ->
        hitTest: (Point -> string option) ->
        spec: InputHostSpec ->
        report: (InputEvent -> unit) ->
        draw: (Scene -> SceneNode list -> unit) ->
        frame: InteractiveScene ->
            Host
