module FS.GG.Game.Render.Browser.Smoke.InputHostSmoke

// FS.GG.Game#788 PR B: the input host of `SceneRenderers.withInput` on the real SVG, Pixi and Babylon backends, played
// with REAL input (the driver's trusted mouse, touch and keyboard). Each backend must report the same `InputEvent`s for
// the same input, form ONE Tab stop that is a labelled listbox (or a grid once any cell group exists) naming the focused
// object's item with `aria-activedescendant`, own its items (`aria-owns` on the SVG root, fallback content in a canvas)
// without ever hiding them from assistive technology, contain both the keydown and the keyup of every key it reports,
// leave Alt- and Meta-modified arrows to the browser, move on a grid as the ARIA Authoring Practices say, draw the focus
// ring's two tones at 1x and 2x CSS scale and at DPR 2, and release everything when switched away. The full `input`
// section of the conformance kit (I1-I16) is PR C.

open Browser.Dom
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open FS.GG.Game.Render
open FS.GG.Game.Render.Browser
open FS.GG.Game.Reference
open FS.GG.UI.Scene

[<Emit("$0.focus()")>]
let private focusElement (_element: Element) : unit = jsNative

[<Emit("document.activeElement === $0")>]
let private isActive (_element: Element) : bool = jsNative

[<Emit("$0.querySelector($1)")>]
let private find (_root: Element) (_selector: string) : Element = jsNative

[<Emit("Array.from($0.querySelectorAll($1))")>]
let private findAll (_root: Element) (_selector: string) : Element array = jsNative

/// The elements of `container` a user can Tab to: focusable (natively, or by a `tabindex`), `tabIndex` 0 or more, not
/// disabled or inert, and rendered (no `display:none` or `visibility:hidden` on it or an ancestor).
[<Emit("[$0, ...$0.querySelectorAll('*')].filter(e => { if (e.tabIndex < 0 || e.disabled || e.closest('[inert]')) return false; for (let n = e; n && n.nodeType === 1; n = n.parentElement) { const s = getComputedStyle(n); if (s.display === 'none' || s.visibility === 'hidden') return false } return e.hasAttribute('tabindex') || e.matches('a[href],button,input,select,textarea,summary,iframe,[contenteditable=\"\"],[contenteditable=true]') }).length")>]
let private tabStops (_container: Element) : int = jsNative

/// Hidden from assistive technology: `aria-hidden`, `hidden`, `display:none` or `visibility:hidden` on the element or
/// any ancestor.
[<Emit("(() => { for (let n = $0; n && n.nodeType === 1; n = n.parentElement) { const s = getComputedStyle(n); if (s.display === 'none' || s.visibility === 'hidden' || n.getAttribute('aria-hidden') === 'true' || n.hidden) return true } return false })()")>]
let private hiddenFromAt (_element: Element) : bool = jsNative

/// Page-level listeners (bubble phase) for every keydown and keyup, and a census of page context menus that opened (a
/// `contextmenu` nobody prevented).
[<Emit("(() => { if (!window.__fsggHostPage) { const p = { keys: [], menus: 0 }; window.__fsggHostPage = p; document.addEventListener('keydown', e => p.keys.push('down:' + e.key)); document.addEventListener('keyup', e => p.keys.push('up:' + e.key)); window.addEventListener('contextmenu', e => { if (!e.defaultPrevented) p.menus++ }) } })()")>]
let private installPage () : unit = jsNative

[<Emit("window.__fsggHostPage.keys.splice(0)")>]
let private takePageKeys () : string array = jsNative

[<Emit("window.__fsggHostPage.menus")>]
let private pageMenus () : int = jsNative

[<Emit("$0.click()")>]
let private clickElement (_element: Element) : unit = jsNative

[<Emit("$0.style.width = $1 + 'px', $0.style.height = $2 + 'px'")>]
let private styleSize (_element: Element) (_width: float) (_height: float) : unit = jsNative

[<Emit("$0.scrollIntoView({ block: 'center', inline: 'center' })")>]
let private scrollIntoView (_element: Element) : unit = jsNative

