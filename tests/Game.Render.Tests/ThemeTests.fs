module Game.Render.Tests.ThemeTests

// ADR GAME-RENDER-02.4 D4 and D11 (FS.GG.Game#789): theme tokens in three layers, validation, deterministic OKLCH
// derivation, CSS and DTCG export and import. The .NET/Fable/Windows byte check and the full 8-bit round trip are
// scripts/test-theme-determinism.sh.

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render

let private rgb r g b : Color =
    {
        Red = byte r
        Green = byte g
        Blue = byte b
        Alpha = 255uy
    }

let private ok (result: Result<Theme, ThemeViolation list>) =
    match result with
    | Ok theme -> theme
    | Error violations -> failtestf "refused: %s" (violations |> List.map string |> String.concat "; ")

let private refused (result: Result<Theme, ThemeViolation list>) =
    match result with
    | Ok _ -> failtest "accepted"
    | Error violations -> violations

let private derive seed = Theme.create seed [ ThemeAlgorithm.standard ] []

let private colourOf alias theme =
    Theme.tryColour alias theme |> Option.defaultWith (fun () -> failtestf "%s is not a colour alias" alias)

[<Tests>]
let tests =
    testList
        "Theme (FS.GG.Game#789, ADR D4/D11)"
        [
            test "quantising clamps, then takes Floor(v + 0.5), never Floor(v)" {
                Expect.equal (ThemeColour.quantise 0.0) 0uy "0"
                Expect.equal (ThemeColour.quantise 1.0) 255uy "1"
                Expect.equal (ThemeColour.quantise (0.5 / 255.0)) 1uy "a half rounds up"
                Expect.equal (ThemeColour.quantise ((127.0 - 1e-12) / 255.0)) 127uy "just below a whole value is that value, where Floor(v) would drop a step"
                Expect.equal (ThemeColour.quantise (127.49 / 255.0)) 127uy "below the half"
                Expect.equal (ThemeColour.quantise -3.0) 0uy "clamped below"
                Expect.equal (ThemeColour.quantise 7.0) 255uy "clamped above"
                Expect.equal (ThemeColour.quantise nan) 0uy "NaN"
            }

            test "the committed sRGB-to-linear table is the transfer function, and contrast reads it" {
                for v in 0..255 do
                    let c = float v / 255.0
                    let formula = if c <= 0.04045 then c / 12.92 else ((c + 0.055) / 1.055) ** 2.4
                    Expect.floatClose Accuracy.veryHigh (ThemeColour.linear (byte v)) formula (sprintf "entry %d" v)

                Expect.floatClose Accuracy.veryHigh (ThemeColour.contrast (rgb 255 255 255) (rgb 0 0 0)) 21.0 "white on black"
                Expect.equal (ThemeColour.contrast (rgb 34 40 49) (rgb 34 40 49)) 1.0 "a colour on itself"
                Expect.equal (ThemeColour.contrast (rgb 10 10 10) (rgb 200 0 0)) (ThemeColour.contrast (rgb 200 0 0) (rgb 10 10 10)) "symmetric"
            }

            test "8-bit colours survive the OKLCH round trip (a sample; the probe runs all 16.7 million)" {
                for r in 0..17..255 do
                    for g in 0..15..255 do
                        for b in 0..5..255 do
                            let c = rgb r g b
                            Expect.equal (ThemeColour.ofOklch (ThemeColour.toOklch c)) c (ThemeColour.hex c)
            }

            test "OKLCH: greys have no hue, and an out-of-gamut colour maps into sRGB at its lightness and hue" {
                let grey = ThemeColour.toOklch (rgb 128 128 128)
                Expect.equal grey.C 0.0 "grey chroma"
                Expect.equal grey.H 0.0 "grey hue"
                let wanted = { L = 0.7; C = 0.4; H = 150.0 }
                let shown = ThemeColour.ofOklch wanted
                let back = ThemeColour.toOklch shown
                Expect.isLessThan back.C 0.4 "chroma reduced"
                Expect.floatClose { absolute = 0.03; relative = 0.0 } back.L 0.7 "lightness kept within the JND"
                Expect.floatClose { absolute = 3.0; relative = 0.0 } back.H 150.0 "hue kept"
                Expect.equal (ThemeColour.ofOklch { L = 1.2; C = 0.1; H = 0.0 }) (rgb 255 255 255) "above white"
                Expect.equal (ThemeColour.ofOklch { L = -0.1; C = 0.1; H = 0.0 }) (rgb 0 0 0) "below black"
            }

            test "hex is #rrggbb, lower case, and reads either case" {
                Expect.equal (ThemeColour.hex (rgb 34 40 49)) "#222831" "hex"
                Expect.equal (ThemeColour.ofHex "#FFD600") (Some(rgb 255 214 0)) "upper case"
                Expect.equal (ThemeColour.ofHex "#ffd60") None "short"
                Expect.equal (ThemeColour.ofHex "ffd600") None "no hash"
                Expect.equal (ThemeColour.ofHex "#ffd60g") None "not hex"
            }

            test "classic is the default preset: baked, equal to a fresh derivation of its seed, and every alias of the closed set" {
                Expect.equal (Theme.presets |> List.map Theme.name) [ "classic" ] "presets"
                let fresh = ok (derive ThemeSeed.classic)
                Expect.equal (Theme.hex Theme.classic) (Theme.hex fresh) "baked equals fresh"
                Expect.equal (Theme.cssVariables Theme.classic) (Theme.cssVariables fresh) "numbers too"

                Expect.equal
                    (Theme.aliases Theme.classic)
                    ([
                        "surface.ground"
                        "glyph.unit.body"
                        "glyph.allegiance.own"
                        "glyph.allegiance.ally"
                        "glyph.allegiance.neutral"
                        "glyph.allegiance.enemy"
                        "glyph.allegiance.unknown"
                     ]
                     @ [ for i in 0..7 -> sprintf "glyph.team.%d" i ]
                     @ [
                         "glyph.badge.ink"
                         "glyph.icon.ink"
                         "glyph.modifier"
                         "glyph.selection"
                         "glyph.marker"
                         "glyph.path"
                         "glyph.stale"
                         "cue.focus.outer"
                         "cue.focus.inner"
                         "cue.hover"
                         "status.good"
                         "status.warn"
                         "status.bad"
                         "status.track"
                         "status.keyline"
                         "terrain.open"
                         "terrain.blocked"
                         "terrain.difficult"
                         "terrain.water"
                         "terrain.void"
                         "size.cell"
                         "stroke.outline"
                         "stroke.gap"
                         "stroke.bracket"
                         "stroke.hover"
                     ])
                    "the closed alias set"

                Expect.equal (Theme.teamCount Theme.classic) 8 "eight teams"
                Expect.equal (Theme.minCssPerUnit Theme.classic) 1.0 "minCssPerUnit defaults to 1"
                Expect.equal [ for s in -1..5 -> Theme.hueCapacity Theme.classic s ] [ 5; 5; 6; 7; 8; 8; 8 ] "hue capacity per size step, clamped"
                Expect.equal (Theme.algorithms Theme.classic) [ "standard" ] "algorithms"
                Expect.equal (Theme.allegiance Identity.Enemy Theme.classic) (colourOf "glyph.allegiance.enemy" Theme.classic) "allegiance"
            }

            test "classic meets every contrast rule against the ground and between colours that touch" {
                let t = Theme.classic
                let c alias = colourOf alias t
                let ground = c "surface.ground"
                let teams = [ for i in 0..7 -> sprintf "glyph.team.%d" i ]
                let allegiances = [ "own"; "ally"; "neutral"; "enemy"; "unknown" ] |> List.map ((+) "glyph.allegiance.")
                let atLeast floor a b = Expect.isGreaterThanOrEqual (ThemeColour.contrast (c a) (c b)) floor (sprintf "%s on %s" a b)

                for alias in teams @ allegiances @ [ "glyph.unit.body"; "glyph.stale"; "glyph.selection"; "glyph.marker"; "glyph.path"; "cue.focus.outer"; "cue.hover" ] do
                    Expect.isGreaterThanOrEqual (ThemeColour.contrast (c alias) ground) 3.0 (sprintf "%s on the ground" alias)

                atLeast 3.0 "cue.focus.inner" "cue.focus.outer"

                for on in "glyph.unit.body" :: teams @ allegiances do
                    atLeast 3.0 "glyph.badge.ink" on

                for on in "glyph.unit.body" :: allegiances do
                    atLeast 3.0 "glyph.icon.ink" on

                for team in teams do
                    atLeast 3.0 team "glyph.unit.body"

                for fill in [ "status.good"; "status.warn"; "status.bad" ] do
                    atLeast 3.0 fill "status.track"

                Expect.isGreaterThanOrEqual (ThemeColour.contrast (c "status.keyline") ground) 3.0 "the keyline on the ground"
                atLeast 3.0 "status.track" "status.keyline"

                for on in "glyph.unit.body" :: teams @ allegiances do
                    atLeast 3.0 "glyph.modifier" on
            }

            test "classic's provisional team colours are distinct from every allegiance colour and from each other (OKLab distance)" {
                let lab (c: Color) =
                    let p = ThemeColour.toOklch c
                    let h = p.H * System.Math.PI / 180.0
                    p.L, p.C * cos h, p.C * sin h

                let distance a b =
                    let (l1, a1, b1), (l2, a2, b2) = lab a, lab b
                    sqrt ((l1 - l2) ** 2.0 + (a1 - a2) ** 2.0 + (b1 - b2) ** 2.0)

                let teams = [ for i in 0..7 -> colourOf (sprintf "glyph.team.%d" i) Theme.classic ]
                let allegiances = [ Identity.Own; Identity.Ally; Identity.Neutral; Identity.Enemy; Identity.Unknown ] |> List.map (fun i -> Theme.allegiance i Theme.classic)

                for i, team in List.indexed teams do
                    for a in allegiances do
                        Expect.isGreaterThanOrEqual (distance team a) 0.065 (sprintf "team %d against allegiance %s" i (ThemeColour.hex a))

                    for j, other in List.indexed teams do
                        if j > i then
                            Expect.isGreaterThanOrEqual (distance team other) 0.09 (sprintf "team %d against team %d" i j)
            }

            test "a theme that breaks a rule cannot be built, and every violation is named" {
                let tokens violations = violations |> List.map (fun (v: ThemeViolation) -> v.Token) |> List.sort

                let pale = ThemeSeed.classic |> ThemeSeed.colour "team.1" (rgb 60 66 75)
                Expect.contains (tokens (refused (derive pale))) "glyph.team.1" "a team too close to the ground"

                let light = ThemeSeed.classic |> ThemeSeed.colour "team.2" (rgb 250 240 120)
                Expect.contains (tokens (refused (derive light))) "glyph.team.2" "a team too light for the body it sits on"

                let translucent = ThemeSeed.classic |> ThemeSeed.colour "accent.selection" { rgb 255 214 0 with Alpha = 128uy }
                Expect.contains (tokens (refused (derive translucent))) "glyph.selection" "alpha is refused (Q5)"

                let unknown = ThemeSeed.classic |> ThemeSeed.colour "accent.glow" (rgb 255 0 0)
                Expect.contains (tokens (refused (derive unknown))) "accent.glow" "not a seed name"

                let swapped = ThemeSeed.classic |> ThemeSeed.number "ground" 1.0
                Expect.contains (tokens (refused (derive swapped))) "ground" "a colour name set to a number"

                let noGround = ThemeSeed.classic |> ThemeSeed.without "ground"
                Expect.contains (tokens (refused (derive noGround))) "ground" "ground is required"

                let gap = ThemeSeed.classic |> ThemeSeed.without "team.3"
                Expect.contains (tokens (refused (derive gap))) "team.4" "a team after a missing one"

                let narrow = ThemeSeed.classic |> ThemeSeed.number "minCssPerUnit" 0.25
                Expect.contains (tokens (refused (derive narrow))) "stroke.gap" "the gap must be at least 1 / minCssPerUnit"

                let capacity = ThemeSeed.classic |> ThemeSeed.number "hueCapacity.3" 9.0
                Expect.contains (tokens (refused (derive capacity))) "hueCapacity.3" "at most 8 hues"

                let falling = ThemeSeed.classic |> ThemeSeed.number "hueCapacity.1" 4.0
                Expect.contains (tokens (refused (derive falling))) "hueCapacity" "capacity does not fall with size"

                let soft = ThemeSeed.classic |> ThemeSeed.number "contrastTarget" 2.0
                Expect.contains (tokens (refused (derive soft))) "contrastTarget" "the floor is at least 3:1"

                let high = ThemeSeed.classic |> ThemeSeed.number "contrastTarget" 7.0
                Expect.isNonEmpty (refused (derive high)) "classic's hues do not reach 7:1"

                let nowhere = Theme.create ThemeSeed.classic [ ThemeAlgorithm.standard ] [ ThemeOverride.colour "glyph.glow" (rgb 1 2 3) ]
                Expect.contains (tokens (refused nowhere)) "glyph.glow" "an override of no token"

                let noAlgorithm = Theme.create ThemeSeed.classic [] []
                Expect.contains (tokens (refused noAlgorithm)) "glyph.unit.body" "no algorithm sets the map tokens the aliases read"
            }

            test "algorithms run in order over the seed, and overrides apply after them, to a map token or an alias" {
                let darker =
                    ThemeAlgorithm.create "darker-ground" (fun _ tokens ->
                        tokens |> ThemeTokens.withColour "ground" (rgb 20 22 26))

                let theme = ok (Theme.create ThemeSeed.classic [ ThemeAlgorithm.standard; darker ] [])
                Expect.equal (colourOf "surface.ground" theme) (rgb 20 22 26) "the later algorithm refines the map"
                Expect.equal (Theme.algorithms theme) [ "standard"; "darker-ground" ] "in order"

                let mapped = ok (Theme.create ThemeSeed.classic [ ThemeAlgorithm.standard ] [ ThemeOverride.colour "selection" (rgb 255 160 0) ])
                Expect.equal (colourOf "glyph.selection" mapped) (rgb 255 160 0) "a map-token override reaches its alias"

                let aliased = ok (Theme.create ThemeSeed.classic [ ThemeAlgorithm.standard ] [ ThemeOverride.number "stroke.gap" 3.0 ])
                Expect.equal (Theme.tryNumber "stroke.gap" aliased) (Some 3.0) "an alias override"
                Expect.equal (Theme.tryNumber "size.cell" (ok (derive (ThemeSeed.classic |> ThemeSeed.number "density" 0.75)))) (Some 24.0) "cell times density"
            }

            test "a player's team-colour override is validated again: one that passes applies, one that fails is refused with its reason" {
                let teal = rgb 0 150 136
                let theme = ok (Theme.withOverrides [ ThemeOverride.colour "glyph.team.0" teal ] Theme.classic)
                Expect.equal (colourOf "glyph.team.0" theme) teal "applied"
                Expect.equal (colourOf "glyph.team.1" theme) (colourOf "glyph.team.1" Theme.classic) "the rest unchanged"

                let violations = refused (Theme.withOverrides [ ThemeOverride.colour "glyph.team.0" (rgb 40 44 52) ] Theme.classic)
                Expect.equal (violations |> List.map (fun v -> v.Token) |> List.sort) [ "glyph.badge.ink"; "glyph.modifier"; "glyph.team.0" ] "refused: the team on the ground, and the badge and modifier inks on the team"
                let onGround = violations |> List.find (fun v -> v.Token = "glyph.team.0")
                Expect.stringContains onGround.Reason "surface.ground" "too close to the ground"
                let badge = violations |> List.find (fun v -> v.Token = "glyph.badge.ink")
                Expect.stringContains badge.Reason "glyph.team.0" "and the badge ink no longer reads on it"
            }

            test "added checks join validation: Theme.validate runs them after the built-in rules, the theme carries them, and withOverrides runs them again" {
                // A stand-in for FS.GG.Game#795's distinctness check: every pair of team colours at least this far apart in OKLab.
                let distinct (minimum: float) =
                    ThemeCheck.create (sprintf "teams-distinct-%g" minimum) (fun theme ->
                        let teams = [ for i in 0 .. Theme.teamCount theme - 1 -> sprintf "glyph.team.%d" i, colourOf (sprintf "glyph.team.%d" i) theme ]
                        let lab (c: Color) =
                            let p = ThemeColour.toOklch c
                            let h = p.H * System.Math.PI / 180.0
                            p.L, p.C * cos h, p.C * sin h
                        [
                            for i, (a, ca) in List.indexed teams do
                                for b, cb in teams |> List.skip (i + 1) do
                                    let l1, a1, b1 = lab ca
                                    let l2, a2, b2 = lab cb
                                    let d = sqrt ((l1 - l2) ** 2.0 + (a1 - a2) ** 2.0 + (b1 - b2) ** 2.0)
                                    if d < minimum then
                                        yield ThemeViolation.create b (sprintf "%.3f from %s, below %g" d a minimum)
                        ])

                let checkedTheme = ok (Theme.validate [ distinct 0.05 ] Theme.classic)
                Expect.equal (Theme.checks checkedTheme) [ "teams-distinct-0.05" ] "the theme carries its check"
                Expect.equal (Theme.hex checkedTheme) (Theme.hex Theme.classic) "and is the same theme"
                Expect.isNonEmpty (refused (Theme.validate [ distinct 0.9 ] Theme.classic)) "a check that fails refuses the theme"

                // A player override that passes the built-in rules but breaks the carried check is refused.
                let clash = Theme.withOverrides [ ThemeOverride.colour "glyph.team.1" (colourOf "glyph.team.0" Theme.classic) ] checkedTheme
                Expect.equal ((refused clash) |> List.map (fun v -> v.Token)) [ "glyph.team.1" ] "the carried check runs on an override"
                Expect.isOk (Theme.withOverrides [ ThemeOverride.colour "glyph.team.1" (colourOf "glyph.team.0" Theme.classic) ] Theme.classic) "without the check the override passes"

                let throws = ThemeCheck.create "broken" (fun _ -> failwith "boom")
                Expect.equal ((refused (Theme.validate [ throws ] Theme.classic)) |> List.map (fun v -> v.Token)) [ "broken" ] "a check that throws is a violation"
            }

            test "CSS variables and hex give the Scene's colours" {
                let css = Theme.cssVariables Theme.classic
                Expect.equal (css |> List.find (fun (k, _) -> k = "--fsgg-glyph-team-0") |> snd) (ThemeColour.hex (colourOf "glyph.team.0" Theme.classic)) "a colour"
                Expect.equal (css |> List.find (fun (k, _) -> k = "--fsgg-size-cell") |> snd) "32" "a size"
                Expect.equal (Theme.hex Theme.classic |> List.length) 35 "every colour alias"
            }

            test "DTCG: export then import gives equal resolved hex for every alias; hex wins over components; no alias group derives again" {
                let text = Theme.toDtcg Theme.classic
                Expect.stringContains text "\"colorSpace\": \"oklch\"" "oklch colours"
                Expect.stringContains text "\"hex\": \"#222831\"" "with hex"
                Expect.stringContains text "\"gg.fs.theme\"" "the extension"
                let back = ok (Theme.ofDtcg [ ThemeAlgorithm.standard ] text)
                Expect.equal (Theme.hex back) (Theme.hex Theme.classic) "lossless"
                Expect.equal (Theme.cssVariables back) (Theme.cssVariables Theme.classic) "numbers too"
                Expect.equal (Theme.name back) "classic" "the name"

                // Hex wins: the components of the ground say something else, the hex is kept.
                let firstGround = text.IndexOf "\"ground\""
                let components = text.IndexOf("\"components\": [", firstGround)
                let close = text.IndexOf(']', components)
                let edited = text.Substring(0, components) + "\"components\": [0.9, 0, 0" + text.Substring close
                Expect.equal (Theme.hex (ok (Theme.ofDtcg [ ThemeAlgorithm.standard ] edited))) (Theme.hex Theme.classic) "hex preferred"

                // A designer edits the seed and deletes the alias group: every alias is derived again.
                let seedOnly = text.Substring(0, text.IndexOf ",\n  \"alias\"") + "\n}\n"
                let reseeded = seedOnly.Replace("\"hex\": \"#db2478\"", "\"hex\": \"#2f6fd0\"")
                let derived = ok (Theme.ofDtcg [ ThemeAlgorithm.standard ] reseeded)
                Expect.equal (colourOf "glyph.team.0" derived) (rgb 0x2f 0x6f 0xd0) "the edited seed reaches the alias"

                Expect.isNonEmpty (refused (Theme.ofDtcg [] text)) "an algorithm not given is refused"
                Expect.isNonEmpty (refused (Theme.ofDtcg [ ThemeAlgorithm.standard ] "{ \"seed\": ")) "malformed JSON is refused"
            }

            test "the DTCG resolver document: version 2025-10-01, a set for one theme, a theme modifier for two or more, and the density modifier" {
                let one = Theme.dtcgResolver Theme.presets
                Expect.stringContains one "\"version\": \"2025-10-01\"" "the version is a date"
                Expect.stringContains one "\"sets\"" "one theme is a set"
                Expect.isFalse (one.Contains "\"theme\":") "no single-context theme modifier"
                Expect.stringContains one "#/sets/classic" "resolved first"
                Expect.stringContains one "\"density\"" "the density modifier"
                let other = ok (derive (ThemeSeed.classic |> ThemeSeed.named "other"))
                let two = Theme.dtcgResolver [ Theme.classic; other ]
                Expect.stringContains two "\"theme\":" "two themes are a modifier"
                Expect.stringContains two "other.tokens.json" "with a context per file"
                Expect.stringContains two "#/modifiers/theme" "resolved first"
            }

            test "DTCG import: references, group-level $type, the compact density re-derives sizes, and number overrides travel" {
                let text = Theme.toDtcg Theme.classic
                // A whole-value reference: alias glyph.team.0 refers to the seed's team.1.
                let teamZero = text.IndexOf "\"team\": {\n      \"0\""
                let aliasStart = text.IndexOf "\"alias\""
                let aliasTeam = text.IndexOf("\"team\": {", aliasStart)
                let open0 = text.IndexOf("\"0\": {", aliasTeam)
                let close0 = text.IndexOf("\n        }", open0)
                let referenced = text.Substring(0, open0) + "\"0\": { \"$type\": \"color\", \"$value\": \"{seed.team.1}\" }" + text.Substring(close0 + "\n        }".Length)
                ignore teamZero
                let back = ok (Theme.ofDtcg [ ThemeAlgorithm.standard ] referenced)
                Expect.equal (colourOf "glyph.team.0" back) (colourOf "glyph.team.1" Theme.classic) "the reference is followed"

                let missing = referenced.Replace("{seed.team.1}", "{seed.team.9}")
                Expect.exists (refused (Theme.ofDtcg [ ThemeAlgorithm.standard ] missing)) (fun v -> v.Reason.Contains "names no token") "a missing target"

                // Group-level $type: the seed's colour tokens drop their own $type, and the group declares it.
                let cut = text.IndexOf "\"alias\""
                let seedPart, aliasPart = text.Substring(0, cut), text.Substring cut
                let grouped =
                    seedPart.Replace("\"seed\": {", "\"seed\": {\n    \"$type\": \"color\",")
                        .Replace("{\n        \"$type\": \"color\",", "{")
                    + aliasPart
                let own (t: string) = (t.Length - t.Replace("\"$type\": \"color\"", "").Length) / "\"$type\": \"color\"".Length
                // Ground and ink sit directly under the seed group and keep theirs; the 18 one level deeper lose theirs.
                Expect.equal (own seedPart - own (grouped.Substring(0, grouped.IndexOf "\"alias\""))) 17 "18 token types dropped, one group type added"
                Expect.equal (Theme.hex (ok (Theme.ofDtcg [ ThemeAlgorithm.standard ] grouped))) (Theme.hex Theme.classic) "types inherited from the group"

                // The resolver's compact context sets the seed's density; the sizes follow it.
                let compact = text.Replace("\"density\": {\n      \"$type\": \"number\",\n      \"$value\": 1\n", "\"density\": {\n      \"$type\": \"number\",\n      \"$value\": 0.75\n")
                Expect.notEqual compact text "the density was edited"
                Expect.equal (Theme.tryNumber "size.cell" (ok (Theme.ofDtcg [ ThemeAlgorithm.standard ] compact))) (Some 24.0) "sizes derived again from the seed"

                // A size the theme overrode travels in the extension.
                let gapped = ok (Theme.withOverrides [ ThemeOverride.number "stroke.gap" 3.0 ] Theme.classic)
                let gappedBack = ok (Theme.ofDtcg [ ThemeAlgorithm.standard ] (Theme.toDtcg gapped))
                Expect.equal (Theme.tryNumber "stroke.gap" gappedBack) (Some 3.0) "the number override survives"
                Expect.equal (Theme.cssVariables gappedBack) (Theme.cssVariables gapped) "and everything else"
            }

            test "players change only a team colour, and nothing lowers a floor" {
                let teal = rgb 0 150 136
                Expect.equal (colourOf "glyph.team.2" (ok (Theme.withTeamColour 2 teal Theme.classic))) teal "an existing team"
                Expect.exists (refused (Theme.withTeamColour 8 teal Theme.classic)) (fun v -> v.Token = "glyph.team.8") "no ninth team"
                Expect.exists (refused (Theme.withTeamColour 0 (rgb 40 44 52) Theme.classic)) (fun v -> v.Token = "glyph.team.0") "a failing colour is refused"

                // The reviewer's case: a stricter floor in the seed cannot be lowered by an override.
                let strictSeed =
                    [ 2..7 ]
                    |> List.fold (fun seed i -> ThemeSeed.without (sprintf "team.%d" i) seed) ThemeSeed.classic
                    |> ThemeSeed.colour "team.0" (rgb 0x40 0x8c 0xff)
                    |> ThemeSeed.colour "team.1" (rgb 0xeb 0x50 0x46)
                    |> ThemeSeed.named "strict"
                    |> ThemeSeed.number "contrastTarget" 4.0
                let strict = ok (derive strictSeed)
                let weak = rgb 150 60 60
                Expect.isError (Theme.withTeamColour 0 weak strict) "below the strict floor"
                let lowered = refused (Theme.withOverrides [ ThemeOverride.number "contrastTarget" 3.0; ThemeOverride.colour "glyph.team.0" weak ] strict)
                Expect.exists lowered (fun v -> v.Token = "contrastTarget") "the floor is not an override"

                for floor in [ "minCssPerUnit"; "hueCapacity.0" ] do
                    Expect.exists (refused (Theme.withOverrides [ ThemeOverride.number floor 4.0 ] Theme.classic)) (fun v -> v.Token = floor) floor
            }
        ]
