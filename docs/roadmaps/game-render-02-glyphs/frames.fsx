// Distinguishability of the identity frames and the index badges (FS.GG.Game#789; #794 reviews R3, N1 and N2), measured
// on the shipped library in FS.GG.Game.Render (the experimental prototype it was first measured on is retired). A drawing
// A is told from B when some pixel decided for A is decided in B's drawing with a different colour. Monochrome
// (`GlyphConformance.monochrome`): one ink on the ground, so only shape counts. Two samplers: the middle-of-edge samples
// (`ScenePixels.expectations`) and ADR D6's corner and quarter-edge samples (`ScenePixels.identityExpectations`). Run
// after building tests/Game.Render.Tests:
//   dotnet fsi docs/roadmaps/game-render-02-glyphs/frames.fsx
#r "../../../tests/Game.Render.Tests/bin/Debug/net10.0/FS.GG.UI.Scene.dll"
#r "../../../tests/Game.Render.Tests/bin/Debug/net10.0/FS.GG.Game.Render.dll"
open FS.GG.UI.Scene
open FS.GG.Game.Render
let W = 96.0
let identities = [ Identity.Own; Identity.Ally; Identity.Neutral; Identity.Enemy; Identity.Unknown ]
let mono = GlyphConformance.monochrome Theme.classic
let ground = Theme.tryColour "surface.ground" mono |> Option.get
/// A monochrome theme whose cell gives a frame of `size` units (`floor (cell x 0.75)`).
let themeFor (size: float) =
    match Theme.create (Theme.seed mono |> ThemeSeed.number "cell" (ceil (size / 0.75))) [ ThemeAlgorithm.standard ] [] with
    | Ok t -> t
    | Error e -> failwithf "%A" (e |> List.map string)
let onGround nodes = SceneNode.Rectangle((0.0, 0.0, W, W), ground) :: nodes
let colourAt (nodes: SceneNode list) (col: int) (row: int) =
    let x0, y0, x1, y1 = float col, float row, float col + 1.0, float row + 1.0
    let mutable top = None
    let mutable partial = false
    for n in nodes do
        match n with
        | SceneNode.Rectangle((l, t, w, h), colour) ->
            let r, b = l + w, t + h
            if x1 <= l || x0 >= r || y1 <= t || y0 >= b then ()
            elif x0 >= l && x1 <= r && y0 >= t && y1 <= b then (top <- Some colour; partial <- false)
            else partial <- true
        | _ -> partial <- true
    if partial then None else top
let differ (sampler: float -> float -> Scene -> PixelExpectation list) a b =
    sampler W W { Nodes = a }
    |> List.filter (fun e -> match colourAt b (int (floor e.Point.X)) (int (floor e.Point.Y)) with Some o -> o <> e.Colour | None -> false)
    |> List.length
let glyph (theme: Theme) (spec: GlyphSpec) = onGround (GlyphDrawing.body (Glyphs.draw theme (Glyph.ofSpec GlyphState.idle spec)))
let unit identity = GlyphSpec.create GlyphFamily.unit { X = 48.0; Y = 48.0 } { Width = 1; Height = 1 } |> GlyphSpec.identity identity
let frame identity size = glyph (themeFor size) (unit identity)
let samplers = [ "middle-of-edge samples", ScenePixels.expectations; "with D6's corner and quarter-edge samples", ScenePixels.identityExpectations ]
let pairs = [ for i, a in List.indexed identities do for b in List.skip (i + 1) identities -> a, b ]
for label, sampler in samplers do
    printfn "\nIdentity frames, %s: min(A read against B, B read against A)" label
    printfn "| size | %s |" (pairs |> List.map (fun (a, b) -> sprintf "%A/%A" a b) |> String.concat " | ")
    printfn "|---|%s" (pairs |> List.map (fun _ -> "---|") |> String.concat "")
    for size in [ 6.0; 8.0; 12.0; 16.0; 24.0; 32.0 ] do
        let cells = pairs |> List.map (fun (a, b) -> min (differ sampler (frame a size) (frame b size)) (differ sampler (frame b size) (frame a size)))
        printfn "| %g | %s |" size (cells |> List.map string |> String.concat " | ")
// The badge digits 0 to 9 at the minimum unit (2, so 10 units tall), from the kit's own digit cases.
let digitScene d =
    (GlyphConformance.cases [ Theme.classic ] |> List.find (fun (c: GlyphCase) -> c.Name = sprintf "badge digit %d" d)).Scenes.Head.Nodes
let digitDiffer sampler a b =
    sampler 16.0 16.0 { Nodes = a } |> List.filter (fun (e: PixelExpectation) -> match colourAt b (int (floor e.Point.X)) (int (floor e.Point.Y)) with Some o -> o <> e.Colour | None -> false) |> List.length
for label, sampler in samplers do
    let cells = [ for a in 0 .. 9 do for b in a + 1 .. 9 -> (a, b), min (digitDiffer sampler (digitScene a) (digitScene b)) (digitDiffer sampler (digitScene b) (digitScene a)) ]
    let zeros = cells |> List.filter (fun (_, v) -> v = 0)
    printfn "\nBadge digits 0-9, %s: %d pairs, smallest %d, zero pairs %d %A" label cells.Length (cells |> List.map snd |> List.min) zeros.Length (zeros |> List.map fst)
// Frames with a team mark and the badge inside them (one-digit teams 1-9), monochrome: for every frame pair, the worst count
// over all 81 team combinations. The badge and the team mark sit inside the frame, away from its edges and corners.
for label, sampler in samplers do
    for size in [ 24.0; 32.0 ] do
        let t = themeFor size
        let framed identity team = glyph t (unit identity |> GlyphSpec.team team |> GlyphSpec.badged)
        let worst =
            [ for a, b in pairs ->
                let cells = [ for ta in 0 .. 7 do for tb in 0 .. 7 -> min (differ sampler (framed a ta) (framed b tb)) (differ sampler (framed b tb) (framed a ta)) ]
                sprintf "%A/%A %d" a b (List.min cells) ]
        printfn "\nFrames with team marks and badges inside, %g units, %s (worst over the classic teams): %s" size label (String.concat ", " worst)
