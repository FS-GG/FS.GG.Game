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

    /// The SVG-default registry for a game-state type, mounted in `container`: `RendererRegistry.svg` with
    /// `view` projecting a state onto a `Scene` and `mountScene` as the retained surface. Pixi and Babylon
    /// adapters are added later with `RendererRegistry.register` when their bindings exist.
    val registry:
        container: HTMLElement -> options: SvgBrowserOptions -> view: ('State -> Scene) -> RendererRegistry<'State>
