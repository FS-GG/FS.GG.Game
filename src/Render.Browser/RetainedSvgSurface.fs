namespace FS.GG.Game.Render.Browser

open Browser.Types
open FS.GG.Game.Render
open FS.GG.UI.Scene
open FS.GG.UI.Scene.SvgBrowser

[<NoEquality; NoComparison>]
type RetainedSvgSurface<'State> =
    {
        Container: HTMLElement
        Label: string
        Size: 'State -> float * float
        Scene: 'State -> RetainedScene
        Selected: 'State -> string option
        OnSelect: string option -> unit
        Input: HTMLElement -> Result<System.IDisposable, string>
    }

[<RequireQualifiedAccess>]
module RetainedSvgSurface =

    let create
        (container: HTMLElement)
        (label: string)
        (size: 'State -> float * float)
        (scene: 'State -> RetainedScene)
        : RetainedSvgSurface<'State> =
        {
            Container = container
            Label = label
            Size = size
            Scene = scene
            Selected = fun _ -> None
            OnSelect = ignore
            Input = fun _ -> Ok { new System.IDisposable with member _.Dispose() = () }
        }

    let withSelection selected onSelect (surface: RetainedSvgSurface<'State>) =
        { surface with
            Selected = selected
            OnSelect = onSelect
        }

    let withInput attach (surface: RetainedSvgSurface<'State>) = { surface with Input = attach }

    let private options (surface: RetainedSvgSurface<'State>) (width, height) : SvgBrowserOptions =
        {
            Width = width
            Height = height
            AccessibleLabel = surface.Label
            WheelZoomFactor = 1.1
        }

    /// The retained scene of `state` as revision `revision`, in `camera` when one is given.
    let private projected (surface: RetainedSvgSurface<'State>) (revision: int) (camera: SvgCamera option) (state: 'State) =
        let scene = surface.Scene state

        { scene with
            Revision = revision
            Camera = defaultArg camera scene.Camera
        }

    let private mount (surface: RetainedSvgSurface<'State>) (state: 'State) : Result<RendererMount<'State>, string> =
        let size = surface.Size state
        // True while the surface synchronises itself with the game: transitions made then are not the user's.
        let syncing = ref true
        let observed: string option ref = ref None

        let onTransition (result: RetainedInteractionResult) =
            if not syncing.Value && result.Error.IsNone then
                let selected = result.State.SelectedObjectId

                if selected <> observed.Value then
                    observed.Value <- selected
                    surface.OnSelect selected

        // The game's selection, applied as the surface's own: a rejected id (unknown, not selectable) changes nothing.
        let synchronise (host: SvgBrowserHost) (next: 'State) =
            let revision = host.State.Scene.Revision

            match surface.Selected next with
            | Some id -> host.Dispatch(RetainedInteractionMessage.Select(revision, id)) |> ignore
            | None -> host.Dispatch(RetainedInteractionMessage.ClearSelection revision) |> ignore

            observed.Value <- host.State.SelectedObjectId

        match SvgBrowser.mount surface.Container (options surface size) (projected surface 1 None state) onTransition with
        | Error error ->
            syncing.Value <- false
            Error(sprintf "the retained SVG host refused the scene: %A" error)
        | Ok host ->
            match
                (try
                    surface.Input surface.Container
                 with ex ->
                     Error ex.Message)
            with
            | Error reason ->
                (host :> System.IDisposable).Dispose()
                syncing.Value <- false
                Error reason
            | Ok input ->
                let revision = ref 1
                let disposed = ref false

                // `selected` is the game's code: if it throws, the mount fails, and both hosts it acquired are released
                // (input first, then the SVG) instead of leaking until the page is closed.
                let synchronised =
                    try
                        try
                            synchronise host state
                            Ok()
                        with ex ->
                            Error ex.Message
                    finally
                        syncing.Value <- false

                let mounted: RendererMount<'State> =
                    {
                        Present =
                            fun next ->
                                if size <> surface.Size next then
                                    failwithf
                                        "the retained SVG surface was mounted at %A and the next state needs %A; declare the registry's viewport (RetainedSvgSurface.registry) so the host remounts"
                                        size
                                        (surface.Size next)

                                revision.Value <- revision.Value + 1
                                syncing.Value <- true

                                try
                                    let scene = projected surface revision.Value (Some host.State.Scene.Camera) next
                                    let result = host.Dispatch(RetainedInteractionMessage.ReplaceScene scene)

                                    match result.Error with
                                    | None -> synchronise host next
                                    | Some error -> failwithf "the retained SVG surface rejected the scene: %A" error
                                finally
                                    syncing.Value <- false
                        Dispose =
                            fun () ->
                                if not disposed.Value then
                                    disposed.Value <- true

                                    try
                                        input.Dispose()
                                    finally
                                        (host :> System.IDisposable).Dispose()
                    }

                match synchronised with
                | Error reason ->
                    mounted.Dispose()
                    Error reason
                | Ok() -> Ok mounted

    let adapter (surface: RetainedSvgSurface<'State>) : RendererAdapter<'State> =
        {
            Backend = RendererBackend.Svg
            Mount = mount surface
        }

    let registry (surface: RetainedSvgSurface<'State>) : RendererRegistry<'State> =
        match RendererRegistry.create (adapter surface) with
        | Ok registry -> registry |> RendererRegistry.withViewport RendererBackend.Svg surface.Size
        | Error reason -> invalidOp reason
