namespace FS.GG.Game.Core

open System

[<RequireQualifiedAccess>]
type KinematicShape =
    | AxisAlignedBox of Rect
    | Circle of Circle
    | Convex of ConvexPolygon

[<RequireQualifiedAccess>]
type KinematicResponse =
    | Trigger
    | Slide
    | Bounce of restitution: float

type KinematicCollider =
    {
        Id: string
        Shape: KinematicShape
        Response: KinematicResponse
    }

type KinematicHit =
    {
        ColliderId: string
        Contact: Contact
        Sweep: RayHit option
        IsTrigger: bool
    }

type KinematicMotion = { Bounds: Rect; Displacement: Point }

type KinematicResult =
    {
        Bounds: Rect
        Displacement: Point
        Hits: KinematicHit list
        CandidateIds: string list
    }

[<RequireQualifiedAccess>]
module Kinematics =

    let private finite value =
        not (Double.IsNaN value || Double.IsInfinity value)

    let private polygonBounds (polygon: ConvexPolygon) =
        if polygon.Vertices.Length = 0 then
            {
                X = 0.0
                Y = 0.0
                Width = 0.0
                Height = 0.0
            }
        else
            let mutable minX = polygon.Vertices[0].X
            let mutable minY = polygon.Vertices[0].Y
            let mutable maxX = minX
            let mutable maxY = minY

            for point in polygon.Vertices do
                minX <- min minX point.X
                minY <- min minY point.Y
                maxX <- max maxX point.X
                maxY <- max maxY point.Y

            {
                X = minX
                Y = minY
                Width = maxX - minX
                Height = maxY - minY
            }

    let bounds (shape: KinematicShape) : Rect =
        match shape with
        | KinematicShape.AxisAlignedBox value -> value
        | KinematicShape.Circle value ->
            {
                X = value.Center.X - value.Radius
                Y = value.Center.Y - value.Radius
                Width = value.Radius * 2.0
                Height = value.Radius * 2.0
            }
        | KinematicShape.Convex value -> polygonBounds value

    let private rectPolygon (rect: Rect) : ConvexPolygon =
        Geometry.obbPolygon
            (Geometry.center rect)
            {
                X = rect.Width / 2.0
                Y = rect.Height / 2.0
            }
            0.0

    let contact (moving: Rect) (shape: KinematicShape) : Contact option =
        match shape with
        | KinematicShape.AxisAlignedBox value -> Geometry.aabbContact moving value
        | KinematicShape.Circle value ->
            Geometry.circleAabbContact value moving
            |> Option.map (fun found ->
                {
                    Normal =
                        {
                            X = -found.Normal.X
                            Y = -found.Normal.Y
                        }
                    Depth = found.Depth
                })
        | KinematicShape.Convex value -> Geometry.polygonContact (rectPolygon moving) value

    let segmentHit (p0: Point) (p1: Point) (shape: KinematicShape) : RayHit option =
        match shape with
        | KinematicShape.AxisAlignedBox value -> Geometry.segmentAabbHit p0 p1 value
        | KinematicShape.Circle value -> Geometry.segmentCircleHit p0 p1 value
        | KinematicShape.Convex value -> Geometry.segmentPolygonHit p0 p1 value

    let private sweptBounds (motion: KinematicMotion) : Rect =
        let endX = motion.Bounds.X + motion.Displacement.X
        let endY = motion.Bounds.Y + motion.Displacement.Y
        let minX = min motion.Bounds.X endX
        let minY = min motion.Bounds.Y endY

        {
            X = minX
            Y = minY
            Width = motion.Bounds.Width + abs motion.Displacement.X
            Height = motion.Bounds.Height + abs motion.Displacement.Y
        }

    let candidates
        (cellSize: float)
        (motion: KinematicMotion)
        (colliders: KinematicCollider list)
        : KinematicCollider list =
        colliders
        |> Seq.map (fun collider -> bounds collider.Shape, collider)
        |> SpatialGrid.buildBounds cellSize
        |> SpatialGrid.queryBounds (sweptBounds motion)

    let private translate (displacement: Point) (rect: Rect) : Rect =
        { rect with
            X = rect.X + displacement.X
            Y = rect.Y + displacement.Y
        }

    let private expandedForMover (moving: Rect) (shape: KinematicShape) : KinematicShape =
        let halfWidth = max 0.0 moving.Width / 2.0
        let halfHeight = max 0.0 moving.Height / 2.0

        match shape with
        | KinematicShape.AxisAlignedBox value ->
            KinematicShape.AxisAlignedBox
                {
                    X = value.X - halfWidth
                    Y = value.Y - halfHeight
                    Width = value.Width + halfWidth * 2.0
                    Height = value.Height + halfHeight * 2.0
                }
        | KinematicShape.Circle value ->
            KinematicShape.Circle
                { value with
                    Radius = value.Radius + sqrt (halfWidth * halfWidth + halfHeight * halfHeight)
                }
        | KinematicShape.Convex value -> KinematicShape.Convex value

    let private sweep (moving: Rect) (displacement: Point) (shape: KinematicShape) : RayHit option =
        let p0 = Geometry.center moving

        let p1 =
            {
                X = p0.X + displacement.X
                Y = p0.Y + displacement.Y
            }

        segmentHit p0 p1 (expandedForMover moving shape)

    let private hit
        (isTrigger: bool)
        (collider: KinematicCollider)
        (overlap: Contact option)
        (swept: RayHit option)
        : KinematicHit =
        let contactValue =
            match overlap, swept with
            | Some value, _ -> value
            | None, Some value ->
                {
                    Normal =
                        {
                            X = -value.Normal.X
                            Y = -value.Normal.Y
                        }
                    Depth = 0.0
                }
            | None, None ->
                {
                    Normal = { X = 0.0; Y = 0.0 }
                    Depth = 0.0
                }

        {
            ColliderId = collider.Id
            Contact = contactValue
            Sweep = swept
            IsTrigger = isTrigger
        }

    let private bounce (restitution: float) (displacement: Point) (normal: Point) : Point =
        let e =
            if finite restitution then
                max 0.0 (min 1.0 restitution)
            else
                0.0

        let dot = displacement.X * normal.X + displacement.Y * normal.Y

        {
            X = displacement.X - (1.0 + e) * dot * normal.X
            Y = displacement.Y - (1.0 + e) * dot * normal.Y
        }

    let advance (cellSize: float) (motion: KinematicMotion) (colliders: KinematicCollider list) : KinematicResult =
        let found = candidates cellSize motion colliders
        let endBounds = translate motion.Displacement motion.Bounds

        let observations =
            found
            |> List.choose (fun collider ->
                let overlap =
                    contact endBounds collider.Shape
                    |> Option.orElseWith (fun () -> contact motion.Bounds collider.Shape)

                let swept = sweep motion.Bounds motion.Displacement collider.Shape

                if overlap.IsNone && swept.IsNone then
                    None
                else
                    let isTrigger = collider.Response = KinematicResponse.Trigger
                    Some(collider, hit isTrigger collider overlap swept))

        let solid =
            observations
            |> List.filter (fun (_, value) -> not value.IsTrigger)
            |> List.sortBy (fun (collider, value) ->
                value.Sweep |> Option.map _.T |> Option.defaultValue -1.0, collider.Id)
            |> List.tryHead

        let finalBounds, remaining =
            match solid with
            | None -> endBounds, motion.Displacement
            | Some(collider, value) ->
                match value.Sweep with
                | Some swept ->
                    let travel =
                        {
                            X = motion.Displacement.X * swept.T
                            Y = motion.Displacement.Y * swept.T
                        }

                    let atHit = translate travel motion.Bounds

                    let response =
                        match collider.Response with
                        | KinematicResponse.Slide -> Resolution.slide motion.Displacement value.Contact.Normal
                        | KinematicResponse.Bounce restitution ->
                            bounce restitution motion.Displacement value.Contact.Normal
                        | KinematicResponse.Trigger -> motion.Displacement

                    atHit, response
                | None ->
                    let separated = Resolution.pushOut (Geometry.center endBounds) value.Contact

                    let corrected =
                        { endBounds with
                            X = separated.X - endBounds.Width / 2.0
                            Y = separated.Y - endBounds.Height / 2.0
                        }

                    let response =
                        match collider.Response with
                        | KinematicResponse.Slide -> Resolution.slide motion.Displacement value.Contact.Normal
                        | KinematicResponse.Bounce restitution ->
                            bounce restitution motion.Displacement value.Contact.Normal
                        | KinematicResponse.Trigger -> motion.Displacement

                    corrected, response

        {
            Bounds = finalBounds
            Displacement = remaining
            Hits = observations |> List.map snd
            CandidateIds = found |> List.map _.Id
        }
