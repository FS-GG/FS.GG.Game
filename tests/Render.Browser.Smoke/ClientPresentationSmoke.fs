module FS.GG.Game.Render.Browser.Smoke.ClientPresentationSmoke

// EXPERIMENTAL proof (FS.GG.Game#810, GAME-RENDER-02.9), not shipped: ReferenceGame's planned move on the real SVG, Pixi
// and Babylon backends with REAL input (Playwright's trusted mouse and keyboard). A press-drag-release from the token
// draws a move arrow through the F# guest's `Local`s; Enter commits it and submits `MoveTo`; the arrow follows the
// pending command (the network is held so "pending" is observable) and clears when the authoritative snapshot shows
// the move. A move into the wall is refused, stays drawn as refused (in a cross, in the invalid ink), survives a backend
// switch, and Escape clears it. Selection is reflected in the mirror (`aria-selected`) and every plan phase change is
// announced once in a polite live region.
//
// Drag and release are not `InputAction`s yet (#788's union is closed; continuous input is a MAJOR change), so a
// game-owned listener on the container feeds them as `PipelineInput.Game`, coalesced to one move per animation frame.

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.Game.Reference
open FS.GG.Game.Reference.ExperimentalClientPresentation
open FS.GG.Game.Reference.ExperimentalPlanGame

[<Emit("setTimeout($1, $0)")>]
let private after (_milliseconds: int) (_action: unit -> unit) : unit = jsNative

[<Emit("requestAnimationFrame($0)")>]
let private onFrame (_action: float -> unit) : float = jsNative

[<Emit("cancelAnimationFrame($0)")>]
let private cancelFrame (_handle: float) : unit = jsNative

[<Emit("$0.scrollIntoView({ block: 'center', inline: 'center' })")>]
let private scrollIntoView (_element: Element) : unit = jsNative

[<Emit("$0.addEventListener($1, $2, true)")>]
let private listenCapture (_element: HTMLElement) (_name: string) (_listener: Event -> unit) : unit = jsNative

[<Emit("$0.removeEventListener($1, $2, true)")>]
let private unlistenCapture (_element: HTMLElement) (_name: string) (_listener: Event -> unit) : unit = jsNative

[<Emit("(window.__fsggInput ? window.__fsggInput($0) : Promise.reject(new Error('the smoke needs the real-input driver')))")>]
let private realInput (_request: obj) : JS.Promise<unit> = jsNative

let private nextFrame: Async<unit> =
    Async.FromContinuations(fun (continuation, _, _) -> onFrame (fun _ -> continuation ()) |> ignore)

let private start: ReferenceState.Model =
    {
        Screen = ReferenceState.Playing
        Room = ReferenceState.Atrium
        X = 1
        DestinationActive = false
        CameraTransition = false
        VaultClear = false
        Aim = 200, 40
        Tick = 0
        Selected = None
    }

let private sceneWidth = ReferenceRenderers.width
let private sceneHeight = ReferenceRenderers.height

let private surfaceOf (container: HTMLElement) (backend: RendererBackend) : HTMLElement option =
    let selector =
        match backend with
        | RendererBackend.Svg -> "svg[data-renderer=svg]"
        | other -> sprintf "canvas[data-renderer=%s]" (RendererBackend.id other)

    match container.querySelector selector with
    | null -> None
    | element -> Some(unbox<HTMLElement> element)

let private scenePoint (surface: HTMLElement) (clientX: float) (clientY: float) : Point =
    let bounds = surface.getBoundingClientRect ()

    {
        X = (clientX - bounds.left) * sceneWidth / max 1.0 bounds.width
        Y = (clientY - bounds.top) * sceneHeight / max 1.0 bounds.height
    }

