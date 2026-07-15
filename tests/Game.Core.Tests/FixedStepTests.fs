module Game.Core.Tests.FixedStepTests

// Ported from FS.GG.Rendering tests/Canvas.Tests/FixedStepTests.fs (ADR-0022 / P2). Only the
// namespace open changes (FS.GG.UI.Canvas → FS.GG.Game.Core). Pure fixed-timestep accumulator
// drain. Deterministic, clamps a runaway frame (default 0.25s), total on degenerate input. dt values
// are negative powers of two so accumulator arithmetic is exact.

open Expecto
open FsCheck
open FS.GG.Game.Core

[<Tests>]
let tests =
    testList "Game.Core FixedStep drain (US3, FR-009/FR-010)" [

        test "default clamp is 0.25s" {
            Expect.equal FixedStep.defaultMaxFrameTime 0.25 "one canonical clamp across the sim core"
        }

        test "an exact multiple of the interval runs that many whole steps with no remainder" {
            // interval = 1/16, frameTime = 0.25 = 4 intervals ⇒ 4 steps, remainder 0.
            let struct (steps, rem) = FixedStep.drain 0.0625 0.25 0.0
            Expect.equal steps 4 "four whole steps"
            Expect.equal rem 0.0 "no remainder"
        }

        test "a carried accumulator is included in the drain" {
            // 1/32 banked + 0.25 frame at interval 1/16 ⇒ still 4 steps, 1/32 carried.
            let struct (steps, rem) = FixedStep.drain 0.0625 0.25 0.03125
            Expect.equal steps 4 "the banked sub-step does not add a whole step"
            Expect.equal rem 0.03125 "the remainder is carried forward"
        }

        test "a sub-interval frame runs zero steps and grows the accumulator" {
            let struct (steps, rem) = FixedStep.drain 0.0625 0.03125 0.0
            Expect.equal steps 0 "not enough time for a step"
            Expect.equal rem 0.03125 "accumulator grows by the frame time"
        }

        test "a runaway frame is clamped (no spiral of death)" {
            // interval 1/16, frameTime 5.0 ⇒ unclamped 80 steps; clamped to 0.25 ⇒ exactly 4.
            let struct (steps, _) = FixedStep.drain 0.0625 5.0 0.0
            Expect.equal steps 4 "clamp caps injected time at 0.25s ⇒ 4 steps, not 80"
        }

        test "a non-positive frame time contributes nothing new (a sub-interval accumulator is preserved)" {
            // acc 1/32 < interval 1/16, so with no new time no step can run and the accumulator is kept.
            Expect.equal (FixedStep.drain 0.0625 -3.0 0.03125) (struct (0, 0.03125)) "negative dt ⇒ no steps, accumulator preserved"
            Expect.equal (FixedStep.drain 0.0625 0.0 0.03125) (struct (0, 0.03125)) "zero dt ⇒ no steps, accumulator preserved"
        }

        test "a non-positive frame still drains a pre-banked accumulator that already holds ≥ one interval" {
            // acc 0.1 > interval 1/16: even with zero new time the banked step must run (correct fixed-step).
            let struct (steps, rem) = FixedStep.drain 0.0625 0.0 0.1
            Expect.equal steps 1 "the already-banked interval drains"
            Expect.floatClose Accuracy.high rem 0.0375 "carrying the sub-step remainder"
        }

        test "a non-positive interval is a no-op (no divide-by-zero / infinite steps)" {
            Expect.equal (FixedStep.drain 0.0 1.0 0.2) (struct (0, 0.2)) "interval = 0 ⇒ struct(0, acc)"
            Expect.equal (FixedStep.drain -0.5 1.0 0.2) (struct (0, 0.2)) "interval < 0 ⇒ struct(0, acc)"
        }

        test "drain is deterministic for identical arguments" {
            let run () = FixedStep.drain 0.125 1.0 0.0
            Expect.equal (run ()) (run ()) "same inputs ⇒ identical struct(steps, rem)"
        }

        test "drainWith uses an explicit tighter clamp than the 0.25 default" {
            // interval 1/16; frameTime 1.0. Default clamp 0.25 ⇒ 4 steps. Explicit 0.05 ⇒ floor(0.05/0.0625)=0.
            let struct (dfltSteps, _) = FixedStep.drain 0.0625 1.0 0.0
            let struct (tightSteps, _) = FixedStep.drainWith 0.05 0.0625 1.0 0.0
            Expect.equal dfltSteps 4 "default 0.25 clamp"
            Expect.equal tightSteps 0 "0.05 clamp caps below one interval ⇒ zero steps"
        }

        testCase "conservation + bounds: newAcc = (acc + clamp dt) - steps*interval ∈ [0, interval) (FsCheck ≥1000)"
        <| fun () ->
            let prop (i: int) (f: int) (a: int) =
                let interval = float (1 + abs (i % 64)) / 64.0 // (0, 1]
                let frameTime = float (f % 500) / 100.0 // may be negative or huge
                let acc = float (abs (a % 64)) / 64.0
                let struct (steps, newAcc) = FixedStep.drain interval frameTime acc
                let clamped = min FixedStep.defaultMaxFrameTime (max 0.0 frameTime)
                let expected = (acc + clamped) - float steps * interval
                // `newAcc >= 0.0` exactly, not `>= -1e-9`: the old tolerance was hiding a real few-ulp
                // negative remainder at exact-multiple boundaries, now clamped in `drainWith`.
                steps >= 0
                && abs (newAcc - expected) < 1e-9
                && newAcc >= 0.0
                && newAcc < interval + 1e-9
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testList "totality — non-finite / nonsense inputs never throw, wedge, or go negative" [
            test "a NaN frame time contributes nothing and does NOT poison the accumulator" {
                // acc 1/32 preserved, no step, finite result — the loop is not wedged.
                let struct (steps, rem) = FixedStep.drain 0.0625 nan 0.03125
                Expect.equal steps 0 "NaN dt ⇒ no steps"
                Expect.equal rem 0.03125 "accumulator preserved, not NaN"
                // and the very next frame still advances normally (no permanent wedge).
                let struct (steps2, _) = FixedStep.drain 0.0625 0.0625 rem
                Expect.equal steps2 1 "the loop keeps working after a NaN frame"
            }
            test "a NaN or infinite interval is a no-op with a finite accumulator" {
                Expect.equal (FixedStep.drain nan 0.1 0.1) (struct (0, 0.1)) "NaN interval ⇒ struct(0, acc)"
                Expect.equal (FixedStep.drain infinity 0.1 0.1) (struct (0, 0.1)) "infinite interval ⇒ struct(0, acc)"
            }
            test "a negative or non-finite accumulator is treated as empty (never negative steps)" {
                Expect.equal (FixedStep.drain 0.0625 0.0 -1.0) (struct (0, 0.0)) "negative acc ⇒ empty, 0 steps"
                Expect.equal (FixedStep.drain 0.0625 0.0 nan) (struct (0, 0.0)) "NaN acc ⇒ empty, 0 steps"
            }
            test "drainWith a degenerate clamp cannot produce negative steps or a NaN accumulator" {
                Expect.equal (FixedStep.drainWith -0.05 0.0625 1.0 0.0) (struct (0, 0.0)) "negative clamp ⇒ no time admitted"
                Expect.equal (FixedStep.drainWith nan 0.0625 5.0 0.0) (struct (0, 0.0)) "NaN clamp ⇒ no time admitted, no poison"
            }
            test "a remainder that rounds negative at an exact-multiple boundary is clamped to zero" {
                // `total / interval` rounds UP across an integer when `total` sits one ulp below an exact
                // multiple; `floor` then yields that multiple and `total - steps*interval` comes out a few
                // ulps NEGATIVE. Witnesses found by search over drain's real signature, then confirmed
                // against the built assembly. Unclamped these return ~-2.8e-17.
                let witnesses =
                    [ 0.06801315413399607, 0.17690726351475394, 0.027132198887234261
                      0.033992989322934299, 0.094482411257251542, 0.0074965567115513453
                      0.071430879530998817, 0.14492674059592309, 0.06936589799707335 ]

                for (interval, frameTime, acc) in witnesses do
                    let struct (steps, rem) = FixedStep.drain interval frameTime acc
                    Expect.equal steps 3 $"drain {interval} {frameTime} {acc} runs three whole steps"
                    Expect.isTrue (rem >= 0.0) $"drain {interval} {frameTime} {acc} ⇒ rem {rem} must not be negative"
                    Expect.isTrue (rem < interval) $"...and still below the interval"
            }

            test "a pathologically tiny interval caps steps at Int32.MaxValue instead of wrapping negative" {
                let interval = 1e-10
                let struct (steps, rem) = FixedStep.drain interval 0.25 0.0 // true count ~2.5e9 > Int32.MaxValue
                Expect.equal steps System.Int32.MaxValue "step count saturates, never wraps negative"
                Expect.isTrue (steps >= 0) "never negative"
                // Saturation is the ONE documented exception to `0 <= newAccumulator < interval` (FixedStep.fsi):
                // the ~0.35e9 steps past Int32.MaxValue stay banked in `rem`, so `rem` exceeds `interval`.
                // The guarantees that DO still hold are pinned here — finite and non-negative.
                Expect.isTrue (System.Double.IsFinite rem) "the banked remainder stays finite"
                Expect.isTrue (rem >= 0.0) "the banked remainder is never negative"
                Expect.isTrue (rem > interval) "documented exception: the uncounted steps overflow the interval bound"
            }
        ]

        testCase "clamp bound: steps never exceed floor((acc + maxClamp)/interval) (FsCheck ≥1000)"
        <| fun () ->
            let prop (i: int) (f: int) (a: int) =
                let interval = float (1 + abs (i % 64)) / 64.0
                let frameTime = float (f % 100000) // deliberately huge
                let acc = float (abs (a % 64)) / 64.0
                let struct (steps, _) = FixedStep.drain interval frameTime acc
                let bound = int (floor ((acc + FixedStep.defaultMaxFrameTime) / interval))
                steps <= bound
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)
    ]
