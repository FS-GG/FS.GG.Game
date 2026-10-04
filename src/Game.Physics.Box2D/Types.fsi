namespace FS.GG.Game.Physics.Box2D

open FS.GG.Game.Core

/// Simulation units are metres, seconds, kilograms and counterclockwise radians; +Y points up.
/// Points use doubles at the boundary and are checked before conversion to Box2D's single precision.
type BodyKind = Static | Kinematic | Dynamic

/// One centred, unscaled fixture per body in the first adapter contract.
type Shape = Circle of radius: float | Box of halfWidth: float * halfHeight: float

/// Immutable creation descriptor. Density is kg/m²; friction/restitution are nonnegative.
type BodyDescriptor =
    { Entity: string
      Kind: BodyKind
      Shape: Shape
      Position: Point
      Rotation: float
      LinearVelocity: Point
      AngularVelocity: float
      Density: float
      Friction: float
      Restitution: float
      IsSensor: bool }

/// A rigid distance joint joining the two body origins, measured in metres.
type DistanceJointDescriptor = { Joint: string; EntityA: string; EntityB: string; Length: float }

/// Applied in list order at the start of one fixed tick. Unknown/duplicate IDs refuse the entire
/// batch before mutation. Removing a body also removes its attached joints; entity IDs may be reused
/// on later ticks, but never after removal in the same batch.
type Command =
    | CreateBody of BodyDescriptor
    | RemoveBody of entity: string
    | ApplyForce of entity: string * newtons: Point
    | ApplyImpulse of entity: string * kilogramMetresPerSecond: Point
    | CreateDistanceJoint of DistanceJointDescriptor
    | RemoveJoint of joint: string

/// Fixed solver profile. No presentation time is accepted by Runtime.Step. The engine runs without
/// a task callback, using its single-worker default and continuous collision detection.
type Settings = { Gravity: Point; TickSeconds: float; Substeps: int }

/// Copied observation, independent of the runtime lifetime and engine IDs.
type Pose = { Position: Point; Rotation: float; LinearVelocity: Point; AngularVelocity: float }

/// Sensor pairs retain sensor/visitor order; contact pairs are sorted by ordinal entity ID.
type PhysicsEvent =
    | SensorEntered of sensor: string * visitor: string
    | SensorExited of sensor: string * visitor: string
    | ContactStarted of entityA: string * entityB: string
    | ContactEnded of entityA: string * entityB: string

/// Immutable map and list; no borrowed engine arrays or mutable handles escape.
type Snapshot = { Tick: int64; Bodies: Map<string, Pose>; Events: PhysicsEvent list }

/// Pure defaults and presentation interpolation for consecutive fixed snapshots.
[<RequireQualifiedAccess>]
module Physics =
    /// Default profile: 60 Hz, four substeps, gravity (0,-10).
    val defaultSettings: Settings
    /// Dynamic circle defaults; use record updates to choose the rest of the descriptor.
    val circle: entity: string -> radius: float -> position: Point -> BodyDescriptor
    /// Interpolate position and shortest-arc angle with alpha in [0,1]. Velocities use current data.
    /// Newly created bodies snap to current; removed bodies disappear. Throws for invalid alpha or
    /// nonconsecutive ticks. This is presentation only and cannot restore hidden solver state.
    val interpolate: alpha: float -> previous: Snapshot -> current: Snapshot -> Map<string, Pose>
