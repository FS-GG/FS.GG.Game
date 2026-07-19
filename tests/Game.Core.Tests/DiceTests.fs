module Game.Core.Tests.DiceTests

// Dice / damage distributions (work item 023, roadmap 3.1). Integer weight maps: constructors,
// convolution = sum distribution, advantage/disadvantage = max/min, exact moments, and seeded
// deterministic sampling with empirical convergence.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private near (a: float) (b: float) = abs (a - b) < 1e-9

[<Tests>]
let tests =
    testList "Game.Core Dice (023, FR-001..FR-006)" [

        test "constructors build the right weight maps (FR-001)" {
            Expect.equal (Dice.outcomes (Dice.die 6)) [ for v in 1..6 -> (v, 1L) ] "die 6 = uniform 1..6"
            Expect.equal (Dice.outcomes (Dice.constant 4)) [ (4, 1L) ] "constant"
            Expect.equal (Dice.outcomes (Dice.uniform 2 5)) [ (2, 1L); (3, 1L); (4, 1L); (5, 1L) ] "uniform"
            Expect.equal (Dice.outcomes (Dice.uniform 5 2)) [] "degenerate uniform (hi<lo) is empty"
        }

        test "convolve is the sum distribution of two dice (FR-002)" {
            let two = Dice.convolve (Dice.die 6) (Dice.die 6)
            Expect.equal (Dice.totalWeight two) 36L "36 equally-likely pairs"
            Expect.equal (Dice.outcomes two |> List.map fst) [ 2..12 ] "support 2..12"
            // weight of 7 is 6 (the most likely sum)
            Expect.equal (Dice.outcomes two |> List.find (fun (v, _) -> v = 7) |> snd) 6L "P(7) is 6/36"
            Expect.isTrue (near (Dice.mean two) 7.0) "mean of 2d6 is 7"
            Expect.equal (Dice.outcomes (Dice.repeat 0 (Dice.die 6))) [ (0, 1L) ] "repeat 0 = constant 0"
            Expect.isTrue (near (Dice.mean (Dice.repeat 3 (Dice.die 6))) 10.5) "mean of 3d6 is 10.5"
        }

        test "advantage/disadvantage are max/min with monotone means (FR-003)" {
            let d = Dice.die 20
            let adv = Dice.advantage d d
            let dis = Dice.disadvantage d d
            Expect.equal (Dice.outcomes adv |> List.map fst) [ 1..20 ] "advantage support 1..20"
            // P(max = 20) with two d20 is 39/400
            Expect.equal (Dice.outcomes adv |> List.find (fun (v, _) -> v = 20) |> snd) 39L "P(max=20)=39/400"
            Expect.isTrue (Dice.mean adv >= Dice.mean d) "advantage mean >= single"
            Expect.isTrue (Dice.mean d >= Dice.mean dis) "single >= disadvantage mean"
            Expect.isTrue (near (Dice.mean adv + Dice.mean dis) (2.0 * Dice.mean d)) "adv + dis means sum to 2× single (max+min=a+b)"
        }

        test "mean and variance are the exact moments of a d6 (FR-004)" {
            Expect.isTrue (near (Dice.mean (Dice.die 6)) 3.5) "mean d6 = 3.5"
            Expect.isTrue (near (Dice.variance (Dice.die 6)) (35.0 / 12.0)) "variance d6 = 35/12"
            Expect.isTrue (near (Dice.variance (Dice.constant 7)) 0.0) "a constant has zero variance"
        }

        test "sample is deterministic given the seed and totality holds (FR-005/FR-006)" {
            let d = Dice.die 6
            let seq rng0 =
                let mutable rng = rng0
                [ for _ in 1..20 ->
                      let struct (v, r) = Dice.sample d rng
                      rng <- r
                      v ]
            Expect.equal (seq (Rng.ofSeed 42UL)) (seq (Rng.ofSeed 42UL)) "same seed ⇒ same sample sequence"
            Expect.isTrue (seq (Rng.ofSeed 1UL) |> List.forall (fun v -> v >= 1 && v <= 6)) "samples in support"
            // empty distribution: total fallback, no throw
            let struct (v, _) = Dice.sample (Dice.uniform 5 2) (Rng.ofSeed 1UL)
            Expect.equal v 0 "empty distribution samples 0 without throwing"
        }

        test "sample's empirical mean converges to the distribution mean (FR-005)" {
            let d = Dice.convolve (Dice.die 6) (Dice.die 6) // mean 7
            let mutable rng = Rng.ofSeed 12345UL
            let n = 20000
            let mutable total = 0L
            for _ in 1..n do
                let struct (v, r) = Dice.sample d rng
                rng <- r
                total <- total + int64 v
            let empirical = float total / float n
            Expect.isTrue (abs (empirical - 7.0) < 0.1) (sprintf "empirical mean %f ≈ 7" empirical)
        }

        testCase "convolve mean equals the sum of means over random dice (FsCheck)" <| fun () ->
            let prop (aSides: int) (bSides: int) =
                let a = Dice.die (1 + (abs aSides) % 12)
                let b = Dice.die (1 + (abs bSides) % 12)
                let c = Dice.convolve a b
                near (Dice.mean c) (Dice.mean a + Dice.mean b)
                && Dice.totalWeight c = Dice.totalWeight a * Dice.totalWeight b

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        testCase "convolve is commutative and associative over random dice (FsCheck)" <| fun () ->
            let prop (x: int) (y: int) (z: int) =
                let a = Dice.die (1 + (abs x) % 8)
                let b = Dice.die (1 + (abs y) % 8)
                let c = Dice.die (1 + (abs z) % 8)
                Dice.outcomes (Dice.convolve a b) = Dice.outcomes (Dice.convolve b a)
                && Dice.outcomes (Dice.convolve (Dice.convolve a b) c) = Dice.outcomes (Dice.convolve a (Dice.convolve b c))

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)
    ]
