module FS.GG.Game.Render.Browser.Smoke.Program

// GAME-RENDER-MULTI-01.4 (FS.GG.Game#730) browser smoke. Runs in a real browser (headless Chromium via
// scripts/test-render-browser-host.sh) and proves the binding-independent part of the renderer host:
// the SVG adapter renders through RendererSelection + RendererHost + the retained SvgBrowser surface, game
// state survives switching, an unavailable backend is refused without touching the DOM, and later adapters
// (Pixi/Babylon) can plug in through RendererRegistry.register and own/release real DOM resources.
//
// The "pixi" adapter and the refusing "babylon" adapter in run() are PLUG-IN PROBES (a stub canvas / a refusing
// mount). They are NOT PixiJS or BabylonJS adapters and prove nothing about those engines. The REAL BabylonJS
// adapter (BabylonSurface over the Babylon bindings generated from the pinned Templates source) is exercised by
// runBabylon(): a WebGL canvas, an engine, meshes and a render loop are acquired, a pixel is read back from the
// canvas, structural counters are read from Babylon's own engine store, and SVG -> Babylon -> SVG switches are
// checked for state and disposal. runPixi() does the same for the real PixiJS adapter (PixiSurface), and
// runThreeBackends() switches SVG <-> Pixi <-> Babylon on one game state.

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.UI.Scene
open FS.GG.Game.Reference

type Board = { Tick: int; TokenX: float }

let background =
    {
        Red = 10uy
        Green = 20uy
        Blue = 30uy
        Alpha = 255uy
    }

let tokenColor =
    {
        Red = 200uy
        Green = 40uy
        Blue = 90uy
        Alpha = 255uy
    }

/// The Fable-portable part of FS.GG.UI.Scene is its data (`SceneNode`); the `Scene` builder functions and
/// Game.Render's `Adapter` are .NET-only, so a Fable game projects its state onto `SceneNode` directly.
let view (board: Board) : Scene =
    {
        Nodes =
            [
                SceneNode.Rectangle((0.0, 0.0, 200.0, 100.0), background)
                SceneNode.Rectangle((board.TokenX, 40.0, 20.0, 20.0), tokenColor)
            ]
    }

let results = ResizeArray<string>()
let infos = ResizeArray<string>()
let mutable failures = 0

/// Environment facts the evidence needs (never counted as a check).
let info (name: string) (value: string) =
    infos.Add("INFO " + name + ": " + value)

let check (name: string) (ok: bool) (detail: string) =
    if ok then
        results.Add("PASS " + name)
    else
        failures <- failures + 1
        results.Add("FAIL " + name + ": " + detail)

let has (needle: string) (html: string) = html.Contains needle

let count (container: HTMLElement) (selector: string) =
    let found = container.querySelectorAll selector
    int found.length

/// Lifecycle journal of the plug-in probe adapters, so disposal is counted, not assumed.
let journal = ResizeArray<string>()

let journalCount (event: string) =
    journal |> Seq.filter ((=) event) |> Seq.length

/// A stand-in for a canvas-owning backend (Pixi/Babylon shape): mounts a <canvas>, removes it on Dispose.
let canvasProbe (container: HTMLElement) (backend: RendererBackend) : RendererAdapter<Board> =
    let name = RendererBackend.id backend

    {
        Backend = backend
        Mount =
            fun board ->
                let canvas = document.createElement "canvas"
                canvas.setAttribute ("data-probe", name)
                canvas.setAttribute ("data-tick", string board.Tick)
                container.appendChild canvas |> ignore
                journal.Add(name + ":mount")

                Ok
                    {
                        Present = fun next -> canvas.setAttribute ("data-tick", string next.Tick)
                        Dispose =
                            fun () ->
                                journal.Add(name + ":dispose")
                                container.removeChild canvas |> ignore
                    }
    }

let refusingProbe (backend: RendererBackend) (reason: string) : RendererAdapter<Board> =
    {
        Backend = backend
        Mount = fun _ -> Error reason
    }

/// A canvas-owning probe whose Mount, Present and Dispose each call back into the page (`hook`), the way an
/// adapter that dispatches from a callback would. `depth` counts adapter callbacks currently on the stack, so
/// a driver that ran nested effects would show up as a maximum depth above 1.
let reentrantProbe
    (container: HTMLElement)
    (backend: RendererBackend)
    (depth: int ref)
    (maxDepth: int ref)
    (hook: (string -> unit) ref)
    : RendererAdapter<Board> =
    let name = RendererBackend.id backend

    let around (event: string) (action: unit -> unit) =
        depth.Value <- depth.Value + 1
        maxDepth.Value <- max maxDepth.Value depth.Value

        try
            action ()
            journal.Add(name + ":" + event)
            hook.Value event
        finally
            depth.Value <- depth.Value - 1

    {
        Backend = backend
        Mount =
            fun _ ->
                let canvas = document.createElement "canvas"
                canvas.setAttribute ("data-probe", name)

                around "mount" (fun () -> container.appendChild canvas |> ignore)

                Ok
                    {
                        Present = fun _ -> around "present" ignore
                        Dispose = fun () -> around "dispose" (fun () -> container.removeChild canvas |> ignore)
                    }
    }

/// Asserts, from INSIDE the observer, that the host has realised the model it is told about (the driver's
/// "the observer never sees an unrealised model" contract). `Mounted` must be the selected backend, or nothing
/// when the selected backend's mount has just failed and its fallback is still queued; it may never be the
/// previous backend. The initial model is observed while `start` is still running, before the driver value
/// exists, so it is not asserted from inside. `strict` additionally requires the selected backend to be mounted
/// (for runs where every mount succeeds). Violations are collected and checked by the caller.
type ObserverAudit() =
    member val Violations = ResizeArray<string>()
    member val Realised = 0 with get, set
    member val Driver: RendererDriver<Board> option = None with get, set

    member this.Observe(strict: bool) : RendererModel<Board> -> unit =
        fun m ->
            match this.Driver with
            | None -> ()
            | Some d ->
                let selected = Some m.Selected

                if d.Mounted = selected then
                    this.Realised <- this.Realised + 1
                elif strict || d.Mounted.IsSome then
                    this.Violations.Add(sprintf "%A observed while %A was mounted" m.Selected d.Mounted)

let run () =
    let container = document.createElement "div"
    container.id <- "stage"
    document.body.appendChild container |> ignore

    let options = SvgSurface.defaultOptions "Smoke board" 200.0 100.0
    let svgRegistry = SvgSurface.registry container options view

    // 1. SVG-only registry: the default renders through the retained SvgBrowser surface.
    let models = ResizeArray<RendererModel<Board>>()
    let initial = { Tick = 0; TokenX = 10.0 }

    let driver = RendererDriver.start svgRegistry initial models.Add

    check
        "svg is the default backend"
        (driver.Model.Selected = RendererBackend.Svg)
        (sprintf "%A" driver.Model.Selected)

    check "svg mounted in the host" (driver.Mounted = Some RendererBackend.Svg) (sprintf "%A" driver.Mounted)
    check "exactly one svg root in the container" (count container "svg" = 1) (string (count container "svg"))
    check "svg draws the game scene (two rects)" (count container "rect" = 2) (string (count container "rect"))
    let firstHtml = container.innerHTML

    check "svg carries the background fill" (has "fill=\"rgb(10 20 30)\"" firstHtml) firstHtml

    check "only svg is offered" (RendererSelection.available driver.Model = [ RendererBackend.Svg ]) "available"

    // FS.GG.Game#768/#788: `BrowserRenderers.bindTo` lets a host without `RendererDriver` hear a late failure, as
    // `MountFailed` for the model's current attempt, and only for the selected, active backend (ADR 0001, I15).
    let lateRoute = BrowserRenderers.route ()
    let heard = ResizeArray<RendererMsg<Board>>()
    BrowserRenderers.bindTo (fun () -> driver.Model) heard.Add lateRoute
    lateRoute.Report RendererBackend.Pixi "not selected"
    lateRoute.Report RendererBackend.Svg "late"

    check
        "BrowserRenderers.bindTo routes only the selected, active backend's late failure, for its current attempt"
        (heard.Count = 1
         && (match heard[0] with
             | RendererMsg.MountFailed(RendererBackend.Svg, attempt, "late") -> attempt = driver.Model.Attempt
             | _ -> false))
        (string heard.Count)

    // 2. Presenting a newer state reconciles the same SVG root and does not leak nodes.
    let nodesBefore = count container "*"

    for tick in 1..50 do
        driver.Dispatch(
            RendererMsg.StateChanged
                {
                    Tick = tick
                    TokenX = 10.0 + float tick
                }
        )

    check "state advanced" (driver.Model.State.Tick = 50) (string driver.Model.State.Tick)
    check "still one svg root after 50 presents" (count container "svg" = 1) (string (count container "svg"))

    check
        "node count is stable across presents (no leak)"
        (count container "*" = nodesBefore)
        (sprintf "%d -> %d" nodesBefore (count container "*"))

    check "svg content follows the state" (container.innerHTML <> firstHtml) "innerHTML unchanged after 50 presents"

    // 3. An unavailable backend is refused: selection, state and DOM are untouched.
    let beforeRefusal = container.innerHTML
    driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

    check
        "unavailable Pixi is refused"
        (driver.Model.Refusal = Some(RendererRefusal.Unavailable RendererBackend.Pixi))
        (sprintf "%A" driver.Model.Refusal)

    check
        "refusal keeps the SVG selection"
        (driver.Model.Selected = RendererBackend.Svg)
        (sprintf "%A" driver.Model.Selected)

    check "refusal leaves the DOM untouched" (container.innerHTML = beforeRefusal) "DOM changed"
    check "refusal keeps svg mounted" (driver.Mounted = Some RendererBackend.Svg) (sprintf "%A" driver.Mounted)

    // 4. Plug-in probe: with a canvas-owning adapter registered, switching unmounts SVG before mounting it.
    let probeRegistry =
        svgRegistry
        |> RendererRegistry.register (canvasProbe container RendererBackend.Pixi)
        |> RendererRegistry.register (refusingProbe RendererBackend.Babylon "no WebGL context")

    (driver :> System.IDisposable).Dispose()
    check "disposing the driver removes the svg root" (count container "svg" = 0) (string (count container "svg"))

    journal.Clear()
    let driver2 = RendererDriver.start probeRegistry { Tick = 7; TokenX = 70.0 } ignore

    check
        "registered probes are offered, in declared order"
        (RendererSelection.available driver2.Model =
            [ RendererBackend.Svg; RendererBackend.Pixi; RendererBackend.Babylon ])
        (sprintf "%A" (RendererSelection.available driver2.Model))

    driver2.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

    check
        "switch to the probe backend"
        (driver2.Model.Selected = RendererBackend.Pixi
         && driver2.Mounted = Some RendererBackend.Pixi)
        (sprintf "%A %A" driver2.Model.Selected driver2.Mounted)

    check
        "svg root was released before the probe mounted"
        (count container "svg" = 0 && count container "canvas[data-probe=pixi]" = 1)
        (container.innerHTML)

    check
        "game state survived the switch"
        (driver2.Model.State = { Tick = 7; TokenX = 70.0 })
        (sprintf "%A" driver2.Model.State)

    driver2.Dispatch(RendererMsg.StateChanged { Tick = 8; TokenX = 80.0 })

    check
        "probe backend presented the new state"
        (container.querySelector("canvas").getAttribute "data-tick" = "8")
        "data-tick"

    driver2.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

    check
        "switching back remounts svg with the latest state"
        (count container "svg" = 1
         && count container "canvas" = 0
         && driver2.Model.State.Tick = 8)
        (container.innerHTML)

    check
        "probe was disposed exactly once"
        (journalCount "pixi:mount" = 1 && journalCount "pixi:dispose" = 1)
        (String.concat "," journal)

    check
        "svg shows the latest state after switching back"
        (has "x=\"80\" y=\"40\"" container.innerHTML)
        "latest x not found"

    // 5. A backend that refuses to mount falls back to SVG with the game state intact.
    driver2.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)

    check
        "refusing backend falls back to svg"
        (driver2.Model.Selected = RendererBackend.Svg
         && driver2.Mounted = Some RendererBackend.Svg
         && count container "svg" = 1)
        (sprintf "%A %A" driver2.Model.Selected driver2.Mounted)

    check
        "fallback records the cause"
        (driver2.Model.Refusal = Some(RendererRefusal.MountFailed(RendererBackend.Babylon, "no WebGL context")))
        (sprintf "%A" driver2.Model.Refusal)

    check "fallback keeps the game state" (driver2.Model.State.Tick = 8) (string driver2.Model.State.Tick)

    // 6. Disposal releases everything the host owns.
    driver2.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
    (driver2 :> System.IDisposable).Dispose()
    check "disposal empties the container" (container.children.length = 0) (container.innerHTML)

    check
        "every probe mount was disposed"
        (journalCount "pixi:mount" = journalCount "pixi:dispose")
        (String.concat "," journal)

    // 7. An SVG surface the browser contract refuses is reported as a mount failure, not thrown.
    let badOptions = SvgSurface.defaultOptions "" 200.0 100.0

    let badDriver =
        RendererDriver.start (SvgSurface.registry container badOptions view) initial ignore

    check
        "an invalid SVG surface leaves the selection Failed"
        (match badDriver.Model.Status with
         | RendererStatus.Failed [ RendererBackend.Svg, _ ] -> true
         | _ -> false)
        (sprintf "%A" badDriver.Model.Status)

    check
        "nothing is mounted after a refused default"
        (badDriver.Mounted.IsNone && container.children.length = 0)
        (container.innerHTML)

    (badDriver :> System.IDisposable).Dispose()

    // 8. Adapters that dispatch from Mount, Present and Dispose: the driver queues the messages, so effects
    //    never nest (RendererHost.Run is not reentrant) and every queued message is still applied in order.
    journal.Clear()
    let depth = ref 0
    let maxDepth = ref 0
    let hook: (string -> unit) ref = ref ignore
    let reentrantDriver: RendererDriver<Board> option ref = ref None

    let reentrantRegistry =
        svgRegistry
        |> RendererRegistry.register (reentrantProbe container RendererBackend.Pixi depth maxDepth hook)

    let reentrant =
        RendererDriver.start reentrantRegistry { Tick = 20; TokenX = 20.0 } ignore

    reentrantDriver.Value <- Some reentrant

    hook.Value <-
        fun event ->
            match reentrantDriver.Value with
            | None -> ()
            | Some d ->
                match event with
                | "mount" -> d.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi) // a no-op re-selection
                | "present" -> d.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)
                | _ -> d.Dispatch(RendererMsg.StateChanged { Tick = 22; TokenX = 22.0 })

    reentrant.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
    reentrant.Dispatch(RendererMsg.StateChanged { Tick = 21; TokenX = 21.0 })

    check
        "adapter callbacks never nest (max depth 1)"
        (maxDepth.Value = 1 && depth.Value = 0)
        (sprintf "max=%d now=%d journal=%s" maxDepth.Value depth.Value (String.concat "," journal))

    check
        "queued re-entrant messages ran in order (mount, present, dispose)"
        (List.ofSeq journal = [ "pixi:mount"; "pixi:present"; "pixi:dispose" ])
        (String.concat "," journal)

    check
        "re-entrant dispatches reach the final model"
        (reentrant.Model.Selected = RendererBackend.Svg
         && reentrant.Model.State.Tick = 22
         && reentrant.Model.Status = RendererStatus.Active)
        (sprintf "%A %A" reentrant.Model.Selected reentrant.Model.State)

    check
        "the host realised the final model (svg mounted, probe canvas gone)"
        (reentrant.Mounted = Some RendererBackend.Svg
         && count container "svg" = 1
         && count container "canvas" = 0)
        (container.innerHTML)

    check "svg shows the last re-entrant state" (has "x=\"22\" y=\"40\"" container.innerHTML) (container.innerHTML)

    (reentrant :> System.IDisposable).Dispose()
    check "re-entrant driver leaves the container empty" (container.children.length = 0) (container.innerHTML)

    check
        "re-entrant probe balanced"
        (journalCount "pixi:mount" = journalCount "pixi:dispose")
        (String.concat "," journal)

    // 9. A throwing model observer must not leave the model ahead of the host or drop the effects.
    let observerThrows = ref true
    let observedSelections = ResizeArray<RendererBackend>()
    let audit = ObserverAudit()
    let observeRealised = audit.Observe true

    let observed =
        RendererDriver.start
            (svgRegistry
             |> RendererRegistry.register (canvasProbe container RendererBackend.Pixi))
            { Tick = 30; TokenX = 30.0 }
            (fun m ->
                observedSelections.Add m.Selected
                observeRealised m

                if observerThrows.Value && m.Selected = RendererBackend.Pixi then
                    failwith "observer boom")

    audit.Driver <- Some observed
    let mutable raised = ""

    try
        observed.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
    with ex ->
        raised <- ex.Message

    check "a throwing observer's failure reaches the dispatcher" (raised = "observer boom") raised

    check
        "model and host stay in sync after a throwing observer"
        (observed.Model.Selected = RendererBackend.Pixi
         && observed.Mounted = Some RendererBackend.Pixi
         && count container "canvas[data-probe=pixi]" = 1
         && count container "svg" = 0)
        (sprintf "%A %A %s" observed.Model.Selected observed.Mounted container.innerHTML)

    observerThrows.Value <- false
    observed.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

    check
        "the driver keeps working after an observer failure"
        (observed.Mounted = Some RendererBackend.Svg
         && count container "svg" = 1
         && count container "canvas" = 0)
        (container.innerHTML)

    check
        "the observer saw every model"
        (List.ofSeq observedSelections = [ RendererBackend.Svg; RendererBackend.Pixi; RendererBackend.Svg ])
        (sprintf "%A" (List.ofSeq observedSelections))

    check
        "the observer never saw an unrealised model (Mounted asserted inside it)"
        (audit.Violations.Count = 0 && audit.Realised = 2)
        (sprintf "realised=%d violations=%s" audit.Realised (String.concat "; " audit.Violations))

    (observed :> System.IDisposable).Dispose()
    check "observer driver leaves the container empty" (container.children.length = 0) (container.innerHTML)


