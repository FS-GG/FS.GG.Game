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

        styleSize surface.parentElement (width * 2.0) (height * 2.0)
        let! doubled, doubledDetail = ring ()
        styleSize surface.parentElement width height

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

[<Emit("($0.style.width = $1 + 'px', $0.style.height = $2 + 'px')")>]
let private sizeInline (_element: Element) (_width: float) (_height: float) : unit = jsNative

[<Emit("($0.style.removeProperty('width'), $0.style.removeProperty('height'))")>]
let private unsizeInline (_element: Element) : unit = jsNative

[<Emit("(() => { const e = document.createElement('style'); e.id = $0; e.textContent = $1; document.head.appendChild(e) })()")>]
let private addStyle (_id: string) (_css: string) : unit = jsNative

[<Emit("document.getElementById($0)?.remove()")>]
let private removeStyle (_id: string) : unit = jsNative

[<Emit("($0.width = $1, $0.height = $2)")>]
let private setAttributes (_element: Element) (_width: float) (_height: float) : unit = jsNative

[<Emit("($0.tagName.toLowerCase() === 'canvas' ? [$0.width, $0.height] : [parseFloat($0.getAttribute('width')), parseFloat($0.getAttribute('height'))])")>]
let private attributesOf (_element: Element) : float[] = jsNative

[<Emit("$0.setAttribute('width', $1), $0.setAttribute('height', $2)")>]
let private setSvgAttributes (_element: Element) (_width: string) (_height: string) : unit = jsNative

/// The page's device pixel ratio and viewport width, through the driver (a device-metrics override).
let private metrics (factor: float) (viewportWidth: float) : Async<unit> =
    async {
        let pending: JS.Promise<float> =
            emitJsExpr (createObj [ "kind" ==> "scale"; "factor" ==> factor; "width" ==> viewportWidth ]) "window.__fsggInput($0)"

        let! _ = Async.AwaitPromise pending
        let! _ = BrowserConformance.waitUntil 120 (fun () -> window.devicePixelRatio = factor && window.innerWidth = viewportWidth)
        return ()
    }

/// One case of the sizing matrix: what is set before the mount, what the page does to the frame at mount and after it,
/// the presented size expected, and whether the game draws again afterwards (the DPR-only cases must not).
type private SizingCase =
    {
        Name: string
        DprAtMount: float
        AtMount: Element -> unit
        After: Element -> Async<unit>
        Expected: float * float
        Full: bool
        Cleanup: unit -> Async<unit>
    }

let private nothing (_: Element) = ()
let private later (_: Element) = async { return () }
let private reset () = async { return () }

