module Game.Render.Tests.ExperimentalThemesTests

// FS.GG.Game#795 (GAME-RENDER-02.8) proof: the colour science, theme derivation, presets, DTCG round trip, player
// override, runtime switching and the encoding grammar's build-time rules. The browser run that draws the worked
// example in every preset on SVG, Pixi and Babylon, and derives the presets again under Fable, is
// tests/Render.Browser.Smoke (`runThemeProof`).

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference.Experimental
open FS.GG.Game.Reference.Experimental.Colour
open FS.GG.Game.Reference.Experimental.Themes
open FS.GG.Game.Reference.Experimental.Symbology

let private themes = presets |> List.map deriveOrFail
let private theme name = themes |> List.find (fun t -> t.Name = name)

let private errorsOf (result: Result<Symbology<'E>, SymbologyError list>) =
    match result with
    | Ok _ -> []
    | Error errors -> errors

let private refuses (draft: Draft<'E>) (expected: SymbologyError -> bool) (what: string) =
    let errors = errorsOf (build themes draft)
    Expect.isTrue (errors |> List.exists expected) (sprintf "%s: expected the error, got %A" what errors)
    Expect.equal errors (List.distinct errors) "errors are de-duplicated"
    for error in errors do
        Expect.isNotEmpty (explain error) "every error explains itself"

let private accepts (draft: Draft<'E>) (what: string) =
    Expect.isEmpty (errorsOf (build themes draft)) what

type private Probe = { Kind: int; Level: int; Share: float; On: bool; Who: Glyphs.Identity; Side: int; Name: string }

let private probe = { Kind = 0; Level = 0; Share = 0.5; On = false; Who = Glyphs.Own; Side = 0; Name = "p" }
let private base24 = Symbology.create<Probe> "unit" 24.0 |> Encode.identity "allegiance" (fun p -> p.Who) Frame

/// Pixels decided in both scenes whose colours differ.
let private differing (width: float) (height: float) (a: Scene) (b: Scene) =
    let other = ScenePixels.expectations width height b |> List.map (fun e -> e.Point, e.Colour) |> Map.ofList
    ScenePixels.expectations width height a
    |> List.filter (fun e -> match other.TryFind e.Point with Some c -> c <> e.Colour | None -> false)
    |> List.length

[<Tests>]
let tests =
    testList
        "experimental themes and symbology (#795)"
        [
            test "OKLab matches published reference values" {
                let white = oklchOf (rgb 255 255 255)
                Expect.floatClose { absolute = 1e-4; relative = 0.0 } white.L 1.0 "white is L 1"
                Expect.isLessThan white.C 1e-4 "white has no chroma"
                let red = oklchOf (rgb 255 0 0)
                Expect.floatClose { absolute = 5e-4; relative = 0.0 } red.L 0.62796 "sRGB red L"
                Expect.floatClose { absolute = 5e-4; relative = 0.0 } red.C 0.25768 "sRGB red C"
                Expect.floatClose { absolute = 0.05; relative = 0.0 } red.H 29.23 "sRGB red h"
            }

            test "the committed sRGB->linear table equals the transfer function" {
                for i in 0..255 do
                    let c = float i / 255.0
                    let v = if c <= 0.04045 then c / 12.92 else ((c + 0.055) / 1.055) ** 2.4
                    Expect.floatClose { absolute = 1e-15; relative = 1e-14 } srgbToLinear.[i] v (sprintf "entry %d" i)
            }

            test "half-up quantisation round-trips every 8-bit colour through OKLCH; Floor does not" {
                let mutable halfUp = 0
                let mutable floorFails = 0
                for r in 0..255 do
                    for g in 0..255 do
                        for b in 0..255 do
                            let c = rgb r g b
                            let lin = linearOfOklab (oklabOfOklch (oklchOf c))
                            let back = ofLinear lin
                            if back <> c then halfUp <- halfUp + 1
                            let lr, lg, lb = lin
                            let enc (v: float) =
                                let v = if v < 0.0 then 0.0 elif v > 1.0 then 1.0 else v
                                if v <= 0.0031308 then 12.92 * v else 1.055 * (v ** (1.0 / 2.4)) - 0.055
                            let viaFloor = { Red = quantiseFloor (enc lr); Green = quantiseFloor (enc lg); Blue = quantiseFloor (enc lb); Alpha = 255uy }
                            if viaFloor <> c then floorFails <- floorFails + 1
                printfn "OKLCH round trip over 16777216 colours: half-up %d failures, Floor %d failures (%.1f %%)" halfUp floorFails (100.0 * float floorFails / 16777216.0)
                Expect.equal halfUp 0 "half up round-trips every colour"
                Expect.isGreaterThan floorFails 8000000 "Floor fails on most colours (the #794 review measured 94.8 % with its implementation)"
            }

            test "CIEDE2000 matches Sharma, Wu and Dalal's test data" {
                let lab l a b : Lab = { L = l; A = a; B = b }
                let cases =
                    [
                        lab 50.0 2.6772 -79.7751, lab 50.0 0.0 -82.7485, 2.0425
                        lab 50.0 3.1571 -77.2803, lab 50.0 0.0 -82.7485, 2.8615
                        lab 50.0 2.8361 -74.0200, lab 50.0 0.0 -82.7485, 3.4412
                        lab 50.0 0.0 0.0, lab 50.0 -1.0 2.0, 2.3669
                        lab 50.0 2.5 0.0, lab 73.0 25.0 -18.0, 27.1492
                        lab 60.2574 -34.0099 36.2677, lab 60.4626 -34.1751 39.4387, 1.2644
                    ]
                for a, b, expected in cases do
                    Expect.floatClose { absolute = 1e-4; relative = 0.0 } (deltaE2000 a b) expected (sprintf "%A vs %A" a b)
                    Expect.floatClose { absolute = 1e-4; relative = 0.0 } (deltaE2000 b a) expected "symmetric"
            }

            test "Machado simulation keeps white and black, and collapses red and green for deuteranopia" {
                for vision in visions do
                    let r, g, b = simulate vision (rgb 255 255 255)
                    Expect.floatClose { absolute = 1e-5; relative = 0.0 } (r + g + b) 3.0 (sprintf "%A white" vision)
                    let r, g, b = simulate vision (rgb 0 0 0)
                    Expect.equal (r + g + b) 0.0 "black"
                let red, green = rgb 200 60 60, rgb 90 140 60
                Expect.isGreaterThan (deltaEUnder Normal red green) 40.0 "far apart with normal vision"
                Expect.isLessThan (deltaEUnder Deutan red green) 15.0 "close for deuteranopia"
            }

            test "gamut mapping lands in sRGB, keeps lightness, and solving meets every target" {
                for l in [ 0.1; 0.3; 0.5; 0.7; 0.9 ] do
                    for h in [ 0.0 .. 30.0 .. 330.0 ] do
                        let r, g, b = gamutMap { L = l; C = 0.4; H = h }
                        for v in [ r; g; b ] do
                            Expect.isTrue (v >= 0.0 && v <= 1.0) "in gamut"
                        Expect.floatClose { absolute = 0.03; relative = 0.0 } (oklabOfLinear (r, g, b)).L l "lightness kept"
                let ground = rgb 34 40 49
                for ratio in [ 1.5; 3.0; 4.5; 7.0; 11.0 ] do
                    for h in [ 0.0 .. 45.0 .. 315.0 ] do
                        match solve ground Lighter ratio 0.14 h with
                        | Some c -> Expect.isGreaterThanOrEqual (contrast c ground) ratio (sprintf "ratio %g hue %g" ratio h)
                        | None -> failtestf "ratio %g hue %g unreachable" ratio h
            }

            test "the five presets derive, validate and report their measured hue capacity" {
                Expect.equal presetNames [ "classic"; "light"; "highContrast"; "colourBlindSafe"; "monochrome" ] "the presets"
                let capacities = themes |> List.map (fun t -> t.Name, hueCapacity t 24.0)
                printfn "hue capacity at 24 units: %A" capacities
                Expect.equal (capacities |> List.map snd) [ 5; 5; 5; 3; 0 ] "capacities at 24 units"
                Expect.equal (theme "highContrast").Capacity [ 4; 5; 5 ] "highContrast per size bucket"
                for t in themes do
                    Expect.isEmpty (validateFor (hueCapacity t 24.0) t) (sprintf "%s validates for its capacity" t.Name)
                Expect.equal (theme "highContrast").Map.Floor 7.0 "highContrast's floor"
                Expect.equal (theme "highContrast").Map.Applied [ "base"; "dark"; "highContrast"; "repair" ] "composed in order"
            }

            test "calibration evidence: the presets' smallest margins, asserted as the design's table states them" {
                let r1 (x: float) = System.Math.Floor(x * 10.0 + 0.5) / 10.0
                let pairs (xs: string list) = [ for i in 0 .. xs.Length - 1 do for j in i + 1 .. xs.Length - 1 -> xs.[i], xs.[j] ]
                let allegiances = [ for i in Glyphs.identities -> "glyph.allegiance." + identityName i ]
                // Preset: capacity at 24, teams min dE normal / simulated, allegiances min dE normal / simulated, min
                // allegiance contrast on the ground (rounded to 0.1 and 0.01). The design's T5 table.
                let expected =
                    [
                        "classic", (5, 20.0, 8.4, 25.4, 16.2, 3.20)
                        "light", (5, 20.3, 8.4, 25.5, 14.1, 3.21)
                        "highContrast", (5, 19.9, 8.3, 24.3, 11.1, 7.21)
                        "colourBlindSafe", (3, 37.8, 8.0, 27.9, 17.1, 3.22)
                    ]
                for name, (capacity, teamN, teamS, allegN, allegS, floorMin) in expected do
                    let t = theme name
                    let n = hueCapacity t 24.0
                    let teamPairs = pairs [ for i in 0 .. n - 1 -> sprintf "glyph.team.%d" i ]
                    let minimum vision ps = ps |> List.map (fun (a, b) -> deltaEUnder vision (colour t a) (colour t b)) |> List.min
                    let simulated ps = [ Protan; Deutan; Tritan ] |> List.map (fun v -> minimum v ps) |> List.min
                    let floor = allegiances |> List.map (fun a -> contrast (colour t a) (colour t "surface.ground")) |> List.min
                    let measured = n, r1 (minimum Normal teamPairs), r1 (simulated teamPairs), r1 (minimum Normal (pairs allegiances)), r1 (simulated (pairs allegiances)), System.Math.Floor(floor * 100.0 + 0.5) / 100.0
                    printfn "%s: %A" name measured
                    Expect.equal measured (capacity, teamN, teamS, allegN, allegS, floorMin) (sprintf "%s's row of the design's table" name)
                    Expect.isGreaterThanOrEqual floor t.Map.Floor "contrast floor"
            }

            test "the baked preset data equals a fresh .NET derivation, alias for alias" {
                Expect.equal (PresetData.hexes |> List.map fst) presetNames "every preset is baked"
                for t in themes do
                    let baked = PresetData.hexes |> List.find (fun (n, _) -> n = t.Name) |> snd
                    Expect.equal (hexes t) baked (sprintf "%s re-derives to its baked hex" t.Name)
                Expect.equal (gridDigest ()) PresetData.gridDigest "the OKLCH grid digest is the baked one"
            }

            test "DTCG export then import gives equal resolved hex, and the committed token files are current" {
                // The repo root, found by walking up from the test binary (CI maps __SOURCE_DIRECTORY__ to /_/).
                let rec up (d: System.IO.DirectoryInfo | null) =
                    match d with
                    | null -> failwith "repo root (FS.GG.Game.slnx) not found"
                    | dir -> if System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "FS.GG.Game.slnx")) then dir.FullName else up dir.Parent
                let root = up (System.IO.DirectoryInfo(System.AppContext.BaseDirectory))
                let dir = System.IO.Path.Combine(root, "docs", "roadmaps", "game-render-02-themes", "themes")
                for t in themes do
                    let text = toDtcg t
                    match ofDtcg (theme "classic") text with
                    | Ok imported when t.Map.ColourCarries || t.Name = "monochrome" -> Expect.equal (hexes imported) (hexes t) (sprintf "%s round-trips onto classic" t.Name)
                    | Ok _ -> ()
                    | Error reasons ->
                        // A token file is validated against the base theme's rules on import (here classic's 3:1 floor).
                        Expect.isTrue (t.Name = "monochrome") (sprintf "%s imports: %A" t.Name reasons)
                    match ofDtcg t text with
                    | Ok same -> Expect.equal (hexes same) (hexes t) (sprintf "%s round-trips onto itself" t.Name)
                    | Error reasons -> failtestf "%s re-import refused: %A" t.Name reasons
                    Expect.equal (System.IO.File.ReadAllText(System.IO.Path.Combine(dir, t.Name + ".tokens.json")).Replace("\r\n", "\n")) text (sprintf "%s.tokens.json is current (run bake.fsx)" t.Name)
                match ofDtcg (theme "classic") "{ \"glyph\": { \"sparkle\": { \"$type\": \"color\", \"$value\": { \"colorSpace\": \"srgb\", \"components\": [ 1, 1, 1 ], \"hex\": \"#ffffff\" } } } }" with
                | Error [ reason ] -> Expect.stringContains reason "glyph.sparkle" "an unknown alias is named"
                | other -> failtestf "unknown alias accepted: %A" other
                match ofDtcg (theme "classic") "{ \"glyph\": { \"team\": { \"0\": { \"$type\": \"color\", \"$value\": { \"colorSpace\": \"srgb\", \"components\": [ 0, 0, 0 ], \"hex\": \"#262b33\" } } } } }" with
                | Error reasons -> Expect.isTrue (reasons |> List.exists (fun r -> r.Contains "glyph.team.0")) "a failing colour is refused, named"
                | Ok _ -> failtest "a team colour on the ground was accepted"
            }

            test "CSS variables carry every alias's hex" {
                for t in themes do
                    let css = cssVariables t
                    for alias, hex in hexes t do
                        Expect.stringContains css (sprintf "--fsgg-%s: %s;" (alias.Replace(".", "-")) hex) alias
            }

            test "a player team-colour override is validated again, and refused with its reason" {
                let classic = theme "classic"
                match overrideTeam 2 0 (rgb 40 46 56) classic with
                | Error violations -> Expect.isTrue (violations |> List.exists (function BelowFloor("glyph.team.0", "surface.ground", _, _) -> true | _ -> false)) "below the floor"
                | Ok _ -> failtest "a ground-coloured team was accepted"
                match overrideTeam 2 1 (colour classic "glyph.team.0") classic with
                | Error violations -> Expect.isTrue (violations |> List.exists (function NotDistinct("glyph.team.0", "glyph.team.1", _, _, _) -> true | _ -> false)) "not distinct"
                | Ok _ -> failtest "two equal teams were accepted"
                match overrideTeam 2 4 (colour classic "glyph.team.0") classic with
                | Error [ TeamOutsideGame(4, 2) ] -> ()
                | other -> failtestf "a team outside the game was accepted: %A" other
                match overrideTeam 2 0 (rgb 116 162 61) classic with
                | Ok t -> Expect.equal (colour t "glyph.team.0") (rgb 116 162 61) "applied at the alias layer"
                | Error violations -> failtestf "a valid override was refused: %A" (violations |> List.map explainViolation)
            }

            test "runtime switching: A -> B -> A gives the same pixels, the state is untouched, and a swap control differs" {
                let view = ArenaExample.view themes
                let state = ArenaExample.sample
                let switch = switchOf (theme "classic")
                let w, h = ArenaExample.width, ArenaExample.height
                let decided scene = ScenePixels.expectations w h scene |> List.map (fun e -> e.Point, e.Colour)
                let first = decided (view switch.Read state)
                for other in themes |> List.tail do
                    switch.Select other
                    let shown = view switch.Read state
                    Expect.isGreaterThan (differing w h (view (fun () -> theme "classic") state) shown) 50 (sprintf "classic and %s differ" other.Name)
                    switch.Select(theme "classic")
                    Expect.equal (decided (view switch.Read state)) first (sprintf "classic -> %s -> classic" other.Name)
                Expect.equal state ArenaExample.sample "the game state is the same value"
                Expect.isGreaterThan first.Length 600 "the example decides pixels"
            }

            test "monochrome keeps identity and team apart in decided pixels" {
                let mono = theme "monochrome"
                let players = build themes ArenaExample.playerDraft |> function Ok s -> s | Error e -> failtestf "%A" e
                let one (p: ArenaExample.Player) =
                    let spec = Symbology.spec players { X = 24.0; Y = 30.0 } p |> function Ok s -> s | Error e -> failtestf "%A" e
                    Symbology.scene mono 48.0 64.0 [ Symbology.draw mono (teamColouring mono players) spec false ]
                let a = ArenaExample.sample.Players.[1]
                for b in [ { a with Team = 2 }; { a with Team = 3 }; { a with Allegiance = Glyphs.Own; Team = 0 } ] do
                    let n = min (differing 48.0 64.0 (one a) (one b)) (differing 48.0 64.0 (one b) (one a))
                    Expect.isGreaterThan n 0 (sprintf "team %d/%A against team %d/%A" a.Team a.Allegiance b.Team b.Allegiance)
            }

            test "the worked example builds; adding the role icon is refused with the icon-slot error" {
                Expect.isEmpty (errorsOf (build themes ArenaExample.playerDraft)) "players"
                Expect.isEmpty (errorsOf (build themes ArenaExample.featureDraft)) "features"
                match build themes ArenaExample.playerDraftWithRoleIcon with
                | Error [ IconSlotTakenByBadge("role", "team", 24.0, Glyphs.Enemy) as e ] -> printfn "%s" (explain e)
                | other -> failtestf "expected the icon-slot error, got %A" other
                let notes = match build themes ArenaExample.playerDraft with Ok s -> s.Notes | Error _ -> []
                Expect.equal notes.Length 2 "team colour falls back in colourBlindSafe (capacity 3) and monochrome"
                Expect.equal
                    (ArenaExample.labels themes ArenaExample.sample |> List.head)
                    "own, team 1, health 90%, rank 3, carrying, Ada"
                    "the mirror label names identity and team"
            }

            test "D12 rule 1: no hue-only ordered data" {
                refuses (base24 |> Encode.ordinal "level" (fun p -> p.Level) [ 0; 1; 2 ] Fill) (function HueOnlyOrderedData("level", _) -> true | _ -> false) "ordinal"
                refuses (base24 |> Encode.fraction "share" (fun p -> p.Share) Fill) (function HueOnlyOrderedData("share", _) -> true | _ -> false) "fraction"
                refuses (base24 |> Encode.angle "side" (fun p -> float p.Side) Fill) (function HueOnlyOrderedData("side", _) -> true | _ -> false) "angle"
                accepts (base24 |> Encode.ordinal "level" (fun p -> p.Level) [ 0; 1; 2 ] (Amplifier Above)) "pips"
            }

            test "D12 rule 2: categorical hue needs a redundant channel" {
                refuses (base24 |> Encode.nominal "kind" (fun p -> p.Kind) [ 0; 1; 2 ] Fill) (function CategoricalHueWithoutRedundancy "kind" -> true | _ -> false) "hue only"
                accepts (base24 |> Encode.nominal "kind" (fun p -> p.Kind) [ 0; 1; 2 ] Fill |> Encode.nominal "kind" (fun p -> p.Kind) [ 0; 1; 2 ] Icon) "hue plus icon"
            }

            test "D12 rule 3: capacities (hue per theme and size, pips, steps, badge digits, icons)" {
                let cases = [ 0..3 ]
                refuses
                    (base24 |> Encode.nominal "kind" (fun p -> p.Kind) cases Fill |> Encode.nominal "kind" (fun p -> p.Kind) cases Icon)
                    (function OverCapacity("kind", Fill, 4, 3, Some "colourBlindSafe") -> true | _ -> false)
                    "four hues in colourBlindSafe"
                refuses (base24 |> Encode.ordinal "level" (fun p -> p.Level) [ 0..5 ] (Amplifier Above)) (function OverCapacity("level", _, 6, 5, None) -> true | _ -> false) "six pips"
                refuses (base24 |> Encode.ordinal "level" (fun p -> p.Level) [ 0..4 ] SizeStep) (function OverCapacity("level", SizeStep, 5, 4, None) -> true | _ -> false) "five size steps"
                refuses (base24 |> Encode.nominal "kind" (fun p -> p.Kind) [ 0..4 ] Icon) (function OverCapacity("kind", Icon, 5, 4, None) -> true | _ -> false) "five icons"
                refuses
                    (base24 |> Encode.team "side" (fun p -> p.Side) (List.replicate 10 Glyphs.Enemy) TeamMark)
                    (function OverCapacity("side", TeamMark, 10, 9, None) -> true | _ -> false)
                    "ten teams: the badge never wraps"
            }

            test "D12 rules 4 and 5: fractions are length, flags are selective" {
                refuses (base24 |> Encode.fraction "share" (fun p -> p.Share) SizeStep) (function FractionAsArea "share" -> true | _ -> false) "area"
                accepts (base24 |> Encode.fraction "share" (fun p -> p.Share) (Amplifier Below)) "bar"
                refuses (base24 |> Encode.flag "on" (fun p -> p.On) SizeStep) (function FlagNotSelective("on", SizeStep) -> true | _ -> false) "flag on size"
                accepts (base24 |> Encode.flag "on" (fun p -> p.On) Modifier) "flag on a modifier"
            }

            test "D12 rule 6 and D1: identity on the frame, labels below the minimum, the mandatory badge" {
                refuses (Symbology.create<Probe> "unit" 24.0 |> Encode.identity "who" (fun p -> p.Who) Icon) (function IdentityNotOnFrame("who", Icon) -> true | _ -> false) "identity on icon"
                refuses (Symbology.create<Probe> "unit" 10.0 |> Encode.identity "who" (fun p -> p.Who) Frame) (function IdentityBelowMinimumWithoutLabel("who", 10.0) -> true | _ -> false) "10 units"
                accepts (Symbology.create<Probe> "unit" 10.0 |> Encode.identity "who" (fun p -> p.Who) Frame |> Encode.label "name" (fun p -> p.Name)) "10 units with a label"
                let shared = [ Glyphs.Own; Glyphs.Enemy; Glyphs.Enemy ]
                let teamed size = Symbology.create<Probe> "unit" size |> Encode.identity "who" (fun p -> p.Who) Frame |> Encode.team "side" (fun p -> p.Side) shared TeamMark
                refuses (teamed 16.0) (function TeamBadgeBelowMinimumWithoutLabel("side", 16.0, Glyphs.Enemy) -> true | _ -> false) "16 units, shared"
                accepts (teamed 16.0 |> Encode.label "name" (fun p -> p.Name)) "16 units with a label"
                accepts (teamed 22.0) "22 units"
            }

            test "the icon slot is unavailable while a mandatory badge occupies a 22-39 unit frame (Root's #794 merge comment)" {
                let roster shared = if shared then [ Glyphs.Own; Glyphs.Enemy; Glyphs.Enemy ] else [ Glyphs.Own; Glyphs.Enemy; Glyphs.Neutral ]
                let draft size shared =
                    Symbology.create<Probe> "unit" size
                    |> Encode.identity "who" (fun p -> p.Who) Frame
                    |> Encode.team "side" (fun p -> p.Side) (roster shared) TeamMark
                    |> Encode.nominal "kind" (fun p -> p.Kind) [ 0; 1; 2 ] Icon
                for size in [ 22.0; 24.0; 32.0; 39.0 ] do
                    refuses (draft size true) (function IconSlotTakenByBadge("kind", "side", s, Glyphs.Enemy) -> s = size | _ -> false) (sprintf "%g units" size)
                accepts (draft 40.0 true) "40 units: side by side"
                accepts (draft 24.0 false) "no shared allegiance: no badge"
                // A size step that reaches into the band counts too.
                refuses
                    (Symbology.create<Probe> "unit" 18.0
                     |> Encode.identity "who" (fun p -> p.Who) Frame
                     |> Encode.label "name" (fun p -> p.Name)
                     |> Encode.team "side" (fun p -> p.Side) (roster true) TeamMark
                     |> Encode.nominal "kind" (fun p -> p.Kind) [ 0; 1; 2 ] Icon
                     |> Encode.ordinal "level" (fun p -> p.Level) [ 0; 1; 2 ] SizeStep)
                    (function IconSlotTakenByBadge _ -> true | _ -> false)
                    "18 to 26 units"
            }

            test "one field per slot, and the facing amplifier needs the stroked profile" {
                refuses
                    (base24 |> Encode.nominal "kind" (fun p -> p.Kind) [ 0; 1 ] Icon |> Encode.nominal "other" (fun p -> p.Level) [ 0; 1 ] Icon)
                    (function SlotTaken(Icon, "kind", "other") -> true | _ -> false)
                    "two on the icon"
                refuses (base24 |> Encode.angle "facing" (fun p -> float p.Side) (Amplifier Facing)) (function NeedsStrokedProfile("facing", _) -> true | _ -> false) "portable"
                accepts (base24 |> Symbology.stroked |> Encode.angle "facing" (fun p -> float p.Side) (Amplifier Facing)) "stroked"
            }

            test "at 40 units the icon and the badge sit side by side on frame ink, with a 1-unit margin, in every frame" {
                let theme = theme "classic"
                for identity in Glyphs.identities do
                    let spec: GlyphSpec =
                        { Family = "unit"; Anchor = { X = 48.0; Y = 48.0 }; Frame = 40.0; Identity = Some identity; Icon = Some 3; Team = Some 7; Badge = Some 8
                          FillCase = None; SizeStep = 0; Pips = None; Bar = None; Modifier = false; Corner = false; Label = "" }
                    let d = Symbology.draw theme true spec false
                    let frame = Glyphs.identityFrame identity spec.Anchor 40.0 (rgb 1 1 1)
                    let covered (x: float) (y: float) =
                        frame |> List.exists (function SceneNode.Rectangle((rx, ry, rw, rh), _) -> x >= rx && x + 1.0 <= rx + rw && y >= ry && y + 1.0 <= ry + rh | _ -> false)
                    let ink = colour theme "glyph.badge.ink"
                    let inner = d.Body |> List.filter (function SceneNode.Rectangle(_, c) -> c = ink | _ -> false)
                    Expect.equal inner.Length ((Symbology.iconCells 3).Length + 7) "the icon's bars and digit 8's seven segments"
                    for node in inner do
                        match node with
                        | SceneNode.Rectangle((x, y, w, h), _) ->
                            for px in [ x - 1.0 .. x + w ] do
                                for py in [ y - 1.0 .. y + h ] do
                                    Expect.isTrue (covered px py) (sprintf "%A: (%g, %g) is on frame ink" identity px py)
                        | _ -> ()
            }

            test "B2: a team without identity on the frame is refused, and a roster that disagrees with an entity is a typed error" {
                // The #805 review's two drafts.
                let teamOnly = Symbology.create<Probe> "unit" 24.0 |> Encode.team "side" (fun x -> x.Side) [ Glyphs.Own; Glyphs.Enemy ] TeamMark
                refuses teamOnly (function TeamWithoutIdentity "side" -> true | _ -> false) "team only"
                let lie =
                    Symbology.create<Probe> "unit" 24.0
                    |> Encode.identity "who" (fun x -> x.Who) Frame
                    |> Encode.team "side" (fun x -> x.Side) [ Glyphs.Own; Glyphs.Enemy; Glyphs.Ally ] TeamMark
                match build themes lie with
                | Ok s ->
                    match Symbology.spec s { X = 24.0; Y = 24.0 } { probe with Who = Glyphs.Enemy; Side = 2 } with
                    | Error [ RosterMismatch("side", 2, Glyphs.Ally, Glyphs.Enemy) as e ] -> Expect.stringContains (explain e) "roster" "explained"
                    | other -> failtestf "an Enemy on the Ally team was drawn: %A" other
                    Expect.isOk (Symbology.spec s { X = 24.0; Y = 24.0 } { probe with Who = Glyphs.Enemy; Side = 1 }) "a matching entity"
                | Error e -> failtestf "the roster itself is valid: %A" e
            }

            test "N7: an out-of-range value is a typed error, teams never wrap, FillStep is refused, the badge is decided per size step" {
                let nominal = base24 |> Encode.nominal "kind" (fun x -> x.Kind) [ 0; 1; 2 ] Icon
                match build themes nominal with
                | Ok s ->
                    match Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Kind = 7 } with
                    | Error [ ValueOutOfRange("kind", _, _) ] -> ()
                    | other -> failtestf "case 7 of 3 drew: %A" other
                | Error e -> failtestf "%A" e
                let nine = base24 |> Encode.team "side" (fun x -> x.Side) (List.replicate 9 Glyphs.Own) TeamMark
                match build themes nine with
                | Ok s ->
                    match Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Side = 9 } with
                    | Error [ ValueOutOfRange("side", "team 9", _) ] -> ()
                    | other -> failtestf "team 9 of 9 drew (a wrap): %A" other
                    match Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Side = 8 } with
                    | Ok spec -> Expect.equal spec.Badge (Some 9) "team 8 shows digit 9"
                    | Error e -> failtestf "%A" e
                | Error e -> failtestf "%A" e
                let fraction = base24 |> Encode.fraction "share" (fun x -> x.Share) (Amplifier Below)
                match build themes fraction with
                | Ok s -> Expect.isError (Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Share = 1.5 }) "a fraction of 1.5"
                | Error e -> failtestf "%A" e
                refuses (base24 |> Encode.ordinal "level" (fun x -> x.Level) [ 0; 1; 2 ] FillStep) (function SlotNotInProof("level", FillStep) -> true | _ -> false) "FillStep"
                let stepped =
                    Symbology.create<Probe> "unit" 18.0
                    |> Encode.identity "who" (fun x -> x.Who) Frame
                    |> Encode.label "name" (fun x -> x.Name)
                    |> Encode.team "side" (fun x -> x.Side) [ Glyphs.Own; Glyphs.Enemy; Glyphs.Enemy ] TeamMark
                    |> Encode.ordinal "level" (fun x -> x.Level) [ 0; 1; 2 ] SizeStep
                match build themes stepped with
                | Ok s ->
                    let badgeAt level = (Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Who = Glyphs.Enemy; Side = 1; Level = level } |> function Ok g -> g.Frame, g.Badge | Error e -> failtestf "%A" e)
                    Expect.equal (badgeAt 0) (18.0, None) "18 units: under the badge minimum, the label carries team"
                    Expect.equal (badgeAt 1) (22.0, Some 2) "22 units: the badge"
                    Expect.equal (badgeAt 2) (26.0, Some 2) "26 units: the badge"
                | Error e -> failtestf "18 to 26 with a label and no icon: %A" e
            }

            test "N6: hue capacity is checked at the smallest frame, and team colouring is decided once per scene" {
                let hc = theme "highContrast"
                Expect.equal (hueCapacity hc 20.0, hueCapacity hc 32.0) (4, 5) "highContrast holds 4 hues under 24 units, 5 from 24"
                let stepped =
                    Symbology.create<Probe> "unit" 20.0
                    |> Encode.identity "who" (fun x -> x.Who) Frame
                    |> Encode.nominal "kind" (fun x -> x.Kind) [ 0..4 ] Fill
                    |> Encode.nominal "kind" (fun x -> x.Kind) [ 0..4 ] Icon
                    |> Encode.ordinal "level" (fun x -> x.Level) [ 0; 1; 2 ] SizeStep
                refuses stepped (function OverCapacity("kind", Fill, 5, 4, Some "highContrast") -> true | _ -> false) "5 hues from 20 units"
                let players = build themes ArenaExample.playerDraft |> function Ok s -> s | Error e -> failtestf "%A" e
                Expect.equal
                    (themes |> List.map (fun t -> t.Name, teamColouring t players))
                    [ "classic", true; "light", true; "highContrast", true; "colourBlindSafe", false; "monochrome", false ]
                    "four teams keep team colours where the capacity holds them"
            }

            test "N10: WrongSlot names the allowed slots" {
                let errors = errorsOf (build themes (base24 |> Encode.fraction "share" (fun x -> x.Share) Icon))
                match errors with
                | [ WrongSlot("share", "fraction", Icon, [ Amplifier Below ]) as e ] -> Expect.stringContains (explain e) "Slot.Amplifier Below" "names the slot to use"
                | other -> failtestf "%A" other
            }

            test "N2 and N12: icon ink and status meaning are validated" {
                let classic = theme "classic"
                let token alias (c: Color) =
                    let path = alias |> fun (a: string) -> a.Split '.'
                    let leaf = sprintf "{ \"$type\": \"color\", \"$value\": { \"colorSpace\": \"srgb\", \"components\": [ 0, 0, 0 ], \"hex\": \"%s\" } }" (hex c)
                    Array.foldBack (fun (k: string) inner -> sprintf "{ \"%s\": %s }" k inner) path leaf
                match ofDtcg classic (token "glyph.icon.ink" (colour classic "glyph.allegiance.neutral")) with
                | Error reasons -> Expect.isTrue (reasons |> List.exists (fun r -> r.Contains "glyph.icon.ink")) "icon ink on the neutral fill is refused"
                | Ok _ -> failtest "icon ink equal to a fill was accepted"
                match ofDtcg classic (token "status.bad" (colour classic "status.good")) with
                | Error reasons -> Expect.isTrue (reasons |> List.exists (fun r -> r.Contains "status.bad")) "a green bad is refused"
                | Ok _ -> failtest "status.bad as green was accepted"
                for t in themes |> List.filter (fun t -> t.Map.ColourCarries) do
                    for alias in [ "status.good"; "status.warn"; "status.bad" ] do
                        Expect.isTrue (statusReads alias (colour t alias)) (sprintf "%s %s keeps its meaning" t.Name alias)
                    Expect.isTrue (statusOrdered (colour t "status.warn") (colour t "status.bad")) (sprintf "%s: amber is lighter than red" t.Name)
            }

            test "N3: a DTCG colour with alpha, without a hex, or with a malformed hex is refused" {
                let classic = theme "classic"
                let tok v = sprintf "{ \"glyph\": { \"team\": { \"0\": { \"$type\": \"color\", \"$value\": %s } } } }" v
                for name, value, needle in
                    [
                        "alpha", "{ \"colorSpace\": \"srgb\", \"components\": [ 1, 0, 0 ], \"alpha\": 0.2, \"hex\": \"#ff0000\" }", "alpha"
                        "no hex", "{ \"colorSpace\": \"srgb\", \"components\": [ 0.13, 0.16, 0.19 ] }", "no hex"
                        "malformed", "{ \"colorSpace\": \"srgb\", \"components\": [ 0, 0, 0 ], \"hex\": \"#12345\" }", "malformed"
                    ] do
                    match ofDtcg classic (tok value) with
                    | Error reasons -> Expect.isTrue (reasons |> List.exists (fun r -> r.Contains "glyph.team.0" && r.Contains needle)) (sprintf "%s: %A" name reasons)
                    | Ok _ -> failtestf "%s was accepted" name
            }
        ]
