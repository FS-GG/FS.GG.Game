// Distinguishability of the identity frames (#794 review R3). A frame A is told from B when some pixel the kit decides
// for A (ScenePixels.expectations, today's samples; or the proposed corner/quarter-edge samples) is decided in B's
// drawing with a different colour. Monochrome: one ink on the ground, so only shape counts.
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
let centre = { X = 48.0; Y = 48.0 }
let nodes id size = SceneNode.Rectangle((0.0, 0.0, W, W), ground) :: Glyphs.identityFrame id centre size ink ground
let rects ns = ns |> List.choose (function SceneNode.Rectangle((x, y, w, h), col) -> Some(x, y, x + w, y + h, col) | _ -> None)
/// The analytic colour of pixel (col,row) in a drawing of rectangles: Some colour when every rectangle covers it wholly
/// or not at all, None when one covers it in part.
let colourAt (rs: (float * float * float * float * Color) list) (col: int) (row: int) =
    let x0, y0, x1, y1 = float col, float row, float col + 1.0, float row + 1.0
    let mutable top = None
    let mutable partial = false
    for (l, t, r, b, colour) in rs do
        if x1 <= l || x0 >= r || y1 <= t || y0 >= b then ()
        elif x0 >= l && x1 <= r && y0 >= t && y1 <= b then (top <- Some colour; partial <- false)
        else partial <- true
    if partial then None else top
let today id size =
    ScenePixels.expectations W W { Nodes = nodes id size } |> List.map (fun e -> int (floor e.Point.X), int (floor e.Point.Y)) |> Set.ofList
/// Proposed extra samples for identity frames: per bar, the pixel just inside and just outside each corner, and the
/// pixels just inside and outside each edge at a quarter and three quarters of its length.
let extended id size =
    let rs = rects (nodes id size) |> List.tail
    [ for (l, t, r, b, _) in rs do
        let li, ti, ri, bi = int (ceil l), int (ceil t), int (floor r) - 1, int (floor b) - 1
        let lo, to', ro, bo = int (floor l) - 1, int (floor t) - 1, int (ceil r), int (ceil b)
        yield! [ li, ti; ri, ti; li, bi; ri, bi; lo, to'; ro, to'; lo, bo; ro, bo ]
        for f in [ 0.25; 0.75 ] do
            let x = int (floor (l + (r - l) * f))
            let y = int (floor (t + (b - t) * f))
            yield! [ x, ti; x, to'; x, bi; x, bo; li, y; lo, y; ri, y; ro, y ] ]
    |> Set.ofList
    |> Set.union (today id size)
let decided id size samples =
    let rs = rects (nodes id size)
    samples |> Set.toList |> List.choose (fun (col, row) -> colourAt rs col row |> Option.map (fun colour -> (col, row), colour))
let control sampler a b size =
    let rb = rects (nodes b size)
    decided a size (sampler a size) |> List.filter (fun ((col, row), colour) -> match colourAt rb col row with Some other -> other <> colour | None -> false) |> List.length
let name = sprintf "%A"
for label, sampler in [ "today's ScenePixels samples", today; "with corner and quarter-edge samples (proposed, D6)", extended ] do
    printfn "\n%s: min(A read against B, B read against A); 0 means the kit cannot tell the pair apart" label
    let pairs = [ for i, a in List.indexed Glyphs.identities do for b in List.skip (i + 1) Glyphs.identities -> a, b ]
    printfn "| size | %s |" (pairs |> List.map (fun (a, b) -> sprintf "%s/%s" (name a) (name b)) |> String.concat " | ")
    printfn "|---|%s" (pairs |> List.map (fun _ -> "---|") |> String.concat "")
    for size in [ 6.0; 8.0; 10.0; 12.0; 16.0; 24.0; 32.0 ] do
        let cells = pairs |> List.map (fun (a, b) -> min (control sampler a b size) (control sampler b a size))
        printfn "| %g | %s |" size (cells |> List.map string |> String.concat " | ")