let private sizingCases (stage: string) : SizingCase list =
    let rule = sprintf "#%s [data-input-frame] { width: 480px; height: 320px; }" stage
    let sheet = stage + "-sheet"
    let query = sprintf "@media (min-width: 900px) { #%s [data-input-frame] { width: 480px; height: 320px; } }" stage

    [
        for dpr in [ 1.0; 2.0 ] do
            let at = sprintf "DPR %g" dpr

            // Owned by the host, by an inline style and by a stylesheet.
            yield { Name = "unsized, " + at; DprAtMount = dpr; AtMount = nothing; After = later; Expected = 240.0, 160.0; Full = true; Cleanup = reset }

            yield
                { Name = "inline-sized, " + at; DprAtMount = dpr; AtMount = (fun f -> sizeInline f 480.0 320.0); After = later; Expected = 480.0, 320.0; Full = true; Cleanup = reset }

            yield
                {
                    Name = "stylesheet-sized, " + at
                    DprAtMount = dpr
                    AtMount = (fun _ -> addStyle sheet rule)
                    After = later
                    Expected = 480.0, 320.0
                    Full = true
                    Cleanup = (fun () -> async { removeStyle sheet })
                }

            // Page sizing withdrawn after mount: a stylesheet removed, a media query turned off, an inline size removed.
            yield
                {
                    Name = "stylesheet removed after mount, " + at
                    DprAtMount = dpr
                    AtMount = (fun _ -> addStyle sheet rule)
                    After = (fun _ -> async { removeStyle sheet })
                    Expected = 240.0, 160.0
                    Full = true
                    Cleanup = reset
                }

            yield
                {
                    Name = "media query off after mount, " + at
                    DprAtMount = dpr
                    AtMount = (fun _ -> addStyle sheet query)
                    After = (fun _ -> metrics dpr 800.0)
                    Expected = 240.0, 160.0
                    Full = true
                    Cleanup = (fun () -> async {
                        removeStyle sheet
                        do! metrics dpr 1024.0
                    })
                }

            yield
                {
                    Name = "inline size removed after mount, " + at
                    DprAtMount = dpr
                    AtMount = (fun f -> sizeInline f 480.0 320.0)
                    After = (fun f -> async { unsizeInline f })
                    Expected = 240.0, 160.0
                    Full = true
                    Cleanup = reset
                }

            // Page sizing added after mount.
            yield
                {
                    Name = "stylesheet added after mount, " + at
                    DprAtMount = dpr
                    AtMount = nothing
                    After = (fun _ -> async { addStyle sheet rule })
                    Expected = 480.0, 320.0
                    Full = true
                    Cleanup = (fun () -> async { removeStyle sheet })
                }

            // Resized after mount, and the density changed after mount (the game draws again).
            yield
                { Name = "CSS resize after mount, " + at; DprAtMount = dpr; AtMount = nothing; After = (fun f -> async { sizeInline f 360.0 240.0 }); Expected = 360.0, 240.0; Full = true; Cleanup = reset }

        yield
            { Name = "DPR 1 to 2 after mount"; DprAtMount = 1.0; AtMount = nothing; After = (fun _ -> metrics 2.0 1024.0); Expected = 240.0, 160.0; Full = true; Cleanup = reset }

        // The density changed after mount with NO draw: the backing store must follow on its own.
        yield
            { Name = "DPR 1 to 2 without a draw"; DprAtMount = 1.0; AtMount = nothing; After = (fun _ -> metrics 2.0 1024.0); Expected = 240.0, 160.0; Full = false; Cleanup = reset }

        yield
            { Name = "DPR 2 to 1 without a draw"; DprAtMount = 2.0; AtMount = nothing; After = (fun _ -> metrics 1.0 1024.0); Expected = 240.0, 160.0; Full = false; Cleanup = reset }
    ]

/// One case of the sizing matrix (FS.GG.Game#788, ADR 0001 "Surface sizing") on one backend: the presented size is the
/// expected one and stays it over three animation frames; a canvas's backing store is its presented size x DPR; then,
/// for a full case, presses hit the objects at that size and both focus-ring tones are presented.
let private playSizing
    (check: string -> bool -> string -> unit)
    (container: HTMLElement)
    (late: LateFailureRoute)
    (route: InputRoute)
    (registry: RendererRegistry<InputFixture.Board>)
    (case: SizingCase)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let name = sprintf "%s %s" (RendererBackend.id backend) case.Name
        let events = ResizeArray<InputEvent>()
        InputRoute.bind events.Add route
        let board: InputFixture.Board = { Tick = 0; Selected = None }

        if window.devicePixelRatio <> case.DprAtMount then
            do! metrics case.DprAtMount 1024.0

        let driver = BrowserRenderers.start late registry board ignore

        if backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend backend)

        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
        let surface = surfaceOf container backend
        let frame = surface.parentElement
        case.AtMount frame
        do! settle 3
        do! case.After frame

        if case.Full then
            driver.Dispatch(RendererMsg.StateChanged driver.Model.State)

        do! settle 4
        let expectedWidth, expectedHeight = case.Expected
        let sizes = ResizeArray<float * float>()

        for _ in 1..3 do
            let bounds = surface.getBoundingClientRect ()
            sizes.Add(bounds.width, bounds.height)
            do! settle 1

        check
            (sprintf "input host sizing %s: presented at %gx%g CSS px, stable over three frames" name expectedWidth expectedHeight)
            (sizes |> Seq.forall (fun (w, h) -> abs (w - expectedWidth) < 0.5 && abs (h - expectedHeight) < 0.5))
            (sprintf "%A dpr=%g frame style=%s" (List.ofSeq sizes) window.devicePixelRatio (frame.getAttribute "style"))

        let attributes = attributesOf surface
        let dpr = window.devicePixelRatio

        let backingOk =
            backend = RendererBackend.Svg
            || (abs (attributes.[0] - System.Math.Round(expectedWidth * dpr)) <= 1.0
                && abs (attributes.[1] - System.Math.Round(expectedHeight * dpr)) <= 1.0)

        check
            (sprintf "input host sizing %s: the backing store is the presented size x DPR%s" name (if backend = RendererBackend.Svg then " (SVG: vector, no backing store)" else ""))
            backingOk
            (sprintf "attributes %A at DPR %g for %gx%g" attributes dpr expectedWidth expectedHeight)

        if case.Full then
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
        do! case.Cleanup()

        if window.devicePixelRatio <> 1.0 || window.innerWidth <> 1024.0 then
            do! metrics 1.0 1024.0

        do! settle 2
    }

