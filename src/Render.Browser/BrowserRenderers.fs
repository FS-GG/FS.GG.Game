namespace FS.GG.Game.Render.Browser

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open FS.GG.Game.Render
open FS.GG.UI.Scene

[<Sealed>]
type LateFailureRoute() =
    let mutable target: (RendererBackend -> string -> unit) option = None

    member _.Bind(report: RendererBackend -> string -> unit) = target <- Some report

    member _.Report (backend: RendererBackend) (reason: string) =
        match target with
        | Some report -> report backend reason
        | None -> ()

    /// The report of the driver bound NOW. A mount captures it when it is created, so a mount of an earlier driver
    /// never reaches a driver bound later.
    member _.Capture() : RendererBackend -> string -> unit =
        match target with
        | Some report -> report
        | None -> fun _ _ -> ()

[<NoEquality; NoComparison>]
type BrowserRenderOptions =
    {
        Label: string
        Width: float
        Height: float
    }

[<RequireQualifiedAccess>]
module BrowserRenderers =

    let route () = LateFailureRoute()

    let options (label: string) (width: float) (height: float) : BrowserRenderOptions =
        {
            Label = label
            Width = width
            Height = height
        }

    let surfaces (container: HTMLElement) (options: BrowserRenderOptions) (route: LateFailureRoute) : SceneSurface list =
        let pixi = PixiSurface.defaultOptions options.Label options.Width options.Height

        // Each Pixi mount reports to the driver bound when it was mounted, not to whichever driver the route is bound
        // to when its initialisation settles.
        let pixiSurface =
            SceneSurface.create RendererBackend.Pixi (fun scene ->
                let report = route.Capture()

                PixiSurface.mountScene
                    container
                    { pixi with
                        OnFailure = report RendererBackend.Pixi
                    }
                    scene)

        [
            SvgSurface.surface container (SvgSurface.defaultOptions options.Label options.Width options.Height)
            pixiSurface
            BabylonSurface.surface container (BabylonSurface.defaultOptions options.Label options.Width options.Height)
        ]

    let declare
        (container: HTMLElement)
        (options: BrowserRenderOptions)
        (route: LateFailureRoute)
        (view: 'State -> Scene)
        : SceneRenderers<'State> =
        match surfaces container options route with
        | svg :: others ->
            others
            |> List.fold
                (fun renderers surface -> SceneRenderers.withSurface surface renderers)
                (SceneRenderers.create view (SceneSurface.mountScene svg))
        | [] -> failwith "BrowserRenderers.surfaces always returns the SVG surface first"

    let start
        (route: LateFailureRoute)
        (registry: RendererRegistry<'State>)
        (state: 'State)
        (onChange: RendererModel<'State> -> unit)
        : RendererDriver<'State> =
        let driver = RendererDriver.start registry state onChange
        route.Bind(RendererDriver.failBackend driver)
        driver

[<RequireQualifiedAccess>]
module BrowserConformance =

    // Babylon's own registry of live engines: independent of the adapter's counters.
    let private engineStore: obj =
        import "EngineStore" "@babylonjs/core/Engines/engineStore.js"

    // The same hook (and `window.__glHook` shape) the renderer smoke installs, so whichever runs first serves both.
    let installContextProbe () : unit =
        emitJsStatement
            0
            "if (!window.__glHook) { const h = { calls: 0, limit: Infinity, records: [], blocked: new Set(), failNext: {} }; window.__glHook = h; const p = HTMLCanvasElement.prototype; const o = p.getContext; p.getContext = function (type, ...rest) { h.calls++; const r = this.getAttribute('data-renderer'); if (r && (h.failNext[r] || 0) > 0 && !h.blocked.has(this)) { h.failNext[r]--; h.blocked.add(this) } if (h.calls > h.limit || h.blocked.has(this)) return null; const c = o.call(this, type, ...rest); if (c && /webgl/.test(String(type))) h.records.push(c); return c } }"

    let liveContexts (backend: RendererBackend) : int =
        emitJsExpr
            (RendererBackend.id backend)
            "(() => { const h = window.__glHook; if (!h) throw new Error('BrowserConformance.installContextProbe was not called'); return h.records.filter(c => c.canvas && c.canvas.getAttribute('data-renderer') === $0 && !c.isContextLost()).length })()"

    let private count (container: HTMLElement) (selector: string) = int (container.querySelectorAll selector).length

    let private engines () : int = emitJsExpr engineStore "$0.Instances.length"

    let resources (container: HTMLElement) (backend: RendererBackend) : int =
        match backend with
        | RendererBackend.Svg -> count container "svg"
        | RendererBackend.Pixi ->
            count container "canvas[data-renderer=pixi]"
            + liveContexts backend
            + (PixiSurface.live ()).Applications
        | RendererBackend.Babylon -> count container "canvas[data-renderer=babylon]" + liveContexts backend + engines ()

    let private nextFrame: Async<unit> =
        Async.FromContinuations(fun (continuation, _, _) -> window.requestAnimationFrame (fun _ -> continuation ()) |> ignore)

    let waitUntil (maxFrames: int) (condition: unit -> bool) : Async<bool> =
        async {
            let frames = ref 0

            while not (condition ()) && frames.Value < maxFrames do
                do! nextFrame
                frames.Value <- frames.Value + 1

            return condition ()
        }

    let host (driver: RendererDriver<'State>) : ConformanceHost<'State> =
        RendererConformance.host
            (fun () -> driver.Model)
            (fun () -> driver.Mounted)
            driver.Dispatch
            (RendererDriver.failBackend driver)
            (fun () -> (driver :> System.IDisposable).Dispose())

    // Refuse every context of the next `count` adapter canvases of `token`, from their first context request on.
    let private refuseNext (token: string) : unit =
        emitJsStatement
            token
            "(() => { const h = window.__glHook; if (!h) throw new Error('BrowserConformance.installContextProbe was not called'); h.failNext[$0] = (h.failNext[$0] || 0) + 1 })()"

    let pixiLateFailure () : LateFailureTrigger =
        let baseline = ref 0

        LateFailureTrigger.create
            (fun () ->
                baseline.Value <- (PixiSurface.counters ()).InitFailures
                refuseNext "pixi")
            (waitUntil 600 (fun () -> (PixiSurface.counters ()).InitFailures > baseline.Value))

    // Read through a 2D canvas in the task that rendered the WebGL frame, before the drawing buffer is presented
    // and cleared.
    let private pixelOf (canvas: HTMLCanvasElement) (x: int) (y: int) : int[] =
        emitJsExpr
            (canvas, x, y)
            "(() => { const cv = $0; const t = document.createElement('canvas'); t.width = cv.width; t.height = cv.height; const g2 = t.getContext('2d'); g2.drawImage(cv, 0, 0); return Array.from(g2.getImageData($1, $2, 1, 1).data) })()"

    let readPixel
        (host: ConformanceHost<'State>)
        (container: HTMLElement)
        (backend: RendererBackend)
        (x: int)
        (y: int)
        (accept: int[] -> bool)
        (maxFrames: int)
        : Async<int[]> =
        async {
            let selector = sprintf "canvas[data-renderer=%s]" (RendererBackend.id backend)

            let read () =
                match container.querySelector selector with
                | null -> [||]
                | element ->
                    host.Dispatch(RendererMsg.StateChanged (host.Model()).State)
                    pixelOf (element :?> HTMLCanvasElement) x y

            let pixel = ref (read ())
            let frames = ref 0

            while pixel.Value.Length > 0 && not (accept pixel.Value) && frames.Value < maxFrames do
                do! nextFrame
                pixel.Value <- read ()
                frames.Value <- frames.Value + 1

            return pixel.Value
        }

    let subject
        (container: HTMLElement)
        (route: LateFailureRoute)
        (registry: RendererRegistry<'State>)
        (expected: RendererBackend list)
        (states: 'State list)
        (shows: ConformanceHost<'State> -> RendererBackend -> 'State -> Async<Result<unit, string>>)
        : ConformanceSubject<'State> =
        let subject =
            RendererConformance.subject registry expected states (resources container) shows
            |> RendererConformance.withStart (fun registry state ->
                host (BrowserRenderers.start route registry state ignore))
            |> RendererConformance.withWaitUntil (waitUntil 600)

        if RendererRegistry.isAvailable RendererBackend.Pixi registry then
            subject
            |> RendererConformance.withLateFailure RendererBackend.Pixi (pixiLateFailure ())
        else
            subject