/// Babylon's own books, read straight from its engine store and the live scenes: independent of the adapter's
/// counters, so a leak the adapter does not know about still shows.
module Probe =
    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js"); Emit("$0.Instances.length")>]
    let engines () : int = jsNative

    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js");
      Emit("$0.Instances.reduce((n, e) => n + e.activeRenderLoops.length, 0)")>]
    let renderLoops () : int = jsNative

    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js");
      Emit("$0.Instances.reduce((n, e) => n + e.scenes.length, 0)")>]
    let scenes () : int = jsNative

    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js");
      Emit("$0.Instances.reduce((n, e) => n + e.scenes.reduce((m, s) => m + s.meshes.length, 0), 0)")>]
    let meshes () : int = jsNative

    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js");
      Emit("$0.Instances.reduce((n, e) => n + e.scenes.reduce((m, s) => m + s.materials.length, 0), 0)")>]
    let materials () : int = jsNative

    [<Emit("navigator.userAgent")>]
    let userAgent () : string = jsNative

    /// A throwaway WebGL context's version and unmasked renderer string.
    [<Emit("(() => { const c = document.createElement('canvas'); const g = c.getContext('webgl2') || c.getContext('webgl'); if (!g) return 'none'; const e = g.getExtension('WEBGL_debug_renderer_info'); const r = e ? g.getParameter(e.UNMASKED_RENDERER_WEBGL) : g.getParameter(g.RENDERER); const v = g.getParameter(g.VERSION); const l = g.getExtension('WEBGL_lose_context'); if (l) l.loseContext(); return v + ' | ' + r })()")>]
    let webGlInfo () : string = jsNative

    /// RGBA of one pixel of a WebGL canvas, read in the task that rendered it (before the drawing buffer is
    /// presented and cleared), through a 2D canvas.
    [<Emit("((cv, px, py) => { const t = document.createElement('canvas'); t.width = cv.width; t.height = cv.height; const g2 = t.getContext('2d'); g2.drawImage(cv, 0, 0); return Array.from(g2.getImageData(px, py, 1, 1).data) })($0, $1, $2)")>]
    let pixel (canvas: HTMLCanvasElement) (x: int) (y: int) : int[] = jsNative

    /// Wrap `getContext` once so the page can count the WebGL contexts it created, and refuse contexts on demand. The
    /// hook is the browser host's conformance probe (`window.__glHook`), so the smoke and the kit share one.
    let installGlHook () : unit = BrowserConformance.installContextProbe ()

    /// WebGL contexts of adapter canvases (`data-renderer`) created since the hook was installed and not lost: the
    /// browser's own account of the GPU contexts the adapters hold, whether or not their canvases are still in the page.
    [<Emit("window.__glHook.records.filter(c => c.canvas && c.canvas.getAttribute('data-renderer') && !c.isContextLost()).length")>]
    let liveContexts () : int = jsNative

    /// Let `n` more `getContext` calls succeed, then refuse every canvas (no WebGL, no 2D) until the returned
    /// function lifts the refusal. `n = 0` refuses all of them.
    [<Emit("(() => { const h = window.__glHook; h.limit = h.calls + $0; return () => { h.limit = Infinity } })()")>]
    let refuseContextsAfter (n: int) : unit -> unit = jsNative

    /// Refuse `getContext` on this one canvas element (every other canvas, including the throwaway one an adapter
    /// asks first, is unaffected) until the returned function lifts the refusal.
    [<Emit("(() => { const h = window.__glHook; h.blocked.add($0); return () => { h.blocked.delete($0) } })()")>]
    let refuseCanvas (canvas: HTMLCanvasElement) : unit -> unit = jsNative

    /// Make the one live Babylon engine's `stopRenderLoop` throw ONCE: the FIRST step of the adapter's release. It
    /// throws only on that first call because `Engine.dispose` calls `stopRenderLoop` itself, and a fault that also
    /// broke the engine's own disposal would test Babylon, not the adapter's release sequence.
    [<Import("EngineStore", "@babylonjs/core/Engines/engineStore.js");
      Emit("(() => { const engine = $0.Instances[0]; const original = engine.stopRenderLoop; let thrown = false; engine.stopRenderLoop = function (...args) { if (!thrown) { thrown = true; throw new Error('release boom') } return original.apply(this, args) } })()")>]
    let breakBabylonStopRenderLoop () : unit = jsNative

    /// Make `Application.destroy` throw (after remembering the application): the step of the Pixi release that
    /// destroys the application. The returned function restores it and destroys what was remembered, so the throw
    /// leaves nothing behind in the page.
    [<Import("Application", "pixi.js");
      Emit("(() => { const original = $0.prototype.destroy; const kept = []; $0.prototype.destroy = function () { kept.push(this); throw new Error('release boom') }; return () => { $0.prototype.destroy = original; for (const app of kept) original.call(app, { removeView: false }, { children: true }) } })()")>]
    let breakPixiDestroy () : unit -> unit = jsNative

let near (expected: int[]) (actual: int[]) (tolerance: int) =
    actual.Length >= 3
    && Array.forall2 (fun e a -> abs (e - a) <= tolerance) expected.[0..2] actual.[0..2]

/// FS.GG.Game#786: every backend shows a fill at its declared colour, within one tolerance.
let pixelTolerance = BrowserConformance.pixelTolerance

/// The largest per-channel difference read on each backend between a decided pixel's declared colour and what the
/// backend drew, over every read whose pixels all matched: the measurement behind `pixelTolerance`.
let deviation = System.Collections.Generic.Dictionary<string, int>()

let noteDeviation (backend: RendererBackend) (want: int[]) (got: int * int * int) =
    let r, g, b = got
    let d = max (abs (want.[0] - r)) (max (abs (want.[1] - g)) (abs (want.[2] - b)))
    let id = RendererBackend.id backend

    match deviation.TryGetValue id with
    | true, current when current >= d -> ()
    | _ -> deviation.[id] <- d

let nextFrame: Async<unit> =
    Async.FromContinuations(fun (continuation, _, _) ->
        window.requestAnimationFrame (fun _ -> continuation ()) |> ignore)

/// Present the unchanged state frame by frame until the pixel at (x, y) satisfies `accept`, at most `maxFrames`
/// frames; returns the last pixel and the frames it took.
let awaitPixel
    (driver: RendererDriver<Board>)
    (canvas: unit -> HTMLCanvasElement)
    (x: int)
    (y: int)
    (accept: int[] -> bool)
    (maxFrames: int)
    : Async<int[] * int> =
    async {
        let pixel = ref (Probe.pixel (canvas ()) x y)
        let frames = ref 0

        while not (accept pixel.Value) && frames.Value < maxFrames do
            do! nextFrame
            driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
            pixel.Value <- Probe.pixel (canvas ()) x y
            frames.Value <- frames.Value + 1

        return pixel.Value, frames.Value
    }

