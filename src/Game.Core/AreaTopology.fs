namespace FS.GG.Game.Core

open System

/// Immutable local-area connections and backend-independent crossing decisions.
[<RequireQualifiedAccess>]
module AreaTopology =
    /// Stable area identity, independent of a physics world handle.
    type AreaId = AreaId of string
    /// Stable directed portal identity.
    type PortalId = PortalId of string
    /// Translation and counter-clockwise rotation in radians; no scale or reflection.
    type RigidMap = private { Angle: float; Translation: Point }
    /// An oriented finite aperture; its positive side is left of Start to Finish.
    type Opening = { Start: Point; Finish: Point }
    /// Permitted crossing of the source opening's oriented line.
    type Direction = PositiveToNegative | NegativeToPositive
    /// One immutable directed connection. Reverse names an explicitly declared inverse edge.
    type Portal =
        { Id: PortalId
          Source: AreaId
          Destination: AreaId
          SourceOpening: Opening
          DestinationOpening: Opening
          Direction: Direction
          Mapping: RigidMap
          Reverse: PortalId option }
    /// Validated immutable graph. Loops, self-edges and multiple entrances are permitted.
    type Graph = private { Areas: Set<AreaId>; Portals: Map<PortalId, Portal> }
    /// Backend-independent pose and momentum. Angular velocity is unchanged by a rigid map.
    type Motion = { Position: Point; Velocity: Point; Orientation: float; AngularVelocity: float }
    /// At most one proposed crossing, before any backend mutation or destination admission.
    type Crossing = { Portal: Portal; Fraction: float; SourcePoint: Point; DestinationPoint: Point; MappedMotion: Motion }

    let private finite x = Double.IsFinite x
    let private validPoint (p: Point) = finite p.X && finite p.Y
    let private sub (a: Point) (b: Point) : Point = { X = a.X - b.X; Y = a.Y - b.Y }
    let private dot (a: Point) (b: Point) = a.X * b.X + a.Y * b.Y
    let private cross (a: Point) (b: Point) = a.X * b.Y - a.Y * b.X
    let private near a b = abs (a - b) <= 1e-9
    let private nearPoint (a: Point) (b: Point) = near a.X b.X && near a.Y b.Y
    let private validOpening o =
        let edge = sub o.Finish o.Start
        let length2 = dot edge edge
        validPoint o.Start && validPoint o.Finish && finite length2 && length2 > 0.

    /// Refuse non-finite translation or angle; rotation has unit scale by construction.
    let rigidMap angle translation =
        if finite angle && validPoint translation then Ok { Angle = angle; Translation = translation }
        else Error "A rigid map requires finite translation and angle."

    /// Rotate a free vector without translating it.
    let mapVector mapping (vector: Point) : Point =
        let c, s = cos mapping.Angle, sin mapping.Angle
        { X = c * vector.X - s * vector.Y; Y = s * vector.X + c * vector.Y }

    /// Rotate then translate a local position.
    let mapPoint mapping point =
        let rotated = mapVector mapping point
        { X = rotated.X + mapping.Translation.X; Y = rotated.Y + mapping.Translation.Y }

    /// The inverse rigid map, for defining two-way pairs.
    let inverse mapping =
        let rotation = { Angle = -mapping.Angle; Translation = { X = 0.; Y = 0. } }
        { rotation with Translation = mapVector rotation { X = -mapping.Translation.X; Y = -mapping.Translation.Y } }

    /// Map a whole body's pose and velocities; caller must supply finite motion.
    let mapMotion mapping motion =
        { Position = mapPoint mapping motion.Position
          Velocity = mapVector mapping motion.Velocity
          Orientation = motion.Orientation + mapping.Angle
          AngularVelocity = motion.AngularVelocity }

    let private openingsMatch mapping a b =
        let first, last = mapPoint mapping a.Start, mapPoint mapping a.Finish
        (nearPoint first b.Start && nearPoint last b.Finish)
        || (nearPoint first b.Finish && nearPoint last b.Start)

    /// Validate IDs, references, apertures, mapped endpoints and declared inverse pairs.
    /// Endpoint and inverse checks use an absolute 1e-9 tolerance in caller-selected units.
    let create areas portals =
        let areaList = List.ofSeq areas
        let portalList = List.ofSeq portals
        let areaSet = Set.ofList areaList
        let portalMap = portalList |> List.map (fun p -> p.Id, p) |> Map.ofList
        let namedArea (AreaId id) = not (String.IsNullOrWhiteSpace id)
        let namedPortal (PortalId id) = not (String.IsNullOrWhiteSpace id)
        let basicValid p =
            namedPortal p.Id && Set.contains p.Source areaSet && Set.contains p.Destination areaSet
            && validOpening p.SourceOpening && validOpening p.DestinationOpening
            && finite p.Mapping.Angle && validPoint p.Mapping.Translation
            && openingsMatch p.Mapping p.SourceOpening p.DestinationOpening
        let pairValid p =
            match p.Reverse with
            | None -> true
            | Some id ->
                match Map.tryFind id portalMap with
                | None -> false
                | Some r ->
                    let identity point = nearPoint (mapPoint r.Mapping (mapPoint p.Mapping point)) point
                    let edge = sub p.SourceOpening.Finish p.SourceOpening.Start
                    let normal = { X = -edge.Y; Y = edge.X }
                    let mappedNormal = mapVector p.Mapping normal
                    let reverseEdge = sub r.SourceOpening.Finish r.SourceOpening.Start
                    let reverseNormal = { X = -reverseEdge.Y; Y = reverseEdge.X }
                    let sign d = if d = PositiveToNegative then -1. else 1.
                    r.Reverse = Some p.Id && r.Source = p.Destination && r.Destination = p.Source
                    && openingsMatch p.Mapping p.SourceOpening r.SourceOpening
                    && openingsMatch r.Mapping r.SourceOpening p.SourceOpening
                    && identity { X = 0.; Y = 0. } && identity { X = 1.; Y = 0. } && identity { X = 0.; Y = 1. }
                    && sign p.Direction * sign r.Direction * dot mappedNormal reverseNormal < 0.
        if areaList.Length <> areaSet.Count || not (List.forall namedArea areaList) then
            Error "Area IDs must be unique and non-empty."
        elif portalList.Length <> portalMap.Count then Error "Portal IDs must be unique."
        elif not (List.forall basicValid portalList) then Error "A portal has invalid references, aperture or map."
        elif not (List.forall pairValid portalList) then Error "A declared reverse edge must be reciprocal and inverse, with opposite traversal."
        else Ok { Areas = areaSet; Portals = portalMap }

    /// Stable source-edge enumeration by PortalId; topology never implies a reverse edge.
    let outgoing area graph =
        graph.Portals |> Map.toList |> List.choose (fun (_, p) -> if p.Source = area then Some p else None)

    let private fits radius point opening =
        let edge = sub opening.Finish opening.Start
        let length = sqrt (dot edge edge)
        let along = dot (sub point opening.Start) edge / length
        finite along && along >= radius && along <= length - radius

    let private candidate radius previous motion portal =
        let edge = sub portal.SourceOpening.Finish portal.SourceOpening.Start
        let before = cross edge (sub previous portal.SourceOpening.Start)
        let after = cross edge (sub motion.Position portal.SourceOpening.Start)
        let directed =
            match portal.Direction with
            | PositiveToNegative -> before > 0. && after <= 0.
            | NegativeToPositive -> before < 0. && after >= 0.
        if not directed || not (finite before && finite after && finite (before - after)) then None
        else
            let fraction = before / (before - after)
            let delta = sub motion.Position previous
            let point : Point = { X = previous.X + delta.X * fraction; Y = previous.Y + delta.Y * fraction }
            let destination = mapPoint portal.Mapping point
            let mapped = mapMotion portal.Mapping motion
            if not (finite fraction && validPoint point && validPoint destination && validPoint mapped.Position
                    && validPoint mapped.Velocity && finite mapped.Orientation && finite mapped.AngularVelocity)
               || not (fits radius point portal.SourceOpening && fits radius destination portal.DestinationOpening) then None
            else Some { Portal = portal; Fraction = fraction; SourcePoint = point; DestinationPoint = destination; MappedMotion = mapped }

    /// Select earliest swept crossing, ties by PortalId, returning at most one decision.
    /// Radius is a conservative enclosing-disc clearance for the whole supported body, in local units.
    /// Zero radius admits a point. Non-finite motion/radius, negative radius, unknown area, line-start,
    /// parallel and backward segments return None. blocked contains openings not yet re-armed.
    /// This pure proposal does not certify destination occupancy, availability, capacity or body state;
    /// the backend validates those before committing, and owns per-entity/tick admission and hysteresis.
    let tryCrossing graph area blocked radius previous motion =
        if not (finite radius) || radius < 0. || not (validPoint previous && validPoint motion.Position
            && validPoint motion.Velocity && finite motion.Orientation && finite motion.AngularVelocity) then None
        else
            outgoing area graph
            |> List.filter (fun p -> not (Set.contains p.Id blocked))
            |> List.choose (candidate radius previous motion)
            |> List.sortBy (fun crossing -> crossing.Fraction, crossing.Portal.Id)
            |> List.tryHead
