namespace FS.GG.Game.Render.Browser

open Browser.Types

/// Checks the canvas adapters (Babylon, Pixi) make before they touch their engine.
module internal CanvasSupport =

    /// Whether the browser can create a WebGL context at all, asked of a throwaway canvas whose context is released
    /// at once. A rendering engine may register itself (Babylon's engine store) or start an asynchronous
    /// initialisation before it asks for a context, so a failure it hits later leaves a half-built engine with no
    /// handle to release; asking first keeps the refusal outside the engine.
    val webGlAvailable: unit -> bool

    /// Finite and greater than zero: a usable canvas dimension.
    val isFinitePositive: value: float -> bool

    /// Lose the canvas's WebGL context now. Disposing an engine does not release its context (a browser keeps about
    /// sixteen and warns when they run out), and an unreferenced canvas holds its context until it is collected, so a
    /// runtime backend switch would otherwise leave one live context per mount behind. A canvas with no live context is
    /// left alone.
    val loseContext: canvas: HTMLCanvasElement -> unit
