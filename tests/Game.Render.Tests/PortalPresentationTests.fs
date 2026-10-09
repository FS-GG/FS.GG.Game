module Game.Render.Tests.PortalPresentationTests

open Expecto
open FS.GG.Game.Core
open FS.GG.Game.Physics.Box2D

module Presentation = Box2D.Portals.Presentation

let private a = AreaTopology.AreaId "a"
let private b = AreaTopology.AreaId "b"
let private pose x y : Pose =
    { Position = { X = x; Y = y }
      Rotation = 0.; LinearVelocity = { X = 1.; Y = 0. }; AngularVelocity = 0. }

let private snapshot tick (areas: (AreaTopology.AreaId * (string * Pose) list) list) traversals : AreaSnapshot =
    { Tick = tick
      Areas = areas |> List.map (fun (area, bodies) -> area, { Tick = tick; Bodies = Map.ofList bodies; Events = [] }) |> Map.ofList
      Ownership = areas |> List.collect (fun (area, bodies) -> bodies |> List.map (fun (entity, _) -> entity, area)) |> Map.ofList
      Traversals = traversals; Refused = [] }

let private before = snapshot 1L [ a, [ "traveller", pose -1. 0.; "ordinary", pose -3. 3. ]; b, [] ] []
let private traversal : Traversal =
    { Tick = 2L; Portal = AreaTopology.PortalId "forward"; Entity = "traveller"
      Source = a; Destination = b; SourcePose = pose 0. 0.; DestinationPose = pose 10. 5. }
let private after = snapshot 2L [ b, [ "traveller", pose 10. 5. ]; a, [ "ordinary", pose -1. 4. ] ] [ traversal ]
let private binding entity (frame: Presentation.Frame) =
    frame.Areas |> List.collect (fun area -> area.Bindings) |> List.find (fun (id, _) -> id = entity) |> snd

[<Tests>]
let tests =
    testList "Portal presentation" [
        test "ordinary endpoints and midpoint use copied consecutive poses" {
            for alpha, expected in [ 0., pose -3. 3.; 0.5, pose -2. 3.5; 1., pose -1. 4. ] do
                let frame = Presentation.view alpha before after
                Expect.equal (binding "ordinary" frame) expected "ordinary interpolation"
                Expect.equal (Presentation.inspect after frame) (2, 2) "two actual nodes and two bound points"
        }
        test "traveller snaps only in destination for every alpha" {
            for alpha in [ 0.; 0.5; 1. ] do
                let frame = Presentation.view alpha before after
                Expect.equal (binding "traveller" frame) traversal.DestinationPose "destination pose, no global blend"
                let locations = frame.Areas |> List.collect (fun area -> area.Bindings |> List.choose (fun (id, _) -> if id = "traveller" then Some area.Area else None))
                Expect.equal locations [ b ] "one traveller, destination only"
        }
        test "actual points and deterministic area/entity order match bindings" {
            let frame = Presentation.view 0.5 before after
            Expect.equal (frame.Areas |> List.map (fun area -> area.Area, List.map fst area.Bindings)) [ a, [ "ordinary" ]; b, [ "traveller" ] ] "stable identity order"
            for area in frame.Areas do
                match area.Scene.Nodes with
                | [ FS.GG.UI.Scene.Points(points, _) ] ->
                    let expected: FS.GG.UI.Scene.Point list =
                        if area.Area = a then [ { X = -2.; Y = 3.5 } ] else [ { X = 10.; Y = 5. } ]
                    Expect.equal points expected "actual node coordinates"
                | _ -> failtest "Expected exactly one actual points node per occupied area."
            Expect.equal frame (Presentation.view 0.5 before after) "repeat evaluation structural equality"
        }
        test "invalid alpha and nonconsecutive ticks keep producer refusal" {
            for alpha in [ nan; infinity; -infinity; -0.1; 1.1 ] do
                Expect.throws (fun () -> Presentation.view alpha before after |> ignore) "invalid alpha refuses"
            Expect.throws (fun () -> Presentation.view 0.5 before { after with Tick = 4L } |> ignore) "nonconsecutive tick refuses"
        }
        test "current-only and global-cross-area negative projections fail expected coordinates" {
            let frame = Presentation.view 0.5 before after
            let requireExpected (candidate: Presentation.Frame) =
                Expect.equal (binding "ordinary" candidate) (pose -2. 3.5) "ordinary midpoint"
                Expect.equal (binding "traveller" candidate) (pose 10. 5.) "destination snap"
            let replace entity replacement =
                { frame with
                    Areas =
                        frame.Areas
                        |> List.map (fun area ->
                            { area with
                                Bindings =
                                    area.Bindings
                                    |> List.map (fun (id, pose) -> id, if id = entity then replacement else pose) }) }
            requireExpected frame
            Expect.throws (fun () -> replace "ordinary" after.Areas[a].Bodies["ordinary"] |> requireExpected) "current-only negative control is rejected"
            Expect.throws (fun () -> replace "traveller" (pose 4.5 2.5) |> requireExpected) "global-cross-area negative control is rejected"
        }
        test "two same-area entities retain deterministic point order" {
            let current = snapshot 2L [ a, [ "traveller", pose 0. 1.; "ordinary", pose -1. 4. ]; b, [] ] []
            let frame = Presentation.view 0.5 before current
            Expect.equal (Presentation.inspect current frame) (1, 2) "empty destination contributes no node"
            let area = frame.Areas.Head
            Expect.equal (List.map fst area.Bindings) [ "ordinary"; "traveller" ] "entity comparison order"
            match area.Scene.Nodes with
            | [ FS.GG.UI.Scene.Points(points, _) ] ->
                let expected: FS.GG.UI.Scene.Point list = [ { X = -2.; Y = 3.5 }; { X = -0.5; Y = 0.5 } ]
                Expect.equal points expected "actual points use that same identity order"
            | _ -> failtest "Expected actual same-area points."
        }
        test "missing traveller point and binding both fail coverage" {
            let frame = Presentation.view 0.5 before after
            let missingBinding = { frame with Areas = frame.Areas |> List.filter (fun area -> area.Area <> b) }
            Expect.throws (fun () -> Presentation.inspect after missingBinding |> ignore) "missing traveller fails identity coverage"
            let missingPoint =
                { frame with
                    Areas =
                        frame.Areas
                        |> List.map (fun area ->
                            if area.Area = b then
                                { area with Scene = FS.GG.UI.Scene.Scene.empty }
                            else
                                area) }
            Expect.throws (fun () -> Presentation.inspect after missingPoint |> ignore) "missing traveller point fails actual scene coverage"
        }
        test "finite real example returns inspected presentation frames" {
            let snapshots, frames = Presentation.qualify ()
            Expect.equal snapshots.Length 5 "five actual fixed ticks"
            Expect.equal frames.Length 12 "three alphas per consecutive pair"
            for frame in frames do
                let nodes, points = Presentation.inspect snapshots[int frame.Tick - 1] frame
                Expect.isTrue (nodes >= 1 && nodes <= 2) "occupied-area node bound"
                Expect.equal points 2 "every owned entity projected"
        }
    ]
