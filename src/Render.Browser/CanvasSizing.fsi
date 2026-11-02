namespace FS.GG.Game.Render.Browser

open Browser.Types

/// The one sizing model of the input-host surfaces (SVG, Pixi and Babylon; FS.GG.Game#788, ADR 0001 "Surface sizing"):
///
/// - the host mounts a FRAME it owns (`data-input-frame`), and the canvas or SVG root fills it out of layout
///   (`position:absolute; inset:0; width:100%; height:100%`), so the output's width and height attributes (the backing
///   store) never contribute to layout, whatever their values;
/// - the frame is sized by the page when the page sizes it (a stylesheet rule or an inline style on the frame, or a
///   container that gives it a definite size); otherwise by an in-flow sizer child at the scene size. The host never
///   writes the frame's own size, so page sizing that is added, changed or withdrawn after mount always wins or falls
///   back to the scene size;
/// - the presented size is the frame's content box, and a canvas's backing store is derived from it only: presented CSS
///   size x device pixel ratio, rounded, never over `maxSide` device pixels a side (refused with a console diagnostic);
/// - it is re-derived on a ResizeObserver of the frame (its content box and its device-pixel content box, which changes
///   with the device pixel ratio), on a change of the ratio (a `matchMedia` resolution query re-armed at each change,
///   and a per-frame comparison of `devicePixelRatio`), on the window's resize and at each draw, only when the size or
///   ratio differs from the last one applied.
module internal CanvasSizing =

    /// The largest backing store side, in device pixels.
    val maxSide: int

    /// Mount an empty frame for `backend` in `container` (scene `width` x `height`): its sizer gives it the scene size
    /// unless the page sizes it. Put the output in it, then `fill` it.
    val emptyFrame: container: HTMLElement -> backend: string -> width: float -> height: float -> HTMLElement

    /// Take `output` (already in its frame) out of layout, filling the frame.
    val fill: output: Element -> unit

    /// Mount a frame for `backend` in `container` (scene `width` x `height`) and put `output` (the canvas or SVG root)
    /// inside it, out of layout. Returns the frame; remove it to remove both.
    val frame: container: HTMLElement -> output: Element -> backend: string -> width: float -> height: float -> HTMLElement

    /// Re-derives one canvas's backing store from its frame's presented size and the device pixel ratio.
    [<Sealed>]
    type Tracker =

        /// Apply the backing store for the current presented size and ratio, if either changed since the last one
        /// applied.
        member Ensure: unit -> unit

        /// Stop observing. Idempotent.
        member Dispose: unit -> unit

    /// Track the canvas in `frame`: `apply backingWidth backingHeight cssWidth cssHeight` gives its renderer the new
    /// backing store and says whether it could (a renderer still initialising cannot; the next `Ensure` applies it).
    val track: frame: HTMLElement -> apply: (int -> int -> float -> float -> bool) -> Tracker
