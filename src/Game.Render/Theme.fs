namespace FS.GG.Game.Render

open FS.GG.UI.Scene

[<RequireQualifiedAccess>]
type Identity =
    | Own
    | Ally
    | Neutral
    | Enemy
    | Unknown

type Oklch = { L: float; C: float; H: float }

[<RequireQualifiedAccess>]
module ThemeColour =

    /// sRGB (8-bit) to linear light, computed once by `((c + 0.055) / 1.055) ** 2.4` (`c / 12.92` at or below 0.04045) and
    /// committed as data, so validation never calls a power function (ADR D11): a ratio near 3:1 cannot pass on one
    /// runtime and fail on the other.
    let private table: float[] =
        [|
            0.0; 0.0003035269835488375; 0.000607053967097675; 0.0009105809506465125
            0.00121410793419535; 0.0015176349177441874; 0.001821161901293025; 0.0021246888848418626
            0.0024282158683907; 0.0027317428519395373; 0.003035269835488375; 0.003346535763899161
            0.003676507324047436; 0.004024717018496307; 0.004391442037410293; 0.004776953480693729
            0.005181516702338386; 0.005605391624202723; 0.006048833022857054; 0.006512090792594475
            0.006995410187265387; 0.007499032043226175; 0.008023192985384994; 0.008568125618069307
            0.009134058702220787; 0.00972121732023785; 0.010329823029626936; 0.010960094006488246
            0.011612245179743885; 0.012286488356915872; 0.012983032342173012; 0.013702083047289686
            0.014443843596092545; 0.01520851442291271; 0.01599629336550963; 0.016807375752887384
            0.017641954488384078; 0.018500220128379697; 0.019382360956935723; 0.0202885630566524
            0.021219010376003555; 0.02217388479338738; 0.02315336617811041; 0.024157632448504756
            0.02518685962736163; 0.026241221894849898; 0.027320891639074894; 0.028426039504420793
            0.0295568344378088; 0.030713443732993635; 0.03189603307301153; 0.033104766570885055
            0.03433980680868217; 0.03560131487502034; 0.03688945040110004; 0.0382043715953465
            0.03954623527673284; 0.04091519690685319; 0.042311410620809675; 0.043735029256973465
            0.04518620438567554; 0.046665086336880095; 0.04817182422688942; 0.04970656598412723
            0.05126945837404324; 0.052860647023180246; 0.05448027644244237; 0.05612849004960009
            0.05780543019106723; 0.0595112381629812; 0.06124605423161761; 0.06301001765316767
            0.06480326669290577; 0.06662593864377289; 0.06847816984440017; 0.07036009569659588
            0.07227185068231748; 0.07421356838014963; 0.07618538148130785; 0.07818742180518633
            0.08021982031446832; 0.0822827071298148; 0.08437621154414882; 0.08650046203654976
            0.08865558628577294; 0.09084171118340768; 0.09305896284668745; 0.0953074666309647
            0.09758734714186246; 0.09989872824711389; 0.10224173308810132; 0.10461648409110419
            0.10702310297826761; 0.10946171077829933; 0.1119324278369056; 0.11443537382697373
            0.11697066775851084; 0.11953842798834562; 0.12213877222960187; 0.12477181756095049
            0.12743768043564743; 0.1301364766903643; 0.13286832155381798; 0.13563332965520566
            0.13843161503245183; 0.14126329114027164; 0.14412847085805777; 0.14702726649759498
            0.14995978981060856; 0.15292615199615017; 0.1559264637078274; 0.1589608350608804
            0.162029375639111; 0.1651321945016676; 0.16826940018969075; 0.1714411007328226
            0.17464740365558504; 0.17788841598362912; 0.18116424424986022; 0.184474994500441
            0.18782077230067787; 0.19120168274079138; 0.1946178304415758; 0.19806931955994886
            0.20155625379439707; 0.20507873639031693; 0.20863687014525575; 0.21223075741405523
            0.21586050011389926; 0.2195261997292692; 0.2232279573168085; 0.22696587351009836
            0.23074004852434915; 0.23455058216100522; 0.238397573812271; 0.24228112246555486
            0.24620132670783548; 0.25015828472995344; 0.25415209433082675; 0.2581828529215958
            0.26225065752969623; 0.26635560480286247; 0.2704977910130658; 0.27467731206038465
            0.2788942634768104; 0.2831487404299921; 0.2874408377269175; 0.29177064981753587
            0.2961382707983211; 0.3005437944157765; 0.3049873140698863; 0.30946892281750854
            0.31398871337571754; 0.31854677812509186; 0.32314320911295075; 0.3277780980565422
            0.33245153634617935; 0.33716361504833037; 0.3419144249086609; 0.3467040563550296
            0.35153259950043936; 0.3564001441459435; 0.3613067797835095; 0.3662525955988395
            0.3712376804741491; 0.3762621229909065; 0.38132601143253014; 0.386429433787049
            0.39157247774972326; 0.39675523072562685; 0.4019777798321958; 0.4072402119017367
            0.41254261348390375; 0.4178850708481375; 0.4232676699860717; 0.4286904966139066
            0.43415363617474895; 0.4396571738409188; 0.44520119451622786; 0.45078578283822346
            0.45641102318040466; 0.4620769996544071; 0.467783796112159; 0.47353149614800955
            0.4793201831008268; 0.4851499400560704; 0.4910208498478356; 0.4969329950608704
            0.5028864580325687; 0.5088813208549338; 0.5149176653765214; 0.5209955732043543
            0.5271151257058131; 0.5332764040105052; 0.5394794890121072; 0.5457244613701866
            0.5520114015120001; 0.5583403896342679; 0.5647115057049292; 0.5711248294648731
            0.5775804404296506; 0.5840784178911641; 0.5906188409193369; 0.5972017883637634
            0.6038273388553378; 0.6104955708078648; 0.6172065624196511; 0.6239603916750761
            0.6307571363461468; 0.6375968739940326; 0.6444796819705821; 0.6514056374198242
            0.6583748172794485; 0.665387298282272; 0.6724431569576875; 0.6795424696330938
            0.6866853124353135; 0.6938717612919899; 0.7011018919329731; 0.7083757798916868
            0.7156935005064807; 0.7230551289219693; 0.7304607400903537; 0.7379104087727308
            0.7454042095403874; 0.7529422167760779; 0.7605245046752924; 0.768151147247507
            0.7758222183174236; 0.7835377915261935; 0.7912979403326302; 0.799102738014409
            0.8069522576692516; 0.8148465722161012; 0.8227857543962835; 0.8307698767746546
            0.83879901174074; 0.846873231509858; 0.8549926081242338; 0.8631572134541023
            0.8713671191987972; 0.8796223968878317; 0.8879231178819663; 0.8962693533742664
            0.9046611743911496; 0.9130986517934192; 0.9215818562772946; 0.9301108583754237
            0.938685728457888; 0.9473065367331999; 0.9559733532492861; 0.9646862478944651
            0.9734452903984125; 0.9822505503331171; 0.9911020971138298; 1.0
        |]

    let quantise (channel: float) : byte =
        if System.Double.IsNaN channel then
            0uy
        else
            let v = channel * 255.0
            let v = if v < 0.0 then 0.0 elif v > 255.0 then 255.0 else v
            byte (System.Math.Floor(v + 0.5))

    let linear (channel: byte) = table.[int channel]

    let luminance (colour: Color) =
        0.2126 * linear colour.Red + 0.7152 * linear colour.Green + 0.0722 * linear colour.Blue

    let contrast (a: Color) (b: Color) =
        let la = luminance a
        let lb = luminance b
        (max la lb + 0.05) / (min la lb + 0.05)

    /// The real cube root, through `Pow` on both runtimes (Fable has no `Math.Cbrt`). Its last-place differences between
    /// runtimes are absorbed by the half-up quantiser (ADR D11).
    let private cbrt (v: float) =
        if v < 0.0 then -(System.Math.Pow(-v, 1.0 / 3.0)) else System.Math.Pow(v, 1.0 / 3.0)

    /// OKLab from linear sRGB (Ottosson's matrices, as CSS Color 4 uses them).
    let private labOfLinear (r: float, g: float, b: float) =
        let l = cbrt (0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
        let m = cbrt (0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
        let s = cbrt (0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)

        0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
        1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
        0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s

    let private linearOfLab (lightness: float, a: float, b: float) =
        let l = lightness + 0.3963377774 * a + 0.2158037573 * b
        let m = lightness - 0.1055613458 * a - 0.0638541728 * b
        let s = lightness - 0.0894841775 * a - 1.2914855480 * b
        let l = l * l * l
        let m = m * m * m
        let s = s * s * s

        4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s

    let private labOfOklch (colour: Oklch) =
        let radians = colour.H * System.Math.PI / 180.0
        colour.L, colour.C * System.Math.Cos radians, colour.C * System.Math.Sin radians

    let toOklch (colour: Color) : Oklch =
        let l, a, b = labOfLinear (linear colour.Red, linear colour.Green, linear colour.Blue)
        let c = System.Math.Sqrt(a * a + b * b)

        // Hue is powerless at zero chroma (CSS Color 4); below 1e-7 the hue is rounding noise, which may differ between
        // runtimes, so it is 0.
        if c < 1e-7 then
            { L = l; C = 0.0; H = 0.0 }
        else
            let h = System.Math.Atan2(b, a) * 180.0 / System.Math.PI
            { L = l; C = c; H = (if h < 0.0 then h + 360.0 else h) }

    /// Linear light to the sRGB transfer, on a value already clamped to [0, 1].
    let private encode (v: float) =
        if v <= 0.0031308 then 12.92 * v else 1.055 * System.Math.Pow(v, 1.0 / 2.4) - 0.055

    let private clamp01 (v: float) = if v < 0.0 then 0.0 elif v > 1.0 then 1.0 else v

    /// In sRGB, within rounding of the round trip (an 8-bit colour comes back within about 1e-12 of its channels).
    let private inGamut (r: float, g: float, b: float) =
        let epsilon = 1e-9
        let ok v = v >= -epsilon && v <= 1.0 + epsilon
        ok r && ok g && ok b

    let private colourOfLinear (r: float, g: float, b: float) : Color =
        {
            Red = quantise (encode (clamp01 r))
            Green = quantise (encode (clamp01 g))
            Blue = quantise (encode (clamp01 b))
            Alpha = 255uy
        }

    /// The OKLab distance between the clipped colour and the wanted one.
    let private deltaOk (r: float, g: float, b: float) (wanted: float * float * float) =
        let l1, a1, b1 = labOfLinear (clamp01 r, clamp01 g, clamp01 b)
        let l2, a2, b2 = wanted
        System.Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2))

    let ofOklch (colour: Oklch) : Color =
        let white = { Red = 255uy; Green = 255uy; Blue = 255uy; Alpha = 255uy }
        let black = { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }

        if System.Double.IsNaN colour.L || colour.L >= 1.0 then
            (if System.Double.IsNaN colour.L then black else white)
        elif colour.L <= 0.0 then
            black
        else
            let chroma = if System.Double.IsNaN colour.C || colour.C < 0.0 then 0.0 else colour.C
            let at c = { colour with C = c }
            let origin = at chroma
            let rgb = linearOfLab (labOfOklch origin)

            if inGamut rgb then
                colourOfLinear rgb
            else
                // CSS Color 4 gamut mapping: binary search on chroma at constant lightness and hue, accepting a clipped
                // colour within the just-noticeable difference.
                let jnd = 0.02
                let epsilon = 0.0001
                let mutable low = 0.0
                let mutable high = chroma
                let mutable lowInGamut = true
                let mutable current = linearOfLab (labOfOklch origin)
                let mutable result = None

                if deltaOk current (labOfOklch origin) < jnd then
                    result <- Some current

                while result.IsNone && high - low > epsilon do
                    let mid = (low + high) / 2.0
                    let wanted = labOfOklch (at mid)
                    current <- linearOfLab wanted

                    if lowInGamut && inGamut current then
                        low <- mid
                    else
                        let e = deltaOk current wanted

                        if e < jnd then
                            if jnd - e < epsilon then
                                result <- Some current
                            else
                                lowInGamut <- false
                                low <- mid
                        else
                            high <- mid

                colourOfLinear (defaultArg result current)

    let private digits = "0123456789abcdef"

    let hex (colour: Color) =
        let two (v: byte) =
            let i = int v
            System.String [| digits.[i / 16]; digits.[i % 16] |]

        "#" + two colour.Red + two colour.Green + two colour.Blue

    let ofHex (text: string) : Color option =
        let value (c: char) =
            let lower = System.Char.ToLowerInvariant c
            digits.IndexOf lower

        if text.Length <> 7 || text.[0] <> '#' then
            None
        else
            let values = [ for i in 1..6 -> value text.[i] ]

            if values |> List.exists (fun v -> v < 0) then
                None
            else
                let channel i = byte (values.[i] * 16 + values.[i + 1])

                Some
                    {
                        Red = channel 0
                        Green = channel 2
                        Blue = channel 4
                        Alpha = 255uy
                    }

