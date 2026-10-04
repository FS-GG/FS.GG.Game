namespace FS.GG.Game.Physics.Box2D

/// Owns one mutable engine world. Use and dispose on one simulation owner; concurrent access is
/// unsupported. Validation failures leave the world usable. An engine failure stops further steps;
/// disposal remains available. Dispose is idempotent. Snapshots remain usable after disposal.
[<Sealed>]
type Runtime =
    new: settings: Settings -> Runtime
    /// The pinned fixed solver profile.
    member Settings: Settings
    /// Copy the current poses without stepping; events are empty.
    member Snapshot: unit -> Snapshot
    /// Validate the full command batch, apply in order, step once and copy all observations.
    /// Events for shapes removed this tick retain their original entity IDs; unknown engine shape
    /// IDs stop the runtime instead of silently dropping observations. Engine IDs never escape.
    member Step: commands: Command list -> Snapshot
    interface System.IDisposable
