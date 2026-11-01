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
                    |> ScenePixels.expectations

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
                    |> ScenePixels.expectations

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
                    |> ScenePixels.expectations

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
                    |> ScenePixels.expectations

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
                    |> ScenePixels.expectations

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
                    |> ScenePixels.expectations

                Expect.equal (colourAt 50.5 50.5 found) (Some square) "the pixel at the centre"
                Expect.equal (colourAt 55.5 50.5 found) (Some background) "one clear of the right extreme"
                Expect.equal (colourAt 44.5 50.5 found) (Some background) "one clear of the left extreme"
                Expect.equal (colourAt 50.5 55.5 found) (Some background) "one clear of the bottom extreme"
                Expect.equal (colourAt 50.5 44.5 found) (Some background) "one clear of the top extreme"
                Expect.equal (colourAt 54.5 50.5 found) None "the pixel the extreme touches is left open"
                Expect.equal (colourAt 53.5 50.5 found) None "no pixel inside the edge of a shaded shape"
            }

            test "where nothing is drawn nothing is decided" {
                let found =
                    scene [ SceneNode.Rectangle((10.0, 20.0, 30.0, 40.0), square) ] |> ScenePixels.expectations

                Expect.equal (colourAt 9.5 40.5 found) None "outside the only node"
                Expect.equal (colourAt 10.5 40.5 found) (Some square) "inside it"
            }
        ]
