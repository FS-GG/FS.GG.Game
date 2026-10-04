namespace FS.GG.Game.Core

/// Immutable local-area connections and backend-independent crossing decisions.
[<RequireQualifiedAccess>]
module AreaTopology =
    /// Stable area identity, independent of a physics world handle.
    type AreaId = AreaId of string
    /// Stable directed portal identity.
    type PortalId = PortalId of string
    /// Translation and counter-clockwise rotation in radians; no scale or reflection.
    type RigidMap
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
    type Graph
    /// Backend-independent pose and momentum. Angular velocity is unchanged by a rigid map.
    type Motion = { Position: Point; Velocity: Point; Orientation: float; AngularVelocity: float }
    /// At most one proposed crossing, before any backend mutation or destination admission.
    type Crossing = { Portal: Portal; Fraction: float; SourcePoint: Point; DestinationPoint: Point; MappedMotion: Motion }

    /// Construct a translation/rotation map, refusing non-finite values. Angles are radians.
    val rigidMap: angle: float -> translation: Point -> Result<RigidMap, string>

    /// Rotate a vector without translation.
    val mapVector: mapping: RigidMap -> vector: Point -> Point

    /// Rotate then translate a position.
    val mapPoint: mapping: RigidMap -> point: Point -> Point

    /// Return the inverse rigid mapping.
    val inverse: mapping: RigidMap -> RigidMap

    /// Transform position, velocity and orientation; preserve angular velocity.
    /// Caller supplies finite motion; tryCrossing refuses non-finite input or mapped output.
    val mapMotion: mapping: RigidMap -> motion: Motion -> Motion

    /// Validate unique non-empty IDs, references, non-degenerate apertures and mapped endpoints.
    /// Declared reverse pairs must be reciprocal, inverse and admit opposite physical traversal.
    /// Geometry and inverse checks use absolute 1e-9 tolerance in caller-selected local units.
    val create: areas: seq<AreaId> -> portals: seq<Portal> -> Result<Graph, string>

    /// Return directed edges in stable PortalId order. Reverse edges are never implicit.
    val outgoing: area: AreaId -> graph: Graph -> Portal list

    /// Select earliest swept crossing, ties by PortalId; at most one proposal per call.
    /// Radius is the body's conservative enclosing-disc radius; zero admits a point.
    /// Clearance includes both finite apertures. Non-finite motion/radius, negative radius,
    /// unknown area, backward, parallel and line-start movement returns None. blocked contains
    /// openings not re-armed yet. Position is the mapped tick-end pose, without a second step.
    /// Backend owns per-entity/tick admission, hysteresis and destination occupancy/capacity/state
    /// validation before commit. A proposal does not imply successful transfer.
    val tryCrossing:
        graph: Graph ->
        area: AreaId ->
        blocked: Set<PortalId> ->
        radius: float ->
        previous: Point ->
        motion: Motion ->
        Crossing option
