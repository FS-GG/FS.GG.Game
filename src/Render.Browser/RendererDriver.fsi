namespace FS.GG.Game.Render.Browser

open FS.GG.Game.Render

/// Runs the `RendererSelection` Elmish loop for a browser page that is not already an Elmish program.
///
/// `RendererSelection.update` stays pure; the driver owns the one `RendererHost` (the mounted backend's
/// resources) beside the model, performs each effect list in order, and feeds the host's feedback messages
/// back through `update` before returning, so a failed mount has fallen back to SVG by the time `Dispatch`
/// returns. An Elmish `Program` does the same thing with its own `Cmd`; this is the equivalent for hosts
/// that dispatch from event handlers. Messages dispatched while the driver is running are queued in order,
/// so adapters that dispatch from a callback cannot reenter `RendererHost.Run`.
[<Sealed>]
type RendererDriver<'State> =
    interface System.IDisposable

    /// The current Elmish model. `State` is the game state and survives every switch.
    member Model: RendererModel<'State>

    /// The backend whose resources are currently mounted, if any.
    member Mounted: RendererBackend option

    /// Apply one message and everything it causes. Ignored after `Dispose`.
    member Dispatch: msg: RendererMsg<'State> -> unit

[<RequireQualifiedAccess>]
module RendererDriver =

    /// Select the default backend, mount it with `state` and return the driver. `onChange` observes every
    /// model produced, in order, including the initial one.
    val start:
        registry: RendererRegistry<'State> ->
        state: 'State ->
        onChange: (RendererModel<'State> -> unit) ->
            RendererDriver<'State>
