namespace FS.GG.Game.Core

[<RequireQualifiedAccess>]
module FixedStep =

    // The canonical spiral-of-death cap for the sim loop.
    let defaultMaxFrameTime = 0.25

    let drainWith
        (maxFrameTime: float)
        (interval: float)
        (frameTime: float)
        (accumulator: float)
        : struct (int * float) =
        // Totality: the documented invariants (steps >= 0, 0 <= newAccumulator < interval) must hold for
        // EVERY input. A single non-finite value (a NaN `dt` from a first-frame or timer glitch) must
        // never poison the accumulator and permanently wedge the loop, and a nonsense accumulator must
        // never yield a negative step count — so each operand is sanitized before the arithmetic.
        let acc =
            if System.Double.IsFinite accumulator && accumulator > 0.0 then accumulator else 0.0

        if not (System.Double.IsFinite interval) || interval <= 0.0 then
            struct (0, acc)
        else
            // A non-finite frame contributes nothing; a runaway frame is capped by maxFrameTime (itself
            // sanitized), so catch-up can never spiral.
            let cap =
                if System.Double.IsFinite maxFrameTime && maxFrameTime > 0.0 then maxFrameTime else 0.0

            let frame = if System.Double.IsFinite frameTime then max 0.0 frameTime else 0.0
            let total = acc + min cap frame
            // Closed-form whole steps (no drain loop); cap at Int32.MaxValue so a pathologically tiny
            // interval can never wrap the count negative. This saturation is the ONE documented exception
            // to `newAccumulator < interval` (see FixedStep.fsi): the steps beyond Int32.MaxValue stay
            // banked in `rem`, so `rem` can exceed `interval` — it stays finite and non-negative, and no
            // real sim interval (a frame or physics tick) is ever tiny enough to reach the cap.
            let stepsF = floor (total / interval)
            let steps = if stepsF >= 2147483647.0 then 2147483647 else int stepsF
            let rem = total - float steps * interval
            // `total / interval` can round UP across an integer when `total` sits one ulp below an exact
            // multiple, so `floor` yields that multiple and the remainder comes out a few ulps negative
            // — e.g. drain 0.06801315413399607 0.17690726351475394 0.027132198887234261 = -2.78e-17.
            // Small, but it breaks the documented `0 <= newAccumulator` and hands a negative accumulator
            // to the next frame. Clamp with an explicit comparison, not `max 0.0 rem`: F#'s generic
            // `max` PROPAGATES NaN (`max 0.0 nan = nan`), which is exactly the poison this module exists
            // to keep out of the loop.
            struct (steps, (if rem > 0.0 then rem else 0.0))

    let drain (interval: float) (frameTime: float) (accumulator: float) : struct (int * float) =
        drainWith defaultMaxFrameTime interval frameTime accumulator
