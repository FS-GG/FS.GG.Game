module FS.GG.Game.Render.Browser.Smoke.Program

// GAME-RENDER-MULTI-01.4 (FS.GG.Game#730) browser smoke. Runs in a real browser (headless Chromium via
// scripts/test-render-browser-host.sh) and proves the binding-independent part of the renderer host:
// the SVG adapter renders through RendererSelection + RendererHost + the retained SvgBrowser surface, game
// state survives switching, an unavailable backend is refused without touching the DOM, and later adapters
// (Pixi/Babylon) can plug in through RendererRegistry.register and own/release real DOM resources.
//
// The "pixi" and "babylon" adapters below are PLUG-IN PROBES (a stub canvas / a refusing mount). They are
// NOT PixiJS or BabylonJS adapters and prove nothing about those engines: the three-backend render test
// stays owed until the Templates#808/#809 bindings exist.

open Browser.Dom
open Browser.Types
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
let mutable failures = 0

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

let report () =
    let pre = document.createElement "pre"
    pre.id <- "smoke-result"
    pre.setAttribute ("data-failures", string failures)
    pre.setAttribute ("data-checks", string results.Count)
    pre.textContent <- String.concat "\n" results
    document.body.appendChild pre |> ignore
    document.body.setAttribute ("data-smoke", (if failures = 0 then "pass" else "fail"))

try
    run ()
with ex ->
    failures <- failures + 1
    results.Add("FAIL smoke raised: " + ex.Message)

report ()
