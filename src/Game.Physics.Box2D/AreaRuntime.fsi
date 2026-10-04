namespace FS.GG.Game.Physics.Box2D

open System
open FS.GG.Game.Core
open FS.GG.Game.Physics.Box2D

/// An independent active local world with a whole-body capacity, including static/sensor bodies.
type AreaSettings = { Area: AreaTopology.AreaId; Physics: Settings; Capacity: int }
/// A game command routed to exactly one active local world.
type AreaCommand = { Area: AreaTopology.AreaId; Command: Command }
/// Copied whole-body traversal, published only after source removal and ownership commit.
type Traversal =
    { Tick: int64; Portal: AreaTopology.PortalId; Entity: string
      Source: AreaTopology.AreaId; Destination: AreaTopology.AreaId
      SourcePose: Pose; DestinationPose: Pose }
/// A known refusal preserves source ownership; uncertain engine mutation stops the whole owner.
type TransferRefusal =
    | MissingDestination | CapacityExceeded | DestinationBlocked | JointConnected | StagingRejected of reason: string
/// A refused proposal for one entity during one tick.
type RefusedTraversal = { Entity: string; Portal: AreaTopology.PortalId; Reason: TransferRefusal }
/// Immutable copied observations after all local steps and transfers.
type AreaSnapshot =
    { Tick: int64; Areas: Map<AreaTopology.AreaId, Snapshot>
      Ownership: Map<string, AreaTopology.AreaId>; Traversals: Traversal list; Refused: RefusedTraversal list }
/// Explicit failure disposition. A stopped result is never successful acceptance and may contain
/// staged duplicates; LastKnown is the last fully published immutable state, not a rollback promise.
type AreaStepResult = Advanced of AreaSnapshot | Stopped of reason: string * lastKnown: AreaSnapshot

/// Owns worlds on one simulation owner. Circle/box fixtures retain scale, density, material,
/// kind and sensor setting; current pose, velocities and engine awake state are preserved.
/// Filters, damping, gravity scale and sleep settings use the existing Runtime fixed defaults.
/// Destination placement conservatively refuses overlap of enclosing discs (sensors excluded).
/// Joint-connected bodies refuse. Self-area proposals refuse duplicate staging in this first slice.
/// All worlds step once before collection/transfer; destination contacts begin on its next tick.
/// Engine uncertainty stops this owner and retains its worlds; dispose explicitly after diagnosis.
[<Sealed>]
type AreaRuntime =
    new: graph: AreaTopology.Graph * settings: AreaSettings list * hysteresis: float -> AreaRuntime
    /// Last fully published immutable observation; unavailable after stop or disposal.
    member Snapshot: unit -> AreaSnapshot
    /// Best-effort copied actual state after stopping, without stepping. None retains an unreadable
    /// world as unknown; no snapshot here certifies ownership or recovery.
    member InspectStopped: unit -> Map<AreaTopology.AreaId, Snapshot option>
    member internal CommitFault: (unit -> unit) option with set
    /// Validate every routed batch first, step once per active area, propose once per entity,
    /// then stage/commit without stepping. Ordinary refusals preserve source ownership.
    member Step: commands: AreaCommand list -> AreaStepResult
    member internal SetAwake: area: AreaTopology.AreaId * entity: string * awake: bool -> unit
    member internal BodyState: area: AreaTopology.AreaId * entity: string -> BodyDescriptor * bool * bool
    member internal LocalTicks: Map<AreaTopology.AreaId, int64>
    interface IDisposable

/// Portal-aware presentation snaps traversals and interpolates other entities normally.
[<RequireQualifiedAccess>]
module AreaPhysics =
    /// Validate alpha and consecutive ticks; return current-area pose maps.
    val interpolate: alpha: float -> previous: AreaSnapshot -> current: AreaSnapshot -> Map<AreaTopology.AreaId, Map<string, Pose>>