/// The game-owned drag listener: capture phase on the container (it runs before the surface's input host, which may stop
/// a press there), so it only observes. A drag starts after 4 CSS px of travel; moves are coalesced to one `DragTo` per
/// animation frame; every release reports whether it dragged. Returns its disposer.
let private attachDrag (container: HTMLElement) (surface: unit -> HTMLElement option) (feed: PlanInput -> unit) : unit -> unit =
    let mutable down: (float * float * float) option = None
    let mutable dragged = false
    let mutable latest: Point option = None
    let mutable frame: float option = None

    let within (raw: Event) =
        match surface () with
        | Some element -> element.contains (unbox<Node> raw.target)
        | None -> false

    let onDown (raw: Event) =
        let pointer = raw :?> PointerEvent

        if pointer.button = 0.0 && within raw then
            down <- Some(pointer.pointerId, pointer.clientX, pointer.clientY)
            dragged <- false

    let onMove (raw: Event) =
        let pointer = raw :?> PointerEvent

        match down, surface () with
        | Some(id, x0, y0), Some element when id = pointer.pointerId ->
            let dx = pointer.clientX - x0
            let dy = pointer.clientY - y0

            if not dragged && dx * dx + dy * dy >= 16.0 then
                dragged <- true

            if dragged then
                latest <- Some(scenePoint element pointer.clientX pointer.clientY)

                if frame.IsNone then
                    frame <-
                        Some(
                            onFrame (fun _ ->
                                frame <- None
                                latest |> Option.iter (DragTo >> feed))
                        )
        | _ -> ()

    let onUp (raw: Event) =
        let pointer = raw :?> PointerEvent

        match down, surface () with
        | Some(id, _, _), Some element when id = pointer.pointerId ->
            frame |> Option.iter cancelFrame
            frame <- None
            down <- None
            feed (Release(scenePoint element pointer.clientX pointer.clientY, dragged))
        | _ -> down <- None

    listenCapture container "pointerdown" onDown
    listenCapture container "pointermove" onMove
    listenCapture container "pointerup" onUp

    fun () ->
        unlistenCapture container "pointerdown" onDown
        unlistenCapture container "pointermove" onMove
        unlistenCapture container "pointerup" onUp

/// A real press-drag-release (Playwright's mouse) on `backend`'s surface between two scene points.
let private dragOn (container: HTMLElement) (backend: RendererBackend) (from: Point) (target: Point) : Async<unit> =
    async {
        match surfaceOf container backend with
        | None -> failwithf "no %s surface" (RendererBackend.id backend)
        | Some element ->
            scrollIntoView element
            let bounds = element.getBoundingClientRect ()
            let cx (p: Point) = bounds.left + p.X * bounds.width / sceneWidth
            let cy (p: Point) = bounds.top + p.Y * bounds.height / sceneHeight

            do!
                realInput (
                    createObj
                        [
                            "kind" ==> "drag"
                            "x0" ==> cx from
                            "y0" ==> cy from
                            "x1" ==> cx target
                            "y1" ==> cy target
                            "steps" ==> 8
                        ]
                )
                |> Async.AwaitPromise
    }

type private Ink =
    | MoveInk
    | InvalidInk
    | Ground

/// What a pixel shows, robust to Babylon's lit 0.75 factor: the move ink is green, the invalid ink red, the Atrium blue.
let private inkOf (r: int, g: int, b: int) =
    if g > r + 40 && g > b then Some MoveInk
    elif r > g + 40 && r > b then Some InvalidInk
    elif b > r && b > g then Some Ground
    else None