/// A token value: a colour or a number.
type internal TokenValue =
    | ColourToken of Color
    | NumberToken of float

[<Sealed>]
type ThemeSeed internal (name: string, values: Map<string, TokenValue>) =
    member internal _.Name = name
    member internal _.Values = values

[<RequireQualifiedAccess>]
module ThemeSeed =

    let colourNames =
        [ "ground"; "ink"; "allegiance.own"; "allegiance.ally"; "allegiance.neutral"; "allegiance.enemy"; "allegiance.unknown" ]
        @ [ for i in 0..7 -> sprintf "team.%d" i ]
        @ [ "accent.selection"; "accent.marker"; "status.good"; "status.warn"; "status.bad" ]

    let numberNames =
        [ "contrastTarget"; "cell"; "strokeBase"; "gap"; "density"; "minCssPerUnit" ]
        @ [ for i in 0..3 -> sprintf "hueCapacity.%d" i ]

    let private rgb (r: byte) (g: byte) (b: byte) : Color = { Red = r; Green = g; Blue = b; Alpha = 255uy }

    let classic =
        ThemeSeed(
            "classic",
            Map.ofList
                [
                    "ground", ColourToken(rgb 34uy 40uy 49uy)
                    "ink", ColourToken(rgb 255uy 255uy 255uy)
                    "allegiance.own", ColourToken(rgb 64uy 140uy 255uy)
                    "allegiance.ally", ColourToken(rgb 0uy 165uy 190uy)
                    "allegiance.neutral", ColourToken(rgb 40uy 165uy 80uy)
                    "allegiance.enemy", ColourToken(rgb 235uy 80uy 70uy)
                    "allegiance.unknown", ColourToken(rgb 200uy 150uy 0uy)
                    // Provisional (FS.GG.Game#789 review): eight hues distinct from every allegiance colour (OKLab
                    // distance at least 0.069) and from each other (at least 0.096), each 3:1 on the ground, the body and
                    // the dark ink. FS.GG.Game#795's derivation replaces them.
                    "team.0", ColourToken(rgb 0xdbuy 0x24uy 0x78uy)
                    "team.1", ColourToken(rgb 0xe5uy 0x6cuy 0x03uy)
                    "team.2", ColourToken(rgb 0x01uy 0xa1uy 0x8duy)
                    "team.3", ColourToken(rgb 0x93uy 0x4fuy 0xe8uy)
                    "team.4", ColourToken(rgb 0x80uy 0x97uy 0x03uy)
                    "team.5", ColourToken(rgb 0xc0uy 0x39uy 0xb9uy)
                    "team.6", ColourToken(rgb 0x02uy 0x94uy 0xd9uy)
                    "team.7", ColourToken(rgb 0x5auy 0x61uy 0xf7uy)
                    "accent.selection", ColourToken(rgb 255uy 214uy 0uy)
                    "accent.marker", ColourToken(rgb 0uy 220uy 220uy)
                    "status.good", ColourToken(rgb 70uy 200uy 100uy)
                    "status.warn", ColourToken(rgb 240uy 180uy 40uy)
                    "status.bad", ColourToken(rgb 230uy 60uy 50uy)
                    "contrastTarget", NumberToken 3.0
                    "cell", NumberToken 32.0
                    "strokeBase", NumberToken 2.0
                    "gap", NumberToken 2.0
                    "density", NumberToken 1.0
                    "minCssPerUnit", NumberToken 1.0
                    "hueCapacity.0", NumberToken 5.0
                    "hueCapacity.1", NumberToken 6.0
                    "hueCapacity.2", NumberToken 7.0
                    "hueCapacity.3", NumberToken 8.0
                ]
        )

    let named (name: string) (seed: ThemeSeed) = ThemeSeed(name, seed.Values)

    let colour (name: string) (colour: Color) (seed: ThemeSeed) =
        ThemeSeed(seed.Name, seed.Values |> Map.add name (ColourToken colour))

    let number (name: string) (value: float) (seed: ThemeSeed) =
        ThemeSeed(seed.Name, seed.Values |> Map.add name (NumberToken value))

    let without (name: string) (seed: ThemeSeed) = ThemeSeed(seed.Name, seed.Values |> Map.remove name)

    let name (seed: ThemeSeed) = seed.Name

    let tryColour (name: string) (seed: ThemeSeed) =
        match seed.Values |> Map.tryFind name with
        | Some(ColourToken c) -> Some c
        | _ -> None

    let tryNumber (name: string) (seed: ThemeSeed) =
        match seed.Values |> Map.tryFind name with
        | Some(NumberToken v) -> Some v
        | _ -> None

    let colours (seed: ThemeSeed) =
        seed.Values
        |> Map.toList
        |> List.choose (fun (k, v) ->
            match v with
            | ColourToken c -> Some(k, c)
            | NumberToken _ -> None)

    let numbers (seed: ThemeSeed) =
        seed.Values
        |> Map.toList
        |> List.choose (fun (k, v) ->
            match v with
            | NumberToken n -> Some(k, n)
            | ColourToken _ -> None)

