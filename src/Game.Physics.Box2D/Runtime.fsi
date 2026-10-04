namespace FS.GG.Game.Physics.Box2D

/// Owns one mutable engine world. Use and dispose on one simulation owner; concurrent access is
/// unsupported on the same instance. World allocation/disposal are serialized across adapter instances
/// because the pinned engine has a shared world-slot table; independent worlds may step concurrently.
/// Direct engine lifetime calls outside this adapter must not race these operations. Validation failures leave the world usable. An engine failure stops further steps;
/// disposal remains available. Dispose is idempotent. Snapshots remain usable after disposal.
[<Sealed>]
type Runtime =
    new: settings: Settings -> Runtime
    /// The pinned fixed solver profile.
    member Settings: Settings
    member internal ValidateBatch: commands: Command list -> unit
    member internal IsStopped: bool
    member internal Inspect: unit -> Snapshot option
    member internal Stop: unit -> unit
    member internal Describe: entity: string -> BodyDescriptor * bool * bool
    member internal Stage: descriptor: BodyDescriptor * awake: bool -> unit
    member internal RemoveWithoutStep: entity: string -> unit
    member internal SetAwake: entity: string * awake: bool -> unit
    /// Copy the current poses without stepping; events are empty.
    member Snapshot: unit -> Snapshot
    /// Validate the full command batch, apply in order, step once and copy all observations.
    /// Events for shapes removed this tick retain their original entity IDs; unknown engine shape
    /// IDs stop the runtime instead of silently dropping observations. Engine IDs never escape.
    member Step: commands: Command list -> Snapshot
    interface System.IDisposable