let run (check: string -> bool -> string -> unit) (info: string -> string -> unit) : Async<unit> =
    async {
        info "client presentation" "EXPERIMENTAL proof (FS.GG.Game#810): not shipped"

        for backend in RendererBackend.all do
            let name = RendererBackend.id backend
            let container = document.createElement "div"
            container.id <- "stage-client-presentation-" + name
            document.body.appendChild container |> ignore
            let late = BrowserRenderers.route ()
            let options = BrowserRenderers.options "Planned move" sceneWidth sceneHeight
            let route = InputRoute.create ()

            // The polite live region the presentation's announcements go to, and the plan summary it describes.
            let live = document.createElement "div"
            live.setAttribute ("role", "status")
            live.setAttribute ("aria-live", "polite")
            live.setAttribute ("data-plan-status", name)
            // Announcements must not shift the surface while trusted pointer coordinates are in flight.
            live.setAttribute ("style", "position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%);white-space:nowrap")
            container.appendChild live |> ignore
            let said = ResizeArray<string>()

            let registry =
                match BrowserRenderers.surfaces container options late with
                | (svg :: _) as all ->
                    declare (SceneSurface.mountScene svg) all
                    |> SceneRenderers.withInput interaction route
                    |> SceneRenderers.registry
                | [] -> failwith "no surfaces"

            let initial: Client =
                {
                    Model = start
                    Presentation = ExperimentalClientPresentation.empty
                    Armed = false
                }

            let driver = BrowserRenderers.start late registry initial ignore

            if backend <> RendererBackend.Svg then
                driver.Dispatch(RendererMsg.SelectBackend backend)

            let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)

            // The network is held: each leg waits in `held` until the smoke flushes it.
            let held = ResizeArray<unit -> unit>()

            let rec flush () =
                if held.Count > 0 then
                    let next = held.[0]
                    held.RemoveAt 0
                    next ()
                    flush ()

            let sent = ResizeArray<CommandEnvelope<Command>>()

            let session =
                new Session(
                    "player",
                    start,
                    8,
                    route,
                    held.Add,
                    after,
                    (fun client -> driver.Dispatch(RendererMsg.StateChanged client)),
                    sent.Add,
                    (fun text ->
                        said.Add text
                        live.textContent <- text)
                )

            let mounted () =
                match driver.Mounted with
                | Some current -> surfaceOf container current
                | None -> None

            let detach = attachDrag container mounted session.Input

            let phases () =
                session.Presentation.Plans |> List.map (fun entry -> entry.Phase)

            /// Present the shown state again and read the pixels at `points`, frame by frame until `accept` holds.
            let pixels (on: RendererBackend) (points: Point list) (accept: Ink option list -> bool) =
                async {
                    let mutable last: Ink option list = []
                    let mutable frames = 0
                    let mutable ok = false

                    while not ok && frames < 120 do
                        driver.Dispatch(RendererMsg.StateChanged session.Shown)
                        let! read = BrowserConformance.drawnPixels container sceneWidth sceneHeight on points
                        last <- read |> List.map (Option.bind inkOf)
                        ok <- accept last

                        if not ok then
                            do! nextFrame
                            frames <- frames + 1

                    return ok, last
                }

            let token = tokenRect start
            let tokenCentre = { X = token.X + token.Width / 2.0; Y = token.Y + token.Height / 2.0 }

            // 1. Press-drag-release from the token: the guest's Locals select it and draw a draft arrow, sending nothing.
            do! dragOn container backend tokenCentre { X = 156.0; Y = 60.0 }

            let! drafted =
                BrowserConformance.waitUntil 300 (fun () -> phases () = [ Draft ] && not session.Shown.Armed)

            let arrowEnd =
                session.Presentation.Plans
                |> List.tryPick (fun entry ->
                    entry.Elements
                    |> List.tryPick (fun element ->
                        match element with
                        | Arrow(AtObject "token", AtPoint p, Move) -> Some p
                        | _ -> None))

            check
                (sprintf "client presentation %s: a real press-drag-release from the token draws a draft move arrow through the guest's Locals, and selects the token, with no command sent" name)
                (drafted
                 && session.Presentation.Primary = Some "token"
                 && sent.Count = 0
                 && (match arrowEnd with
                     | Some p -> abs (p.X - 156.0) <= 1.0 && abs (p.Y - 60.0) <= 1.0
                     | None -> false))
                (sprintf "phases=%A primary=%A sent=%d end=%A customs=%A" (phases ()) session.Presentation.Primary sent.Count arrowEnd session.Customs)

            let selectedItem =
                container.querySelector (sprintf "[data-scene-controls][data-renderer=%s] [aria-selected=true]" name)

            check
                (sprintf "client presentation %s: the guest's selection is reflected in the mirror (aria-selected on the Player token)" name)
                (not (isNull selectedItem)
                 && (selectedItem.textContent |> Option.ofObj |> Option.exists (fun label -> label.Contains "Player token")))
                (if isNull selectedItem then "no selected item" else (unbox<HTMLElement> selectedItem).outerHTML)

            // On the shaft, sample 6 (x=98) is drawn in every phase and sample 7 (x=102) only once submitted.
            let onShaft = { X = 98.0; Y = 60.0 }
            let offDraft = { X = 102.0; Y = 60.0 }

            let! draftOk, draftInks =
                pixels backend [ onShaft; offDraft ] (fun inks -> inks = [ Some MoveInk; Some Ground ])

            check
                (sprintf "client presentation %s: the draft arrow is drawn in the plan pass, dotted (the draft pattern)" name)
                draftOk
                (sprintf "inks at x=98,102: %A" draftInks)

            // 2. Enter submits it: the plan follows the pending command, drawn in the submitted pattern.
            do! BrowserConformance.key "Enter"
            let! submitted = BrowserConformance.waitUntil 300 (fun () -> sent.Count = 1 && phases () = [ Pending ])

            let! pendingOk, pendingInks =
                pixels backend [ onShaft; offDraft ] (fun inks -> inks = [ Some MoveInk; Some MoveInk ])

            check
                (sprintf "client presentation %s: Enter commits the plan and submits MoveTo; while the command is pending the arrow follows it in the submitted pattern" name)
                (submitted
                 && pendingOk
                 && session.Presentation.Plans.Head.Command = Some(CommandEnvelope.id sent.[0])
                 && session.Shown.Model.X = 1)
                (sprintf "sent=%A phases=%A inks=%A X=%d" (sent |> Seq.map (CommandEnvelope.command >> commandText) |> List.ofSeq) (phases ()) pendingInks session.Shown.Model.X)

            // 3. The authority answers; the snapshot shows the move and the arrow clears.
            flush ()
            let! settled = BrowserConformance.waitUntil 300 (fun () -> session.Shown.Model.X = 4 && phases () = [])
            let! clearedOk, clearedInks = pixels backend [ onShaft ] (fun inks -> inks = [ Some Ground ])

            check
                (sprintf "client presentation %s: on settlement the snapshot shows the token at column 4 and the arrow is gone" name)
                (settled && clearedOk && session.Failures.IsEmpty)
                (sprintf "X=%d phases=%A inks=%A failures=%A" session.Shown.Model.X (phases ()) clearedInks (session.Failures |> List.map PipelineFailure.text))

            // 4. A move into the wall is refused, and stays drawn as refused (the invalid ink) until the player clears it.
            let moved = tokenRect session.Shown.Model
            let movedCentre = { X = moved.X + moved.Width / 2.0; Y = moved.Y + moved.Height / 2.0 }
            do! dragOn container backend movedCentre { X = 28.0; Y = 60.0 }
            let! _ = BrowserConformance.waitUntil 300 (fun () -> phases () = [ Draft ] && not session.Shown.Armed)
            do! BrowserConformance.key "Enter"
            let! _ = BrowserConformance.waitUntil 300 (fun () -> sent.Count = 2)
            flush ()
            let! refused = BrowserConformance.waitUntil 300 (fun () -> phases () = [ Refused "blocked" ])
            let onRefused = { X = 118.0; Y = 60.0 }
            let! refusedOk, refusedInks = pixels backend [ onRefused ] (fun inks -> inks = [ Some InvalidInk ])

            check
                (sprintf "client presentation %s: a MoveTo into the wall is refused; the plan stays drawn in the invalid ink and the token does not move" name)
                (refused && refusedOk && session.Shown.Model.X = 4)
                (sprintf "phases=%A inks=%A X=%d" (phases ()) refusedInks session.Shown.Model.X)

            // 5. The plan is client state, so it survives a backend switch (SVG -> Pixi -> SVG, on the SVG run).
            if backend = RendererBackend.Svg then
                driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
                let! onPixi = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some RendererBackend.Pixi)
                let! pixiOk, pixiInks = pixels RendererBackend.Pixi [ onRefused ] (fun inks -> inks = [ Some InvalidInk ])
                let kept = phases () = [ Refused "blocked" ] && session.Presentation.Primary = Some "token"
                driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Svg)
                let! back = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some RendererBackend.Svg)

                check
                    "client presentation svg: the refused plan and the selection survive a switch to Pixi and back, and Pixi draws the plan"
                    (onPixi && pixiOk && kept && back)
                    (sprintf "pixi=%b inks=%A kept=%b back=%b" onPixi pixiInks kept back)

                // The switch replaced the focused surface; give the new one focus (focus only, no input).
                mounted () |> Option.iter (fun element -> element.focus ())

            // 6. Escape (the guest's Cancel) clears the refused plan, keeps the selection, and announces it.
            do! BrowserConformance.key "Escape"
            let! escaped = BrowserConformance.waitUntil 300 (fun () -> phases () = [])
            let! escapedOk, escapedInks = pixels backend [ onRefused ] (fun inks -> inks = [ Some Ground ])

            check
                (sprintf "client presentation %s: Escape clears the refused plan and keeps the selection" name)
                (escaped && escapedOk && session.Presentation.Primary = Some "token")
                (sprintf "phases=%A inks=%A primary=%A" (phases ()) escapedInks session.Presentation.Primary)

            let expected =
                [
                    "Plan move drafted"
                    "Plan move sent"
                    "Plan move done"
                    "Plan move drafted"
                    "Plan move sent"
                    "Plan refused: blocked"
                    "Plans cleared"
                ]

            check
                (sprintf "client presentation %s: each plan phase change is announced once in the polite live region, never per drag move" name)
                (List.ofSeq said = expected
                 && live.textContent = "Plans cleared"
                 && live.getAttribute "aria-live" = "polite")
                (sprintf "said=%A live=%s" (List.ofSeq said) live.textContent)

            info
                (sprintf "client presentation %s" name)
                (sprintf "commands=%A" (sent |> Seq.map (CommandEnvelope.command >> commandText) |> List.ofSeq))

            detach ()
            session.Dispose()
            (driver :> System.IDisposable).Dispose()
            container.remove ()
    }

