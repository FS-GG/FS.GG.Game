namespace FS.GG.Game.Render

open FS.GG.UI.Scene

/// See SceneRenderers.fsi for the contract.
[<Sealed>]
type SceneSurface
    internal
    (
        backend: RendererBackend,
        mountScene: Scene -> Result<RendererMount<Scene>, string>,
        mountInteractive:
            ((SelectionRequest -> unit) -> InteractiveScene -> Result<RendererMount<InteractiveScene>, string>) option
    ) =
    member internal _.Backend = backend
    member internal _.MountScene = mountScene
    member internal _.MountInteractive = mountInteractive

[<RequireQualifiedAccess>]
module SceneSurface =

    let create (backend: RendererBackend) (mountScene: Scene -> Result<RendererMount<Scene>, string>) =
        SceneSurface(backend, mountScene, None)

    let backend (surface: SceneSurface) = surface.Backend

    let mountScene (surface: SceneSurface) (scene: Scene) = surface.MountScene scene

    let adapter (view: 'State -> Scene) (surface: SceneSurface) : RendererAdapter<'State> =
        {
            Backend = surface.Backend
            Mount =
                fun state ->
                    surface.MountScene(view state)
                    |> Result.map (fun mounted ->
                        {
                            Present = fun next -> mounted.Present(view next)
                            Dispose = mounted.Dispose
                        })
        }

    let withInteraction mountInteractive (surface: SceneSurface) =
        SceneSurface(surface.Backend, surface.MountScene, Some mountInteractive)

    let isInteractive (surface: SceneSurface) = surface.MountInteractive.IsSome

    /// Contract only (FS.GG.Game#787); FS.GG.Game#788 implements it.
    let withInputHost
        (_: (InputEvent -> unit) -> InteractiveScene -> Result<RendererMount<InteractiveScene>, string>)
        (_: SceneSurface)
        : SceneSurface =
        failwith "SceneSurface.withInputHost is a contract stub; FS.GG.Game#788 implements it."

    let hasInputHost (_: SceneSurface) = false

    let interactiveAdapter
        (view: 'State -> Scene)
        (interaction: SceneInteraction<'State>)
        (route: SelectionRoute)
        (surface: SceneSurface)
        : RendererAdapter<'State> =
        match surface.MountInteractive with
        | None -> adapter view surface
        | Some mountInteractive ->
            let frame = SceneInteraction.frame view interaction
            let report request = SelectionRoute.report request route

            {
                Backend = surface.Backend
                Mount =
                    fun state ->
                        mountInteractive report (frame state)
                        |> Result.map (fun mounted ->
                            {
                                Present = fun next -> mounted.Present(frame next)
                                Dispose = mounted.Dispose
                            })
            }

/// Opaque behind the .fsi: `create` is the only constructor and always declares the SVG surface.
[<NoEquality; NoComparison>]
type SceneRenderers<'State> =
    {
        View: 'State -> Scene
        Views: Map<RendererBackend, 'State -> Scene>
        Surfaces: Map<RendererBackend, SceneSurface>
        Interaction: (SceneInteraction<'State> * SelectionRoute) option
    }

[<RequireQualifiedAccess>]
module SceneRenderers =

    let create (view: 'State -> Scene) (svg: Scene -> Result<RendererMount<Scene>, string>) : SceneRenderers<'State> =
        {
            View = view
            Views = Map.empty
            Surfaces =
                Map.ofList
                    [
                        RendererBackend.Svg, SceneSurface.create RendererBackend.Svg svg
                    ]
            Interaction = None
        }

    let withSurface (surface: SceneSurface) (renderers: SceneRenderers<'State>) =
        { renderers with
            Surfaces = Map.add surface.Backend surface renderers.Surfaces
        }

    let withView (backend: RendererBackend) (view: 'State -> Scene) (renderers: SceneRenderers<'State>) =
        { renderers with
            Views = Map.add backend view renderers.Views
        }

    let view (backend: RendererBackend) (renderers: SceneRenderers<'State>) : 'State -> Scene =
        match Map.tryFind backend renderers.Views with
        | Some own -> own
        | None -> renderers.View

    let backends (renderers: SceneRenderers<'State>) =
        RendererBackend.all
        |> List.filter (fun backend -> Map.containsKey backend renderers.Surfaces)

    let withInteraction (interaction: SceneInteraction<'State>) (route: SelectionRoute) (renderers: SceneRenderers<'State>) =
        { renderers with
            Interaction = Some(interaction, route)
        }

    /// Contract only (FS.GG.Game#787); FS.GG.Game#788 implements it.
    let withInput (_: SceneInteraction<'State>) (_: InputRoute) (_: SceneRenderers<'State>) : SceneRenderers<'State> =
        failwith "SceneRenderers.withInput is a contract stub; FS.GG.Game#788 implements it."

    let registry (renderers: SceneRenderers<'State>) : RendererRegistry<'State> =
        let svg = renderers.Surfaces.[RendererBackend.Svg]

        let adapterOf backend =
            match renderers.Interaction with
            | None -> SceneSurface.adapter (view backend renderers) renderers.Surfaces.[backend]
            | Some(interaction, route) ->
                SceneSurface.interactiveAdapter (view backend renderers) interaction route renderers.Surfaces.[backend]

        let plain =
            RendererRegistry.svg (view RendererBackend.Svg renderers) svg.MountScene

        backends renderers
        |> List.filter ((<>) RendererBackend.Svg)
        |> List.fold (fun registry backend -> RendererRegistry.register (adapterOf backend) registry) plain
        // With an interaction, the SVG adapter is the interactive one too; the plain default only made the registry.
        |> fun registry ->
            match renderers.Interaction with
            | Some _ -> RendererRegistry.register (adapterOf RendererBackend.Svg) registry
            | None -> registry
