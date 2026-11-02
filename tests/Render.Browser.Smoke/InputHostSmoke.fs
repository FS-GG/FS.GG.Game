module FS.GG.Game.Render.Browser.Smoke.InputHostSmoke

// FS.GG.Game#788 PR B: the input host of `SceneRenderers.withInput` on the real SVG, Pixi and Babylon backends, played
// with REAL input (the driver's trusted mouse and keyboard). Each backend must report the same `InputEvent`s for the
// same input, form ONE Tab stop that is a labelled listbox (or a grid once any cell group exists) naming the focused
// object's item with `aria-activedescendant`, own its items (`aria-owns` on the SVG root, fallback content in a
// canvas) without ever hiding them from assistive technology, contain the keys it reports, and release everything when
// switched away. The full `input` section of the conformance kit (I1-I16) is PR C.

open Browser.Dom
open Browser.Types
open Fable.Core
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.Game.Reference

[<Emit("$0.focus()")>]
let private focusElement (_element: Element) : unit = jsNative

[<Emit("document.activeElement === $0")>]
let private isActive (_element: Element) : bool = jsNative

[<Emit("$0.querySelector($1)")>]
let private find (_root: Element) (_selector: string) : Element = jsNative

[<Emit("Array.from($0.querySelectorAll($1))")>]
let private findAll (_root: Element) (_selector: string) : Element array = jsNative

[<Emit("Array.from(document.querySelectorAll('[tabindex]')).filter(e => e.tabIndex >= 0 && $0.contains(e)).length")>]
let private tabStops (_container: Element) : int = jsNative

[<Emit("(() => { const s = getComputedStyle($0); return s.display === 'none' || s.visibility === 'hidden' || $0.closest('[aria-hidden=true]') !== null })()")>]
let private hiddenFromAt (_element: Element) : bool = jsNative

[<Emit("(() => { const r = $0.getBoundingClientRect(); return [r.width, r.height] })()")>]
let private sizeOf (_element: Element) : float array = jsNative

/// A page-level listener (bubble phase) that counts the keys reaching the page.
[<Emit("(() => { window.__fsggPageKeys = []; document.addEventListener('keydown', e => window.__fsggPageKeys.push(e.key)) })()")>]
let private installPageKeys () : unit = jsNative

[<Emit("window.__fsggPageKeys.splice(0).join(',')")>]
let private takePageKeys () : string = jsNative

let private eventText (event: InputEvent) =
    let source =
        match InputEvent.source event with
        | InputSource.Pointer -> "pointer"
        | InputSource.Keyboard -> "keyboard"
        | InputSource.Mirror -> "mirror"

    let action =
        match InputEvent.action event with
        | InputAction.Activate -> "activate"
        | InputAction.ActivateSecondary -> "secondary"
        | InputAction.Hover -> "hover"
        | InputAction.Focus -> "focus"
        | InputAction.Cancel -> "cancel"

    sprintf "%s:%s:%s" source action (InputEvent.target event |> Option.defaultValue "-")

let private start: ReferenceState.Model =
    {
        Screen = ReferenceState.Playing
        Room = ReferenceState.Atrium
        X = 1
        DestinationActive = false
        CameraTransition = false
        VaultClear = false
        Aim = 200, 30
        Tick = 0
        Selected = None
    }

let private nextFrame: Async<unit> =
    Async.FromContinuations(fun (resolve, _, _) -> window.requestAnimationFrame (fun _ -> resolve ()) |> ignore)

let private settle frames =
    async {
        for _ in 1..frames do
            do! nextFrame
    }

