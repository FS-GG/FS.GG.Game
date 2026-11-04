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
            ((SelectionRequest -> unit) -> InteractiveScene -> Result<RendererMount<InteractiveScene>, string>) option,
        mountInput:
            (InputHostSpec -> (InputEvent -> unit) -> InteractiveScene -> Result<RendererMount<InteractiveScene>, string>) option
    ) =
    member internal _.Backend = backend
    member internal _.MountScene = mountScene
    member internal _.MountInteractive = mountInteractive
    member internal _.MountInput = mountInput

[<RequireQualifiedAccess>]
module SceneSurface =

    let create (backend: RendererBackend) (mountScene: Scene -> Result<RendererMount<Scene>, string>) =
        SceneSurface(backend, mountScene, None, None)

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
        SceneSurface(surface.Backend, surface.MountScene, Some mountInteractive, surface.MountInput)

    let isInteractive (surface: SceneSurface) = surface.MountInteractive.IsSome

    let withInputHost mountInput (surface: SceneSurface) =
        SceneSurface(surface.Backend, surface.MountScene, surface.MountInteractive, Some mountInput)

    let hasInputHost (surface: SceneSurface) = surface.MountInput.IsSome

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

    let inputAdapter
        (view: 'State -> Scene)
        (interaction: SceneInteraction<'State>)
        (route: InputRoute)
        (surface: SceneSurface)
        : RendererAdapter<'State> =
        match surface.MountInput with
        | None -> adapter view surface
        | Some mountInput ->
            let frame = SceneInteraction.frame view interaction
            let spec = SceneInteraction.inputHost interaction
            let report event = InputRoute.report event route

            {
                Backend = surface.Backend
                Mount =
                    fun state ->
                        mountInput spec report (frame state)
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
        /// FS.GG.Game#788: the input route, which replaces `Interaction` (the two are exclusive).
        Input: (SceneInteraction<'State> * InputRoute) option
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
            Input = None
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
            Input = None
        }

    let withInput (interaction: SceneInteraction<'State>) (route: InputRoute) (renderers: SceneRenderers<'State>) =
        { renderers with
            Interaction = None
            Input = Some(interaction, route)
        }

    let registry (renderers: SceneRenderers<'State>) : RendererRegistry<'State> =
        let svg = renderers.Surfaces.[RendererBackend.Svg]

        let adapterOf backend =
            match renderers.Input, renderers.Interaction with
            | Some(interaction, route), _ ->
                SceneSurface.inputAdapter (view backend renderers) interaction route renderers.Surfaces.[backend]
            | None, None -> SceneSurface.adapter (view backend renderers) renderers.Surfaces.[backend]
            | None, Some(interaction, route) ->
                SceneSurface.interactiveAdapter (view backend renderers) interaction route renderers.Surfaces.[backend]

        let plain =
            RendererRegistry.svg (view RendererBackend.Svg renderers) svg.MountScene

        backends renderers
        |> List.filter ((<>) RendererBackend.Svg)
        |> List.fold (fun registry backend -> RendererRegistry.register (adapterOf backend) registry) plain
        // With an interaction or an input route, the SVG adapter is the interactive one too; the plain default only made the registry.
        |> fun registry ->
            if renderers.Interaction.IsSome || renderers.Input.IsSome then
                RendererRegistry.register (adapterOf RendererBackend.Svg) registry
            else
                registry
