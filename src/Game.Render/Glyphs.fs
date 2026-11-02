namespace FS.GG.Game.Render

open FS.GG.UI.Scene

type internal FamilyKind =
    | UnitKind
    | StructureKind
    | TerrainKind
    | ItemKind
    | MarkerKind

[<Sealed>]
type GlyphFamily internal (kind: FamilyKind, name: string) =
    member internal _.Kind = kind
    member internal _.Name = name

[<RequireQualifiedAccess>]
module GlyphFamily =
    let unit = GlyphFamily(UnitKind, "unit")
    let structure = GlyphFamily(StructureKind, "structure")
    let terrain = GlyphFamily(TerrainKind, "terrain")
    let item = GlyphFamily(ItemKind, "item")
    let marker = GlyphFamily(MarkerKind, "marker")
    let name (family: GlyphFamily) = family.Name

[<Sealed>]
type IconRef internal (name: string) =
    member internal _.Name = name

[<RequireQualifiedAccess>]
module IconRef =
    let named (name: string) = IconRef name
    let name (icon: IconRef) = icon.Name

[<Sealed>]
type ModifierRef internal (name: string) =
    member internal _.Name = name

[<RequireQualifiedAccess>]
module ModifierRef =
    let stale = ModifierRef "stale"
    let carrying = ModifierRef "carrying"
    let disabled = ModifierRef "disabled"
    let named (name: string) = ModifierRef name
    let name (modifier: ModifierRef) = modifier.Name

type internal SlotKind =
    | Above
    | Below
    | Corner of int
    | Facing
    | Right

[<Sealed>]
type AmplifierSlot internal (kind: SlotKind) =
    member internal _.Kind = kind

[<RequireQualifiedAccess>]
module AmplifierSlot =
    let above = AmplifierSlot Above
    let below = AmplifierSlot Below
    let corner (index: int) = AmplifierSlot(Corner(((index % 4) + 4) % 4))
    let facing = AmplifierSlot Facing
    let right = AmplifierSlot Right

type internal AmplifierKind =
    | Pips of level: int * outOf: int
    | Bar of fraction: float
    | IconBadge of IconRef
    | FacingAngle of float

[<Sealed>]
type Amplifier internal (kind: AmplifierKind) =
    member internal _.Kind = kind

[<RequireQualifiedAccess>]
module Amplifier =
    let pips (level: int) (outOf: int) = Amplifier(Pips(level, outOf))
    let bar (fraction: float) = Amplifier(Bar fraction)
    let badge (icon: IconRef) = Amplifier(IconBadge icon)
    let facing (radians: float) = Amplifier(FacingAngle radians)

[<NoComparison; NoEquality>]
type internal SpecData =
    {
        Family: GlyphFamily
        Anchor: Point
        Footprint: Size
        Identity: Identity option
        Icon: IconRef option
        Team: int option
        Badged: bool
        AllegianceColouring: bool
        SizeStep: int
        FillStep: int
        Modifiers: ModifierRef list
        Amplifiers: (AmplifierSlot * Amplifier) list
    }

[<Sealed>]
type GlyphSpec internal (data: SpecData) =
    member internal _.Data = data

[<RequireQualifiedAccess>]
module GlyphSpec =
    let create (family: GlyphFamily) (anchor: Point) (footprint: Size) =
        GlyphSpec
            {
                Family = family
                Anchor = anchor
                Footprint = footprint
                Identity = None
                Icon = None
                Team = None
                Badged = false
                AllegianceColouring = false
                SizeStep = 3
                FillStep = 0
                Modifiers = []
                Amplifiers = []
            }

    let private update f (spec: GlyphSpec) = GlyphSpec(f spec.Data)
    let identity (identity: Identity) spec = update (fun d -> { d with Identity = Some identity }) spec
    let icon (icon: IconRef) spec = update (fun d -> { d with Icon = Some icon }) spec
    let team (team: int) spec = update (fun d -> { d with Team = Some team }) spec
    let badged spec = update (fun d -> { d with Badged = true }) spec
    let allegianceColouring spec = update (fun d -> { d with AllegianceColouring = true }) spec
    let sizeStep (step: int) spec = update (fun d -> { d with SizeStep = max 0 (min 3 step) }) spec
    let fillStep (step: int) spec = update (fun d -> { d with FillStep = max 0 (min 3 step) }) spec
    let modifier (modifier: ModifierRef) spec = update (fun d -> { d with Modifiers = d.Modifiers @ [ modifier ] }) spec

    let amplifier (slot: AmplifierSlot) (amplifier: Amplifier) spec =
        update (fun d -> { d with Amplifiers = d.Amplifiers @ [ slot, amplifier ] }) spec

[<Sealed>]
type GlyphState internal (selected: bool, stale: bool) =
    member internal _.Selected = selected
    member internal _.Stale = stale
    override _.Equals(other: obj) =
        match other with
        | :? GlyphState as o -> o.Selected = selected && o.Stale = stale
        | _ -> false
    override _.GetHashCode() = (if selected then 1 else 0) + (if stale then 2 else 0)

[<RequireQualifiedAccess>]
module GlyphState =
    let idle = GlyphState(false, false)
    let selected (selected: bool) (state: GlyphState) = GlyphState(selected, state.Stale)
    let stale (stale: bool) (state: GlyphState) = GlyphState(state.Selected, stale)
    let isSelected (state: GlyphState) = state.Selected
    let isStale (state: GlyphState) = state.Stale

