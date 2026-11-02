namespace FS.GG.Game.Render.Browser

open Browser.Types
open Fable.Core

module internal CanvasSizing =

    let maxSide = 8192

    [<Emit("(() => { const c = $0; if (c.style.width) return true; const before = getComputedStyle(c).width; c.width = c.width + 1; const after = getComputedStyle(c).width; c.width = c.width - 1; return before === after })()")>]
    let private sizedByPage (_canvas: HTMLCanvasElement) : bool = jsNative

    [<Emit("(($0.style.width = $1 + 'px'), ($0.style.height = $2 + 'px'))")>]
    let private setCssSize (_canvas: HTMLCanvasElement) (_width: float) (_height: float) : unit = jsNative

    [<Emit("(window.devicePixelRatio || 1)")>]
    let private devicePixelRatio () : float = jsNative

    /// Observe `element`'s content-box size; the callback gets (width, height) in CSS px. Returns the disconnect.
    [<Emit("(() => { const o = new ResizeObserver(es => { for (const e of es) { const b = e.contentBoxSize && e.contentBoxSize[0]; $1(b ? b.inlineSize : e.contentRect.width, b ? b.blockSize : e.contentRect.height) } }); o.observe($0); return () => o.disconnect() })()")>]
    let private observe (_element: HTMLCanvasElement) (_changed: float -> float -> unit) : (unit -> unit) = jsNative

    [<Emit("console.error($0)")>]
    let private diagnose (_message: string) : unit = jsNative

    let claim (canvas: HTMLCanvasElement) (width: float) (height: float) =
        let page = sizedByPage canvas

        if not page then
            setCssSize canvas width height

        page

    [<Sealed>]
    type Tracker internal (canvas: HTMLCanvasElement, apply: int -> int -> float -> float -> bool) =
        let mutable applied: (float * float * float) option = None
        let mutable refused: (float * float * float) option = None
        let mutable stop: (unit -> unit) option = None

        let derive (cssWidth: float) (cssHeight: float) =
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
                                    "FS.GG.Game.Render.Browser: refused a %dx%d backing store (over %d device px a side) for a canvas presented at %gx%g CSS px at DPR %g; the canvas keeps its last backing store"
                                    backingWidth
                                    backingHeight
                                    maxSide
                                    cssWidth
                                    cssHeight
                                    dpr
                            )
                    elif apply backingWidth backingHeight cssWidth cssHeight then
                        applied <- Some key

        do stop <- Some(observe canvas derive)

        member _.Ensure() =
            let bounds = canvas.getBoundingClientRect ()
            derive bounds.width bounds.height

        member _.Dispose() =
            stop |> Option.iter (fun disconnect -> disconnect ())
            stop <- None

    let track (canvas: HTMLCanvasElement) (apply: int -> int -> float -> float -> bool) = Tracker(canvas, apply)
