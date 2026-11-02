namespace FS.GG.Game.Reference.Experimental

open FS.GG.UI.Scene

/// EXPERIMENTAL (FS.GG.Game#795, GAME-RENDER-02.8). The colour science under the theme machinery the design
/// `docs/roadmaps/game-render-02-themes.md` proposes: OKLab and OKLCH, CSS Color 4 gamut mapping, half-up 8-bit
/// quantisation, WCAG 2 contrast on 8-bit values through a committed table, CIELAB and CIEDE2000, and Machado 2009
/// colour-vision simulation. Everything is F#, so .NET and Fable derive the same bytes (glyph ADR D11). Not a contract:
/// the implementation lands in FS.GG.Game.Render after #789's types.
///
/// Sources (published facts and formulas, cited; no code is ported):
/// - OKLab: B. Ottosson, "A perceptual color space for image processing" (2020), and CSS Color 4 section 9;
/// - gamut mapping: CSS Color 4, section 13.2 (binary search on OKLCH chroma with a deltaEOK JND of 0.02);
/// - relative luminance and contrast ratio: WCAG 2.2, SC 1.4.11 and its definitions;
/// - CIEDE2000: Sharma, Wu and Dalal, Color Res. Appl. 30(1), 2005;
/// - colour-vision simulation: Machado, Oliveira and Fernandes, IEEE TVCG 15(6), 2009, severity 1.0 matrices,
///   applied in linear RGB.
module Colour =

    /// An OKLCH colour: L in 0..1, chroma C, hue h in degrees.
    type Oklch = { L: float; C: float; H: float }

    /// An OKLab colour.
    type Oklab = { L: float; A: float; B: float }

    /// An opaque 8-bit sRGB colour.
    let rgb (r: int) (g: int) (b: int) : Color =
        { Red = byte r; Green = byte g; Blue = byte b; Alpha = 255uy }

    /// The committed sRGB -> linear table, one entry per 8-bit value (generated once from the sRGB transfer function
    /// and committed as literals, so validation never calls `pow` and a ratio near a floor cannot pass on one runtime
    /// and fail on another; glyph ADR D11).
    let srgbToLinear: float[] =
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

    /// The real cube root: `Math.Cbrt` on .NET and `Math.cbrt` in JavaScript (Fable has no mapping for it). The two may
    /// differ in the last ULP, which half-up quantisation absorbs (glyph ADR D11).
    let cbrt (x: float) : float =
#if FABLE_COMPILER
        Fable.Core.JsInterop.emitJsExpr x "Math.cbrt($0)"
#else
        System.Math.Cbrt x
