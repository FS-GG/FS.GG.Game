module Game.Render.Tests.ExperimentalMotionTests

// FS.GG.Game#812 (GAME-RENDER-02.10) proof: motion tokens, the deterministic easing, presentation-only animation as a
// pure function of render time, reduced motion, the flash and cue-width checks, and the DTCG round trip. The browser
// run that draws the frames on SVG, Pixi and Babylon, compares SVG with Pixi, and replays the frames under Fable is
// tests/Render.Browser.Smoke (`runMotionProof`).

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference.Experimental
open FS.GG.Game.Reference.Experimental.Motion

let private renderTheme name =
    Themes.presets |> List.find (fun p -> p.Name = name) |> Themes.deriveOrFail |> Themes.renderOrFail

let private classic = renderTheme "classic"
let private light = renderTheme "light"
let private presets = Themes.presets |> List.map (fun p -> p.Name, renderTheme p.Name)

let private at (theme: Theme) (tokens: MotionTokens) (t: int) = frame (fun () -> theme) (fun () -> tokens) sample t

/// The frames the digest covers: every millisecond of the first 2.4 s (two pulse periods, three march periods).
let digestTimes = [ 0..2400 ]

/// The digest .NET computes over `digestTimes` in classic with full motion; the browser smoke recomputes it under Fable.
let bakedDigest = Motion.digest (at classic full) digestTimes

let private geometry (scene: Scene) =
    scene.Nodes |> List.choose (function SceneNode.Rectangle(r, _) -> Some r | _ -> None)

let private colours (scene: Scene) =
    scene.Nodes |> List.choose (function SceneNode.Rectangle(_, c) -> Some c | _ -> None)