/// The same game state, drawn by the Babylon adapter: `view` is the very function the SVG adapter uses.
let runBabylon () : Async<unit> =
    async {
        let container = document.createElement "div"
        container.id <- "stage-babylon"
        document.body.appendChild container |> ignore

        Probe.installGlHook ()
        let glInfo = Probe.webGlInfo ()
        info "webgl" glInfo
        info "user-agent" (Probe.userAgent ())
        check "this browser offers a WebGL context" (glInfo <> "none") glInfo

        let svgOptions = SvgSurface.defaultOptions "Smoke board" 200.0 100.0

        let babylonOptions =
            BabylonSurface.defaultOptions "Smoke board (babylon)" 200.0 100.0

        let before = BabylonSurface.counters ()

        let registry =
            SvgSurface.registry container svgOptions view
            |> BabylonSurface.register container babylonOptions view

        let initial = { Tick = 0; TokenX = 10.0 }
        let babylonAudit = ObserverAudit()
        let driver = RendererDriver.start registry initial (babylonAudit.Observe false)
        babylonAudit.Driver <- Some driver

        let babylonCanvas () =
            container.querySelector "canvas[data-renderer=babylon]" :?> HTMLCanvasElement

        check
            "babylon is offered beside svg once registered"
            (RendererSelection.available driver.Model = [ RendererBackend.Svg; RendererBackend.Babylon ])
            (sprintf "%A" (RendererSelection.available driver.Model))

        check
            "svg stays the default with babylon registered"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Mounted = Some RendererBackend.Svg
             && Probe.engines () = 0)
            (sprintf "%A %A engines=%d" driver.Model.Selected driver.Mounted (Probe.engines ()))

        // 1. SVG -> Babylon. The game state is advanced on SVG first so the switch has something to preserve.
        driver.Dispatch(RendererMsg.StateChanged { Tick = 5; TokenX = 70.0 })
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)

        check
            "switch to babylon mounts it and selects it"
            (driver.Model.Selected = RendererBackend.Babylon
             && driver.Mounted = Some RendererBackend.Babylon
             && driver.Model.Status = RendererStatus.Active)
            (sprintf "%A %A %A" driver.Model.Selected driver.Mounted driver.Model.Status)

        check
            "game state survived svg -> babylon"
            (driver.Model.State = { Tick = 5; TokenX = 70.0 })
            (sprintf "%A" driver.Model.State)

        check
            "the svg root was released before babylon mounted (one canvas, no svg)"
            (count container "svg" = 0
             && count container "canvas" = 1
             && count container "canvas[data-renderer=babylon]" = 1)
            container.innerHTML

        // 2. Structural counters from Babylon's own books.
        check "exactly one babylon engine is alive" (Probe.engines () = 1) (string (Probe.engines ()))

        check "exactly one render loop is running" (Probe.renderLoops () = 1) (string (Probe.renderLoops ()))
        check "exactly one scene" (Probe.scenes () = 1) (string (Probe.scenes ()))
        check "one mesh per scene node (background and token)" (Probe.meshes () = 2) (string (Probe.meshes ()))
        check "one material per mesh" (Probe.materials () = 2) (string (Probe.materials ()))

        // 3. Pixels: the token and the background are drawn where the scene puts them.
        let canvas = babylonCanvas ()

        check
            "the babylon canvas has the requested pixel size"
            (not (isNull canvas) && canvas.width = 200.0 && canvas.height = 100.0)
            (if isNull canvas then
                 "no canvas"
             else
                 sprintf "%gx%g" canvas.width canvas.height)

        // Babylon compiles shaders asynchronously, so the first frames can draw nothing: present the unchanged state
        // frame by frame (each present renders synchronously, in the task that reads the pixel) until it is drawn.
        let! tokenPixel, framesToFirstPixel =
            awaitPixel driver babylonCanvas 80 50 (fun p -> p.[0] > 40) 600

        let backgroundPixel = Probe.pixel (babylonCanvas ()) 20 20
        info "frames until the first drawn pixel" (string framesToFirstPixel)

        check
            "the first frame was drawn within 600 frames"
            (tokenPixel.[0] > 40)
            (sprintf "%A after %d frames" tokenPixel framesToFirstPixel)

        info "pixel token (80,50)" (sprintf "%A" tokenPixel)
        info "pixel background (20,20)" (sprintf "%A" backgroundPixel)

        // FS.GG.Game#786: the materials are unlit, so a face shows its declared colour.
        check
            "the token pixel is the token colour, unlit (200,40,90)"
            (near [| 200; 40; 90 |] tokenPixel pixelTolerance)
            (sprintf "%A" tokenPixel)

        check
            "the background pixel is the background colour, unlit (10,20,30)"
            (near [| 10; 20; 30 |] backgroundPixel pixelTolerance)
            (sprintf "%A" backgroundPixel)

        // 4. Presenting 100 newer states moves the token in place: nothing is created, nothing leaks.
        let created = (BabylonSurface.counters ()).MeshesCreated
        let materialsBefore = Probe.materials ()

        for tick in 1..100 do
            driver.Dispatch(
                RendererMsg.StateChanged
                    {
                        Tick = 5 + tick
                        TokenX = 70.0 + float (tick % 50)
                    }
            )

        // 105 and 70 + (100 % 50) = 70: put the token somewhere else so the pixel proves the last state was drawn.
        driver.Dispatch(RendererMsg.StateChanged { Tick = 200; TokenX = 150.0 })

        check
            "state advanced on babylon"
            (driver.Model.State = { Tick = 200; TokenX = 150.0 })
            (sprintf "%A" driver.Model.State)

        check "still one engine after 101 presents" (Probe.engines () = 1) (string (Probe.engines ()))
        check "still one render loop after 101 presents" (Probe.renderLoops () = 1) (string (Probe.renderLoops ()))
        check "still one canvas after 101 presents" (count container "canvas" = 1) (string (count container "canvas"))
        check "mesh count is stable across presents" (Probe.meshes () = 2) (string (Probe.meshes ()))

        check
            "material count is stable across presents"
            (Probe.materials () = materialsBefore)
            (sprintf "%d -> %d" materialsBefore (Probe.materials ()))

        check
            "no mesh was created by a present"
            ((BabylonSurface.counters ()).MeshesCreated = created)
            (sprintf "%d -> %d" created (BabylonSurface.counters ()).MeshesCreated)

        let! moved, _ = awaitPixel driver babylonCanvas 160 50 (fun p -> p.[0] > 40) 600
        let vacated = Probe.pixel (babylonCanvas ()) 80 50
        info "pixel moved token (160,50)" (sprintf "%A" moved)

        check "the token pixel followed the state" (near [| 200; 40; 90 |] moved pixelTolerance) (sprintf "%A" moved)
        check "the vacated pixel is background again" (near [| 10; 20; 30 |] vacated pixelTolerance) (sprintf "%A" vacated)

        // 5. Babylon -> SVG: everything is released, the latest state is shown.
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        check
            "switch back to svg"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Mounted = Some RendererBackend.Svg
             && driver.Model.State = { Tick = 200; TokenX = 150.0 })
            (sprintf "%A %A %A" driver.Model.Selected driver.Mounted driver.Model.State)

        check
            "svg shows the state babylon was showing"
            (count container "svg" = 1 && has "x=\"150\" y=\"40\"" container.innerHTML)
            container.innerHTML

        check "no babylon canvas remains" (count container "canvas" = 0) container.innerHTML
        check "no babylon engine remains" (Probe.engines () = 0) (string (Probe.engines ()))
        check "no render loop remains" (Probe.renderLoops () = 0) (string (Probe.renderLoops ()))
        check "no scene remains" (Probe.scenes () = 0) (string (Probe.scenes ()))
        check "no mesh remains" (Probe.meshes () = 0) (string (Probe.meshes ()))

        check
            "babylon released its WebGL context, not just its engine"
            (Probe.liveContexts () = 0)
            (string (Probe.liveContexts ()))

        // 6. Repeated switching never holds two engines and ends released.
        let maxEngines = ref 0
        let maxCanvases = ref 0

        for round in 1..5 do
            driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)
            maxEngines.Value <- max maxEngines.Value (Probe.engines ())
            maxCanvases.Value <- max maxCanvases.Value (count container "canvas")

            driver.Dispatch(
                RendererMsg.StateChanged
                    {
                        Tick = 200 + round
                        TokenX = 20.0 * float round
                    }
            )

            driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        check
            "five svg -> babylon -> svg rounds never held two engines"
            (maxEngines.Value = 1)
            (string maxEngines.Value)

        check "five rounds never held two canvases" (maxCanvases.Value = 1) (string maxCanvases.Value)

        check
            "five rounds ended on svg with the latest state and nothing left over"
            (driver.Mounted = Some RendererBackend.Svg
             && driver.Model.State = { Tick = 205; TokenX = 100.0 }
             && count container "canvas" = 0
             && Probe.engines () = 0
             && Probe.renderLoops () = 0
             && Probe.meshes () = 0)
            (sprintf "%A engines=%d loops=%d" driver.Model.State (Probe.engines ()) (Probe.renderLoops ()))

        // 7. A node set that grows and shrinks keeps one mesh per node and reuses materials.
        let swarm (board: Board) : Scene =
            {
                Nodes =
                    SceneNode.Rectangle((0.0, 0.0, 200.0, 100.0), background)
                    :: [
                        for i in 0 .. (board.Tick % 4) ->
                            SceneNode.Rectangle((10.0 + 40.0 * float i, 40.0, 20.0, 20.0), tokenColor)
                    ]
            }

        let swarmRegistry =
            SvgSurface.registry container svgOptions swarm
            |> BabylonSurface.register container babylonOptions swarm

        let swarmDriver =
            RendererDriver.start swarmRegistry { Tick = 3; TokenX = 0.0 } ignore

        swarmDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)

        let nodeCount () =
            1 + (swarmDriver.Model.State.Tick % 4) + 1

        check
            "babylon draws one mesh per node of a larger scene"
            (Probe.meshes () = nodeCount ())
            (sprintf "%d vs %d" (Probe.meshes ()) (nodeCount ()))

        let meshesTrackNodes = ref true
        let peakMaterials = ref 0

        for tick in 0..40 do
            swarmDriver.Dispatch(RendererMsg.StateChanged { Tick = tick; TokenX = 0.0 })
            peakMaterials.Value <- max peakMaterials.Value (Probe.materials ())

            if Probe.meshes () <> nodeCount () then
                meshesTrackNodes.Value <- false

        check "mesh count tracked the node count through 41 grow/shrink presents" meshesTrackNodes.Value "diverged"

        check
            "materials never exceed the most nodes ever drawn (5)"
            (peakMaterials.Value <= 5)
            (string peakMaterials.Value)

        check
            "still one engine and one loop after grow/shrink"
            (Probe.engines () = 1 && Probe.renderLoops () = 1)
            (sprintf "%d %d" (Probe.engines ()) (Probe.renderLoops ()))

        (swarmDriver :> System.IDisposable).Dispose()

        check
            "disposing the driver on babylon releases the canvas, engine, loop and meshes"
            (count container "canvas" = 0
             && Probe.engines () = 0
             && Probe.renderLoops () = 0
             && Probe.scenes () = 0
             && Probe.meshes () = 0)
            (sprintf "canvas=%d engines=%d" (count container "canvas") (Probe.engines ()))

        // 8. No WebGL: the mount is refused, SVG takes over with the state intact, nothing is left behind.
        let restore = Probe.refuseContextsAfter 0
        driver.Dispatch(RendererMsg.StateChanged { Tick = 300; TokenX = 30.0 })
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)
        restore ()

        check
            "a browser with no WebGL falls back to svg"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Mounted = Some RendererBackend.Svg
             && driver.Model.Status = RendererStatus.Active)
            (sprintf "%A %A %A" driver.Model.Selected driver.Mounted driver.Model.Status)

        check
            "the fallback names babylon as the cause"
            (match driver.Model.Refusal with
             | Some(RendererRefusal.MountFailed(RendererBackend.Babylon, _)) -> true
             | _ -> false)
            (sprintf "%A" driver.Model.Refusal)

        check
            "the fallback kept the game state"
            (driver.Model.State = { Tick = 300; TokenX = 30.0 })
            (sprintf "%A" driver.Model.State)

        check
            "the refused mount left no canvas, engine or loop"
            (count container "canvas" = 0
             && Probe.engines () = 0
             && Probe.renderLoops () = 0
             && count container "svg" = 1)
            (sprintf "%s engines=%d" container.innerHTML (Probe.engines ()))

        // 9. Options the adapter refuses are reported as a refused mount too.
        let invalid =
            RendererRegistry.register
                (BabylonSurface.adapter container (BabylonSurface.defaultOptions "bad" 0.0 100.0) view)
                (SvgSurface.registry container svgOptions view)

        let invalidDriver = RendererDriver.start invalid initial ignore
        invalidDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)

        check
            "a zero-width babylon canvas is refused and falls back to svg"
            (invalidDriver.Model.Selected = RendererBackend.Svg
             && invalidDriver.Mounted = Some RendererBackend.Svg
             && count container "canvas" = 0)
            (sprintf "%A %A" invalidDriver.Model.Selected invalidDriver.Mounted)

        (invalidDriver :> System.IDisposable).Dispose()

        // 10. A Present that throws is contained: the babylon mount is released and SVG shows the same state.
        let fragile (board: Board) : Scene =
            if board.Tick = 99 then failwith "view boom" else view board

        let fragileRegistry =
            SvgSurface.registry container svgOptions view
            |> BabylonSurface.register container babylonOptions fragile

        let fragileDriver =
            RendererDriver.start fragileRegistry { Tick = 1; TokenX = 10.0 } ignore

        fragileDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)

        check
            "the fragile view mounts on babylon first"
            (fragileDriver.Mounted = Some RendererBackend.Babylon && Probe.engines () = 1)
            (sprintf "%A %d" fragileDriver.Mounted (Probe.engines ()))

        fragileDriver.Dispatch(RendererMsg.StateChanged { Tick = 99; TokenX = 99.0 })

        check
            "a throwing present falls back to svg with the state that threw"
            (fragileDriver.Model.Selected = RendererBackend.Svg
             && fragileDriver.Mounted = Some RendererBackend.Svg
             && fragileDriver.Model.State = { Tick = 99; TokenX = 99.0 })
            (sprintf "%A %A %A" fragileDriver.Model.Selected fragileDriver.Mounted fragileDriver.Model.State)

        check
            "the throwing present released the canvas, engine, loop and meshes"
            (count container "canvas" = 0
             && Probe.engines () = 0
             && Probe.renderLoops () = 0
             && Probe.meshes () = 0)
            (sprintf "canvas=%d engines=%d" (count container "canvas") (Probe.engines ()))

        (fragileDriver :> System.IDisposable).Dispose()

        // 11. The adapter's own books balance with Babylon's.
        check
            "the observer never saw a model whose backend was not the one mounted (Mounted asserted inside it)"
            (babylonAudit.Violations.Count = 0 && babylonAudit.Realised >= 12)
            (sprintf "realised=%d violations=%s" babylonAudit.Realised (String.concat "; " babylonAudit.Violations))

        (driver :> System.IDisposable).Dispose()
        let after = BabylonSurface.counters ()
        let created = after.EnginesCreated - before.EnginesCreated

        let counterLine =
            sprintf
                "engines %d/%d loops %d/%d meshes %d/%d unsupported %d (created/disposed)"
                after.EnginesCreated
                after.EnginesDisposed
                after.LoopsStarted
                after.LoopsStopped
                after.MeshesCreated
                after.MeshesDisposed
                after.UnsupportedNodes

        info "adapter counters" counterLine

        check
            "every engine the adapter created was disposed"
            (created > 0 && created = after.EnginesDisposed - before.EnginesDisposed)
            (sprintf "%A" after)

        check
            "every render loop the adapter started was stopped"
            (after.LoopsStarted - before.LoopsStarted = created
             && after.LoopsStopped - before.LoopsStopped = created)
            (sprintf "%A" after)

        check
            "every mesh the adapter created was disposed"
            (after.MeshesCreated - before.MeshesCreated = after.MeshesDisposed - before.MeshesDisposed)
            (sprintf "%A" after)

        check "nothing is left in the container" (container.children.length = 0) container.innerHTML

        check
            "no engine or adapter WebGL context survived the whole babylon section"
            (Probe.engines () = 0 && Probe.scenes () = 0 && Probe.liveContexts () = 0)
            (sprintf "%d %d contexts=%d" (Probe.engines ()) (Probe.scenes ()) (Probe.liveContexts ()))

        // 12. A release step that throws must not stop the ones after it. The FIRST step of the Babylon release
        //     (stopping the render loop) is made to throw: the meshes, scene, engine, WebGL context and canvas must
        //     still be released, and the host must report the failure without losing the selection.
        let releaseDriver =
            RendererDriver.start registry { Tick = 400; TokenX = 40.0 } ignore

        releaseDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)

        check
            "the release probe starts with babylon mounted"
            (releaseDriver.Mounted = Some RendererBackend.Babylon && Probe.engines () = 1)
            (sprintf "%A engines=%d" releaseDriver.Mounted (Probe.engines ()))

        Probe.breakBabylonStopRenderLoop ()
        releaseDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        check
            "a throwing first release step is reported, and the selection moves on to svg"
            (releaseDriver.Model.Selected = RendererBackend.Svg
             && releaseDriver.Mounted = Some RendererBackend.Svg
             && (match releaseDriver.Model.Refusal with
                 | Some(RendererRefusal.DisposeFailed(RendererBackend.Babylon, _)) -> true
                 | _ -> false))
            (sprintf "%A %A %A" releaseDriver.Model.Selected releaseDriver.Mounted releaseDriver.Model.Refusal)

        check
            "every release step after the throwing one still ran (meshes, scene, engine, context, canvas)"
            (Probe.meshes () = 0
             && Probe.scenes () = 0
             && Probe.engines () = 0
             && Probe.liveContexts () = 0
             && count container "canvas" = 0)
            (sprintf
                "meshes=%d scenes=%d engines=%d contexts=%d canvas=%d"
                (Probe.meshes ())
                (Probe.scenes ())
                (Probe.engines ())
                (Probe.liveContexts ())
                (count container "canvas"))

        (releaseDriver :> System.IDisposable).Dispose()
    }

let settle (frames: int) : Async<unit> =
    async {
        for _ in 1..frames do
            do! nextFrame
    }

/// Wait, frame by frame, until `condition` holds (at most `maxFrames`); returns the frames it took.
let waitUntil (condition: unit -> bool) (maxFrames: int) : Async<int> =
    async {
        let frames = ref 0

        while not (condition ()) && frames.Value < maxFrames do
            do! nextFrame
            frames.Value <- frames.Value + 1

        return frames.Value
    }

