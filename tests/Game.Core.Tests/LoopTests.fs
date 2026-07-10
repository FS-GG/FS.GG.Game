module Game.Core.Tests.LoopTests

// The double step buffer, on top of FixedStep.drain. Where FixedStepTests pins the accumulator, these
// pin the two-world bracket: which world lands in `Previous` when a frame runs several steps, that a
// non-finite input degrades instead of wedging the loop (FS-GG/FS.GG.Rendering#266), and that a
// scripted frameTime sequence reproduces an identical StepState chain. dt values are negative powers
// of two so accumulator arithmetic is exact and the goldens are literal, not approximate.

open Expecto
open FsCheck
open FS.GG.Game.Core

/// A world that records every dt it was stepped with, so a test can see how many steps ran, in what
/// order, and with what timestep — `Current` alone cannot distinguish one step of 2dt from two of dt.
type private Trace = { Ticks: int; Dts: float list }

let private origin = { Ticks = 0; Dts = [] }
let private tick (w: Trace) (dt: float) = { Ticks = w.Ticks + 1; Dts = w.Dts @ [ dt ] }

/// The plain continuous world: a position moving at unit velocity.
let private move (x: float) (dt: float) = x + dt

let private dt = 0.0625 // 1/16

[<Tests>]
let tests =
    testList "Game.Core Loop double step buffer (US3, FR-009/FR-010)" [

        test "init seeds both worlds from the same value with an empty accumulator" {
            let s = Loop.init 42.0
            Expect.equal s.Current 42.0 "Current is the world as given"
            Expect.equal s.Previous 42.0 "Previous = Current, so the first frame lerps nowhere"
            Expect.equal s.Accumulator 0.0 "no banked time"
            Expect.equal (Loop.alpha dt s) 0.0 "alpha is 0 before any time accumulates"
        }

        test "a sub-step frame runs no steps and banks the time" {
            let s = Loop.advance dt move 0.03125 (Loop.init 0.0)
            Expect.equal s.Current 0.0 "no whole step ⇒ the world does not move"
            Expect.equal s.Previous 0.0 "and Previous is untouched"
            Expect.equal s.Accumulator 0.03125 "the half-step is carried"
            Expect.equal (Loop.alpha dt s) 0.5 "halfway between the two worlds"
        }

        test "one step advances Current and leaves Previous at the pre-step world" {
            let s = Loop.advance dt move dt (Loop.init 0.0)
            Expect.equal s.Current dt "one whole step"
            Expect.equal s.Previous 0.0 "the world before it"
            Expect.equal s.Accumulator 0.0 "nothing carried"
        }

        test "Previous is the world before the LAST step, not before the frame" {
            // 4 * (1/16) = 0.25 ⇒ exactly 4 steps from an empty accumulator.
            let s = Loop.advance dt tick 0.25 (Loop.init origin)
            Expect.equal s.Current.Ticks 4 "four whole steps ran"
            Expect.equal s.Previous.Ticks 3 "Previous is one step behind Current — not four"
            Expect.equal s.Accumulator 0.0 "no remainder"
        }

        test "every step is integrated with the fixed dt, never the frame time" {
            // A variable dt is one of the three ways to lose replay; the loop must never leak frameTime.
            let s = Loop.advance dt tick 0.25 (Loop.init origin)
            Expect.equal s.Current.Dts [ dt; dt; dt; dt ] "four steps, each of exactly dt"
        }

        test "a runaway frame is clamped by FixedStep's spiral-of-death cap (no second constant)" {
            // frameTime 5.0 at dt = 1/16 would be 80 steps unclamped; the 0.25 cap admits exactly 4.
            let s = Loop.advance dt tick 5.0 (Loop.init origin)
            Expect.equal s.Current.Ticks 4 "the clamp is FixedStep.defaultMaxFrameTime, inherited"

            let struct (steps, _) = FixedStep.drain dt 5.0 0.0
            Expect.equal s.Current.Ticks steps "advance runs exactly the count drain reports"
        }

        test "a banked accumulator drains even when the new frame contributes no time" {
            let banked = { Loop.init 0.0 with Accumulator = 0.1 } // 0.1 > 1/16
            let s = Loop.advance dt move 0.0 banked
            Expect.equal s.Current dt "the already-banked interval runs"
            Expect.floatClose Accuracy.high s.Accumulator 0.0375 "the sub-step remainder is carried"
        }

        testList "totality — a non-finite input degrades; it never wedges the loop (FS-GG/FS.GG.Rendering#266)" [

            test "a NaN frame time neither steps nor poisons the accumulator, and the NEXT frame still advances" {
                // This is the #266 repro. Canvas.Loop propagates NaN into Accumulator; `NaN >= dt` is
                // false forever, so the sim silently freezes and alpha is NaN for the rest of the run.
                let s0 = Loop.advance dt tick nan { Loop.init origin with Accumulator = 0.03125 }
                Expect.equal s0.Current.Ticks 0 "NaN frameTime ⇒ no steps"
                Expect.equal s0.Accumulator 0.03125 "the accumulator is preserved, not NaN"
                Expect.isTrue (System.Double.IsFinite(Loop.alpha dt s0)) "alpha stays finite"

                // The loop is not wedged: a normal frame after the bad one steps as if nothing happened.
                let s1 = Loop.advance dt tick 0.03125 s0
                Expect.equal s1.Current.Ticks 1 "the loop keeps stepping after a NaN frame"
                Expect.equal s1.Accumulator 0.0 "and the accumulator is back to exact"
            }

            test "an infinite frame time contributes no time — it is not clamped to 0.25s" {
                // Inherited from FixedStep.drain, and deliberately: a non-finite frame is a broken
                // timer reading, not a very long frame. Admitting 0.25s of it would step the sim on
                // garbage. A runaway *finite* frame is what the clamp is for.
                let s = Loop.advance dt tick infinity { Loop.init origin with Accumulator = 0.03125 }
                Expect.equal s.Current.Ticks 0 "+inf ⇒ no steps"
                Expect.equal s.Accumulator 0.03125 "the banked time survives, unpoisoned"

                let sNeg = Loop.advance dt tick -infinity (Loop.init origin)
                Expect.equal sNeg.Current.Ticks 0 "-inf ⇒ no steps"
                Expect.equal sNeg.Accumulator 0.0 "and no poison"
            }

            test "a non-finite or non-positive dt runs no steps and leaves the worlds untouched" {
                for badDt in [ nan; infinity; -infinity; 0.0; -0.5 ] do
                    let s = Loop.advance badDt tick 1.0 (Loop.init origin)
                    Expect.equal s.Current.Ticks 0 $"dt = {badDt} ⇒ no steps"
                    Expect.equal s.Previous.Ticks 0 $"dt = {badDt} ⇒ Previous untouched"
                    Expect.isTrue (System.Double.IsFinite s.Accumulator) $"dt = {badDt} ⇒ finite accumulator"
                    Expect.equal (Loop.alpha badDt s) 0.0 $"dt = {badDt} ⇒ alpha degrades to 0"
            }

            test "a non-finite or negative accumulator is treated as empty, never propagated" {
                for badAcc in [ nan; infinity; -infinity; -1.0 ] do
                    let s = Loop.advance dt tick 0.0 { Loop.init origin with Accumulator = badAcc }
                    Expect.equal s.Current.Ticks 0 $"acc = {badAcc} ⇒ no steps"
                    Expect.equal s.Accumulator 0.0 $"acc = {badAcc} ⇒ emptied, not carried"
            }

            test "the accumulator never comes back negative at an exact-multiple boundary" {
                // advance inherits drain's remainder, so it inherited drain's few-ulp negative remainder
                // when `total` lands one ulp below an exact multiple of dt. Same witnesses as
                // FixedStepTests — pinned here too, because it is `StepState.Accumulator` whose contract
                // says `[0, dt)` and it is the value that survives into the next frame.
                let witnesses =
                    [ 0.06801315413399607, 0.17690726351475394, 0.027132198887234261
                      0.033992989322934299, 0.094482411257251542, 0.0074965567115513453
                      0.071430879530998817, 0.14492674059592309, 0.06936589799707335 ]

                for (d, frameTime, acc) in witnesses do
                    let s = Loop.advance d tick frameTime { Loop.init origin with Accumulator = acc }
                    Expect.equal s.Current.Ticks 3 $"dt {d}: three whole steps"
                    Expect.isTrue (s.Accumulator >= 0.0) $"dt {d}: accumulator {s.Accumulator} is not negative"
                    Expect.isTrue (s.Accumulator < d) $"dt {d}: accumulator stays below one step"
            }

            test "alpha never returns NaN or leaves [0, 1], for any dt and any hand-built StepState" {
                let hostile = [ nan; infinity; -infinity; -1.0; 0.0; 0.03125; 1e300 ]

                for a in hostile do
                    for d in hostile do
                        let v = Loop.alpha d { Loop.init 0.0 with Accumulator = a }
                        Expect.isFalse (System.Double.IsNaN v) $"alpha {d} (acc {a}) is not NaN"
                        Expect.isTrue (v >= 0.0 && v <= 1.0) $"alpha {d} (acc {a}) = {v} is in [0, 1]"
            }
        ]

        testCase "advance agrees with FixedStep.drain on both step count and carry (FsCheck ≥1000)"
        <| fun () ->
            // The delegation is the contract: one hardened accumulator, not two. If these ever part,
            // the second copy has been reintroduced.
            let prop (d: int) (f: int) (a: int) =
                let dt = float (1 + abs (d % 64)) / 64.0
                let frameTime = float (f % 500) / 100.0 // may be negative or huge
                let acc = float (abs (a % 64)) / 64.0
                let s = Loop.advance dt tick frameTime { Loop.init origin with Accumulator = acc }
                let struct (steps, carry) = FixedStep.drain dt frameTime acc
                s.Current.Ticks = steps && s.Accumulator = carry
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testCase "the accumulator stays in [0, dt) and Previous stays exactly one step behind (FsCheck ≥1000)"
        <| fun () ->
            let prop (d: int) (f: int) (a: int) =
                let dt = float (1 + abs (d % 64)) / 64.0
                let frameTime = float (f % 500) / 100.0
                let acc = float (abs (a % 64)) / 64.0
                let s = Loop.advance dt tick frameTime { Loop.init origin with Accumulator = acc }
                // Strict, both ends — the .fsi promises [0, dt) and drainWith now clamps to make it true.
                let inBounds = s.Accumulator >= 0.0 && s.Accumulator < dt
                // Previous trails Current by one step whenever a step ran; by zero when none did.
                let lag = if s.Current.Ticks > 0 then 1 else 0
                let alphaOk =
                    let v = Loop.alpha dt s
                    System.Double.IsFinite v && v >= 0.0 && v <= 1.0
                inBounds && s.Current.Ticks - s.Previous.Ticks = lag && alphaOk
            Check.One(Config.QuickThrowOnFailure.WithMaxTest 1000, prop)

        testList "determinism" [

            // A scripted frame sequence with everything a real one has: sub-step frames that only bank
            // time, exact multiples, a runaway frame that must clamp, and a NaN glitch mid-run.
            let script = [ 0.03125; 0.03125; 0.25; 0.0; nan; 0.09375; 5.0; 0.015625; 0.03125 ]

            let run () =
                script |> List.scan (fun s ft -> Loop.advance dt move ft s) (Loop.init 0.0)

            test "golden: a scripted frameTime sequence reproduces an exact StepState chain" {
                let chain = run ()

                // Literal, not approximate — every value is a multiple of 1/64.
                let expected =
                    [ { Current = 0.0; Previous = 0.0; Accumulator = 0.0 } // init
                      { Current = 0.0; Previous = 0.0; Accumulator = 0.03125 } // bank 1/32
                      { Current = 0.0625; Previous = 0.0; Accumulator = 0.0 } // 1/32 + 1/32 = one step
                      { Current = 0.3125; Previous = 0.25; Accumulator = 0.0 } // + 0.25 = four steps
                      { Current = 0.3125; Previous = 0.25; Accumulator = 0.0 } // zero frame: nothing
                      { Current = 0.3125; Previous = 0.25; Accumulator = 0.0 } // NaN frame: nothing
                      { Current = 0.375; Previous = 0.3125; Accumulator = 0.03125 } // 3/32 = one step + 1/32
                      { Current = 0.625; Previous = 0.5625; Accumulator = 0.03125 } // 5.0 clamped to 0.25 ⇒ four
                      { Current = 0.625; Previous = 0.5625; Accumulator = 0.046875 } // 1/32 + 1/64 < a step: bank
                      { Current = 0.6875; Previous = 0.625; Accumulator = 0.015625 } ] // 3/64 + 1/32 ⇒ one step

                Expect.equal chain expected "the whole chain, step for step"
            }

            test "the chain is reproducible: same script, identical states" {
                Expect.equal (run ()) (run ()) "no wall clock, no hidden state — replay is a value comparison"
            }

            test "the chain is frame-split invariant where the clamp does not bite" {
                // One 0.25s frame and four 1/16s frames deliver the same world: the accumulator carries
                // the difference. (Below the clamp only — a runaway frame is deliberately lossy.)
                let whole = Loop.advance dt tick 0.25 (Loop.init origin)

                let split =
                    List.replicate 4 dt
                    |> List.fold (fun s ft -> Loop.advance dt tick ft s) (Loop.init origin)

                Expect.equal whole.Current split.Current "same world, however the frames were cut"
                Expect.equal whole.Accumulator split.Accumulator "and the same carry"
            }
        ]

        test "alpha is the accumulator's fraction of a step" {
            let s = { Loop.init 0.0 with Accumulator = 0.046875 } // 3/64 of a 4/64 step
            Expect.equal (Loop.alpha dt s) 0.75 "3/4 of the way from Previous to Current"
        }
    ]