#endif

    /// sRGB transfer function, linear -> encoded (0..1, unclamped).
    let private encode (c: float) =
        if c <= 0.0031308 then 12.92 * c else 1.055 * (c ** (1.0 / 2.4)) - 0.055

    /// sRGB transfer function, encoded -> linear (for values that are not 8-bit).
    let private decode (c: float) =
        if c <= 0.04045 then c / 12.92 else ((c + 0.055) / 1.055) ** 2.4

    /// Quantise one encoded channel to 8 bits: clamp `v = c * 255` to [0, 255], then `Floor(v + 0.5)`. Never `Floor(v)`
    /// (94.8 % of OKLCH round trips land just below the integer) and never `Math.Round` (the runtimes break ties
    /// differently). The #795 correction and glyph ADR D11.
    let quantise (c: float) : byte =
        let v = c * 255.0
        let v = if v < 0.0 then 0.0 elif v > 255.0 then 255.0 else v
        byte (System.Math.Floor(v + 0.5))

    /// The rule the research first proposed and the #794 review measured as wrong. Kept only so a test can show the
    /// failure rate it causes.
    let quantiseFloor (c: float) : byte =
        let v = c * 255.0
        let v = if v < 0.0 then 0.0 elif v > 255.0 then 255.0 else v
        byte (System.Math.Floor v)

    let linearOf (colour: Color) =
        srgbToLinear.[int colour.Red], srgbToLinear.[int colour.Green], srgbToLinear.[int colour.Blue]

    let oklabOfLinear (r: float, g: float, b: float) : Oklab =
        let l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
        let m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
        let s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
        let l', m', s' = cbrt l, cbrt m, cbrt s
        { L = 0.2104542553 * l' + 0.7936177850 * m' - 0.0040720468 * s'
          A = 1.9779984951 * l' - 2.4285922050 * m' + 0.4505937099 * s'
          B = 0.0259040371 * l' + 0.7827717662 * m' - 0.8086757660 * s' }

    let linearOfOklab (lab: Oklab) =
        let l' = lab.L + 0.3963377774 * lab.A + 0.2158037573 * lab.B
        let m' = lab.L - 0.1055613458 * lab.A - 0.0638541728 * lab.B
        let s' = lab.L - 0.0894841775 * lab.A - 1.2914855480 * lab.B
        let l, m, s = l' * l' * l', m' * m' * m', s' * s' * s'
        4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s

    let oklchOfOklab (lab: Oklab) : Oklch =
        let c = sqrt (lab.A * lab.A + lab.B * lab.B)
        let h = System.Math.Atan2(lab.B, lab.A) * 180.0 / System.Math.PI
        { L = lab.L; C = c; H = (if h < 0.0 then h + 360.0 else h) }

    let oklabOfOklch (lch: Oklch) : Oklab =
        let h = lch.H * System.Math.PI / 180.0
        { L = lch.L; A = lch.C * cos h; B = lch.C * sin h }

    let oklchOf (colour: Color) : Oklch = oklchOfOklab (oklabOfLinear (linearOf colour))
    let oklabOf (colour: Color) : Oklab = oklabOfLinear (linearOf colour)

    let private inGamut (r: float, g: float, b: float) =
        let ok v = v >= -1e-9 && v <= 1.0 + 1e-9
        ok r && ok g && ok b

    let private clip01 v = if v < 0.0 then 0.0 elif v > 1.0 then 1.0 else v

    /// Quantise a linear-RGB triple (clamped) to an 8-bit colour.
    let ofLinear (r: float, g: float, b: float) : Color =
        { Red = quantise (encode (clip01 r)); Green = quantise (encode (clip01 g)); Blue = quantise (encode (clip01 b)); Alpha = 255uy }

    let private deltaEOK (a: Oklab) (b: Oklab) =
        let dl, da, db = a.L - b.L, a.A - b.A, a.B - b.B
        sqrt (dl * dl + da * da + db * db)

    /// CSS Color 4 gamut mapping to sRGB (section 13.2): reduce chroma at constant L and h by binary search until the
    /// clipped colour is within a deltaEOK JND (0.02) of the unclipped one. Returns linear RGB, clipped to [0, 1].
    let gamutMap (lch: Oklch) : float * float * float =
        let clipped (lin: float * float * float) =
            let r, g, b = lin
            clip01 r, clip01 g, clip01 b
        if lch.L >= 1.0 then 1.0, 1.0, 1.0
        elif lch.L <= 0.0 then 0.0, 0.0, 0.0
        else
            let origin = linearOfOklab (oklabOfOklch lch)
            if inGamut origin then clipped origin
            else
                let jnd, eps = 0.02, 0.0001
                let toLab lin = oklabOfLinear lin
                let mutable current = lch
                let mutable clip = clipped origin
                let first = deltaEOK (toLab clip) (oklabOfOklch current)
                if first < jnd then clip
                else
                    let mutable lo = 0.0
                    let mutable hi = lch.C
                    let mutable loInGamut = true
                    let mutable result = None
                    while result.IsNone && hi - lo > eps do
                        let chroma = (lo + hi) / 2.0
                        current <- { lch with C = chroma }
                        let lin = linearOfOklab (oklabOfOklch current)
                        if loInGamut && inGamut lin then lo <- chroma
                        else
                            clip <- clipped lin
                            let e = deltaEOK (toLab clip) (oklabOfOklch current)
                            if e < jnd then
                                if jnd - e < eps then result <- Some clip
                                else
                                    loInGamut <- false
                                    lo <- chroma
                            else hi <- chroma
                    match result with
                    | Some c -> c
                    | None -> clip

    /// An OKLCH colour, gamut-mapped and quantised half up to 8-bit sRGB.
    let ofOklch (lch: Oklch) : Color = ofLinear (gamutMap lch)

    /// WCAG 2 relative luminance of an 8-bit colour, through the committed table.
    let luminance (colour: Color) : float =
        let r, g, b = linearOf colour
        0.2126 * r + 0.7152 * g + 0.0722 * b

    /// WCAG 2 contrast ratio, 1 to 21, on the 8-bit values that are drawn.
    let contrast (a: Color) (b: Color) : float =
        let la, lb = luminance a, luminance b
        (max la lb + 0.05) / (min la lb + 0.05)

    /// Which side of the ground a solved colour lies on.
    type Polarity =
        | Lighter
        | Darker

    /// Leonardo-style contrast solving (Adobe Leonardo, Apache-2.0; the idea, not its code): the colour of hue `h` and
    /// chroma `c` whose contrast against `ground` is at least `target`, as close to the ground's lightness as that
    /// allows, found by bisecting OKLCH lightness on the gamut-mapped, quantised colour. `None` when no lightness on that
    /// side reaches the target.
    let solve (ground: Color) (polarity: Polarity) (target: float) (c: float) (h: float) : Color option =
        let gl = (oklchOf ground).L
        let at l = ofOklch { L = l; C = c; H = h }
        let meets l = contrast (at l) ground >= target
        let far = match polarity with Lighter -> 1.0 | Darker -> 0.0
        if not (meets far) then None
        else
            let mutable near = gl
            let mutable reach = far
            for _ in 1..48 do
                let mid = (near + reach) / 2.0
                if meets mid then reach <- mid else near <- mid
            Some(at reach)

    // CIELAB (D65) and CIEDE2000.

    type Lab = { L: float; A: float; B: float }

    let labOfLinear (r: float, g: float, b: float) : Lab =
        let x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047
        let y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b
        let z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883
        let d = 6.0 / 29.0
        let f t = if t > d * d * d then cbrt t else t / (3.0 * d * d) + 4.0 / 29.0
        let fx, fy, fz = f x, f y, f z
        { L = 116.0 * fy - 16.0; A = 500.0 * (fx - fy); B = 200.0 * (fy - fz) }

    let labOf (colour: Color) : Lab = labOfLinear (linearOf colour)

    /// CIEDE2000 colour difference (kL = kC = kH = 1), Sharma, Wu and Dalal (2005).
    let deltaE2000 (x: Lab) (y: Lab) : float =
        let rad d = d * System.Math.PI / 180.0
        let deg r = r * 180.0 / System.Math.PI
        let c1 = sqrt (x.A * x.A + x.B * x.B)
        let c2 = sqrt (y.A * y.A + y.B * y.B)
        let cBar = (c1 + c2) / 2.0
        let c7 = cBar ** 7.0
        let g = 0.5 * (1.0 - sqrt (c7 / (c7 + 6103515625.0)))
        let a1, a2 = (1.0 + g) * x.A, (1.0 + g) * y.A
        let c1', c2' = sqrt (a1 * a1 + x.B * x.B), sqrt (a2 * a2 + y.B * y.B)
        let hue (b: float) (a: float) =
            if a = 0.0 && b = 0.0 then 0.0
            else
                let h = deg (System.Math.Atan2(b, a))
                if h < 0.0 then h + 360.0 else h
        let h1, h2 = hue x.B a1, hue y.B a2
        let dL = y.L - x.L
        let dC = c2' - c1'
        let dh =
            if c1' * c2' = 0.0 then 0.0
            elif abs (h2 - h1) <= 180.0 then h2 - h1
            elif h2 - h1 > 180.0 then h2 - h1 - 360.0
            else h2 - h1 + 360.0
        let dH = 2.0 * sqrt (c1' * c2') * sin (rad (dh / 2.0))
        let lBar = (x.L + y.L) / 2.0
        let cBar' = (c1' + c2') / 2.0
        let hBar =
            if c1' * c2' = 0.0 then h1 + h2
            elif abs (h1 - h2) <= 180.0 then (h1 + h2) / 2.0
            elif h1 + h2 < 360.0 then (h1 + h2 + 360.0) / 2.0
            else (h1 + h2 - 360.0) / 2.0
        let t =
            1.0 - 0.17 * cos (rad (hBar - 30.0)) + 0.24 * cos (rad (2.0 * hBar)) + 0.32 * cos (rad (3.0 * hBar + 6.0))
            - 0.20 * cos (rad (4.0 * hBar - 63.0))
        let dTheta = 30.0 * exp (-(((hBar - 275.0) / 25.0) ** 2.0))
        let cBar7 = cBar' ** 7.0
        let rc = 2.0 * sqrt (cBar7 / (cBar7 + 6103515625.0))
        let l50 = (lBar - 50.0) * (lBar - 50.0)
        let sl = 1.0 + 0.015 * l50 / sqrt (20.0 + l50)
        let sc = 1.0 + 0.045 * cBar'
        let sh = 1.0 + 0.015 * cBar' * t
        let rt = -sin (rad (2.0 * dTheta)) * rc
        sqrt ((dL / sl) ** 2.0 + (dC / sc) ** 2.0 + (dH / sh) ** 2.0 + rt * (dC / sc) * (dH / sh))

    /// Colour vision: normal, or a dichromacy simulated at Machado severity 1.0.
    type Vision =
        | Normal
        | Protan
        | Deutan
        | Tritan

    let visions = [ Normal; Protan; Deutan; Tritan ]

    let private machado (vision: Vision) =
        match vision with
        | Normal -> [| 1.0; 0.0; 0.0; 0.0; 1.0; 0.0; 0.0; 0.0; 1.0 |]
        | Protan -> [| 0.152286; 1.052583; -0.204868; 0.114503; 0.786281; 0.099216; -0.003882; -0.048116; 1.051998 |]
        | Deutan -> [| 0.367322; 0.860646; -0.227968; 0.280085; 0.672501; 0.047413; -0.011820; 0.042940; 0.968881 |]
        | Tritan -> [| 1.255528; -0.076749; -0.178779; -0.078411; 0.930809; 0.147602; 0.004733; 0.691367; 0.303900 |]

    /// The linear RGB a viewer with `vision` sees for `colour` (Machado 2009, severity 1.0, in linear RGB, clamped).
    let simulate (vision: Vision) (colour: Color) : float * float * float =
        let r, g, b = linearOf colour
        let m = machado vision
        clip01 (m.[0] * r + m.[1] * g + m.[2] * b), clip01 (m.[3] * r + m.[4] * g + m.[5] * b), clip01 (m.[6] * r + m.[7] * g + m.[8] * b)

    /// CIEDE2000 between two colours as a viewer with `vision` sees them.
    let deltaEUnder (vision: Vision) (a: Color) (b: Color) : float =
        deltaE2000 (labOfLinear (simulate vision a)) (labOfLinear (simulate vision b))

    /// OKLab lightness difference between two colours as a viewer with `vision` sees them.
    let lightnessGapUnder (vision: Vision) (a: Color) (b: Color) : float =
        abs ((oklabOfLinear (simulate vision a)).L - (oklabOfLinear (simulate vision b)).L)

    let private hexDigit (n: int) = "0123456789abcdef".[n]

    /// `#rrggbb`.
    let hex (colour: Color) : string =
        let two (v: byte) = System.String [| hexDigit (int v / 16); hexDigit (int v % 16) |]
        "#" + two colour.Red + two colour.Green + two colour.Blue

    /// Parse `#rrggbb` (case-insensitive).
    let ofHex (text: string) : Color option =
        let digit (ch: char) =
            let c = System.Char.ToLowerInvariant ch
            if c >= '0' && c <= '9' then Some(int c - int '0')
            elif c >= 'a' && c <= 'f' then Some(int c - int 'a' + 10)
            else None
        if text.Length <> 7 || text.[0] <> '#' then None
        else
            let ds = [ for i in 1..6 -> digit text.[i] ]
            if ds |> List.exists Option.isNone then None
            else
                let d = ds |> List.map Option.get
                Some(rgb (d.[0] * 16 + d.[1]) (d.[2] * 16 + d.[3]) (d.[4] * 16 + d.[5]))

    /// A float with a fixed number of decimals, half up, formatted identically on .NET and through Fable (for DTCG
    /// components, which are informational: import takes the hex).
    let fixedText (decimals: int) (value: float) : string =
        let scale = 10.0 ** float decimals
        let scaled = System.Math.Floor(abs value * scale + 0.5)
        let whole = System.Math.Floor(scaled / scale)
        let frac = scaled - whole * scale
        let fracText = string (int64 frac)
        let padded = if decimals > fracText.Length then System.String('0', decimals - fracText.Length) + fracText else fracText
        (if value < 0.0 && scaled > 0.0 then "-" else "") + string (int64 whole) + (if decimals > 0 then "." + padded else "")
