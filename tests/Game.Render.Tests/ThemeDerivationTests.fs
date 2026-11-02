module Game.Render.Tests.ThemeDerivationTests

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Render.Symbology
open FS.GG.Game.Reference
open ThemeNumerics

let private themes = Theme.presets
let private theme name = themes |> List.find (fun t -> Theme.name t = name)
let private colour theme alias = Theme.tryColour alias theme |> Option.get
let private ok = function Ok value -> value | Error errors -> failtestf "%A" errors
let private errorsOf (result: Result<Symbology<'E>, SymbologyError list>) = match result with Ok _ -> [] | Error errors -> errors
let private differing width height (a: Scene) (b: Scene) =
    let other = ScenePixels.expectations width height b |> List.map (fun e -> e.Point, e.Colour) |> Map.ofList
    ScenePixels.expectations width height a
    |> List.filter (fun e -> match other.TryFind e.Point with Some c -> c <> e.Colour | None -> false)
    |> List.length

let private root () =
    let rec up (dir: System.IO.DirectoryInfo | null) =
        match dir with
        | null -> None
        | d when System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "FS.GG.Game.slnx")) -> Some d.FullName
        | d -> up d.Parent
    up (System.IO.DirectoryInfo(System.AppContext.BaseDirectory))
    |> Option.orElseWith (fun () -> up (System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory())))
    |> Option.defaultWith (fun () -> failwith "repo root not found")