/// The proof that the output's width and height attributes never size anything: set them to 4096 x 4096 (17 and 26
/// times the scene) and the presented box does not move.
let private playAttributes
    (check: string -> bool -> string -> unit)
    (container: HTMLElement)
    (late: LateFailureRoute)
    (registry: RendererRegistry<InputFixture.Board>)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let board: InputFixture.Board = { Tick = 0; Selected = None }
        let driver = BrowserRenderers.start late registry board ignore

        if backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend backend)

        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
        do! settle 3
        let surface = surfaceOf container backend
        let before = surface.getBoundingClientRect ()
        let frameBefore = surface.parentElement.getBoundingClientRect ()
        let saved = attributesOf surface

        if backend = RendererBackend.Svg then
            setSvgAttributes surface "4096" "4096"
        else
            setAttributes surface 4096.0 4096.0

        do! settle 3
        let after = surface.getBoundingClientRect ()
        let frameAfter = surface.parentElement.getBoundingClientRect ()

        check
            (sprintf "input host sizing %s: width and height attributes of 4096 x 4096 do not change the layout box" (RendererBackend.id backend))
            (after.width = before.width && after.height = before.height && frameAfter.width = frameBefore.width && frameAfter.height = frameBefore.height)
            (sprintf "before %gx%g after %gx%g (frame %gx%g -> %gx%g)" before.width before.height after.width after.height frameBefore.width frameBefore.height frameAfter.width frameAfter.height)

        if backend = RendererBackend.Svg then
            setSvgAttributes surface (string saved.[0]) (string saved.[1])
        else
            setAttributes surface saved.[0] saved.[1]

        (driver :> System.IDisposable).Dispose()
        do! settle 2
    }

/// The frame's content box as PRESENTED (viewport CSS px, through any page transform or zoom): [left, top, width, height].
[<Emit("(() => { const s = getComputedStyle($0); const px = v => parseFloat(v) || 0; const r = $0.getBoundingClientRect(); const pl = px(s.paddingLeft), pr = px(s.paddingRight), pt = px(s.paddingTop), pb = px(s.paddingBottom), bl = px(s.borderLeftWidth), br = px(s.borderRightWidth), bt = px(s.borderTopWidth), bb = px(s.borderBottomWidth); const border = s.boxSizing === 'border-box'; const w = px(s.width) - (border ? pl + pr + bl + br : 0), h = px(s.height) - (border ? pt + pb + bt + bb : 0); const k = r.width / (w + pl + pr + bl + br); return [r.left + (bl + pl) * k, r.top + (bt + pt) * k, w * k, h * (r.height / (h + pt + pb + bt + bb))] })()")>]
let private contentRect (_frame: Element) : float[] = jsNative