/// Decode a base64 PNG of a `clipWidth` x `clipHeight` CSS px region and read the pixel under each point (CSS px from the
/// region's top left), at whatever device scale the image has.
[<Emit("(async () => { const img = new Image(); img.src = 'data:image/png;base64,' + $0; await img.decode(); const c = document.createElement('canvas'); c.width = img.width; c.height = img.height; const g = c.getContext('2d'); g.drawImage(img, 0, 0); return $1.map(p => { const x = Math.floor(p[0] * img.width / $2), y = Math.floor(p[1] * img.height / $3); const d = g.getImageData(x, y, 1, 1).data; return [d[0], d[1], d[2]] }) })()")>]
let private decodeAt (_png: string) (_points: float[][]) (_clipWidth: float) (_clipHeight: float) : JS.Promise<int[][]> = jsNative

/// The PRESENTED pixels (what the page shows, at the device pixel ratio, through the driver's screenshot) of `surface`
/// at scene points of a `width` x `height` scene.
let private presentedPixels (surface: Element) (width: float) (height: float) (points: Point list) : Async<(int * int * int) list> =
    async {
        scrollIntoView surface
        let bounds = surface.getBoundingClientRect ()

        let request =
            createObj
                [
                    "kind" ==> "shot"
                    "x" ==> bounds.left
                    "y" ==> bounds.top
                    "width" ==> bounds.width
                    "height" ==> bounds.height
                ]

        let pending: JS.Promise<string> = emitJsExpr request "window.__fsggInput($0)"
        let! png = Async.AwaitPromise pending

        let at =
            points
            |> List.map (fun p -> [| p.X * bounds.width / width; p.Y * bounds.height / height |])
            |> Array.ofList

        let! read = Async.AwaitPromise(decodeAt png at bounds.width bounds.height)
        return read |> Array.map (fun p -> p.[0], p.[1], p.[2]) |> List.ofArray
    }

/// The focus ring around `b` as PRESENTED, from the middle of each edge: in the light inner band at 1.5 and 1.75 CSS px
/// out, in the dark outer band at 2.25 and 3.5. The samples a quarter CSS px either side of the bands' common edge see an
/// image drawn at a lower density and scaled up (it blends the two tones there); the others, the tones themselves.
let private ringTones (surface: Element) (width: float) (height: float) (b: Rect) : Async<bool * string> =
    async {
        let css = surface.getBoundingClientRect().width / width

        let around distance =
            let d = distance / css

            [
                { X = b.X - d; Y = b.Y + b.Height / 2.0 }
                { X = b.X + b.Width / 2.0; Y = b.Y - d }
                { X = b.X + b.Width + d; Y = b.Y + b.Height / 2.0 }
                { X = b.X + b.Width / 2.0; Y = b.Y + b.Height + d }
            ]

        let! inner = presentedPixels surface width height (around 1.5 @ around 1.75)
        let! outer = presentedPixels surface width height (around 2.25 @ around 3.5)
        let light = inner |> List.forall (fun (r, g, b) -> r >= 231 && g >= 231 && b >= 231)
        let dark = outer |> List.forall (fun (r, g, b) -> r <= 40 && g <= 40 && b <= 40)
        return light && dark, sprintf "css=%g dpr=%g inner=%A outer=%A" css window.devicePixelRatio inner outer
    }

/// A real input request of `kind` at the scene point `point` of `surface` (a scene of `width` x `height` units).
let private inputAt (surface: Element) (width: float) (height: float) (kind: string) (point: Point) : Async<unit> =
    let bounds = surface.getBoundingClientRect ()

    let request =
        createObj
            [
                "kind" ==> kind
                "x" ==> bounds.left + point.X * bounds.width / width
                "y" ==> bounds.top + point.Y * bounds.height / height
                "ms" ==> 600
            ]

    let pending: JS.Promise<unit> = emitJsExpr request "window.__fsggInput($0)"
    Async.AwaitPromise pending

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

let private texts (events: ResizeArray<InputEvent>) = events |> Seq.map eventText |> List.ofSeq

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

/// The keys the host reports, in the page's `down:`/`up:` notation, that a page must never see.
let private reportedKeys =
    [ "ArrowRight"; "ArrowLeft"; "ArrowUp"; "ArrowDown"; "Home"; "End"; "PageUp"; "PageDown"; "Enter"; " "; "Escape" ]
    |> List.collect (fun key -> [ "down:" + key; "up:" + key ])
    |> Set.ofList

