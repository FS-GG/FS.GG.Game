module Game.Core.Tests.AiTests

// Ai: the decision-layer vocabulary. Three things here are load-bearing and each is asserted the only
// way that means anything:
//
//   * the fog boundary  — `TeamView` yields sightings and nothing else, and ghosts decay;
//   * determinism       — a golden replay with the roster VARIED, because the bug this module exists to
//                         prevent (drawing from the root RNG, or splitting down a list) only appears
//                         when an agent dies one tick earlier than it did in the recording;
//   * the total order   — `best` reproduces turn-based-tactics §4.9 exactly, index tail included.
//
// The determinism tests deliberately also assert that the NAIVE approaches FAIL, so the reason
// `substream` keys on identity is executable rather than a comment.

open Expecto
open FsCheck
open FS.GG.Game.Core

let private p x y : Point = { X = x; Y = y }
let private cell c r : Cell = { Col = c; Row = r }

/// A sighting whose payload is the facts perception yielded, never the model that yielded them.
let private sighting id x y tick : Sighting<int> =
    { Agent = AgentId id
      Position = p x y
      Seen = id * 10
      LastSeenTick = tick }

/// One agent's draw: an aim error plus a follow-up roll, so a desynchronised stream shows up in both.
let private draw (rng: Rng) =
    let struct (e, r1) = Ai.aimError 0.05 rng
    let struct (n, _) = Rng.nextInt 0 999 r1
    (e, n)

/// The correct roster: each agent's stream is a function of its identity.
let private roster (root: Rng) (ids: int list) =
    ids |> List.map (fun id -> id, draw (Ai.substream root (AgentId id)))

/// The naive roster: walk `Rng.split` down the (sorted) agent list, handing each agent the next stream.
/// This is what "give each agent its own sub-stream via Rng.split at spawn" degenerates into when the
/// stream is keyed on POSITION rather than identity.
let private naiveRoster (root: Rng) (ids: int list) =
    ids
    |> List.fold
        (fun (rng, acc) id ->
            let struct (mine, next) = Rng.split rng
            (next, (id, draw mine) :: acc))
        (root, [])
    |> snd
    |> List.rev

/// FsCheck hands out NaN/infinity/1e300; clamp into a range where a sum of a few hundred stays finite.
let private clampDps (v: float) =
    if System.Double.IsFinite v then max 0.0 (min 1000.0 (abs v)) else 0.0

