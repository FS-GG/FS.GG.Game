namespace FS.GG.Game.Render

open FS.GG.UI.Scene

/// See SceneRenderers.fsi for the contract.
[<Sealed>]
type SceneSurface internal (backend: RendererBackend, mountScene: Scene -> Result<RendererMount<Scene>, string>) =
    member internal _.Backend = backend
    member internal _.MountScene = mountScene

[<RequireQualifiedAccess>]
module SceneSurface =

    let create (backend: RendererBackend) (mountScene: Scene -> Result<RendererMount<Scene>, string>) =
        SceneSurface(backend, mountScene)

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

/// Opaque behind the .fsi: `create` is the only constructor and always declares the SVG surface.
[<NoEquality; NoComparison>]
type SceneRenderers<'State> =
    {
        View: 'State -> Scene
        Views: Map<RendererBackend, 'State -> Scene>
        Surfaces: Map<RendererBackend, SceneSurface>
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

    let registry (renderers: SceneRenderers<'State>) : RendererRegistry<'State> =
        let svg = renderers.Surfaces.[RendererBackend.Svg]

        backends renderers
        |> List.filter ((<>) RendererBackend.Svg)
        |> List.fold
            (fun registry backend ->
                RendererRegistry.register
                    (SceneSurface.adapter (view backend renderers) renderers.Surfaces.[backend])
                    registry)
            (RendererRegistry.svg (view RendererBackend.Svg renderers) svg.MountScene)