/// Decode a base64 PNG of a `clipWidth` CSS px wide region; from the CSS px point (`x`, `y`) in it, the extent (CSS px)
/// of the run of pixels of the colour there along its row and its column, and that colour: [width, height, r, g, b].
[<Emit("(async () => { const img = new Image(); img.src = 'data:image/png;base64,' + $0; await img.decode(); const c = document.createElement('canvas'); c.width = img.width; c.height = img.height; const g = c.getContext('2d'); g.drawImage(img, 0, 0); const d = g.getImageData(0, 0, img.width, img.height).data; const k = img.width / $3; const at = (x, y) => { const i = (y * img.width + x) * 4; return [d[i], d[i + 1], d[i + 2]] }; const x0 = Math.floor($1 * k), y0 = Math.floor($2 * k); const c0 = at(x0, y0); const same = p => Math.abs(p[0] - c0[0]) + Math.abs(p[1] - c0[1]) + Math.abs(p[2] - c0[2]) < 60; let l = x0, r = x0, u = y0, b = y0; while (l > 0 && same(at(l - 1, y0))) l--; while (r < img.width - 1 && same(at(r + 1, y0))) r++; while (u > 0 && same(at(x0, u - 1))) u--; while (b < img.height - 1 && same(at(x0, b + 1))) b++; return [(r - l + 1) / k, (b - u + 1) / k, c0[0], c0[1], c0[2]] })()")>]
let private extentAt (_png: string) (_x: float) (_y: float) (_clipWidth: float) : JS.Promise<float[]> = jsNative

[<Emit("(() => { $0.scrollTop = $1 })()")>]
let private scrollTo (_element: Element) (_top: float) : unit = jsNative

/// One page layout around the frame (FS.GG.Game#788, round 5): the stage's stylesheet (`{S}` is the stage selector),
/// the presented output size expected, if the layout determines it, and the ratio to set first.
type private LayoutCase =
    {
        Name: string
        Css: string
        Expected: (float * float) option
        /// Whether the frame's content box is the expected size too (no letterbox): the default size, or one dimension
        /// set by the page with the other following the scene's aspect ratio.
        FrameFits: bool
        Dpr: float
        Scroll: float
    }

let private layoutCases: LayoutCase list =
    let case name css expected =
        { Name = name; Css = css; Expected = expected; FrameFits = true; Dpr = 1.0; Scroll = 0.0 }

    [
        // Parents and page rules that stretched the frame before (the default must keep the scene's size).
        case "flex-column parent" "{S} { display: flex; flex-direction: column; }" (Some(240.0, 160.0))
        case "flex-row parent, 400 px tall" "{S} { display: flex; height: 400px; }" (Some(240.0, 160.0))
        case "grid parent, 1fr column" "{S} { display: grid; grid-template-columns: 1fr; }" (Some(240.0, 160.0))
        case "grid parent, 1fr column and a 400 px row" "{S} { display: grid; grid-template-columns: 1fr; grid-template-rows: 400px; }" (Some(240.0, 160.0))
        case "page rule on every stage div: width 100%, padding 10 px" "{S} { width: 640px; } {S} div { width: 100%; padding: 10px; }" (Some(640.0, 640.0 / 1.5))
        case "border-box everywhere and a 3 px border on every stage div" "{S} * { box-sizing: border-box; } {S} div { border: 3px solid red; }" (Some(240.0, 160.0))
        // The page sizing the frame: one dimension, or a box of another aspect (contain-fit letterboxes).
        case "frame width only, 480 px" "{S} [data-input-frame] { width: 480px; }" (Some(480.0, 320.0))
        case "frame height only, 320 px" "{S} [data-input-frame] { height: 320px; }" (Some(480.0, 320.0))
        { case "frame much wider than the scene, 600x100" "{S} [data-input-frame] { width: 600px; height: 100px; }" (Some(150.0, 100.0)) with FrameFits = false }
        { case "frame much taller than the scene, 120x600" "{S} [data-input-frame] { width: 120px; height: 600px; }" (Some(120.0, 80.0)) with FrameFits = false }
        // Adversarial placements: positioned, transformed, zoomed and scrolled parents.
        case "absolutely positioned parent" "{S} { position: absolute; left: 300px; top: 40px; }" (Some(240.0, 160.0))
        case "fixed parent" "{S} { position: fixed; left: 420px; top: 260px; }" (Some(240.0, 160.0))
        case "transformed parent, scale 1.5" "{S} { transform: scale(1.5); transform-origin: 0 0; }" (Some(360.0, 240.0))
        case "zoomed parent, zoom 2" "{S} { zoom: 2; }" (Some(480.0, 320.0))
        { case "scrolled parent" "{S} { height: 260px; overflow: auto; } {S}::before { content: ''; display: block; height: 300px; }" (Some(240.0, 160.0)) with Scroll = 250.0 }
        { case "flex-column parent" "{S} { display: flex; flex-direction: column; }" (Some(240.0, 160.0)) with Dpr = 2.0 }
    ]

