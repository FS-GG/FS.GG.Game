module Game.Core.Tests.EffectsTests

// Effects capsule — the mitigation layer: an ordered, named `Stage` pipeline with an observable early
// exit (`DamageTrace.Halted`), and the status-effect list with its four stacking policies. `Effects`
// never queries space: a region operator (`Ballistics.splash`, `SpatialGrid.queryRadius`, a chain
// traversal) decides WHO is hit and with what transport multiplier, and this module decides FOR HOW
// MUCH. Pure, total (no input can put a NaN in a DamageTrace), byte-deterministic.

open Expecto
open FsCheck
open FS.GG.Game.Core

// ---------------------------------------------------------------------------------------------
// A minimal world. `'T` is the target, `'K` the damage kind: Game.Core learns neither.

type Kind =
    | Physical
    | Frost

type Unit =
    { Armor: float
      Cover: float
      FrostResist: float
      Vulnerable: float
      Ghost: bool }

let private baseUnit =
    { Armor = 0.0
      Cover = 0.0
      FrostResist = 0.0
      Vulnerable = 0.0
      Ghost = false }

let private hit kind source amount : Damage<Kind> =
    { Kind = kind
      Source = source
      Base = amount }

// The stages the specs actually name, wired to this world's fields.
let private armor: Stage<Unit, Kind> = Effects.subtract (fun u _ -> u.Armor)
let private cover: Stage<Unit, Kind> = Effects.subtract (fun u _ -> u.Cover)
let private vuln: Stage<Unit, Kind> = Effects.amplify (fun u -> u.Vulnerable)

let private frostResist: Stage<Unit, Kind> =
    Effects.resist (fun u k -> if k = Frost then u.FrostResist else 0.0)

let private ghost: Stage<Unit, Kind> =
    Effects.immuneWhen (fun u d -> u.Ghost && d.Kind = Physical)

/// Armor-bypass is a property of the damage **Kind**, not its `Source` (§3.6). "Elemental sources
/// ignore armor" is tempting to spell as a `Source` gate — Poison's DoT really is `Periodic` — but a
/// Frost bolt is a `Declared` attack, so a source-gated armor stage still subtracts armor from it and
/// only Poison ever gets its bypass. That is why `amountOf` takes `'K`.
let private armorOf: Stage<Unit, Kind> =
    Effects.subtract (fun u k ->
        match k with
        | Frost -> 0.0 // elemental: bypasses armor whatever its Source
        | Physical -> u.Armor)

/// One pipeline for the whole of tower-defense: amplifiers → multiplicative → subtractive → floor.
/// No `gatedBy` — a Cannon shell and a Frost bolt walk the same four stages and take different
/// branches inside two of them.
let private towerDefense = [ vuln; frostResist; armorOf; Effects.floorAt 1.0 ]

let private final (t: DamageTrace) = t.Final

let private active e ticks : Effects.Active<int> = { Effect = e; TicksRemaining = ticks }

// `Strongest` orients magnitude so that larger is stronger; the effect payload here IS its magnitude.
let private strongest: Effects.Policy<int> = Effects.Strongest float