[<Sealed>]
type ThemeTokens internal (values: Map<string, TokenValue>) =
    member internal _.Values = values

[<RequireQualifiedAccess>]
module ThemeTokens =

    let empty = ThemeTokens Map.empty

    let tryColour (name: string) (tokens: ThemeTokens) =
        match tokens.Values |> Map.tryFind name with
        | Some(ColourToken c) -> Some c
        | _ -> None

    let tryNumber (name: string) (tokens: ThemeTokens) =
        match tokens.Values |> Map.tryFind name with
        | Some(NumberToken v) -> Some v
        | _ -> None

    let withColour (name: string) (colour: Color) (tokens: ThemeTokens) =
        ThemeTokens(tokens.Values |> Map.add name (ColourToken colour))

    let withNumber (name: string) (value: float) (tokens: ThemeTokens) =
        ThemeTokens(tokens.Values |> Map.add name (NumberToken value))

    let names (tokens: ThemeTokens) = tokens.Values |> Map.toList |> List.map fst

[<Sealed>]
type ThemeAlgorithm internal (name: string, refine: ThemeSeed -> ThemeTokens -> ThemeTokens) =
    member internal _.Name = name
    member internal _.Refine = refine

[<RequireQualifiedAccess>]
module ThemeAlgorithm =

    let create (name: string) (refine: ThemeSeed -> ThemeTokens -> ThemeTokens) = ThemeAlgorithm(name, refine)

    let name (algorithm: ThemeAlgorithm) = algorithm.Name

    /// Mix two colours in OKLab, `t` of the way from `a` to `b`.
    let internal mix (a: Color) (b: Color) (t: float) : Color =
        let pa = ThemeColour.toOklch a
        let pb = ThemeColour.toOklch b
        let lab (p: Oklch) =
            let radians = p.H * System.Math.PI / 180.0
            p.L, p.C * System.Math.Cos radians, p.C * System.Math.Sin radians
        let la, aa, ba = lab pa
        let lb, ab, bb = lab pb
        let l = la + (lb - la) * t
        let a = aa + (ab - aa) * t
        let b = ba + (bb - ba) * t
        let c = System.Math.Sqrt(a * a + b * b)
        let h = if c < 1e-7 then 0.0 else System.Math.Atan2(b, a) * 180.0 / System.Math.PI
        ThemeColour.ofOklch { L = l; C = (if c < 1e-7 then 0.0 else c); H = (if h < 0.0 then h + 360.0 else h) }

    let standard =
        create "standard" (fun seed tokens ->
            let mutable map = tokens.Values

            let colourOr name fallback =
                ThemeSeed.tryColour name seed |> Option.defaultValue fallback

            let numberOr name fallback =
                ThemeSeed.tryNumber name seed |> Option.defaultValue fallback

            let setColour name colour = map <- map |> Map.add name (ColourToken colour)
            let setNumber name value = map <- map |> Map.add name (NumberToken value)

            // Every seed colour is copied as it is (an 8-bit colour survives the OKLCH round trip unchanged).
            for name, colour in ThemeSeed.colours seed do
                setColour name colour

            let black = { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }
            let ground = colourOr "ground" black
            let ink = colourOr "ink" { black with Red = 255uy; Green = 255uy; Blue = 255uy }
            let groundL = (ThemeColour.toOklch ground).L
            let inkL = (ThemeColour.toOklch ink).L

            // The pole opposite the ink: near-black when the ink is light, near-white when it is dark. It inks badges and
            // icons, lines the status track and is the inner focus tone.
            let pole = ThemeColour.ofOklch { L = (if inkL >= groundL then 0.16 else 0.97); C = 0.0; H = 0.0 }
            let selection = colourOr "accent.selection" ink
            let marker = colourOr "accent.marker" ink

            setColour "body" ink
            setColour "badge.ink" pole
            setColour "icon.ink" pole
            setColour "selection" selection
            setColour "marker" marker
            setColour "path" marker
            setColour "stale" (mix ink ground 0.45)
            setColour "focus.outer" ink
            setColour "focus.inner" pole
            setColour "hover" (mix ink ground 0.3)
            setColour "status.track" pole
            setColour "status.keyline" ink
            setColour "modifier.ink" pole
            setColour "terrain.open" (mix ground ink 0.06)
            setColour "terrain.blocked" (mix ground ink 0.2)
            setColour "terrain.difficult" (ThemeColour.ofOklch { L = groundL + 0.06; C = 0.05; H = 70.0 })
            setColour "terrain.water" (ThemeColour.ofOklch { L = groundL + 0.04; C = 0.07; H = 245.0 })
            setColour "terrain.void" (mix ground pole 0.6)

            let strokeBase = numberOr "strokeBase" 2.0
            setNumber "cell" (numberOr "cell" 32.0 * numberOr "density" 1.0)
            setNumber "band" strokeBase
            setNumber "gap" (numberOr "gap" 2.0)
            setNumber "bracket" strokeBase
            setNumber "hoverWidth" (max 1.0 (System.Math.Floor(strokeBase / 2.0)))
            setNumber "contrastTarget" (numberOr "contrastTarget" 3.0)
            setNumber "minCssPerUnit" (numberOr "minCssPerUnit" 1.0)

            for i in 0..3 do
                let key = sprintf "hueCapacity.%d" i
                setNumber key (numberOr key (float (min 8 (5 + i))))

            ThemeTokens map)

