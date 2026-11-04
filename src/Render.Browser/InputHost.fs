namespace FS.GG.Game.Render.Browser

open Browser.Dom
open Browser.Types
open Fable.Core
open FS.GG.Game.Render
open FS.GG.UI.Scene

module internal InputHost =

    [<RequireQualifiedAccess>]
    type Ownership =
        | Owns of parent: HTMLElement
        | Fallback

    [<RequireQualifiedAccess>]
    type CanvasHost =
        | Selection of report: (SelectionRequest -> unit)
        | Input of spec: InputHostSpec * report: (InputEvent -> unit)

    /// The scene point [x, y] under `pointer` on `surface` (a `sceneWidth` wide scene); see `localPoint`.
    [<Emit("(() => { const el = $0, e = $1; if (typeof SVGSVGElement !== 'undefined' && el instanceof SVGSVGElement) { const m = el.getScreenCTM(); if (m) { const p = new DOMPoint(e.clientX, e.clientY).matrixTransform(m.inverse()); return [p.x, p.y] } } const own = parseFloat(getComputedStyle(el).width) || 0; if (e.target === el && own > 0) { let z = 1; for (let a = el; a; a = a.parentElement) { const v = parseFloat(getComputedStyle(a).zoom); if (v > 0) z *= v } const s = own / $2; return [e.offsetX / z / s, e.offsetY / z / s] } const b = el.getBoundingClientRect(); const k = b.width > 0 ? b.width / $2 : 1; return [(e.clientX - b.left) / k, (e.clientY - b.top) / k] })()")>]
    let private pointInScene (_surface: Element) (_pointer: MouseEvent) (_sceneWidth: float) : float[] = jsNative

    /// The cue layer's top-level rectangles with their edges on whole device px (`unit` device px per scene unit; the
    /// surface's origin is on a device px, CanvasSizing): two bands that share an edge still share it, and a band of N
    /// CSS px is presented as solid device px at any scale instead of anti-aliased across a pixel boundary.
    let private snapToDevice (unit: float) (nodes: SceneNode list) =
        if not (unit > 0.0) || System.Double.IsInfinity unit then
            nodes
        else
            let snap (v: float) = System.Math.Round(v * unit) / unit

            nodes
            |> List.map (fun node ->
                match node with
                | SceneNode.Rectangle((x, y, w, h), fill) ->
                    let left, top = snap x, snap y
                    SceneNode.Rectangle((left, top, snap (x + w) - left, snap (y + h) - top), fill)
                | other -> other)

    [<Emit("$0.isComposing === true")>]
    let private isComposing (_event: KeyboardEvent) : bool = jsNative

    [<Emit("$0.pointerType ?? 'mouse'")>]
    let private pointerType (_event: Event) : string = jsNative

    [<Emit("$0.pointerId ?? 0")>]
    let private pointerId (_event: Event) : float = jsNative

    [<Emit("$0.button ?? 0")>]
    let private button (_event: Event) : float = jsNative

    [<Emit("$0.repeat === true")>]
    let private isRepeat (_event: KeyboardEvent) : bool = jsNative

    [<Emit("$0.shiftKey === true")>]
    let private shiftKey (_event: Event) : bool = jsNative

    [<Emit("$0.ctrlKey === true")>]
    let private ctrlKey (_event: Event) : bool = jsNative

    [<Emit("$0.altKey === true")>]
    let private altKey (_event: Event) : bool = jsNative

    [<Emit("$0.metaKey === true")>]
    let private metaKey (_event: Event) : bool = jsNative

    [<Emit("setTimeout($0, $1)")>]
    let private setTimeout (_action: unit -> unit) (_milliseconds: int) : float = jsNative

    [<Emit("clearTimeout($0)")>]
    let private clearTimeout (_timer: float) : unit = jsNative

    [<Emit("$0.target === $1")>]
    let private targets (_event: Event) (_element: Element) : bool = jsNative

    [<Emit("(() => { try { return $0.matches(':focus-visible') } catch (_) { return true } })()")>]
    let private focusVisible (_element: Element) : bool = jsNative

    [<Emit("$0.closest('[data-scene-control-id]')?.getAttribute('data-scene-control-id') ?? null")>]
    let private controlIdOf (_target: EventTarget) : string option = jsNative

    [<Emit("$0.addEventListener($1, $2, true)")>]
    let private listenCapture (_target: EventTarget) (_name: string) (_handler: Event -> unit) : unit = jsNative

    [<Emit("$0.removeEventListener($1, $2, true)")>]
    let private unlistenCapture (_target: EventTarget) (_name: string) (_handler: Event -> unit) : unit = jsNative

    [<Emit("$0.stopImmediatePropagation()")>]
    let private stopImmediate (_event: Event) : unit = jsNative

    /// The visually hidden style of an owned mirror: in the accessibility tree (never `display:none`, `visibility:hidden`
    /// or `aria-hidden`), out of the layout flow, so it never expands the page.
    let private hiddenStyle =
        "position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip-path:inset(50%);white-space:nowrap"

    let private modifiersOf (event: Event) : InputModifiers =
        {
            Shift = shiftKey event
            Control = ctrlKey event
            Alt = altKey event
            Meta = metaKey event
        }

    /// Mount serials, so every mount's item ids are unique in the page.
    let mutable private mounts = 0

    /// A touch press waiting to become a tap (released early) or a long-press (held 500 ms).
    type private Touch =
        {
            Pointer: float
            ClientX: float
            ClientY: float
            Target: string option
            Point: Point
            Modifiers: InputModifiers
            mutable Timer: float option
            mutable Fired: bool
        }

    /// The long-press: held this long, moving less than `slop` CSS pixels.
    let private longPressMs = 500

    let private slop = 10.0

    [<Sealed>]
    type Host
        internal
        (
            surface: Element,
            ownership: Ownership,
            backend: RendererBackend,
            label: string,
            width: float,
            height: float,
            stopPointer: bool,
            hitTest: Point -> string option,
            spec: InputHostSpec,
            report: InputEvent -> unit,
            draw: Scene -> SceneNode list -> unit,
            first: InteractiveScene
        ) =
        let serial =
            mounts <- mounts + 1
            mounts

        let prefix = sprintf "fsgg-input-%s-%d" (RendererBackend.id backend) serial
        let listeners = ResizeArray<EventTarget * string * (Event -> unit)>()
        let mirror = document.createElement "div"
        let domIds = System.Collections.Generic.Dictionary<string, string>()
        let elements = System.Collections.Generic.Dictionary<string, Element>()
        let mutable current = first
        let mutable items: InputLayout.Item list = []
        let mutable layout: InputLayout.Layout option = None
        let mutable focused: string option = None
        let mutable hovered: string option = None
        let mutable visible = false
        let mutable drawnCues: InteractionCues option = None
        let mutable touch: Touch option = None
        let mutable disposed = false

        let domId (objectId: string) =
            match domIds.TryGetValue objectId with
            | true, id -> id
            | _ ->
                let id = sprintf "%s-item-%d" prefix (domIds.Count + 1)
                domIds.[objectId] <- id
                id

        let listen (target: EventTarget) (name: string) (handler: Event -> unit) =
            listenCapture target name handler
            listeners.Add(target, name, handler)

        let setAttribute (element: Element) (name: string) (value: string) =
            if element.getAttribute name <> value then
                element.setAttribute (name, value)

        let isReachable (id: string) =
            items |> List.exists (fun item -> item.Id = id)

        /// The presented CSS px per scene unit: ONE scale for both axes. The surface sits at its frame's contain-fit
        /// (CanvasSizing), so this is the fit's scale (its own width / the scene width) times the scale at which any page
        /// transform or zoom presents it; the cue layer reads it, and the pointer mapping inverts the same placement.
        let cssPerUnit () =
            let own = CanvasSizing.ownWidth surface

            if own > 0.0 then own / width * CanvasSizing.presentedScale surface else 1.0

        /// Draw the frame with the cue layer, or only when the cues changed (`always` false).
        let redraw (always: bool) =
            let cues = InteractionCues.create focused hovered visible

            let changed =
                match drawnCues with
                | Some drawn ->
                    InteractionCues.focused drawn <> focused
                    || InteractionCues.hovered drawn <> hovered
                    || InteractionCues.focusVisible drawn <> visible
                | None -> true

            if always || changed then
                drawnCues <- Some cues
                let scale = cssPerUnit ()
                let context = CueContext.create cues current.Objects scale
                draw current.Scene (snapToDevice (scale * window.devicePixelRatio) (InputHostSpec.cues spec context))

        let syncActive () =
            match focused |> Option.filter isReachable with
            | Some id -> setAttribute surface "aria-activedescendant" (domId id)
            | None ->
                if not (isNull (surface.getAttribute "aria-activedescendant")) then
                    surface.removeAttribute "aria-activedescendant"

        let item (role: string) (entry: InputLayout.Item) (name: string option) =
            let element = document.createElement "div"
            element.setAttribute ("role", role)
            element.setAttribute ("id", domId entry.Id)
            element.setAttribute ("data-scene-control-id", entry.Id)
            element.textContent <- entry.Label
            name |> Option.iter (fun name -> element.setAttribute ("aria-label", name))
            elements.[entry.Id] <- element
            element

        /// Rebuild the mirror's items when the structure changed. Items are never focused (the surface keeps the focus
        /// and names the active item), so rebuilding never moves the keyboard focus.
        let rebuild (next: InputLayout.Layout) =
            elements.Clear()

            while not (isNull mirror.firstChild) do
                mirror.removeChild mirror.firstChild |> ignore

            let top = ResizeArray<string>()
            let mutable groups = 0

            let groupId () =
                groups <- groups + 1
                sprintf "%s-group-%d" prefix groups

            match next with
            | InputLayout.Layout.Listbox entries ->
                setAttribute surface "role" "listbox"

                for name in [ "aria-rowcount"; "aria-colcount" ] do
                    surface.removeAttribute name

                for entry in entries do
                    match entry with
                    | InputLayout.Entry.Option one ->
                        let element = item "option" one None
                        top.Add(element.getAttribute "id")
                        mirror.appendChild element |> ignore
                    | InputLayout.Entry.Group(groupLabel, members) ->
                        let group = document.createElement "div"
                        let id = groupId ()
                        group.setAttribute ("role", "group")
                        group.setAttribute ("id", id)
                        group.setAttribute ("aria-label", groupLabel)

                        for one in members do
                            group.appendChild (item "option" one None) |> ignore

                        top.Add id
                        mirror.appendChild group |> ignore
            | InputLayout.Layout.Grid(rowGroups, rowCount, colCount) ->
                setAttribute surface "role" "grid"
                setAttribute surface "aria-rowcount" (string rowCount)
                setAttribute surface "aria-colcount" (string colCount)

                for rowGroup in rowGroups do
                    let group = document.createElement "div"
                    let id = groupId ()
                    group.setAttribute ("role", "rowgroup")
                    group.setAttribute ("id", id)
                    group.setAttribute ("aria-label", rowGroup.Label)

                    for row in rowGroup.Rows do
                        let line = document.createElement "div"
                        line.setAttribute ("role", "row")
                        line.setAttribute ("aria-rowindex", string row.RowIndex)

                        for cell in row.Cells do
                            let element = item "gridcell" cell.Item (Some cell.Name)
                            element.setAttribute ("aria-colindex", string cell.ColIndex)
                            line.appendChild element |> ignore

                        group.appendChild line |> ignore

                    top.Add id
                    mirror.appendChild group |> ignore

            match ownership with
            | Ownership.Owns _ -> setAttribute surface "aria-owns" (String.concat " " top)
            | Ownership.Fallback -> ()

            layout <- Some next

        let syncSelected () =
            for KeyValue(id, element) in elements do
                setAttribute element "aria-selected" (if current.Selected = Some id then "true" else "false")

        let sync () =
            items <- InputLayout.items (InputHostSpec.access spec) current.Objects
            let next = InputLayout.layout (label + " objects") items

            if layout <> Some next then
                rebuild next

            syncSelected ()
            syncActive ()

        let event source action target point modifiers repeat =
            if not disposed then
                report (InputEvent.create source action target point modifiers repeat)

        /// The scene point under a pointer event, through the surface's placement (the fit) and any page transform or
        /// zoom, rotations included: an SVG root through its screen CTM (viewBox units are scene units); a canvas through
        /// the event's offset in its own CSS px (the browser maps it through every transform; zoom is divided out) over
        /// the fit's scale.
        let localPoint (pointer: MouseEvent) : Point =
            let at = pointInScene surface pointer width
            { X = at.[0]; Y = at.[1] }

        /// A pointer press moves the focus to the pressed object (when the keyboard reaches it) without showing it.
        let pressFocus (target: string option) =
            match target |> Option.filter isReachable with
            | Some id -> focused <- Some id
            | None -> ()

            visible <- false
            syncActive ()
            redraw false

        let clearTouch () =
            match touch with
            | Some pending ->
                pending.Timer |> Option.iter clearTimeout
                touch <- None
            | None -> ()

        let contain (event: Event) =
            event.preventDefault ()
            stopImmediate event

        /// Keys whose keydown the host contained and whose keyup has not come yet.
        let containedKeys = System.Collections.Generic.HashSet<string>()

        let containKey (event: Event) (key: string) =
            containedKeys.Add key |> ignore
            contain event

        let release () =
            if not disposed then
                disposed <- true
                clearTouch ()

                for target, name, handler in listeners do
                    unlistenCapture target name handler

                listeners.Clear()
                elements.Clear()

                if not (isNull mirror.parentNode) then
                    mirror.parentNode.removeChild mirror |> ignore

                for name in [ "aria-owns"; "aria-activedescendant" ] do
                    surface.removeAttribute name

        do
            try
                let surfaceTarget = surface :> EventTarget

                setAttribute surface "tabindex" "0"
                setAttribute surface "aria-label" label
                mirror.setAttribute ("data-scene-controls", RendererBackend.id backend)
                mirror.setAttribute ("data-renderer", RendererBackend.id backend)
                mirror.setAttribute ("data-input-host", prefix)
                mirror.setAttribute ("role", "none")

                match ownership with
                | Ownership.Owns parent ->
                    mirror.setAttribute ("style", hiddenStyle)
                    parent.appendChild mirror |> ignore
                | Ownership.Fallback -> surface.appendChild mirror |> ignore

                listen surfaceTarget "pointerdown" (fun raw ->
                    if not disposed then
                        let pointer = raw :?> MouseEvent
                        let point = localPoint pointer
                        let target = hitTest point
                        let modifiers = modifiersOf raw

                        if stopPointer then
                            stopImmediate raw

                        if pointerType raw = "touch" then
                            clearTouch ()

                            let pending =
                                {
                                    Pointer = pointerId raw
                                    ClientX = pointer.clientX
                                    ClientY = pointer.clientY
                                    Target = target
                                    Point = point
                                    Modifiers = modifiers
                                    Timer = None
                                    Fired = false
                                }

                            pending.Timer <-
                                Some(
                                    setTimeout
                                        (fun () ->
                                            match touch with
                                            | Some waiting when obj.ReferenceEquals(waiting, pending) && not pending.Fired ->
                                                pending.Fired <- true
                                                pending.Timer <- None
                                                pressFocus pending.Target

                                                event
                                                    InputSource.Pointer
                                                    InputAction.ActivateSecondary
                                                    pending.Target
                                                    (Some pending.Point)
                                                    pending.Modifiers
                                                    false
                                            | _ -> ())
                                        longPressMs
                                )

                            touch <- Some pending
                        else
                            match button raw with
                            | 0.0 ->
                                pressFocus target
                                event InputSource.Pointer InputAction.Activate target (Some point) modifiers false
                            | 2.0 ->
                                pressFocus target
                                event InputSource.Pointer InputAction.ActivateSecondary target (Some point) modifiers false
                            | _ -> ())

                listen surfaceTarget "pointermove" (fun raw ->
                    if not disposed then
                        let pointer = raw :?> MouseEvent

                        match touch with
                        | Some pending when pending.Pointer = pointerId raw ->
                            let dx = pointer.clientX - pending.ClientX
                            let dy = pointer.clientY - pending.ClientY

                            if dx * dx + dy * dy >= slop * slop then
                                clearTouch ()
                        | _ -> ()

                        if pointerType raw <> "touch" then
                            let point = localPoint pointer
                            let target = hitTest point

                            if target <> hovered then
                                hovered <- target
                                redraw false
                                event InputSource.Pointer InputAction.Hover target (Some point) (modifiersOf raw) false)

                listen surfaceTarget "pointerup" (fun raw ->
                    if not disposed then
                        match touch with
                        | Some pending when pending.Pointer = pointerId raw ->
                            let fired = pending.Fired
                            clearTouch ()

                            if not fired then
                                pressFocus pending.Target

                                event
                                    InputSource.Pointer
                                    InputAction.Activate
                                    pending.Target
                                    (Some pending.Point)
                                    pending.Modifiers
                                    false
                        | _ -> ())

                listen surfaceTarget "pointercancel" (fun raw ->
                    match touch with
                    | Some pending when pending.Pointer = pointerId raw -> clearTouch ()
                    | _ -> ())

                // Leaving fires on each child too (capture sees non-bubbling events); only leaving the surface counts.
                listen surfaceTarget "pointerleave" (fun raw ->
                    if not disposed && targets raw surface && hovered.IsSome && pointerType raw <> "touch" then
                        hovered <- None
                        redraw false
                        event InputSource.Pointer InputAction.Hover None None (modifiersOf raw) false)

                // The page's context menu never opens on the surface: a secondary press, a long-press and the ContextMenu
                // key are reported as `ActivateSecondary` instead.
                listen surfaceTarget "contextmenu" (fun raw -> raw.preventDefault ())

                if stopPointer then
                    listen surfaceTarget "wheel" (fun raw -> stopImmediate raw)

                listen surfaceTarget "focus" (fun raw ->
                    if not disposed && targets raw surface then
                        if (focused |> Option.filter isReachable).IsNone then
                            focused <-
                                current.Selected
                                |> Option.filter isReachable
                                |> Option.orElse (items |> List.tryHead |> Option.map (fun first -> first.Id))

                        visible <- focusVisible surface
                        syncActive ()
                        redraw false)

                listen surfaceTarget "blur" (fun raw ->
                    if not disposed && targets raw surface then
                        containedKeys.Clear()
                        visible <- false
                        redraw false)

                listen surfaceTarget "keydown" (fun raw ->
                    let keyboard = raw :?> KeyboardEvent

                    if not disposed && not (isComposing keyboard) then
                        let key = keyboard.key
                        let modifiers = modifiersOf raw
                        let repeat = isRepeat keyboard

                        // An arrow (or any navigation key) with Alt or Meta held is the browser's (history back and
                        // forward): it is neither reported nor contained.
                        if InputLayout.isNavigation key && (modifiers.Alt || modifiers.Meta) then
                            ()
                        elif InputLayout.isNavigation key then
                            let shape = layout |> Option.defaultValue (InputLayout.Layout.Listbox [])
                            let next = InputLayout.navigate key modifiers.Control focused shape items
                            let moved = next <> focused
                            focused <- next
                            visible <- true
                            syncActive ()
                            redraw false

                            if moved then
                                event InputSource.Keyboard InputAction.Focus next None modifiers repeat

                            containKey raw key
                        else
                            let action =
                                match key with
                                | "Enter"
                                | " " -> Some InputAction.Activate
                                | "ContextMenu" -> Some InputAction.ActivateSecondary
                                | "F10" when keyboard.shiftKey -> Some InputAction.ActivateSecondary
                                | "Escape" -> Some InputAction.Cancel
                                | _ -> None

                            match action with
                            | Some action ->
                                let target = focused |> Option.filter isReachable

                                match action with
                                | InputAction.Cancel -> event InputSource.Keyboard action target None modifiers repeat
                                | _ when target.IsSome ->
                                    visible <- true
                                    redraw false
                                    event InputSource.Keyboard action target None modifiers repeat
                                | _ -> ()

                                containKey raw key
                            | None -> ())

                // The keyup of every key whose keydown was contained is contained too, so no page binding sees half a key.
                listen surfaceTarget "keyup" (fun raw ->
                    let key = (raw :?> KeyboardEvent).key

                    if containedKeys.Remove key then
                        contain raw)

                // A screen reader's click on an item (its default action) activates the item's object.
                listen (mirror :> EventTarget) "click" (fun raw ->
                    match controlIdOf raw.target with
                    | Some id when not disposed && isReachable id ->
                        focused <- Some id
                        visible <- true
                        syncActive ()
                        redraw false
                        event InputSource.Mirror InputAction.Activate (Some id) None (modifiersOf raw) false
                    | _ -> ())

                sync ()
                redraw true

            with _ ->
                // The caller has no Host to dispose until construction returns. Retire partial
                // acquisition here, preserving the original access/cue/draw failure.
                try release () with _ -> ()
                reraise ()

        member _.Update(frame: InteractiveScene) =
            if not disposed then
                current <- frame
                sync ()
                focused <- focused |> Option.filter isReachable

                hovered <-
                    hovered
                    |> Option.filter (fun id -> frame.Objects |> List.exists (fun target -> target.Id = id))

                syncActive ()
                redraw true

        member _.Dispose() = release ()

    let attach surface ownership backend label width height stopPointer hitTest spec report draw frame =
        Host(surface, ownership, backend, label, width, height, stopPointer, hitTest, spec, report, draw, frame)
