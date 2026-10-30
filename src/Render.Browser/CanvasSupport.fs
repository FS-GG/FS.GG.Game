namespace FS.GG.Game.Render.Browser

open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

module internal CanvasSupport =

    // An expression inside an ordinary function, not an `[<Emit>]` value: a signature file hides an Emit attribute
    // from Fable, which would then import a function this module never exports.
    let webGlAvailable () : bool =
        emitJsExpr
            0
            "(() => { const c = document.createElement('canvas'); const g = c.getContext('webgl2') || c.getContext('webgl'); if (!g) return false; const l = g.getExtension('WEBGL_lose_context'); if (l) l.loseContext(); return true })()"

    let isFinitePositive (value: float) =
        not (System.Double.IsNaN value || System.Double.IsInfinity value) && value > 0.0

    // Private to this file, so the signature file does not hide the Emit attribute from Fable.
    [<Emit("(() => { for (const type of ['webgl2', 'webgl']) { const g = $0.getContext(type); if (g && !g.isContextLost()) { const l = g.getExtension('WEBGL_lose_context'); if (l) l.loseContext(); return } } })()")>]
    let private loseContextOf (canvas: HTMLCanvasElement) : unit = jsNative

    let loseContext (canvas: HTMLCanvasElement) : unit = loseContextOf canvas