/// Every plan element kind, in several phases, at fixed positions: the SVG-vs-Pixi equivalence scene (Root's binding
/// requirement on FS-GG/.github#5218: SVG must stand in for Pixi in documentation).
let private equivalenceScene: Client =
    let entry order phase elements =
        {
            Id = PlanId(sprintf "e%d" order)
            Elements = elements
            Phase = phase
            Settle = ClearWhenShown
            Command = None
            Order = uint64 order
        }

    let at x y : Point = { X = x; Y = y }

    {
        Model = start
        Armed = false
        Presentation =
            { ExperimentalClientPresentation.empty with
                Plans =
                    [
                        entry 1 Draft [ Arrow(AtObject "token", AtPoint(at 150.0 22.0), Move) ]
                        entry 2 Pending [ Path([ AtPoint(at 10.0 108.0); AtPoint(at 60.0 108.0); AtPoint(at 104.0 96.0) ], Attack) ]
                        entry
                            3
                            Draft
                            [
                                GhostAt
                                    {
                                        Family = UnitGhost
                                        Identity = Own
                                        Icon = None
                                        Team = None
                                        Anchor = at 214.0 90.0
                                        Footprint = { Width = 1; Height = 1 }
                                        Valid = false
                                    }
                            ]
                        entry 4 Applied [ Area(Circle(at 186.0 26.0, 12.0), Support) ]
                        entry 5 (Refused "blocked") [ TargetLine(AtPoint(at 112.0 10.0), AtPoint(at 150.0 50.0)) ]
                        entry 6 Draft [ Area(Box { X = 120.0; Y = 84.0; Width = 40.0; Height = 24.0 }, Move) ]
                        entry 7 Draft [ Label(AtPoint(at 28.0 18.0), "Rally") ]
                    ]
            }
    }

