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
        let pixiOptions () =
            let report = route.Capture()

            { pixi with
                OnFailure = report RendererBackend.Pixi
            }

        let pixiSurface =
            SceneSurface.create RendererBackend.Pixi (fun scene -> PixiSurface.mountScene container (pixiOptions ()) scene)
            |> SceneSurface.withInteraction (fun request frame ->
                PixiSurface.mountInteractive container (pixiOptions ()) request frame)

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
        | (svg :: _) as all ->
            // The SVG surface is declared again so its interactive mount is kept.
            all
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

    // FS.GG.Game#751: the same kind of page-level census as the context probe, over event listeners. Every listener
    // added and not removed is kept with its target, so a listener an input host forgets is still counted when its
    // element has left the page. A mutation observer counts writes to each backend's live status.
    let installInputProbe () : unit =
        emitJsStatement
            0
            "if (!window.__inputHook) { const h = { live: [] }; window.__inputHook = h; const p = EventTarget.prototype; const add = p.addEventListener; const remove = p.removeEventListener; const cap = o => typeof o === 'boolean' ? o : !!(o && o.capture); p.addEventListener = function (type, listener, options) { const c = cap(options); if (listener && !h.live.some(r => r.t === this && r.y === type && r.l === listener && r.c === c)) h.live.push({ t: this, y: type, l: listener, c: c }); return add.call(this, type, listener, options) }; p.removeEventListener = function (type, listener, options) { const c = cap(options); const i = h.live.findIndex(r => r.t === this && r.y === type && r.l === listener && r.c === c); if (i >= 0) h.live.splice(i, 1); return remove.call(this, type, listener, options) } }"

        emitJsStatement
            0
            "if (!window.__liveHook) { const h = { counts: {} }; window.__liveHook = h; const count = records => { for (const r of records) { let t = r.target; if (t && t.nodeType === 3) t = t.parentElement; const s = t && t.closest ? t.closest('[role=status]') : null; const g = s ? s.closest('[data-scene-controls]') : null; const b = g ? g.getAttribute('data-renderer') : null; if (b) h.counts[b] = (h.counts[b] || 0) + 1 } }; h.observer = new MutationObserver(count); h.observer.observe(document, { subtree: true, childList: true, characterData: true, attributes: true }); h.flush = () => count(h.observer.takeRecords()) }"

    let inputListeners (backend: RendererBackend) : int =
        emitJsExpr
            (RendererBackend.id backend)
            "(() => { const h = window.__inputHook; if (!h) throw new Error('BrowserConformance.installInputProbe was not called'); return h.live.filter(r => r.t && typeof r.t.getAttribute === 'function' && r.t.getAttribute('data-renderer') === $0).length })()"

    let inputHosts (backend: RendererBackend) : int =
        inputListeners backend
        + int (document.querySelectorAll (sprintf "[data-scene-controls][data-renderer=%s]" (RendererBackend.id backend))).length

    let liveWrites (backend: RendererBackend) : int =
        emitJsExpr
            (RendererBackend.id backend)
            "(() => { const h = window.__liveHook; if (!h) throw new Error('BrowserConformance.installInputProbe was not called'); h.flush(); return h.counts[$0] || 0 })()"

    /// The element a backend's input host listens on: the SVG root or the canvas, in `container`.
    let private inputElement (container: HTMLElement) (backend: RendererBackend) : HTMLElement option =
        let selector =
            match backend with
            | RendererBackend.Svg -> "svg[data-renderer=svg]"
            | other -> sprintf "canvas[data-renderer=%s]" (RendererBackend.id other)

        match container.querySelector selector with
        | null -> None
        | element -> Some(unbox<HTMLElement> element)

    /// The element's own pixel width: a canvas's `width`, an SVG root's `width` attribute.
    let private intrinsicWidth (element: HTMLElement) : float =
        emitJsExpr (element, 0) "($0.tagName.toLowerCase() === 'canvas' ? $0.width : parseFloat($0.getAttribute('width')))"

    /// Real input: the test driver's trusted pointer and keyboard (Playwright `page.mouse` and `page.keyboard`), exposed
    /// to the page as `window.__fsggInput`. Without a driver there is no real input, and the probe refuses.
    let private realInput (request: obj) : Async<unit> =
        let pending: JS.Promise<unit> =
            emitJsExpr
                request
                "(window.__fsggInput ? window.__fsggInput($0) : Promise.reject(new Error('BrowserConformance needs a real-input driver: expose window.__fsggInput (Playwright page.exposeFunction) that performs page.mouse and page.keyboard input')))"

        Async.AwaitPromise pending

    /// The viewport point of the scene point `point` on `element`, after scrolling it into view.
    let private clientPoint (element: HTMLElement) (point: Point) : float * float =
        // The SVG surface's hit test asks the browser what is under the point, so it must be on screen.
        emitJsStatement (element, 0) "$0.scrollIntoView({ block: 'center', inline: 'center' })"
        let bounds = element.getBoundingClientRect ()
        let scale = bounds.width / intrinsicWidth element
        bounds.left + point.X * scale, bounds.top + point.Y * scale

    let private mirrorOf (container: HTMLElement) (backend: RendererBackend) : Element option =
        match container.querySelector (sprintf "[data-scene-controls][data-renderer=%s]" (RendererBackend.id backend)) with
        | null -> None
        | element -> Some element

    let press (container: HTMLElement) (backend: RendererBackend) (point: Point) : Async<unit> =
        async {
            match inputElement container backend with
            | None -> ()
            | Some element ->
                let x, y = clientPoint element point
                do! realInput (createObj [ "kind" ==> "click"; "x" ==> x; "y" ==> y ])
        }

    let move (container: HTMLElement) (backend: RendererBackend) (point: Point) : Async<unit> =
        async {
            match inputElement container backend with
            | None -> ()
            | Some element ->
                let x, y = clientPoint element point
                do! realInput (createObj [ "kind" ==> "move"; "x" ==> x; "y" ==> y ])
        }

    let key (name: string) : Async<unit> =
        realInput (createObj [ "kind" ==> "key"; "key" ==> name ])

    let focused (container: HTMLElement) (backend: RendererBackend) : string =
        let active = document.activeElement

        if isNull active then
            "none"
        else
            match inputElement container backend with
            | Some element when obj.ReferenceEquals(element, active) -> "surface"
            | _ ->
                match mirrorOf container backend with
                | Some mirror when mirror.contains active && active.hasAttribute "data-scene-control-id" ->
                    "control:" + active.getAttribute "data-scene-control-id"
                | _ -> active.tagName.ToLowerInvariant()

    let interactionProbe (container: HTMLElement) : InteractionProbe =
        installInputProbe ()

        InteractionProbe.create
            (press container)
            (move container)
            (fun backend ->
                async {
                    match inputElement container backend with
                    | None -> ()
                    | Some element -> element.focus ()
                })
            key
            (focused container)
            (fun backend ->
                mirrorOf container backend
                |> Option.map (fun mirror ->
                    let buttons = mirror.querySelectorAll "[data-scene-control-id]"

                    [
                        for index in 0 .. int buttons.length - 1 do
                            let button = buttons.[index]

                            yield
                                {
                                    Id = button.getAttribute "data-scene-control-id"
                                    Label = button.textContent
                                    Pressed = button.getAttribute "aria-pressed" = "true"
                                }
                    ]))
            liveWrites
            (fun backend ->
                inputElement container backend
                |> Option.bind (fun element -> element.getAttribute "tabindex" |> Option.ofObj))
            inputHosts

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
