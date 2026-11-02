namespace FS.GG.Game.Reference.Experimental

open FS.GG.UI.Scene
open FS.GG.Game.Render

/// EXPERIMENTAL (FS.GG.Game#812, GAME-RENDER-02.10): theme motion tokens and deterministic, presentation-only animation
/// for glyphs, cues and overlays, as `docs/roadmaps/game-render-02-motion.md` proposes. Not packed, and it does not
/// constrain the API.
///
/// **The rule.** A frame is a pure function of (game state, presentation state, render time). Render time is whole
/// milliseconds that the host passes in for each Present (a `requestAnimationFrame` timestamp, floored) or that a
/// replay supplies from its sample list. Nothing here reads a clock, and nothing here feeds back into game state, so
/// lockstep and replays are unaffected, and the frame at time t is the same Scene on every backend and every run.
///
/// **Determinism.** Every animated value is computed from integers (elapsed milliseconds, phases by integer modulo),
/// IEEE basic operations (the cubic Bézier bisection uses only `+`, `-`, `*` and `/`, which are correctly rounded and
/// identical on .NET and in JavaScript), and is then quantised before it reaches the Scene: eased progress to whole
/// permille, positions to whole units, colours by integer mixing of 8-bit channels. No `Math.Round` (glyph ADR D11).
///
/// **Ownership.** #802 owns the `Theme` token type, so the motion tokens here are a STAND-IN (`MotionTokens`) carried
/// beside a #802 `Theme`; the design proposes them as additions to `ThemeSeed`, `ThemeTokens` and the alias set. #810
/// owns overlay semantics: the plan arrow here is a proof drawing, not #810's vocabulary. #813 owns effect tokens.
module Motion =

    // ---- Render time ------------------------------------------------------------------------------------------------

    /// Render time, in whole milliseconds, supplied by the host or a replay. Never read from a clock in the library.
    type RenderTime = int

    // ---- Tokens: STAND-IN for proposed #802 additions (ThemeSeed numbers, map tokens and aliases) ---------------------

    /// A DTCG `cubicBezier` value: P1 = (X1, Y1), P2 = (X2, Y2); X1 and X2 lie in [0, 1].
    type CubicBezier = { X1: float; Y1: float; X2: float; Y2: float }

    /// One motion token value, by DTCG type.
    type MotionValue =
        /// DTCG `duration`, held in whole milliseconds.
        | Duration of int
        /// DTCG `cubicBezier`.
        | Easing of CubicBezier
        /// DTCG `number`.
        | Number of float

    /// Resolved motion tokens: the seed, then the derived map and aliases, and the algorithms that produced them.
    type MotionTokens = { Name: string; Values: Map<string, MotionValue>; Algorithms: string list }

    /// A motion algorithm, `tokens -> tokens`, composable in a list like #802's `ThemeAlgorithm`.
    type MotionAlgorithm = { AlgorithmName: string; Refine: MotionTokens -> MotionTokens }

    /// The seed names: the time unit and base (Ant Design's `motionUnit` and `motionBase`) and the pulse amplitude.
    let seedNames = [ "motion.unit"; "motion.base"; "motion.amplitude.pulse" ]

    /// The alias names glyph, cue and overlay code reads. Closed, like #802's alias set.
    let aliasNames =
        [
            "motion.duration.instant"
            "motion.duration.fast"
            "motion.duration.mid"
            "motion.duration.slow"
            "motion.easing.standard"
            "motion.easing.enter"
            "motion.easing.exit"
            "motion.easing.inOut"
            "motion.easing.linear"
            "motion.period.pulse"
            "motion.period.march"
            "motion.amplitude.pulse"
        ]

    let private bezier x1 y1 x2 y2 = Easing { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2 }

    /// The default seed: a 100 ms unit and base 0 (Ant Design v5's defaults), and an OKLab lightness amplitude of 0.12
    /// for the selection pulse.
    let seed: MotionTokens =
        {
            Name = "standard"
            Values =
                Map.ofList
                    [
                        "motion.unit", Duration 100
                        "motion.base", Duration 0
                        "motion.amplitude.pulse", Number 0.12
                    ]
            Algorithms = []
        }

    let duration (name: string) (tokens: MotionTokens) : int =
        match tokens.Values.TryFind name with
        | Some(Duration ms) -> ms
        | _ -> failwithf "motion token %s is not a duration" name

    let easing (name: string) (tokens: MotionTokens) : CubicBezier =
        match tokens.Values.TryFind name with
        | Some(Easing b) -> b
        | _ -> failwithf "motion token %s is not a cubicBezier" name

    let number (name: string) (tokens: MotionTokens) : float =
        match tokens.Values.TryFind name with
        | Some(Number n) -> n
        | _ -> failwithf "motion token %s is not a number" name

    let private set name value (tokens: MotionTokens) = { tokens with Values = tokens.Values.Add(name, value) }

    /// The base algorithm: durations as `base + n x unit` (Ant Design's derivation), the easing curves (Material 3's
    /// standard, decelerate and accelerate, and Ant Design's ease-in-out), and the loop periods: the pulse 12 units and
    /// the march 8 units.
    let standard: MotionAlgorithm =
        {
            AlgorithmName = "standard"
            Refine =
                fun tokens ->
                    let u = duration "motion.unit" tokens
                    let b = duration "motion.base" tokens
                    tokens
                    |> set "motion.duration.instant" (Duration 0)
                    |> set "motion.duration.fast" (Duration(b + u))
                    |> set "motion.duration.mid" (Duration(b + 2 * u))
                    |> set "motion.duration.slow" (Duration(b + 3 * u))
                    |> set "motion.easing.standard" (bezier 0.2 0.0 0.0 1.0)
                    |> set "motion.easing.enter" (bezier 0.0 0.0 0.0 1.0)
                    |> set "motion.easing.exit" (bezier 0.3 0.0 1.0 1.0)
                    |> set "motion.easing.inOut" (bezier 0.645 0.045 0.355 1.0)
                    |> set "motion.easing.linear" (bezier 0.0 0.0 1.0 1.0)
                    |> set "motion.period.pulse" (Duration(12 * u))
                    |> set "motion.period.march" (Duration(8 * u))
                    |> set "motion.amplitude.pulse" (Number(number "motion.amplitude.pulse" tokens))
        }

    /// Reduced motion (WCAG 2.3.3, `prefers-reduced-motion: reduce`): every decorative motion settles. Transition
    /// durations become 0, so a change shows its end state at once; loop periods become 0, so loops hold their rest
    /// phase; the pulse amplitude becomes 0. Composable after any preset, like `highContrast`.
    let reducedMotion: MotionAlgorithm =
        {
            AlgorithmName = "reducedMotion"
            Refine =
                fun tokens ->
                    tokens
                    |> set "motion.duration.fast" (Duration 0)
                    |> set "motion.duration.mid" (Duration 0)
                    |> set "motion.duration.slow" (Duration 0)
                    |> set "motion.period.pulse" (Duration 0)
                    |> set "motion.period.march" (Duration 0)
                    |> set "motion.amplitude.pulse" (Number 0.0)
        }

    /// Resolve: run the algorithms over the seed, in order.
    let create (seed: MotionTokens) (algorithms: MotionAlgorithm list) : MotionTokens =
        algorithms
        |> List.fold (fun tokens a -> { a.Refine tokens with Algorithms = tokens.Algorithms @ [ a.AlgorithmName ] }) seed

    let full = create seed [ standard ]
    let reduced = create seed [ standard; reducedMotion ]

    // ---- Easing: a deterministic cubic Bézier ----------------------------------------------------------------------

    let private curve (p1: float) (p2: float) (t: float) =
        let u = 1.0 - t
        3.0 * u * u * t * p1 + 3.0 * u * t * t * p2 + t * t * t

    /// Eased progress, in whole permille, of `elapsed` ms into a transition of `total` ms. 0 before it starts, 1000 at
    /// and after its end, and 1000 at once when `total` is 0 (a settled, reduced-motion transition). Between, x is
    /// solved by 40 bisection steps (X1 and X2 in [0, 1] make x(t) monotone), y is read at the midpoint, and the result
    /// is `Floor(y x 1000 + 0.5)`. Only basic IEEE operations: the same permille on .NET and through Fable.
    let easePermille (b: CubicBezier) (elapsed: int) (total: int) : int =
        if total <= 0 || elapsed >= total then 1000
        elif elapsed <= 0 then 0
        else
            let x = float elapsed / float total
            let mutable lo = 0.0
            let mutable hi = 1.0
            for _ in 1..40 do
                let mid = (lo + hi) * 0.5
                if curve b.X1 b.X2 mid < x then lo <- mid else hi <- mid
            let y = curve b.Y1 b.Y2 ((lo + hi) * 0.5)
            int (System.Math.Floor(y * 1000.0 + 0.5))

    /// Each 8-bit channel of `a` mixed towards `b` by `k` permille, in integers: `(a x 1000 + (b - a) x k + 500) / 1000`.
    let mix (a: Color) (b: Color) (k: int) : Color =
        let k = max 0 (min 1000 k)
        let ch (x: byte) (y: byte) = byte ((int x * 1000 + (int y - int x) * k + 500) / 1000)
        { Red = ch a.Red b.Red; Green = ch a.Green b.Green; Blue = ch a.Blue b.Blue; Alpha = 255uy }

    // ---- Presentation state ------------------------------------------------------------------------------------------

    /// A value easing from `From` to `To`, starting at render time `Since`.
    type Transition = { From: float; To: float; Since: RenderTime }

    /// Presentation state: never game state, never sent, never saved. Animation start stamps are render times, taken
    /// at the first Present that saw the change, so a replay given the same state sequence and the same time samples
    /// derives the same stamps. It lives with the game's view, not with a backend, so it survives a backend switch.
    type Presentation = { SelectedSince: RenderTime option; PlanSince: RenderTime option; Health: Transition }

    /// The value a transition shows at `t`, in `motion.duration.slow` with `motion.easing.standard`.
    let transitionAt (tokens: MotionTokens) (tr: Transition) (t: RenderTime) : float =
        let k = easePermille (easing "motion.easing.standard" tokens) (t - tr.Since) (duration "motion.duration.slow" tokens)
        if k >= 1000 then tr.To
        elif k <= 0 then tr.From
        else tr.From + (tr.To - tr.From) * float k / 1000.0

    /// Retarget: a new value starts from the value shown at `t`, never snapping back (FS.GG.UI's
    /// `AnimationState.retarget` idea, on absolute render time instead of accumulated deltas).
    let retarget (tokens: MotionTokens) (target: float) (t: RenderTime) (tr: Transition) : Transition =
        if target = tr.To then tr else { From = transitionAt tokens tr t; To = target; Since = t }

    /// Fold one Present into presentation state: `selected` and `health` are the game state the frame shows.
    let observe (tokens: MotionTokens) (selected: bool) (planning: bool) (health: float) (t: RenderTime) (p: Presentation) =
        {
            SelectedSince = (if selected then (match p.SelectedSince with Some s -> Some s | None -> Some t) else None)
            PlanSince = (if planning then (match p.PlanSince with Some s -> Some s | None -> Some t) else None)
            Health = retarget tokens health t p.Health
        }

    // ---- What animates ----------------------------------------------------------------------------------------------

    let private gridL (x: float) = System.Math.Floor(x * 1e6 + 0.5) / 1e6

    let private aliasColour (alias: string) (theme: Theme) =
        match Theme.tryColour alias theme with
        | Some c -> c
        | None -> failwithf "the theme has no colour alias %s" alias

    /// The selection pulse's peak: `glyph.selection` moved `motion.amplitude.pulse` in OKLab lightness away from the
    /// ground (so contrast with the ground only grows), gamut-mapped and quantised by #802's `ThemeColour`. On #802 this
    /// is a map token the motion algorithm writes and the theme validates.
    let pulsePeak (theme: Theme) (tokens: MotionTokens) : Color =
        let selection = aliasColour "glyph.selection" theme
        let amplitude = number "motion.amplitude.pulse" tokens
        if amplitude = 0.0 then selection
        else
            let s = ThemeColour.toOklch selection
            let g = ThemeColour.toOklch (aliasColour "surface.ground" theme)
            let l = if gridL s.L >= gridL g.L then min 1.0 (gridL s.L + amplitude) else max 0.0 (gridL s.L - amplitude)
            let h = if s.C < 0.001 then 0.0 else System.Math.Floor(s.H * 1e4 + 0.5) / 1e4
            ThemeColour.ofOklch { L = l; C = gridL s.C; H = h }

    /// The pulse's mix, in permille, at `elapsed` ms after selection: up over the first half period and down over the
    /// second, each half eased by `motion.easing.inOut`. 0 (rest) at the start of every period and whenever the period
    /// is 0 (reduced motion).
    let pulsePermille (tokens: MotionTokens) (elapsed: int) : int =
        let period = duration "motion.period.pulse" tokens
        if period <= 0 || elapsed <= 0 then 0
        else
            let phase = elapsed % period
            let half = period / 2
            let e = easing "motion.easing.inOut" tokens
            if phase < half then easePermille e phase half else 1000 - easePermille e (phase - half) (period - half)

    /// The bracket colour at `elapsed`.
    let pulseColour (theme: Theme) (tokens: MotionTokens) (elapsed: int) : Color =
        mix (aliasColour "glyph.selection" theme) (pulsePeak theme tokens) (pulsePermille tokens elapsed)

    /// The plan arrow's dash pattern, in whole units: `Dash` on, then off, over a cycle of `Cycle`.
    let dash, cycle = 5, 8

    /// How far the dashes have marched, in whole units (0 to `cycle - 1`): one cycle per `motion.period.march`, towards
    /// the arrowhead. 0 whenever the period is 0 (reduced motion).
    let marchOffset (tokens: MotionTokens) (elapsed: int) : int =
        let period = duration "motion.period.march" tokens
        if period <= 0 || elapsed <= 0 then 0 else (elapsed % period) * cycle / period

    let private box (x: float) (y: float) (w: float) (h: float) (c: Color) = SceneNode.Rectangle((x, y, w, h), c)

    /// A marching plan arrow through axis-aligned whole-unit points, 2 units wide, in `glyph.path`. The shaft is the
    /// dashes; the stepped arrowhead is static and carries the direction, so the motion carries no information the
    /// still frame lacks.
    let arrow (theme: Theme) (offset: int) (points: (int * int) list) : SceneNode list =
        let colour = aliasColour "glyph.path" theme
        let on u = ((u - offset) % cycle + cycle) % cycle < dash
        let shaft =
            points
            |> List.pairwise
            |> List.fold
                (fun (u0, acc) ((x0, y0), (x1, y1)) ->
                    let dx, dy = sign (x1 - x0), sign (y1 - y0)
                    let n = abs (x1 - x0) + abs (y1 - y0)
                    // Unit cells i = 0 .. n - 1 of this segment, at arc length u0 + i, merged into runs.
                    let runs =
                        [ 0 .. n - 1 ]
                        |> List.fold
                            (fun runs i ->
                                if not (on (u0 + i)) then runs
                                else
                                    match runs with
                                    | (a, b) :: rest when b = i - 1 -> (a, i) :: rest
                                    | _ -> (i, i) :: runs)
                            []
                        |> List.rev
                    let nodes =
                        runs
                        |> List.map (fun (a, b) ->
                            let ax, ay = x0 + dx * a, y0 + dy * a
                            let bx, by = x0 + dx * (b + 1), y0 + dy * (b + 1)
                            let left, right = min ax bx, max ax bx
                            let top, bottom = min ay by, max ay by
                            if dy = 0 then box (float left) (float (y0 - 1)) (float (right - left)) 2.0 colour
                            else box (float (x0 - 1)) (float top) 2.0 (float (bottom - top)) colour)
                    u0 + n, acc @ nodes)
                (0, [])
            |> snd
        let head =
            match List.rev points with
            | (ex, ey) :: (px, py) :: _ ->
                let dx, dy = sign (ex - px), sign (ey - py)
                [
                    for j in 0..5 do
                        let half = 6 - j
                        if dy = 0 then box (float (ex + dx * j - (if dx < 0 then 1 else 0))) (float (ey - half)) 1.0 (float (2 * half)) colour
                        else box (float (ex - half)) (float (ey + dy * j - (if dy < 0 then 1 else 0))) (float (2 * half)) 1.0 colour
                ]
            | _ -> []
        shaft @ head

    /// The focus ring's band width, in scene units, `elapsed` ms after focus became visible: it appears at once at
    /// twice its width and settles to `max(band, 2 / cssPerUnit)` over `motion.duration.fast` with
    /// `motion.easing.exit`, rounded up to a whole CSS px. It never narrows below its settled width, so every frame
    /// keeps ADR-0001's 2 CSS px. Under reduced motion it is the settled width at once.
    let focusBand (tokens: MotionTokens) (band: float) (cssPerUnit: float) (elapsed: int) : float =
        let settled = max band (2.0 / cssPerUnit)
        let k = easePermille (easing "motion.easing.exit" tokens) elapsed (duration "motion.duration.fast" tokens)
        let width = settled + settled * float (1000 - k) / 1000.0
        System.Math.Ceiling(width * cssPerUnit - 1e-9) / cssPerUnit

    // ---- The proof scene ---------------------------------------------------------------------------------------------

    let width, height = 160.0, 104.0

    /// The plan arrow's points: out of the unit's reserve, along, then down to a waypoint.
    let planPoints = [ (52, 52); (128, 52); (128, 82) ]

    let private unitSpec (health: float) =
        GlyphSpec.create GlyphFamily.unit { X = 32.0; Y = 52.0 } { Width = 1; Height = 1 }
        |> GlyphSpec.identity Identity.Own
        |> GlyphSpec.team 0
        |> GlyphSpec.badged
        |> GlyphSpec.amplifier AmplifierSlot.above (Amplifier.bar health)

    /// The frame at render time `t`: the ground, the glyph library's amplifier and body passes, the plan pass (the
    /// arrow), then the bracket pass recoloured by the pulse. Animation never moves a node between passes or changes
    /// their order. The theme and the motion tokens are read through readers each frame, so a switch mid-animation
    /// takes effect at the next frame and the phase, which depends on time alone, carries on.
    let frame (theme: unit -> Theme) (motion: unit -> MotionTokens) (p: Presentation) (t: RenderTime) : Scene =
        let th = theme ()
        let tokens = motion ()
        let health = transitionAt tokens p.Health t
        let selected = p.SelectedSince.IsSome
        let drawing = Glyphs.draw th (Glyph.ofSpec (GlyphState.selected selected GlyphState.idle) (unitSpec health))
        let brackets =
            match p.SelectedSince with
            | Some since ->
                let c = pulseColour th tokens (t - since)
                let rest = aliasColour "glyph.selection" th
                GlyphDrawing.brackets drawing
                |> List.map (function
                    | SceneNode.Rectangle(r, colour) when colour = rest -> SceneNode.Rectangle(r, c)
                    | node -> node)
            | None -> []
        let plan =
            match p.PlanSince with
            | Some since -> arrow th (marchOffset tokens (t - since)) planPoints
            | None -> []
        {
            Nodes =
                box 0.0 0.0 width height (aliasColour "surface.ground" th)
                :: GlyphDrawing.amplifiers drawing
                @ GlyphDrawing.body drawing
                @ plan
                @ brackets
        }

    /// The proof's presentation: selected and planning since t = 0, health easing from 0.9 to 0.35 from t = 0.
    let sample: Presentation =
        { SelectedSince = Some 0; PlanSince = Some 0; Health = { From = 0.9; To = 0.35; Since = 0 } }

    /// The still frame: what the glyph library draws with no motion at all (selected, health 0.35) plus the arrow at
    /// rest. Reduced motion must equal it at every t.
    let still (theme: Theme) : Scene =
        let drawing = Glyphs.draw theme (Glyph.ofSpec (GlyphState.selected true GlyphState.idle) (unitSpec 0.35))
        {
            Nodes =
                box 0.0 0.0 width height (aliasColour "surface.ground" theme)
                :: GlyphDrawing.amplifiers drawing
                @ GlyphDrawing.body drawing
                @ arrow theme 0 planPoints
                @ GlyphDrawing.brackets drawing
        }

    // ---- Checks ------------------------------------------------------------------------------------------------------

    /// The colour the topmost rectangle shows at the centre of pixel (x, y), if any.
    let colourAt (scene: Scene) (x: int) (y: int) : Color option =
        let px, py = float x + 0.5, float y + 0.5
        scene.Nodes
        |> List.choose (function
            | SceneNode.Rectangle((rx, ry, rw, rh), c) when px >= rx && px < rx + rw && py >= ry && py < ry + rh -> Some c
            | _ -> None)
        |> List.tryLast

    /// A digest of the rectangles of each frame at `times`: integers only, the same on .NET and through Fable.
    let digest (frameAt: RenderTime -> Scene) (times: RenderTime list) : string =
        let mutable h = 7.0
        let add (n: int) = h <- (h * 31.0 + float n) % 1000000007.0
        for t in times do
            for node in (frameAt t).Nodes do
                match node with
                | SceneNode.Rectangle((x, y, w, hh), c) ->
                    add (int (x * 4.0)); add (int (y * 4.0)); add (int (w * 4.0)); add (int (hh * 4.0))
                    add (int c.Red); add (int c.Green); add (int c.Blue)
                | _ -> add -1
        string (int h)

    /// WCAG 2.3.1, conservatively: the most general and red flashes in any one second of a colour signal sampled every
    /// millisecond over [0, span), at any area. Neither the area threshold nor the general threshold's exemption for a
    /// darker side at or above 0.80 is credited. A general transition is a change of relative luminance of at least 0.1
    /// from the last extreme. A red transition needs saturated red, `R / (R + G + B) >= 0.8` (linear channels), on one
    /// side, and is counted under either published rule, whichever counts more: WCAG 2.0's (and XAG 118's) change of
    /// `max(0, (R - G - B) x 320)` of more than 20, or WCAG 2.2's chromaticity difference of more than 0.2 in the CIE
    /// 1976 UCS diagram (u', v'). A flash is a pair of opposing transitions.
    let flashes (colourAt: int -> Color) (span: int) : float * float =
        let lin (c: byte) = ThemeColour.linear c
        let lum (c: Color) = ThemeColour.luminance c
        let redness (c: Color) = max 0.0 ((lin c.Red - lin c.Green - lin c.Blue) * 320.0)
        let saturatedRed (c: Color) =
            let s = lin c.Red + lin c.Green + lin c.Blue
            s > 0.0 && lin c.Red / s >= 0.8
        // A zigzag over the signal: a transition is recorded when the value moves `threshold` back from the extreme of
        // the current direction (or, before the first, from the lowest or highest value seen).
        let transitions (value: Color -> float) (counts: Color -> Color -> bool) (threshold: float) =
            let times = ResizeArray<int>()
            let first = colourAt 0
            let mutable dir = 0
            let mutable ext = first
            let mutable lo = first
            let mutable hi = first
            for t in 1 .. span - 1 do
                let c = colourAt t
                let v = value c
                if dir = 0 then
                    if v - value lo >= threshold && counts lo c then
                        times.Add t
                        dir <- 1
                        ext <- c
                    elif value hi - v >= threshold && counts hi c then
                        times.Add t
                        dir <- -1
                        ext <- c
                    else
                        if v < value lo then lo <- c
                        if v > value hi then hi <- c
                elif dir = 1 then
                    if v > value ext then ext <- c
                    elif value ext - v >= threshold && counts ext c then
                        times.Add t
                        dir <- -1
                        ext <- c
                else if v < value ext then ext <- c
                elif v - value ext >= threshold && counts ext c then
                    times.Add t
                    dir <- 1
                    ext <- c
            List.ofSeq times
        let perSecond (times: int list) =
            let arr = Array.ofList times
            let mutable best = 0
            for i in 0 .. arr.Length - 1 do
                let n = arr |> Array.filter (fun x -> x >= arr.[i] && x < arr.[i] + 1000) |> Array.length
                best <- max best n
            float best / 2.0
        // WCAG 2.2: u'v' of the linear colour (D65 white for black, whose chromaticity is undefined).
        let uv (c: Color) =
            let r, g, b = lin c.Red, lin c.Green, lin c.Blue
            let x = 0.4124 * r + 0.3576 * g + 0.1805 * b
            let y = 0.2126 * r + 0.7152 * g + 0.0722 * b
            let z = 0.0193 * r + 0.1192 * g + 0.9505 * b
            let d = x + 15.0 * y + 3.0 * z
            if d <= 0.0 then 0.1978, 0.4683 else 4.0 * x / d, 9.0 * y / d
        // A change counts when the colour has moved more than 0.2 in u'v' from the last counted state.
        let chromaTransitions () =
            let times = ResizeArray<int>()
            let mutable anchor = colourAt 0
            for t in 1 .. span - 1 do
                let c = colourAt t
                let (ua, va), (uc, vc) = uv anchor, uv c
                let d = System.Math.Sqrt((ua - uc) * (ua - uc) + (va - vc) * (va - vc))
                if d > 0.2 && (saturatedRed anchor || saturatedRed c) then
                    times.Add t
                    anchor <- c
            List.ofSeq times
        let general = transitions lum (fun _ _ -> true) 0.1
        let red20 = transitions redness (fun a b -> saturatedRed a || saturatedRed b) 20.0
        let red22 = chromaTransitions ()
        perSecond general, max (perSecond red20) (perSecond red22)

    /// The flash rule as a theme-and-motion check: the selection pulse's colour, over three seconds, has at most three
    /// general and three red flashes in any second. Empty when it passes.
    let flashViolations (theme: Theme) (tokens: MotionTokens) : string list =
        let general, red = flashes (pulseColour theme tokens) 3000
        [
            if general > 3.0 then sprintf "motion.period.pulse: %g general flashes per second (WCAG 2.3.1 allows 3)" general
            if red > 3.0 then sprintf "motion.period.pulse: %g red flashes per second (WCAG 2.3.1 allows 3)" red
        ]

    /// Every colour the pulse can show (all 1001 permille steps) keeps the theme's `contrastTarget` against the ground.
    let pulseContrastViolations (theme: Theme) (tokens: MotionTokens) : string list =
        let ground = aliasColour "surface.ground" theme
        let target = ThemeSeed.tryNumber "contrastTarget" (Theme.seed theme) |> Option.defaultValue 3.0
        let peak = pulsePeak theme tokens
        let selection = aliasColour "glyph.selection" theme
        [
            for k in 0..1000 do
                let c = mix selection peak k
                let r = ThemeColour.contrast c ground
                if r < target then yield sprintf "pulse at %d permille: %s is %.2f:1 on the ground" k (ThemeColour.hex c) r
        ]

    // ---- DTCG 2025.10: STAND-IN for the motion part of #802's `Theme.toDtcg` and `Theme.ofDtcg` ------------------------

    let private num (x: float) =
        // Whole numbers without a fraction, others with up to 6 places: the same text on .NET and Fable.
        if x = System.Math.Floor x then string (int x)
        else
            let scaled = int (System.Math.Floor(abs x * 1e6 + 0.5))
            let whole, frac = scaled / 1000000, scaled % 1000000
            let digits = (string (1000000 + frac)).Substring(1).TrimEnd('0')
            (if x < 0.0 then "-" else "") + string whole + "." + digits

    let private valueJson (v: MotionValue) =
        match v with
        | Duration ms -> "{ \"$type\": \"duration\", \"$value\": { \"value\": " + string ms + ", \"unit\": \"ms\" } }"
        | Easing b -> sprintf "{ \"$type\": \"cubicBezier\", \"$value\": [ %s, %s, %s, %s ] }" (num b.X1) (num b.Y1) (num b.X2) (num b.Y2)
        | Number n -> "{ \"$type\": \"number\", \"$value\": " + num n + " }"

    /// The motion tokens as DTCG 2025.10 JSON: the seed under `seed` and every alias under `alias`, by dotted path,
    /// with the algorithm names under the `$extensions` key `gg.fs.motion`.
    let toDtcg (tokens: MotionTokens) : string =
        let group (names: string list) =
            names
            |> List.map (fun n -> sprintf "    \"%s\": %s" n (valueJson tokens.Values.[n]))
            |> String.concat ",\n"
        let algorithms = tokens.Algorithms |> List.map (fun a -> "\"" + a + "\"") |> String.concat ", "
        sprintf
            "{\n  \"seed\": {\n%s\n  },\n  \"alias\": {\n%s\n  },\n  \"$extensions\": { \"gg.fs.motion\": { \"name\": \"%s\", \"algorithms\": [ %s ] } }\n}\n"
            (group seedNames)
            (group aliasNames)
            tokens.Name
            algorithms

    let algorithmsByName = [ standard; reducedMotion ]

    /// Read DTCG motion tokens. The seed is read and the algorithms run again; an alias that differs from the derived
    /// value is kept as an override. Refused, each named: an unknown name, a `$type` other than the token's, a duration
    /// unit other than `ms` or `s`, a duration that is not a whole number of milliseconds, a `cubicBezier` that is not
    /// four numbers or whose X1 or X2 lies outside [0, 1], and an unknown algorithm.
    let ofDtcg (text: string) : Result<MotionTokens, string list> =
        match Themes.parseJson text with
        | Error e -> Error [ e ]
        | Ok(Themes.JObject top) ->
            let field (name: string) (fields: (string * Themes.Json) list) = fields |> List.tryPick (fun (k, v) -> if k = name then Some v else None)
            let readValue (name: string) (json: Themes.Json) : Result<MotionValue, string> =
                match json with
                | Themes.JObject fields ->
                    match field "$type" fields, field "$value" fields with
                    | Some(Themes.JString "duration"), Some(Themes.JObject v) ->
                        match field "value" v, field "unit" v with
                        | Some(Themes.JNumber n), Some(Themes.JString "ms") when n = System.Math.Floor n -> Ok(Duration(int n))
                        | Some(Themes.JNumber n), Some(Themes.JString "s") when n * 1000.0 = System.Math.Floor(n * 1000.0) -> Ok(Duration(int (n * 1000.0)))
                        | Some(Themes.JNumber _), Some(Themes.JString ("ms" | "s")) -> Error(name + ": a duration must be a whole number of milliseconds")
                        | _, Some(Themes.JString u) -> Error(name + ": duration unit " + u + " is not ms or s")
                        | _ -> Error(name + ": a duration needs value and unit")
                    | Some(Themes.JString "cubicBezier"), Some(Themes.JArray [ Themes.JNumber x1; Themes.JNumber y1; Themes.JNumber x2; Themes.JNumber y2 ]) ->
                        if x1 < 0.0 || x1 > 1.0 || x2 < 0.0 || x2 > 1.0 then Error(name + ": cubicBezier x values must lie in [0, 1]")
                        else Ok(Easing { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2 })
                    | Some(Themes.JString "cubicBezier"), _ -> Error(name + ": a cubicBezier is four numbers")
                    | Some(Themes.JString "number"), Some(Themes.JNumber n) -> Ok(Number n)
                    | Some(Themes.JString t), _ -> Error(name + ": $type " + t + " is not a motion type")
                    | _ -> Error(name + ": no $type")
                | _ -> Error(name + ": not a token")
            let readGroup (group: string) (allowed: string list) =
                match field group top with
                | Some(Themes.JObject entries) ->
                    entries
                    |> List.map (fun (name, json) ->
                        if not (List.contains name allowed) then Error(name + ": not a motion " + group + " token")
                        else readValue name json |> Result.map (fun v -> name, v))
                | _ -> [ Error("no " + group + " group") ]
            let seedRead = readGroup "seed" seedNames
            let aliasRead = readGroup "alias" aliasNames
            let errors = (seedRead @ aliasRead) |> List.choose (function Error e -> Some e | Ok _ -> None)
            let algorithms =
                match field "$extensions" top |> Option.bind (function Themes.JObject f -> field "gg.fs.motion" f | _ -> None) with
                | Some(Themes.JObject ext) ->
                    match field "algorithms" ext with
                    | Some(Themes.JArray names) ->
                        names
                        |> List.map (function
                            | Themes.JString n ->
                                match algorithmsByName |> List.tryFind (fun a -> a.AlgorithmName = n) with
                                | Some a -> Ok a
                                | None -> Error("unknown motion algorithm " + n)
                            | _ -> Error "an algorithm name must be a string")
                    | _ -> [ Error "no algorithms" ]
                | _ -> [ Error "no gg.fs.motion extension" ]
            let algorithmErrors = algorithms |> List.choose (function Error e -> Some e | Ok _ -> None)
            let name =
                match field "$extensions" top |> Option.bind (function Themes.JObject f -> field "gg.fs.motion" f | _ -> None) with
                | Some(Themes.JObject ext) -> (match field "name" ext with Some(Themes.JString n) -> n | _ -> "imported")
                | _ -> "imported"
            if not (errors @ algorithmErrors).IsEmpty then Error(errors @ algorithmErrors)
            else
                let seedValues = seedRead |> List.choose (function Ok kv -> Some kv | Error _ -> None) |> Map.ofList
                let derived = create { Name = name; Values = seedValues; Algorithms = [] } (algorithms |> List.choose (function Ok a -> Some a | Error _ -> None))
                let overridden =
                    aliasRead
                    |> List.choose (function Ok kv -> Some kv | Error _ -> None)
                    |> List.fold (fun (t: MotionTokens) (k, v) -> { t with Values = t.Values.Add(k, v) }) derived
                Ok overridden
        | Ok _ -> Error [ "a DTCG document is an object" ]

/// The digest of the proof's frames (`Motion.digest`, classic, full motion, every millisecond from 0 to 2400) as .NET
/// computes it. A .NET test keeps it current, and the browser smoke recomputes it under Fable and must match.
module MotionBaked =

    let digest = "796381805"