let private surfaceOf (container: HTMLElement) (backend: RendererBackend) =
    find container (sprintf "[data-renderer=%s]:is(svg, canvas)" (RendererBackend.id backend))

let private itemsOf (backend: RendererBackend) (role: string) =
    findAll document.body (sprintf "[data-input-host^='fsgg-input-%s'] [role=%s]" (RendererBackend.id backend) role)

let private ownedBy (backend: RendererBackend) (surface: Element) (item: Element) =
    match backend with
    | RendererBackend.Svg ->
        let owns = (surface.getAttribute "aria-owns").Split(' ') |> Set.ofArray

        owns.Contains(item.getAttribute "id")
        || (let group = item.closest "[role=group],[role=rowgroup]"
            group.IsSome && owns.Contains(group.Value.getAttribute "id"))
    | _ -> surface.contains item

/// The reference game as a listbox: the structure, a press, the keyboard and key containment, and release.
let private playListbox
    (check: string -> bool -> string -> unit)
    (container: HTMLElement)
    (late: LateFailureRoute)
    (route: InputRoute)
    (registry: RendererRegistry<ReferenceState.Model>)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let name = RendererBackend.id backend
        let events = ResizeArray<InputEvent>()
        InputRoute.bind events.Add route
        let driver = BrowserRenderers.start late registry start ignore

        if backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend backend)

        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
        do! settle 3
        let surface = surfaceOf container backend

        check
            (sprintf "input host %s: the surface is ONE labelled listbox Tab stop" name)
            (surface.getAttribute "role" = "listbox" && surface.getAttribute "aria-label" = "Input board" && tabStops container = 1)
            (sprintf "role=%s label=%s tabStops=%d" (surface.getAttribute "role") (surface.getAttribute "aria-label") (tabStops container))

        let items = itemsOf backend "option"
        let itemIds = items |> Array.map (fun item -> item.getAttribute "data-scene-control-id") |> List.ofArray
        let owned = items |> Array.forall (ownedBy backend surface)

        check
            (sprintf "input host %s: one option per keyboard-reachable object, owned by the surface, none hidden from assistive technology (up the ancestor chain)" name)
            (itemIds = [ "token"; "aim" ] && owned && items |> Array.forall (hiddenFromAt >> not))
            (sprintf "items=%A owned=%b hidden=%A" itemIds owned (items |> Array.map hiddenFromAt))

        events.Clear()
        let x, y, w, h = ReferenceRenderers.tokenBounds start
        do! BrowserConformance.press container backend { X = x + w / 2.0; Y = y + h / 2.0 }

        check
            (sprintf "input host %s: a real press on an object reports Activate from the pointer on it" name)
            (texts events |> List.contains "pointer:activate:token")
            (String.concat "," (texts events))

        focusElement surface
        events.Clear()
        takePageKeys () |> ignore
        do! BrowserConformance.key "ArrowRight"
        do! BrowserConformance.key "Enter"
        do! BrowserConformance.key "Escape"
        do! BrowserConformance.key "x"
        let active = surface.getAttribute "aria-activedescendant"
        let activeItem = if isNull active then None else items |> Array.tryFind (fun item -> item.getAttribute "id" = active)

        check
            (sprintf "input host %s: arrows report Focus, Enter Activate and Escape Cancel from the keyboard, on the focused object" name)
            (texts events = [ "keyboard:focus:aim"; "keyboard:activate:aim"; "keyboard:cancel:aim" ])
            (String.concat "," (texts events))

        check
            (sprintf "input host %s: aria-activedescendant names the focused object's item, and Escape keeps the focus on the surface" name)
            (activeItem |> Option.map (fun item -> item.getAttribute "data-scene-control-id") = Some "aim" && isActive surface)
            (sprintf "activedescendant=%s focused=%b" active (isActive surface))

        let pageKeys = takePageKeys () |> List.ofArray

        check
            (sprintf "input host %s: neither the keydown nor the keyup of a reported key reaches the page; an unreported key does" name)
            (pageKeys = [ "down:x"; "up:x" ])
            (sprintf "%A" pageKeys)

        driver.Dispatch(RendererMsg.SelectBackend(if backend = RendererBackend.Svg then RendererBackend.Pixi else RendererBackend.Svg))
        do! settle 2
        let left = findAll document.body (sprintf "[data-input-host^='fsgg-input-%s']" name)
        check (sprintf "input host %s: switching away removes its mirror" name) (left.Length = 0) (string left.Length)
        (driver :> System.IDisposable).Dispose()
        InputRoute.unbind route
        do! settle 2
    }