/// One page layout on one backend, from ONE screenshot of the frame taken with the focus ring shown (the fit, the drawn
/// tile, the letterbox, the stretched point and both ring bands are all read from it), then three presses: the output is
/// the uniform contain-fit of the frame's content box (and the expected size, which the frame keeps too where the layout
/// determines it); a square tile is DRAWN square at the fit's scale; outside the fit is letterbox (no scene, no press); a
/// press activates the tile exactly where it is drawn, and at the point a stretched mapping would use only if the
/// screenshot shows it there; both focus-ring bands are presented.
let private playLayout
    (check: string -> bool -> string -> unit)
    (stage: HTMLElement)
    (late: LateFailureRoute)
    (route: InputRoute)
    (registry: RendererRegistry<InputFixture.Board>)
    (case: LayoutCase)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let name =
            sprintf "%s %s%s" (RendererBackend.id backend) case.Name (if case.Dpr <> 1.0 then sprintf ", DPR %g" case.Dpr else "")

        let sheet = "stage-layout-sheet"
        addStyle sheet (case.Css.Replace("{S}", "#" + stage.id))

        if window.devicePixelRatio <> case.Dpr then
            do! metrics case.Dpr 1024.0

        let events = ResizeArray<InputEvent>()
        InputRoute.bind events.Add route
        let board: InputFixture.Board = { Tick = 0; Selected = None }
        let driver = BrowserRenderers.start late registry board ignore

        if backend <> RendererBackend.Svg then
            driver.Dispatch(RendererMsg.SelectBackend backend)

        let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
        let surface = surfaceOf stage backend
        let frame = surface.parentElement

        if case.Scroll > 0.0 then
            scrollTo stage case.Scroll

        let width, height = InputFixture.width, InputFixture.height
        let objects = InputFixture.objects board
        let tile = objects |> List.find (fun t -> t.Id = "a")
        let t00 = objects |> List.find (fun t -> t.Id = "t00")

        // The focus ring on t00, shown (a press focuses without showing; the arrows show it), before the one screenshot.
        do! settle 2
        do! inputAt surface width height "click" { X = t00.Bounds.X + 15.0; Y = t00.Bounds.Y + 15.0 }
        focusElement surface
        do! BrowserConformance.key "ArrowRight"
        do! BrowserConformance.key "ArrowLeft"
        driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
        do! settle 3

        scrollIntoView frame
        do! settle 1
        let frameBox = frame.getBoundingClientRect ()
        let out = surface.getBoundingClientRect ()
        let box = contentRect frame

        let request =
            createObj
                [
                    "kind" ==> "shot"
                    "x" ==> frameBox.left
                    "y" ==> frameBox.top
                    "width" ==> frameBox.width
                    "height" ==> frameBox.height
                ]

        let pending: JS.Promise<string> = emitJsExpr request "window.__fsggInput($0)"
        let! png = Async.AwaitPromise pending
        let inShot (x: float) (y: float) = [| x - frameBox.left; y - frameBox.top |]

        let read (points: (float * float) list) =
            async {
                let pointsInShot = points |> List.map (fun (px, py) -> inShot px py) |> Array.ofList
                let shotWidth, shotHeight = frameBox.width, frameBox.height
                let! pixels = Async.AwaitPromise(decodeAt png pointsInShot shotWidth shotHeight)
                return pixels |> Array.map (fun p -> float p.[0], float p.[1], float p.[2]) |> List.ofArray
            }

        // (1) The fit.
        let scale = min (box.[2] / width) (box.[3] / height)
        let fitWidth, fitHeight = width * scale, height * scale
        let fitLeft, fitTop = box.[0] + (box.[2] - fitWidth) / 2.0, box.[1] + (box.[3] - fitHeight) / 2.0

        let expectedOk =
            match case.Expected with
            | Some(w, h) ->
                abs (out.width - w) < 1.0
                && abs (out.height - h) < 1.0
                && (not case.FrameFits || (abs (box.[2] - w) < 1.0 && abs (box.[3] - h) < 1.0))
            | None -> true

        check
            (sprintf
                "input host layout %s: the output is the uniform contain-fit of the frame's content box%s"
                name
                (match case.Expected with
                 | Some(w, h) when case.FrameFits -> sprintf " (%gx%g, and so is the frame: it keeps the scene's aspect and does not stretch)" w h
                 | Some(w, h) -> sprintf " (%gx%g)" w h
                 | None -> ""))
            (expectedOk
             && abs (out.width / out.height - width / height) < 0.01
             && abs (out.width - fitWidth) < 1.0
             && abs (out.height - fitHeight) < 1.0
             && abs (out.left - fitLeft) < 1.0
             && abs (out.top - fitTop) < 1.0)
            (sprintf "output %g,%g %gx%g; content box %A; fit %g,%g %gx%g; frame style=%s" out.left out.top out.width out.height box fitLeft fitTop fitWidth fitHeight (frame.getAttribute "style"))

        // (2) The tile, drawn square at the fit's scale.
        let presented = out.width / width
        let tileX, tileY = tile.Bounds.X + tile.Bounds.Width / 2.0, tile.Bounds.Y + tile.Bounds.Height / 2.0
        let drawnX, drawnY = out.left + tileX * presented, out.top + tileY * presented
        // (Named apart from the Emit's own locals: an Emit argument is substituted as text.)
        let tileInShot = inShot drawnX drawnY
        let tileShotX, tileShotY, shotWidth = tileInShot.[0], tileInShot.[1], frameBox.width
        let! extent = Async.AwaitPromise(extentAt png tileShotX tileShotY shotWidth)
        let colour = extent.[2], extent.[3], extent.[4]
        let near (r: float, g: float, b: float) (r', g', b') = abs (r - r') + abs (g - g') + abs (b - b') < 60.0
        let bg = InputFixture.background
        let backgroundColour = float bg.Red, float bg.Green, float bg.Blue
        let side = tile.Bounds.Width * presented

        check
            (sprintf "input host layout %s: a square tile is drawn square, at the fit's scale (one scale for both axes)" name)
            (not (near colour backgroundColour) && abs (extent.[0] - side) <= 2.0 && abs (extent.[1] - side) <= 2.0)
            (sprintf "drawn %gx%g, expected %gx%g; colour %A" extent.[0] extent.[1] side side colour)

        // (5, read now) Both focus-ring bands around t00. When every edge lands on a whole device pixel (the integer
        // scales), a quarter CSS px either side of the bands' common edge too; otherwise at each band's centre.
        let dpr = window.devicePixelRatio
        let whole (v: float) = abs (v * dpr - System.Math.Round(v * dpr)) < 0.01
        let b = t00.Bounds

        let aligned =
            whole (out.left + b.X * presented)
            && whole (out.left + (b.X + b.Width) * presented)
            && whole (out.top + b.Y * presented)
            && whole (out.top + (b.Y + b.Height) * presented)

        let around (distance: float) =
            let d = distance / presented

            [
                b.X - d, b.Y + b.Height / 2.0
                b.X + b.Width / 2.0, b.Y - d
                b.X + b.Width + d, b.Y + b.Height / 2.0
                b.X + b.Width / 2.0, b.Y + b.Height + d
            ]
            |> List.map (fun (x, y) -> out.left + x * presented, out.top + y * presented)

        let innerAt, outerAt = if aligned then [ 1.5; 1.75 ], [ 2.25; 3.5 ] else [ 1.0 ], [ 3.0 ]
        let! inner = read (innerAt |> List.collect around)
        let! outer = read (outerAt |> List.collect around)
        let light = inner |> List.forall (fun (r, g, b) -> r >= 231.0 && g >= 231.0 && b >= 231.0)
        let dark = outer |> List.forall (fun (r, g, b) -> r <= 40.0 && g <= 40.0 && b <= 40.0)

        // (3, read now) The letterbox, and (4, read now) the stretched point.
        let gapX = out.left - box.[0]
        let gapY = out.top - box.[1]
        let letterboxed = gapX > 2.0 || gapY > 2.0

        let lx, ly =
            if gapX > 2.0 then box.[0] + gapX / 2.0, out.top + out.height / 2.0
            else out.left + out.width / 2.0, box.[1] + gapY / 2.0

        let stretchedX = box.[0] + tileX * box.[2] / width
        let stretchedY = box.[1] + tileY * box.[3] / height
        let! readLetter = read [ lx, ly; stretchedX, stretchedY ]
        let letterColour = readLetter.[0]
        let shownThere = near readLetter.[1] colour

        // The presses, after every read.
        let press x y =
            async {
                events.Clear()

                do!
                    Async.AwaitPromise(
                        emitJsExpr (createObj [ "kind" ==> "click"; "x" ==> x; "y" ==> y ]) "window.__fsggInput($0)"
                        : JS.Promise<unit>
                    )

                do! settle 1
                return texts events |> List.filter (fun e -> e.Contains "activate")
            }

        if letterboxed then
            let! pressed = press lx ly

            check
                (sprintf "input host layout %s: the letterbox shows no scene and a press there activates nothing" name)
                (not (near letterColour backgroundColour) && not (near letterColour colour) && pressed.IsEmpty)
                (sprintf "letterbox at %g,%g colour %A; presses %A" lx ly letterColour pressed)
        else
            check
                (sprintf "input host layout %s: no letterbox (the frame's content box has the scene's aspect)" name)
                (abs gapX < 1.0 && abs gapY < 1.0)
                (sprintf "gaps %g,%g" gapX gapY)

        // The stretched point is judged only when it is clearly inside or clearly outside the drawn tile (1.5 CSS px from
        // its edges); on an edge, the anti-aliased pixel and the inclusive hit test may differ.
        let tileLeft, tileTop = out.left + tile.Bounds.X * presented, out.top + tile.Bounds.Y * presented
        let tileRight, tileBottom = tileLeft + side, tileTop + side

        let edgeDistance =
            min
                (min (abs (stretchedX - tileLeft)) (abs (stretchedX - tileRight)))
                (min (abs (stretchedY - tileTop)) (abs (stretchedY - tileBottom)))

        let judged = edgeDistance >= 1.5
        let hits (pressed: string list) = pressed |> List.exists (fun e -> e.StartsWith "pointer:activate:a")
        let! drawnPress = press drawnX drawnY
        let! stretchedPress = if judged && (abs (stretchedX - drawnX) > 0.5 || abs (stretchedY - drawnY) > 0.5) then press stretchedX stretchedY else async { return drawnPress }
        let hitDrawn, hitStretched = hits drawnPress, hits stretchedPress

        check
            (sprintf "input host layout %s: a press activates the tile where it is drawn and only there (screenshot plus press)" name)
            (hitDrawn && (not judged || hitStretched = shownThere))
            (sprintf "drawn %g,%g -> %b; stretched %g,%g (%g CSS px from an edge%s) shows tile=%b -> %b" drawnX drawnY hitDrawn stretchedX stretchedY edgeDistance (if judged then "" else ", not judged") shownThere hitStretched)

        check
            (sprintf "input host layout %s: both focus-ring bands are presented (2 CSS px each, one scale)" name)
            (light && dark)
            (sprintf "%s; css=%g dpr=%g inner=%A outer=%A" (if aligned then "edges on whole device px, both sides of the common edge" else "edges inside device px, band centres") presented dpr inner outer)

        (driver :> System.IDisposable).Dispose()
        InputRoute.unbind route
        removeStyle sheet

        if window.devicePixelRatio <> 1.0 || window.innerWidth <> 1024.0 then
            do! metrics 1.0 1024.0

        do! settle 1
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

        // The sizing matrix (ADR 0001 "Surface sizing"), each case on every backend, each in its own stage.
        let cases = sizingCases "stage-sizing"
        let stage = document.createElement "div"
        stage.id <- "stage-sizing"
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

        for case in cases do
            for backend in RendererBackend.all do
                do! playSizing check stage late route registry case backend

        for backend in RendererBackend.all do
            do! playAttributes check stage late registry backend

        stage.remove ()
    }

