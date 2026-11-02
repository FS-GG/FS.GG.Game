namespace FS.GG.Game.Render.Browser

open Browser.Dom
open Browser.Types
open Fable.Core

module internal CanvasSizing =

    let maxSide = 8192

    [<Emit("(() => { const f = document.createElement('div'); f.setAttribute('data-input-frame', $1); f.style.position = 'relative'; f.style.display = 'inline-block'; f.style.verticalAlign = 'top'; f.style.lineHeight = '0'; const s = document.createElement('div'); s.setAttribute('data-input-frame-sizer', ''); s.style.width = $2 + 'px'; s.style.height = $3 + 'px'; f.appendChild(s); $0.appendChild(f); return f })()")>]
    let private makeFrame (_container: HTMLElement) (_backend: string) (_width: float) (_height: float) : HTMLElement = jsNative

    [<Emit("(($0.style.position = 'absolute'), ($0.style.left = '0'), ($0.style.top = '0'), ($0.style.width = '100%'), ($0.style.height = '100%'), ($0.style.display = 'block'))")>]
    let private fillStyle (_output: Element) : unit = jsNative

    let emptyFrame (container: HTMLElement) (backend: string) (width: float) (height: float) = makeFrame container backend width height

    let fill (output: Element) = fillStyle output

    let frame (container: HTMLElement) (output: Element) (backend: string) (width: float) (height: float) =
        let made = makeFrame container backend width height
        made.appendChild output |> ignore
        fillStyle output
        made

    [<Emit("(window.devicePixelRatio || 1)")>]
    let private devicePixelRatio () : float = jsNative

    /// The frame's content box in CSS px, [width, height].
    [<Emit("(() => { const s = getComputedStyle($0); const r = $0.getBoundingClientRect(); const px = v => parseFloat(v) || 0; return [r.width - px(s.paddingLeft) - px(s.paddingRight) - px(s.borderLeftWidth) - px(s.borderRightWidth), r.height - px(s.paddingTop) - px(s.paddingBottom) - px(s.borderTopWidth) - px(s.borderBottomWidth)] })()")>]
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
    type Tracker internal (frame: HTMLElement, apply: int -> int -> float -> float -> bool) =
        let mutable applied: (float * float * float) option = None
        let mutable refused: (float * float * float) option = None

        let derive () =
            let box = contentBox frame
            let cssWidth, cssHeight = box.[0], box.[1]

            if cssWidth > 0.0 && cssHeight > 0.0 then
                let dpr = devicePixelRatio ()
                let key = (cssWidth, cssHeight, dpr)

                if applied <> Some key then
                    let backingWidth = int (System.Math.Round(cssWidth * dpr))
                    let backingHeight = int (System.Math.Round(cssHeight * dpr))

                    if backingWidth > maxSide || backingHeight > maxSide then
                        if refused <> Some key then
                            refused <- Some key

                            diagnose (
                                sprintf
                                    "FS.GG.Game.Render.Browser: refused a %dx%d backing store (over %d device px a side) for a surface presented at %gx%g CSS px at DPR %g; it keeps its last backing store"
                                    backingWidth
                                    backingHeight
                                    maxSide
                                    cssWidth
                                    cssHeight
                                    dpr
                            )
                    elif apply backingWidth backingHeight cssWidth cssHeight then
                        applied <- Some key

        let stops = [ observe frame derive; watchRatio derive ]
        let mutable live = true

        member _.Ensure() = if live then derive ()

        member _.Dispose() =
            if live then
                live <- false

                for stop in stops do
                    stop ()

    let track (frame: HTMLElement) (apply: int -> int -> float -> float -> bool) = Tracker(frame, apply)
