namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene

/// A game's own retained SVG surface as a renderer adapter at the game-state type: the state projects onto a
/// `RetainedScene`, so selectable objects, accessible labels, the camera and the game's input host stay what the
/// game made them, instead of being flattened into one non-selectable `Scene` object by `SvgSurface`.
///
/// Built by `RetainedSvgSurface.create` and adjusted by the `with*` functions; the type is opaque so a later option is
/// not a breaking change.
[<Sealed>]
type RetainedSvgSurface<'State>

/// The retained-scene SVG adapter (GAME-RENDER-ADOPT-01.2, FS.GG.Game#750).
///
/// ```fsharp
/// let surface =
///     RetainedSvgSurface.create container "Board" size scene
///     |> RetainedSvgSurface.withSelection selected onSelect
///     |> RetainedSvgSurface.withInput attachInput
///
/// let registry = RetainedSvgSurface.registry surface   // no placeholder: the ready adapter is the default
/// ```
///
/// What the adapter owns, so the game does not:
///
/// - **Revisions.** Each presented scene gets the next `RetainedScene.Revision` (the first is 1), so a game's scene
///   function need not manage them and a presented scene is never rejected as stale.
/// - **Camera.** The user's pan and zoom survive every present: the camera of the mounted host replaces the camera of
///   the projected scene. The first scene's camera is the initial one.
/// - **Selection.** After each scene the surface's selection follows the state (`withSelection`'s `selected`), and a
///   selection the user makes on the surface is reported (`onSelect`). The report is never made from `Mount`,
///   `Present` or `Dispose` (those synchronise silently), so a game may dispatch from it.
/// - **Input.** `withInput` attaches the game's input host to the container at each mount and releases it with
///   that mount, before the SVG host is disposed. An input host that refuses fails the mount, and the SVG host
///   just mounted is disposed, so a refused mount leaves nothing behind.
/// - **Viewport.** The SVG host's viewport is fixed at mount: `size` is read at `Mount`, and a `Present` whose size
///   differs raises (the renderer host contains it and recovers). `registry` therefore declares the size as the
///   registry's viewport, so a size change remounts the surface with the latest state before it is presented.
[<RequireQualifiedAccess>]
module RetainedSvgSurface =

    /// A surface in `container`. `label` is the SVG root's accessible label (not blank), `size` the viewport in
    /// pixels, `(width, height)`, each finite and greater than zero, and `scene` the retained projection of a state.
    val create:
        container: HTMLElement ->
        label: string ->
        size: ('State -> float * float) ->
        scene: ('State -> RetainedScene) ->
            RetainedSvgSurface<'State>

    /// Keep the surface's selection in step with the game. `selected` names the object the state says is selected
    /// (`None` clears it); an unknown or non-selectable id leaves the surface unchanged. `onSelect` receives the
    /// object the USER selected on the surface, `None` when the user cleared it. Replaces an earlier declaration.
    val withSelection:
        selected: ('State -> string option) ->
        onSelect: (string option -> unit) ->
        surface: RetainedSvgSurface<'State> ->
            RetainedSvgSurface<'State>

    /// Attach the game's input host to the container at every mount. The returned `IDisposable` is disposed with the
    /// mount; `Error` fails the mount (and the SVG host just mounted is disposed). Replaces an earlier declaration.
    val withInput:
        attach: (HTMLElement -> Result<System.IDisposable, string>) ->
        surface: RetainedSvgSurface<'State> ->
            RetainedSvgSurface<'State>

    /// The `Svg` adapter at the game-state type. Prefer `registry`, which also declares the viewport.
    val adapter: surface: RetainedSvgSurface<'State> -> RendererAdapter<'State>

    /// The registry whose default is `adapter surface`, built by `RendererRegistry.create`.
    val registry: surface: RetainedSvgSurface<'State> -> RendererRegistry<'State>