[<Emit("[$0.style.imageRendering || 'auto', $0.width, $0.height]")>]
let private renderingOf (_canvas: Element) : obj[] = jsNative

/// Nearest sampling only where it is exact (#797 round 6): a canvas is `pixelated` under `scale(1.5)` (its backing store
/// is its presented device size), and NOT under a non-uniform `scale(2, 1)` (the density is the area scale, so the
/// browser must resample) or with a backing store refused at the cap (a 5000 x 2500 frame at DPR 2).
let private playRendering
    (check: string -> bool -> string -> unit)
    (stage: HTMLElement)
    (late: LateFailureRoute)
    (registry: RendererRegistry<InputFixture.Board>)
    (backend: RendererBackend)
    : Async<unit> =
    async {
        let sheet = "stage-layout-sheet"

        for css, dpr, wanted, what in
            [
                "{S} { transform: scale(1.5); transform-origin: 0 0; }", 1.0, "pixelated", "pixelated under scale(1.5) (backing store = presented device px)"
                "{S} { transform: scale(2, 1); transform-origin: 0 0; }", 1.0, "auto", "not pixelated under scale(2, 1) (the backing store is resampled)"
                "{S} [data-input-frame] { width: 5000px; height: 2500px; }", 2.0, "auto", "not pixelated with a backing store refused at the cap (5000x2500 at DPR 2)"
            ] do
            addStyle sheet (css.Replace("{S}", "#" + stage.id))

            if window.devicePixelRatio <> dpr then
                do! metrics dpr 1024.0

            let driver = BrowserRenderers.start late registry { Tick = 0; Selected = None } ignore
            driver.Dispatch(RendererMsg.SelectBackend backend)
            let! _ = BrowserConformance.waitUntil 600 (fun () -> driver.Mounted = Some backend)
            driver.Dispatch(RendererMsg.StateChanged driver.Model.State)
            do! settle 3
            let surface = surfaceOf stage backend
            let state = renderingOf surface
            let bounds = surface.getBoundingClientRect ()

            check
                (sprintf "input host rendering %s: %s" (RendererBackend.id backend) what)
                (string state.[0] = wanted)
                (sprintf "image-rendering=%O backing=%Ox%O presented=%gx%g CSS px at DPR %g" state.[0] state.[1] state.[2] bounds.width bounds.height window.devicePixelRatio)

            (driver :> System.IDisposable).Dispose()
            removeStyle sheet

            if window.devicePixelRatio <> 1.0 then
                do! metrics 1.0 1024.0

            do! settle 1
    }

/// The page layouts around the frame (contain-fit and the default size), each on every backend: the smoke's `layout`
/// part, which CI runs as its own page (scripts/test-render-browser-host.sh, RENDER_SMOKE_PART).
let runLayout (check: string -> bool -> string -> unit) : Async<unit> =
    async {
        let late = BrowserRenderers.route ()
        let route = InputRoute.create ()
        let stage = document.createElement "div"
        stage.id <- "stage-layout"
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

        for case in layoutCases do
            for backend in RendererBackend.all do
                do! playLayout check stage late route registry case backend

        for backend in [ RendererBackend.Pixi; RendererBackend.Babylon ] do
            do! playRendering check stage late registry backend

        stage.remove ()
    }