/// The board as a grid (every kind of ObjectAccess, rows across three rowgroups, a mixed ungrouped rowgroup): the
/// structure, mouse, touch, long-press, secondary press, hover and mirror input, the APG grid keyboard, key containment,
/// Alt-modified arrows left to the browser, and the focus ring's two tones at 1x and 2x CSS scale and at DPR 2.
let private playGrid
    (check: string -> bool -> string -> unit)
    (container: HTMLElement)
    (late: LateFailureRoute)
    (route: InputRoute)
    (registry: RendererRegistry<InputFixture.Board>)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let name = RendererBackend.id backend + " grid"
        let events = ResizeArray<InputEvent>()
        InputRoute.bind events.Add route
        let board: InputFixture.Board = { Tick = 0; Selected = None }
        let driver = BrowserRenderers.start late registry board ignore

        if backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend backend)

        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
        do! settle 3
        let surface = surfaceOf container backend
        let width, height = InputFixture.width, InputFixture.height
        let centre id =
            InputFixture.objects board
            |> List.find (fun target -> target.Id = id)
            |> fun target -> { X = target.Bounds.X + target.Bounds.Width / 2.0; Y = target.Bounds.Y + target.Bounds.Height / 2.0 }

        // Structure.
        let items = itemsOf backend "gridcell"

        let shape =
            items
            |> Array.map (fun item ->
                sprintf
                    "%s r%s c%s %s"
                    (item.getAttribute "data-scene-control-id")
                    ((item.closest "[role=row]").Value.getAttribute "aria-rowindex")
                    (item.getAttribute "aria-colindex")
                    ((item.closest "[role=rowgroup]").Value.getAttribute "aria-label"))
            |> List.ofArray

        check
            (sprintf "input host %s: ONE grid Tab stop; rows across three rowgroups by aria-rowindex, the ungrouped a and b in one rowgroup, sparse Map columns, the pointer-only object absent, nothing hidden" name)
            (surface.getAttribute "role" = "grid"
             && tabStops container = 1
             && surface.getAttribute "aria-rowcount" = "6"
             && surface.getAttribute "aria-colcount" = "3"
             && shape
                = [
                    "a r1 c1 Input board objects"
                    "b r2 c1 Input board objects"
                    "u1 r3 c1 Units"
                    "u2 r4 c1 Units"
                    "t00 r5 c1 Map"
                    "t02 r5 c3 Map"
                    "t10 r6 c1 Map"
                    "t12 r6 c3 Map"
                ]
             && items |> Array.forall (ownedBy backend surface)
             && items |> Array.forall (hiddenFromAt >> not))
            (sprintf "role=%s tabStops=%d rows=%s cols=%s %A" (surface.getAttribute "role") (tabStops container) (surface.getAttribute "aria-rowcount") (surface.getAttribute "aria-colcount") shape)

        // Pointer, touch, long-press, secondary press, hover and the mirror.
        let after (action: Async<unit>) =
            async {
                events.Clear()
                do! action
                do! settle 1
                return texts events
            }

        let! pressed = after (inputAt surface width height "click" (centre "u1"))
        check (sprintf "input host %s: a mouse press reports Activate from the pointer" name) (List.contains "pointer:activate:u1" pressed) (String.concat "," pressed)

        let! tapped = after (inputAt surface width height "touchTap" (centre "t02"))

        check
            (sprintf "input host %s: a touch tap reports Activate from the pointer, and nothing secondary" name)
            (List.contains "pointer:activate:t02" tapped && not (tapped |> List.exists (fun e -> e.Contains "secondary")))
            (String.concat "," tapped)

        let menus = pageMenus ()
        let! held = after (inputAt surface width height "touchHold" (centre "t10"))

        check
            (sprintf "input host %s: a touch long-press reports ActivateSecondary from the pointer, no Activate, and no page context menu" name)
            (held |> List.filter (fun e -> e.Contains ":activate:" || e.Contains ":secondary:") = [ "pointer:secondary:t10" ]
             && pageMenus () = menus)
            (sprintf "%s menus %d -> %d" (String.concat "," held) menus (pageMenus ()))

        let! secondary = after (inputAt surface width height "rightClick" (centre "t12"))

        check
            (sprintf "input host %s: a secondary press reports ActivateSecondary from the pointer, and no page context menu" name)
            (List.contains "pointer:secondary:t12" secondary && pageMenus () = menus)
            (sprintf "%s menus %d -> %d" (String.concat "," secondary) menus (pageMenus ()))

        let centreA = centre "a"
        do! inputAt surface width height "move" { X = 160.0; Y = 150.0 }

        let! hovered =
            after (
                async {
                    do! inputAt surface width height "move" centreA
                    do! inputAt surface width height "move" { centreA with X = centreA.X + 5.0 }
                    do! inputAt surface width height "move" { X = 160.0; Y = 150.0 }
                }
            )

        check
            (sprintf "input host %s: hover is reported once per change of hovered object, and once with no target when leaving it" name)
            (hovered |> List.filter (fun e -> e.Contains "hover") = [ "pointer:hover:a"; "pointer:hover:-" ])
            (String.concat "," hovered)

        let u2 = items |> Array.find (fun item -> item.getAttribute "data-scene-control-id" = "u2")
        let! clicked = after (async { clickElement u2 })
        check (sprintf "input host %s: a screen reader's click on an item reports Activate from the Mirror" name) (clicked = [ "mirror:activate:u2" ]) (String.concat "," clicked)

        // The APG grid keyboard, with every reported keydown and keyup contained.
        focusElement surface
        takePageKeys () |> ignore

        let! walked =
            after (
                async {
                    for key in
                        [
                            "Control+Home"
                            "ArrowDown"
                            "ArrowDown"
                            "ArrowDown"
                            "ArrowDown"
                            "ArrowRight"
                            "ArrowRight"
                            "ArrowDown"
                            "ArrowLeft"
                            "ArrowUp"
                            "End"
                        ] do
                        do! BrowserConformance.key key
                }
            )

        let focus = walked |> List.filter (fun e -> e.StartsWith "keyboard:focus:") |> List.map (fun e -> e.Substring 15)

        check
            (sprintf "input host %s: the grid keyboard follows the APG: Control+Home, Down by aria-rowindex across rowgroups, Right stops at the row's end, Left, Up, End" name)
            (focus = [ "a"; "b"; "u1"; "u2"; "t00"; "t02"; "t12"; "t10"; "t00"; "t02" ])
            (String.concat "," focus)

        let leaked = takePageKeys () |> Array.filter reportedKeys.Contains |> List.ofArray

        check
            (sprintf "input host %s: neither the keydown nor the keyup of a reported key reaches the page" name)
            leaked.IsEmpty
            (sprintf "%A" leaked)

        let! history = after (BrowserConformance.key "Alt+ArrowRight")
        let pageKeys = takePageKeys () |> List.ofArray

        check
            (sprintf "input host %s: an Alt-modified arrow is neither reported nor contained (the browser's history keys)" name)
            (history |> List.forall (fun e -> not (e.StartsWith "keyboard:focus")) && List.contains "down:ArrowRight" pageKeys && List.contains "up:ArrowRight" pageKeys)
            (sprintf "events=%A page=%A" history pageKeys)

        // The focus ring's two tones around t00, 1 and 3 CSS px outside the middle of each edge.
        do! inputAt surface width height "click" (centre "t00")
        focusElement surface
        do! BrowserConformance.key "ArrowRight"
        do! BrowserConformance.key "ArrowLeft"

        let ring () =
            async {
                driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
                do! settle 3
                driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
                do! settle 2
                return! ringTones surface width height (InputFixture.objects board |> List.find (fun t -> t.Id = "t00")).Bounds
            }

        let! plain, plainDetail = ring ()

        check
            (sprintf "input host %s: the focus ring is presented with its light inner and dark outer band at their declared tones (1x CSS, presented pixels)" name)
            plain
            plainDetail

        styleSize surface (width * 2.0) (height * 2.0)
        let! doubled, doubledDetail = ring ()
        styleSize surface width height

        check
            (sprintf "input host %s: the focus ring's two presented tones hold at 2x CSS scale (inline style, after mount)" name)
            doubled
            doubledDetail

        do! BrowserConformance.deviceScale 2.0
        let! dense, denseDetail = ring ()
        do! BrowserConformance.deviceScale 1.0

        check
            (sprintf "input host %s: the focus ring's two presented tones hold at DPR 2" name)
            dense
            denseDetail

        driver.Dispatch(RendererMsg.SelectBackend(if backend = RendererBackend.Svg then RendererBackend.Pixi else RendererBackend.Svg))
        do! settle 2
        let left = findAll document.body (sprintf "[data-input-host^='fsgg-input-%s']" (RendererBackend.id backend))
        check (sprintf "input host %s: switching away removes its mirror" name) (left.Length = 0) (string left.Length)
        (driver :> System.IDisposable).Dispose()
        InputRoute.unbind route
        do! settle 2
    }

