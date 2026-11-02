// Distinguishability of the identity frames and the index badges (FS.GG.Game#789; #794 reviews R3, N1 and N2). A drawing A
// is told from B when some pixel decided for A is decided in B's drawing with a different colour. Monochrome: one ink on
// the ground, so only shape counts. Two samplers: today's ScenePixels.expectations, and the experimental prototype of
// ADR D6's corner and quarter-edge samples (Glyphs.identitySamples). Frames are drawn at whole-unit centres, which
// identityFrame enforces by snapping. Run after building tests/Game.Render.Tests:
//   dotnet fsi docs/roadmaps/game-render-02-glyphs/frames.fsx
#r "../../../tests/Game.Render.Tests/bin/Debug/net10.0/FS.GG.UI.Scene.dll"
#r "../../../tests/Game.Render.Tests/bin/Debug/net10.0/FS.GG.Game.Render.dll"
#r "../../../tests/Game.Render.Tests/bin/Debug/net10.0/FS.GG.Game.Reference.dll"
open FS.GG.UI.Scene
open FS.GG.Game.Render
open FS.GG.Game.Reference.Experimental
let c r g b : Color = { Red = r; Green = g; Blue = b; Alpha = 255uy }
let ground = c 34uy 40uy 49uy
let ink = c 225uy 228uy 232uy
let W = 96.0
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
let frame id size = onGround (Glyphs.identityFrame id { X = 48.0; Y = 48.0 } size ink)
let samplers = [ "today's ScenePixels samples", ScenePixels.expectations; "with D6's corner and quarter-edge samples", Glyphs.identitySamples ]
for label, sampler in samplers do
    printfn "\nIdentity frames, %s: min(A read against B, B read against A)" label
    let pairs = [ for i, a in List.indexed Glyphs.identities do for b in List.skip (i + 1) Glyphs.identities -> a, b ]
    printfn "| size | %s |" (pairs |> List.map (fun (a, b) -> sprintf "%A/%A" a b) |> String.concat " | ")
    printfn "|---|%s" (pairs |> List.map (fun _ -> "---|") |> String.concat "")
    for size in [ 6.0; 8.0; 12.0; 16.0; 24.0; 32.0 ] do
        let cells = pairs |> List.map (fun (a, b) -> min (differ sampler (frame a size) (frame b size)) (differ sampler (frame b size) (frame a size)))
        printfn "| %g | %s |" size (cells |> List.map string |> String.concat " | ")
for label, sampler in samplers do
    let badge d = onGround (Glyphs.badge d { X = 40.0; Y = 40.0 } 2.0 ink)
    let cells = [ for a in 1 .. 9 do for b in a + 1 .. 9 -> (a, b), min (differ sampler (badge a) (badge b)) (differ sampler (badge b) (badge a)) ]
    let zeros = cells |> List.filter (fun (_, v) -> v = 0)
    printfn "\nBadges at the minimum unit (2, so 10 units tall), %s: %d pairs, smallest %d, zero pairs %d %A" label cells.Length (cells |> List.map snd |> List.min) zeros.Length (zeros |> List.map fst)
// Frames with the team index badge drawn inside them (review M1 of #794): for every pair and all 81 digit combinations,
// the worst count per pair. Equal to the bare-frame matrix means the badge does not disturb identity sampling.
let th = Glyphs.Theme.standard
let framed id size team = onGround (fst (Glyphs.identityFrameWithBadge id { X = 48.0; Y = 48.0 } size th.Body th.BadgeInk team))
for label, sampler in samplers do
    for size in [ 24.0; 32.0 ] do
        let worst =
            [ for i, a in List.indexed Glyphs.identities do
                for b in List.skip (i + 1) Glyphs.identities do
                    let cells = [ for ta in 0 .. 8 do for tb in 0 .. 8 -> min (differ sampler (framed a size (Some ta)) (framed b size (Some tb))) (differ sampler (framed b size (Some tb)) (framed a size (Some ta))) ]
                    yield sprintf "%A/%A %d" a b (List.min cells) ]
        printfn "\nFrames with inside badges, %s, size %g, worst over 81 digit pairs: %s" label size (String.concat " | " worst)
