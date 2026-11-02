namespace FS.GG.Game.Render.Browser

open Browser.Types
open Fable.Core
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

    [<Emit("$0.querySelectorAll(':scope > [data-scene-controls]:not([data-input-host])')")>]
    let private ownControls (_container: HTMLElement) : NodeListOf<Element> = jsNative

    [<Emit("$0.querySelector(':scope > desc')")>]
    let private descriptionOf (_root: Element) : Element = jsNative

    let mountInput
        (container: HTMLElement)
        (options: SvgBrowserOptions)
        (spec: InputHostSpec)
        (report: InputEvent -> unit)
        (frame: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        match refusal frame with
        | (_ :: _) as problems -> Error(sprintf "SVG surface refused the objects: %s" (String.concat "; " problems))
        | [] ->
            // The retained root draws the frame's Scene with the cue layer above it as ONE non-selectable object; the
            // input host, not the root, owns every input (its capture listeners stop the root's own press, wheel and
            // keys, so the root never pans, zooms or moves its own focus).
            // FS.GG.Game#788, the surface sizing model (CanvasSizing): the root sits in a frame the host owns, out of
            // layout at the frame's contain-fit, as the canvases do, so one page rule on the frame sizes every backend
            // and the root's own `xMidYMid meet` never has to letterbox (the pointer maps through the same box).
            let surfaceFrame = CanvasSizing.frame container "svg" options.Width options.Height

            match SvgBrowser.mount surfaceFrame options (retained options 1 frame.Scene) ignore with
            | Error error ->
                surfaceFrame.remove ()
                Error(sprintf "SVG surface refused the scene: %A" error)
            | Ok host ->
                let mutable revision = 1
                let current = ref frame
                let root = host.Root
                root.setAttribute ("data-renderer", "svg")
                root.setAttribute ("preserveAspectRatio", "xMidYMid meet")
                CanvasSizing.place root options.Width options.Height
                let placement = CanvasSizing.follow surfaceFrame root options.Width options.Height
                placement.Ensure()

                // The root's own (empty) object-selection group is replaced by the input host's mirror, and its
                // description, which names that group, goes with it.
                let made = ownControls surfaceFrame

                for index in 0 .. made.length - 1 do
                    let element = made.[index]
                    element.parentNode.removeChild element |> ignore

                match descriptionOf root with
                | null -> ()
                | description -> root.removeChild description |> ignore

                let draw (scene: Scene) (cues: SceneNode list) =
                    placement.Ensure()
                    revision <- revision + 1

                    let result =
                        host.Dispatch(
                            RetainedInteractionMessage.ReplaceScene(
                                retained options revision { scene with Nodes = scene.Nodes @ cues }
                            )
                        )

                    match result.Error with
                    | None -> ()
                    | Some error -> failwithf "SVG surface rejected the scene: %A" error

                try
                    let input =
                        InputHost.attach
                            root
                            (InputHost.Ownership.Owns container)
                            RendererBackend.Svg
                            options.AccessibleLabel
                            options.Width
                            options.Height
                            true
                            (fun point -> SceneInteraction.hitTest point current.Value.Objects)
                            spec
                            report
                            draw
                            frame

                    let disposed = ref false

                    Ok
                        {
                            Present =
                                fun next ->
                                    if not disposed.Value then
                                        match refusal next with
                                        | [] -> ()
                                        | problems ->
                                            failwithf "SVG surface refused the objects: %s" (String.concat "; " problems)

                                        current.Value <- next
                                        input.Update next
                            Dispose =
                                fun () ->
                                    if not disposed.Value then
                                        disposed.Value <- true

                                        try
                                            input.Dispose()
                                        finally
                                            placement.Dispose()
                                            (host :> System.IDisposable).Dispose()
                                            surfaceFrame.remove ()
                        }
                with ex ->
                    placement.Dispose()
                    (host :> System.IDisposable).Dispose()
                    surfaceFrame.remove ()
                    Error(sprintf "SVG surface could not start its input host: %s" ex.Message)

    let surface (container: HTMLElement) (options: SvgBrowserOptions) : SceneSurface =
        SceneSurface.create RendererBackend.Svg (mountScene container options)
        |> SceneSurface.withInteraction (mountInteractive container options)
        |> SceneSurface.withInputHost (mountInput container options)

    let registry (container: HTMLElement) (options: SvgBrowserOptions) (view: 'State -> Scene) =
        RendererRegistry.svg view (mountScene container options)
