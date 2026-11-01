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

    /// The id of the semantic object that holds the drawn Scene of an interactive mount. Not selectable; a game object
    /// may not use it.
    let private sceneObjectId = "game-render:scene"

    let private transparent: Color =
        {
            Red = 0uy
            Green = 0uy
            Blue = 0uy
            Alpha = 0uy
        }

    /// One selectable semantic object per game object: a painted but transparent hit shape over its region, so the
    /// browser's own hit test (`elementFromPoint`) finds it above the drawn Scene.
    let private interactiveScene
        (options: SvgBrowserOptions)
        (revision: int)
        (frame: InteractiveScene)
        : RetainedScene =
        let region (target: SelectableObject) : Scene =
            let bounds = target.Bounds

            {
                Nodes =
                    [
                        match target.Shape with
                        | SelectableShape.Box ->
                            SceneNode.Rectangle((bounds.X, bounds.Y, bounds.Width, bounds.Height), transparent)
                        | SelectableShape.Ellipse -> SceneNode.FilledEllipse(bounds, transparent)
                    ]
            }

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
                                    Id = sceneObjectId
                                    Selectable = false
                                    AccessibleLabel = options.AccessibleLabel
                                    Content = frame.Scene
                                }
                            ]
                    }
                    {
                        Id = "selectables"
                        Visible = true
                        Objects =
                            frame.Objects
                            |> List.map (fun target ->
                                {
                                    Id = target.Id
                                    Selectable = true
                                    AccessibleLabel = target.Label
                                    Content = region target
                                })
                    }
                ]
        }

    let private refusal (frame: InteractiveScene) : string list =
        SceneInteraction.problems frame.Objects
        @ (if frame.Objects |> List.exists (fun target -> target.Id = sceneObjectId) then
               [ sprintf "the id %s is reserved by the SVG surface" sceneObjectId ]
           else
               [])

    let mountInteractive
        (container: HTMLElement)
        (options: SvgBrowserOptions)
        (report: SelectionRequest -> unit)
        (frame: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        match refusal frame with
        | (_ :: _) as problems -> Error(sprintf "SVG surface refused the objects: %s" (String.concat "; " problems))
        | [] ->
            // The game's selection, as last presented. The retained host keeps its own selection and focus; a change
            // the player makes there (pointer, keyboard, mirror) is asked of the game, and a change the game presents
            // is applied there without asking.
            let gameSelection = ref frame.Selected
            // The retained host's selection after its last transition: only a transition that changes it is a request.
            let retainedSelection: string option ref = ref None
            let syncing = ref false

            let onTransition (result: RetainedInteractionResult) =
                let before = retainedSelection.Value
                let after = result.State.SelectedObjectId
                retainedSelection.Value <- after

                if
                    not syncing.Value
                    && result.Error.IsNone
                    && after <> before
                    && after <> gameSelection.Value
                then
                    match after with
                    | Some id -> report (SelectionRequest.Select id)
                    | None -> report SelectionRequest.Clear

            match SvgBrowser.mount container options (interactiveScene options 1 frame) onTransition with
            | Error error -> Error(sprintf "SVG surface refused the scene: %A" error)
            | Ok host ->
                let mutable revision = 1

                let mirror =
                    container.querySelectorAll "[data-scene-controls]"
                    |> fun found ->
                        if found.length > 0 then
                            Some(found.[found.length - 1])
                        else
                            None

                host.Root.setAttribute ("data-renderer", "svg")

                mirror
                |> Option.iter (fun element -> CanvasInput.tagMirror element RendererBackend.Svg)

                let sync (selected: string option) =
                    syncing.Value <- true

                    try
                        if host.State.SelectedObjectId <> selected then
                            let result =
                                match selected with
                                | Some id ->
                                    host.Dispatch(RetainedInteractionMessage.Select(host.State.Scene.Revision, id))
                                | None ->
                                    host.Dispatch(RetainedInteractionMessage.ClearSelection host.State.Scene.Revision)

                            match result.Error with
                            | None -> ()
                            | Some error -> failwithf "SVG surface rejected the selection: %A" error
                    finally
                        syncing.Value <- false

                sync frame.Selected

                Ok
                    {
                        Present =
                            fun next ->
                                match refusal next with
                                | [] -> ()
                                | problems ->
                                    failwithf "SVG surface refused the objects: %s" (String.concat "; " problems)

                                revision <- revision + 1
                                gameSelection.Value <- next.Selected
                                syncing.Value <- true

                                let result =
                                    try
                                        host.Dispatch(
                                            RetainedInteractionMessage.ReplaceScene(
                                                interactiveScene options revision next
                                            )
                                        )
                                    finally
                                        syncing.Value <- false

                                match result.Error with
                                | None -> sync next.Selected
                                | Some error -> failwithf "SVG surface rejected the scene: %A" error
                        Dispose = fun () -> (host :> System.IDisposable).Dispose()
                    }

    let surface (container: HTMLElement) (options: SvgBrowserOptions) : SceneSurface =
        SceneSurface.create RendererBackend.Svg (mountScene container options)
        |> SceneSurface.withInteraction (mountInteractive container options)

    let registry (container: HTMLElement) (options: SvgBrowserOptions) (view: 'State -> Scene) =
        RendererRegistry.svg view (mountScene container options)