[<Sealed>]
type ThemeOverride internal (token: string, value: TokenValue) =
    member internal _.Token = token
    member internal _.Value = value

[<RequireQualifiedAccess>]
module ThemeOverride =

    let colour (token: string) (colour: Color) = ThemeOverride(token, ColourToken colour)

    let number (token: string) (value: float) = ThemeOverride(token, NumberToken value)

[<Sealed>]
type ThemeViolation internal (token: string, reason: string) =
    member _.Token = token
    member _.Reason = reason
    override _.ToString() = token + ": " + reason

[<RequireQualifiedAccess>]
module ThemeViolation =

    let create (token: string) (reason: string) = ThemeViolation(token, reason)

/// The resolved colours glyph drawing reads (internal: the library's own view of the alias layer).
[<NoComparison>]
type internal Palette =
    {
        Ground: Color
        Body: Color
        Own: Color
        Ally: Color
        Neutral: Color
        Enemy: Color
        Unknown: Color
        Teams: Color list
        BadgeInk: Color
        IconInk: Color
        Selection: Color
        Marker: Color
        Path: Color
        Stale: Color
        FocusOuter: Color
        FocusInner: Color
        Hover: Color
        Good: Color
        Warn: Color
        Bad: Color
        Track: Color
        Keyline: Color
        ModifierInk: Color
        TerrainOpen: Color
        TerrainBlocked: Color
        TerrainDifficult: Color
        TerrainWater: Color
        TerrainVoid: Color
        Cell: float
        Band: float
        Gap: float
        Bracket: float
        HoverWidth: float
        MinCssPerUnit: float
        HueCapacity: int list
    }

[<Sealed>]
type Theme
    internal
    (
        seed: ThemeSeed,
        algorithms: ThemeAlgorithm list,
        overrides: ThemeOverride list,
        tokens: ThemeTokens,
        aliases: (string * TokenValue) list,
        palette: Palette,
        checks: ThemeCheck list
    ) =
    member internal _.Checks = checks
    member internal _.Seed = seed
    member internal _.Algorithms = algorithms
    member internal _.Overrides = overrides
    member internal _.Tokens = tokens
    member internal _.Aliases = aliases
    member internal _.Palette = palette

and [<Sealed>] ThemeCheck internal (name: string, run: Theme -> ThemeViolation list) =
    member internal _.Name = name
    member internal _.Run = run

[<RequireQualifiedAccess>]
module ThemeCheck =

    let create (name: string) (run: Theme -> ThemeViolation list) = ThemeCheck(name, run)

    let name (check: ThemeCheck) = check.Name