[<Tests>]
let tests =
    testList
        "experimental motion (#812)"
        [
            test "the standard algorithm derives durations from base and unit, and the alias set is closed" {
                Expect.equal (duration "motion.duration.fast" full) 100 "fast = base + unit"
                Expect.equal (duration "motion.duration.mid" full) 200 "mid = base + 2 units"
                Expect.equal (duration "motion.duration.slow" full) 300 "slow = base + 3 units"
                Expect.equal (duration "motion.period.pulse" full) 1200 "pulse period 12 units"
                Expect.equal (duration "motion.period.march" full) 800 "march period 8 units"
                Expect.equal full.Algorithms [ "standard" ] "algorithms recorded"
                for alias in aliasNames do
                    Expect.isTrue (full.Values.ContainsKey alias) (alias + " is resolved")
                let slower = create { seed with Values = seed.Values.Add("motion.unit", Duration 150) } [ standard ]
                Expect.equal (duration "motion.duration.slow" slower) 450 "a seed change re-derives the map"
            }

            test "reduced motion zeroes every decorative duration, period and amplitude, after any preset" {
                Expect.equal reduced.Algorithms [ "standard"; "reducedMotion" ] "composed after standard"
                for name in [ "motion.duration.fast"; "motion.duration.mid"; "motion.duration.slow"; "motion.period.pulse"; "motion.period.march" ] do
                    Expect.equal (duration name reduced) 0 name
                Expect.equal (number "motion.amplitude.pulse" reduced) 0.0 "no pulse amplitude"
            }

            test "the cubic Bezier easing pins its ends, is monotone for the standard curves, and quantises to permille" {
                for name in [ "motion.easing.standard"; "motion.easing.enter"; "motion.easing.exit"; "motion.easing.inOut"; "motion.easing.linear" ] do
                    let b = easing name full
                    Expect.equal (easePermille b 0 300) 0 (name + " starts at 0")
                    Expect.equal (easePermille b 300 300) 1000 (name + " ends at 1000")
                    Expect.equal (easePermille b -5 300) 0 (name + " before the start")
                    Expect.equal (easePermille b 900 300) 1000 (name + " after the end")
                    let values = [ for e in 0..300 -> easePermille b e 300 ]
                    Expect.isTrue (List.pairwise values |> List.forall (fun (a, c) -> c >= a)) (name + " is monotone")
                Expect.equal (easePermille (easing "motion.easing.linear" full) 150 300) 500 "linear is linear"
                Expect.equal (easePermille (easing "motion.easing.standard" full) 10 0) 1000 "a zero duration is settled at once"
            }

            test "integer colour mixing is exact at its ends and never uses Math.Round" {
                let a = Themes.colour (Themes.deriveOrFail Themes.presets.Head) "glyph.selection"
                let b = { a with Red = 0uy; Green = 0uy; Blue = 0uy }
                Expect.equal (mix a b 0) a "k = 0 is a"
                Expect.equal (mix a b 1000) b "k = 1000 is b"
                // A half-way tie rounds half up: 2 -> 3 at 500 permille is 2.5, so 3 (round-half-to-even would give 2).
                let c v = { Red = v; Green = v; Blue = v; Alpha = 255uy }
                Expect.equal (mix (c 2uy) (c 3uy) 500) (c 3uy) "half up, not to even"
            }

            test "a frame is a pure function of render time: same t, same Scene; settled at rest equals the still frame" {
                for t in [ 0; 1; 137; 600; 1199; 1200; 5000 ] do
                    Expect.equal (at classic full t) (at classic full t) (sprintf "t = %d twice" t)
                // At a multiple of both periods, with the transition settled, motion is at rest.
                Expect.equal (at classic full 2400) (still classic) "at rest, motion draws what the library draws without it"
                Expect.notEqual (at classic full 300) (still classic) "mid-pulse differs from the still frame"
                Expect.notEqual (geometry (at classic full 100)) (geometry (at classic full 200)) "the arrow marches"
            }

            test "frame-rate independence: sampling every 16 ms or every 7 ms gives the same frame at every shared t" {
                let replay (step: int) = [ for t in 0..step..2400 -> t, at classic full t ] |> Map.ofList
                let r60, r144 = replay 16, replay 7
                for t in [ 0; 112; 336; 560; 1008; 2352 ] do
                    Expect.equal r60.[t] r144.[t] (sprintf "t = %d" t)
                    Expect.equal r60.[t] (at classic full t) "and equal to the direct sample"
            }

            test "the baked digest is reproducible (the smoke compares Fable's against it)" {
                Expect.equal (Motion.digest (at classic full) digestTimes) bakedDigest "same digest twice"
                Expect.notEqual (Motion.digest (at classic reduced) digestTimes) bakedDigest "reduced motion differs"
                Expect.equal FS.GG.Game.Reference.Experimental.MotionBaked.digest bakedDigest "the committed digest is current"
            }

            test "reduced motion gives static frames, each equal to the still frame" {
                for theme in [ classic; light ] do
                    let still = still theme
                    for t in [ 0..37..2400 ] do
                        Expect.equal (at theme reduced t) still (sprintf "%s reduced at t = %d" (Theme.name theme) t)
            }

            test "a theme switch mid-animation keeps the phase and redraws the colours at the next frame" {
                let current = ref classic
                let read () = current.Value
                let before = frame read (fun () -> full) sample 480
                current.Value <- light
                let after = frame read (fun () -> full) sample 480
                Expect.equal (geometry after) (geometry before) "same geometry: the phase depends on time alone"
                Expect.notEqual (colours after) (colours before) "the colours are light's"
                Expect.equal after (at light full 480) "the switched frame is light's own frame at t"
                current.Value <- classic
                Expect.equal (frame read (fun () -> full) sample 480) before "and back to classic gives classic's frame"
            }

            test "turning reduced motion on mid-animation settles at the next frame" {
                let tokens = ref full
                let f t = frame (fun () -> classic) (fun () -> tokens.Value) sample t
                Expect.notEqual (f 700) (still classic) "animating before"
                tokens.Value <- reduced
                Expect.equal (f 700) (still classic) "still after"
            }

            test "presentation state is derived from (state, render time) and retargets from the shown value" {
                let p0 = { SelectedSince = None; PlanSince = None; Health = { From = 1.0; To = 1.0; Since = 0 } }
                let p1 = observe full true false 0.5 100 p0
                Expect.equal p1.SelectedSince (Some 100) "selection stamped at its first Present"
                let p2 = observe full true false 0.5 180 p1
                Expect.equal p2.SelectedSince (Some 100) "and kept"
                let shown = transitionAt full p2.Health 250
                let p3 = observe full true false 0.2 250 p2
                Expect.equal p3.Health.From shown "a new target starts from the shown value: no snap back"
                Expect.equal (observe full false false 0.2 260 p3).SelectedSince None "deselection clears the stamp"
                // A backend switch changes nothing here: presentation is the view's value, and the next backend draws
                // frame(t) from it.
                Expect.equal (frame (fun () -> classic) (fun () -> full) p3 400) (frame (fun () -> classic) (fun () -> full) p3 400) "continues"
            }

            test "every pulse colour keeps contrastTarget on the ground, in every preset" {
                for name, theme in presets do
                    Expect.isEmpty (pulseContrastViolations theme full) (name + ": every permille step")
            }

            test "flash safety (WCAG 2.3.1): the pulse passes in every preset, and a planted fast pulse is refused" {
                for name, theme in presets do
                    Expect.isEmpty (flashViolations theme full) name
                    Expect.isEmpty (flashViolations theme reduced) (name + " reduced")
                let general, _ = flashes (pulseColour classic full) 3000
                Expect.isLessThanOrEqual general 1.0 "classic's 1.2 s pulse: at most one flash in any second"
                let fast = { full with Values = full.Values.Add("motion.period.pulse", Duration 250) }
                Expect.isNonEmpty (flashViolations classic fast) "a 250 ms pulse is 4 flashes per second"
                let red = { Red = 255uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }
                let black = { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }
                let _, redFlashes = flashes (fun t -> if (t / 100) % 2 = 0 then red else black) 3000
                Expect.isGreaterThan redFlashes 3.0 "a 5 Hz saturated red flicker is caught as red flashes"
                let dimRed = { Red = 120uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }
                let _, dim = flashes (fun t -> if (t / 100) % 2 = 0 then dimRed else black) 3000
                Expect.isGreaterThan dim 3.0 "a dim red flicker is caught by the WCAG 2.2 chromaticity rule"
            }

            test "the marching arrow's pixels flash slower than 3 per second" {
                let shaft = 90, 52
                let colourAtShaft t = colourAt (at classic full t) (fst shaft) (snd shaft) |> Option.get
                let general, red = flashes colourAtShaft 3000
                Expect.isLessThanOrEqual general 3.0 "general"
                Expect.isLessThanOrEqual red 3.0 "red"
            }

            test "the static frame carries everything: the arrowhead gives the direction, the brackets the selection" {
                let s = still classic
                let path = Theme.tryColour "glyph.path" classic |> Option.get
                let selection = Theme.tryColour "glyph.selection" classic |> Option.get
                Expect.equal (colourAt s 128 86) (Some path) "arrowhead below the last waypoint, pointing down"
                Expect.isTrue (colours s |> List.contains selection) "brackets in the selection colour"
            }

            test "the focus ring settles from twice its width and never drops below 2 CSS px (ADR-0001)" {
                for css in [ 0.5; 1.0; 1.5; 2.0; 3.0 ] do
                    for tokens in [ full; reduced ] do
                        for e in 0..120 do
                            let w = focusBand tokens 2.0 css e
                            Expect.isGreaterThanOrEqual (w * css) 2.0 (sprintf "css %g, t %d" css e)
                            let whole = w * css
                            Expect.floatClose Accuracy.high whole (System.Math.Round whole) "a whole CSS px"
                Expect.equal (focusBand reduced 2.0 1.0 0) 2.0 "reduced motion: settled at once"
                Expect.equal (focusBand full 2.0 1.0 0) 4.0 "full motion: starts at twice"
                Expect.equal (focusBand full 2.0 1.0 100) 2.0 "settled after motion.duration.fast"
            }

            test "DTCG: motion tokens round-trip, and bad types, units and curves are refused by name" {
                for tokens in [ full; reduced ] do
                    match ofDtcg (toDtcg tokens) with
                    | Ok back ->
                        Expect.equal back.Values tokens.Values "values"
                        Expect.equal back.Algorithms tokens.Algorithms "algorithms"
                    | Error e -> failtestf "refused: %A" e
                let text = toDtcg full
                let refusedWith (edited: string) (needle: string) =
                    match ofDtcg edited with
                    | Ok _ -> failtestf "accepted %s" needle
                    | Error e -> Expect.isTrue (e |> List.exists (fun m -> m.Contains needle)) (sprintf "%A names %s" e needle)
                refusedWith (text.Replace("\"value\": 100, \"unit\": \"ms\"", "\"value\": 100, \"unit\": \"min\"")) "unit min"
                refusedWith (text.Replace("[ 0.2, 0, 0, 1 ]", "[ 1.2, 0, 0, 1 ]")) "x values"
                refusedWith (text.Replace("[ 0.2, 0, 0, 1 ]", "[ 0.2, 0, 0 ]")) "four numbers"
                refusedWith (text.Replace("\"value\": 100, \"unit\": \"ms\"", "\"value\": 100.5, \"unit\": \"ms\"")) "whole number"
                refusedWith (text.Replace("\"motion.period.march\"", "\"motion.period.wobble\"")) "motion.period.wobble"
                refusedWith (text.Replace("\"reducedMotion\"", "\"spin\"").Replace("\"standard\" ]", "\"standard\", \"spin\" ]")) "spin"
                match ofDtcg (text.Replace("\"value\": 100, \"unit\": \"ms\"", "\"value\": 0.1, \"unit\": \"s\"")) with
                | Ok t -> Expect.equal (duration "motion.unit" t) 100 "seconds are read as milliseconds"
                | Error e -> failtestf "refused seconds: %A" e
            }
        ]