[<Tests>]
let tests =
    testList "Game.Core Ai (US-42, FR-042..FR-046)" [

        // -----------------------------------------------------------------------------------------
        // TeamView — the fog boundary. Spotted vs ghost vs forgotten.
        // -----------------------------------------------------------------------------------------

        test "view partitions into spotted (seen this tick) and ghosts (seen before)" {
            let v = Ai.view 100 50 [ sighting 1 0.0 0.0 100; sighting 2 5.0 5.0 80 ]
            Expect.equal (Ai.spotted v |> List.map _.Agent) [ AgentId 1 ] "only the fresh sighting is spotted"
            Expect.equal (Ai.ghosts v |> List.map _.Agent) [ AgentId 2 ] "the stale one decayed into a ghost"
            Expect.equal (Ai.viewTick v) 100 "the view remembers its tick"
        }

        test "a ghost older than its lifetime is forgotten entirely" {
            let v = Ai.view 100 10 [ sighting 1 0.0 0.0 89 ]
            Expect.isEmpty (Ai.spotted v) "not spotted"
            Expect.isEmpty (Ai.ghosts v) "and past the 10-tick lifetime, not even remembered"
            Expect.equal (Ai.tryFind (AgentId 1) v) ValueNone "tryFind agrees"
        }

        test "a ghost exactly at its lifetime is still remembered (boundary is inclusive)" {
            let v = Ai.view 100 10 [ sighting 1 0.0 0.0 90 ]
            Expect.equal (Ai.ghosts v |> List.map _.Agent) [ AgentId 1 ] "tick - lastSeen = lifetime survives"
        }

        test "a ghost's position is the stale one — shooting at where it was is the point" {
            let v = Ai.view 100 50 [ sighting 7 3.0 4.0 60 ]
            let g = Ai.ghosts v |> List.exactlyOne
            Expect.equal g.Position (p 3.0 4.0) "the remembered position, not a current one"
        }

        test "duplicate sightings of one agent collapse to the freshest" {
            // Two spotters report the same enemy. The caller must not have to deduplicate.
            let v = Ai.view 100 50 [ sighting 1 0.0 0.0 70; sighting 1 9.0 9.0 95 ]
            let s = Ai.known v |> List.exactlyOne
            Expect.equal s.LastSeenTick 95 "the newer report wins"
            Expect.equal s.Position (p 9.0 9.0) "and brings its position"
        }

        test "known is ascending by AgentId regardless of input order" {
            let v = Ai.view 10 50 [ sighting 5 0.0 0.0 10; sighting 1 0.0 0.0 3; sighting 3 0.0 0.0 10 ]
            Expect.equal (Ai.known v |> List.map _.Agent) [ AgentId 1; AgentId 3; AgentId 5 ] "ascending id"
        }

        test "a sighting from the future is clamped to spotted, not dropped" {
            let v = Ai.view 100 10 [ sighting 1 0.0 0.0 101 ]
            Expect.equal (Ai.spotted v |> List.map _.Agent) [ AgentId 1 ] "an agent that can see, not one gone blind"
        }

        test "a negative ghost lifetime remembers nothing but what is currently spotted" {
            let v = Ai.view 100 -5 [ sighting 1 0.0 0.0 100; sighting 2 0.0 0.0 99 ]
            Expect.equal (Ai.spotted v |> List.map _.Agent) [ AgentId 1 ] "spotted survives"
            Expect.isEmpty (Ai.ghosts v) "everything else is forgotten"
        }

        // -----------------------------------------------------------------------------------------
        // Determinism — the golden replay, with the roster varied. This is the acceptance test.
        // -----------------------------------------------------------------------------------------

        test "GOLDEN: an agent's draws depend on its id alone, not on how many agents are alive" {
            let root = Rng.ofSeed 0xC0FFEEUL
            let full = roster root [ 1; 2; 3; 4; 5 ]
            // Agents 2 and 4 died. In a replay, 1/3/5 must draw exactly what they drew before.
            let survivors = roster root [ 1; 3; 5 ]

            for id in [ 1; 3; 5 ] do
                let before = full |> List.find (fst >> (=) id) |> snd
                let after = survivors |> List.find (fst >> (=) id) |> snd
                Expect.equal after before $"agent {id} draws identically after its neighbours died"
        }

        test "GOLDEN: substream is byte-identical across repeated construction" {
            let root = Rng.ofSeed 42UL
            Expect.equal (roster root [ 1 .. 8 ]) (roster root [ 1 .. 8 ]) "same seed, same roster, same bytes"
        }

        test "the naive position-keyed split FAILS the roster-varied replay (why substream keys on id)" {
            let root = Rng.ofSeed 0xC0FFEEUL
            let full = naiveRoster root [ 1; 2; 3; 4; 5 ]
            let survivors = naiveRoster root [ 1; 3; 5 ]

            let before = full |> List.find (fst >> (=) 3) |> snd
            let after = survivors |> List.find (fst >> (=) 3) |> snd
            Expect.notEqual after before "agent 3 shifts up the split chain when agent 2 dies — the bug"
        }

        test "distinct ids yield decorrelated streams" {
            let root = Rng.ofSeed 7UL
            let draws = [ 0; 1; 2; -1; System.Int32.MinValue ] |> List.map (fun id -> draw (Ai.substream root (AgentId id)))
            Expect.equal (List.distinct draws |> List.length) (List.length draws) "no two ids share a stream"
        }

        test "AgentId 0 and AgentId -1 do not reproduce the root stream" {
            let root = Rng.ofSeed 12345UL
            Expect.notEqual (draw (Ai.substream root (AgentId 0))) (draw root) "id 0 is mixed"
            Expect.notEqual (draw (Ai.substream root (AgentId -1))) (draw root) "id -1 is mixed"
        }

        // -----------------------------------------------------------------------------------------
        // aimError — a difficulty knob, and a generator that must not desynchronise.
        // -----------------------------------------------------------------------------------------

        test "a non-positive or non-finite sigma yields exactly 0.0 and does NOT advance the generator" {
            let rng = Rng.ofSeed 99UL

            for sigma in [ 0.0; -1.0; nan; infinity; -infinity ] do
                let struct (e, r) = Ai.aimError sigma rng
                Expect.equal e 0.0 $"sigma {sigma} produces no deviation"
                Expect.equal r rng "and consumes no randomness, so a perfect agent cannot desync a replay"
        }

        test "aimError is deterministic and scales with sigma" {
            let rng = Rng.ofSeed 5UL
            let struct (a, _) = Ai.aimError 1.0 rng
            let struct (b, _) = Ai.aimError 1.0 rng
            Expect.equal a b "pure: the input generator is unchanged"
            let struct (c, _) = Ai.aimError 2.0 rng
            Expect.floatClose Accuracy.high c (2.0 * a) "sigma is a linear scale on the same draw"
        }

        testCase "aimError is always finite (FsCheck >=500)"
        <| fun () ->
            let prop (seed: uint64) (sigma: float) =
                let struct (e, _) = Ai.aimError (clampDps sigma) (Rng.ofSeed seed)
                System.Double.IsFinite e

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // -----------------------------------------------------------------------------------------
        // due — layer by cost, cheapest first.
        // -----------------------------------------------------------------------------------------

        test "due fires on multiples of the cadence, including tick 0" {
            Expect.isTrue (Ai.due 15 0) "tick 0"
            Expect.isTrue (Ai.due 15 15) "tick 15"
            Expect.isFalse (Ai.due 15 14) "tick 14"
            Expect.isTrue (Ai.due 1 7) "cadence 1 runs every tick"
        }

        test "due uses a floored modulus, so a pre-match countdown does not alias" {
            Expect.isTrue (Ai.due 15 -15) "-15 is a multiple"
            Expect.isFalse (Ai.due 15 -1) "-1 is not — and .NET's % would say -1, not 14"
        }

        test "a non-positive cadence never runs, rather than dividing by zero" {
            Expect.isFalse (Ai.due 0 0) "cadence 0"
            Expect.isFalse (Ai.due -3 9) "negative cadence"
        }

        // -----------------------------------------------------------------------------------------
        // threatField — the weighting and summing live here, not in Pathfinding.
        // -----------------------------------------------------------------------------------------

        test "danger sums dps over sources that are both in range and in line of sight" {
            let sources = [ cell 0 0, 10.0, 5; cell 9 9, 100.0, 1 ]
            let f = Ai.threatField (fun _ _ -> true) sources [ cell 1 0 ]
            Expect.equal f.[cell 1 0] 10.0 "the far source is out of range and contributes nothing"
        }

        test "an occluded source contributes nothing — hasLos is a caller-supplied oracle" {
            let sources = [ cell 0 0, 10.0, 5 ]
            let blind = Ai.threatField (fun _ _ -> false) sources [ cell 1 0 ]
            Expect.equal blind.[cell 1 0] 0.0 "no LOS, no danger"
        }

        test "a non-finite dps contributes 0, and a negative range is out of range everywhere" {
            let f = Ai.threatField (fun _ _ -> true) [ cell 0 0, nan, 5; cell 0 0, 7.0, -1 ] [ cell 0 0 ]
            Expect.equal f.[cell 0 0] 0.0 "total on hostile input"
        }

        test "only the cells asked for appear — this never enumerates a grid it was not given" {
            let f = Ai.threatField (fun _ _ -> true) [ cell 0 0, 1.0, 99 ] [ cell 3 3 ]
            Expect.equal (Map.count f) 1 "one cell in, one cell out"
        }

        testCase "PROPERTY: threatField is invariant under permutation of sources (FsCheck >=500)"
        <| fun () ->
            // Float addition is not associative. If the sum ran in caller order, this map would depend on
            // SpatialGrid insertion order and a replay would diverge the moment an entity spawned.
            let prop (raw: (int * int * float * int) list) =
                let sources =
                    raw
                    |> List.truncate 24
                    |> List.map (fun (c, r, dps, range) ->
                        cell (c % 8) (r % 8), clampDps dps, abs (range % 6))

                let cells = [ for c in 0..3 do for r in 0..3 -> cell c r ]
                let hasLos (a: Cell) (b: Cell) = (a.Col + b.Row) % 3 <> 0

                let forward = Ai.threatField hasLos sources cells
                let reversed = Ai.threatField hasLos (List.rev sources) cells
                let rotated = Ai.threatField hasLos (List.skip (List.length sources / 2) sources @ List.truncate (List.length sources / 2) sources) cells

                forward = reversed && forward = rotated

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // -----------------------------------------------------------------------------------------
        // fleeField — distanceField composed, with the policy coefficient where policy belongs.
        // -----------------------------------------------------------------------------------------

        test "maxPasses <= 0 returns the scaled field, unrelaxed" {
            let desire = Map.ofList [ cell 0 0, 0.0; cell 1 0, 1.0 ]
            let f = Ai.fleeField FourWay -1.2 0 desire
            Expect.equal f.[cell 1 0] -1.2 "scaled but not relaxed"
        }

        test "a relaxed flee field rolls away from the threat" {
            // A distance-to-threat map along a corridor: the threat is at (0,0).
            let desire = Map.ofList [ for c in 0..4 -> cell c 0, float c ]
            let f = Ai.fleeField FourWay -1.2 16 desire
            // Downhill (more negative) must lead away from the threat at (0,0).
            for c in 0..3 do
                Expect.isLessThan f.[cell (c + 1) 0] f.[cell c 0] $"cell {c + 1} is downhill of {c}"
        }

        test "fleeField is deterministic and drops non-finite values rather than poisoning neighbours" {
            let desire = Map.ofList [ cell 0 0, 0.0; cell 1 0, nan; cell 2 0, 2.0 ]
            let a = Ai.fleeField EightWay -1.2 16 desire
            let b = Ai.fleeField EightWay -1.2 16 desire
            Expect.equal a b "same input, same bytes"
            Expect.isFalse (a.ContainsKey(cell 1 0)) "the NaN cell is dropped, not propagated"
            Expect.all (a |> Map.toList |> List.map snd) System.Double.IsFinite "no NaN escapes"
        }

        test "an empty field is total" {
            Expect.isEmpty (Ai.fleeField FourWay -1.2 16 Map.empty |> Map.toList) "empty in, empty out"
        }

        test "a non-finite coefficient yields a smaller field, not a NaN-poisoned one" {
            // Regression: filtering only the INPUT for finiteness let `v * coefficient` reintroduce NaN, and
            // the relaxation cannot remove it (`value > lowest + 1.0` is false for NaN, so it survives).
            let desire = Map.ofList [ cell 0 0, 0.0; cell 1 0, 1.0 ]

            for bad in [ nan; infinity; -infinity ] do
                let f = Ai.fleeField FourWay bad 16 desire
                Expect.all (f |> Map.toList |> List.map snd) System.Double.IsFinite $"coefficient {bad} returns no poison"
        }

        test "a desire that overflows under scaling is dropped, not returned as -infinity" {
            // 1.7e308 is finite; 1.7e308 * -1.2 is not.
            let desire = Map.ofList [ cell 0 0, 0.0; cell 1 0, 1.7e308 ]
            let f = Ai.fleeField FourWay -1.2 16 desire
            Expect.isFalse (f.ContainsKey(cell 1 0)) "the overflowing cell is dropped"
            Expect.all (f |> Map.toList |> List.map snd) System.Double.IsFinite "no infinity escapes"
        }

        test "threatField does not wrap int64 when two cells sit at opposite Int32 extremes" {
            // Regression: `dc * dc` for dc ~ 4.3e9 overflows int64 and wraps NEGATIVE, so the squared-distance
            // test `<= r*r` would succeed and a source on the far side of the map would read as in range.
            let far = cell System.Int32.MinValue System.Int32.MinValue
            let here = cell System.Int32.MaxValue System.Int32.MaxValue
            let f = Ai.threatField (fun _ _ -> true) [ far, 100.0, System.Int32.MaxValue ] [ here ]
            Expect.equal f.[here] 0.0 "the far source is out of range, however the arithmetic is done"
        }

        // -----------------------------------------------------------------------------------------
        // best — the total tie-break. turn-based-tactics §4.9, exactly.
        // -----------------------------------------------------------------------------------------

        test "best picks the highest score" {
            let plans = [ ("a", 1.0, 0, 0); ("b", 3.0, 9, 9); ("c", 2.0, 0, 0) ]
            let pick = Ai.best (fun (_, s, _, _) -> s) (fun (_, _, t, p) -> struct (t, p)) plans
            Expect.equal (pick |> ValueOption.map (fun (n, _, _, _) -> n)) (ValueSome "b") "highest score wins"
        }

        test "GOLDEN: on a score tie, lowest target index then lowest position index (turn-based-tactics §4.9)" {
            // Same score. The index tail is the entire reason this is reproducible.
            let plans =
                [ ("late", 5.0, 3, 1)
                  ("winner", 5.0, 1, 0)
                  ("same-target-worse-pos", 5.0, 1, 4)
                  ("early-but-worse-target", 5.0, 2, 0) ]

            let pick = Ai.best (fun (_, s, _, _) -> s) (fun (_, _, t, p) -> struct (t, p)) plans
            Expect.equal (pick |> ValueOption.map (fun (n, _, _, _) -> n)) (ValueSome "winner") "(score, target, position)"
        }

        test "a fully tied plan keeps the incumbent — earliest in list order" {
            let plans = [ ("first", 1.0, 0, 0); ("second", 1.0, 0, 0) ]
            let pick = Ai.best (fun (_, s, _, _) -> s) (fun (_, _, t, p) -> struct (t, p)) plans
            Expect.equal (pick |> ValueOption.map (fun (n, _, _, _) -> n)) (ValueSome "first") "stable, hence total"
        }

        test "a NaN score is never preferred to a real one, in either list order" {
            let mk order =
                Ai.best (fun (_, s, _, _) -> s) (fun (_, _, t, p) -> struct (t, p)) order
                |> ValueOption.map (fun (n, _, _, _) -> n)

            Expect.equal (mk [ ("nan", nan, 0, 0); ("real", -99.0, 9, 9) ]) (ValueSome "real") "NaN first"
            Expect.equal (mk [ ("real", -99.0, 9, 9); ("nan", nan, 0, 0) ]) (ValueSome "real") "NaN second"
        }

        test "best of an empty list is ValueNone" {
            Expect.equal (Ai.best fst (fun _ -> struct (0, 0)) ([]: (float * int) list)) ValueNone "no plans, no pick"
        }

        testCase "PROPERTY: best is invariant under permutation when the tie-break is total (FsCheck >=500)"
        <| fun () ->
            // Give every plan a distinct (target, position), so (score, tie) is a total order and the
            // winner cannot depend on list order.
            let prop (scores: float list) =
                let plans =
                    scores
                    |> List.truncate 20
                    |> List.mapi (fun i s -> (i, (if System.Double.IsNaN s then 0.0 else s), i, i))

                if plans.IsEmpty then
                    true
                else
                    let pick l = Ai.best (fun (_, s, _, _) -> s) (fun (_, _, t, p) -> struct (t, p)) l
                    pick plans = pick (List.rev plans)

            Check.One(Config.QuickThrowOnFailure.WithMaxTest 500, prop)

        // -----------------------------------------------------------------------------------------
        // Difficulty — a knob vector, never a stat multiplier. Asserted structurally.
        // -----------------------------------------------------------------------------------------

        test "ACCEPTANCE: the difficulty vector has no stat field — only time and precision knobs" {
            // The doctrine is enforceable because there is no field with which to cheat. If someone adds
            // `DamageMultiplier`, this test names it.
            let fields =
                Reflection.FSharpType.GetRecordFields(typeof<Difficulty>)
                |> Array.map _.Name
                |> Array.sort

            let expected =
                [| "AimErrorSigma"; "ReactionTicks"; "SpotCycleTicks"; "ThreatWeight"; "UsesWeakPointTargeting" |]

            Expect.equal fields expected "every knob takes time or precision away; none touches a game stat"
        }

        test "the ladder is monotone in time and precision" {
            Expect.isGreaterThan Difficulty.easy.ReactionTicks Difficulty.hard.ReactionTicks "hard reacts sooner"
            Expect.isGreaterThan Difficulty.easy.AimErrorSigma Difficulty.hard.AimErrorSigma "hard aims tighter"
            Expect.isGreaterThan Difficulty.easy.SpotCycleTicks Difficulty.hard.SpotCycleTicks "hard looks more often"
            Expect.isTrue Difficulty.hard.UsesWeakPointTargeting "hard aims at the lower plate"
            Expect.isFalse Difficulty.easy.UsesWeakPointTargeting "easy aims centre-mass"
        }

        test "clamp is the identity on every rung of the ladder" {
            for rung in [ Difficulty.easy; Difficulty.normal; Difficulty.hard ] do
                Expect.equal (Difficulty.clamp rung) rung "the shipped ladder is already sane"
        }

        test "clamp sanitises a hostile knob vector, and is idempotent" {
            let hostile =
                { ReactionTicks = -5
                  AimErrorSigma = nan
                  SpotCycleTicks = -1
                  UsesWeakPointTargeting = true
                  ThreatWeight = infinity }

            let once = Difficulty.clamp hostile
            Expect.equal once.ReactionTicks 0 "floored"
            Expect.equal once.AimErrorSigma 0.0 "NaN reads as zero"
            Expect.equal once.SpotCycleTicks 0 "floored"
            Expect.equal once.ThreatWeight 0.0 "infinity reads as zero"
            Expect.equal (Difficulty.clamp once) once "idempotent"
        }
    ]

