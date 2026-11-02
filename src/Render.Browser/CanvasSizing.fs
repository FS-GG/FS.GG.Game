namespace FS.GG.Game.Render.Browser

open Browser.Dom
open Browser.Types
open Fable.Core

module internal CanvasSizing =

    let maxSide = 8192

    type Fit =
        {
            Scale: float
            Left: float
            Top: float
            Width: float
            Height: float
        }

    let fit (boxWidth: float) (boxHeight: float) (sceneWidth: float) (sceneHeight: float) =
        let scale = min (boxWidth / sceneWidth) (boxHeight / sceneHeight)
        let width = sceneWidth * scale
        let height = sceneHeight * scale

        {
            Scale = scale
            Left = (boxWidth - width) / 2.0
            Top = (boxHeight - height) / 2.0
            Width = width
            Height = height
        }

    // The frame's defaults are inline so that page rules aimed at host-internal divs cannot undo them; a page rule that
    // sets the frame's width or height still wins over the aspect ratio, which only fills an `auto` dimension. The sizer
    // is the frame's only in-flow child: the scene width, no height (the aspect ratio gives the frame its height), and
    // nothing a page rule on `div` can add to it.
    [<Emit("(() => { const f = document.createElement('div'); f.setAttribute('data-input-frame', $1); const st = f.style; st.position = 'relative'; st.display = 'inline-block'; st.verticalAlign = 'top'; st.lineHeight = '0'; st.aspectRatio = 'auto ' + $2 + ' / ' + $3; st.alignSelf = 'start'; st.justifySelf = 'start'; const s = document.createElement('div'); s.setAttribute('data-input-frame-sizer', ''); const ss = s.style; ss.display = 'block'; ss.boxSizing = 'content-box'; ss.width = $2 + 'px'; ss.minWidth = '0'; ss.maxWidth = 'none'; ss.height = '0'; ss.minHeight = '0'; ss.maxHeight = 'none'; ss.padding = '0'; ss.border = '0'; ss.margin = '0'; f.appendChild(s); $0.appendChild(f); return f })()")>]
    let private makeFrame (_container: HTMLElement) (_backend: string) (_width: float) (_height: float) : HTMLElement = jsNative

    [<Emit("(($0.style.position = 'absolute'), ($0.style.display = 'block'), ($0.style.boxSizing = 'border-box'), ($0.style.margin = '0'), ($0.style.padding = '0'), ($0.style.border = '0'), ($0.style.maxWidth = 'none'), ($0.style.maxHeight = 'none'), ($0.style.minWidth = '0'), ($0.style.minHeight = '0'), ($0.style.left = $1 + 'px'), ($0.style.top = $2 + 'px'), ($0.style.width = $3 + 'px'), ($0.style.height = $4 + 'px'))")>]
    let private placeAt (_output: Element) (_left: float) (_top: float) (_width: float) (_height: float) : unit = jsNative

    let frame (container: HTMLElement) (backend: string) (width: float) (height: float) = makeFrame container backend width height

    let place (output: Element) (width: float) (height: float) = placeAt output 0.0 0.0 width height

    [<Emit("(() => { let k = 1; for (let e = $0; e; e = e.parentElement) { const s = getComputedStyle(e); if (s.transform && s.transform !== 'none') { const m = new DOMMatrixReadOnly(s.transform); k *= Math.sqrt(Math.abs(m.a * m.d - m.b * m.c)) } const z = parseFloat(s.zoom); if (z > 0) k *= z } return k > 0 && isFinite(k) ? k : 1 })()")>]
    let private presentedScaleOf (_element: Element) : float = jsNative

    let presentedScale (element: Element) = presentedScaleOf element

    /// Whether every CSS transform on `element` and its ancestors keeps axes aligned (no rotation or skew).
    [<Emit("(() => { for (let e = $0; e; e = e.parentElement) { const t = getComputedStyle(e).transform; if (t && t !== 'none') { const m = new DOMMatrixReadOnly(t); if (Math.abs(m.b) > 1e-9 || Math.abs(m.c) > 1e-9) return false } } return true })()")>]
    let private axisAligned (_element: Element) : bool = jsNative

    /// Whether `canvas`'s backing store is exactly its presented device size on both axes (within half a device px).
    [<Emit("(() => { const r = $0.getBoundingClientRect(); const d = window.devicePixelRatio || 1; return $0.width > 0 && $0.height > 0 && Math.abs(r.width * d - $0.width) < 0.5 && Math.abs(r.height * d - $0.height) < 0.5 })()")>]
    let private backingIsPresented (_canvas: Element) : bool = jsNative

    [<Emit("($0.style.imageRendering = $1)")>]
    let private setImageRendering (_element: Element) (_value: string) : unit = jsNative

    [<Emit("(parseFloat(getComputedStyle($0).width) || 0)")>]
    let private ownWidthOf (_element: Element) : float = jsNative

    let ownWidth (element: Element) = ownWidthOf element

    [<Emit("(window.devicePixelRatio || 1)")>]
    let private devicePixelRatio () : float = jsNative

    /// The frame's content box in its own (untransformed, unzoomed) CSS px, and where it starts in the padding box the
    /// output is positioned in: [width, height, left, top]. Zero when the frame is not rendered.
    [<Emit("(() => { if (!$0.isConnected) return [0, 0, 0, 0]; const s = getComputedStyle($0); const px = v => parseFloat(v) || 0; if (s.display === 'none') return [0, 0, 0, 0]; const pl = px(s.paddingLeft), pr = px(s.paddingRight), pt = px(s.paddingTop), pb = px(s.paddingBottom); const bl = px(s.borderLeftWidth), br = px(s.borderRightWidth), bt = px(s.borderTopWidth), bb = px(s.borderBottomWidth); const border = s.boxSizing === 'border-box'; const w = px(s.width) - (border ? pl + pr + bl + br : 0); const h = px(s.height) - (border ? pt + pb + bt + bb : 0); return [w, h, pl, pt] })()")>]
    let private contentBox (_frame: HTMLElement) : float[] = jsNative

    /// Observe `element`'s content box AND its device-pixel content box (which changes with the device pixel ratio), and
    /// the window's resize; returns the function that stops observing.
    [<Emit("(() => { const o = new ResizeObserver(() => $1()); o.observe($0); let d = null; try { d = new ResizeObserver(() => $1()); d.observe($0, { box: 'device-pixel-content-box' }) } catch (_) { d = null } const w = () => $1(); window.addEventListener('resize', w); return () => { o.disconnect(); if (d) d.disconnect(); window.removeEventListener('resize', w) } })()")>]
    let private observe (_element: HTMLElement) (_changed: unit -> unit) : (unit -> unit) = jsNative

    /// Call `changed` whenever the device pixel ratio changes: a `matchMedia` resolution query re-armed at each change,
    /// and, because not every way of changing the ratio fires it (a device-metrics override), a per-frame comparison of
    /// `devicePixelRatio` (one property read a frame). Returns the function that stops watching.
    [<Emit("(() => { let q = null; let stopped = false; let last = window.devicePixelRatio || 1; let frame = 0; const fire = () => { if (stopped) return; last = window.devicePixelRatio || 1; $0(); arm() }; const arm = () => { if (stopped) return; if (q) q.removeEventListener('change', fire); q = matchMedia('(resolution: ' + (window.devicePixelRatio || 1) + 'dppx)'); q.addEventListener('change', fire, { once: true }) }; const tick = () => { if (stopped) return; if ((window.devicePixelRatio || 1) !== last) fire(); frame = requestAnimationFrame(tick) }; arm(); frame = requestAnimationFrame(tick); return () => { stopped = true; cancelAnimationFrame(frame); if (q) q.removeEventListener('change', fire) } })()")>]
    let private watchRatio (_changed: unit -> unit) : (unit -> unit) = jsNative

    [<Emit("console.error($0)")>]
    let private diagnose (_message: string) : unit = jsNative

    [<Sealed>]
    type Tracker
        internal
        (
            frame: HTMLElement,
            output: Element,
            sceneWidth: float,
            sceneHeight: float,
            apply: (int -> int -> Fit -> bool) option
        ) =
        let mutable current = fit sceneWidth sceneHeight sceneWidth sceneHeight
        let mutable placed: (float * float * float * float) option = None
        let mutable applied: (float * float * float) option = None
        let mutable refused: (float * float * float) option = None
        let mutable rendering = ""

        let derive () =
            let box = contentBox frame
            let boxWidth, boxHeight = box.[0], box.[1]

            if boxWidth > 0.0 && boxHeight > 0.0 then
                // Device px per CSS px of the frame's own: the ratio, and any page transform or zoom that presents the
                // frame larger or smaller, so the backing store is the presented density.
                let dpr = devicePixelRatio () * presentedScale frame
                // The fit, snapped to whole device px (its size and its offset in the frame), so a canvas's backing
                // store maps 1:1 onto the presented box and the cue layer's device-px bands stay solid.
                let exact = fit boxWidth boxHeight sceneWidth sceneHeight
                let snap (v: float) = System.Math.Round(v * dpr) / dpr
                let backingWidth = int (System.Math.Round(exact.Width * dpr))
                let backingHeight = int (System.Math.Round(exact.Height * dpr))

                let next =
                    if backingWidth > 0 && backingHeight > 0 then
                        let width = float backingWidth / dpr

                        {
                            Scale = width / sceneWidth
                            Left = snap exact.Left
                            Top = snap exact.Top
                            Width = width
                            Height = float backingHeight / dpr
                        }
                    else
                        exact

                let left = box.[2] + next.Left
                let top = box.[3] + next.Top
                let at = (left, top, next.Width, next.Height)
                current <- next

                if placed <> Some at then
                    placed <- Some at
                    placeAt output left top next.Width next.Height

                match apply with
                | None -> ()
                | Some apply ->
                    let key = (next.Width, next.Height, dpr)

                    if applied <> Some key then

                        if backingWidth > maxSide || backingHeight > maxSide then
                            if refused <> Some key then
                                refused <- Some key

                                diagnose (
                                    sprintf
                                        "FS.GG.Game.Render.Browser: refused a %dx%d backing store (over %d device px a side) for a surface fitted at %gx%g CSS px at %g device px per CSS px; it keeps its last backing store"
                                        backingWidth
                                        backingHeight
                                        maxSide
                                        next.Width
                                        next.Height
                                        dpr
                                )
                        elif apply backingWidth backingHeight next then
                            applied <- Some key

                    // Nearest sampling only where it is exact: the backing store IS the presented device size on both
                    // axes (no rotation or skew), so each backing px is one device px and the default filter would only
                    // blend a solid band edge (as under `scale(1.5)`). Anywhere else the browser must resample (a
                    // non-uniform `scale(2, 1)`, whose density is the area scale; a backing store refused at the cap;
                    // a renderer not yet started), and the default filter keeps that smooth instead of stepped.
                    let wanted = if axisAligned frame && backingIsPresented output then "pixelated" else ""

                    if wanted <> rendering then
                        rendering <- wanted
                        setImageRendering output wanted

        let stops =
            [
                observe frame derive
                match apply with
                | Some _ -> watchRatio derive
                | None -> ignore
            ]

        let mutable live = true

        member _.Fit = current

        member _.Ensure() = if live then derive ()

        member _.Dispose() =
            if live then
                live <- false

                for stop in stops do
                    stop ()

    let track (frame: HTMLElement) (output: Element) (sceneWidth: float) (sceneHeight: float) (apply: int -> int -> Fit -> bool) =
        Tracker(frame, output, sceneWidth, sceneHeight, Some apply)

    let follow (frame: HTMLElement) (output: Element) (sceneWidth: float) (sceneHeight: float) =
        Tracker(frame, output, sceneWidth, sceneHeight, None)
