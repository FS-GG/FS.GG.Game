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

    /// An application the adapter created, and where its asynchronous initialisation stands: an application has no
    /// ticker or stage before it is initialised, and one whose initialisation failed never held anything to destroy.
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

    /// Pixi's own record of destruction: `Application.destroy` leaves `stage` null (before initialisation it is
    /// undefined). The adapter's books say what it asked for; this says what Pixi did.
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

    let mountScene
        (container: HTMLElement)
        (options: PixiOptions)
        (scene: Scene)
        : Result<RendererMount<Scene>, string> =
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
                container.appendChild canvas |> ignore
                acquire (fun () -> container.removeChild canvas |> ignore)

                let app = P.application ()
                applicationsCreated <- applicationsCreated + 1
                let ready = ref false
                let failedInit = ref false

                liveApplications.Add
                    {
                        App = app
                        Ready = ready
                        Failed = failedInit
                    }

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
                            P.destroyApplication app
                        finally
                            CanvasSupport.loseContext canvas

                let reconcile (next: Scene) =
                    let shapes = ResizeArray<Shape>()
                    unsupportedNodes <- unsupportedNodes + collect (0.0, 0.0) next shapes
                    let stage = P.stage app
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
                    P.renderApplication app

                let initialised =
                    P.initApplication
                        app
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
                            fun next ->
                                if not disposed.Value then
                                    match failed.Value with
                                    | Some reason -> failwith reason
                                    | None ->
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

    let adapter (container: HTMLElement) (options: PixiOptions) (view: 'State -> Scene) : RendererAdapter<'State> =
        {
            Backend = RendererBackend.Pixi
            Mount =
                fun state ->
                    mountScene container options (view state)
                    |> Result.map (fun mount ->
                        {
                            Present = fun next -> mount.Present(view next)
                            Dispose = mount.Dispose
                        })
        }

    let register
        (container: HTMLElement)
        (options: PixiOptions)
        (view: 'State -> Scene)
        (registry: RendererRegistry<'State>)
        =
        RendererRegistry.register (adapter container options view) registry