// ------------------------------------------------------------------------------------------------
// Influence map (work item 025, roadmap 3.2): a thin integer linear-falloff wrapper over
// distanceField, combined by max, plus the friendly-vs-enemy tension recipe.

module InfluenceMapTests =

    open Expecto
    open FsCheck
    open FS.GG.Game.Core

    // Open bounded grid cost: 1 inside w×h, 0 (impassable) outside.
    let private gridCost (w: int) (h: int) (c: Cell) =
        if c.Col >= 0 && c.Col < w && c.Row >= 0 && c.Row < h then 1 else 0

    [<Tests>]
    let influenceTests =
        testList "Game.Core Ai influenceMap (025, FR-001..FR-004)" [

            test "single source falls off linearly by baseStep (FR-001)" {
                let cost = gridCost 11 11
                let s = { Col = 5; Row = 5 }
                let m = Ai.influenceMap FourWay 5000 cost [ (s, 50) ]
                Expect.equal (Map.tryFind s m) (Some 50) "source cell = strength"
                Expect.equal (Map.tryFind { Col = 6; Row = 5 } m) (Some 40) "one tile away: strength - baseStep"
                Expect.equal (Map.tryFind { Col = 9; Row = 5 } m) (Some 10) "four tiles away: strength - 4*baseStep"
                Expect.isFalse (Map.containsKey { Col = 10; Row = 5 } m) "five tiles (influence 0) is absent"
                Expect.isTrue (m |> Map.forall (fun _ v -> v > 0)) "no non-positive influence is stored"
            }

            test "multiple sources combine by max (FR-002)" {
                let cost = gridCost 11 11
                let m = Ai.influenceMap FourWay 5000 cost [ ({ Col = 0; Row = 0 }, 30); ({ Col = 10; Row = 0 }, 50) ]
                // (9,0) is 10 tiles from src1 (absent there) and 1 tile from src2 -> 40.
                Expect.equal (Map.tryFind { Col = 9; Row = 0 } m) (Some 40) "nearest strong source dominates"
                // (1,0) is 1 tile from src1 -> 20, and 9 tiles from src2 -> 50-90 <=0 absent -> 20.
                Expect.equal (Map.tryFind { Col = 1; Row = 0 } m) (Some 20) "src1 contribution where src2 does not reach"
            }

            test "empty sources yield an empty map (FR-003)" {
                Expect.equal (Ai.influenceMap FourWay 5000 (gridCost 5 5) []) Map.empty "no sources ⇒ empty"
            }

            test "tension recipe: friendly minus enemy marks control (FR-004)" {
                let cost = gridCost 11 11
                let friendly = Ai.influenceMap FourWay 5000 cost [ ({ Col = 0; Row = 0 }, 60) ]
                let enemy = Ai.influenceMap FourWay 5000 cost [ ({ Col = 10; Row = 0 }, 60) ]
                let tension c =
                    (Map.tryFind c friendly |> Option.defaultValue 0) - (Map.tryFind c enemy |> Option.defaultValue 0)
                Expect.isTrue (tension { Col = 1; Row = 0 } > 0) "near the friendly source ⇒ positive tension"
                Expect.isTrue (tension { Col = 9; Row = 0 } < 0) "near the enemy source ⇒ negative tension"
            }

            testCase "influenceMap equals the brute-force per-source max over random sources (FsCheck)" <| fun () ->
                let prop (srcsRaw: (int * int * int) list) =
                    let cost = gridCost 7 7
                    let sources =
                        srcsRaw
                        |> List.map (fun (c, r, st) -> ({ Col = (abs c) % 7; Row = (abs r) % 7 }, 5 + (abs st) % 60))
                    let m = Ai.influenceMap FourWay 5000 cost sources
                    // brute force: per cell, max over sources of max(0, strength - distanceField[source])
                    let fields = sources |> List.map (fun (s, st) -> (st, Pathfinding.distanceField FourWay 5000 cost [ s ]))
                    let cells = [ for cc in 0..6 do for rr in 0..6 -> { Col = cc; Row = rr } ]
                    cells
                    |> List.forall (fun cell ->
                        let brute =
                            fields
                            |> List.choose (fun (st, f) -> Map.tryFind cell f |> Option.map (fun d -> st - d))
                            |> List.filter (fun v -> v > 0)
                            |> function [] -> None | vs -> Some(List.max vs)
                        Map.tryFind cell m = brute)

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 300, prop)

            testCase "influenceMap is byte-deterministic over random sources (FsCheck)" <| fun () ->
                let prop (srcsRaw: (int * int * int) list) =
                    let cost = gridCost 7 7
                    let sources = srcsRaw |> List.map (fun (c, r, st) -> ({ Col = (abs c) % 7; Row = (abs r) % 7 }, 5 + (abs st) % 60))
                    Ai.influenceMap FourWay 5000 cost sources = Ai.influenceMap FourWay 5000 cost sources

                Check.One(Config.QuickThrowOnFailure.WithMaxTest 200, prop)
        ]