/// The colour the scene's rectangles decide at the pixel containing `p` (the last rectangle drawn over it wins), or
/// `None` when a non-rectangle node's bounds reach it (the aim marker), so only decided pixels are compared.
let private decided (nodes: SceneNode list) (p: Point) : (int * int * int) option =
    let px, py = floor p.X + 0.5, floor p.Y + 0.5

    let undecided =
        nodes
        |> List.exists (fun node ->
            match node with
            | SceneNode.Circle(c, r, _) -> abs (px - c.X) <= r + 1.0 && abs (py - c.Y) <= r + 1.0
            | SceneNode.Rectangle _ -> false
            | _ -> true)

    if undecided then
        None
    else
        nodes
        |> List.fold
            (fun found node ->
                match node with
                | SceneNode.Rectangle((x, y, w, h), fill) when px >= x && px < x + w && py >= y && py < y + h ->
                    Some(int fill.Red, int fill.Green, int fill.Blue)
                | _ -> found)
            None

let runEquivalence (check: string -> bool -> string -> unit) (info: string -> string -> unit) : Async<unit> =
    async {
        let container = document.createElement "div"
        container.id <- "stage-client-presentation-equivalence"
        document.body.appendChild container |> ignore
        let late = BrowserRenderers.route ()
        let options = BrowserRenderers.options "Plan equivalence" sceneWidth sceneHeight

        let registry =
            match BrowserRenderers.surfaces container options late with
            | (svg :: _) as all -> declare (SceneSurface.mountScene svg) all |> SceneRenderers.registry
            | [] -> failwith "no surfaces"

        let driver = BrowserRenderers.start late registry equivalenceScene ignore
        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some RendererBackend.Svg)
        let nodes = (view equivalenceScene).Nodes
        // Exhaust the bounded 240 x 120 scene at pixel centres. Sampling only rectangle centres
        // and one keyline point cannot establish the promised agreement on every decided pixel.
        let samples =
            [ for y in 0 .. int sceneHeight - 1 do
                for x in 0 .. int sceneWidth - 1 do
                    { X = float x + 0.5; Y = float y + 0.5 } ]
            |> List.choose (fun p -> decided nodes p |> Option.map (fun colour -> p, colour))

        let points = samples |> List.map fst

        let read (backend: RendererBackend) =
            async {
                driver.Dispatch(RendererMsg.StateChanged equivalenceScene)
                let! pixels = BrowserConformance.drawnPixels container sceneWidth sceneHeight backend points
                return pixels
            }

        let! svg = read RendererBackend.Svg
        driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some RendererBackend.Pixi)
        let mutable pixi = []
        let mutable frames = 0

        // A canvas is read in the task that presents; retry a few frames until Pixi has drawn its first frame.
        while frames < 120 && (pixi.IsEmpty || List.exists Option.isNone pixi || pixi = List.map (fun _ -> pixi.Head) pixi) do
            let! read' = read RendererBackend.Pixi
            pixi <- read'

            if List.exists Option.isNone pixi || pixi = List.map (fun _ -> pixi.Head) pixi then
                do! BrowserConformance.waitUntil 1 (fun () -> false) |> Async.Ignore
                frames <- frames + 1
            else
                frames <- 120

        let delta (a: int * int * int) (b: int * int * int) =
            let r1, g1, b1 = a
            let r2, g2, b2 = b
            List.max [ abs (r1 - r2); abs (g1 - g2); abs (b1 - b2) ]

        let rows = List.zip3 samples svg pixi

        let worstPair =
            rows |> List.map (fun (_, s, p) -> match s, p with | Some s, Some p -> delta s p | _ -> 999) |> List.fold max 0

        let worstSvg =
            rows |> List.map (fun ((_, expected), s, _) -> match s with | Some s -> delta s expected | None -> 999) |> List.fold max 0

        let kinds =
            equivalenceScene.Presentation.Plans
            |> List.filter (fun entry -> not (drawPlan proofInks (bounds equivalenceScene) entry).IsEmpty)
            |> List.length

        let tolerance = 0

        check
            "client presentation: SVG and Pixi draw every plan element kind equivalently (arrow, path, ghost, circle and box areas, target line, label marker; draft, pending, applied and refused), with zero channel tolerance on every decided pixel"
            (points.Length >= 100 && kinds = 7 && worstPair <= tolerance && worstSvg <= tolerance)
            (sprintf "samples=%d kinds=%d worst svg-vs-pixi=%d worst svg-vs-decided=%d" points.Length kinds worstPair worstSvg)

        info "client presentation equivalence" (sprintf "samples=%d worst svg-vs-pixi=%d worst svg-vs-decided=%d tolerance=%d" points.Length worstPair worstSvg tolerance)
        (driver :> System.IDisposable).Dispose()
        container.remove ()
    }
