namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How ONE backend presents an `FS.GG.UI.Scene`: the only backend-specific half of a renderer adapter. The
/// game-specific half is a `view: 'State -> Scene`, so a game writes its view once and every backend that can
/// present a Scene (the retained SVG surface, the Pixi and Babylon canvases) becomes an adapter for its state
/// type through `SceneSurface.adapter`. `MountScene` has the `RendererAdapter.Mount` contract at the Scene type:
/// acquire, present the first Scene, return `Error reason` (or throw) when the backend cannot start.
[<NoEquality; NoComparison>]
type SceneSurface =
    {
        /// The backend this surface realises.
        Backend: RendererBackend
        /// Acquire the backend's resources and present the first Scene.
        MountScene: FS.GG.UI.Scene.Scene -> Result<RendererMount<FS.GG.UI.Scene.Scene>, string>
    }

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module SceneSurface =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The adapter for `surface.Backend` at a game-state type: every state, the first one and each presented one,
    /// is projected through `view` before it reaches the surface. `RendererAdapter.svg view mountScene` is this
    /// function for an SVG surface.
    val adapter: view: ('State -> FS.GG.UI.Scene.Scene) -> surface: SceneSurface -> RendererAdapter<'State>

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The authoring declaration of a game's renderers for ONE game-state type: the shared Scene description of the
/// state (`view`), an optional backend-specific representation per backend, and the surfaces that present them.
/// SVG is always declared, so the registry it produces always has its default. Built by `SceneRenderers.create`.
[<NoEquality; NoComparison>]
type SceneRenderers<'State>

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// The authoring path: declare a state type's view once, add a surface per backend, and take the registry.
///
/// ```fsharp
/// let renderers =
///     SceneRenderers.create view svgMountScene
///     |> SceneRenderers.withSurface pixiSurface
///     |> SceneRenderers.withSurface babylonSurface
/// let registry = SceneRenderers.registry renderers
/// ```
[<RequireQualifiedAccess>]
module SceneRenderers =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare the renderers of a state type: `view` is the shared Scene description every backend draws unless
    /// `withView` gives it another, and `svg` is the retained SVG surface (the `mountScene` of
    /// `RendererRegistry.svg`), so the default backend is always declared.
    val create:
        view: ('State -> FS.GG.UI.Scene.Scene) ->
        svg: (FS.GG.UI.Scene.Scene -> Result<RendererMount<FS.GG.UI.Scene.Scene>, string>) ->
            SceneRenderers<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare `surface.Backend` through `surface`, replacing any surface already declared for that backend.
    val withSurface: surface: SceneSurface -> renderers: SceneRenderers<'State> -> SceneRenderers<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Give `backend` its own representation of the state, replacing the shared view for that backend only (a
    /// Babylon scene that stacks layers differently, a Pixi view with a different level of detail). It applies
    /// whether the backend's surface is declared before or after.
    val withView:
        backend: RendererBackend ->
        view: ('State -> FS.GG.UI.Scene.Scene) ->
        renderers: SceneRenderers<'State> ->
            SceneRenderers<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The representation `backend` draws: its own view when `withView` gave one, the shared view otherwise.
    val view: backend: RendererBackend -> renderers: SceneRenderers<'State> -> ('State -> FS.GG.UI.Scene.Scene)

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The declared backends, in `RendererBackend.all` order. Always contains `Svg`.
    val backends: renderers: SceneRenderers<'State> -> RendererBackend list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The registry of the declared renderers: `SceneSurface.adapter (view backend) surface` for every declared
    /// backend, so exactly the declared backends are selectable.
    val registry: renderers: SceneRenderers<'State> -> RendererRegistry<'State>