/// The Pixi adapter on the same game state: `view` is the function the SVG and Babylon adapters use, and Pixi
/// draws it unlit, so its pixels are the scene colours to within 2 per channel.
let runPixi () : Async<unit> =
    async {
        let container = document.createElement "div"
        container.id <- "stage-pixi"
        document.body.appendChild container |> ignore

        let svgOptions = SvgSurface.defaultOptions "Smoke board" 200.0 100.0
        let driverRef: RendererDriver<Board> option ref = ref None

        let pixiOptions =
            { PixiSurface.defaultOptions "Smoke board (pixi)" 200.0 100.0 with
                OnFailure =
                    fun reason ->
                        match driverRef.Value with
                        | Some d -> RendererDriver.failBackend d RendererBackend.Pixi reason
                        | None -> ()
            }

        let before = PixiSurface.counters ()
        let contextsBefore = Probe.liveContexts ()

        let registry =
            SvgSurface.registry container svgOptions view
            |> PixiSurface.register container pixiOptions view

        let initial = { Tick = 0; TokenX = 10.0 }
        let pixiAudit = ObserverAudit()
        let driver = RendererDriver.start registry initial (pixiAudit.Observe false)
        pixiAudit.Driver <- Some driver
        driverRef.Value <- Some driver

        let pixiCanvas () =
            container.querySelector "canvas[data-renderer=pixi]" :?> HTMLCanvasElement

        let live () = PixiSurface.live ()

        check
            "pixi is offered beside svg once registered"
            (RendererSelection.available driver.Model = [ RendererBackend.Svg; RendererBackend.Pixi ])
            (sprintf "%A" (RendererSelection.available driver.Model))

        check
            "svg stays the default with pixi registered"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Mounted = Some RendererBackend.Svg
             && (live ()).Applications = 0)
            (sprintf "%A %A %A" driver.Model.Selected driver.Mounted (live ()))

        // 1. SVG -> Pixi.
        driver.Dispatch(RendererMsg.StateChanged { Tick = 5; TokenX = 70.0 })
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

        check
            "switch to pixi mounts it and selects it"
            (driver.Model.Selected = RendererBackend.Pixi
             && driver.Mounted = Some RendererBackend.Pixi
             && driver.Model.Status = RendererStatus.Active)
            (sprintf "%A %A %A" driver.Model.Selected driver.Mounted driver.Model.Status)

        check
            "game state survived svg -> pixi"
            (driver.Model.State = { Tick = 5; TokenX = 70.0 })
            (sprintf "%A" driver.Model.State)

        check
            "the svg root was released before pixi mounted (one canvas, no svg)"
            (count container "svg" = 0
             && count container "canvas" = 1
             && count container "canvas[data-renderer=pixi]" = 1)
            container.innerHTML

        check
            "one pixi application exists as soon as the mount returns (initialisation pending)"
            ((live ()).Applications = 1)
            (sprintf "%A" (live ()))

        // 2. Pixi initialises asynchronously: wait for the first drawn frame, then read pixels back.
        let! tokenPixel, framesToFirstPixel =
            awaitPixel driver pixiCanvas 80 50 (fun p -> p.[0] > 40) 600

        info "pixi frames until the first drawn pixel" (string framesToFirstPixel)
        let backgroundPixel = Probe.pixel (pixiCanvas ()) 20 20
        info "pixel pixi token (80,50)" (sprintf "%A" tokenPixel)
        info "pixel pixi background (20,20)" (sprintf "%A" backgroundPixel)

        check
            "pixi drew its first frame within 600 frames"
            (tokenPixel.[0] > 40)
            (sprintf "%A after %d frames" tokenPixel framesToFirstPixel)

        check
            "the token pixel is the token colour within 2 per channel (200,40,90)"
            (near [| 200; 40; 90 |] tokenPixel 2)
            (sprintf "%A" tokenPixel)

        check
            "the background pixel is the background colour within 2 per channel (10,20,30)"
            (near [| 10; 20; 30 |] backgroundPixel 2)
            (sprintf "%A" backgroundPixel)

        check
            "pixi's own books: one application, one render callback on its ticker, one display object per node"
            ((live ()).Applications = 1
             && (live ()).TickerListeners = 1
             && (live ()).DisplayObjects = 2)
            (sprintf "%A" (live ()))

        check
            "pixi holds exactly one live adapter WebGL context"
            (Probe.liveContexts () = contextsBefore + 1)
            (sprintf "%d -> %d" contextsBefore (Probe.liveContexts ()))

        // 3. 101 presents move the token in place: nothing is created, nothing leaks.
        let created = (PixiSurface.counters ()).ShapesCreated

        for tick in 1..100 do
            driver.Dispatch(
                RendererMsg.StateChanged
                    {
                        Tick = 5 + tick
                        TokenX = 70.0 + float (tick % 50)
                    }
            )

        driver.Dispatch(RendererMsg.StateChanged { Tick = 200; TokenX = 150.0 })

        check
            "state advanced on pixi"
            (driver.Model.State = { Tick = 200; TokenX = 150.0 })
            (sprintf "%A" driver.Model.State)

        check "still one pixi application after 101 presents" ((live ()).Applications = 1) (sprintf "%A" (live ()))
        check "still one render callback after 101 presents" ((live ()).TickerListeners = 1) (sprintf "%A" (live ()))
        check "display object count is stable across presents" ((live ()).DisplayObjects = 2) (sprintf "%A" (live ()))
        check "still one canvas after 101 presents" (count container "canvas" = 1) (string (count container "canvas"))

        check
            "no display object was created by a present"
            ((PixiSurface.counters ()).ShapesCreated = created)
            (sprintf "%d -> %d" created (PixiSurface.counters ()).ShapesCreated)

        let moved = Probe.pixel (pixiCanvas ()) 160 50
        let vacated = Probe.pixel (pixiCanvas ()) 80 50
        info "pixel pixi moved token (160,50)" (sprintf "%A" moved)
        check "the token pixel followed the state" (near [| 200; 40; 90 |] moved 2) (sprintf "%A" moved)
        check "the vacated pixel is background again" (near [| 10; 20; 30 |] vacated 2) (sprintf "%A" vacated)

        // 4. Pixi -> SVG releases everything, synchronously (the application was initialised).
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        check
            "switch back to svg with the state pixi was showing"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Mounted = Some RendererBackend.Svg
             && driver.Model.State = { Tick = 200; TokenX = 150.0 }
             && count container "svg" = 1
             && has "x=\"150\" y=\"40\"" container.innerHTML)
            (sprintf "%A %A %A" driver.Model.Selected driver.Mounted driver.Model.State)

        check "no pixi canvas remains" (count container "canvas" = 0) container.innerHTML
        check "no pixi application remains" ((live ()).Applications = 0) (sprintf "%A" (live ()))
        check "no pixi render callback remains" ((live ()).TickerListeners = 0) (sprintf "%A" (live ()))

        check
            "pixi released its WebGL context"
            (Probe.liveContexts () = contextsBefore)
            (sprintf "%d -> %d" contextsBefore (Probe.liveContexts ()))

        // 5. Switching away while initialisation is still pending: the canvas goes at once, the application when
        //    initialisation settles, and nothing throws.
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        check
            "a dispose during initialisation removes the canvas at once"
            (count container "canvas" = 0 && count container "svg" = 1)
            container.innerHTML

        let! _ = waitUntil (fun () -> (live ()).Applications = 0) 600

        check
            "the pending application is destroyed once initialisation settles"
            ((live ()).Applications = 0)
            (sprintf "%A" (live ()))

        check
            "the model stayed on svg through the abandoned pixi mount"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Model.Status = RendererStatus.Active)
            (sprintf "%A" driver.Model.Status)

        // 6. Repeated switching never holds two applications and ends released.
        let maxApplications = ref 0
        let maxCanvases = ref 0

        for round in 1..5 do
            driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
            maxApplications.Value <- max maxApplications.Value (live ()).Applications
            maxCanvases.Value <- max maxCanvases.Value (count container "canvas")
            let! _ = waitUntil (fun () -> (live ()).DisplayObjects = 2) 600

            driver.Dispatch(
                RendererMsg.StateChanged
                    {
                        Tick = 200 + round
                        TokenX = 20.0 * float round
                    }
            )

            driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        check
            "five svg -> pixi -> svg rounds never held two applications"
            (maxApplications.Value = 1)
            (string maxApplications.Value)

        check "five rounds never held two canvases" (maxCanvases.Value = 1) (string maxCanvases.Value)

        check
            "five rounds ended on svg with the latest state and nothing left over"
            (driver.Mounted = Some RendererBackend.Svg
             && driver.Model.State = { Tick = 205; TokenX = 100.0 }
             && count container "canvas" = 0
             && (live ()).Applications = 0
             && Probe.liveContexts () = contextsBefore)
            (sprintf "%A %A contexts=%d" driver.Model.State (live ()) (Probe.liveContexts ()))

        // 7. A node set that grows and shrinks keeps one display object per node.
        let swarm (board: Board) : Scene =
            {
                Nodes =
                    SceneNode.Rectangle((0.0, 0.0, 200.0, 100.0), background)
                    :: [
                        for i in 0 .. (board.Tick % 4) ->
                            SceneNode.Rectangle((10.0 + 40.0 * float i, 40.0, 20.0, 20.0), tokenColor)
                    ]
            }

        let swarmRegistry =
            SvgSurface.registry container svgOptions swarm
            |> PixiSurface.register container pixiOptions swarm

        let swarmDriver =
            RendererDriver.start swarmRegistry { Tick = 3; TokenX = 0.0 } ignore

        swarmDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

        let nodeCount () =
            1 + (swarmDriver.Model.State.Tick % 4) + 1

        let! _ = waitUntil (fun () -> (live ()).DisplayObjects = nodeCount ()) 600

        check
            "pixi draws one display object per node of a larger scene"
            ((live ()).DisplayObjects = nodeCount ())
            (sprintf "%d vs %d" (live ()).DisplayObjects (nodeCount ()))

        let tracks = ref true

        for tick in 0..40 do
            swarmDriver.Dispatch(RendererMsg.StateChanged { Tick = tick; TokenX = 0.0 })

            if (live ()).DisplayObjects <> nodeCount () then
                tracks.Value <- false

        check "display object count tracked the node count through 41 grow/shrink presents" tracks.Value "diverged"

        check
            "still one application and one render callback after grow/shrink"
            ((live ()).Applications = 1 && (live ()).TickerListeners = 1)
            (sprintf "%A" (live ()))

        (swarmDriver :> System.IDisposable).Dispose()

        check
            "disposing the driver on pixi releases the canvas, application and display objects"
            (count container "canvas" = 0
             && (live ()).Applications = 0
             && (live ()).DisplayObjects = 0)
            (sprintf "canvas=%d %A" (count container "canvas") (live ()))

        // 8. No WebGL: refused, SVG takes over with the state intact, nothing left behind.
        let restore = Probe.refuseContextsAfter 0
        driver.Dispatch(RendererMsg.StateChanged { Tick = 300; TokenX = 30.0 })
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        restore ()

        check
            "a browser with no WebGL falls back to svg"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Mounted = Some RendererBackend.Svg
             && driver.Model.State = { Tick = 300; TokenX = 30.0 })
            (sprintf "%A %A %A" driver.Model.Selected driver.Mounted driver.Model.State)

        check
            "the fallback names pixi as the cause"
            (match driver.Model.Refusal with
             | Some(RendererRefusal.MountFailed(RendererBackend.Pixi, _)) -> true
             | _ -> false)
            (sprintf "%A" driver.Model.Refusal)

        check
            "the refused mount left no canvas or application"
            (count container "canvas" = 0 && (live ()).Applications = 0)
            (sprintf "%s %A" container.innerHTML (live ()))

        // 9. Pixi's asynchronous initialisation fails after Mount returned Ok: the live mount reports it, the
        //    driver falls back to SVG, and the half-started application is released.
        let failuresBefore = (PixiSurface.counters ()).InitFailures
        let restoreAsync = Probe.refuseContextsAfter 1
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

        check
            "the mount is accepted before the asynchronous failure (pixi selected, canvas present)"
            (driver.Model.Selected = RendererBackend.Pixi && count container "canvas" = 1)
            (sprintf "%A" driver.Model.Selected)

        let! _ = waitUntil (fun () -> driver.Model.Selected = RendererBackend.Svg) 600
        restoreAsync ()

        check
            "an asynchronous initialisation failure falls back to svg"
            (driver.Model.Selected = RendererBackend.Svg
             && driver.Mounted = Some RendererBackend.Svg
             && driver.Model.State = { Tick = 300; TokenX = 30.0 })
            (sprintf "%A %A" driver.Model.Selected driver.Mounted)

        check
            "the asynchronous failure is recorded against pixi"
            (match driver.Model.Refusal with
             | Some(RendererRefusal.MountFailed(RendererBackend.Pixi, _)) -> true
             | _ -> false)
            (sprintf "%A" driver.Model.Refusal)

        check
            "the failed initialisation was counted"
            ((PixiSurface.counters ()).InitFailures = failuresBefore + 1)
            (sprintf "%A" (PixiSurface.counters ()))

        let! _ = waitUntil (fun () -> (live ()).Applications = 0) 600

        check
            "the failed application and its canvas were released"
            ((live ()).Applications = 0 && count container "canvas" = 0)
            (sprintf "%A %s" (live ()) container.innerHTML)

        // 9b. A failure that arrives LATE, from a mount that was already disposed, must not fail the mount that is
        //     live now. Pixi's context request on the first canvas is refused; the mount is abandoned before it
        //     settles; Pixi is selected again on a healthy canvas. The old rejection then arrives while the new mount
        //     is live and the model is on Pixi, which is exactly when a report would be taken for the new mount's.
        let lateBefore = (PixiSurface.counters ()).InitFailures
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        let abandoned = pixiCanvas ()
        let liftRefusal = Probe.refuseCanvas abandoned
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

        let! _ =
            waitUntil
                (fun () ->
                    (PixiSurface.counters ()).InitFailures > lateBefore
                    && (live ()).Applications = 1
                    && (live ()).DisplayObjects = 2)
                600

        let! _ = settle 20
        liftRefusal ()

        check
            "the abandoned mount's initialisation did fail (the late failure really arrived)"
            ((PixiSurface.counters ()).InitFailures = lateBefore + 1)
            (sprintf "%d -> %d" lateBefore (PixiSurface.counters ()).InitFailures)

        check
            "a late failure from a disposed mount does not fail the live pixi mount"
            (driver.Model.Selected = RendererBackend.Pixi
             && driver.Mounted = Some RendererBackend.Pixi
             && driver.Model.Status = RendererStatus.Active
             && (live ()).Applications = 1
             && (live ()).DisplayObjects = 2)
            (sprintf "%A %A %A %A" driver.Model.Selected driver.Mounted driver.Model.Status (live ()))

        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        // 10. Options the adapter refuses are reported as a refused mount too.
        let invalidRegistry =
            RendererRegistry.register
                (PixiSurface.adapter container (PixiSurface.defaultOptions "bad" 0.0 100.0) view)
                (SvgSurface.registry container svgOptions view)

        let invalidDriver = RendererDriver.start invalidRegistry initial ignore
        invalidDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)

        check
            "a zero-width pixi canvas is refused and falls back to svg"
            (invalidDriver.Model.Selected = RendererBackend.Svg
             && invalidDriver.Mounted = Some RendererBackend.Svg
             && count container "canvas" = 0)
            (sprintf "%A %A" invalidDriver.Model.Selected invalidDriver.Mounted)

        (invalidDriver :> System.IDisposable).Dispose()

        // 11. A Present that throws is contained: the pixi mount is released and SVG shows the same state.
        let fragile (board: Board) : Scene =
            if board.Tick = 99 then failwith "view boom" else view board

        let fragileDriver =
            RendererDriver.start
                (SvgSurface.registry container svgOptions view
                 |> PixiSurface.register container pixiOptions fragile)
                { Tick = 1; TokenX = 10.0 }
                ignore

        fragileDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        let! _ = waitUntil (fun () -> (live ()).DisplayObjects = 2) 600

        check
            "the fragile view mounts on pixi first"
            (fragileDriver.Mounted = Some RendererBackend.Pixi && (live ()).Applications = 1)
            (sprintf "%A %A" fragileDriver.Mounted (live ()))

        fragileDriver.Dispatch(RendererMsg.StateChanged { Tick = 99; TokenX = 99.0 })

        check
            "a throwing present falls back to svg with the state that threw"
            (fragileDriver.Model.Selected = RendererBackend.Svg
             && fragileDriver.Mounted = Some RendererBackend.Svg
             && fragileDriver.Model.State = { Tick = 99; TokenX = 99.0 })
            (sprintf "%A %A %A" fragileDriver.Model.Selected fragileDriver.Mounted fragileDriver.Model.State)

        check
            "the throwing present released the canvas and application"
            (count container "canvas" = 0 && (live ()).Applications = 0)
            (sprintf "canvas=%d %A" (count container "canvas") (live ()))

        (fragileDriver :> System.IDisposable).Dispose()

        // 12. The adapter's own books balance with Pixi's.
        check
            "the observer never saw a model whose backend was not the one mounted (Mounted asserted inside it)"
            (pixiAudit.Violations.Count = 0 && pixiAudit.Realised >= 14)
            (sprintf "realised=%d violations=%s" pixiAudit.Realised (String.concat "; " pixiAudit.Violations))

        (driver :> System.IDisposable).Dispose()
        let! _ = waitUntil (fun () -> (live ()).Applications = 0) 600
        let after = PixiSurface.counters ()
        let made = after.ApplicationsCreated - before.ApplicationsCreated

        let counterLine =
            sprintf
                "applications %d/%d shapes %d/%d init failures %d unsupported %d (created/destroyed)"
                after.ApplicationsCreated
                after.ApplicationsDestroyed
                after.ShapesCreated
                after.ShapesDestroyed
                after.InitFailures
                after.UnsupportedNodes

        info "pixi adapter counters" counterLine

        check
            "every application the adapter created was destroyed"
            (made > 0 && made = after.ApplicationsDestroyed - before.ApplicationsDestroyed)
            counterLine

        check
            "every display object the adapter created was destroyed"
            (after.ShapesCreated - before.ShapesCreated = after.ShapesDestroyed - before.ShapesDestroyed)
            counterLine

        check "nothing is left in the container" (container.children.length = 0) container.innerHTML

        // 13. A release step that throws must not stop the ones after it. `Application.destroy`, the step that
        //     destroys the application, is made to throw: the WebGL context must still be lost and the canvas
        //     removed, and the host must report the failure without losing the selection.
        let releaseDriver =
            RendererDriver.start registry { Tick = 400; TokenX = 40.0 } ignore

        releaseDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        let! _ = waitUntil (fun () -> (live ()).DisplayObjects = 2) 600

        check
            "the release probe starts with pixi mounted and drawn"
            (releaseDriver.Mounted = Some RendererBackend.Pixi
             && (live ()).Applications = 1
             && Probe.liveContexts () = 1)
            (sprintf "%A %A contexts=%d" releaseDriver.Mounted (live ()) (Probe.liveContexts ()))

        let repair = Probe.breakPixiDestroy ()
        releaseDriver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)

        check
            "a throwing destroy is reported, and the selection moves on to svg"
            (releaseDriver.Model.Selected = RendererBackend.Svg
             && releaseDriver.Mounted = Some RendererBackend.Svg
             && (match releaseDriver.Model.Refusal with
                 | Some(RendererRefusal.DisposeFailed(RendererBackend.Pixi, _)) -> true
                 | _ -> false))
            (sprintf "%A %A %A" releaseDriver.Model.Selected releaseDriver.Mounted releaseDriver.Model.Refusal)

        check
            "the release steps after the throwing destroy still ran (context lost, canvas removed)"
            (Probe.liveContexts () = 0 && count container "canvas" = 0)
            (sprintf "contexts=%d canvas=%d" (Probe.liveContexts ()) (count container "canvas"))

        repair ()
        let! _ = waitUntil (fun () -> (live ()).Applications = 0) 600
        (releaseDriver :> System.IDisposable).Dispose()

        check
            "no pixi application or WebGL context survived the pixi section"
            ((live ()).Applications = 0 && Probe.liveContexts () = contextsBefore)
            (sprintf "%A contexts=%d" (live ()) (Probe.liveContexts ()))
    }