[<RequireQualifiedAccess>]
module Theme =

    /// The colour aliases before the teams, then after them, each with the map token it reads.
    let private leadingColours =
        [
            "surface.ground", "ground"
            "glyph.unit.body", "body"
            "glyph.allegiance.own", "allegiance.own"
            "glyph.allegiance.ally", "allegiance.ally"
            "glyph.allegiance.neutral", "allegiance.neutral"
            "glyph.allegiance.enemy", "allegiance.enemy"
            "glyph.allegiance.unknown", "allegiance.unknown"
        ]

    let private trailingColours =
        [
            "glyph.badge.ink", "badge.ink"
            "glyph.icon.ink", "icon.ink"
            "glyph.modifier", "modifier.ink"
            "glyph.selection", "selection"
            "glyph.marker", "marker"
            "glyph.path", "path"
            "glyph.stale", "stale"
            "cue.focus.outer", "focus.outer"
            "cue.focus.inner", "focus.inner"
            "cue.hover", "hover"
            "status.good", "status.good"
            "status.warn", "status.warn"
            "status.bad", "status.bad"
            "status.track", "status.track"
            "status.keyline", "status.keyline"
            "terrain.open", "terrain.open"
            "terrain.blocked", "terrain.blocked"
            "terrain.difficult", "terrain.difficult"
            "terrain.water", "terrain.water"
            "terrain.void", "terrain.void"
        ]

    let private numberAliases =
        [
            "size.cell", "cell"
            "stroke.outline", "band"
            "stroke.gap", "gap"
            "stroke.bracket", "bracket"
            "stroke.hover", "hoverWidth"
        ]

    /// The theme's teams: `team.0` up to the first one missing, at most 8.
    let private teamsOf (tokens: ThemeTokens) =
        Seq.initInfinite id
        |> Seq.takeWhile (fun i -> i < 8 && (ThemeTokens.tryColour (sprintf "team.%d" i) tokens).IsSome)
        |> Seq.length

    let private aliasTable (teams: int) =
        [
            yield! leadingColours
            for i in 0 .. teams - 1 do
                yield sprintf "glyph.team.%d" i, sprintf "team.%d" i
            yield! trailingColours
            yield! numberAliases
        ]

    let private resolve (seed: ThemeSeed) (algorithms: ThemeAlgorithm list) (overrides: ThemeOverride list) (checks: ThemeCheck list) =
        let violations = ResizeArray<ThemeViolation>()
        let fail token reason = violations.Add(ThemeViolation(token, reason))

        // The seed: only known names, colours as colours and numbers as numbers.
        for name, value in seed.Values |> Map.toList do
            match value with
            | ColourToken _ when not (ThemeSeed.colourNames |> List.contains name) ->
                if ThemeSeed.numberNames |> List.contains name then fail name "is a number, not a colour"
                else fail name "is not a seed name"
            | NumberToken _ when not (ThemeSeed.numberNames |> List.contains name) ->
                if ThemeSeed.colourNames |> List.contains name then fail name "is a colour, not a number"
                else fail name "is not a seed name"
            | _ -> ()

        for name in [ "ground"; "ink" ] do
            if (ThemeSeed.tryColour name seed).IsNone then fail name "the seed must set it"

        let teamGap =
            [ 0..7 ]
            |> List.tryFind (fun i -> (ThemeSeed.tryColour (sprintf "team.%d" i) seed).IsNone)

        match teamGap with
        | Some 0 -> fail "team.0" "the seed must set at least one team"
        | Some first ->
            for i in first + 1 .. 7 do
                if (ThemeSeed.tryColour (sprintf "team.%d" i) seed).IsSome then
                    fail (sprintf "team.%d" i) (sprintf "follows the missing team.%d, so it is not a team" first)
        | None -> ()

        let mapped =
            algorithms |> List.fold (fun tokens (algorithm: ThemeAlgorithm) -> algorithm.Refine seed tokens) ThemeTokens.empty

        let teams = teamsOf mapped
        let table = aliasTable teams

        // Overrides: a map token by its own name, an alias by its alias name.
        let mutable mapValues = mapped.Values
        let aliasOverrides = System.Collections.Generic.Dictionary<string, TokenValue>()

        // The floors (contrast, presented scale, hue capacity) are the seed's: an override could only lower them.
        let isFloor (token: string) =
            token = "contrastTarget" || token = "minCssPerUnit" || token.StartsWith "hueCapacity"

        for o in overrides do
            match table |> List.tryFind (fun (alias, _) -> alias = o.Token) with
            | _ when isFloor o.Token -> fail o.Token "is a floor: it is set in the seed, never by an override"
            | Some _ -> aliasOverrides.[o.Token] <- o.Value
            | None when mapValues.ContainsKey o.Token -> mapValues <- mapValues |> Map.add o.Token o.Value
            | None -> fail o.Token "is neither an alias nor a map token of this theme"

        let tokens = ThemeTokens mapValues

        let aliases =
            table
            |> List.choose (fun (alias, key) ->
                let value =
                    match aliasOverrides.TryGetValue alias with
                    | true, value -> Some value
                    | _ -> mapValues |> Map.tryFind key

                match value with
                | Some value -> Some(alias, value)
                | None ->
                    fail alias (sprintf "reads map token %s, which no algorithm set" key)
                    None)

        let colourOf alias =
            match aliases |> List.tryFind (fun (a, _) -> a = alias) with
            | Some(_, ColourToken c) -> c
            | _ -> { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }

        let numberOf alias fallback =
            match aliases |> List.tryFind (fun (a, _) -> a = alias) with
            | Some(_, NumberToken v) -> v
            | _ -> fallback

        let mapNumber key fallback =
            match mapValues |> Map.tryFind key with
            | Some(NumberToken v) -> v
            | _ -> fallback

        // Kinds and opacity.
        for alias, value in aliases do
            match value, table |> List.findIndex (fun (a, _) -> a = alias) with
            | ColourToken c, _ when c.Alpha <> 255uy -> fail alias (sprintf "alpha %d: every token is opaque until alpha is supported (Q5)" c.Alpha)
            | NumberToken _, _ when not (numberAliases |> List.exists (fun (a, _) -> a = alias)) -> fail alias "is a colour alias, set to a number"
            | ColourToken _, _ when numberAliases |> List.exists (fun (a, _) -> a = alias) -> fail alias "is a number alias, set to a colour"
            | _ -> ()

        // Numbers.
        let positive alias =
            let v = numberOf alias nan
            if System.Double.IsNaN v || System.Double.IsInfinity v || v <= 0.0 then fail alias (sprintf "%g: must be greater than 0" v)

        for alias, _ in numberAliases do
            positive alias

        let target = mapNumber "contrastTarget" 3.0
        let minCss = mapNumber "minCssPerUnit" 1.0

        if System.Double.IsNaN target || target < 3.0 || target > 21.0 then
            fail "contrastTarget" (sprintf "%g: must be from 3 to 21" target)

        if System.Double.IsNaN minCss || System.Double.IsInfinity minCss || minCss <= 0.0 then
            fail "minCssPerUnit" (sprintf "%g: must be greater than 0" minCss)
        elif numberOf "stroke.gap" 0.0 < 1.0 / minCss then
            fail "stroke.gap" (sprintf "%g: must be at least 1 / minCssPerUnit = %g units" (numberOf "stroke.gap" 0.0) (1.0 / minCss))

        let capacity =
            [
                for i in 0..3 ->
                    let v = mapNumber (sprintf "hueCapacity.%d" i) 0.0
                    if System.Double.IsNaN v || v < 1.0 || v > 8.0 || v <> System.Math.Floor v then
                        fail (sprintf "hueCapacity.%d" i) (sprintf "%g: must be a whole number from 1 to 8" v)
                    int (if System.Double.IsNaN v then 0.0 else v)
            ]

        if capacity <> List.sort capacity then
            fail "hueCapacity" "must not fall as the size step grows"

        // Contrast (ADR D4, D10, D12): on the ground, and between the colours that touch.
        let ground = colourOf "surface.ground"
        let body = colourOf "glyph.unit.body"
        let teamAliases = [ for i in 0 .. teams - 1 -> sprintf "glyph.team.%d" i ]
        let allegianceAliases = [ "own"; "ally"; "neutral"; "enemy"; "unknown" ] |> List.map (fun a -> "glyph.allegiance." + a)

        let atLeast (floor: float) alias (other: string) (on: Color) =
            let ratio = ThemeColour.contrast (colourOf alias) on
            if ratio < floor then
                fail alias (sprintf "%.2f:1 on %s, below the %g:1 floor" ratio other floor)

        for alias in
            teamAliases
            @ allegianceAliases
            @ [ "glyph.unit.body"; "glyph.stale"; "glyph.selection"; "glyph.marker"; "glyph.path"; "cue.focus.outer"; "cue.hover"; "status.keyline" ] do
            atLeast target alias "surface.ground" ground

        atLeast 3.0 "cue.focus.inner" "cue.focus.outer" (colourOf "cue.focus.outer")

        for on in "glyph.unit.body" :: teamAliases @ allegianceAliases do
            atLeast 3.0 "glyph.badge.ink" on (colourOf on)

        for on in "glyph.unit.body" :: allegianceAliases do
            atLeast 3.0 "glyph.icon.ink" on (colourOf on)

        for alias in teamAliases do
            atLeast 3.0 alias "glyph.unit.body" body

        for alias in [ "status.good"; "status.warn"; "status.bad" ] do
            atLeast 3.0 alias "status.track" (colourOf "status.track")

        atLeast 3.0 "status.track" "status.keyline" (colourOf "status.keyline")

        for on in "glyph.unit.body" :: teamAliases @ allegianceAliases do
            atLeast 3.0 "glyph.modifier" on (colourOf on)

        let palette =
            {
                Ground = ground
                Body = body
                Own = colourOf "glyph.allegiance.own"
                Ally = colourOf "glyph.allegiance.ally"
                Neutral = colourOf "glyph.allegiance.neutral"
                Enemy = colourOf "glyph.allegiance.enemy"
                Unknown = colourOf "glyph.allegiance.unknown"
                Teams = teamAliases |> List.map colourOf
                BadgeInk = colourOf "glyph.badge.ink"
                IconInk = colourOf "glyph.icon.ink"
                Selection = colourOf "glyph.selection"
                Marker = colourOf "glyph.marker"
                Path = colourOf "glyph.path"
                Stale = colourOf "glyph.stale"
                FocusOuter = colourOf "cue.focus.outer"
                FocusInner = colourOf "cue.focus.inner"
                Hover = colourOf "cue.hover"
                Good = colourOf "status.good"
                Warn = colourOf "status.warn"
                Bad = colourOf "status.bad"
                Track = colourOf "status.track"
                Keyline = colourOf "status.keyline"
                ModifierInk = colourOf "glyph.modifier"
                TerrainOpen = colourOf "terrain.open"
                TerrainBlocked = colourOf "terrain.blocked"
                TerrainDifficult = colourOf "terrain.difficult"
                TerrainWater = colourOf "terrain.water"
                TerrainVoid = colourOf "terrain.void"
                Cell = numberOf "size.cell" 32.0
                Band = numberOf "stroke.outline" 2.0
                Gap = numberOf "stroke.gap" 2.0
                Bracket = numberOf "stroke.bracket" 2.0
                HoverWidth = numberOf "stroke.hover" 1.0
                MinCssPerUnit = minCss
                HueCapacity = capacity
            }

        if violations.Count = 0 then
            // The added checks (FS.GG.Game#795's distinctness and colour-vision checks, Theme.validate) run on the theme the
            // built-in rules accept, in order; a check that throws is itself a violation.
            let theme = Theme(seed, algorithms, overrides, tokens, aliases, palette, checks)

            for check in checks do
                try
                    violations.AddRange(check.Run theme)
                with error ->
                    fail check.Name ("the check failed: " + error.Message)

            if violations.Count = 0 then Ok theme else Error(List.ofSeq violations)
        else
            Error(List.ofSeq violations)

    let create (seed: ThemeSeed) (algorithms: ThemeAlgorithm list) (overrides: ThemeOverride list) =
        resolve seed algorithms overrides []

    let validate (checks: ThemeCheck list) (theme: Theme) =
        resolve theme.Seed theme.Algorithms theme.Overrides (theme.Checks @ checks)

    let checks (theme: Theme) = theme.Checks |> List.map (fun c -> c.Name)

    let withOverrides (overrides: ThemeOverride list) (theme: Theme) =
        resolve theme.Seed theme.Algorithms (theme.Overrides @ overrides) theme.Checks

    let withTeamColour (team: int) (colour: Color) (theme: Theme) =
        let alias = sprintf "glyph.team.%d" team
        let teams = theme.Palette.Teams.Length

        if team < 0 || team >= teams then
            Error [ ThemeViolation(alias, sprintf "theme %s colours teams 0 to %d" theme.Seed.Name (teams - 1)) ]
        else
            withOverrides [ ThemeOverride(alias, ColourToken colour) ] theme

    let name (theme: Theme) = theme.Seed.Name
    let seed (theme: Theme) = theme.Seed
    let tokens (theme: Theme) = theme.Tokens
    let algorithms (theme: Theme) = theme.Algorithms |> List.map (fun a -> a.Name)
    let aliases (theme: Theme) = theme.Aliases |> List.map fst

    let tryColour (alias: string) (theme: Theme) =
        match theme.Aliases |> List.tryFind (fun (a, _) -> a = alias) with
        | Some(_, ColourToken c) -> Some c
        | _ -> None

    let tryNumber (alias: string) (theme: Theme) =
        match theme.Aliases |> List.tryFind (fun (a, _) -> a = alias) with
        | Some(_, NumberToken v) -> Some v
        | _ -> None

    let allegiance (identity: Identity) (theme: Theme) =
        let p = theme.Palette

        match identity with
        | Identity.Own -> p.Own
        | Identity.Ally -> p.Ally
        | Identity.Neutral -> p.Neutral
        | Identity.Enemy -> p.Enemy
        | Identity.Unknown -> p.Unknown

    let teamCount (theme: Theme) = theme.Palette.Teams.Length
    let minCssPerUnit (theme: Theme) = theme.Palette.MinCssPerUnit

    let hueCapacity (theme: Theme) (sizeStep: int) =
        let capacity = theme.Palette.HueCapacity
        capacity.[max 0 (min (capacity.Length - 1) sizeStep)]

    let hex (theme: Theme) =
        theme.Aliases
        |> List.choose (fun (alias, value) ->
            match value with
            | ColourToken c -> Some(alias, ThemeColour.hex c)
            | NumberToken _ -> None)

    /// A number as text, the same on .NET and Fable: rounded half up to 6 decimals, trailing zeros dropped.
    let internal number (v: float) =
        let scaled = System.Math.Floor(abs v * 1000000.0 + 0.5)
        let whole = System.Math.Floor(scaled / 1000000.0)
        let fraction = int (scaled - whole * 1000000.0)
        let sign = if v < 0.0 && scaled > 0.0 then "-" else ""
        let digits = string (int64 whole)

        if fraction = 0 then
            sign + digits
        else
            sign + digits + "." + (string fraction).PadLeft(6, '0').TrimEnd('0')

    let cssVariables (theme: Theme) =
        theme.Aliases
        |> List.map (fun (alias, value) ->
            "--fsgg-" + alias.Replace('.', '-'),
            match value with
            | ColourToken c -> ThemeColour.hex c
            | NumberToken v -> number v)

    // ---- JSON, the same bytes on .NET and Fable --------------------------------------------------------------------

    type internal Json =
        | JString of string
        | JNumber of float
        | JBool of bool
        | JNull
        | JArray of Json list
        | JObject of (string * Json) list

    let private quote (s: string) =
        let sb = System.Text.StringBuilder()
        sb.Append('"') |> ignore

        for c in s do
            match c with
            | '"' -> sb.Append("\\\"") |> ignore
            | '\\' -> sb.Append("\\\\") |> ignore
            | '\n' -> sb.Append("\\n") |> ignore
            | '\r' -> sb.Append("\\r") |> ignore
            | '\t' -> sb.Append("\\t") |> ignore
            | c when int c < 0x20 ->
                let code = int c
                sb.Append("\\u00").Append("0123456789abcdef".[code / 16]).Append("0123456789abcdef".[code % 16]) |> ignore
            | c -> sb.Append(c) |> ignore

        sb.Append('"') |> ignore
        sb.ToString()

    let rec private write (indent: string) (json: Json) : string =
        let inner = indent + "  "

        match json with
        | JString s -> quote s
        | JNumber v -> number v
        | JBool b -> if b then "true" else "false"
        | JNull -> "null"
        | JArray [] -> "[]"
        | JArray items ->
            // Arrays of scalars stay on one line (components).
            if items |> List.forall (function JArray _ | JObject _ -> false | _ -> true) then
                "[" + (items |> List.map (write inner) |> String.concat ", ") + "]"
            else
                "[\n" + (items |> List.map (fun i -> inner + write inner i) |> String.concat ",\n") + "\n" + indent + "]"
        | JObject [] -> "{}"
        | JObject members ->
            "{\n"
            + (members |> List.map (fun (k, v) -> inner + quote k + ": " + write inner v) |> String.concat ",\n")
            + "\n"
            + indent
            + "}"

    exception private JsonError of string

    let private parse (text: string) : Result<Json, string> =
        let mutable i = 0
        let n = text.Length
        let error message = raise (JsonError(sprintf "%s at character %d" message i))

        let skip () =
            while i < n && (text.[i] = ' ' || text.[i] = '\n' || text.[i] = '\r' || text.[i] = '\t') do
                i <- i + 1

        let expect (c: char) =
            skip ()
            if i < n && text.[i] = c then i <- i + 1 else error (sprintf "expected '%c'" c)

        let rec value () : Json =
            skip ()
            if i >= n then error "unexpected end"

            match text.[i] with
            | '{' ->
                i <- i + 1
                skip ()
                if i < n && text.[i] = '}' then
                    i <- i + 1
                    JObject []
                else
                    let members = ResizeArray<string * Json>()
                    let mutable more = true

                    while more do
                        skip ()
                        let key = str ()
                        expect ':'
                        members.Add((key, value ()))
                        skip ()

                        if i < n && text.[i] = ',' then i <- i + 1
                        elif i < n && text.[i] = '}' then
                            i <- i + 1
                            more <- false
                        else error "expected ',' or '}'"

                    JObject(List.ofSeq members)
            | '[' ->
                i <- i + 1
                skip ()
                if i < n && text.[i] = ']' then
                    i <- i + 1
                    JArray []
                else
                    let items = ResizeArray<Json>()
                    let mutable more = true

                    while more do
                        items.Add(value ())
                        skip ()

                        if i < n && text.[i] = ',' then i <- i + 1
                        elif i < n && text.[i] = ']' then
                            i <- i + 1
                            more <- false
                        else error "expected ',' or ']'"

                    JArray(List.ofSeq items)
            | '"' -> JString(str ())
            | 't' when i + 4 <= n && text.Substring(i, 4) = "true" ->
                i <- i + 4
                JBool true
            | 'f' when i + 5 <= n && text.Substring(i, 5) = "false" ->
                i <- i + 5
                JBool false
            | 'n' when i + 4 <= n && text.Substring(i, 4) = "null" ->
                i <- i + 4
                JNull
            | c when c = '-' || (c >= '0' && c <= '9') ->
                let start = i
                i <- i + 1

                while i < n && (System.Char.IsDigit text.[i] || text.[i] = '.' || text.[i] = 'e' || text.[i] = 'E' || text.[i] = '+' || text.[i] = '-') do
                    i <- i + 1

                let literal = text.Substring(start, i - start)
