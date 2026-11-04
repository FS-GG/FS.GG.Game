namespace FS.GG.Game.Render.Browser

open Browser.Dom
open Browser.Types
open Fable.Core
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser.SceneShapes
open FS.GG.UI.Scene

[<NoEquality; NoComparison>]
type PixiOptions =
    {
        Label: string
        Width: float
        Height: float
        Antialias: bool
        OnFailure: string -> unit
    }

[<NoEquality; NoComparison>]
type PixiCounters =
    {
        ApplicationsCreated: int
        ApplicationsDestroyed: int
        ShapesCreated: int
        ShapesDestroyed: int
        InitFailures: int
        UnsupportedNodes: int
    }

[<NoEquality; NoComparison>]
type PixiLive =
    {
        Applications: int
        TickerListeners: int
        DisplayObjects: int
    }

[<RequireQualifiedAccess>]
module PixiSurface =

    module P = Qualification.Pixi

    // ── Lifetime totals ──────────────────────────────────────────────────────────────────────
    let mutable private applicationsCreated = 0
    let mutable private applicationsDestroyed = 0
    let mutable private shapesCreated = 0
    let mutable private shapesDestroyed = 0
    let mutable private initFailures = 0
    let mutable private unsupportedNodes = 0

    /// An application the adapter created, and where its asynchronous initialisation stands. Pixi allocates a stage
    /// in the constructor; the renderer and ticker become available only during asynchronous initialisation.
    type private Entry =
        {
            App: P.Application
            Ready: bool ref
            Failed: bool ref
        }

    let private liveApplications = ResizeArray<Entry>()

    let counters () : PixiCounters =
        {
            ApplicationsCreated = applicationsCreated
            ApplicationsDestroyed = applicationsDestroyed
            ShapesCreated = shapesCreated
            ShapesDestroyed = shapesDestroyed
            InitFailures = initFailures
            UnsupportedNodes = unsupportedNodes
        }

    /// Pixi's own record of destruction: `Application.destroy` leaves `stage` null (construction creates a Container).
    /// The adapter's books say what it asked for; this says what Pixi did.
    [<Emit("$0.stage === null")>]
    let private destroyedByPixi (app: P.Application) : bool = jsNative

    let live () : PixiLive =
        liveApplications.RemoveAll(fun entry -> entry.Failed.Value || destroyedByPixi entry.App)
        |> ignore

        let ready =
            liveApplications |> Seq.filter (fun entry -> entry.Ready.Value) |> Seq.toList

        {
            Applications = liveApplications.Count
            TickerListeners =
                ready
                |> List.sumBy (fun entry -> P.tickerListenerCount (P.applicationTicker entry.App))
            DisplayObjects = ready |> List.sumBy (fun entry -> P.childCount (P.stage entry.App))
        }

    let defaultOptions (label: string) (width: float) (height: float) : PixiOptions =
        {
            Label = label
            Width = width
            Height = height
            Antialias = false
            OnFailure = ignore
        }

    /// Pixi's own hit test over a tree of interactive display objects (FS.GG.Game#751).
    [<Import("EventBoundary", "pixi.js"); EmitConstructor>]
    let private eventBoundary (_root: P.Container) : obj = jsNative

    /// The topmost interactive display object under `(x, y)`, or null.
    [<Emit("$0.hitTest($1, $2)")>]
    let private boundaryHitTest (_boundary: obj) (_x: float) (_y: float) : obj = jsNative

    /// Resize the renderer to `width` x `height` CSS pixels at `resolution` device pixels per CSS pixel.
    [<Emit("$0.renderer.resize($1, $2, $3)")>]
    let private resizeAt (_app: P.Application) (_width: float) (_height: float) (_resolution: float) : unit = jsNative

    let private rgb (color: Color) =
        (int color.Red <<< 16) ||| (int color.Green <<< 8) ||| int color.Blue

    /// One live display object. The geometry is drawn at the origin of its own coordinates, so moving a shape is a
    /// position change and only a new size or colour redraws it.
    type private Slot =
        {
            Kind: Kind
            Node: P.Graphics
            mutable Width: float
            mutable Height: float
            mutable Fill: Color
        }

    let private draw (slot: Slot) =
        P.clearGraphics slot.Node

        match slot.Kind with
        | Kind.Flat -> P.rect slot.Node 0.0 0.0 slot.Width slot.Height
        | Kind.Round ->
            P.ellipse slot.Node (slot.Width / 2.0) (slot.Height / 2.0) (slot.Width / 2.0) (slot.Height / 2.0)

        P.fill slot.Node (rgb slot.Fill) (float slot.Fill.Alpha / 255.0)

    /// The mount behind `mountScene`, `mountInteractive` and `mountInput`: with a `host`, the mount also holds an input
    /// host whose hit test is Pixi's own `EventBoundary` over one hit region per selectable object.
    let private mountWith
        (container: HTMLElement)
        (options: PixiOptions)
        (host: InputHost.CanvasHost option)
        (first: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        // FS.GG.Game#788: an input-host mount renders at the presented density.
        let sharpens =
            match host with
            | Some(InputHost.CanvasHost.Input _) -> true
            | _ -> false

        let scene = first.Scene

        if System.String.IsNullOrWhiteSpace options.Label then
            Error "Pixi surface refused the scene: the accessible label is blank"
        elif
            not (
                CanvasSupport.isFinitePositive options.Width
                && CanvasSupport.isFinitePositive options.Height
            )
        then
            Error(sprintf "Pixi surface refused the scene: invalid size %Ax%A" options.Width options.Height)
        elif not (CanvasSupport.webGlAvailable ()) then
            Error "Pixi surface refused the scene: the browser has no WebGL context"
        else
            // Everything acquired synchronously, most recent first. Mount failure and Dispose both run it.
            let releases: (unit -> unit) list ref = ref []

            let acquire (release: unit -> unit) =
                releases.Value <- release :: releases.Value

            let releaseAll () =
                let mutable first: exn option = None
                let pending = releases.Value
                releases.Value <- []

                for release in pending do
                    try
                        release ()
                    with ex ->
                        if first.IsNone then
                            first <- Some ex

                match first with
                | Some ex -> raise ex
                | None -> ()

            try
                let canvas = document.createElement "canvas" :?> HTMLCanvasElement
                canvas.width <- options.Width
                canvas.height <- options.Height
                canvas.setAttribute ("data-renderer", "pixi")
                canvas.setAttribute ("role", "img")
                canvas.setAttribute ("aria-label", options.Label)
                // FS.GG.Game#788, the surface sizing model (CanvasSizing): an input-host canvas sits in a frame the
                // host owns, out of layout at the frame's contain-fit, so its width and height attributes never size
                // anything and the scene is never drawn at two scales.
                let frame =
                    if sharpens then
                        let made = CanvasSizing.frame container "pixi" options.Width options.Height
                        made.appendChild canvas |> ignore
                        CanvasSizing.place canvas options.Width options.Height
                        Some made
                    else
                        container.appendChild canvas |> ignore
                        None

                acquire (fun () ->
                    match frame with
                    | Some frame -> frame.remove ()
                    | None -> container.removeChild canvas |> ignore)

                let ready = ref false
                let failedInit = ref false
                // Initial access/cue callbacks can fail before initialisation starts. Do not acquire an application
                // (and its stage) or retain it in the registry until those synchronous host callbacks have succeeded.
                let app =
                    lazy (
                        let made = P.application ()
                        applicationsCreated <- applicationsCreated + 1
                        liveApplications.Add { App = made; Ready = ready; Failed = failedInit }
                        made
                    )

                // FS.GG.Game#751: the hit regions live in their own tree, never on the stage, so they are not drawn and
                // the stage keeps one display object per drawable scene node. Drawn at their scene coordinates, which are
                // the canvas's pixels.
                let hitRoot = P.container ()
                let boundary = eventBoundary hitRoot
                let regions = ResizeArray<P.Graphics * string>()
                let regionsOf: SelectableObject list ref = ref []

                let reregion (objects: SelectableObject list) =
                    if objects <> regionsOf.Value then
                        for region, _ in regions do
                            P.removeChild hitRoot region
                            P.destroyNode region false

                        regions.Clear()

                        for target in objects do
                            let region = P.graphics ()
                            let bounds = target.Bounds

                            match target.Shape with
                            | SelectableShape.Box -> P.rect region bounds.X bounds.Y bounds.Width bounds.Height
                            | SelectableShape.Ellipse ->
                                P.ellipse
                                    region
                                    (bounds.X + bounds.Width / 2.0)
                                    (bounds.Y + bounds.Height / 2.0)
                                    (bounds.Width / 2.0)
                                    (bounds.Height / 2.0)

                            P.fill region 0xffffff 1.0
                            P.setEventMode region "static"
                            P.addChild hitRoot region
                            regions.Add(region, target.Id)

                        regionsOf.Value <- objects

                acquire (fun () ->
                    regions.Clear()
                    P.destroyNode hitRoot true)

                let hitTest (point: Point) : string option =
                    let hit = boundaryHitTest boundary point.X point.Y

                    regions
                    |> Seq.tryFind (fun (region, _) -> obj.ReferenceEquals(box region, hit))
                    |> Option.map snd

                let slots = ResizeArray<Slot>()
                let latest = ref scene
                let failed: string option ref = ref None
                let disposed = ref false
                let destroyed = ref false

                // Destroying an application twice, or while its initialisation is pending, is not allowed.
                let destroy () =
                    if not destroyed.Value then
                        destroyed.Value <- true

                        applicationsDestroyed <- applicationsDestroyed + 1
                        shapesDestroyed <- shapesDestroyed + slots.Count
                        slots.Clear()

                        try
                            P.destroyApplication app.Value
                        finally
                            CanvasSupport.loseContext canvas

                let reconcile (next: Scene) =
                    let shapes = ResizeArray<Shape>()
                    unsupportedNodes <- unsupportedNodes + collect (0.0, 0.0) next shapes
                    let stage = P.stage app.Value
                    P.setSortableChildren stage true

                    for index in 0 .. shapes.Count - 1 do
                        let shape = shapes.[index]

                        if index < slots.Count && slots.[index].Kind <> shape.Kind then
                            let old = slots.[index]
                            P.removeChild stage old.Node
                            P.destroyNode old.Node false
                            shapesDestroyed <- shapesDestroyed + 1
                            slots.RemoveAt index

                        if index >= slots.Count then
                            let node = P.graphics ()
                            shapesCreated <- shapesCreated + 1
                            P.setZIndex node index
                            P.addChild stage node

                            let slot =
                                {
                                    Kind = shape.Kind
                                    Node = node
                                    Width = shape.Width
                                    Height = shape.Height
                                    Fill = shape.Fill
                                }

                            slots.Insert(index, slot)
                            draw slot

                        let slot = slots.[index]

                        if
                            slot.Width <> shape.Width
                            || slot.Height <> shape.Height
                            || slot.Fill <> shape.Fill
                        then
                            slot.Width <- shape.Width
                            slot.Height <- shape.Height
                            slot.Fill <- shape.Fill
                            draw slot

                        P.setPosition slot.Node shape.X shape.Y

                    while slots.Count > shapes.Count do
                        let last = slots.[slots.Count - 1]
                        P.removeChild stage last.Node
                        P.destroyNode last.Node false
                        shapesDestroyed <- shapesDestroyed + 1
                        slots.RemoveAt(slots.Count - 1)

                    // Render now, in this task, so the presented state is what a reader of the canvas sees; the
                    // application's own ticker renders every later frame.
                    P.renderApplication app.Value

                // FS.GG.Game#788: an input-host mount renders at the presented density, its backing store derived from
                // the frame's contain-fit and the device pixel ratio (CanvasSizing), so the cue layer's 2 CSS px bands
                // are drawn sharp at any presented size and on a high-DPI screen. The fit is uniform, so one resolution
                // serves both axes.
                let tracker =
                    match frame with
                    | Some frame ->
                        Some(
                            CanvasSizing.track frame canvas options.Width options.Height (fun backingWidth backingHeight _ ->
                                if ready.Value && not destroyed.Value && not disposed.Value then
                                    // One resolution for both axes (the fit is uniform): the screen is the scene width
                                    // and the backing height at that resolution, so the backing store is exactly the
                                    // snapped fit.
                                    let resolution = float backingWidth / options.Width
                                    resizeAt app.Value options.Width (float backingHeight / resolution) resolution
                                    reconcile latest.Value
                                    true
                                else
                                    false)
                        )
                    | None -> None

                acquire (fun () -> tracker |> Option.iter (fun t -> t.Dispose()))
                let sharpen () = tracker |> Option.iter (fun t -> t.Ensure())

                // The input host: `update frame` presents a frame to it, and `drawsItself` says whether it draws the frame
                // (with its cue layer) or leaves that to `Present`.
                let input =
                    match host with
                    | None -> None
                    | Some(InputHost.CanvasHost.Selection report) ->
                        reregion first.Objects

                        let attached =
                            CanvasInput.attach
                                container
                                canvas
                                RendererBackend.Pixi
                                options.Label
                                options.Width
                                options.Height
                                hitTest
                                report
                                first

                        Some((fun frame -> attached.Update frame), (fun () -> attached.Dispose()), false)
                    | Some(InputHost.CanvasHost.Input(spec, report)) ->
                        reregion first.Objects

                        let draw (next: Scene) (cues: SceneNode list) =
                            let drawn = { next with Nodes = next.Nodes @ cues }
                            latest.Value <- drawn

                            if ready.Value then
                                sharpen ()
                                reconcile drawn

                        let attached =
                            InputHost.attach
                                canvas
                                InputHost.Ownership.Fallback
                                RendererBackend.Pixi
                                options.Label
                                options.Width
                                options.Height
                                false
                                hitTest
                                spec
                                report
                                draw
                                first

                        Some((fun frame -> attached.Update frame), (fun () -> attached.Dispose()), true)

                acquire (fun () -> input |> Option.iter (fun (_, dispose, _) -> dispose ()))

                let initialised =
                    P.initApplication
                        app.Value
                        (unbox<P.Canvas>(box canvas))
                        (int options.Width)
                        (int options.Height)
                        0x000000
                        options.Antialias
                        "webgl"

                initialised.``then`` (
                    (fun () ->
                        if disposed.Value then
                            // Disposed while initialising: the canvas is already gone, only the application is left.
                            destroy ()
                        else
                            ready.Value <- true

                            try
                                sharpen ()
                                reconcile latest.Value
                            with ex ->
                                failed.Value <- Some ex.Message
                                initFailures <- initFailures + 1
                                options.OnFailure ex.Message),
                    (fun error ->
                        failedInit.Value <- true
                        initFailures <- initFailures + 1

                        if disposed.Value then
                            try
                                destroy ()
                            with _ ->
                                ()
                        else
                            let message = sprintf "Pixi could not start: %O" error
                            failed.Value <- Some message
                            options.OnFailure message)
                )
                |> ignore

                acquire (fun () ->
                    // A pending initialisation cannot be interrupted: the continuation above destroys the
                    // application when it settles. An initialised one is destroyed now.
                    if ready.Value then
                        destroy ()
                    elif failed.Value.IsSome then
                        // Initialisation failed: there is nothing to tear down but what Pixi half-built.
                        try
                            destroy ()
                        with _ ->
                            ())

                Ok
                    {
                        Present =
                            fun frame ->
                                if not disposed.Value then
                                    match failed.Value with
                                    | Some reason -> failwith reason
                                    | None ->
                                        let next = frame.Scene

                                        let drawsItself =
                                            match input with
                                            | Some(update, _, drawsItself) ->
                                                match SceneInteraction.problems frame.Objects with
                                                | [] -> ()
                                                | problems ->
                                                    failwithf
                                                        "Pixi surface refused the objects: %s"
                                                        (String.concat "; " problems)

                                                reregion frame.Objects
                                                update frame
                                                drawsItself
                                            | None -> false

                                        if not drawsItself then
                                            latest.Value <- next

                                            if ready.Value then
                                                reconcile next
                        Dispose =
                            fun () ->
                                if not disposed.Value then
                                    disposed.Value <- true
                                    releaseAll ()
                    }
            with ex ->
                // A throwing mount hands the host nothing to dispose: release what was acquired here.
                try
                    releaseAll ()
                with _ ->
                    ()

                Error(sprintf "Pixi surface could not start: %s" ex.Message)

    let private plain (scene: Scene) : InteractiveScene =
        {
            Scene = scene
            Objects = []
            Selected = None
        }

    let mountScene
        (container: HTMLElement)
        (options: PixiOptions)
        (scene: Scene)
        : Result<RendererMount<Scene>, string> =
        mountWith container options None (plain scene)
        |> Result.map (fun mount ->
            {
                Present = fun next -> mount.Present(plain next)
                Dispose = mount.Dispose
            })

    let mountInteractive
        (container: HTMLElement)
        (options: PixiOptions)
        (report: SelectionRequest -> unit)
        (frame: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        if not (List.isEmpty (SceneInteraction.problems frame.Objects)) then
            Error(
                sprintf
                    "Pixi surface refused the objects: %s"
                    (String.concat "; " (SceneInteraction.problems frame.Objects))
            )
        else
            mountWith container options (Some(InputHost.CanvasHost.Selection report)) frame

    let mountInput
        (container: HTMLElement)
        (options: PixiOptions)
        (spec: InputHostSpec)
        (report: InputEvent -> unit)
        (frame: InteractiveScene)
        : Result<RendererMount<InteractiveScene>, string> =
        match SceneInteraction.problems frame.Objects with
        | [] -> mountWith container options (Some(InputHost.CanvasHost.Input(spec, report))) frame
        | problems -> Error(sprintf "Pixi surface refused the objects: %s" (String.concat "; " problems))

    let surface (container: HTMLElement) (options: PixiOptions) : SceneSurface =
        SceneSurface.create RendererBackend.Pixi (mountScene container options)
        |> SceneSurface.withInteraction (mountInteractive container options)
        |> SceneSurface.withInputHost (mountInput container options)

    let adapter (container: HTMLElement) (options: PixiOptions) (view: 'State -> Scene) : RendererAdapter<'State> =
        SceneSurface.adapter view (surface container options)

    let register
        (container: HTMLElement)
        (options: PixiOptions)
        (view: 'State -> Scene)
        (registry: RendererRegistry<'State>)
        =
        RendererRegistry.register (adapter container options view) registry