/// All three backends on one game state: SVG <-> Pixi <-> Babylon, state preserved across every switch, exactly
/// one backend holding resources at any time, and everything released at the end.
let runThreeBackends () : Async<unit> =
    async {
        let container = document.createElement "div"
        container.id <- "stage-three"
        document.body.appendChild container |> ignore

        let svgOptions = SvgSurface.defaultOptions "Smoke board" 200.0 100.0
        let driverRef: RendererDriver<Board> option ref = ref None

        let pixiOptions =
            { PixiSurface.defaultOptions "Smoke board (pixi)" 200.0 100.0 with
                OnFailure =
                    fun reason ->
                        match driverRef.Value with
                        | Some d -> RendererDriver.failBackend d RendererBackend.Pixi reason
                        | None -> ()
            }

        let babylonOptions =
            BabylonSurface.defaultOptions "Smoke board (babylon)" 200.0 100.0

        // Each canvas adapter's view is called at the start of its Mount, before it acquires anything. At that
        // moment the previous backend must already be fully released: no svg root, no canvas, no engine, no application.
        let atMount = ResizeArray<string>()

        let sampled (name: string) (board: Board) : Scene =
            match driverRef.Value with
            | Some d when d.Mounted.IsNone ->
                atMount.Add(
                    sprintf
                        "%s: svg=%d canvas=%d engines=%d applications=%d"
                        name
                        (count container "svg")
                        (count container "canvas")
                        (Probe.engines ())
                        (PixiSurface.live ()).Applications
                )
            | _ -> ()

            view board

        let registry =
            SvgSurface.registry container svgOptions view
            |> PixiSurface.register container pixiOptions (sampled "pixi")
            |> BabylonSurface.register container babylonOptions (sampled "babylon")

        let audit = ObserverAudit()

        let driver =
            RendererDriver.start registry { Tick = 0; TokenX = 10.0 } (audit.Observe false)

        audit.Driver <- Some driver
        driverRef.Value <- Some driver

        check
            "all three backends are offered, in declared order"
            (RendererSelection.available driver.Model = RendererBackend.all)
            (sprintf "%A" (RendererSelection.available driver.Model))

        let canvasOf (token: string) =
            container.querySelector (sprintf "canvas[data-renderer=%s]" token) :?> HTMLCanvasElement

        let steps =
            [
                RendererBackend.Pixi
                RendererBackend.Babylon
                RendererBackend.Svg
                RendererBackend.Babylon
                RendererBackend.Pixi
                RendererBackend.Svg
                RendererBackend.Pixi
                RendererBackend.Svg
            ]

        let mutable previous = RendererBackend.Svg
        let stepNumber = ref 0
        let exclusive = ref true
        let renderedRight = ref 0

        for backend in steps do
            stepNumber.Value <- stepNumber.Value + 1
            let n = stepNumber.Value

            let state =
                {
                    Tick = 100 + n
                    TokenX = 20.0 + 10.0 * float n
                }

            let name =
                sprintf "step %d: %s -> %s" n (RendererBackend.id previous) (RendererBackend.id backend)

            driver.Dispatch(RendererMsg.StateChanged state)
            driver.Dispatch(RendererMsg.SelectBackend backend)

            check
                (name + " selects and mounts the backend with the state intact")
                (driver.Model.Selected = backend
                 && driver.Mounted = Some backend
                 && driver.Model.State = state
                 && driver.Model.Status = RendererStatus.Active)
                (sprintf "%A %A %A" driver.Model.Selected driver.Mounted driver.Model.State)

            // The canvas backends draw asynchronously (shader compilation / renderer initialisation): wait for the
            // token to appear where the state puts it, then require that backend's colour (Pixi within 2 per channel, Babylon within 4 of the lit colour).
            match backend with
            | RendererBackend.Svg ->
                check
                    (name + ": svg shows the state")
                    (count container "svg" = 1
                     && has (sprintf "x=\"%g\" y=\"40\"" state.TokenX) container.innerHTML)
                    container.innerHTML
            | RendererBackend.Pixi ->
                let! pixel, _ =
                    awaitPixel driver (fun () -> canvasOf "pixi") (int state.TokenX + 10) 50 (fun p -> p.[0] > 40) 600

                if near [| 200; 40; 90 |] pixel 2 then
                    renderedRight.Value <- renderedRight.Value + 1

                check
                    (name + ": the pixi token pixel is within 2 per channel (200,40,90)")
                    (near [| 200; 40; 90 |] pixel 2)
                    (sprintf "%A" pixel)
            | RendererBackend.Babylon ->
                let! pixel, _ =
                    awaitPixel
                        driver
                        (fun () -> canvasOf "babylon")
                        (int state.TokenX + 10)
                        50
                        (fun p -> p.[0] > 40)
                        600

                if near [| 200; 40; 90 |] pixel pixelTolerance then
                    renderedRight.Value <- renderedRight.Value + 1

                check
                    (name + ": the babylon token pixel is the declared colour, unlit (200,40,90)")
                    (near [| 200; 40; 90 |] pixel pixelTolerance)
                    (sprintf "%A" pixel)

            let canvases = count container "canvas"
            let engines = Probe.engines ()
            let applications = (PixiSurface.live ()).Applications

            let expected =
                match backend with
                | RendererBackend.Svg -> (0, 0, 0)
                | RendererBackend.Pixi -> (1, 0, 1)
                | RendererBackend.Babylon -> (1, 1, 0)

            if (canvases, engines, applications) <> expected then
                exclusive.Value <- false

            check
                (name + ": only that backend holds resources")
                ((canvases, engines, applications) = expected)
                (sprintf "canvas=%d engines=%d applications=%d" canvases engines applications)

            previous <- backend

        check
            "all five canvas renders matched their expected pixels"
            (renderedRight.Value = 5)
            (string renderedRight.Value)

        check "no step ever held resources of two backends" exclusive.Value "overlap"

        check
            "every canvas mount started with the previous backend fully released"
            (atMount.Count >= 5
             && atMount
                |> Seq.forall (fun line -> line.EndsWith "svg=0 canvas=0 engines=0 applications=0"))
            (String.concat " | " atMount)

        check
            "the observer never saw a model whose backend was not the one mounted (Mounted asserted inside it)"
            (audit.Violations.Count = 0 && audit.Realised >= steps.Length)
            (sprintf "realised=%d violations=%s" audit.Realised (String.concat "; " audit.Violations))

        // Straight Pixi -> Babylon -> Pixi with no SVG between, then dispose while Babylon is mounted.
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)
        let! _ = waitUntil (fun () -> (PixiSurface.live ()).Applications = 0) 600

        check
            "direct pixi <-> babylon switching, abandoning a pending pixi initialisation, ends on babylon with one engine and no application"
            (driver.Mounted = Some RendererBackend.Babylon
             && Probe.engines () = 1
             && (PixiSurface.live ()).Applications = 0
             && count container "canvas" = 1)
            (sprintf "%A engines=%d %A" driver.Mounted (Probe.engines ()) (PixiSurface.live ()))

        (driver :> System.IDisposable).Dispose()
        let! _ = waitUntil (fun () -> (PixiSurface.live ()).Applications = 0) 600

        check
            "disposing the driver leaves no canvas, svg, engine, application or adapter WebGL context"
            (container.children.length = 0
             && Probe.engines () = 0
             && Probe.renderLoops () = 0
             && (PixiSurface.live ()).Applications = 0
             && Probe.liveContexts () = 0)
            (sprintf "%s engines=%d %A" container.innerHTML (Probe.engines ()) (PixiSurface.live ()))
    }

/// GAME-RENDER-ADOPT-01.1 (FS.GG.Game#745): the conformance kit on the real backends. The reference game's own
/// renderer declaration (src/ReferenceGame/Renderers.fs) and the generator's sample (GeneratedRenderers.fs) each run
/// `RendererConformance.run` on SVG, Pixi and Babylon through `RendererDriver`, with the page's independent resource
/// probe (SVG roots, canvases, engines, applications, live WebGL contexts) and outputs read back from the DOM and
/// from canvas pixels. Every kit result becomes one smoke check.
let kitChecks = 73

/// The kit's interaction section on three backends (FS.GG.Game#751): one subject check and twelve per backend.
let interactionChecks = 37

/// The kit's pixels section (FS.GG.Game#753): the subject check, one per backend, and one hit-and-edge check per backend.
let pixelChecks = 7

/// Kit checks that fail for a known, routed reason, each with its owner. The smoke asserts that they still fail, so the
/// gap stays visible and the fix that closes it must remove the entry. The retained SVG host of FS.GG.UI.Scene 0.31.0
/// re-appends every mirror button and rewrites its status on each Present (focus drops to the body, the live region
/// is written every frame); the bump that fixes it is FS.GG.Rendering#1504.
let knownGaps =
    [
        "interaction svg: keyboard focus stays on the mirror entry across the Present of its selection and across unchanged Presents",
        "FS.GG.Rendering#1504"
        "interaction svg: an unchanged Present does not write the live status", "FS.GG.Rendering#1504"
    ]
    |> Map.ofList

let rgbOf (color: Color) = [| int color.Red; int color.Green; int color.Blue |]


let svgFill (color: Color) =
    sprintf "fill=\"rgb(%d %d %d)\"" color.Red color.Green color.Blue