#if FABLE_COMPILER
                let parsed = System.Double.TryParse literal
#else
                let parsed =
                    System.Double.TryParse(literal, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
#endif
                match parsed with
                | true, v -> JNumber v
                | _ -> error "bad number"
            | _ -> error "unexpected character"

        and str () : string =
            skip ()
            if i >= n || text.[i] <> '"' then error "expected a string"
            i <- i + 1
            let sb = System.Text.StringBuilder()
            let mutable closed = false

            while not closed do
                if i >= n then error "unterminated string"
                let c = text.[i]
                i <- i + 1

                if c = '"' then closed <- true
                elif c = '\\' then
                    if i >= n then error "unterminated escape"
                    let e = text.[i]
                    i <- i + 1

                    match e with
                    | '"' -> sb.Append('"') |> ignore
                    | '\\' -> sb.Append('\\') |> ignore
                    | '/' -> sb.Append('/') |> ignore
                    | 'b' -> sb.Append('\b') |> ignore
                    | 'f' -> sb.Append('\012') |> ignore
                    | 'n' -> sb.Append('\n') |> ignore
                    | 'r' -> sb.Append('\r') |> ignore
                    | 't' -> sb.Append('\t') |> ignore
                    | 'u' when i + 4 <= n ->
                        let code = System.Convert.ToInt32(text.Substring(i, 4), 16)
                        i <- i + 4
                        sb.Append(char code) |> ignore
                    | _ -> error "bad escape"
                else
                    sb.Append(c) |> ignore

            sb.ToString()

        try
            let result = value ()
            skip ()
            if i <> n then error "trailing text"
            Ok result
        with JsonError message ->
            Error message

    // ---- DTCG ---------------------------------------------------------------------------------------------------------

    let private colourToken (c: Color) =
        let p = ThemeColour.toOklch c

        JObject
            [
                "$type", JString "color"
                "$value",
                JObject
                    [
                        "colorSpace", JString "oklch"
                        "components", JArray [ JNumber p.L; JNumber p.C; JNumber p.H ]
                        "hex", JString(ThemeColour.hex c)
                    ]
            ]

    let private numberToken (v: float) =
        JObject [ "$type", JString "number"; "$value", JNumber v ]

    let private tokenJson value =
        match value with
        | ColourToken c -> colourToken c
        | NumberToken v -> numberToken v

    /// Dotted names as nested DTCG groups, in first-seen order.
    let rec private nest (entries: (string list * Json) list) : (string * Json) list =
        let keys = entries |> List.map (fun (path, _) -> List.head path) |> List.distinct

        keys
        |> List.map (fun key ->
            let mine = entries |> List.filter (fun (path, _) -> List.head path = key)

            match mine with
            | [ ([ _ ], token) ] -> key, token
            | _ -> key, JObject(nest (mine |> List.map (fun (path, token) -> List.tail path, token) |> List.filter (fun (p, _) -> not p.IsEmpty))))

    let private group (entries: (string * TokenValue) list) =
        JObject(nest (entries |> List.map (fun (name, value) -> List.ofArray (name.Split '.'), tokenJson value)))

    let toDtcg (theme: Theme) =
        let seedEntries =
            (ThemeSeed.colours theme.Seed |> List.map (fun (k, c) -> k, ColourToken c))
            @ (ThemeSeed.numbers theme.Seed |> List.map (fun (k, v) -> k, NumberToken v))

        write
            ""
            (JObject
                [
                    "$description", JString(sprintf "FS.GG.Game.Render glyph theme %s (ADR GAME-RENDER-02.4 D4)" theme.Seed.Name)
                    "$extensions",
                    JObject
                        [
                            "gg.fs.theme",
                            JObject
                                [
                                    "name", JString theme.Seed.Name
                                    "algorithms", JArray(theme.Algorithms |> List.map (fun a -> JString a.Name))
                                    "numberOverrides",
                                    JObject(
                                        theme.Overrides
                                        |> List.choose (fun o -> match o.Value with NumberToken v -> Some(o.Token, JNumber v) | ColourToken _ -> None)
                                    )
                                ]
                        ]
                    "seed", group seedEntries
                    "alias", group theme.Aliases
                ])
        + "\n"

    let dtcgResolver (themes: Theme list) =
        let names = themes |> List.map (fun t -> t.Seed.Name)
        let file n = JArray [ JObject [ "$ref", JString(n + ".tokens.json") ] ]

        // DTCG Resolver Module: `version` and `resolutionOrder` are required. The module contradicts itself on the version
        // date (2025-10-01 in its property table, 2025-11-01 in section 4.1.2); Root's ruling (FS.GG.Game#789, #805 Q-T9)
        // is 2025-10-01. A modifier should have two or more contexts, so a single theme is a set, not a modifier.
        let single = List.length names = 1

        let sets =
            if single then [ "sets", JObject [ names.Head, JObject [ "sources", file names.Head ] ] ] else []

        let themeModifier =
            if single then
                []
            else
                [
                    "theme",
                    JObject
                        [
                            "contexts", JObject [ for n in names -> n, file n ]
                            "default", JString(List.tryHead names |> Option.defaultValue "")
                        ]
                ]

        let themeRef = if single then "#/sets/" + names.Head else "#/modifiers/theme"

        let density =
            "density",
            JObject
                [
                    "contexts",
                    JObject [ "regular", JArray []; "compact", JArray [ JObject [ "seed", JObject [ "density", numberToken 0.75 ] ] ] ]
                    "default", JString "regular"
                ]

        write
            ""
            (JObject(
                [
                    "name", JString "fsgg-glyph-themes"
                    "description", JString "FS.GG.Game.Render glyph themes (ADR GAME-RENDER-02.4 D4)"
                    "version", JString "2025-10-01"
                ]
                @ sets
                @ [
                    "modifiers", JObject(themeModifier @ [ density ])
                    "resolutionOrder", JArray [ JObject [ "$ref", JString themeRef ]; JObject [ "$ref", JString "#/modifiers/density" ] ]
                ]
            ))
        + "\n"

    /// Every token of a DTCG document, flattened to dotted paths from its root, with the `$type` it declares or
    /// inherits from its nearest group (DTCG 2025.10: a group's `$type` applies to the tokens inside it).
    let rec private collect (prefix: string) (inherited: string option) (json: Json) : (string * string option * Json) list =
        match json with
        | JObject members ->
            let own = members |> List.tryPick (fun (k, v) -> match k, v with | "$type", JString t -> Some t | _ -> None)
            let kind = match own with Some t -> Some t | None -> inherited

            match members |> List.tryPick (fun (k, v) -> if k = "$value" then Some v else None) with
            | Some value -> [ prefix, kind, value ]
            | None ->
                members
                |> List.filter (fun (k, _) -> not (k.StartsWith "$"))
                |> List.collect (fun (k, v) -> collect (if prefix = "" then k else prefix + "." + k) kind v)
        | _ -> []

    /// The value of the token at `path`, following DTCG references (`"{seed.team.0}"`, a whole-value alias) through
    /// the document, refusing a missing target or a cycle.
    let rec private resolveToken
        (all: Map<string, string option * Json>)
        (seen: string list)
        (path: string)
        : Result<TokenValue, string> =
        match all |> Map.tryFind path with
        | None -> Error(sprintf "reference {%s} names no token" path)
        | Some _ when seen |> List.contains path -> Error(sprintf "reference cycle: %s" (String.concat " -> " (List.rev (path :: seen))))
        | Some(kind, value) ->
            match value with
            | JString text when text.StartsWith "{" && text.EndsWith "}" ->
                let target = text.Substring(1, text.Length - 2)

                match resolveToken all (path :: seen) target, kind, all |> Map.tryFind target with
                | Error e, _, _ -> Error e
                | Ok v, Some declared, Some(Some targetKind, _) when declared <> targetKind ->
                    Error(sprintf "%s: a %s token refers to the %s token {%s}" path declared targetKind target)
                | Ok v, _, _ -> Ok v
            | _ ->
                match kind, value with
                | Some "number", JNumber v -> Ok(NumberToken v)
                | Some "color", JObject colour ->
                    let field name = colour |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

                    match field "hex" with
                    | Some(JString h) ->
                        match ThemeColour.ofHex h with
                        | Some c -> Ok(ColourToken c)
                        | None -> Error(sprintf "%s: hex %s is not #rrggbb" path h)
                    | _ ->
                        match field "colorSpace", field "components" with
                        | Some(JString "oklch"), Some(JArray [ JNumber l; JNumber c; JNumber h ]) ->
                            Ok(ColourToken(ThemeColour.ofOklch { L = l; C = c; H = h }))
                        | Some(JString "srgb"), Some(JArray [ JNumber r; JNumber g; JNumber b ]) ->
                            Ok(
                                ColourToken
                                    {
                                        Red = ThemeColour.quantise r
                                        Green = ThemeColour.quantise g
                                        Blue = ThemeColour.quantise b
                                        Alpha = 255uy
                                    }
                            )
                        | _ -> Error(sprintf "%s: a colour needs a hex, or oklch or srgb components" path)
                | None, _ -> Error(sprintf "%s: no $type on the token or its groups" path)
                | Some kind, _ -> Error(sprintf "%s: only color and number tokens are read, not %s" path kind)

    let ofDtcg (algorithms: ThemeAlgorithm list) (json: string) =
        let refuse reason = Error [ ThemeViolation("dtcg", reason) ]

        match parse json with
        | Error e -> refuse e
        | Ok(JObject members as document) ->
            let member' name = members |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

            let extension =
                match member' "$extensions" with
                | Some(JObject ext) -> ext |> List.tryPick (fun (k, v) -> if k = "gg.fs.theme" then Some v else None)
                | _ -> None

            let field name =
                match extension with
                | Some(JObject ext) -> ext |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
                | _ -> None

            let name = match field "name" with Some(JString s) -> s | _ -> "imported"

            let wanted =
                match field "algorithms" with
                | Some(JArray items) -> items |> List.choose (function JString s -> Some s | _ -> None)
                | _ -> [ "standard" ]

            let numberOverrides =
                match field "numberOverrides" with
                | Some(JObject entries) -> entries |> List.choose (fun (k, v) -> match v with JNumber n -> Some(k, n) | _ -> None)
                | _ -> []

            let found = wanted |> List.map (fun n -> n, algorithms |> List.tryFind (fun a -> a.Name = n))

            match found |> List.tryFind (fun (_, a) -> a.IsNone) with
            | Some(n, _) -> refuse (sprintf "algorithm %s is not among those given" n)
            | None ->
                let all = collect "" None document |> List.map (fun (path, kind, value) -> path, (kind, value)) |> Map.ofList

                let read (group: string) =
                    all
                    |> Map.toList
                    |> List.filter (fun (path, _) -> path.StartsWith(group + "."))
                    |> List.map (fun (path, _) -> path.Substring(group.Length + 1), resolveToken all [] path)

                let failures = (read "seed" @ read "alias") |> List.choose (fun (_, r) -> match r with Error e -> Some e | Ok _ -> None)

                if not failures.IsEmpty then
                    Error(failures |> List.map (fun e -> ThemeViolation("dtcg", e)))
                else
                    let ok entries = entries |> List.choose (fun (k, r) -> match r with Ok v -> Some(k, v) | Error _ -> None)
                    let seed = ThemeSeed(name, Map.ofList (ok (read "seed")))

                    // Colour aliases come back as overrides (hex preferred). Size aliases are derived again from the seed
                    // (`cell` x `density`, `strokeBase`, `gap`), so a resolver's `compact` context, which sets the seed's
                    // `density`, changes them; a size the theme had overridden travels in `numberOverrides`.
                    let colourOverrides =
                        ok (read "alias")
                        |> List.choose (fun (k, v) -> match v with ColourToken _ -> Some(ThemeOverride(k, v)) | NumberToken _ -> None)

                    let sizeOverrides = numberOverrides |> List.map (fun (k, v) -> ThemeOverride(k, NumberToken v))
                    create seed (found |> List.map (snd >> Option.get)) (colourOverrides @ sizeOverrides)
        | Ok _ -> refuse "a DTCG file is an object"

    let classic =
        // Baked at build time (ADR D11): the resolved alias values are committed data, never derived when the package
        // loads. A test derives the classic seed afresh and requires the same values.
        let overrides =
            ThemeBaked.classic
            |> List.map (fun (alias, value) ->
                match ThemeColour.ofHex value with
                | Some c -> ThemeOverride(alias, ColourToken c)
                | None -> ThemeOverride(alias, NumberToken(float value)))

        let aliases = overrides |> List.map (fun o -> o.Token, o.Value)
        let seed = ThemeSeed.classic

        let colour alias =
            match aliases |> List.find (fun (a, _) -> a = alias) |> snd with
            | ColourToken c -> c
            | NumberToken _ -> failwith alias

        let number alias =
            match aliases |> List.find (fun (a, _) -> a = alias) |> snd with
            | NumberToken v -> v
            | ColourToken _ -> failwith alias

        let teams = aliases |> List.filter (fun (a, _) -> a.StartsWith "glyph.team.") |> List.map (fst >> colour)
        let seedNumber key = ThemeSeed.tryNumber key seed |> Option.get

        let palette =
            {
                Ground = colour "surface.ground"
                Body = colour "glyph.unit.body"
                Own = colour "glyph.allegiance.own"
                Ally = colour "glyph.allegiance.ally"
                Neutral = colour "glyph.allegiance.neutral"
                Enemy = colour "glyph.allegiance.enemy"
                Unknown = colour "glyph.allegiance.unknown"
                Teams = teams
                BadgeInk = colour "glyph.badge.ink"
                IconInk = colour "glyph.icon.ink"
                Selection = colour "glyph.selection"
                Marker = colour "glyph.marker"
                Path = colour "glyph.path"
                Stale = colour "glyph.stale"
                FocusOuter = colour "cue.focus.outer"
                FocusInner = colour "cue.focus.inner"
                Hover = colour "cue.hover"
                Good = colour "status.good"
                Warn = colour "status.warn"
                Bad = colour "status.bad"
                Track = colour "status.track"
                Keyline = colour "status.keyline"
                ModifierInk = colour "glyph.modifier"
                TerrainOpen = colour "terrain.open"
                TerrainBlocked = colour "terrain.blocked"
                TerrainDifficult = colour "terrain.difficult"
                TerrainWater = colour "terrain.water"
                TerrainVoid = colour "terrain.void"
                Cell = number "size.cell"
                Band = number "stroke.outline"
                Gap = number "stroke.gap"
                Bracket = number "stroke.bracket"
                HoverWidth = number "stroke.hover"
                MinCssPerUnit = seedNumber "minCssPerUnit"
                HueCapacity = [ for i in 0..3 -> int (seedNumber (sprintf "hueCapacity.%d" i)) ]
            }

        // The map layer is the baked aliases' sources plus the seed numbers: enough for `tokens`, and a theme built
        // from it by `withOverrides` derives again from the seed.
        Theme(seed, [ ThemeAlgorithm.standard ], [], ThemeTokens(seed.Values), aliases, palette, [])

    let presets = [ classic ]
