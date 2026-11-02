namespace FS.GG.Game.Render.Browser

open Browser.Types

/// The one sizing model of the input-host canvases (Pixi and Babylon, FS.GG.Game#788; ADR 0001, "Canvas sizing"):
///
/// - the PRESENTED CSS size is owned either by the page (an inline style or a stylesheet rule sizes the canvas) or by
///   the host (the scene size), decided once by `claim`, right after the canvas is attached and before any backing
///   store change; a host-owned canvas gets the scene size as its inline CSS width and height;
/// - the BACKING STORE is derived only: presented CSS size x device pixel ratio, rounded. It never feeds back into the
///   CSS size;
/// - a `Tracker` re-derives it when the presented CSS size or the device pixel ratio differs from the last one it
///   applied (at each draw, and on a ResizeObserver whose notifications for an unchanged CSS size are ignored);
/// - a backing store over `maxSide` device pixels on either side is refused, with a console diagnostic, never grown.
module internal CanvasSizing =

    /// The largest backing store side, in device pixels.
    val maxSide: int

    /// Decide who owns `canvas`'s presented size, now that it is attached: true when the page sizes it (an inline
    /// width, or a stylesheet rule: its computed width does not follow its width attribute). When the page does not,
    /// give it `width` x `height` as its inline CSS size. Call once, after `appendChild` and before the canvas has a
    /// context or any backing-store change.
    val claim: canvas: HTMLCanvasElement -> width: float -> height: float -> bool

    /// Re-derives one canvas's backing store from its presented size.
    [<Sealed>]
    type Tracker =

        /// Apply the backing store for the current presented CSS size and device pixel ratio, if either changed since
        /// the last one applied.
        member Ensure: unit -> unit

        /// Stop observing. Idempotent.
        member Dispose: unit -> unit

    /// Track `canvas`: `apply backingWidth backingHeight cssWidth cssHeight` gives the canvas's renderer its new backing
    /// store and says whether it could (a renderer still initialising cannot; the next `Ensure` applies it then).
    val track: canvas: HTMLCanvasElement -> apply: (int -> int -> float -> float -> bool) -> Tracker