[<Emit("document.head.insertAdjacentHTML('beforeend', '<style>' + $0 + '</style>')")>]
let private addStylesheet (_css: string) : unit = jsNative

/// Who sizes the presented canvas or SVG root in a sizing case.
type private Owner =
    | Unsized
    | Inline
    | Stylesheet

/// When the presented density or size changes in a sizing case.
type private Timing =
    | Dpr1
    | Dpr2AtMount
    | Dpr1To2AfterMount
    | CssResizeAfterMount

[<Emit("($0.style.width = $1 + 'px', $0.style.height = $2 + 'px')")>]
let private sizeInline (_element: Element) (_width: float) (_height: float) : unit = jsNative

/// One case of the sizing matrix (FS.GG.Game#788, ADR 0001 "Canvas sizing"): the presented size is the expected one and
/// stays it over three animation frames (no feedback between backing store and CSS size), both focus-ring tones are
/// presented, and presses hit the objects at that size.
let private playSizing
    (check: string -> bool -> string -> unit)
    (container: HTMLElement)
    (late: LateFailureRoute)
    (route: InputRoute)
    (registry: RendererRegistry<InputFixture.Board>)
    (owner: Owner)
    (timing: Timing)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let name = sprintf "%s %A %A" (RendererBackend.id backend) owner timing
        let events = ResizeArray<InputEvent>()
        InputRoute.bind events.Add route
        let board: InputFixture.Board = { Tick = 0; Selected = None }

        if timing = Dpr2AtMount then
            do! BrowserConformance.deviceScale 2.0

        let driver = BrowserRenderers.start late registry board ignore

        if backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend backend)

        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
        let surface = surfaceOf container backend

        if owner = Inline then
            sizeInline surface 480.0 320.0

        do! settle 3

        match timing with
        | Dpr1To2AfterMount -> do! BrowserConformance.deviceScale 2.0
        | CssResizeAfterMount -> sizeInline surface 360.0 240.0
        | _ -> ()

        driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
        do! settle 4

        let expectedWidth, expectedHeight =
            match timing, owner with
            | CssResizeAfterMount, _ -> 360.0, 240.0
            | _, Unsized -> 240.0, 160.0
            | _ -> 480.0, 320.0

        let sizes = ResizeArray<float * float>()

        for _ in 1..3 do
            let bounds = surface.getBoundingClientRect ()
            sizes.Add(bounds.width, bounds.height)
            do! settle 1

        check
            (sprintf "input host sizing %s: presented at %gx%g CSS px, stable over three frames" name expectedWidth expectedHeight)
            (sizes |> Seq.forall (fun (w, h) -> abs (w - expectedWidth) < 0.5 && abs (h - expectedHeight) < 0.5))
            (sprintf "%A dpr=%g inline=%s" (List.ofSeq sizes) window.devicePixelRatio (surface.getAttribute "style"))

        let width, height = InputFixture.width, InputFixture.height

        let centre id =
            InputFixture.objects board
            |> List.find (fun target -> target.Id = id)
            |> fun target -> { X = target.Bounds.X + target.Bounds.Width / 2.0; Y = target.Bounds.Y + target.Bounds.Height / 2.0 }

        let corner id =
            InputFixture.objects board
            |> List.find (fun target -> target.Id = id)
            |> fun target -> { X = target.Bounds.X + target.Bounds.Width - 2.0; Y = target.Bounds.Y + 2.0 }

        events.Clear()
        do! inputAt surface width height "click" (centre "u1")
        do! inputAt surface width height "click" (corner "t12")
        let pressed = texts events |> List.filter (fun e -> e.Contains "activate")

        check
            (sprintf "input host sizing %s: presses hit the objects (a centre, and 2 units inside a corner)" name)
            (pressed = [ "pointer:activate:u1"; "pointer:activate:t12" ])
            (String.concat "," pressed)

        do! inputAt surface width height "click" (centre "t00")
        focusElement surface
        do! BrowserConformance.key "ArrowRight"
        do! BrowserConformance.key "ArrowLeft"
        driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
        do! settle 3
        driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
        do! settle 2
        let! tones, detail = ringTones surface width height (InputFixture.objects board |> List.find (fun t -> t.Id = "t00")).Bounds

        check (sprintf "input host sizing %s: both focus-ring tones are presented" name) tones detail

        (driver :> System.IDisposable).Dispose()
        InputRoute.unbind route

        if window.devicePixelRatio <> 1.0 then
            do! BrowserConformance.deviceScale 1.0

        do! settle 2
    }

