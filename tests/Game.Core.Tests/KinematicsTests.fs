module Game.Core.Tests.KinematicsTests

open Expecto
open FS.GG.Game.Core

let private rect x y width height =
    {
        X = x
        Y = y
        Width = width
        Height = height
    }

let private point x y = { X = x; Y = y }

[<Tests>]
let tests =
    testList
        "Kinematics"
        [
            testCase "narrow phase agrees for AABB circle and convex fixtures"
            <| fun _ ->
                let moving = rect 0.0 0.0 2.0 2.0
                let box = KinematicShape.AxisAlignedBox(rect 1.0 0.0 2.0 2.0)
                let circle = KinematicShape.Circle { Center = point 2.0 1.0; Radius = 1.5 }

                let polygon =
                    KinematicShape.Convex(Geometry.obbPolygon (point 2.0 1.0) (point 1.0 1.0) 0.25)

                Expect.isSome (Kinematics.contact moving box) "AABB overlap"
                Expect.isSome (Kinematics.contact moving circle) "circle overlap"
                Expect.isSome (Kinematics.contact moving polygon) "convex overlap"

            testCase "broad phase contains every directly swept AABB"
            <| fun _ ->
                let colliders =
                    [
                        for index in 0..20 ->
                            {
                                Id = string index
                                Shape = KinematicShape.AxisAlignedBox(rect (float index * 3.0) 0.0 1.0 1.0)
                                Response = KinematicResponse.Slide
                            }
                    ]

                let motion =
                    {
                        Bounds = rect 0.0 0.0 1.0 1.0
                        Displacement = point 30.0 0.0
                    }

                let candidateIds =
                    Kinematics.candidates 4.0 motion colliders |> List.map _.Id |> Set.ofList

                let directIds =
                    colliders
                    |> List.choose (fun collider ->
                        match collider.Shape with
                        | KinematicShape.AxisAlignedBox target when
                            Geometry.sweptIntersects motion.Bounds motion.Displacement target
                            ->
                            Some collider.Id
                        | _ -> None)

                for id in directIds do
                    Expect.isTrue (candidateIds.Contains id) $"candidate {id}"

            testCase "segment casts preserve the shared query answers"
            <| fun _ ->
                let p0, p1 = point -2.0 0.0, point 4.0 0.0
                let box = rect 0.0 -1.0 1.0 2.0
                let expected = Geometry.segmentAabbHit p0 p1 box
                let actual = Kinematics.segmentHit p0 p1 (KinematicShape.AxisAlignedBox box)
                Expect.equal actual expected "adapter delegates the cast"

            testCase "swept motion cannot tunnel through a thin obstacle"
            <| fun _ ->
                let wall =
                    {
                        Id = "wall"
                        Shape = KinematicShape.AxisAlignedBox(rect 5.0 -2.0 0.1 4.0)
                        Response = KinematicResponse.Slide
                    }

                let result =
                    Kinematics.advance
                        2.0
                        {
                            Bounds = rect 0.0 -0.5 1.0 1.0
                            Displacement = point 20.0 0.0
                        }
                        [ wall ]

                Expect.isTrue (result.Hits |> List.exists (fun hit -> hit.ColliderId = "wall")) "wall hit"
                Expect.isLessThanOrEqual (result.Bounds.X + result.Bounds.Width) 5.0000001 "stopped at wall"
                Expect.floatClose Accuracy.high 0.0 result.Displacement.X "normal movement removed"

            testCase "triggers report without changing motion"
            <| fun _ ->
                let trigger =
                    {
                        Id = "pickup"
                        Shape = KinematicShape.AxisAlignedBox(rect 2.0 0.0 1.0 1.0)
                        Response = KinematicResponse.Trigger
                    }

                let motion =
                    {
                        Bounds = rect 0.0 0.0 1.0 1.0
                        Displacement = point 4.0 0.0
                    }

                let result = Kinematics.advance 2.0 motion [ trigger ]
                Expect.equal result.Bounds (rect 4.0 0.0 1.0 1.0) "trigger is not response"
                Expect.isTrue result.Hits.Head.IsTrigger "classified as trigger"

            testCase "bounce reverses the normal component"
            <| fun _ ->
                let wall =
                    {
                        Id = "wall"
                        Shape = KinematicShape.AxisAlignedBox(rect 2.0 -2.0 1.0 4.0)
                        Response = KinematicResponse.Bounce 1.0
                    }

                let result =
                    Kinematics.advance
                        2.0
                        {
                            Bounds = rect 0.0 -0.5 1.0 1.0
                            Displacement = point 4.0 1.0
                        }
                        [ wall ]

                Expect.floatClose Accuracy.high -4.0 result.Displacement.X "normal component reflected"
                Expect.floatClose Accuracy.high 1.0 result.Displacement.Y "tangent preserved"
        ]