[<Tests>]
let tests =
    testList "Game.Core Effects (mitigation layer, damage pipeline + status policies)" [

        // -------------------------------------------------------------------------------------
        // The bug the module exists to prevent. A `max` at the END of a pipeline erases every zero
        // the pipeline produced, so a full immunity that merely multiplied by 0.0 deals the floor.

        testList "the floor never resurrects a zero (the reason `Halt` exists)" [

            test "a fully frost-immune target takes 0 from a frost bolt, not the floor of 1" {
                let spectre = { baseUnit with FrostResist = 1.0 }
                let trace = Effects.pipeline towerDefense spectre (hit Frost Source.Declared 5.0)

                Expect.equal trace.Final 0.0 "resist 1.0 ⇒ 0 damage, NOT the floorAt 1.0"
                Expect.equal trace.Halted (ValueSome "resist") "the caller must be able to see WHY, to suppress the rider Slow"
            }

            test "a resistance above 1.0 halts rather than multiplying by a negative and healing" {
                let overResistant = { baseUnit with FrostResist = 1.5 }
                let trace = Effects.pipeline towerDefense overResistant (hit Frost Source.Declared 10.0)

                Expect.equal trace.Final 0.0 "resist > 1.0 ⇒ 0, never a negative amount"
                Expect.equal trace.Halted (ValueSome "resist") "halted, not multiplied"
            }

            test "a resistance of 0.999 does NOT halt — it multiplies, and the floor then applies" {
                let almost = { baseUnit with FrostResist = 0.999 }
                let trace = Effects.pipeline towerDefense almost (hit Frost Source.Declared 5.0)

                Expect.equal trace.Final 1.0 "0.005 raised to the floor of 1.0 — the floor is for the nearly-immune"
                Expect.equal trace.Halted ValueNone "no halt: 0.999 is a resistance, not an immunity"
            }

            // The same discontinuity is waiting one layer up, and `Effects` does NOT close it — it makes
            // it visible. A zero transport multiplier is a zero SEED, and a seed is not a `Halt`: the
            // floor still runs. `splash` includes the rim (`distance <= radius`), so a rim-grazing
            // target IS a target, and it takes 1 damage from a blast that reached it with none.
            //
            // That is a gap in tower-defense.md §4.5, raised in the design's §9 — not a bug here. What
            // the module owes is that the behaviour be legible rather than a balance mystery, and that
            // a game have a way to close it. Both are pinned below.
            test "a zero transport multiplier is a SEED, not a Halt — the floor still lifts it to 1" {
                let rim = [ baseUnit, 0.0 ]
                let dealt = Effects.applyAll towerDefense (hit Physical Source.Declared 30.0) rim

                Expect.equal (dealt |> List.map (snd >> final)) [ 1.0 ] "the documented sharp edge: a linearFalloff 0.0 rim target takes the floor"
                Expect.equal (dealt |> List.map (snd >> fun t -> t.Halted)) [ ValueNone ] "nothing halted — which is exactly why the floor ran"
            }

            test "…and an immunity DOES halt, so the floor never runs — the difference in one place" {
                let spectre = { baseUnit with FrostResist = 1.0 }
                let seeded = Effects.applyAll towerDefense (hit Physical Source.Declared 30.0) [ baseUnit, 0.0 ]
                let halted = Effects.applyAll towerDefense (hit Frost Source.Declared 30.0) [ spectre, 1.0 ]

                Expect.equal (seeded |> List.map (snd >> final)) [ 1.0 ] "zero seed ⇒ floored"
                Expect.equal (halted |> List.map (snd >> final)) [ 0.0 ] "zero by Halt ⇒ not floored"
            }

            test "a game closes the rim gap in its region operator, or with immuneWhen" {
                let damage = hit Physical Source.Declared 30.0

                // (a) Drop the zero-multiplier targets before the pipeline ever sees them.
                let pruned = [ baseUnit, 0.0; baseUnit, 0.5 ] |> List.filter (fun (_, m) -> m > 0.0)
                Expect.equal (Effects.applyAll towerDefense damage pruned |> List.map (snd >> final)) [ 15.0 ] "the rim target is not a target"

                // (b) Or halt on it, which also suppresses any rider effect.
                let grazed = Effects.immuneWhen (fun _ _ -> true)
                Expect.equal (Effects.pipeline (grazed :: towerDefense) baseUnit damage |> final) 0.0 "an explicit halt beats the floor"
            }

            test "immuneWhen halts, and names itself, so a rider effect can be suppressed" {
                let spectre = { baseUnit with Ghost = true }
                let trace = Effects.pipeline [ ghost; Effects.floorAt 1.0 ] spectre (hit Physical Source.Declared 99.0)

                Expect.equal trace.Final 0.0 "categorical immunity ⇒ 0"
                Expect.equal trace.Halted (ValueSome "immuneWhen") "named"
                Expect.equal trace.Steps [ "immuneWhen", 0.0 ] "the floor never ran"
            }
        ]

        // -------------------------------------------------------------------------------------
        // Ordering. Each of these is a real game-design decision that the pipeline order encodes.

        testList "order of operations (each stage pure, and separately testable)" [

            test "amplify multiplies by (1 + bonus)" {
                let u = { baseUnit with Vulnerable = 0.4 }
                Expect.equal (Effects.pipeline [ vuln ] u (hit Physical Source.Declared 30.0) |> final) 42.0 "30 × 1.4"
            }

            test "resist multiplies by (1 - r)" {
                let u = { baseUnit with FrostResist = 0.25 }
                Expect.equal (Effects.pipeline [ frostResist ] u (hit Frost Source.Declared 40.0) |> final) 30.0 "40 × 0.75"
            }

            test "resist only bites the kind it is written against" {
                let u = { baseUnit with FrostResist = 1.0 }
                Expect.equal (Effects.pipeline [ frostResist ] u (hit Physical Source.Declared 40.0) |> final) 40.0 "frost resist ignores a physical hit"
            }

            test "subtract removes a flat amount, and may drive the running total negative" {
                let u = { baseUnit with Armor = 6.0 }
                Expect.equal (Effects.pipeline [ armor ] u (hit Physical Source.Declared 4.0) |> final) -2.0 "floorAt decides the bottom, not subtract"
            }

            test "floorAt clamps up, and is the last word" {
                let u = { baseUnit with Armor = 6.0 }
                Expect.equal (Effects.pipeline [ armor; Effects.floorAt 1.0 ] u (hit Physical Source.Declared 4.0) |> final) 1.0 "tower-defense floor"
                Expect.equal (Effects.pipeline [ armor; Effects.floorAt 0.0 ] u (hit Physical Source.Declared 4.0) |> final) 0.0 "tactics floor — a unit behind cover survives"
            }

            test "scale BEFORE subtract: falloff must not shield the target from its own armor" {
                // Cannon 30, armor 6, at the rim of a linearFalloff 0.5 blast.
                let brute = { baseUnit with Armor = 6.0 }
                let atRim = Effects.applyAll [ armor; Effects.floorAt 1.0 ] (hit Physical Source.Declared 30.0) [ brute, 0.5 ]
                let atCentre = Effects.applyAll [ armor; Effects.floorAt 1.0 ] (hit Physical Source.Declared 30.0) [ brute, 1.0 ]

                Expect.equal (atRim |> List.map (snd >> final)) [ 9.0 ] "max(1, 0.5·30 − 6) = 9 — NOT 0.5·max(1, 30 − 6) = 12"
                Expect.equal (atCentre |> List.map (snd >> final)) [ 24.0 ] "the two orders agree at the centre; they disagree by a third at the rim"
            }

            test "amplify BEFORE subtract: an anti-armor upgrade must overcome armor, not scale the leak" {
                let brute = { baseUnit with Armor = 6.0; Vulnerable = 0.4 }
                let chosen = Effects.pipeline [ vuln; armor; Effects.floorAt 1.0 ] brute (hit Physical Source.Declared 30.0)
                let rejected = Effects.pipeline [ armor; vuln; Effects.floorAt 1.0 ] brute (hit Physical Source.Declared 30.0)

                Expect.equal chosen.Final 36.0 "max(1, 42 − 6) — the Breaker upgrade beats armor"
                Expect.floatClose Accuracy.high rejected.Final 33.6 "max(1, 24) × 1.4 — it merely scales what armor let through"
                Expect.isTrue (chosen.Final > rejected.Final) "and the difference is the whole point of the upgrade"
            }
        ]

        // -------------------------------------------------------------------------------------

        testList "gatedBy (damage source is an axis, and it is load-bearing)" [

            // §3.6's trap, pinned as a regression. "Elemental sources ignore armor" LOOKS like a
            // `Source` rule because Poison's DoT ticks really are `Periodic`. Spell it that way and a
            // Frost bolt — a `Declared` attack — still gets armor subtracted from it, and only Poison
            // ever gets its bypass. Gate on the axis the rule is actually about.
            test "armor-bypass is keyed on Kind, not Source: a Declared Frost bolt still bypasses armor" {
                let brute = { baseUnit with Armor = 6.0 }

                let byKind = [ armorOf; Effects.floorAt 1.0 ] // correct
                let bySource = [ Effects.gatedBy [ Source.Declared; Source.Collision ] armor; Effects.floorAt 1.0 ] // the trap

                Expect.equal (Effects.pipeline byKind brute (hit Frost Source.Declared 4.0) |> final) 4.0 "a Frost bolt ignores armor"
                Expect.equal (Effects.pipeline bySource brute (hit Frost Source.Declared 4.0) |> final) 1.0 "the source gate armors it — the bug"
                Expect.equal (Effects.pipeline byKind brute (hit Physical Source.Declared 4.0) |> final) 1.0 "a Physical 4 against armor 6 is floored"
            }

            test "a Periodic tick and a Declared bolt of the same elemental Kind agree — no Source gate needed" {
                let brute = { baseUnit with Armor = 6.0 }
                let p = [ armorOf; Effects.floorAt 1.0 ]

                Expect.equal (Effects.pipeline p brute (hit Frost Source.Periodic 4.0) |> final) 4.0 "poison tick bypasses armor"
                Expect.equal (Effects.pipeline p brute (hit Frost Source.Declared 4.0) |> final) 4.0 "and so does the bolt that applied it"
            }

            test "gatedBy is for the rules that ARE about Source: cover and Armored" {
                // turn-based-tactics' cover and `Armored` reduce declared attacks only, so its
                // collision 1 and its lava 3 run the same list and are reduced by neither.
                let crawler = { baseUnit with Cover = 2.0; Armor = 1.0 }

                let tbt =
                    [ Effects.gatedBy [ Source.Declared ] cover
                      Effects.gatedBy [ Source.Declared ] armor
                      Effects.floorAt 0.0 ]

                Expect.equal (Effects.pipeline tbt crawler (hit Physical Source.Declared 3.0) |> final) 0.0 "3 − 2 cover − 1 armored"
                Expect.equal (Effects.pipeline tbt crawler (hit Physical Source.Declared 1.0) |> final) 0.0 "AC#3 — max(0, 1 − 2) lets the Crawler survive the Ram"
                Expect.equal (Effects.pipeline tbt crawler (hit Physical Source.Collision 1.0) |> final) 1.0 "shoved into a wall: neither applies"
                Expect.equal (Effects.pipeline tbt crawler (hit Physical Source.Environmental 3.0) |> final) 3.0 "standing in lava: neither applies"
            }

            test "a gated stage keeps the inner stage's name, fired or not, so traces are comparable" {
                let brute = { baseUnit with Armor = 6.0 }
                let gated = Effects.gatedBy [ Source.Declared ] armor

                let fired = Effects.pipeline [ gated ] brute (hit Physical Source.Declared 10.0)
                let skipped = Effects.pipeline [ gated ] brute (hit Physical Source.Periodic 10.0)

                Expect.equal (fired.Steps |> List.map fst) [ "subtract" ] "the rule it encodes, not 'gatedBy'"
                Expect.equal (skipped.Steps |> List.map fst) [ "subtract" ] "one entry per stage regardless of source"
                Expect.equal (List.map snd fired.Steps) [ 4.0 ] "fired"
                Expect.equal (List.map snd skipped.Steps) [ 10.0 ] "passed through untouched"
            }
        ]

        // -------------------------------------------------------------------------------------

        testList "DamageTrace (the audit is the feature)" [

            test "Steps records every stage's output, in order" {
                let brute = { baseUnit with Armor = 6.0; Vulnerable = 0.5 }
                let trace = Effects.pipeline [ vuln; armor; Effects.floorAt 1.0 ] brute (hit Physical Source.Declared 10.0)

                Expect.equal trace.Steps [ "amplify", 15.0; "subtract", 9.0; "floorAt", 9.0 ] "one entry per stage, in pipeline order"
                Expect.equal trace.Halted ValueNone "nothing halted"
                Expect.equal trace.Final 9.0 "the last step's output"
            }

            test "a halt truncates Steps at the halting stage" {
                let spectre = { baseUnit with FrostResist = 1.0; Armor = 6.0 }
                let trace = Effects.pipeline towerDefense spectre (hit Frost Source.Declared 20.0)

                Expect.equal (trace.Steps |> List.map fst) [ "amplify"; "resist" ] "armor and floorAt never ran"
                Expect.equal trace.Final 0.0 "and the amount is the halting stage's"
            }

            test "an empty pipeline is the identity — half the corpus mitigates nothing" {
                let trace = Effects.pipeline [] baseUnit (hit Physical Source.Declared 40.0)
                Expect.equal trace.Final 40.0 "a missile-command blast decides membership and kills"
                Expect.isEmpty trace.Steps "no stages ran"
                Expect.equal trace.Halted ValueNone "nothing halted"
                Expect.equal trace.Seed 40.0 "the seed is reported even when nothing consumed it"
            }

            // The transport multiplier is the one input `Steps` cannot record, because it is a seed and
            // not a stage. Without `Seed`, a designer asking "why did this target take 9?" cannot tell a
            // ×0.5 falloff from a 15-point base — and the audit is the feature.
            test "Seed records the transport multiplier that Steps cannot" {
                let brute = { baseUnit with Armor = 6.0 }
                let dealt = Effects.applyAll [ armor ] (hit Physical Source.Declared 30.0) [ brute, 0.5 ]
                let trace = dealt |> List.head |> snd

                Expect.equal trace.Seed 15.0 "30 × 0.5 — the amount the first stage was handed"
                Expect.equal trace.Steps [ "subtract", 9.0 ] "and it is still not a Step"
                Expect.equal trace.Final 9.0 "15 − 6"
            }

            test "Seed distinguishes a halved blast from a half-strength one — same Final, same Steps" {
                let brute = { baseUnit with Armor = 6.0 }
                let halvedBlast = Effects.applyAll [ armor ] (hit Physical Source.Declared 30.0) [ brute, 0.5 ]
                let weakBlast = Effects.applyAll [ armor ] (hit Physical Source.Declared 15.0) [ brute, 1.0 ]

                let traceOf d = d |> List.head |> snd

                Expect.equal (traceOf halvedBlast).Final (traceOf weakBlast).Final "both deal 9"
                Expect.equal (traceOf halvedBlast).Steps (traceOf weakBlast).Steps "and trace identically through the stages"
                Expect.equal (traceOf halvedBlast).Seed (traceOf weakBlast).Seed "…and seed identically too: Seed audits the amount, not its provenance"
            }

            test "under pipeline, Seed is damage.Base" {
                let brute = { baseUnit with Armor = 6.0; Vulnerable = 0.5 }
                let trace = Effects.pipeline [ vuln; armor ] brute (hit Physical Source.Declared 10.0)

                Expect.equal trace.Seed 10.0 "no transport multiplier ⇒ the seed is the base"
                Expect.equal trace.Steps [ "amplify", 15.0; "subtract", 9.0 ] "amplify's 15.0 is a Step; the seed is not"
            }

            test "a halt still reports the seed it started from" {
                let spectre = { baseUnit with FrostResist = 1.0 }
                let trace = Effects.pipeline towerDefense spectre (hit Frost Source.Declared 20.0)

                Expect.equal trace.Final 0.0 "halted at resist"
                Expect.equal trace.Seed 20.0 "and the audit still records what it started from"
            }

            test "a non-finite seed degrades to 0.0 rather than reporting NaN" {
                let byBase = Effects.pipeline [ armor ] baseUnit (hit Physical Source.Declared nan)
                let byMultiplier = Effects.applyAll [ armor ] (hit Physical Source.Declared 30.0) [ baseUnit, infinity ]

                Expect.equal byBase.Seed 0.0 "a NaN Base seeds 0.0, and Seed says so"
                Expect.equal ((byMultiplier |> List.head |> snd).Seed) 0.0 "an infinite multiplier seeds 0.0 too"
            }
        ]

        // -------------------------------------------------------------------------------------
        // Permutation invariance. Structural, not arithmetic: `Stage.Run` is handed one target and a
        // running amount, so no stage CAN observe another target. The test is a regression guard on
        // the TYPE — if someone widens `Run` to take the target list, this is what fails.

        testList "applyAll (the AoE application)" [

            // #43's permutation-invariance criterion, stated so that it can actually FAIL.
            //
            // Asserting `List.rev (applyAll (List.rev ts)) = applyAll ts` proves nothing: reversal is
            // symmetric on both sides, so it is a theorem about `List.map` and holds for any positional
            // implementation, including one that folds cross-target state. The content of the claim is
            // that a target's trace depends on THAT TARGET and its multiplier alone — so compute each
            // target's trace in company, and again in isolation, and demand they agree.
            testCase "a target's trace is independent of the other targets in the list (FsCheck >=500)"
            <| fun () ->
                let prop (armors: int list) (seed: int) =
                    let targets =
                        armors
                        |> List.mapi (fun i a -> { baseUnit with Armor = float (abs a % 20) }, 0.25 + float ((i + abs seed) % 4) * 0.25)

                    let damage = hit Physical Source.Declared 30.0

                    let inCompany = Effects.applyAll towerDefense damage targets |> List.map snd

                    let inIsolation =
                        targets |> List.map (fun t -> Effects.applyAll towerDefense damage [ t ] |> List.head |> snd)

                    inCompany = inIsolation

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            testCase "permuting the targets permutes the results, and changes nothing else (FsCheck >=500)"
            <| fun () ->
                let prop (armors: int list) (rot: int) =
                    let targets =
                        armors |> List.mapi (fun i a -> { baseUnit with Armor = float (abs a % 20) }, 0.25 + float (i % 4) * 0.25)

                    let damage = hit Physical Source.Declared 30.0

                    match targets with
                    | [] -> true
                    | _ ->
                        // An asymmetric permutation: rotate left by k. `List.map f` commutes with it,
                        // but so would a reversal — what makes this bite is comparing the MULTISET too.
                        let k = abs rot % List.length targets
                        let rotated = List.skip k targets @ List.take k targets

                        let straight = Effects.applyAll towerDefense damage targets
                        let permuted = Effects.applyAll towerDefense damage rotated

                        let rotatedBack = List.skip (List.length permuted - k) permuted @ List.take (List.length permuted - k) permuted

                        // Compare the multiset by sorting on the WHOLE element. `sortBy (fun t ->
                        // t.Final)` would not do: it is stable, so two distinct targets that happen to
                        // take the same damage (armor 0 at ×0.25, and armor 15 at ×0.75, both 7.5) keep
                        // their input order and the rotation flips it.
                        rotatedBack = straight && List.sort permuted = List.sort straight

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

            test "applyAll returns results in the order the targets were given" {
                let a = { baseUnit with Armor = 1.0 }
                let b = { baseUnit with Armor = 2.0 }
                let dealt = Effects.applyAll [ armor ] (hit Physical Source.Declared 10.0) [ a, 1.0; b, 1.0 ]

                Expect.equal (dealt |> List.map (snd >> final)) [ 9.0; 8.0 ] "insertion order preserved"
            }

            test "applyAll SEEDS with the transport multiplier — it is not a stage in the list" {
                let brute = { baseUnit with Armor = 6.0 }
                let dealt = Effects.applyAll [ armor ] (hit Physical Source.Declared 30.0) [ brute, 0.5 ]

                Expect.equal (dealt |> List.map (snd >> final)) [ 9.0 ] "15 − 6; the multiplier cannot be reordered after subtract"
                Expect.equal (dealt |> List.collect (snd >> fun t -> t.Steps)) [ "subtract", 9.0 ] "the seed is not a Step"
            }

            test "applyAll over no targets is empty — a blast that caught nobody" {
                Expect.isEmpty (Effects.applyAll towerDefense (hit Physical Source.Declared 30.0) []) "no targets, no traces"
            }
        ]

        // -------------------------------------------------------------------------------------
        // The four AoE models of #43, each a (region operator → ('T * float) list) × pipeline
        // composition. Demonstrated, not asserted: the point is that the SAME pipeline consumes a
        // distance falloff, a jump-index falloff, and a uniform scalar without knowing the difference.

        testList "the AoE models compose as (region operator) × (pipeline)" [

            test "tower-defense cannon splash: Ballistics.splash × [amplify; resist; armor; floor 1]" {
                let brute = { baseUnit with Armor = 6.0 }
                let grid = SpatialGrid.build 32.0 [ { X = 0.0; Y = 0.0 }, brute; { X = 42.0; Y = 0.0 }, brute ]

                // The region operator. `Effects` never sees the grid, the radius, or the curve.
                let region =
                    Ballistics.splash { X = 0.0; Y = 0.0 } 42.0 (Ballistics.linearFalloff 0.5) (fun _ -> { X = 0.0; Y = 0.0 }) grid

                let dealt = Effects.applyAll towerDefense (hit Physical Source.Declared 30.0) region
                Expect.equal (dealt |> List.length) 2 "both in radius"
                Expect.equal (dealt |> List.map (snd >> final)) [ 24.0; 24.0 ] "both projected to the centre ⇒ multiplier 1.0"
            }

            test "tower-defense Tesla chain: the scalar is chainFalloff^k in the JUMP INDEX, not distance" {
                // A curve of `float -> float` over normalised distance could never express this — which
                // is exactly why `Effects` consumes a list, not a falloff.
                let chainFalloff = 0.6
                let victims = [ baseUnit; baseUnit; baseUnit ]
                let region = victims |> List.mapi (fun k u -> u, chainFalloff ** float k)

                let dealt = Effects.applyAll [ frostResist; Effects.floorAt 1.0 ] (hit Frost Source.Declared 100.0) region
                Expect.equal (dealt |> List.map (snd >> final)) [ 100.0; 60.0; 36.0 ] "the k-th jump takes 0.6^k"
            }

            test "missile-command blast: SpatialGrid.queryRadius × [] — membership IS the mechanic" {
                let grid = SpatialGrid.build 32.0 [ { X = 5.0; Y = 5.0 }, baseUnit ]
                let region = SpatialGrid.queryRadius { X = 0.0; Y = 0.0 } 40.0 grid |> List.map (fun u -> u, 1.0)

                let dealt = Effects.applyAll [] (hit Physical Source.Declared 1.0) region
                Expect.equal (dealt |> List.map (snd >> final)) [ 1.0 ] "any positive amount is lethal; there is nothing to mitigate"
            }

            test "turn-based-tactics Blast r: a uniform scalar × [cover; armored; floor 0]" {
                let crawler = { baseUnit with Cover = 2.0 }
                let armored = { baseUnit with Armor = 1.0 }
                let region = [ crawler, 1.0; armored, 1.0 ] // Chebyshev radius in cells — uniform.

                let p =
                    [ Effects.gatedBy [ Source.Declared ] cover
                      Effects.gatedBy [ Source.Declared ] armor
                      Effects.floorAt 0.0 ]

                let dealt = Effects.applyAll p (hit Physical Source.Declared 1.0) region
                Expect.equal (dealt |> List.map (snd >> final)) [ 0.0; 0.0 ] "cover 2 and armor 1 both stop a Ram of 1"
            }
        ]

        // -------------------------------------------------------------------------------------

        testList "status-effect policies (the coin flip every spec leaves unflipped)" [

            testList "Refresh (Stun: one instance, duration extends but never shortens)" [
                test "onto an empty list, applies" {
                    Expect.equal (Effects.applyEffect Effects.Refresh (active 1 30) []) [ active 1 30 ] "first application"
                }
                test "a longer reapplication extends" {
                    Expect.equal (Effects.applyEffect Effects.Refresh (active 1 30) [ active 1 10 ]) [ active 1 30 ] "max(10, 30)"
                }
                test "a shorter reapplication does not cut the running stun short" {
                    Expect.equal (Effects.applyEffect Effects.Refresh (active 1 5) [ active 1 20 ]) [ active 1 20 ] "max(20, 5)"
                }
            ]

            testList "Strongest (Slow: the >= is what lets a second tower re-up a slow)" [
                test "onto an empty list, applies" {
                    Expect.equal (Effects.applyEffect strongest (active 5 10) []) [ active 5 10 ] "first application"
                }
                test "a stronger application replaces and refreshes" {
                    Expect.equal (Effects.applyEffect strongest (active 8 30) [ active 5 20 ]) [ active 8 30 ] "8 > 5"
                }
                test "an EQUAL application refreshes — this is the load-bearing >=" {
                    Expect.equal (Effects.applyEffect strongest (active 5 30) [ active 5 4 ]) [ active 5 30 ] "a second Frost tower re-ups the same slow"
                }
                test "a weaker application changes nothing at all" {
                    Expect.equal (Effects.applyEffect strongest (active 2 99) [ active 5 4 ]) [ active 5 4 ] "not even the duration"
                }

                // Replacement *refreshes*, and a refresh may extend but never cut short — `Refresh`'s
                // rule, for `Refresh`'s reason. Taking the incoming duration wholesale is invisible in a
                // tower-defense (every slow of a tower type shares a base duration) and a live bug the
                // moment two sources of one effect differ in duration.
                test "an equal-magnitude SHORTER reapplication does not cut the running slow short" {
                    Expect.equal (Effects.applyEffect strongest (active 5 5) [ active 5 80 ]) [ active 5 80 ] "max(80, 5) — the 75 ticks do not evaporate"
                }
                test "a STRONGER but shorter application replaces the effect and keeps the longer duration" {
                    Expect.equal (Effects.applyEffect strongest (active 8 5) [ active 5 80 ]) [ active 8 80 ] "the incoming effect wins; only the duration is reconciled"
                }
                test "Strongest and Refresh agree on duration — the asymmetry is gone" {
                    let incoming, existing = active 5 5, [ active 5 80 ]
                    let ticks = List.map (fun (a: Effects.Active<int>) -> a.TicksRemaining)

                    Expect.equal
                        (Effects.applyEffect strongest incoming existing |> ticks)
                        (Effects.applyEffect Effects.Refresh incoming existing |> ticks)
                        "both extend, neither shortens"
                }
            ]

            testList "Stacks cap (Poison: independent, additive, appended in order)" [
                test "stacks accumulate in application order up to the cap" {
                    let after =
                        []
                        |> Effects.applyEffect (Effects.Stacks 3) (active 1 10)
                        |> Effects.applyEffect (Effects.Stacks 3) (active 2 20)
                        |> Effects.applyEffect (Effects.Stacks 3) (active 3 30)

                    Expect.equal after [ active 1 10; active 2 20; active 3 30 ] "three stacks, in order"
                }
                test "at the cap the application is a NO-OP — it does not refresh or evict" {
                    let atCap = [ active 1 10; active 2 20; active 3 30 ]
                    Expect.equal (Effects.applyEffect (Effects.Stacks 3) (active 9 99) atCap) atCap "the documented choice; the source spec states none"
                }
                test "a cap of zero admits nothing" {
                    Expect.isEmpty (Effects.applyEffect (Effects.Stacks 0) (active 1 10) []) "cap 0 ⇒ no instances"
                }
            ]

            testList "Once (the first application wins, forever)" [
                test "onto an empty list, applies" {
                    Expect.equal (Effects.applyEffect Effects.Once (active 1 10) []) [ active 1 10 ] "first application"
                }
                test "a later, stronger, longer application is still a no-op" {
                    Expect.equal (Effects.applyEffect Effects.Once (active 9 99) [ active 1 1 ]) [ active 1 1 ] "no-op"
                }
            ]
        ]

        testList "tickEffects (durations are int ticks; a tick is whatever the caller drains)" [

            test "one tick decrements every instance" {
                Expect.equal (Effects.tickEffects [ active 1 3; active 2 2 ]) [ active 1 2; active 2 1 ] "decrement, order-preserving"
            }

            test "an instance that reaches zero is dropped" {
                Expect.equal (Effects.tickEffects [ active 1 1; active 2 5 ]) [ active 2 4 ] "the expired one is gone"
            }

            test "a non-positive duration is collected on the next tick rather than living forever" {
                Expect.isEmpty (Effects.tickEffects [ active 1 0; active 2 -3 ]) "<= 0 is dropped"
            }

            test "ticking an empty list is empty" {
                Expect.isEmpty (Effects.tickEffects ([]: Effects.Active<int> list)) "total on the empty case"
            }
        ]

        // -------------------------------------------------------------------------------------
        // Determinism. `TicksRemaining` is an int, so an effect expires on the same STEP regardless of
        // how the frames were sliced. `Loop.advance` / `FixedStep.drain` yield the same step count for
        // a given accumulated time, and no float duration ever crosses a frame boundary.

        testList "determinism golden (effects expire on the same step at any frame rate)" [

            // The fixed interval and every frame time below are exact binary fractions, as
            // `FixedStepTests` uses 1/16. That is deliberate: with an inexact interval like 1/60 this
            // suite would be asserting that float accumulation happened to agree, when what is under
            // test is that an INT tick count cannot drift. Take the float question out of the test.
            let dt = 1.0 / 16.0

            // The world is the effect list plus the number of ticks that have run.
            let integrate (world: Effects.Active<int> list * int) (_dt: float) =
                let actives, ticks = world
                Effects.tickEffects actives, ticks + 1

            // Two effects: one authored at 12 ticks, one at 40. Over 32 steps the first must be gone
            // and the second must have exactly 8 left, on every frame script.
            let replay (frameTimes: float list) =
                frameTimes
                |> List.fold (fun state ft -> Loop.advance dt integrate ft state) (Loop.init ([ active 1 12; active 2 40 ], 0))
                |> fun s -> s.Current

            test "a coarse and a fine frame script over the same wall time expire the same effects" {
                // 2.0 s of wall time at a 1/16 s step is 32 fixed steps, however it is sliced.
                let coarse = replay (List.replicate 16 0.125) //  8 Hz — 2 steps drained per frame
                let fine = replay (List.replicate 64 0.03125) // 32 Hz — 1 step drained every 2 frames

                let activesCoarse, stepsCoarse = coarse
                let activesFine, stepsFine = fine

                Expect.equal stepsCoarse 32 "2.0 s at a 1/16 s step is 32 steps"
                Expect.equal stepsFine 32 "and the frame slicing cannot change that"
                Expect.equal activesCoarse activesFine "byte-identical effect lists"
                Expect.equal activesCoarse [ active 2 8 ] "the 12-tick effect expired; the 40-tick one has 8 left"
            }

            test "a ragged, varying frame-time script agrees with a uniform one" {
                let uniform = replay (List.replicate 32 dt)

                // Sums to exactly 2.0; every entry is under the 0.25 spiral-of-death clamp.
                let ragged =
                    replay [ 0.25; 0.25; 0.25; 0.25; 0.25; 0.25; 0.25; 0.125; 0.0625; 0.03125; 0.015625; 0.015625 ]

                Expect.equal (snd ragged) 32 "the same accumulated time drains the same steps"
                Expect.equal (snd uniform) (snd ragged) "however the frames sliced it"
                Expect.equal (fst uniform) (fst ragged) "so the effect lists are identical"
            }

            test "a frame that drains no steps leaves the effects untouched" {
                let stalled = replay (List.replicate 3 0.015625) // 3 × 1/64 < one 1/16 step
                Expect.equal stalled ([ active 1 12; active 2 40 ], 0) "no step ran, so nothing ticked"
            }

            test "applyEffect and the pipeline are pure — the same inputs give the same outputs" {
                let brute = { baseUnit with Armor = 6.0; Vulnerable = 0.4 }
                let d = hit Physical Source.Declared 30.0
                Expect.equal (Effects.pipeline towerDefense brute d) (Effects.pipeline towerDefense brute d) "no clock, no mutable state"
            }
        ]

        // -------------------------------------------------------------------------------------
        // Totality. As with `Ballistics.splash`, hostile input degrades to 0.0 rather than poisoning a
        // damage total. A NaN in an HP pool freezes an entity forever, and it is unattributable.

        testList "totality (no input can put a NaN in a DamageTrace)" [

            let isNan (v: float) = System.Double.IsNaN v

            test "a non-finite Base contributes 0.0" {
                [ nan; infinity; -infinity ]
                |> List.iter (fun b ->
                    let trace = Effects.pipeline towerDefense baseUnit (hit Physical Source.Declared b)
                    Expect.isFalse (isNan trace.Final) $"never NaN (Base = {b})"
                    Expect.equal trace.Final 1.0 $"seeded at 0.0, then raised by floorAt 1.0 (Base = {b})")
            }

            test "a non-finite transport multiplier seeds 0.0" {
                [ nan; infinity; -infinity ]
                |> List.iter (fun m ->
                    let dealt = Effects.applyAll [ armor ] (hit Physical Source.Declared 30.0) [ baseUnit, m ]
                    Expect.equal (dealt |> List.map (snd >> final)) [ 0.0 ] $"0.0 seed, minus zero armor (multiplier = {m})")
            }

            test "a hostile Stage cannot inject a NaN into the trace" {
                let hostile: Stage<Unit, Kind> =
                    { Name = "hostile"; Run = fun _ _ _ -> StageResult.Continue nan }

                let trace = Effects.pipeline [ hostile; armor ] baseUnit (hit Physical Source.Declared 10.0)
                Expect.isFalse (isNan trace.Final) "sanitised at the stage boundary"
                Expect.equal trace.Steps [ "hostile", 0.0; "subtract", 0.0 ] "the NaN became 0.0 before the next stage saw it"
            }

            test "a hostile Stage cannot inject a NaN through a Halt either" {
                let hostile: Stage<Unit, Kind> =
                    { Name = "hostile"; Run = fun _ _ _ -> StageResult.Halt nan }

                let trace = Effects.pipeline [ hostile ] baseUnit (hit Physical Source.Declared 10.0)
                Expect.equal trace.Final 0.0 "halted amounts are sanitised too"
                Expect.equal trace.Halted (ValueSome "hostile") "and it still names itself"
            }

            test "floorAt with a non-finite minimum degrades to a floor of 0.0" {
                let u = { baseUnit with Armor = 20.0 }
                let trace = Effects.pipeline [ armor; Effects.floorAt nan ] u (hit Physical Source.Declared 10.0)
                Expect.equal trace.Final 0.0 "not NaN, and not -10.0: the floor degraded to 0"
            }

            test "a NaN resistance does not halt, and does not poison" {
                let u = { baseUnit with FrostResist = nan }
                let trace = Effects.pipeline [ frostResist ] u (hit Frost Source.Declared 10.0)
                Expect.equal trace.Final 0.0 "NaN >= 1.0 is false ⇒ multiply ⇒ NaN ⇒ sanitised to 0.0"
                Expect.equal trace.Halted ValueNone "it is a multiply, not an immunity"
            }

            testCase "no pipeline over unclamped arbitrary floats ever yields a non-finite Final (FsCheck >=500)"
            <| fun () ->
                let prop (a: float) (r: float) (s: float) (f: float) (b: float) (m: float) =
                    let u = { baseUnit with Vulnerable = a; FrostResist = r; Armor = s }
                    let stages = [ vuln; frostResist; armor; Effects.floorAt f ]
                    let damage = hit Frost Source.Declared b

                    // Both entry points, because they differ in exactly the field under test: `applyAll`
                    // is the only way to seed at something other than `damage.Base`.
                    let traces =
                        Effects.pipeline stages u damage :: (Effects.applyAll stages damage [ u, m ] |> List.map snd)

                    traces
                    |> List.forall (fun t ->
                        System.Double.IsFinite t.Seed
                        && System.Double.IsFinite t.Final
                        && t.Steps |> List.forall (snd >> System.Double.IsFinite))

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)
        ]
    ]
