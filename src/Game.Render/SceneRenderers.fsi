namespace FS.GG.Game.Render

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How ONE backend presents an `FS.GG.UI.Scene`: the only backend-specific half of a renderer adapter. The
/// game-specific half is a `view: 'State -> Scene`, so a game writes its view once and every backend that can
/// present a Scene (the retained SVG surface, the Pixi and Babylon canvases) becomes an adapter for its state
/// type through `SceneSurface.adapter`. Built by `SceneSurface.create`.
[<Sealed>]
type SceneSurface

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module SceneSurface =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The surface of `backend`. `mountScene` has the `RendererAdapter.Mount` contract at the Scene type: acquire the
    /// backend's resources, present the first Scene, and return `Error reason` (or throw) when the backend cannot
    /// start.
    val create:
        backend: RendererBackend ->
        mountScene: (FS.GG.UI.Scene.Scene -> Result<RendererMount<FS.GG.UI.Scene.Scene>, string>) ->
            SceneSurface

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The backend the surface realises.
    val backend: surface: SceneSurface -> RendererBackend

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Mount the surface on a first Scene.
    val mountScene:
        surface: SceneSurface -> scene: FS.GG.UI.Scene.Scene -> Result<RendererMount<FS.GG.UI.Scene.Scene>, string>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The adapter for the surface's backend at a game-state type: every state, the first one and each presented one,
    /// is projected through `view` before it reaches the surface. `RendererAdapter.svg view mountScene` is this
    /// function for an SVG surface.
    val adapter: view: ('State -> FS.GG.UI.Scene.Scene) -> surface: SceneSurface -> RendererAdapter<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The surface with an interactive mount (FS.GG.Game#751): `mountInteractive report frame` has the
    /// `RendererAdapter.Mount` contract at the `InteractiveScene` type and also attaches the surface's input host,
    /// which implements the `SceneInteraction` contract (hit test, keyboard, accessible mirror, disposal) and sends
    /// every request to `report`. Replaces any interactive mount the surface had; the plain `mountScene` is kept.
    val withInteraction:
        mountInteractive:
            ((SelectionRequest -> unit) -> InteractiveScene -> Result<RendererMount<InteractiveScene>, string>) ->
        surface: SceneSurface ->
            SceneSurface

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the surface has an interactive mount.
    val isInteractive: surface: SceneSurface -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The surface with an input-host mount (FS.GG.Game#787, `docs/adr/0001-standard-input-pipeline.md`):
    /// `mountInput report frame` has the `RendererAdapter.Mount` contract at the `InteractiveScene` type and attaches the
    /// surface's input host, which reports every `InputEvent` (source, modifiers, primary and secondary activation,
    /// repeats, hover, focus and Cancel) to `report` and draws the interaction's cue layer. Contract only:
    /// FS.GG.Game#788 implements it, and until then it raises.
    val withInputHost:
        mountInput: ((InputEvent -> unit) -> InteractiveScene -> Result<RendererMount<InteractiveScene>, string>) ->
        surface: SceneSurface ->
            SceneSurface

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether the surface has an input-host mount.
    val hasInputHost: surface: SceneSurface -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The interactive adapter for the surface's backend: every state is presented as `SceneInteraction.frame view
    /// interaction state`, and the input host reports to `route`. A surface without an interactive mount gives the
    /// plain `SceneSurface.adapter view surface`, which the interaction checks of the conformance kit fail.
    val interactiveAdapter:
        view: ('State -> FS.GG.UI.Scene.Scene) ->
        interaction: SceneInteraction<'State> ->
        route: SelectionRoute ->
        surface: SceneSurface ->
            RendererAdapter<'State>

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
    /// Declare the surface's backend through `surface`, replacing any surface already declared for that backend.
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
    /// Make the declared renderers interactive (FS.GG.Game#751): from now on `registry` gives every backend
    /// `SceneSurface.interactiveAdapter (view backend) interaction route surface`, so the selectable objects of each
    /// presented state are offered on every backend and their requests go to `route`. Replaces any interaction
    /// declared before.
    val withInteraction:
        interaction: SceneInteraction<'State> ->
        route: SelectionRoute ->
        renderers: SceneRenderers<'State> ->
            SceneRenderers<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Make the declared renderers report every input to `route` (FS.GG.Game#787): from now on `registry` mounts each
    /// backend's input host (`SceneSurface.withInputHost`), which presents `SceneInteraction.frame` of each state with
    /// the interaction's cue layer above it. A game binds `route` to its `InputPipeline` (`PipelineHost.start`). It
    /// replaces `withInteraction`, which keeps working as this route seen through `InputEvent.toSelectionRequest`.
    /// Contract only: FS.GG.Game#788 implements it, and until then it raises.
    val withInput:
        interaction: SceneInteraction<'State> ->
        route: InputRoute ->
        renderers: SceneRenderers<'State> ->
            SceneRenderers<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The registry of the declared renderers: `SceneSurface.adapter (view backend) surface` for every declared
    /// backend (`SceneSurface.interactiveAdapter` once `withInteraction` declared an interaction), so exactly the
    /// declared backends are selectable.
    val registry: renderers: SceneRenderers<'State> -> RendererRegistry<'State>