/// Play one backend of `renderers` (a registry from `withInput`) and check it.
let private play
    (check: string -> bool -> string -> unit)
    (container: HTMLElement)
    (late: LateFailureRoute)
    (route: InputRoute)
    (registry: RendererRegistry<ReferenceState.Model>)
    (grid: bool)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let name = (RendererBackend.id backend) + (if grid then " grid" else "")
        let events = ResizeArray<InputEvent>()
        InputRoute.bind events.Add route
        let driver = BrowserRenderers.start late registry start ignore

        if backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend backend)

        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
        do! settle 3
        let selector = sprintf "[data-renderer=%s]:is(svg, canvas)" (RendererBackend.id backend)
        let surface = find container selector

        let role = surface.getAttribute "role"
        let wantRole = if grid then "grid" else "listbox"

        check
            (sprintf "input host %s: the surface is ONE labelled %s Tab stop" name wantRole)
            (role = wantRole && surface.getAttribute "aria-label" = "Input board" && surface.getAttribute "tabindex" = "0" && tabStops container = 1)
            (sprintf "role=%s label=%s tabindex=%s tabStops=%d" role (surface.getAttribute "aria-label") (surface.getAttribute "tabindex") (tabStops container))

        let itemRole = if grid then "gridcell" else "option"
        let items = findAll document.body (sprintf "[data-input-host^='fsgg-input-%s'] [role=%s]" (RendererBackend.id backend) itemRole)
        let itemIds = items |> Array.map (fun item -> item.getAttribute "data-scene-control-id") |> List.ofArray

        let owned =
            match backend with
            | RendererBackend.Svg ->
                let owns = (surface.getAttribute "aria-owns").Split(' ') |> Set.ofArray
                // Every item is owned: directly, or through the group or rowgroup the root owns.
                items
                |> Array.forall (fun item ->
                    owns.Contains(item.getAttribute "id")
                    || (let group = item.closest "[role=group],[role=rowgroup]"
                        group.IsSome && owns.Contains(group.Value.getAttribute "id")))
            | _ -> items |> Array.forall (fun item -> surface.contains item)

        check
            (sprintf "input host %s: one %s per keyboard-reachable object, owned by the surface, none hidden from assistive technology" name itemRole)
            (itemIds = [ "token"; "aim" ] && owned && items |> Array.forall (hiddenFromAt >> not))
            (sprintf "items=%A owned=%b hidden=%A" itemIds owned (items |> Array.map hiddenFromAt))

        if grid then
            let cells =
                items
                |> Array.map (fun item ->
                    sprintf
                        "%s r%s c%s"
                        (item.getAttribute "aria-label")
                        ((item.closest "[role=row]").Value.getAttribute "aria-rowindex")
                        (item.getAttribute "aria-colindex"))
                |> List.ofArray

            check
                (sprintf "input host %s: cells carry their group's name and their row and column indices" name)
                (cells = [ "Player token, Board r1 c1"; "Aim marker, Board r1 c3" ]
                 && surface.getAttribute "aria-rowcount" = "1"
                 && surface.getAttribute "aria-colcount" = "3")
                (sprintf "%A rowcount=%s colcount=%s" cells (surface.getAttribute "aria-rowcount") (surface.getAttribute "aria-colcount"))

        // A real primary press on the token: Activate from the pointer, on the token.
        events.Clear()
        let x, y, w, h = ReferenceRenderers.tokenBounds start
        do! BrowserConformance.press container backend { X = x + w / 2.0; Y = y + h / 2.0 }
        do! settle 1

        check
            (sprintf "input host %s: a real press on an object reports Activate from the pointer on it" name)
            (events |> Seq.map eventText |> Seq.contains "pointer:activate:token")
            (events |> Seq.map eventText |> String.concat ",")

        // Keyboard: focus, an arrow, Enter, Escape; the reported keys never reach the page.
        focusElement surface
        events.Clear()
        takePageKeys () |> ignore
        do! BrowserConformance.key "ArrowRight"
        do! BrowserConformance.key "Enter"
        do! BrowserConformance.key "Escape"
        do! BrowserConformance.key "x"
        do! settle 1
        let reported = events |> Seq.map eventText |> List.ofSeq
        let active = surface.getAttribute "aria-activedescendant"
        let activeItem = if isNull active then None else items |> Array.tryFind (fun item -> item.getAttribute "id" = active)

        check
            (sprintf "input host %s: arrows report Focus, Enter Activate and Escape Cancel from the keyboard, on the focused object" name)
            (reported = [ "keyboard:focus:aim"; "keyboard:activate:aim"; "keyboard:cancel:aim" ])
            (String.concat "," reported)

        check
            (sprintf "input host %s: aria-activedescendant names the focused object's item, and Escape keeps the focus on the surface" name)
            (activeItem |> Option.map (fun item -> item.getAttribute "data-scene-control-id") = Some "aim" && isActive surface)
            (sprintf "activedescendant=%s focused=%b" active (isActive surface))

        let pageKeys = takePageKeys ()

        check
            (sprintf "input host %s: the reported keys stop at the host; others reach the page" name)
            (pageKeys = "x")
            pageKeys

        // Disposal: switching away releases the host's mirror and its surface attributes.
        driver.Dispatch(RendererMsg.SelectBackend(if backend = RendererBackend.Svg then RendererBackend.Pixi else RendererBackend.Svg))
        do! settle 2
        let left = findAll document.body (sprintf "[data-input-host^='fsgg-input-%s']" (RendererBackend.id backend))

        check
            (sprintf "input host %s: switching away removes its mirror" name)
            (left.Length = 0)
            (string left.Length)

        (driver :> System.IDisposable).Dispose()
        InputRoute.unbind route
        do! settle 2
    }

let run (check: string -> bool -> string -> unit) : Async<unit> =
    async {
        installPageKeys ()
        let container = document.createElement "div"
        container.id <- "stage-input-host"
        document.body.appendChild container |> ignore
        let late = BrowserRenderers.route ()
        let options = BrowserRenderers.options "Input board" ReferenceRenderers.width ReferenceRenderers.height

        let declare (interaction: SceneInteraction<ReferenceState.Model>) (route: InputRoute) =
            match BrowserRenderers.surfaces container options late with
            | (svg :: _) as all ->
                ReferenceRenderers.declare (SceneSurface.mountScene svg) all
                |> SceneRenderers.withInput interaction route
                |> SceneRenderers.registry
            | [] -> failwith "no surfaces"

        let route = InputRoute.create ()
        let listbox = declare ReferenceRenderers.interaction route

        let cells =
            ReferenceRenderers.interaction
            |> SceneInteraction.withAccess (fun target ->
                if target.Id = "token" then ObjectAccess.cell "Board" 0 0 else ObjectAccess.cell "Board" 0 2)

        let grid = declare cells route

        for backend in RendererBackend.all do
            do! play check container late route listbox false backend

        for backend in RendererBackend.all do
            do! play check container late route grid true backend

        container.remove ()
    }
