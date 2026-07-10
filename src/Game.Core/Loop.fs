namespace FS.GG.Game.Core

type StepState<'world> =
    { Current: 'world
      Previous: 'world
      Accumulator: float }

[<RequireQualifiedAccess>]
module Loop =

    let init (world: 'world) : StepState<'world> =
        { Current = world
          Previous = world
          Accumulator = 0.0 }

    let advance
        (dt: float)
        (integrate: 'world -> float -> 'world)
        (frameTime: float)
        (state: StepState<'world>)
        : StepState<'world> =
        // The whole point of this module: the clamp, the sanitization, and the step count all come
        // from FixedStep.drain. No second 0.25 constant, no second `while acc >= dt` — a NaN that
        // cannot poison `drain` cannot poison the buffer either.
        let struct (steps, carry) = FixedStep.drain dt frameTime state.Accumulator

        // `previous <- current` INSIDE the fold, not before it: after N steps, `Previous` is the
        // world before the Nth step, which is the interval the renderer interpolates across. Hoisting
        // it out would leave `Previous` N-1 steps stale and stretch the lerp over the whole frame.
        let mutable previous = state.Previous
        let mutable current = state.Current

        for _ in 1..steps do
            previous <- current
            current <- integrate current dt

        { Current = current
          Previous = previous
          Accumulator = carry }

    let alpha (dt: float) (state: StepState<'world>) : float =
        // Total for a hand-built StepState too — `advance` guarantees a finite accumulator in [0, dt),
        // but nothing stops a caller constructing the record directly, and a NaN alpha silently
        // corrupts every interpolated draw call downstream.
        if not (System.Double.IsFinite dt) || dt <= 0.0 then
            0.0
        elif not (System.Double.IsFinite state.Accumulator) || state.Accumulator <= 0.0 then
            0.0
        else
            // Explicit comparison, not `min 1.0 t`: F#'s generic `min` propagates NaN, and a NaN alpha
            // is precisely the failure this module was extracted to prevent. `t` cannot be NaN given the
            // guards above (both operands finite, positive, so no 0/0 and no inf/inf) — the point is
            // that the clamp stays correct if a future guard is relaxed. `t` may still be +inf when a
            // hand-built accumulator dwarfs a denormal `dt`, and that must read as a full step.
            let t = state.Accumulator / dt
            if t > 1.0 then 1.0 else t
