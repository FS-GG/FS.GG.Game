namespace FS.GG.Game.Render.Browser

open Browser.Types

/// The one sizing model of the input-host surfaces (SVG, Pixi and Babylon; FS.GG.Game#788, ADR 0001 "Surface sizing"):
///
/// - the host mounts a FRAME it owns (`data-input-frame`). Its in-flow content is one sizer at the scene width and no
///   height, and its inline `aspect-ratio` is the scene's, so by default the frame is the scene size; inline
///   `align-self: start` and `justify-self: start` keep a flex or grid parent from stretching it. A page sizes it by
///   setting the frame's own `width` and/or `height` (an `auto` one follows the aspect ratio); the host never writes the
///   frame's size, so page sizing that is added, changed or withdrawn after mount always wins or falls back;
/// - whatever size the frame ends up, the scene is drawn at ONE uniform scale: `fit` computes `scale = min(boxWidth /
///   sceneWidth, boxHeight / sceneHeight)` from the frame's content box, centred and letterboxed, and the tracker places
///   the output (the canvas or SVG root, absolutely positioned, out of layout) at exactly that rectangle. Drawing (the
///   output's box and its backing store), the pointer mapping and picking, and the cue layer's CSS px per scene unit all
///   read that one placement, so they cannot disagree;
/// - the placed fit is snapped to whole device px (its width, height and offset, at the device pixel ratio x the
///   presented scale), so a canvas's backing store maps 1:1 onto it;
/// - a canvas is presented with `image-rendering: pixelated` only while its backing store equals its presented device
///   size on both axes and no transform rotates or skews it (nearest sampling is then exact, where the default filter
///   blends a band edge); otherwise (a non-uniform `scale(2, 1)`, a backing store refused at the cap) it keeps the
///   default filter;
/// - a canvas's backing store is derived from the fit only: fitted CSS size x device pixel ratio (x the scale at which a
///   page transform or zoom presents the frame), rounded, never over `maxSide` device pixels a side (refused with a
///   console diagnostic);
/// - it is re-derived on a ResizeObserver of the frame (its content box and its device-pixel content box, which changes
///   with the device pixel ratio), on a change of the ratio (a `matchMedia` resolution query re-armed at each change,
///   and a per-frame comparison of `devicePixelRatio`), on the window's resize and at each draw, only when the fit or
///   ratio differs from the last one applied.
module internal CanvasSizing =

    /// The largest backing store side, in device pixels.
    val maxSide: int

    /// The uniform contain-fit of a scene in a box, in the box's CSS px: the scale (CSS px per scene unit) and the
    /// centred rectangle the scene occupies.
    type Fit =
        {
            Scale: float
            Left: float
            Top: float
            Width: float
            Height: float
        }

    /// How many presented CSS px one of `element`'s own CSS px is: the product, over it and its ancestors, of each CSS
    /// `zoom` and of the area scale (the square root of the determinant) of each CSS `transform`. 1 without either.
    val presentedScale: element: Element -> float

    /// `element`'s own used CSS width (untransformed, unzoomed), 0 when it is not rendered.
    val ownWidth: element: Element -> float

    /// The contain-fit of a `sceneWidth` x `sceneHeight` scene in a `boxWidth` x `boxHeight` box.
    val fit: boxWidth: float -> boxHeight: float -> sceneWidth: float -> sceneHeight: float -> Fit

    /// Mount an empty frame for `backend` in `container` (scene `width` x `height`). Put the output in it, `place` it,
    /// then `track` or `follow` it. Remove the frame to remove both.
    val frame: container: HTMLElement -> backend: string -> width: float -> height: float -> HTMLElement

    /// Take `output` (already in its frame) out of layout at the scene size, until its tracker places it.
    val place: output: Element -> width: float -> height: float -> unit

    /// Places one output at the contain-fit of its frame and, for a canvas, re-derives its backing store from that fit
    /// and the device pixel ratio.
    [<Sealed>]
    type Tracker =

        /// The last fit placed (the scene size before the frame is first measured).
        member Fit: Fit

        /// Place the output and apply the backing store for the current fit and ratio, if either changed since the last
        /// one applied.
        member Ensure: unit -> unit

        /// Stop observing. Idempotent.
        member Dispose: unit -> unit

    /// Track the canvas `output` in `frame` for a `sceneWidth` x `sceneHeight` scene: `apply backingWidth backingHeight
    /// fit` gives its renderer the new backing store and says whether it could (a renderer still initialising cannot;
    /// the next `Ensure` applies it).
    val track:
        frame: HTMLElement ->
        output: Element ->
        sceneWidth: float ->
        sceneHeight: float ->
        apply: (int -> int -> Fit -> bool) ->
            Tracker

    /// Place the vector `output` (no backing store) at the contain-fit of `frame`, following its size.
    val follow: frame: HTMLElement -> output: Element -> sceneWidth: float -> sceneHeight: float -> Tracker
