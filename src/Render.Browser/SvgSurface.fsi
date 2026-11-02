namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene
open FS.GG.UI.Scene.SvgBrowser

/// The browser's retained SVG surface for the SVG renderer adapter.
///
/// `RendererAdapter.svg view mountScene` leaves the host-owned retained surface open; this is that
/// surface. It mounts ONE `SvgBrowser` root per mount into a container and reconciles every presented
/// `Scene` into it through the portable reducer, so SVG output is the unchanged existing path
/// (`FS.GG.UI.Scene.SvgBrowser`), not a second SVG implementation.
[<RequireQualifiedAccess>]
module SvgSurface =

    /// Options for the retained SVG root: pixel size, accessible label and the wheel-zoom factor the
    /// SvgBrowser contract requires (finite and greater than one).
    val defaultOptions: label: string -> width: float -> height: float -> SvgBrowserOptions

    /// The `mountScene` argument for `RendererAdapter.svg` / `RendererRegistry.svg`. Each call mounts a new
    /// SVG root in `container`; `Present` replaces the scene (a rejected or invalid scene raises, which the
    /// renderer host contains and reports as a mount failure); `Dispose` removes the root and its listeners.
    /// A mount the SvgBrowser contract refuses is returned as `Error`, never thrown.
    val mountScene:
        container: HTMLElement -> options: SvgBrowserOptions -> scene: Scene -> Result<RendererMount<Scene>, string>

    /// The interactive SVG mount (FS.GG.Game#751): the retained `SvgBrowser` root already has the `SceneInteraction`
    /// contract (browser hit test, keyboard focus and selection, the HTML mirror, listener disposal); this binds it to
    /// the game. Each selectable object becomes a selectable semantic object with a transparent hit shape over its
    /// region, above the drawn Scene; a selection the player changes there is reported as a `SelectionRequest`, and the
    /// game's selection in each presented frame is applied without reporting. The root and the mirror carry
    /// `data-renderer="svg"`. Objects that `SceneInteraction.problems` rejects, or that use the reserved id
    /// `game-render:scene`, are refused at mount and raise from `Present`.
    val mountInteractive:
        container: HTMLElement ->
        options: SvgBrowserOptions ->
        report: (SelectionRequest -> unit) ->
        frame: InteractiveScene ->
            Result<RendererMount<InteractiveScene>, string>

    /// The SVG input-host mount (FS.GG.Game#788): the retained `SvgBrowser` root draws each frame's Scene with `spec`'s
    /// cue layer above it, and the input host owns every input on the root (its own pan, zoom and keyboard focus never
    /// run). The root is the single Tab stop, a `role=listbox` or `role=grid` labelled `options`' label, and owns the
    /// mirror's items (a visually hidden group appended to `container`) through `aria-owns`; `aria-activedescendant`
    /// names the focused object's item. The hit test is `SceneInteraction.hitTest`. Objects that
    /// `SceneInteraction.problems` rejects, or that use the reserved id `game-render:scene`, are refused at mount and
    /// raise from `Present`.
    val mountInput:
        container: HTMLElement ->
        options: SvgBrowserOptions ->
        spec: InputHostSpec ->
        report: (InputEvent -> unit) ->
        frame: InteractiveScene ->
            Result<RendererMount<InteractiveScene>, string>

    /// `mountScene container options` as the SVG `SceneSurface` for `SceneRenderers` (`SceneRenderers.create view
    /// (surface ...).MountScene`, or `BrowserRenderers.declare`, which does that for all three backends).
    /// It is interactive through `mountInteractive container options` and has the input host `mountInput container
    /// options`.
    val surface: container: HTMLElement -> options: SvgBrowserOptions -> SceneSurface

    /// The SVG-default registry for a game-state type, mounted in `container`: `RendererRegistry.svg` with
    /// `view` projecting a state onto a `Scene` and `mountScene` as the retained surface. Pixi and Babylon
    /// adapters are added later with `RendererRegistry.register` when their bindings exist.
    val registry:
        container: HTMLElement -> options: SvgBrowserOptions -> view: ('State -> Scene) -> RendererRegistry<'State>
