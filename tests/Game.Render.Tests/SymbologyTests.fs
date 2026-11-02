module Game.Render.Tests.SymbologyTests

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Render.Symbology

let private themes = Theme.presets
let private theme name = themes |> List.find (fun t -> Theme.name t = name)
let private hueCapacity theme frame =
    (ThemePresets.measuredCapacity theme).[if frame < 24.0 then 0 elif frame < 40.0 then 1 else 2]

let private errorsOf (result: Result<Symbology<'E>, SymbologyError list>) =
    match result with
    | Ok _ -> []
    | Error errors -> errors

let private refuses (draft: Draft<'E>) (expected: SymbologyError -> bool) (what: string) =
    let errors = errorsOf (build themes draft)
    Expect.isTrue (errors |> List.exists expected) (sprintf "%s: expected the error, got %A" what errors)
    Expect.equal errors.Length (List.distinct errors).Length "errors are de-duplicated, including non-reflexive NaN inputs"
    for error in errors do
        Expect.isNotEmpty (explain error) "every error explains itself"

let private accepts (draft: Draft<'E>) (what: string) =
    Expect.isEmpty (errorsOf (build themes draft)) what

type private Probe = { Kind: int; Level: int; Share: float; On: bool; Who: Identity; Side: int; Name: string }

let private probe = { Kind = 0; Level = 0; Share = 0.5; On = false; Who = Identity.Own; Side = 0; Name = "p" }
let private base24 = Symbology.create<Probe> "unit" 24.0 |> Encode.identity "allegiance" (fun p -> p.Who) Frame


let private playerDraft =
    base24
    |> Encode.team "side" (fun p -> p.Side) [ Identity.Own; Identity.Enemy; Identity.Enemy; Identity.Enemy ] TeamMark
    |> Encode.label "name" (fun p -> p.Name)

[<Tests>]
let tests =
    testList "production symbology (#795)" [
            test "empty theme sets, invalid frames, unsupported families and empty rosters are refused" {
                Expect.contains (errorsOf (build [] base24)) NoThemes "no vacuous theme validation"
                for frame in [0.0; -1.0; nan; infinity; -infinity] do
                    refuses (Symbology.create<Probe> "unit" frame |> Encode.label "name" (fun p -> p.Name)) (function InvalidFrame _ -> true | _ -> false) "invalid frame"
                refuses (Symbology.create<Probe> "terrain" 24.0) (function UnsupportedFamily "terrain" -> true | _ -> false) "no silent unit substitution"
                refuses (base24 |> Encode.team "side" (fun p -> p.Side) [] TeamMark) (function EmptyDomain "side" -> true | _ -> false) "empty roster"
            }
            test "themes are checked for identity and status rules without a team encoding" {
                let own = Theme.allegiance Identity.Own Theme.classic
                let invalid = Theme.create ThemeSeed.classic [ThemeAlgorithm.standard] [ThemeOverride.colour "glyph.allegiance.enemy" own] |> Result.defaultWith (fun e -> failtestf "%A" e)
                Expect.isOk (build [Theme.classic] base24) "valid theme positive control"
                Expect.isTrue (errorsOf (build [invalid] base24) |> List.exists (function ThemeRefuses _ -> true | _ -> false)) "unchecked non-team categories are refused"
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
                refuses (base24 |> Encode.nominal "kind" (fun p -> p.Kind) [ 0..5 ] Icon) (function OverCapacity("kind", Icon, 6, 5, None) -> true | _ -> false) "six icons: the library set has five"
                refuses
                    (base24 |> Encode.team "side" (fun p -> p.Side) (List.replicate 10 Identity.Enemy) TeamMark)
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
                let shared = [ Identity.Own; Identity.Enemy; Identity.Enemy ]
                let teamed size = Symbology.create<Probe> "unit" size |> Encode.identity "who" (fun p -> p.Who) Frame |> Encode.team "side" (fun p -> p.Side) shared TeamMark
                refuses (teamed 16.0) (function TeamBadgeBelowMinimumWithoutLabel("side", 16.0, Identity.Enemy) -> true | _ -> false) "16 units, shared"
                accepts (teamed 16.0 |> Encode.label "name" (fun p -> p.Name)) "16 units with a label"
                accepts (teamed 22.0) "22 units"
            }

            test "the icon slot is unavailable while a mandatory badge occupies a 22-39 unit frame (Root's #794 merge comment)" {
                let roster shared = if shared then [ Identity.Own; Identity.Enemy; Identity.Enemy ] else [ Identity.Own; Identity.Enemy; Identity.Neutral ]
                let draft size shared =
                    Symbology.create<Probe> "unit" size
                    |> Encode.identity "who" (fun p -> p.Who) Frame
                    |> Encode.team "side" (fun p -> p.Side) (roster shared) TeamMark
                    |> Encode.nominal "kind" (fun p -> p.Kind) [ 0; 1; 2 ] Icon
                for size in [ 22.0; 24.0; 32.0; 39.0 ] do
                    refuses (draft size true) (function IconSlotTakenByBadge("kind", "side", s, Identity.Enemy) -> s = size | _ -> false) (sprintf "%g units" size)
                accepts (draft 40.0 true) "40 units: side by side"
                accepts (draft 24.0 false) "no shared allegiance: no badge"
                // A size step that reaches into the band counts too: 18, 21 and 24 units.
                refuses
                    (Symbology.create<Probe> "unit" 24.0
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

            test "B2: a team without identity on the frame is refused, and a roster that disagrees with an entity is a typed error" {
                // The #805 review's two drafts.
                let teamOnly = Symbology.create<Probe> "unit" 24.0 |> Encode.team "side" (fun x -> x.Side) [ Identity.Own; Identity.Enemy ] TeamMark
                refuses teamOnly (function TeamWithoutIdentity "side" -> true | _ -> false) "team only"
                let lie =
                    Symbology.create<Probe> "unit" 24.0
                    |> Encode.identity "who" (fun x -> x.Who) Frame
                    |> Encode.team "side" (fun x -> x.Side) [ Identity.Own; Identity.Enemy; Identity.Ally ] TeamMark
                match build themes lie with
                | Ok s ->
                    match Symbology.spec s { X = 24.0; Y = 24.0 } { probe with Who = Identity.Enemy; Side = 2 } with
                    | Error [ RosterMismatch("side", 2, Identity.Ally, Identity.Enemy) as e ] -> Expect.stringContains (explain e) "roster" "explained"
                    | other -> failtestf "an Enemy on the Ally team was drawn: %A" other
                    Expect.isOk (Symbology.spec s { X = 24.0; Y = 24.0 } { probe with Who = Identity.Enemy; Side = 1 }) "a matching entity"
                | Error e -> failtestf "the roster itself is valid: %A" e
            }

            test "N7: an out-of-range value is a typed error, teams never wrap, FillStep is drawn, the badge is decided per size step" {
                let nominal = base24 |> Encode.nominal "kind" (fun x -> x.Kind) [ 0; 1; 2 ] Icon
                match build themes nominal with
                | Ok s ->
                    match Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Kind = 7 } with
                    | Error [ ValueOutOfRange("kind", _, _) ] -> ()
                    | other -> failtestf "case 7 of 3 drew: %A" other
                | Error e -> failtestf "%A" e
                let nine = base24 |> Encode.team "side" (fun x -> x.Side) (List.replicate 9 Identity.Own) TeamMark
                match build themes nine with
                | Ok s ->
                    match Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Side = 9 } with
                    | Error [ ValueOutOfRange("side", "team 9", _) ] -> ()
                    | other -> failtestf "team 9 of 9 drew (a wrap): %A" other
                    match Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Side = 8 } with
                    | Ok encoded ->
                        let zero = Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Side = 0 } |> Result.defaultWith (fun e -> failtestf "%A" e)
                        let body e = Symbology.glyph false false e |> Glyphs.draw Theme.classic |> GlyphDrawing.body
                        Expect.notEqual (body encoded) (body zero) "digit 9 differs from digit 1 with allegiance colouring fixed"
                        Expect.equal (Symbology.label s { probe with Side = 8 } |> Result.defaultWith (fun e -> failtestf "%A" e)) "own, team 9" "one-based mirror label, never wrapped"
                    | Error e -> failtestf "%A" e
                | Error e -> failtestf "%A" e
                let fraction = base24 |> Encode.fraction "share" (fun x -> x.Share) (Amplifier Below)
                match build themes fraction with
                | Ok s -> Expect.isError (Symbology.spec s { X = 0.0; Y = 0.0 } { probe with Share = 1.5 }) "a fraction of 1.5"
                | Error e -> failtestf "%A" e
                accepts (base24 |> Encode.ordinal "level" (fun x -> x.Level) [ 0; 1; 2 ] FillStep) "FillStep: the library draws it"
                refuses (base24 |> Encode.flag "on" (fun x -> x.On) (Amplifier Corner)) (function UnsupportedSlot("on", Amplifier Corner) -> true | _ -> false) "a corner flag"
                let stepped =
                    Symbology.create<Probe> "unit" 24.0
                    |> Encode.identity "who" (fun x -> x.Who) Frame
                    |> Encode.label "name" (fun x -> x.Name)
                    |> Encode.team "side" (fun x -> x.Side) [ Identity.Own; Identity.Enemy; Identity.Enemy ] TeamMark
                    |> Encode.ordinal "level" (fun x -> x.Level) [ 0; 1; 2 ] SizeStep
                match build themes stepped with
                | Ok s ->
                    let target = theme "classic"
                    let at level =
                        let e = Symbology.spec s { X = 24.0; Y = 24.0 } { probe with Who = Identity.Own; Side = 0; Level = level } |> function Ok g -> g | Error e -> failtestf "%A" e
                        let drawing = FS.GG.Game.Render.Glyphs.draw target (Symbology.glyph true false e)
                        (GlyphDrawing.frame drawing).Width, FS.GG.Game.Render.GlyphDrawing.diagnostics drawing |> List.exists (fun d -> d.Contains "badge")
                    Expect.equal (at 0) (18.0, true) "18 units: the library omits the badge and says so; the label carries team"
                    Expect.equal (at 1) (21.0, true) "21 units: still under the 22-unit minimum"
                    Expect.equal (at 2) (24.0, false) "24 units: the badge is drawn"
                | Error e -> failtestf "18 to 24 with a label and no icon: %A" e
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
                let players = build themes playerDraft |> function Ok s -> s | Error e -> failtestf "%A" e
                Expect.equal
                    (themes |> List.map (fun t -> Theme.name t, teamColouring t players))
                    [ "classic", true; "light", true; "highContrast", true; "colourBlindSafe", false; "monochrome", false ]
                    "four teams keep team colours where the capacity holds them"
            }

            test "N10: WrongSlot names the allowed slots" {
                let errors = errorsOf (build themes (base24 |> Encode.fraction "share" (fun x -> x.Share) Icon))
                match errors with
                | [ WrongSlot("share", "fraction", Icon, [ Amplifier Below ]) as e ] -> Expect.stringContains (explain e) "Slot.Amplifier Below" "names the slot to use"
                | other -> failtestf "%A" other
            }


            test "declared frame size controls rendered geometry at the icon and badge boundary" {
                for frame in [ 24.0; 40.0 ] do
                    let draft = Symbology.create<Probe> "unit" frame |> Encode.identity "who" (fun p -> p.Who) Frame
                    let built = build themes draft |> Result.defaultWith (fun e -> failtestf "%A" e)
                    let encoded = spec built { X = 50.0; Y = 50.0 } probe |> Result.defaultWith (fun e -> failtestf "%A" e)
                    let drawing = glyph false false encoded |> Glyphs.draw Theme.classic
                    Expect.equal (GlyphDrawing.frame drawing).Width frame "the checked frame is the drawn frame"
            }

            test "facing capability is reported and nonfinite angles are refused" {
                let draft = base24 |> Symbology.stroked |> Encode.angle "facing" (fun p -> p.Share) (Amplifier Facing)
                let built = build themes draft |> Result.defaultWith (fun e -> failtestf "%A" e)
                let encoded = spec built { X = 0.0; Y = 0.0 } probe |> Result.defaultWith (fun e -> failtestf "%A" e)
                let drawing = glyph false false encoded |> Glyphs.draw Theme.classic
                Expect.exists (GlyphDrawing.diagnostics drawing) (fun text -> text.Contains "facing needs the stroked profile") "the portable renderer reports its capability rather than silently dropping the angle"
                for bad in [ nan; infinity; -infinity ] do
                    match spec built { X = 0.0; Y = 0.0 } { probe with Share = bad } with
                    | Error [ ValueOutOfRange("facing", _, _) ] -> ()
                    | other -> failtestf "nonfinite angle accepted: %A" other
            }
    ]
