namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene
open FS.GG.UI.Scene.SvgBrowser

[<RequireQualifiedAccess>]
module SvgSurface =

    let defaultOptions (label: string) (width: float) (height: float) : SvgBrowserOptions =
        {
            Width = width
            Height = height
            AccessibleLabel = label
            WheelZoomFactor = 1.1
        }

    /// One semantic object wraps the whole game Scene so the retained reducer sees a stable identity;
    /// only the revision moves, which keeps the root, layer and object ids stable across presents.
    let private retained (options: SvgBrowserOptions) (revision: int) (scene: Scene) : RetainedScene =
        {
            RootId = "game-render"
            Revision = revision
            Camera = { PanX = 0.0; PanY = 0.0; Zoom = 1.0 }
            Layers =
                [
                    {
                        Id = "game"
                        Visible = true
                        Objects =
                            [
                                {
                                    Id = "scene"
                                    Selectable = false
                                    AccessibleLabel = options.AccessibleLabel
                                    Content = scene
                                }
                            ]
                    }
                ]
        }

    let mountScene
        (container: HTMLElement)
        (options: SvgBrowserOptions)
        (scene: Scene)
        : Result<RendererMount<Scene>, string> =
        match SvgBrowser.mount container options (retained options 1 scene) ignore with
        | Error error -> Error(sprintf "SVG surface refused the scene: %A" error)
        | Ok host ->
            let mutable revision = 1

            Ok
                {
                    Present =
                        fun next ->
                            revision <- revision + 1

                            let result =
                                host.Dispatch(RetainedInteractionMessage.ReplaceScene(retained options revision next))

                            match result.Error with
                            | None -> ()
                            | Some error -> failwithf "SVG surface rejected the scene: %A" error
                    Dispose = fun () -> (host :> System.IDisposable).Dispose()
                }

    let registry (container: HTMLElement) (options: SvgBrowserOptions) (view: 'State -> Scene) =
        RendererRegistry.svg view (mountScene container options)