let run (check: string -> bool -> string -> unit) : Async<unit> =
    async {
        installPage ()
        let container = document.createElement "div"
        container.id <- "stage-input-host"
        document.body.appendChild container |> ignore
        let late = BrowserRenderers.route ()
        let route = InputRoute.create ()

        let listbox =
            match BrowserRenderers.surfaces container (BrowserRenderers.options "Input board" ReferenceRenderers.width ReferenceRenderers.height) late with
            | (svg :: _) as all ->
                ReferenceRenderers.declare (SceneSurface.mountScene svg) all
                |> SceneRenderers.withInput ReferenceRenderers.interaction route
                |> SceneRenderers.registry
            | [] -> failwith "no surfaces"

        let grid =
            match BrowserRenderers.surfaces container (BrowserRenderers.options "Input board" InputFixture.width InputFixture.height) late with
            | (svg :: _) as all ->
                all
                |> List.fold
                    (fun renderers surface -> SceneRenderers.withSurface surface renderers)
                    (SceneRenderers.create InputFixture.view (SceneSurface.mountScene svg))
                |> SceneRenderers.withInput InputFixture.interaction route
                |> SceneRenderers.registry
            | [] -> failwith "no surfaces"

        for backend in RendererBackend.all do
            do! playListbox check container late route listbox backend

        for backend in RendererBackend.all do
            do! playGrid check container late route grid backend

        container.remove ()

        // The sizing matrix: {unsized, inline-sized, stylesheet-sized} x {DPR 1, DPR 2 at mount, DPR 1 -> 2 after mount,
        // a CSS resize after mount}, on every backend.
        addStylesheet "#stage-sizing-stylesheet canvas, #stage-sizing-stylesheet svg { width: 480px; height: 320px; }"

        for owner in [ Unsized; Inline; Stylesheet ] do
            let stage = document.createElement "div"
            stage.id <- sprintf "stage-sizing-%s" ((sprintf "%A" owner).ToLowerInvariant())
            document.body.appendChild stage |> ignore

            let registry =
                match BrowserRenderers.surfaces stage (BrowserRenderers.options "Input board" InputFixture.width InputFixture.height) late with
                | (svg :: _) as all ->
                    all
                    |> List.fold
                        (fun renderers surface -> SceneRenderers.withSurface surface renderers)
                        (SceneRenderers.create InputFixture.view (SceneSurface.mountScene svg))
                    |> SceneRenderers.withInput InputFixture.interaction route
                    |> SceneRenderers.registry
                | [] -> failwith "no surfaces"

            for timing in [ Dpr1; Dpr2AtMount; Dpr1To2AfterMount; CssResizeAfterMount ] do
                for backend in RendererBackend.all do
                    do! playSizing check stage late route registry owner timing backend

            stage.remove ()
    }