[<Tests>]
let tests =
    testList "production theme derivation (#795)" [
            test "OKLCH agrees with published sRGB red and white reference values" {
                let white = ThemeColour.toOklch (rgb 255 255 255)
                Expect.floatClose { absolute = 1e-4; relative = 0.0 } white.L 1.0 "white"
                let red = ThemeColour.toOklch (rgb 255 0 0)
                Expect.floatClose { absolute = 5e-4; relative = 0.0 } red.L 0.62796 "red L"
                Expect.floatClose { absolute = 5e-4; relative = 0.0 } red.C 0.25768 "red C"
                Expect.floatClose { absolute = 0.05; relative = 0.0 } red.H 29.23 "red hue"
            }
            test "CIEDE2000 agrees with Sharma, Wu and Dalal test data" {
                for a, b, expected in [
                    (50.0,2.6772,-79.7751), (50.0,0.0,-82.7485), 2.0425
                    (50.0,3.1571,-77.2803), (50.0,0.0,-82.7485), 2.8615
                    (50.0,2.8361,-74.0200), (50.0,0.0,-82.7485), 3.4412
                    (50.0,0.0,0.0), (50.0,-1.0,2.0), 2.3669
                    (50.0,2.5,0.0), (73.0,25.0,-18.0), 27.1492
                    (60.2574,-34.0099,36.2677), (60.4626,-34.1751,39.4387), 1.2644 ] do
                    Expect.floatClose { absolute = 1e-4; relative = 0.0 } (ThemeVision.deltaE2000Lab a b) expected "reference pair"
                    Expect.floatClose { absolute = 1e-4; relative = 0.0 } (ThemeVision.deltaE2000Lab b a) expected "symmetric"
            }
            test "Machado simulation preserves poles and makes the red/green control less distinct" {
                for vision in ThemeVision.visions do
                    let r,g,b = ThemeVision.simulate vision (rgb 255 255 255)
                    Expect.floatClose { absolute = 1e-5; relative = 0.0 } (r+g+b) 3.0 "white"
                    Expect.equal (ThemeVision.simulate vision (rgb 0 0 0)) (0.0,0.0,0.0) "black"
                let red, green = rgb 200 60 60, rgb 90 140 60
                Expect.isGreaterThan (ThemeVision.deltaE2000 Vision.Normal red green) 40.0 "normal control"
                Expect.isLessThan (ThemeVision.deltaE2000 Vision.Deutan red green) 15.0 "deutan control"
            }
            test "every 8-bit colour round-trips through production OKLCH" {
                let failures = roundTripFailures ()
                printfn "production OKLCH round trip colours=16777216 failures=%d" failures
                Expect.equal failures 0 "the complete 8-bit domain"
            }
            test "the production solver meets contrast on quantised results, including the review ground" {
                for ground in [rgb 34 40 49; rgb 40 26 34] do
                    for target in [1.5;3.0;4.5;7.0;11.0] do
                        for hue in [0.0;90.0;200.0;300.0] do
                            match ThemeGrid.solve ground true target 0.012 hue with
                            | Some c -> Expect.isGreaterThanOrEqual (ThemeColour.contrast c ground) target "8-bit outcome"
                            | None -> failtestf "unreachable target %g" target
                Expect.equal (ThemeGrid.inputOf (rgb 128 128 128)).H 0.0 "no hue below chroma floor"
            }
            test "all five baked presets equal fresh recipes and carry the validation checks" {
                Expect.equal (themes |> List.map Theme.name) ["classic";"light";"highContrast";"colourBlindSafe";"monochrome"] "presets"
                for name, seed, algorithms in ThemePresets.recipes do
                    let fresh = Theme.create seed algorithms [] |> Result.bind (Theme.validate (ThemePresets.checks 2)) |> ok
                    let baked = theme name
                    Expect.equal (Theme.hex baked) (Theme.hex fresh) name
                    Expect.equal (Theme.cssVariables baked) (Theme.cssVariables fresh) "numbers too"
                    Expect.equal (Theme.checks baked) (ThemePresets.checks 2 |> List.map ThemeCheck.name) "checks retained"
                Expect.equal (themes |> List.map (ThemePresets.measuredCapacity >> fun c -> c.[1])) [5;5;5;3;0] "capacity at 24 units"
            }
            test "the declared capacities meet provisional normal and simulated thresholds" {
                let pairs xs = [for i,a in List.indexed xs do for b in List.skip (i+1) xs -> a,b]
                for t in themes |> List.filter ThemePresets.carriesColour do
                    let capacity = (ThemePresets.measuredCapacity t).[1]
                    let teams = [for i in 0..capacity-1 -> colour t (sprintf "glyph.team.%d" i)]
                    let allegiance = [Identity.Own;Identity.Ally;Identity.Neutral;Identity.Enemy;Identity.Unknown] |> List.map (fun i -> Theme.allegiance i t)
                    let status = ["status.good";"status.warn";"status.bad"] |> List.map (colour t)
                    for vision in ThemeVision.visions do
                        let threshold = if vision = Vision.Normal then 12.0 else 8.0
                        for a,b in pairs teams @ pairs allegiance @ pairs status do
                            Expect.isGreaterThanOrEqual (ThemeVision.deltaE2000 vision a b) threshold (sprintf "%s %A" (Theme.name t) vision)
                    Expect.isOk (Theme.validate (ThemePresets.checks capacity) t) "all kit checks at measured capacity"
            }
            test "capacity validation follows actual cell and density at each size step" {
                let _, seed, algorithms = ThemePresets.recipes |> List.find (fun (name,_,_) -> name = "highContrast")
                let compact = seed |> ThemeSeed.number "density" 0.75
                let result = Theme.create compact algorithms [] |> Result.bind (Theme.validate (ThemePresets.checks 2))
                match result with
                | Error errors -> Expect.exists errors (fun e -> e.Token = "hueCapacity.3") "18-unit top frame cannot declare five hues"
                | Ok _ -> failtest "compact highContrast falsely retained the 24-unit capacity"
                let corrected = compact |> ThemeSeed.number "hueCapacity.3" 4.0
                Expect.isOk (Theme.create corrected algorithms [] |> Result.bind (Theme.validate (ThemePresets.checks 2))) "corrected declaration"
            }
            test "exports round-trip every production preset and generated DTCG files are current" {
                for t in themes do
                    let text = Theme.toDtcg t
                    let back = Theme.ofDtcg ThemePresets.algorithms text |> ok
                    Expect.equal (Theme.hex back) (Theme.hex t) "resolved aliases"
                    Expect.equal (Theme.cssVariables back) (Theme.cssVariables t) "numbers"
                    let path = System.IO.Path.Combine(root (), "docs", "roadmaps", "game-render-02-themes", "themes", Theme.name t + ".tokens.json")
                    Expect.equal (System.IO.File.ReadAllText(path).Replace("\r\n","\n")) text "run the production bake if stale"
                let css = themes |> List.map (Theme.cssVariables >> List.map (fun (k,v) -> k+": "+v) >> String.concat ";") |> String.concat "\n"
                for t in themes do
                    for _,hex in Theme.hex t do Expect.stringContains css hex "every colour exported"
            }
            test "player overrides keep the production checks and refuse a bad or nonexistent team" {
                let classic = Theme.classic
                for index, c in [0,rgb 40 46 56; 1,colour classic "glyph.team.0"; 8,rgb 86 151 233] do
                    Expect.isError (Theme.withTeamColour index c classic) "ground, duplicate and outside roster controls"
                let changed = Theme.withTeamColour 0 (rgb 86 151 233) classic |> ok
                Expect.equal (colour changed "glyph.team.0") (rgb 86 151 233) "alias override"
                Expect.equal (Theme.checks changed) (Theme.checks classic) "checks retained"
                let greenBad = Theme.withOverrides [ThemeOverride.colour "status.bad" (colour classic "status.good")] classic
                Expect.isError greenBad "status meaning is checked again"
            }
            test "runtime switching: A -> B -> A gives the same pixels, the state is untouched, and a swap control differs" {
                let view = ArenaExample.view themes
                let state = ArenaExample.sample
                let mutable selectedTheme = theme "classic"
                let read () = selectedTheme
                let select t = selectedTheme <- t
                let w, h = ArenaExample.width, ArenaExample.height
                let decided scene = ScenePixels.expectations w h scene |> List.map (fun e -> e.Point, e.Colour)
                let first = decided (view read state)
                for other in themes |> List.tail do
                    select other
                    let shown = view read state
                    Expect.isGreaterThan (differing w h (view (fun () -> theme "classic") state) shown) 50 (sprintf "classic and %s differ" (Theme.name other))
                    select(theme "classic")
                    Expect.equal (decided (view read state)) first (sprintf "classic -> %s -> classic" (Theme.name other))
                Expect.equal state ArenaExample.sample "the game state is the same value"
                Expect.isGreaterThan first.Length 600 "the example decides pixels"
            }

            test "monochrome keeps identity and team apart in decided pixels" {
                let mono = theme "monochrome"
                let players = build themes ArenaExample.playerDraft |> function Ok s -> s | Error e -> failtestf "%A" e
                let target = mono
                let one (p: ArenaExample.Player) =
                    let spec = Symbology.spec players { X = 24.0; Y = 30.0 } p |> function Ok s -> s | Error e -> failtestf "%A" e
                    let drawn = FS.GG.Game.Render.Glyphs.scene target [ Symbology.glyph (teamColouring mono players) false spec ]
                    { Nodes = SceneNode.Rectangle((0.0, 0.0, 48.0, 64.0), colour mono "surface.ground") :: drawn.Nodes }
                let a = ArenaExample.sample.Players.[1]
                for b in [ { a with Team = 2 }; { a with Team = 3 }; { a with Allegiance = Identity.Own; Team = 0 } ] do
                    let n = min (differing 48.0 64.0 (one a) (one b)) (differing 48.0 64.0 (one b) (one a))
                    Expect.isGreaterThan n 0 (sprintf "team %d/%A against team %d/%A" a.Team a.Allegiance b.Team b.Allegiance)
            }

            test "the worked example builds; adding the role icon is refused with the icon-slot error" {
                Expect.isEmpty (errorsOf (build themes ArenaExample.playerDraft)) "players"
                Expect.isEmpty (errorsOf (build themes ArenaExample.featureDraft)) "features"
                match build themes ArenaExample.playerDraftWithRoleIcon with
                | Error [ IconSlotTakenByBadge("role", "team", 24.0, Identity.Enemy) as e ] -> printfn "%s" (explain e)
                | other -> failtestf "expected the icon-slot error, got %A" other
                let notes = match build themes ArenaExample.playerDraft with Ok s -> (Symbology.notes s) | Error _ -> []
                Expect.equal notes.Length 2 "team colour falls back in colourBlindSafe (capacity 3) and monochrome"
                Expect.equal
                    (ArenaExample.labels themes ArenaExample.sample |> List.head)
                    "own, team 1, health 90%, rank 3, carrying, Ada"
                    "the mirror label names identity and team"
            }

    ]
