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
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.UI.Scene

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

    /// Wrap `getContext` once so the page can count the WebGL contexts it created, and refuse contexts on demand.
    [<Emit("(() => { if (window.__glHook) return; const h = { calls: 0, limit: Infinity, records: [], blocked: new Set() }; window.__glHook = h; const p = HTMLCanvasElement.prototype; const o = p.getContext; p.getContext = function (type, ...rest) { h.calls++; if (h.calls > h.limit || h.blocked.has(this)) return null; const c = o.call(this, type, ...rest); if (c && /webgl/.test(String(type))) h.records.push(c); return c } })()")>]
    let installGlHook () : unit = jsNative

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

        // A face toward the camera is lit at 3/4 by the tilted hemispheric light, with no specular highlight.
        check
            "the token pixel is the token colour, lit (about 200,40,90 x 0.75)"
            (near [| 150; 30; 68 |] tokenPixel 4)
            (sprintf "%A" tokenPixel)

        check
            "the background pixel is the background colour, lit (about 10,20,30 x 0.75)"
            (near [| 8; 15; 23 |] backgroundPixel 4)
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

        check "the token pixel followed the state" (near [| 150; 30; 68 |] moved 4) (sprintf "%A" moved)
        check "the vacated pixel is background again" (near [| 8; 15; 23 |] vacated 4) (sprintf "%A" vacated)

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

                if near [| 150; 30; 68 |] pixel 4 then
                    renderedRight.Value <- renderedRight.Value + 1

                check
                    (name + ": the babylon token pixel is lit as expected (150,30,68)")
                    (near [| 150; 30; 68 |] pixel 4)
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
    with ex ->
        failures <- failures + 1
        results.Add("FAIL smoke raised: " + ex.Message)

    report ()
}
|> Async.StartImmediate