/// Whether `backend`'s canvas shows `state`: a token colour at one point and a background colour at another, both
/// derived from the state by `probe`. It first waits (bounded) until the canvas shows the state the HOST presents, so
/// asynchronous first frames are not mistaken for a mismatch, then reads the asked state's points once: a state that
/// is not shown is rejected without waiting.
let showsPixels
    (host: ConformanceHost<'State>)
    (container: HTMLElement)
    (backend: RendererBackend)
    (probe: 'State -> (Color * int * int) * (Color * int * int))
    (state: 'State)
    : Async<Result<unit, string>> =
    async {
        let expect, tolerance =
            match backend with
            | _ -> rgbOf, pixelTolerance

        let read (color: Color, x: int, y: int) (frames: int) =
            BrowserConformance.readPixel host container backend x y (fun p -> near (expect color) p tolerance) frames

        let currentToken, currentBackground = probe (host.Model()).State
        let! _ = read currentToken 600
        let! _ = read currentBackground 60
        let token, background = probe state
        let! tokenPixel = read token 0
        let! backgroundPixel = read background 0
        let tokenColor, _, _ = token
        let backgroundColor, _, _ = background

        return
            if near (expect tokenColor) tokenPixel tolerance
               && near (expect backgroundColor) backgroundPixel tolerance then
                Ok()
            else
                Error(
                    sprintf
                        "%s token %A (want %A) background %A (want %A)"
                        (RendererBackend.id backend)
                        tokenPixel
                        (expect tokenColor)
                        backgroundPixel
                        (expect backgroundColor)
                )
    }

let runKit (label: string) (expected: int) (subject: ConformanceSubject<'State>) : Async<unit> =
    async {
        let! results = RendererConformance.run subject

        for result in results do
            match Map.tryFind result.Check knownGaps with
            | Some owner ->
                check
                    (sprintf "%s: known gap (%s) still fails: %s" label owner result.Check)
                    (not result.Passed)
                    "the check now passes: remove it from knownGaps"

                info (sprintf "%s known gap %s" label owner) result.Detail
            | None -> check (label + ": " + result.Check) result.Passed result.Detail

        check
            (sprintf "%s: the kit ran all %d checks for three backends" label expected)
            (results.Length = expected)
            (string results.Length)
    }

let runConformance () : Async<unit> =
    async {
        BrowserConformance.installContextProbe ()
        BrowserConformance.installInputProbe ()

        // The reference game.
        let container = document.createElement "div"
        container.id <- "stage-reference"
        document.body.appendChild container |> ignore
        let route = BrowserRenderers.route ()

        let options =
            BrowserRenderers.options "Reference game" ReferenceRenderers.width ReferenceRenderers.height

        // FS.GG.Game#751: the reference game is played by selection on every backend.
        let selection = SelectionRoute.create ()

        let renderers =
            match BrowserRenderers.surfaces container options route with
            | (svg :: _) as all ->
                ReferenceRenderers.declare (SceneSurface.mountScene svg) all
                |> SceneRenderers.withInteraction ReferenceRenderers.interaction selection
            | [] -> failwith "no surfaces"

        let start: ReferenceState.Model =
            {
                Screen = ReferenceState.Menu
                Room = ReferenceState.Atrium
                X = 0
                DestinationActive = false
                CameraTransition = false
                VaultClear = false
                Aim = 0, 0
                Tick = 0
                Selected = None
            }

        let states =
            [
                start
                { start with
                    Screen = ReferenceState.Playing
                    X = 1
                    Aim = 12, 7
                }
                { start with
                    Screen = ReferenceState.Paused
                    Room = ReferenceState.Vault
                    X = 3
                    Tick = 4
                }
                { start with
                    Screen = ReferenceState.Won
                    Room = ReferenceState.Exit
                    X = 5
                    Aim = 200, 90
                }
            ]

        let shows (host: ConformanceHost<ReferenceState.Model>) backend (model: ReferenceState.Model) =
            let x, y, _, _ = ReferenceRenderers.tokenBounds model
            let token = ReferenceRenderers.tokenColor model.Screen
            let room = ReferenceRenderers.roomColor model.Room

            match backend with
            | RendererBackend.Svg ->
                async {
                    let html = container.innerHTML

                    return
                        if has (sprintf "x=\"%g\" y=\"%g\"" x y) html
                           && has (svgFill token) html
                           && has (svgFill room) html then
                            Ok()
                        else
                            Error(sprintf "svg does not show the token at %g with %s on %s" x (svgFill token) (svgFill room))
                }
            | _ ->
                showsPixels
                    host
                    container
                    backend
                    (fun (m: ReferenceState.Model) ->
                        let x, y, _, _ = ReferenceRenderers.tokenBounds m

                        (ReferenceRenderers.tokenColor m.Screen, int x + 12, int y + 12),
                        (ReferenceRenderers.roomColor m.Room, 2, 100))
                    model

        do!
            runKit
                "reference kit"
                (kitChecks + interactionChecks + pixelChecks)
                (BrowserConformance.subject
                    container
                    route
                    (SceneRenderers.registry renderers)
                    RendererBackend.all
                    states
                    shows
                 |> RendererConformance.withInteraction
                     ReferenceRenderers.interaction
                     selection
                     (BrowserConformance.interactionProbe container)
                 |> RendererConformance.withPixels
                     (fun model -> ScenePixels.expectations ReferenceRenderers.width ReferenceRenderers.height (ReferenceRenderers.view model))
                     (BrowserConformance.pixelProbe container ReferenceRenderers.width ReferenceRenderers.height))

        check
            "reference kit: the stage is empty afterwards"
            (container.children.length = 0)
            container.innerHTML

        // Causal control: the output probes tell states apart. Each backend shows the first state and is asked whether
        // it shows the last one (another room, screen and token column); every probe must say no.
        let controlHost =
            BrowserConformance.host (BrowserRenderers.start route (SceneRenderers.registry renderers) states.Head ignore)

        for backend in RendererBackend.all do
            controlHost.Dispatch(RendererMsg.SelectBackend backend)
            let! own = shows controlHost backend states.Head
            let! other = shows controlHost backend (List.last states)

            check
                (sprintf "reference kit control: %s shows its state and does not pass for another" (RendererBackend.id backend))
                (own = Ok() && other <> Ok())
                (sprintf "own=%A other=%A" own other)

        controlHost.Dispose()
        let! _ = BrowserConformance.waitUntil 600 (fun () -> (PixiSurface.live ()).Applications = 0)

        // FS.GG.Game#751, the reference game played through its renderer driver: on every backend a pointer press on the
        // token selects it in the game state, the backend draws the selection ring, the mirror's status names it, and
        // the surface is focusable; disposing leaves no input host behind.
        // The census is page-wide: Pixi leaves its own listeners on the canvas of an initialisation that failed (the
        // late-failure sections above), which are not this driver's.
        let before = RendererBackend.all |> List.map (fun backend -> backend, BrowserConformance.inputHosts backend) |> Map.ofList
        let driver = BrowserRenderers.start route (SceneRenderers.registry renderers) start ignore
        let demoHost = BrowserConformance.host driver
        BrowserConformance.installInputProbe ()

        selection
        |> SelectionRoute.bind (fun request ->
            driver.Dispatch(RendererMsg.StateChanged(SceneInteraction.apply ReferenceRenderers.interaction request driver.Model.State)))

        let x, y, _, _ = ReferenceRenderers.tokenBounds start

        for backend in RendererBackend.all do
            let name = RendererBackend.id backend
            driver.Dispatch(RendererMsg.SelectBackend backend)
            driver.Dispatch(RendererMsg.StateChanged { driver.Model.State with Selected = None })
            do! BrowserConformance.press container backend { X = x + 12.0; Y = y + 12.0 }
            let selected = driver.Model.State.Selected

            let status =
                match container.querySelector (sprintf "[data-scene-controls][data-renderer=%s] [role=status]" name) with
                | null -> "<no status>"
                | element -> element.textContent

            let surface =
                match container.querySelector (sprintf "[data-renderer=%s]:not([data-scene-controls])" name) with
                | null -> "<none>"
                | element -> sprintf "%s/%s" (element.getAttribute "role") (element.getAttribute "tabindex")

            let! ring =
                match backend with
                | RendererBackend.Svg ->
                    async {
                        return
                            if has (svgFill ReferenceRenderers.selectionColor) container.innerHTML then
                                Ok()
                            else
                                Error "no ring in the svg"
                    }
                | _ ->
                    async {
                        let expect = rgbOf ReferenceRenderers.selectionColor
                        let tolerance = pixelTolerance

                        let! pixel =
                            BrowserConformance.readPixel
                                demoHost
                                container
                                backend
                                (int x - 2)
                                (int y + 12)
                                (fun p -> near expect p tolerance)
                                600

                        return if near expect pixel tolerance then Ok() else Error(sprintf "ring pixel %A, want %A" pixel expect)
                    }

            check
                (sprintf "reference game on %s: a pointer press on the token selects it in the game state, the ring is drawn and the status names it" name)
                (selected = Some "token" && status = "Player token" && ring = Ok())
                (sprintf "selected=%A status=%s ring=%A" selected status ring)

            check
                (sprintf "reference game on %s: the surface is a focusable application" name)
                (surface = "application/0")
                surface

        (driver :> System.IDisposable).Dispose()
        SelectionRoute.unbind selection

        let! released =
            BrowserConformance.waitUntil 600 (fun () ->
                RendererBackend.all |> List.forall (fun backend -> BrowserConformance.inputHosts backend <= before.[backend]))

        check
            "reference game: disposing the driver leaves no input host behind"
            released
            (RendererBackend.all
             |> List.map (fun backend ->
                 sprintf "%s=%d (before %d)" (RendererBackend.id backend) (BrowserConformance.inputHosts backend) before.[backend])
             |> String.concat " ")

        let! _ = BrowserConformance.waitUntil 600 (fun () -> (PixiSurface.live ()).Applications = 0)
        ()

        // The generator's sample, unedited: its placeholder view on all three backends.
        let generated = document.createElement "div"
        generated.id <- "stage-generated"
        document.body.appendChild generated |> ignore

        let backgroundOf (state: int) =
            match (GeneratedRenderers.view state).Nodes with
            | SceneNode.Rectangle(_, fill) :: _ -> fill
            | _ -> failwith "the generated view starts with its background"

        let generatedShows (host: ConformanceHost<int>) backend (state: int) =
            match backend with
            | RendererBackend.Svg ->
                async {
                    return
                        if has (svgFill (backgroundOf state)) generated.innerHTML then
                            Ok()
                        else
                            Error("svg lacks the generated background " + svgFill (backgroundOf state))
                }
            | _ -> showsPixels host generated backend (fun s -> (backgroundOf s, 160, 90), (backgroundOf s, 2, 2)) state

        do! runKit "generated kit" kitChecks (GeneratedRenderers.conformance generated [ 0; 1; 2 ] generatedShows)

        check
            "generated kit: the stage is empty afterwards"
            (generated.children.length = 0)
            generated.innerHTML

        // The generated start path: SVG first, then a runtime switch to each canvas backend with the state kept.
        let driver = GeneratedRenderers.start generated 7 ignore
        let offered = RendererSelection.available driver.Model
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)
        let onBabylon = driver.Mounted = Some RendererBackend.Babylon && driver.Model.State = 7
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        let onPixi = driver.Mounted = Some RendererBackend.Pixi && driver.Model.State = 7

        check
            "generated start: offers all three backends and switches at runtime with the state kept"
            (offered = RendererBackend.all && onBabylon && onPixi)
            (sprintf "%A babylon=%b pixi=%b" offered onBabylon onPixi)

        (driver :> System.IDisposable).Dispose()
        let! released = BrowserConformance.waitUntil 600 (fun () -> (PixiSurface.live ()).Applications = 0)
        check "generated start: disposing releases the pending pixi application" released (sprintf "%A" (PixiSurface.live ()))
    }

/// FS.GG.Game#753: Babylon draws a 2D scene at its declared positions and sizes at any node count. A grid of
/// `columns x rows` small squares (every fifth one round) over a background is mounted on Babylon alone. Every node
/// must be among Babylon's active meshes, and the pixels `ScenePixels.expectations` decides for the background and
/// every `stride`-th node (which the grid's non-overlapping nodes leave valid) must be read back. A perspective camera
/// drew the later nodes magnified about the centre of the canvas and culled those beyond about 400.
let runBabylonScale (columns: int) (rows: int) (stride: int) : Async<unit> =
    async {
        let cell = 10.0
        let width = float columns * cell
        let height = float rows * cell

        let node (index: int) =
            let x = float (index % columns) * cell + 2.0
            let y = float (index / columns) * cell + 2.0

            let color: Color =
                {
                    Red = byte (40 + (index * 7) % 200)
                    Green = byte (60 + (index * 13) % 180)
                    Blue = byte (80 + (index * 29) % 150)
                    Alpha = 255uy
                }

            if index % 5 = 4 then
                SceneNode.Circle({ X = x + 3.0; Y = y + 3.0 }, 3.0, color)
            else
                SceneNode.Rectangle((x, y, 6.0, 6.0), color)

        let backgroundColor: Color =
            {
                Red = 10uy
                Green = 20uy
                Blue = 30uy
                Alpha = 255uy
            }

        let background = SceneNode.Rectangle((0.0, 0.0, width, height), backgroundColor)
        let total = columns * rows + 1
        let scene: Scene = { Nodes = background :: [ for index in 0 .. columns * rows - 1 -> node index ] }

        let sample: Scene =
            { Nodes = background :: [ for index in 0..stride .. columns * rows - 1 -> node index ] }

        let wanted = ScenePixels.expectations width height sample
        let points = wanted |> List.map (fun expectation -> expectation.Point)
        let container = document.createElement "div"
        document.body.appendChild container |> ignore
        let name = sprintf "babylon scale %d nodes" total

        match BabylonSurface.mountScene container (BabylonSurface.defaultOptions name width height) scene with
        | Error reason -> check (name + ": mounts") false reason
        | Ok mount ->
            let frames = ref 0
            let finished = ref false
            let wrong = ref [ "not read" ]
            let active = ref 0

            // Shaders compile asynchronously: present the same scene frame by frame (each present renders in the task
            // that reads) until every pixel is drawn and every mesh is active.
            while not finished.Value do
                mount.Present scene
                let! shown = BrowserConformance.drawnPixels container width height RendererBackend.Babylon points
                active.Value <- BrowserConformance.babylonActiveMeshes ()

                let matched = ResizeArray<int[] * (int * int * int)>()

                wrong.Value <-
                    List.zip wanted shown
                    |> List.choose (fun (expectation, got) ->
                        let want = rgbOf expectation.Colour
                        let wantR, wantG, wantB = want.[0], want.[1], want.[2]

                        match got with
                        | Some(r, g, b) when
                            abs (r - wantR) <= pixelTolerance && abs (g - wantG) <= pixelTolerance && abs (b - wantB) <= pixelTolerance
                            ->
                            matched.Add((want, (r, g, b)))
                            None
                        | _ -> Some(sprintf "(%g, %g) wants (%d,%d,%d) shows %A" expectation.Point.X expectation.Point.Y wantR wantG wantB got))

                if wrong.Value.IsEmpty then
                    for want, got in matched do
                        noteDeviation RendererBackend.Babylon want got

                if (wrong.Value.IsEmpty && active.Value = total) || frames.Value >= 600 then
                    finished.Value <- true
                else
                    frames.Value <- frames.Value + 1
                    do! BrowserConformance.waitUntil 1 (fun () -> false) |> Async.Ignore

            info (name + ": frames until drawn") (string frames.Value)

            check
                (sprintf "%s: every node is among babylon's active meshes" name)
                (active.Value = total)
                (sprintf "%d active of %d nodes" active.Value total)

            check
                (sprintf "%s: %d sampled pixels are drawn at their declared positions and sizes" name wanted.Length)
                (not wanted.IsEmpty && wrong.Value.IsEmpty)
                (sprintf "%d differ, e.g. %s" wrong.Value.Length (wrong.Value |> List.truncate 3 |> String.concat "; "))

            mount.Dispose()

        container.remove ()
    }

/// FS.GG.Game#753: draw calls and CPU time of Babylon's present, for the before/after record. WebGL draw calls are
/// counted at the context, independent of Babylon's own counters.
module DrawCalls =
    [<Emit("(() => { if (!window.__draws) { const h = { n: 0 }; window.__draws = h; for (const C of [window.WebGLRenderingContext, window.WebGL2RenderingContext]) { if (!C) continue; for (const f of ['drawElements', 'drawArrays', 'drawElementsInstanced', 'drawArraysInstanced', 'drawRangeElements']) { const o = C.prototype[f]; if (o) C.prototype[f] = function (...a) { h.n++; return o.apply(this, a) } } } } })()")>]
    let install () : unit = jsNative

    [<Emit("window.__draws.n")>]
    let total () : int = jsNative

    [<Emit("performance.now()")>]
    let now () : float = jsNative

let measureBabylon (columns: int) (rows: int) : Async<unit> =
    async {
        DrawCalls.install ()
        let cell = 10.0
        let width = float columns * cell
        let height = float rows * cell

        let node (index: int) =
            let x = float (index % columns) * cell + 2.0
            let y = float (index / columns) * cell + 2.0

            let color: Color =
                {
                    Red = byte (40 + (index * 7) % 200)
                    Green = byte (60 + (index * 13) % 180)
                    Blue = byte (80 + (index * 29) % 150)
                    Alpha = 255uy
                }

            if index % 5 = 4 then
                SceneNode.Circle({ X = x + 3.0; Y = y + 3.0 }, 3.0, color)
            else
                SceneNode.Rectangle((x, y, 6.0, 6.0), color)

        let total = columns * rows + 1

        let scene: Scene =
            { Nodes = SceneNode.Rectangle((0.0, 0.0, width, height), { Red = 10uy; Green = 20uy; Blue = 30uy; Alpha = 255uy }) :: [ for index in 0 .. columns * rows - 1 -> node index ] }

        // The same scene with its first node one unit to the right, to present a changed scene.
        let moved: Scene =
            { Nodes =
                scene.Nodes
                |> List.mapi (fun index node ->
                    match index, node with
                    | 1, SceneNode.Rectangle((x, y, w, h), color) -> SceneNode.Rectangle((x + 1.0, y, w, h), color)
                    | _ -> node) }

        let container = document.createElement "div"
        document.body.appendChild container |> ignore
        let name = sprintf "babylon measure %d nodes" total

        match BabylonSurface.mountScene container (BabylonSurface.defaultOptions name width height) scene with
        | Error reason -> info name reason
        | Ok mount ->
            // Let the shaders compile and the loop settle.
            let! _ = BrowserConformance.waitUntil 120 (fun () -> false)
            let presents = 20
            let before = DrawCalls.total ()
            let started = DrawCalls.now ()

            for _ in 1..presents do
                mount.Present scene

            let elapsed = DrawCalls.now () - started
            let perPresent = float (DrawCalls.total () - before) / float presents
            let idleBefore = DrawCalls.total ()
            let! _ = BrowserConformance.waitUntil 60 (fun () -> false)
            let perIdleFrame = float (DrawCalls.total () - idleBefore) / 60.0

            // Presenting a scene that changed by one node.
            let movedBefore = DrawCalls.total ()
            let movedStarted = DrawCalls.now ()

            for index in 1..presents do
                mount.Present(if index % 2 = 1 then moved else scene)

            let movedElapsed = DrawCalls.now () - movedStarted
            let perMovedPresent = float (DrawCalls.total () - movedBefore) / float presents

            info
                (name + ": draw calls")
                (sprintf
                    "%.1f per present, %.1f per idle frame (60 frames), unchanged present %.2f ms CPU, one-node-moved present %.2f ms CPU (%.1f draw calls), active meshes %d of %d"
                    perPresent
                    perIdleFrame
                    (elapsed / float presents)
                    (movedElapsed / float presents)
                    perMovedPresent
                    (BrowserConformance.babylonActiveMeshes ())
                    total)

            mount.Dispose()

        container.remove ()
    }

/// What a backend draws that differs from what `wanted` expects, polled frame by frame (`present` renders again in
/// the task that reads): empty when every expected pixel is shown. Points are board units of a `width` x `height` scene.
let drawnMismatches
    (container: HTMLElement)
    (width: float)
    (height: float)
    (backend: RendererBackend)
    (present: unit -> unit)
    (wanted: PixelExpectation list)
    (frames: int)
    : Async<string list> =
    async {
        let points = wanted |> List.map (fun expectation -> expectation.Point)
        let remaining = ref frames
        let finished = ref false
        let last: string list ref = ref [ "not read" ]

        while not finished.Value do
            present ()
            let! shown = BrowserConformance.drawnPixels container width height backend points

            let matched = ResizeArray<int[] * (int * int * int)>()

            last.Value <-
                List.zip wanted shown
                |> List.choose (fun (expectation, got) ->
                    let want, tolerance = rgbOf expectation.Colour, pixelTolerance

                    match got with
                    | Some(r, g, b) when near want [| r; g; b |] tolerance ->
                        matched.Add((want, (r, g, b)))
                        None
                    | _ ->
                        Some(sprintf "(%g, %g) wants %A shows %A" expectation.Point.X expectation.Point.Y want got))

            // The deviation is measured on reads that match in full: a read of a frame still being drawn is not one.
            if last.Value.IsEmpty then
                for want, got in matched do
                    noteDeviation backend want got

            if last.Value.IsEmpty || remaining.Value <= 0 then
                finished.Value <- true
            else
                remaining.Value <- remaining.Value - 1
                do! BrowserConformance.waitUntil 1 (fun () -> false) |> Async.Ignore

        return last.Value
    }

let private fixtureColor (red: byte) (green: byte) (blue: byte) : Color =
    {
        Red = red
        Green = green
        Blue = blue
        Alpha = 255uy
    }

/// FS.GG.Game#753, review B1: a board styled to another size than itself (SC2 shows its 640 x 360 map at 960 x 540, and
/// a small view at 320 x 180, at device pixel ratio 1 and 2; the page's ratio is asserted, not assumed). Babylon's engine sizes the backing store to the CSS size, so the pixels the probe reads
/// and the point a pick takes are not board units. On each backend: the board's pixels (the nodes' edges, and the empty
/// background) are where the scene declares them, and real presses just inside and just outside object edges, and at
/// the far corner, select exactly what the hit test says. A press went through the page's bounds, as a player's does.
let runScaledBoard () : Async<unit> =
    async {
        let boardWidth = 640.0
        let boardHeight = 360.0

        let boxA = { X = 0.0; Y = 0.0; Width = 32.0; Height = 32.0 }
        let boxB = { X = 100.0; Y = 100.0; Width = 80.0; Height = 60.0 }
        let boxC = { X = boardWidth - 32.0; Y = boardHeight - 32.0; Width = 32.0; Height = 32.0 }

        let fill (box: Rect) color =
            SceneNode.Rectangle((box.X, box.Y, box.Width, box.Height), color)

        let scene: Scene =
            {
                Nodes =
                    [
                        SceneNode.Rectangle((0.0, 0.0, boardWidth, boardHeight), fixtureColor 20uy 20uy 20uy)
                        fill boxA (fixtureColor 200uy 40uy 40uy)
                        fill boxB (fixtureColor 40uy 200uy 40uy)
                        fill boxC (fixtureColor 40uy 40uy 200uy)
                    ]
            }

        let target id label bounds : SelectableObject =
            {
                Id = id
                Label = label
                Bounds = bounds
                Shape = SelectableShape.Box
            }

        let objects = [ target "A" "A" boxA; target "B" "B" boxB; target "C" "C" boxC ]

        let frame: InteractiveScene =
            {
                Scene = scene
                Objects = objects
                Selected = None
            }

        let wanted = ScenePixels.expectations boardWidth boardHeight scene

        // Real presses: board point, and the object the hit test says is there.
        let presses =
            [
                "A centre", 16.0, 16.0
                "B centre", 140.0, 130.0
                "C centre", boardWidth - 16.0, boardHeight - 16.0
                "background", 300.0, 50.0
                "B just inside its left edge", boxB.X + 1.0, 130.0
                "B just outside its left edge", boxB.X - 1.0, 130.0
                "B just inside its right edge", boxB.X + boxB.Width - 1.0, 130.0
                "B just outside its right edge", boxB.X + boxB.Width + 1.0, 130.0
                "B just inside its top edge", 140.0, boxB.Y + 1.0
                "B just outside its top edge", 140.0, boxB.Y - 1.0
                "B just inside its bottom edge", 140.0, boxB.Y + boxB.Height - 1.0
                "B just outside its bottom edge", 140.0, boxB.Y + boxB.Height + 1.0
                "C inside the far corner", boardWidth - 2.0, boardHeight - 2.0
            ]

        for factor in [ 1.0; 2.0 ] do
            do! BrowserConformance.deviceScale factor

            check
                (sprintf "scaled board: the page runs at devicePixelRatio %g" factor)
                (window.devicePixelRatio = factor)
                (sprintf "devicePixelRatio is %g" window.devicePixelRatio)

            for (cssWidth, cssHeight), backend in List.allPairs [ 960.0, 540.0; 320.0, 180.0 ] RendererBackend.all do
                let name = sprintf "scaled board %gx%g styled %gx%g, dpr %g, %s" boardWidth boardHeight cssWidth cssHeight factor (RendererBackend.id backend)
                let container = document.createElement "div"
                container.id <- sprintf "stage-scaled-%s-%g-%g" (RendererBackend.id backend) factor cssWidth
                document.body.appendChild container |> ignore
                let style = document.createElement "style"
                style.textContent <- sprintf "#%s canvas, #%s svg { width: %gpx; height: %gpx }" container.id container.id cssWidth cssHeight
                document.head.appendChild style |> ignore
                let requests = ResizeArray<SelectionRequest>()
                let report (request: SelectionRequest) = requests.Add request

                let mounted =
                    match backend with
                    | RendererBackend.Svg ->
                        SvgSurface.mountInteractive container (SvgSurface.defaultOptions name boardWidth boardHeight) report frame
                    | RendererBackend.Pixi ->
                        PixiSurface.mountInteractive container (PixiSurface.defaultOptions name boardWidth boardHeight) report frame
                    | RendererBackend.Babylon ->
                        BabylonSurface.mountInteractive container (BabylonSurface.defaultOptions name boardWidth boardHeight) report frame

                match mounted with
                | Error reason -> check (name + ": mounts") false reason
                | Ok mount ->
                    let! wrong = drawnMismatches container boardWidth boardHeight backend (fun () -> mount.Present frame) wanted 600

                    check
                        (sprintf "%s: the nodes' edges and the empty background are drawn where the scene declares them (%d pixels)" name wanted.Length)
                        wrong.IsEmpty
                        (sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; "))

                    let misses = ResizeArray<string>()

                    for label, x, y in presses do
                        mount.Present frame
                        requests.Clear()
                        do! BrowserConformance.pressBoard container boardWidth boardHeight backend { X = x; Y = y }

                        let expected =
                            match SceneInteraction.hitTest { X = x; Y = y } objects with
                            | Some id -> [ SelectionRequest.Select id ]
                            | None -> []

                        if List.ofSeq requests <> expected then
                            misses.Add(sprintf "%s at (%g, %g): asked %A, the hit test says %A" label x y (List.ofSeq requests) expected)

                    check
                        (sprintf "%s: real presses just inside and outside object edges select what the hit test says (%d presses)" name presses.Length)
                        (misses.Count = 0)
                        (String.concat "; " (Seq.truncate 4 misses))

                    mount.Dispose()

                style.remove ()
                container.remove ()

        do! BrowserConformance.deviceScale 1.0
    }

/// FS.GG.Game#753, review N3: nodes that overhang the canvas edge (FourD's clipped cell) are read at the canvas edge,
/// and every pixel nothing is drawn on is the clear colour, so a node drawn larger than declared is caught. On each
/// backend: one node past the right edge, one past the left edge, one alone in the open.
let runOverhang () : Async<unit> =
    async {
        let width = 200.0
        let height = 100.0

        let scene: Scene =
            {
                Nodes =
                    [
                        SceneNode.Rectangle((150.0, 20.0, 80.0, 40.0), fixtureColor 200uy 40uy 90uy)
                        SceneNode.Rectangle((-20.0, 60.0, 60.0, 30.0), fixtureColor 40uy 200uy 90uy)
                        SceneNode.Rectangle((80.0, 10.0, 30.0, 30.0), fixtureColor 230uy 200uy 40uy)
                    ]
            }

        let wanted = ScenePixels.expectations width height scene

        for backend in RendererBackend.all do
            let name = sprintf "overhang %gx%g, %s" width height (RendererBackend.id backend)
            let container = document.createElement "div"
            document.body.appendChild container |> ignore

            let mounted =
                match backend with
                | RendererBackend.Svg -> SvgSurface.mountScene container (SvgSurface.defaultOptions name width height) scene
                | RendererBackend.Pixi -> PixiSurface.mountScene container (PixiSurface.defaultOptions name width height) scene
                | RendererBackend.Babylon -> BabylonSurface.mountScene container (BabylonSurface.defaultOptions name width height) scene

            match mounted with
            | Error reason -> check (name + ": mounts") false reason
            | Ok mount ->
                let! wrong = drawnMismatches container width height backend (fun () -> mount.Present scene) wanted 600

                check
                    (sprintf "%s: nodes past the canvas edge are read at the edge and the empty background is the clear colour (%d pixels)" name wanted.Length)
                    (wrong.IsEmpty && wanted.Length > 12)
                    (sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; "))

                // Causal control: the same pixels read against a scene whose nodes are 6 units smaller on each side must
                // NOT match, so a backend that drew its nodes that much larger than declared would be caught.
                let smaller: Scene =
                    { Nodes =
                        scene.Nodes
                        |> List.map (fun node ->
                            match node with
                            | SceneNode.Rectangle((x, y, w, h), color) -> SceneNode.Rectangle((x + 6.0, y + 6.0, w - 12.0, h - 12.0), color)
                            | other -> other) }

                let! control =
                    drawnMismatches container width height backend (fun () -> mount.Present scene) (ScenePixels.expectations width height smaller) 0

                check
                    (sprintf "%s: control, pixels drawn 6 units larger than declared do not match the declaration" name)
                    (not control.IsEmpty)
                    "the smaller declaration matched, so an oversized node would pass"

                mount.Dispose()

            container.remove ()
    }

/// The page's request for a screenshot of `selector` named `name`. The driver writes it when RENDER_SMOKE_SCREENSHOTS
/// names a directory and answers whether it did; without the variable it writes nothing and answers false.
let screenshot (selector: string) (name: string) : Async<bool> =
    async {
        let pending: JS.Promise<bool> =
            emitJsExpr
                (createObj [ "kind" ==> "screenshot"; "selector" ==> selector; "name" ==> name ])
                "(window.__fsggInput ? window.__fsggInput($0) : Promise.resolve(false))"

        return! Async.AwaitPromise pending
    }

/// FS.GG.Game#789 (GAME-RENDER-02.4) proof: six experimental glyphs (unit, structure, waypoint, selection, focus,
/// status) in two themes, lowered to the portable primitive set, drawn on each backend at the pixels the kit's
/// `ScenePixels` model decides. A causal control reads the same pixels against the board with its themes swapped, which
/// must not match, so a backend that drew the theme tokens wrong (or ignored the glyphs) would be caught. Named points
/// (one per glyph part) are reported as INFO lines for the PR's pixel table; they are informational, not asserted (the
/// asserted pixels are the ones ScenePixels decides).
let runGlyphProof () : Async<unit> =
    async {
        let width = Experimental.Glyphs.proofWidth
        let height = Experimental.Glyphs.proofHeight
        let standard = Experimental.Glyphs.Theme.standard
        let safe = Experimental.Glyphs.Theme.colourBlindSafe
        let scene = Experimental.Glyphs.proofScene standard safe
        let swapped = Experimental.Glyphs.proofScene safe standard
        let wanted = ScenePixels.expectations width height scene

        let named =
            [
                for label, top, theme in [ "standard", 0.0, standard; "colour-blind-safe", 64.0, safe ] do
                    yield sprintf "%s unit body" label, { X = 32.5; Y = top + 32.5 }, theme.Body
                    yield sprintf "%s unit team-0 ring" label, { X = 42.5; Y = top + 32.5 }, Experimental.Glyphs.Theme.team theme 0
                    yield sprintf "%s selection bracket" label, { X = 19.5; Y = top + 16.5 }, theme.Selection
                    yield sprintf "%s status 25%% fill" label, { X = 22.5; Y = top + 9.5 }, theme.Bad
                    yield sprintf "%s status track" label, { X = 42.5; Y = top + 9.5 }, theme.Track
                    yield sprintf "%s status keyline" label, { X = 42.5; Y = top + 6.5 }, theme.Body
                    yield sprintf "%s structure body" label, { X = 96.5; Y = top + 30.5 }, theme.Body
                    yield sprintf "%s structure team-1 band" label, { X = 96.5; Y = top + 42.5 }, Experimental.Glyphs.Theme.team theme 1
                    yield sprintf "%s focus outline" label, { X = 96.5; Y = top + 16.5 }, theme.Focus
                    yield sprintf "%s unit team-2 ring" label, { X = 170.5; Y = top + 32.5 }, Experimental.Glyphs.Theme.team theme 2
                    yield sprintf "%s status 100%% fill" label, { X = 160.5; Y = top + 9.5 }, theme.Good
                    yield sprintf "%s waypoint" label, { X = 219.5; Y = top + 32.5 }, theme.Marker
                    yield sprintf "%s ground" label, { X = 128.5; Y = top + 60.5 }, theme.Ground
            ]

        info "glyph proof" (sprintf "%gx%g board, %d nodes, %d pixels decided by ScenePixels" width height scene.Nodes.Length wanted.Length)

        for backend in RendererBackend.all do
            let id = RendererBackend.id backend
            let name = sprintf "glyph proof (#789), %s" id
            let container = document.createElement "div"
            container.id <- sprintf "glyph-proof-%s" id
            container.setAttribute ("style", sprintf "width: %gpx; height: %gpx; line-height: 0; overflow: hidden" width height)
            document.body.appendChild container |> ignore

            let mounted =
                match backend with
                | RendererBackend.Svg -> SvgSurface.mountScene container (SvgSurface.defaultOptions name width height) scene
                | RendererBackend.Pixi -> PixiSurface.mountScene container (PixiSurface.defaultOptions name width height) scene
                | RendererBackend.Babylon -> BabylonSurface.mountScene container (BabylonSurface.defaultOptions name width height) scene

            match mounted with
            | Error reason -> check (name + ": mounts") false reason
            | Ok mount ->
                let! wrong = drawnMismatches container width height backend (fun () -> mount.Present scene) wanted 600

                check
                    (sprintf "%s: every pixel ScenePixels decides for the six glyphs in both themes is drawn (%d pixels)" name wanted.Length)
                    (wrong.IsEmpty && wanted.Length > 150)
                    (sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; "))

                let! control =
                    drawnMismatches container width height backend (fun () -> mount.Present scene) (ScenePixels.expectations width height swapped) 0

                check
                    (sprintf "%s: control, the same pixels read against the board with its themes swapped do not match" name)
                    (control.Length > 20)
                    (sprintf "only %d differ" control.Length)

                let! shown = BrowserConformance.drawnPixels container width height backend (named |> List.map (fun (_, p, _) -> p))

                for (label, point, colour), got in List.zip named shown do
                    let want, tolerance = rgbOf colour, pixelTolerance

                    let verdict =
                        match got with
                        | Some(r, g, b) when near want [| r; g; b |] tolerance -> "ok"
                        | _ -> "DIFF"

                    info
                        (sprintf "glyph pixel %s %s" id label)
                        (sprintf "(%g, %g) scene %A want %A got %A %s" point.X point.Y (rgbOf colour) want got verdict)

                let! wrote = screenshot (sprintf "#%s %s" container.id (if backend = RendererBackend.Svg then "svg" else "canvas")) (sprintf "glyphs-%s.png" id)
                info (sprintf "glyph screenshot %s" id) (if wrote then sprintf "glyphs-%s.png" id else "not requested")
                mount.Dispose()

            container.remove ()
    }

/// FS.GG.Game#789, review R3 of #794: the five identity frames (ADR D1), monochrome so only shape counts, at the default
/// frame size (24 units). On each backend: a strip of all five draws every pixel ScenePixels decides; and each frame, drawn
/// alone, shows its own decided pixels while every other frame's decided pixels differ from it (20 ordered pairs), so a
/// backend that drew one identity's frame for another's would be caught. The pairs use the kit's ADR D6 corner and
/// quarter-edge samples (`ScenePixels.identityExpectations`): the middle-of-edge samples cannot see Ally's quarter-point
/// holes (review N1).
let runIdentityFrames () : Async<unit> =
    async {
        let ink = fixtureColor 225uy 228uy 232uy
        let ground = fixtureColor 34uy 40uy 49uy
        let size = 24.0
        let identities = Experimental.Glyphs.identities
        let stripWidth = 48.0 * float identities.Length
        let stripHeight = 48.0

        let strip: Scene =
            { Nodes =
                SceneNode.Rectangle((0.0, 0.0, stripWidth, stripHeight), ground)
                :: (identities
                    |> List.mapi (fun i identity ->
                        Experimental.Glyphs.identityFrame identity { X = 24.0 + 48.0 * float i; Y = 24.0 } size ink)
                    |> List.concat) }

        let single identity : Scene =
            { Nodes =
                SceneNode.Rectangle((0.0, 0.0, 48.0, 48.0), ground)
                :: Experimental.Glyphs.identityFrame identity { X = 24.0; Y = 24.0 } size ink }

        // The same frames with the team index badge drawn inside them (review M1 of #794): digit 8, the most cells.
        let badged identity : Scene =
            { Nodes =
                SceneNode.Rectangle((0.0, 0.0, 48.0, 48.0), ground)
                :: fst (Experimental.Glyphs.identityFrameWithBadge identity { X = 24.0; Y = 24.0 } size ink (fixtureColor 10uy 10uy 10uy) (Some 7)) }

        let mountOn backend (container: HTMLElement) name width height scene =
            match backend with
            | RendererBackend.Svg -> SvgSurface.mountScene container (SvgSurface.defaultOptions name width height) scene
            | RendererBackend.Pixi -> PixiSurface.mountScene container (PixiSurface.defaultOptions name width height) scene
            | RendererBackend.Babylon -> BabylonSurface.mountScene container (BabylonSurface.defaultOptions name width height) scene

        for backend in RendererBackend.all do
            let id = RendererBackend.id backend
            let name = sprintf "identity frames (#789), %s" id
            let container = document.createElement "div"
            container.id <- sprintf "identity-frames-%s" id
            container.setAttribute ("style", sprintf "width: %gpx; height: %gpx; line-height: 0; overflow: hidden" stripWidth stripHeight)
            document.body.appendChild container |> ignore
            let wanted = ScenePixels.expectations stripWidth stripHeight strip

            match mountOn backend container name stripWidth stripHeight strip with
            | Error reason -> check (name + ": mounts") false reason
            | Ok mount ->
                let! wrong = drawnMismatches container stripWidth stripHeight backend (fun () -> mount.Present strip) wanted 600

                check
                    (sprintf "%s: the strip of five frames draws every pixel ScenePixels decides (%d pixels)" name wanted.Length)
                    (wrong.IsEmpty && wanted.Length > 40)
                    (sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; "))

                let! wrote = screenshot (sprintf "#%s %s" container.id (if backend = RendererBackend.Svg then "svg" else "canvas")) (sprintf "frames-%s.png" id)
                info (sprintf "identity frames screenshot %s" id) (if wrote then sprintf "frames-%s.png" id else "not requested")
                mount.Dispose()

            container.remove ()

            for label, sceneOf in [ "", single; " with badges inside", badged ] do
                let misses = ResizeArray<string>()
                let pairs = ref 0

                for a in identities do
                    let host = document.createElement "div"
                    document.body.appendChild host |> ignore

                    match mountOn backend host (sprintf "%s %A" name a) 48.0 48.0 (sceneOf a) with
                    | Error reason -> misses.Add(sprintf "%A does not mount: %s" a reason)
                    | Ok mount ->
                        let! own = drawnMismatches host 48.0 48.0 backend (fun () -> mount.Present(sceneOf a)) (ScenePixels.identityExpectations 48.0 48.0 (sceneOf a)) 600

                        if not own.IsEmpty then
                            misses.Add(sprintf "%A does not show its own pixels: %s" a (own |> List.truncate 2 |> String.concat "; "))

                        for b in identities |> List.filter ((<>) a) do
                            pairs.Value <- pairs.Value + 1
                            let! other = drawnMismatches host 48.0 48.0 backend (fun () -> mount.Present(sceneOf a)) (ScenePixels.identityExpectations 48.0 48.0 (sceneOf b)) 0

                            if other.IsEmpty then
                                misses.Add(sprintf "%A drawn passes %A's decided pixels" a b)
                            else
                                info (sprintf "identity pair%s %s %A drawn, %A read" label id a b) (sprintf "%d differing decided pixels" other.Length)

                        mount.Dispose()

                    host.remove ()

                check
                    (sprintf "%s%s: each frame shows its own decided pixels and differs from every other frame's (%d ordered pairs)" name label pairs.Value)
                    (misses.Count = 0 && pairs.Value = 20)
                    (String.concat "; " (Seq.truncate 4 misses))
    }

/// FS.GG.Game#789, ADR D6: the kit's glyph section (`GlyphConformance.cases`), drawn by the library in FS.GG.Game.Render
/// on every backend. Each case presents its scenes in order (the theme switch presents A, B and A again), then every
/// pixel the last scene decides must be drawn, and every control (another identity, another digit, the board drawn
/// bodies first, B's pixels, the other team...) must fail at least one pixel. The controls are read only after the
/// subject's own pixels have settled.
let runGlyphSection () : Async<unit> =
    async {
        let cases = GlyphConformance.cases [ Theme.classic ]
        info "glyph section (#789)" (sprintf "%d cases, %d controls, %d decided pixels" cases.Length (cases |> List.sumBy (fun c -> c.Controls.Length)) (cases |> List.sumBy (fun c -> c.Expected.Length)))

        for backend in RendererBackend.all do
            let id = RendererBackend.id backend

            for case in cases do
                let name = sprintf "glyph section (#789), %s: %s" id case.Name
                let container = document.createElement "div"
                container.setAttribute ("style", sprintf "width: %gpx; height: %gpx; line-height: 0; overflow: hidden" case.Width case.Height)
                document.body.appendChild container |> ignore
                let first = List.head case.Scenes
                let last = List.last case.Scenes

                let mounted =
                    match backend with
                    | RendererBackend.Svg -> SvgSurface.mountScene container (SvgSurface.defaultOptions name case.Width case.Height) first
                    | RendererBackend.Pixi -> PixiSurface.mountScene container (PixiSurface.defaultOptions name case.Width case.Height) first
                    | RendererBackend.Babylon -> BabylonSurface.mountScene container (BabylonSurface.defaultOptions name case.Width case.Height) first

                match mounted with
                | Error reason -> check (name + ": mounts") false reason
                | Ok mount ->
                    for scene in List.tail case.Scenes do
                        mount.Present scene

                    let! wrong = drawnMismatches container case.Width case.Height backend (fun () -> mount.Present last) case.Expected 600
                    let failedControls = ResizeArray<string>()

                    if wrong.IsEmpty then
                        for controlName, control in case.Controls do
                            let! other = drawnMismatches container case.Width case.Height backend (fun () -> mount.Present last) control 0

                            if other.IsEmpty then
                                failedControls.Add controlName

                        // A drawn control: present the control scene and wait until it shows its own pixels, so the read
                        // is of the control and never of a stale frame, then require the modelled difference.
                        for controlName, scene, own, modelled in case.DrawnControls do
                            let! ownWrong = drawnMismatches container case.Width case.Height backend (fun () -> mount.Present scene) own 600

                            if not ownWrong.IsEmpty then
                                failedControls.Add(sprintf "%s (never showed its own pixels: %s)" controlName (ownWrong |> List.truncate 2 |> String.concat "; "))
                            else
                                let! differing = drawnMismatches container case.Width case.Height backend (fun () -> mount.Present scene) case.Expected 0
                                info (sprintf "glyph drawn control %s, %s: %s" id case.Name controlName) (sprintf "%d differing (modelled %d)" differing.Length modelled)

                                if differing.Length < modelled || modelled = 0 then
                                    failedControls.Add(sprintf "%s (%d differ, modelled %d)" controlName differing.Length modelled)

                    check
                        (sprintf "%s: every decided pixel is drawn (%d) and every control differs (%d, %d drawn)" name case.Expected.Length case.Controls.Length case.DrawnControls.Length)
                        (wrong.IsEmpty && failedControls.Count = 0 && not case.Expected.IsEmpty)
                        (if not wrong.IsEmpty then
                             sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; ")
                         else
                             sprintf "controls matched: %s" (String.concat ", " failedControls))

                    mount.Dispose()

                container.remove ()
    }

/// FS.GG.Game#789, ADR D6: a node the kit does not model but can bound leaves open only the pixels inside its bounds.
/// The scene draws two bars and the ground, then a stroked path, a stroked rectangle, a line, a mitred acute path, a
/// square-capped diagonal and a rectangle clip LAST, where a label
/// usually goes. Before D6 such a node undecided every pixel; now every pixel outside the three nodes' reach stays
/// decided, and each backend must draw it (Pixi and Babylon skip strokes until #773, SVG draws them, and none may draw
/// outside their reach). The causal control reads the same pixels against the bars moved 6 units, which must not match.
let runBoundedUnmodelled () : Async<unit> =
    async {
        let width, height = 160.0, 80.0
        let stroke (width: float) : Paint =
            {
                Fill = None
                Stroke = Some { Width = width; Cap = Butt; Join = Bevel; Miter = 4.0 }
                Opacity = 1.0
                Antialias = true
                BlendMode = SrcOver
                Shader = None
                ColorFilter = NoColorFilter
                MaskFilter = NoMaskFilter
                ImageFilter = NoImageFilter
                PathEffect = NoPathEffect
            }
        let ink = fixtureColor 255uy 255uy 255uy
        let strokeOf colour = { stroke 2.0 with Fill = Some colour }
        let bars dx : SceneNode list =
            [
                SceneNode.Rectangle((0.0, 0.0, width, height), fixtureColor 34uy 40uy 49uy)
                SceneNode.Rectangle((10.0 + dx, 10.0, 40.0, 24.0), fixtureColor 200uy 40uy 90uy)
                SceneNode.Rectangle((10.0 + dx, 46.0, 40.0, 24.0), fixtureColor 40uy 200uy 90uy)
            ]
        let late: SceneNode list =
            [
                SceneNode.Path(
                    { Commands = [ MoveTo { X = 90.0; Y = 12.0 }; LineTo { X = 110.0; Y = 12.0 }; LineTo { X = 100.0; Y = 30.0 }; Close ]; FillType = Winding },
                    strokeOf ink
                )
                SceneNode.PaintedRectangle({ X = 120.0; Y = 12.0; Width = 24.0; Height = 18.0 }, strokeOf ink)
                SceneNode.Line({ X = 90.0; Y = 60.0 }, { X = 140.0; Y = 60.0 }, strokeOf ink)
                // A mitred acute path: its join reaches the miter limit times the width past the vertex.
                SceneNode.Path(
                    { Commands = [ MoveTo { X = 60.0; Y = 40.0 }; LineTo { X = 80.0; Y = 42.0 }; LineTo { X = 60.0; Y = 44.0 } ]; FillType = Winding },
                    { strokeOf ink with Stroke = Some { Width = 2.0; Cap = Butt; Join = Miter; Miter = 4.0 } }
                )
                // A square-capped diagonal: its caps reach half the width past each end, diagonally.
                SceneNode.Line({ X = 150.0; Y = 40.0 }, { X = 156.0; Y = 50.0 }, { strokeOf ink with Stroke = Some { Width = 3.0; Cap = Square; Join = Bevel; Miter = 4.0 } })
                // A rectangle clip: whatever it holds draws only inside it.
                SceneNode.ClipNode(RectClip { X = 120.0; Y = 64.0; Width = 30.0; Height = 10.0 }, { Nodes = [ SceneNode.Rectangle((100.0, 50.0, 80.0, 40.0), ink) ] })
            ]
        let scene: Scene = { Nodes = bars 0.0 @ late }
        let moved: Scene = { Nodes = bars 6.0 @ late }
        let wanted = ScenePixels.expectations width height scene
        let labelled = ScenePixels.expectations width height { Nodes = bars 0.0 @ [ SceneNode.Text((100.0, 40.0), "x", ink) ] }
        info "bounded unmodelled (#789)" (sprintf "%d pixels decided with six bounded nodes drawn last, %d with a text node drawn last" wanted.Length labelled.Length)

        for backend in RendererBackend.all do
            let id = RendererBackend.id backend
            let name = sprintf "bounded unmodelled nodes (#789), %s" id
            let container = document.createElement "div"
            document.body.appendChild container |> ignore

            let mounted =
                match backend with
                | RendererBackend.Svg -> SvgSurface.mountScene container (SvgSurface.defaultOptions name width height) scene
                | RendererBackend.Pixi -> PixiSurface.mountScene container (PixiSurface.defaultOptions name width height) scene
                | RendererBackend.Babylon -> BabylonSurface.mountScene container (BabylonSurface.defaultOptions name width height) scene

            match mounted with
            | Error reason -> check (name + ": mounts") false reason
            | Ok mount ->
                let! wrong = drawnMismatches container width height backend (fun () -> mount.Present scene) wanted 600

                check
                    (sprintf "%s: a stroked path, rectangle and line, a mitred acute path, a square-capped diagonal and a rectangle clip drawn last leave every pixel outside their reach decided and drawn (%d pixels)" name wanted.Length)
                    (wrong.IsEmpty && wanted.Length > 20 && labelled.IsEmpty)
                    (sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; "))

                let! control = drawnMismatches container width height backend (fun () -> mount.Present scene) (ScenePixels.expectations width height moved) 0

                check
                    (sprintf "%s: control, the bars moved 6 units do not match" name)
                    (not control.IsEmpty)
                    "the moved declaration matched"

                mount.Dispose()

            container.remove ()
    }

/// FS.GG.Game#789, review N2 of #794: the nine index badges (ADR D1) at the minimum unit (2, so 10 units tall), and D9's
/// dense board. On each backend: a strip of the nine digits draws every decided pixel; each digit drawn alone differs
/// from every other digit's D6 samples (72 ordered pairs); the dense board, with amplifiers drawn before bodies, draws
/// every decided pixel; and the control, the same board with bodies drawn first, does not match it.
let runBadgesAndDenseBoard () : Async<unit> =
    async {
        let ink = fixtureColor 225uy 228uy 232uy
        let ground = fixtureColor 34uy 40uy 49uy
        let digits = [ 1..9 ]
        let stripWidth = 16.0 * float digits.Length
        let strip: Scene =
            { Nodes =
                SceneNode.Rectangle((0.0, 0.0, stripWidth, 16.0), ground)
                :: (digits |> List.collect (fun d -> Experimental.Glyphs.badge d { X = 16.0 * float (d - 1) + 5.0; Y = 3.0 } 2.0 ink)) }
        let single d : Scene =
            { Nodes = SceneNode.Rectangle((0.0, 0.0, 16.0, 16.0), ground) :: Experimental.Glyphs.badge d { X = 5.0; Y = 3.0 } 2.0 ink }
        let theme = Experimental.Glyphs.Theme.standard
        let board = Experimental.Glyphs.denseBoard theme 5 3 true
        let control = Experimental.Glyphs.denseBoard theme 5 3 false
        let boardWidth, boardHeight = 160.0, 104.0

        let mountOn backend (container: HTMLElement) name width height scene =
            match backend with
            | RendererBackend.Svg -> SvgSurface.mountScene container (SvgSurface.defaultOptions name width height) scene
            | RendererBackend.Pixi -> PixiSurface.mountScene container (PixiSurface.defaultOptions name width height) scene
            | RendererBackend.Babylon -> BabylonSurface.mountScene container (BabylonSurface.defaultOptions name width height) scene

        for backend in RendererBackend.all do
            let id = RendererBackend.id backend

            // The badge strip.
            let name = sprintf "index badges (#789), %s" id
            let container = document.createElement "div"
            container.id <- sprintf "badges-%s" id
            container.setAttribute ("style", sprintf "width: %gpx; height: 16px; line-height: 0; overflow: hidden" stripWidth)
            document.body.appendChild container |> ignore
            let wanted = ScenePixels.identityExpectations stripWidth 16.0 strip

            match mountOn backend container name stripWidth 16.0 strip with
            | Error reason -> check (name + ": mounts") false reason
            | Ok mount ->
                let! wrong = drawnMismatches container stripWidth 16.0 backend (fun () -> mount.Present strip) wanted 600
                check
                    (sprintf "%s: the strip of nine digits draws every decided pixel (%d pixels)" name wanted.Length)
                    (wrong.IsEmpty && wanted.Length > 100)
                    (sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; "))
                let! wrote = screenshot (sprintf "#%s %s" container.id (if backend = RendererBackend.Svg then "svg" else "canvas")) (sprintf "badges-%s.png" id)
                info (sprintf "badges screenshot %s" id) (if wrote then sprintf "badges-%s.png" id else "not requested")
                mount.Dispose()

            container.remove ()

            // Each digit against the other eight.
            let misses = ResizeArray<string>()
            let pairs = ref 0

            for a in digits do
                let host = document.createElement "div"
                document.body.appendChild host |> ignore

                match mountOn backend host (sprintf "%s %d" name a) 16.0 16.0 (single a) with
                | Error reason -> misses.Add(sprintf "%d does not mount: %s" a reason)
                | Ok mount ->
                    let! own = drawnMismatches host 16.0 16.0 backend (fun () -> mount.Present(single a)) (ScenePixels.identityExpectations 16.0 16.0 (single a)) 600
                    if not own.IsEmpty then misses.Add(sprintf "%d does not show its own pixels" a)

                    for b in digits |> List.filter ((<>) a) do
                        pairs.Value <- pairs.Value + 1
                        let! other = drawnMismatches host 16.0 16.0 backend (fun () -> mount.Present(single a)) (ScenePixels.identityExpectations 16.0 16.0 (single b)) 0
                        if other.IsEmpty then misses.Add(sprintf "%d drawn passes %d's samples" a b)

                    mount.Dispose()

                host.remove ()

            check
                (sprintf "%s: each digit shows its own samples and differs from every other digit's (%d ordered pairs)" name pairs.Value)
                (misses.Count = 0 && pairs.Value = 72)
                (String.concat "; " (Seq.truncate 4 misses))

            // The dense board.
            let name = sprintf "dense board (#789), %s" id
            let container = document.createElement "div"
            container.id <- sprintf "dense-board-%s" id
            container.setAttribute ("style", sprintf "width: %gpx; height: %gpx; line-height: 0; overflow: hidden" boardWidth boardHeight)
            document.body.appendChild container |> ignore
            let wanted = ScenePixels.expectations boardWidth boardHeight board

            match mountOn backend container name boardWidth boardHeight board with
            | Error reason -> check (name + ": mounts") false reason
            | Ok mount ->
                let! wrong = drawnMismatches container boardWidth boardHeight backend (fun () -> mount.Present board) wanted 600
                check
                    (sprintf "%s: with amplifiers drawn before bodies, every decided pixel is drawn, frames' edges included (%d pixels)" name wanted.Length)
                    (wrong.IsEmpty && wanted.Length > 150)
                    (sprintf "%d differ, e.g. %s" wrong.Length (wrong |> List.truncate 3 |> String.concat "; "))
                let! wrote = screenshot (sprintf "#%s %s" container.id (if backend = RendererBackend.Svg then "svg" else "canvas")) (sprintf "dense-%s.png" id)
                info (sprintf "dense board screenshot %s" id) (if wrote then sprintf "dense-%s.png" id else "not requested")
                mount.Present control
                let! controlled = drawnMismatches container boardWidth boardHeight backend (fun () -> mount.Present control) wanted 0
                check
                    (sprintf "%s: control, the same board with bodies drawn first does not match" name)
                    (not controlled.IsEmpty)
                    "bodies-first drew the same pixels, so the order is not proven"
                mount.Dispose()

            container.remove ()
    }

/// The channel scripts/test-render-browser-host.sh reads: one console line with the counts and the base64 report
/// (a real-time browser run, because a WebGL canvas needs compositor frames that a virtual-time run does not give).
[<Emit("console.log('RENDER-SMOKE-RESULT checks=' + $0 + ' failures=' + $1 + ' ' + btoa(unescape(encodeURIComponent($2))))")>]
let announce (checks: int) (failures: int) (report: string) : unit = jsNative

let report () =
    let text = String.concat "\n" (Seq.append results infos)
    let pre = document.createElement "pre"
    pre.id <- "smoke-result"
    pre.setAttribute ("data-failures", string failures)
    pre.setAttribute ("data-checks", string results.Count)
    pre.textContent <- text
    document.body.appendChild pre |> ignore
    document.body.setAttribute ("data-smoke", (if failures = 0 then "pass" else "fail"))
    announce results.Count failures text

async {
    try
        Probe.installGlHook ()
        run ()
        do! runBabylon ()
        do! runPixi ()
        do! runThreeBackends ()
        do! runConformance ()
        do! runBabylonScale 40 30 17
        do! runBabylonScale 100 50 97
        do! measureBabylon 10 10
        do! measureBabylon 40 30
        do! measureBabylon 100 50
        do! runScaledBoard ()
        do! runOverhang ()
        do! runGlyphProof ()
        do! runIdentityFrames ()
        do! runBadgesAndDenseBoard ()
        do! runBoundedUnmodelled ()
        do! runGlyphSection ()
        do! RetainedSurface.run check
        do! InputHostSmoke.run check

        // FS.GG.Game#786: the measured deviation behind the one pixel tolerance.
        for entry in deviation do
            info (sprintf "pixel deviation %s" entry.Key) (sprintf "%d (tolerance %d)" entry.Value pixelTolerance)
    with ex ->
        failures <- failures + 1
        results.Add("FAIL smoke raised: " + ex.Message)

    report ()
}
|> Async.StartImmediate