[<Sealed>]
type GlyphDrawing
    internal
    (
        amplifiers: SceneNode list,
        body: SceneNode list,
        brackets: SceneNode list,
        frame: Rect,
        hit: SelectableShape,
        extent: Rect,
        diagnostics: string list
    ) =
    member internal _.Amplifiers = amplifiers
    member internal _.Body = body
    member internal _.Brackets = brackets
    member internal _.Frame = frame
    member internal _.Hit = hit
    member internal _.Extent = extent
    member internal _.Diagnostics = diagnostics

[<RequireQualifiedAccess>]
module GlyphDrawing =
    let create (body: SceneNode list) (frame: Rect) (hit: SelectableShape) (extent: Rect) =
        GlyphDrawing([], body, [], frame, hit, extent, [])

    let nodes (drawing: GlyphDrawing) = drawing.Amplifiers @ drawing.Body @ drawing.Brackets
    let amplifiers (drawing: GlyphDrawing) = drawing.Amplifiers
    let body (drawing: GlyphDrawing) = drawing.Body
    let brackets (drawing: GlyphDrawing) = drawing.Brackets
    let frame (drawing: GlyphDrawing) = drawing.Frame
    let hit (drawing: GlyphDrawing) = drawing.Hit
    let extent (drawing: GlyphDrawing) = drawing.Extent
    let diagnostics (drawing: GlyphDrawing) = drawing.Diagnostics

type internal GlyphKind =
    | SpecGlyph of GlyphState * GlyphSpec
    | SelectionGlyph of Rect
    | PathGlyph of Point list
    | CustomGlyph of (Theme -> GlyphDrawing)

[<Sealed>]
type Glyph internal (kind: GlyphKind) =
    member internal _.Kind = kind

[<RequireQualifiedAccess>]
module Glyph =
    let ofSpec (state: GlyphState) (spec: GlyphSpec) = Glyph(SpecGlyph(state, spec))
    let selection (bounds: Rect) = Glyph(SelectionGlyph bounds)
    let path (points: Point list) = Glyph(PathGlyph points)
    let custom (draw: Theme -> GlyphDrawing) = Glyph(CustomGlyph draw)

