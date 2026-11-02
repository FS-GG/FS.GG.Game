module Game.Render.Tests.ScenePixelsTests

// FS.GG.Game#753: `ScenePixels.expectations` decides which pixels of a scene a backend must show, at which colour,
// and leaves out every pixel the scene does not decide. The browser run of the kit's pixels section, which reads
// them back from SVG, Pixi and Babylon, is tests/Render.Browser.Smoke.

open Expecto
open FS.GG.UI.Scene
open FS.GG.Game.Render

let private rgb red green blue : Color =
    {
        Red = red
        Green = green
        Blue = blue
        Alpha = 255uy
    }

let private background = rgb 10uy 20uy 30uy
let private square = rgb 200uy 40uy 90uy

let private colourAt (x: float) (y: float) (expectations: PixelExpectation list) : Color option =
    expectations
    |> List.tryFind (fun expectation -> expectation.Point.X = x && expectation.Point.Y = y)
    |> Option.map (fun expectation -> expectation.Colour)

let private scene nodes : Scene = { Nodes = nodes }

[<Tests>]
let tests =
    testList
        "ScenePixels"
        [
            test "a rectangle is decided at the first whole pixel inside and outside each edge, and at its middle" {
                let found =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square)
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 10.5 40.5 found) (Some square) "inside the left edge"
                Expect.equal (colourAt 9.5 40.5 found) (Some background) "outside the left edge"
                Expect.equal (colourAt 39.5 40.5 found) (Some square) "inside the right edge"
                Expect.equal (colourAt 40.5 40.5 found) (Some background) "outside the right edge"
                Expect.equal (colourAt 25.5 20.5 found) (Some square) "inside the top edge"
                Expect.equal (colourAt 25.5 19.5 found) (Some background) "outside the top edge"
                Expect.equal (colourAt 25.5 59.5 found) (Some square) "inside the bottom edge"
                Expect.equal (colourAt 25.5 60.5 found) (Some background) "outside the bottom edge"
                Expect.equal (colourAt 25.5 40.5 found) (Some square) "the middle"
            }

            test "a pixel an edge only partly covers is not decided" {
                let found =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.5, 20.0, 30.0, 40.0), square)
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 10.5 40.5 found) None "the half-covered pixel is left open"
                Expect.equal (colourAt 11.5 40.5 found) (Some square) "the first whole pixel inside"
                Expect.equal (colourAt 9.5 40.5 found) (Some background) "the first whole pixel outside"
            }

            test "a later node that covers a pixel wins, and one that covers it in part leaves it open" {
                let found =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square)
                            SceneNode.Rectangle((0.0, 0.0, 25.5, 100.0), rgb 1uy 2uy 3uy)
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 25.5 40.5 found) None "the later node's edge runs through the middle pixel"
                Expect.equal (colourAt 10.5 40.5 found) (Some(rgb 1uy 2uy 3uy)) "the later node is on top"
            }

            test "translucent nodes decide nothing beneath or on them" {
                let found =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), { square with Alpha = 128uy })
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 25.5 40.5 found) None "inside the translucent node"
                Expect.equal (colourAt 9.5 40.5 found) (Some background) "outside it"
            }

            test "groups and translations place the node" {
                let found =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Translate((5.0, 5.0), scene [ SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square) ])
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 15.5 45.5 found) (Some square) "inside the translated left edge"
                Expect.equal (colourAt 14.5 45.5 found) (Some background) "outside it"
            }

            test "a circle is decided at its centre and one clear of each extreme, not inside its edge" {
                let found =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Circle({ X = 50.0; Y = 50.0 }, 4.0, square)
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 50.5 50.5 found) (Some square) "the pixel at the centre"
                Expect.equal (colourAt 55.5 50.5 found) (Some background) "one clear of the right extreme"
                Expect.equal (colourAt 44.5 50.5 found) (Some background) "one clear of the left extreme"
                Expect.equal (colourAt 50.5 55.5 found) (Some background) "one clear of the bottom extreme"
                Expect.equal (colourAt 50.5 44.5 found) (Some background) "one clear of the top extreme"
                Expect.equal (colourAt 54.5 50.5 found) None "the pixel the extreme touches is left open"
                Expect.equal (colourAt 53.5 50.5 found) None "no pixel inside the edge of a shaded shape"
            }

            test "where nothing is drawn the clear colour is expected, so a node drawn larger than declared fails" {
                let found =
                    scene [ SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square) ] |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 9.5 40.5 found) (Some(rgb 0uy 0uy 0uy)) "outside the only node"
                Expect.equal (colourAt 40.5 40.5 found) (Some(rgb 0uy 0uy 0uy)) "outside its right edge"
                Expect.equal (colourAt 10.5 40.5 found) (Some square) "inside it"
            }

            test "a node of a kind the kit does not model leaves open every pixel it could overdraw, and the clear colour is expected nowhere" {
                let text = SceneNode.Text((12.0, 30.0), "score", rgb 255uy 255uy 255uy)

                let overdrawn =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square)
                            text
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.isEmpty overdrawn "the text is drawn last and may cover any pixel"

                let beneath =
                    scene
                        [
                            text
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square)
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 10.5 40.5 beneath) (Some square) "an opaque node drawn after the text decides its pixels"
                Expect.equal (colourAt 9.5 40.5 beneath) (Some background) "and so does the background"

                let apart =
                    scene
                        [
                            SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square)
                            text
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.isEmpty apart "no pixel is expected to be the clear colour when an unmodelled node may draw there"
            }

            test "a node with known bounds leaves open only the pixels inside its bounds grown by its stroke reach (ADR D6, #789)" {
                let stroke width : Paint =
                    {
                        Fill = None
                        Stroke = Some { Width = width; Cap = Butt; Join = Bevel; Miter = 4.0 }
                        Opacity = 1.0
                        Antialias = true
                        BlendMode = SrcOver
                        Shader = None
                        ColorFilter = NoColorFilter
                        MaskFilter = NoMaskFilter
                        ImageFilter = NoImageFilter
                        PathEffect = NoPathEffect
                    }

                let path =
                    SceneNode.Path(
                        {
                            Commands = [ MoveTo { X = 40.0; Y = 40.0 }; LineTo { X = 60.0; Y = 40.0 }; LineTo { X = 50.0; Y = 60.0 }; Close ]
                            FillType = Winding
                        },
                        stroke 2.0
                    )

                let found =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square)
                            path
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                // The path's box is 40..60 x 40..60, grown by its stroke width (2) and one unit: 37..63 x 37..63.
                Expect.equal (colourAt 25.5 40.5 found) (Some square) "the rectangle's middle, clear of the path, stays decided"
                Expect.equal (colourAt 50.5 50.5 found) None "inside the path's bounds nothing is decided"
                Expect.equal (colourAt 39.5 40.5 found) None "the rectangle's right edge pixel lies in the path's reach"
                Expect.equal (colourAt 40.5 40.5 found) None "and so does the pixel just outside it"
                Expect.isGreaterThan found.Length 10 "a path drawn last no longer undecides the scene"

                let mitred =
                    SceneNode.Line(
                        { X = 70.0; Y = 70.0 },
                        { X = 80.0; Y = 70.0 },
                        { stroke 2.0 with Stroke = Some { Width = 2.0; Cap = Butt; Join = Miter; Miter = 4.0 } }
                    )

                let near =
                    scene [ SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background); mitred; SceneNode.Rectangle((60.0, 60.0, 1.0, 1.0), square) ]
                    |> ScenePixels.expectations 100.0 100.0

                // A mitred join reaches 2 x 4 + 1 = 9 units: 61..89 x 61..79.
                Expect.equal (colourAt 60.5 60.5 near) (Some square) "outside the mitred reach"

                let image = SceneNode.Image((30.0, 30.0, 10.0, 10.0), "atlas.png")

                let imaged =
                    scene [ SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background); image ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 50.5 50.5 imaged) (Some background) "an image leaves the background's middle decided"

                let blurred =
                    SceneNode.Path(
                        { Commands = [ MoveTo { X = 40.0; Y = 40.0 }; LineTo { X = 41.0; Y = 41.0 } ]; FillType = Winding },
                        { stroke 1.0 with MaskFilter = Blur 2.0 }
                    )

                let unbounded =
                    scene [ SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background); blurred ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.isEmpty unbounded "a blurred paint cannot be bounded, so it undecides every pixel it could overdraw"
            }

            test "identity samples add every rectangle's corner and quarter-edge pixels after the middle-of-edge ones (ADR D6, #789)" {
                let nodes =
                    scene
                        [
                            SceneNode.Rectangle((0.0, 0.0, 100.0, 100.0), background)
                            SceneNode.Rectangle((10.0, 20.0, 40.0, 20.0), square)
                        ]

                let today = ScenePixels.expectations 100.0 100.0 nodes
                let identity = ScenePixels.identityExpectations 100.0 100.0 nodes
                let key (e: PixelExpectation) = e.Point.X, e.Point.Y, e.Colour
                Expect.equal (identity |> List.truncate today.Length |> List.map key) (today |> List.map key) "today's samples come first, unchanged"
                Expect.equal (identity |> List.map (fun e -> e.Point) |> List.distinct |> List.length) identity.Length "no pixel twice"
                Expect.equal (colourAt 10.5 20.5 identity) (Some square) "the top-left corner, inside"
                Expect.equal (colourAt 9.5 19.5 identity) (Some background) "the top-left corner, diagonally outside"
                Expect.equal (colourAt 49.5 39.5 identity) (Some square) "the bottom-right corner, inside"
                Expect.equal (colourAt 50.5 40.5 identity) (Some background) "the bottom-right corner, diagonally outside"
                Expect.equal (colourAt 20.5 20.5 identity) (Some square) "a quarter along the top edge, inside"
                Expect.equal (colourAt 20.5 19.5 identity) (Some background) "a quarter along the top edge, outside"
                Expect.equal (colourAt 40.5 39.5 identity) (Some square) "three quarters along the bottom edge, inside"
                Expect.equal (colourAt 9.5 25.5 identity) (Some background) "a quarter down the left edge, outside"
                Expect.isNone (colourAt 10.5 20.5 today) "today's samples do not read corners"
            }

            test "a node that overhangs the canvas is read at the canvas edge and nowhere outside it" {
                let found =
                    scene
                        [
                            SceneNode.Rectangle((80.0, 20.0, 40.0, 40.0), square)
                            SceneNode.Rectangle((-20.0, 70.0, 40.0, 20.0), square)
                        ]
                    |> ScenePixels.expectations 100.0 100.0

                Expect.equal (colourAt 99.5 40.5 found) (Some square) "the last pixel of the canvas, inside the overhanging node"
                Expect.equal (colourAt 79.5 40.5 found) (Some(rgb 0uy 0uy 0uy)) "outside its left edge"
                Expect.equal (colourAt 0.5 80.5 found) (Some square) "the first pixel of the canvas, inside the other"
                Expect.isTrue
                    (found
                     |> List.forall (fun expectation ->
                         expectation.Point.X > 0.0 && expectation.Point.X < 100.0 && expectation.Point.Y > 0.0 && expectation.Point.Y < 100.0))
                    "no pixel outside the canvas"
            }
        ]