[<RequireQualifiedAccess>]
module Glyphs =

    let minIdentityFrame = 12.0
    let minBadgeFrame = 22.0
    let minTwoDigitBadgeFrame = 33.0
    let minIconAndBadgeFrame = 40.0

    let private floor (v: float) = System.Math.Floor v

    /// Whole units, half up (`Floor(v + 0.5)`, never `Math.Round`, ADR D1 and D11).
    let private snap (v: float) = floor (v + 0.5)

    let private box (x: float) (y: float) (w: float) (h: float) (colour: Color) = SceneNode.Rectangle((x, y, w, h), colour)

    let private rect x y w h : Rect = { X = x; Y = y; Width = w; Height = h }

    let private grow (by: float) (r: Rect) = rect (r.X - by) (r.Y - by) (r.Width + 2.0 * by) (r.Height + 2.0 * by)

    let private union (a: Rect) (b: Rect) =
        let l = min a.X b.X
        let t = min a.Y b.Y
        let r = max (a.X + a.Width) (b.X + b.Width)
        let bottom = max (a.Y + a.Height) (b.Y + b.Height)
        rect l t (r - l) (bottom - t)

    /// The drawn bounding box of rectangles.
    let private boundsOf (nodes: SceneNode list) : Rect =
        let parts =
            nodes
            |> List.choose (function
                | SceneNode.Rectangle((x, y, w, h), _) -> Some(x, y, x + w, y + h)
                | SceneNode.Circle(c, r, _) -> Some(c.X - r, c.Y - r, c.X + r, c.Y + r)
                | SceneNode.FilledEllipse(b, _) -> Some(b.X, b.Y, b.X + b.Width, b.Y + b.Height)
                | _ -> None)

        match parts with
        | [] -> rect 0.0 0.0 0.0 0.0
        | _ ->
            let l = parts |> List.map (fun (l, _, _, _) -> l) |> List.min
            let t = parts |> List.map (fun (_, t, _, _) -> t) |> List.min
            let r = parts |> List.map (fun (_, _, r, _) -> r) |> List.max
            let b = parts |> List.map (fun (_, _, _, b) -> b) |> List.max
            rect l t (r - l) (b - t)

    /// ADR D1's identity frame of `identity`, `size` units across, centred on whole units at (cx, cy), from opaque bars
    /// only. Band `max 1 (floor (w / 8))`; Enemy's step `max 1 (floor (w / 12))`; Ally's holes `max 2 (floor (w / 6))`
    /// square at the quarter points of its top and bottom edges.
    let internal identityFrame (identity: Identity) (cx: float) (cy: float) (size: float) (ink: Color) : SceneNode list =
        let w = floor size
        let band = max 1.0 (floor (w / 8.0))
        let half = floor (w / 2.0)
        let at (dx: float) (dy: float) (bw: float) (bh: float) = box (cx + dx) (cy + dy) bw bh ink

        if w <= 0.0 then
            []
        else
            match identity with
            | Identity.Own ->
                let h = floor (w * 2.0 / 3.0)
                [ at -half -(floor (h / 2.0)) w h ]
            | Identity.Ally ->
                let h = floor (w * 2.0 / 3.0)
                let top = -(floor (h / 2.0))
                let n = max 2.0 (floor (w / 6.0))
                let left = -half
                let q1 = left + floor (w / 4.0) - floor (n / 2.0)
                let q3 = left + floor (3.0 * w / 4.0) - floor (n / 2.0)

                let strip y =
                    [ at left y (q1 - left) n; at (q1 + n) y (q3 - q1 - n) n; at (q3 + n) y (left + w - q3 - n) n ]

                [ yield! strip top; at left (top + n) w (h - 2.0 * n); yield! strip (top + h - n) ]
            | Identity.Neutral -> [ at -half -half w w ]
            | Identity.Enemy ->
                let step = max 1.0 (floor (w / 12.0))
                let steps = int (floor (half / step))

                [
                    for i in 0 .. steps - 1 do
                        let rowWidth = w - 2.0 * step * float (i + 1)

                        if rowWidth > 0.0 then
                            let x = -(floor (rowWidth / 2.0))
                            yield at x (-step * float (i + 1)) rowWidth step
                            yield at x (step * float i) rowWidth step
                ]
            | Identity.Unknown ->
                let reach = half + band
                let arm = floor (w / 2.0)
                let armHalf = floor (arm / 2.0)
                [ at -reach -armHalf (2.0 * reach) arm; at -armHalf -reach arm (2.0 * reach) ]

    let private layerCache =
        System.Collections.Generic.Dictionary<string, (float * float * float * float) list * (float * float * float * float) list * (float * float * float * float) list>()

    /// The team-mark layers of `identity`'s frame at size `w`, with a frame band of `band` units and a keyline of
    /// `keyline` units, as rectangles relative to the frame's centre (ADR erratum E1, as corrected in #809):
    /// - the ring: cells at least `band + keyline` inside the outline along their row and column (a plus, which shrinks
    ///   a stepped diamond as a nested diamond does) and at least `keyline + 1` from any cell that is not frame ink, so
    ///   Ally's quarter-point holes are lined too. Ally's outline is the friend rectangle without its holes; every other
    ///   frame's outline is its own ink;
    /// - the keyline: the ring grown by `keyline` units in every direction, on frame ink, so every cell next to a ring
    ///   cell is keyline or ring, never ground, a hole or the frame colour;
    /// - the body: cells at least `2 band + keyline` inside the outline, within the ring.
    /// Rows with the same runs are merged into one rectangle. Cached per shape.
    let internal teamLayers (identity: Identity) (w: float) (band: int) (keyline: int) =
        let key = sprintf "%A|%g|%d|%d" identity w band keyline

        match layerCache.TryGetValue key with
        | true, cached -> cached
        | _ ->
            let barsOf (shape: Identity) =
                identityFrame shape 0.0 0.0 w { Red = 0uy; Green = 0uy; Blue = 0uy; Alpha = 255uy }
                |> List.choose (function
                    | SceneNode.Rectangle((x, y, rw, rh), _) -> Some(int x, int y, int (x + rw), int (y + rh))
                    | _ -> None)

            let bars = barsOf identity
            let outline = if identity = Identity.Ally then barsOf Identity.Own else bars

            let result =
                match bars with
                | [] -> [], [], []
                | _ ->
                    let all = bars @ outline
                    let left = all |> List.map (fun (l, _, _, _) -> l) |> List.min
                    let top = all |> List.map (fun (_, t, _, _) -> t) |> List.min
                    let right = all |> List.map (fun (_, _, r, _) -> r) |> List.max
                    let bottom = all |> List.map (fun (_, _, _, b) -> b) |> List.max
                    let cols, rows = right - left, bottom - top
                    let at x y = y * cols + x

                    let grid (rects: (int * int * int * int) list) =
                        let cells = Array.init (cols * rows) (fun _ -> false)

                        for l, t, r, b in rects do
                            for y in t .. b - 1 do
                                for x in l .. r - 1 do
                                    cells.[at (x - left) (y - top)] <- true

                        cells

                    // Erosion by a plus of radius n: every cell within n along the row and along the column is set (a
                    // cell outside the box is not).
                    let erode (cells: bool[]) (n: int) =
                        Array.init (cols * rows) (fun i ->
                            let x, y = i % cols, i / cols

                            x - n >= 0
                            && x + n < cols
                            && y - n >= 0
                            && y + n < rows
                            && Seq.forall (fun d -> cells.[at (x + d) y] && cells.[at x (y + d)]) (seq { -n .. n }))

                    // Dilation by a square of radius n.
                    let dilate (cells: bool[]) (n: int) =
                        Array.init (cols * rows) (fun i ->
                            let x, y = i % cols, i / cols

                            seq { for dy in -n .. n do for dx in -n .. n -> x + dx, y + dy }
                            |> Seq.exists (fun (cx, cy) -> cx >= 0 && cx < cols && cy >= 0 && cy < rows && cells.[at cx cy]))

                    let ink = grid bars
                    let shape = grid outline
                    let both (a: bool[]) (b: bool[]) = Array.map2 (&&) a b
                    let ring = both (erode shape (band + keyline)) (erode ink (keyline + 1))
                    let keyed = both (dilate ring keyline) ink
                    let body = both (erode shape (2 * band + keyline)) ring

                    let rectsOf (kept: bool[]) =
                        let runsOf (y: int) =
                            [
                                let mutable x = 0

                                while x < cols do
                                    if kept.[at x y] then
                                        let start = x

                                        while x < cols && kept.[at x y] do
                                            x <- x + 1

                                        yield start, x - start
                                    else
                                        x <- x + 1
                            ]

                        let rects = ResizeArray<float * float * float * float>()
                        let mutable y = 0

                        while y < rows do
                            let runs = runsOf y
                            let mutable next = y + 1

                            while next < rows && runsOf next = runs do
                                next <- next + 1

                            for start, width in runs do
                                rects.Add((float (left + start), float (top + y), float width, float (next - y)))

                            y <- next

                        List.ofSeq rects

                    rectsOf keyed, rectsOf ring, rectsOf body

            layerCache.[key] <- result
            result

    /// One seven-segment digit, 0 to 9, on a 3 x 5 grid of 2-unit cells (6 x 10) whose top left is (x, y).
    let internal digit (d: int) (x: float) (y: float) (ink: Color) : SceneNode list =
        let u = 2.0
        let cell (c: float) (r: float) (cw: float) (rh: float) = box (x + c * u) (y + r * u) (cw * u) (rh * u) ink

        let segment s =
            match s with
            | 'a' -> cell 0.0 0.0 3.0 1.0
            | 'b' -> cell 2.0 0.0 1.0 3.0
            | 'c' -> cell 2.0 2.0 1.0 3.0
            | 'd' -> cell 0.0 4.0 3.0 1.0
            | 'e' -> cell 0.0 2.0 1.0 3.0
            | 'f' -> cell 0.0 0.0 1.0 3.0
            | _ -> cell 0.0 2.0 3.0 1.0

        let segments =
            match d with
            | 0 -> "abcdef"
            | 1 -> "bc"
            | 2 -> "abged"
            | 3 -> "abgcd"
            | 4 -> "fgbc"
            | 5 -> "afgcd"
            | 6 -> "afgedc"
            | 7 -> "abc"
            | 8 -> "abcdefg"
            | _ -> "abcdfg"

        [ for ch in segments -> segment ch ]

    /// The team badge of team number `number` (from 1; never wrapped), centred on (cx, cy): one digit (6 x 10) or two
    /// digits with a one-cell gap (14 x 10).
    let internal badgeNodes (number: int) (cx: float) (cy: float) (ink: Color) : SceneNode list =
        if number < 10 then
            digit number (cx - 3.0) (cy - 5.0) ink
        else
            digit (number / 10) (cx - 7.0) (cy - 5.0) ink @ digit (number % 10) (cx + 1.0) (cy - 5.0) ink

    /// The box a badge needs on frame ink: the digits plus a 1-unit margin all round.
    let internal badgeBox (number: int) (cx: float) (cy: float) =
        if number < 10 then rect (cx - 4.0) (cy - 6.0) 8.0 12.0 else rect (cx - 8.0) (cy - 6.0) 16.0 12.0

    let private size (theme: Theme) (data: SpecData) =
        let cells = float (max 1 (min data.Footprint.Width data.Footprint.Height))
        floor (theme.Palette.Cell * cells * (0.5 + float data.SizeStep / 12.0))

    let frameSize (theme: Theme) (spec: GlyphSpec) = size theme spec.Data

    let private ringBand (theme: Theme) = max theme.Palette.Band (2.0 / theme.Palette.MinCssPerUnit)

    let reserve (theme: Theme) =
        2.0 * ringBand theme + theme.Palette.Gap + theme.Palette.Bracket

    /// The badge drawn for `data`, if any: its number and whether it fits.
    let private badgeOf (data: SpecData) (w: float) =
        match data.Team with
        | Some team when data.Badged || data.AllegianceColouring ->
            let number = team + 1
            let minimum = if number < 10 then minBadgeFrame else minTwoDigitBadgeFrame
            Some(number, w >= minimum && number < 100, minimum)
        | _ -> None

    let private hasFrame (data: SpecData) =
        match data.Family.Kind with
        | UnitKind
        | StructureKind -> true
        | _ -> false

    let iconSlotAvailable (theme: Theme) (spec: GlyphSpec) =
        let data = spec.Data
        let w = size theme data

        hasFrame data
        && (match badgeOf data w with
            | Some(_, true, _) -> w >= minIconAndBadgeFrame
            | _ -> true)

    /// The icon `name` in a box of side `s` centred on (cx, cy), in bars, or `None` for a name not in the set.
    let private iconNodes (name: string) (cx: float) (cy: float) (s: float) (ink: Color) : SceneNode list option =
        let s = max 4.0 (floor s)
        let t = max 1.0 (floor (s / 4.0))
        let half = floor (s / 2.0)
        let thalf = floor (t / 2.0)

        match name with
        | "infantry" -> Some [ box (cx - half) (cy - thalf) s t ink; box (cx - thalf) (cy - half) t s ink ]
        | "armour" ->
            let h = floor (s * 0.6)
            let hh = floor (h / 2.0)

            Some
                [
                    box (cx - half) (cy - hh) s t ink
                    box (cx - half) (cy - hh + h - t) s t ink
                    box (cx - half) (cy - hh) t h ink
                    box (cx - half + s - t) (cy - hh) t h ink
                ]
        | "artillery" ->
            let q = floor (s / 2.0)
            let qh = floor (q / 2.0)
            Some [ box (cx - qh) (cy - qh) q q ink ]
        | "support" -> Some [ box (cx - half) (cy - thalf) s t ink ]
        | "structure" ->
            let q = floor (s * 0.6)
            let qh = floor (q / 2.0)
            Some [ box (cx - qh) (cy - qh) q q ink; box (cx - half) (cy + qh) s t ink ]
        | _ -> None

    /// A colour `steps` lightness steps (0.06 each, in OKLCH) towards the ground.
    let private stepped (theme: Theme) (steps: int) (colour: Color) =
        if steps <= 0 then
            colour
        else
            let p = ThemeColour.toOklch colour
            let g = ThemeColour.toOklch theme.Palette.Ground
            let delta = 0.06 * float steps
            let l = if g.L < p.L then max g.L (p.L - delta) else min g.L (p.L + delta)
            ThemeColour.ofOklch { p with L = l }

    /// Corner brackets around `bounds`, grown by `offset`, `stroke` wide.
    let private bracketsAround (theme: Theme) (bounds: Rect) =
        let s = theme.Palette.Bracket
        let b = grow (2.0 * ringBand theme + theme.Palette.Gap) bounds
        let arm = max (2.0 * s) (floor (min b.Width b.Height * 0.3))
        let l = b.X - s
        let t = b.Y - s
        let r = b.X + b.Width
        let bottom = b.Y + b.Height
        let c = theme.Palette.Selection

        [
            box l t arm s c
            box l t s arm c
            box (r + s - arm) t arm s c
            box r t s arm c
            box l bottom arm s c
            box l (bottom + s - arm) s arm c
            box (r + s - arm) bottom arm s c
            box r (bottom + s - arm) s arm c
        ]

    /// The amplifier pass of a framed glyph: each slot stacks outwards from the reserve.
    let private amplifierNodes (theme: Theme) (frame: Rect) (amplifiers: (AmplifierSlot * Amplifier) list) =
        let p = theme.Palette
        let r = reserve theme
        let diagnostics = ResizeArray<string>()
        let nodes = ResizeArray<SceneNode>()
        let mutable extent = grow r frame
        let barHeight = max 4.0 (floor (p.Cell * 3.0 / 16.0))
        let pip = max 2.0 (floor (p.Cell / 8.0))
        let iconBox = max 6.0 (floor (p.Cell / 4.0))
        let mutable above = frame.Y - r
        let mutable below = frame.Y + frame.Height + r
        let mutable right = frame.X + frame.Width + r

        /// The size an amplifier takes, across and along its slot.
        let measure (a: Amplifier) =
            match a.Kind with
            | Bar _ -> frame.Width, barHeight
            | Pips(_, outOf) ->
                let n = float (max 1 (min 5 outOf))
                n * pip + (n - 1.0) * 2.0, pip
            | IconBadge _ -> iconBox, iconBox
            | FacingAngle _ -> 0.0, 0.0

        let drawAt (a: Amplifier) (x: float) (y: float) (w: float) (h: float) =
            match a.Kind with
            | Bar fraction ->
                let f = if System.Double.IsNaN fraction then 0.0 else max 0.0 (min 1.0 fraction)
                let fill = if f > 0.6 then p.Good elif f > 0.3 then p.Warn else p.Bad
                let k = 1.0
                nodes.Add(box x y w h p.Keyline)
                nodes.Add(box (x + k) (y + k) (w - 2.0 * k) (h - 2.0 * k) p.Track)
                let length = floor ((w - 2.0 * k) * f)

                if length > 0.0 then
                    nodes.Add(box (x + k) (y + k) length (h - 2.0 * k) fill)
            | Pips(level, outOf) ->
                if outOf > 5 then
                    diagnostics.Add(sprintf "pips %d of %d: at most 5 are drawn" level outOf)

                let n = max 1 (min 5 outOf)

                for i in 0 .. n - 1 do
                    nodes.Add(box (x + float i * (pip + 2.0)) y pip pip (if i < level then p.Body else p.Stale))
            | IconBadge icon ->
                nodes.Add(box x y w h p.Body)

                match iconNodes icon.Name (x + floor (w / 2.0)) (y + floor (h / 2.0)) (w - 2.0) p.IconInk with
                | Some icon -> nodes.AddRange icon
                | None -> diagnostics.Add(sprintf "icon %s is not in the icon set" icon.Name)
            | FacingAngle _ -> ()

        for slot, a in amplifiers do
            match a.Kind, slot.Kind with
            | FacingAngle _, _
            | _, Facing -> diagnostics.Add "facing needs the stroked profile (after #773); the portable profile draws none"
            | _ ->
                let w, h = measure a
                let w = floor w
                let h = floor h

                let x, y =
                    match slot.Kind with
                    | Above ->
                        above <- above - h
                        let at = frame.X + floor ((frame.Width - w) / 2.0), above
                        above <- above - 1.0
                        at
                    | Below ->
                        let at = frame.X + floor ((frame.Width - w) / 2.0), below
                        below <- below + h + 1.0
                        at
                    | Right ->
                        let at = right, frame.Y + floor ((frame.Height - h) / 2.0)
                        right <- right + w + 1.0
                        at
                    | Corner 0 -> frame.X - r - w, frame.Y - r - h
                    | Corner 1 -> frame.X + frame.Width + r, frame.Y - r - h
                    | Corner 2 -> frame.X + frame.Width + r, frame.Y + frame.Height + r
                    | Corner _ -> frame.X - r - w, frame.Y + frame.Height + r
                    | Facing -> frame.X, frame.Y

                drawAt a x y w h
                extent <- union extent (rect x y w h)

        List.ofSeq nodes, extent, List.ofSeq diagnostics

    let private drawFramed (theme: Theme) (state: GlyphState) (data: SpecData) =
        let p = theme.Palette
        let diagnostics = ResizeArray<string>()
        let cx = snap data.Anchor.X
        let cy = snap data.Anchor.Y
        let w = size theme data
        let identity = defaultArg data.Identity Identity.Unknown
        let band = max 1.0 (floor (w / 8.0))

        if w < minIdentityFrame then
            diagnostics.Add(sprintf "frame %g is below the %g-unit identity minimum: identity rests on colour and the mirror label" w minIdentityFrame)

        let allegiance = Theme.allegiance identity theme
        let ink = if state.Stale then p.Stale else stepped theme data.FillStep allegiance
        let outer = identityFrame identity cx cy w ink
        let frame = boundsOf outer

        // The team mark: a ring in the team colour on the neutral body, inside a body-colour keyline (ADR E1).
        let teamMark =
            match data.Team with
            | None -> []
            | Some team ->
                let colour =
                    if data.AllegianceColouring then
                        allegiance
                    elif team < 0 || team >= p.Teams.Length then
                        diagnostics.Add(sprintf "team %d has no colour in theme %s (%d teams): allegiance colour" (team + 1) (Theme.name theme) p.Teams.Length)
                        allegiance
                    else
                        p.Teams.[team]

                // ADR erratum E1, corrected in #809: from the edge in, the frame, a body-colour keyline, the team colour,
                // then the body. The layers come from the frame's own cells (`teamLayers`): the ring keeps the keyline
                // plus one unit from any cell that is not frame ink, and the keyline is the ring grown by its width, so no
                // team-colour pixel shares an edge with the ground, Ally's quarter-point holes or the identity colour, on
                // every frame and at every size. Nested frames of the same shape did, on Ally and at 12 units.
                // Widths: the frame keeps `band`, the keyline is half a band (at least one unit), the ring is a band.
                let bands = int band
                let keylineWidth = max 1 (bands / 2)
                let ringAt = bands + keylineWidth

                let place (rects: (float * float * float * float) list) (colour: Color) =
                    rects |> List.map (fun (dx, dy, rw, rh) -> box (cx + dx) (cy + dy) rw rh colour)

                let keyedCells, ringCells, bodyCells = teamLayers identity w bands keylineWidth
                let keyline = place keyedCells p.Body
                let inner = place ringCells colour

                match data.Family.Kind with
                | StructureKind ->
                    // D3 and erratum E6: a structure's team mark is a foot band, the ring layer's lower rows in the team
                    // colour with the body above, inside every stepped frame, so a structure and a unit of the same
                    // identity, size and badge still differ.
                    // The band is as tall as it must be for its team-colour area to reach three quarters of Own's (the
                    // inner width times the band), and never more than 0.6 of the inner frame: a diamond's or a
                    // quatrefoil's bottom rows are narrow, so a fixed height left a hostile structure only a tip of
                    // team colour (#789 review).
                    let box = boundsOf inner
                    let bottom = box.Y + box.Height
                    let wanted = 0.75 * (w - 2.0 * float ringAt) * band

                    // The ink area of the inner frame's bottom `h` rows, row by row as a union of spans: the clipped
                    // pieces, like a quatrefoil's two bars, overlap.
                    let area (h: float) =
                        [ for row in 0 .. int h - 1 ->
                              let y = bottom - 1.0 - float row
                              let spans =
                                  inner
                                  |> List.choose (function
                                      | SceneNode.Rectangle((x, ry, rw, rh), _) when ry <= y && y + 1.0 <= ry + rh -> Some(x, x + rw)
                                      | _ -> None)
                                  |> List.sortBy fst

                              spans
                              |> List.fold
                                  (fun (total, reach) (l, r) ->
                                      if r <= reach then total, reach else total + r - max l reach, r)
                                  (0.0, -infinity)
                              |> fst ]
                        |> List.sum

                    let cap = max band (floor (box.Height * 0.6))
                    let height = [ band .. cap ] |> List.tryFind (fun h -> area h >= wanted) |> Option.defaultValue cap
                    let foot = bottom - height

                    // The body above the band is drawn as the inner frame in the body colour, so the kit samples it where a
                    // unit's ring would be: the two families differ in decided pixels both ways.
                    keyline
                    @ place ringCells p.Body
                    @ (inner
                       |> List.choose (function
                           | SceneNode.Rectangle((x, y, rw, rh), c) when y + rh > foot ->
                               let top = max y foot
                               Some(SceneNode.Rectangle((x, top, rw, y + rh - top), c))
                           | _ -> None))
                | _ -> keyline @ inner @ place bodyCells p.Body

        let badge = badgeOf data w

        let badgeDrawn =
            match badge with
            | Some(number, false, minimum) ->
                diagnostics.Add(
                    if number >= 100 then
                        sprintf "badge for team %d omitted: at most two digits" number
                    else
                        sprintf "badge for team %d omitted: frame %g is below %g units" number w minimum
                )

                None
            | Some(number, true, _) -> Some number
            | None -> None

        let sideBySide = badgeDrawn.IsSome && w >= minIconAndBadgeFrame
        let quarter = floor (w / 8.0)

        let icon =
            match data.Icon with
            | None -> []
            | Some icon when badgeDrawn.IsSome && not sideBySide ->
                diagnostics.Add(
                    sprintf
                        "icon slot unavailable: the mandatory badge takes the inner centre of a %g-unit frame (%g to %g units); icon %s not drawn"
                        w
                        minBadgeFrame
                        (minIconAndBadgeFrame - 1.0)
                        icon.Name
                )

                []
            | Some icon ->
                let x = if sideBySide then cx - quarter else cx
                let s = if sideBySide then 10.0 else min 10.0 (floor (w / 3.0))

                match iconNodes icon.Name x cy s p.IconInk with
                | Some nodes -> nodes
                | None ->
                    diagnostics.Add(sprintf "icon %s is not in the icon set" icon.Name)
                    []

        let modifiers =
            let n = max 2.0 (floor (w / 8.0))
            let inner = floor (w / 4.0)
            let corners = [ cx - inner, cy - inner; cx + inner - n, cy - inner; cx + inner - n, cy + inner - n; cx - inner, cy + inner - n ]

            if data.Modifiers.Length > 4 then
                diagnostics.Add(sprintf "%d modifiers: at most 4 corner-notch slots are drawn" data.Modifiers.Length)

            List.zip (List.truncate (min 4 data.Modifiers.Length) corners) (List.truncate 4 data.Modifiers)
            |> List.map (fun ((x, y), _) -> box x y n n p.ModifierInk)

        let badgeNodes =
            match badgeDrawn with
            | Some number -> badgeNodes number (if sideBySide then cx + quarter else cx) cy p.BadgeInk
            | None -> []

        let amplifiers, extent, amplifierDiagnostics = amplifierNodes theme frame data.Amplifiers
        diagnostics.AddRange amplifierDiagnostics
        let brackets = if state.Selected then bracketsAround theme frame else []
        let body = outer @ teamMark @ icon @ modifiers @ badgeNodes
        GlyphDrawing(amplifiers, body, brackets, frame, SelectableShape.Box, extent, List.ofSeq diagnostics)

    let private drawUnframed (theme: Theme) (state: GlyphState) (data: SpecData) =
        let p = theme.Palette
        let diagnostics = ResizeArray<string>()
        let cx = snap data.Anchor.X
        let cy = snap data.Anchor.Y
        let w = size theme data
        let band = max 1.0 (floor (w / 8.0))
        let iconName = data.Icon |> Option.map (fun i -> i.Name)

        if not data.Modifiers.IsEmpty then
            diagnostics.Add(sprintf "%s glyphs have no modifier slots" data.Family.Name)

        let body, hit =
            match data.Family.Kind with
            | TerrainKind ->
                let cells = p.Cell
                let tw = cells * float (max 1 data.Footprint.Width) - 2.0 * p.Gap
                let th = cells * float (max 1 data.Footprint.Height) - 2.0 * p.Gap

                let colour =
                    match defaultArg iconName "open" with
                    | "open" -> p.TerrainOpen
                    | "blocked" -> p.TerrainBlocked
                    | "difficult" -> p.TerrainDifficult
                    | "water" -> p.TerrainWater
                    | "void" -> p.TerrainVoid
                    | other ->
                        diagnostics.Add(sprintf "terrain kind %s is not in the theme: open" other)
                        p.TerrainOpen

                [ box (cx - floor (tw / 2.0)) (cy - floor (th / 2.0)) tw th colour ], SelectableShape.Box
            | ItemKind ->
                let ow = max 2.0 (floor (w / 2.0))
                let oh = max 2.0 (floor (w / 3.0))
                let colour =
                    match data.Identity with
                    | Some identity -> Theme.allegiance identity theme
                    | None -> p.Body

                [ SceneNode.FilledEllipse(rect (cx - floor (ow / 2.0)) (cy - floor (oh / 2.0)) ow oh, colour) ], SelectableShape.Ellipse
            | _ ->
                let a = floor (w / 4.0)
                let thalf = floor (band / 2.0)

                match defaultArg iconName "waypoint" with
                | "destination" ->
                    [ SceneNode.Circle({ X = cx; Y = cy }, a, p.Marker); SceneNode.Circle({ X = cx; Y = cy }, max 1.0 (a - band), p.Ground) ],
                    SelectableShape.Ellipse
                | "target" ->
                    let g = max 1.0 (floor (w / 16.0))
                    let arm = max 1.0 (a - g)

                    [
                        box (cx - a) (cy - thalf) arm band p.Marker
                        box (cx + g) (cy - thalf) arm band p.Marker
                        box (cx - thalf) (cy - a) band arm p.Marker
                        box (cx - thalf) (cy + g) band arm p.Marker
                    ],
                    SelectableShape.Box
                | other ->
                    if other <> "waypoint" then
                        diagnostics.Add(sprintf "marker %s is not in the icon set: waypoint" other)

                    [ box (cx - a) (cy - thalf) (2.0 * a) band p.Marker; box (cx - thalf) (cy - a) band (2.0 * a) p.Marker ],
                    SelectableShape.Box

        let frame = boundsOf body
        let amplifiers, extent, amplifierDiagnostics = amplifierNodes theme frame data.Amplifiers
        diagnostics.AddRange amplifierDiagnostics
        let brackets = if state.Selected then bracketsAround theme frame else []
        GlyphDrawing(amplifiers, body, brackets, frame, hit, extent, List.ofSeq diagnostics)

    let private drawPath (theme: Theme) (points: Point list) =
        let p = theme.Palette
        let d = max 2.0 p.Band
        let spacing = max 1.0 (floor (p.Cell / 2.0))
        let dot (x: float) (y: float) (s: float) = box (snap x - floor (s / 2.0)) (snap y - floor (s / 2.0)) s s p.Path

        match points with
        | [] -> GlyphDrawing([], [], [], rect 0.0 0.0 0.0 0.0, SelectableShape.Box, rect 0.0 0.0 0.0 0.0, [ "a path needs at least one point" ])
        | first :: _ ->
            let dots =
                [
                    yield dot first.X first.Y d

                    for a, b in List.pairwise points do
                        let dx = b.X - a.X
                        let dy = b.Y - a.Y
                        let length = System.Math.Sqrt(dx * dx + dy * dy)
                        let n = int (floor (length / spacing))

                        for i in 1..n do
                            let t = float i * spacing / length
                            yield dot (a.X + dx * t) (a.Y + dy * t) d
                ]

            let last = List.last points
            let body = dots @ [ dot last.X last.Y (2.0 * d) ]
            let frame = boundsOf body
            GlyphDrawing([], body, [], frame, SelectableShape.Box, frame, [])

    let draw (theme: Theme) (glyph: Glyph) : GlyphDrawing =
        match glyph.Kind with
        | SpecGlyph(state, spec) -> if hasFrame spec.Data then drawFramed theme state spec.Data else drawUnframed theme state spec.Data
        | SelectionGlyph bounds ->
            let brackets = bracketsAround theme bounds
            GlyphDrawing([], [], brackets, bounds, SelectableShape.Box, union bounds (boundsOf brackets), [])
        | PathGlyph points -> drawPath theme points
        | CustomGlyph draw -> draw theme

    let scene (theme: Theme) (glyphs: Glyph list) : Scene =
        let drawings = glyphs |> List.map (draw theme)

        {
            Nodes =
                (drawings |> List.collect (fun d -> d.Amplifiers))
                @ (drawings |> List.collect (fun d -> d.Body))
                @ (drawings |> List.collect (fun d -> d.Brackets))
        }

    /// Four bands of `width` just outside `b`.
    let private bandsAround (b: Rect) (width: float) (colour: Color) =
        [
            box (b.X - width) (b.Y - width) (b.Width + 2.0 * width) width colour
            box (b.X - width) (b.Y + b.Height) (b.Width + 2.0 * width) width colour
            box (b.X - width) b.Y width b.Height colour
            box (b.X + b.Width) b.Y width b.Height colour
        ]

    /// `units` scene units, rounded up to a whole CSS px at `cssPerUnit`.
    let private wholeCss (cssPerUnit: float) (units: float) =
        System.Math.Ceiling(units * cssPerUnit - 1e-9) / cssPerUnit

    let private cueLayer (withHover: bool) (theme: unit -> Theme) (context: CueContext) : SceneNode list =
        let theme = theme ()
        let p = theme.Palette
        let css = CueContext.cssPerUnit context
        let cues = CueContext.cues context
        let objects = CueContext.objects context
        let find id = objects |> List.tryFind (fun o -> o.Id = id)

        let focused =
            match InteractionCues.focused cues with
            | Some id when InteractionCues.focusVisible cues -> find id
            | _ -> None

        let hover =
            if not withHover then
                []
            else
                match InteractionCues.hovered cues |> Option.bind find with
                | Some target when (focused |> Option.map (fun f -> f.Id)) <> Some target.Id ->
                    bandsAround target.Bounds (wholeCss css (max p.HoverWidth (1.0 / css))) p.Hover
                | _ -> []

        let ring =
            match focused with
            | Some target ->
                let band = wholeCss css (max p.Band (2.0 / css))
                bandsAround target.Bounds band p.FocusInner @ bandsAround (grow band target.Bounds) band p.FocusOuter
            | None -> []

        hover @ ring

    let cues (theme: unit -> Theme) (context: CueContext) = cueLayer false theme context
    let cuesHover (theme: unit -> Theme) (context: CueContext) = cueLayer true theme context

    let samples (origin: Point) : Glyph list =
        let at col row = { X = origin.X + 32.0 * float col + 16.0; Y = origin.Y + 32.0 * float row + 16.0 }
        let one: Size = { Width = 1; Height = 1 }
        let idle = GlyphState.idle
        let unit col row = GlyphSpec.create GlyphFamily.unit (at col row) one
        let identities = [ Identity.Own; Identity.Ally; Identity.Neutral; Identity.Enemy; Identity.Unknown ]

        [
            for i, identity in List.indexed identities do
                yield unit i 0 |> GlyphSpec.identity identity |> GlyphSpec.icon (IconRef.named "infantry") |> Glyph.ofSpec idle

            yield
                unit 0 1
                |> GlyphSpec.identity Identity.Own
                |> GlyphSpec.team 0
                |> GlyphSpec.badged
                |> GlyphSpec.amplifier AmplifierSlot.above (Amplifier.bar 0.5)
                |> Glyph.ofSpec (GlyphState.selected true idle)
            yield unit 1 1 |> GlyphSpec.identity Identity.Enemy |> GlyphSpec.team 1 |> Glyph.ofSpec (GlyphState.stale true idle)
            yield unit 2 1 |> GlyphSpec.identity Identity.Ally |> GlyphSpec.modifier ModifierRef.carrying |> Glyph.ofSpec idle
            yield GlyphSpec.create GlyphFamily.structure (at 3 1) one |> GlyphSpec.identity Identity.Neutral |> GlyphSpec.icon (IconRef.named "structure") |> Glyph.ofSpec idle
            yield GlyphSpec.create GlyphFamily.item (at 4 1) one |> Glyph.ofSpec idle
            yield GlyphSpec.create GlyphFamily.terrain (at 0 2) one |> GlyphSpec.icon (IconRef.named "water") |> Glyph.ofSpec idle
            yield GlyphSpec.create GlyphFamily.marker (at 1 2) one |> Glyph.ofSpec idle
            yield GlyphSpec.create GlyphFamily.marker (at 2 2) one |> GlyphSpec.icon (IconRef.named "destination") |> Glyph.ofSpec idle
            yield GlyphSpec.create GlyphFamily.marker (at 3 2) one |> GlyphSpec.icon (IconRef.named "target") |> Glyph.ofSpec idle
            yield Glyph.path [ at 4 2; { (at 4 2) with Y = (at 4 2).Y + 8.0 } ]
        ]
