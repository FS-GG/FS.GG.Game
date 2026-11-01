namespace FS.GG.Game.Render

/// Two viewports are the same when each component is equal; `NaN` equals `NaN`, so an invalid viewport can never
/// remount on every state. Shared by `RendererSelection` and `RendererConformance`.
module internal RendererViewport =

    val same: viewport: (float * float) -> other: (float * float) -> bool

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// A rendering backend a game can be presented through. Declaring a backend does NOT make it
/// selectable: a backend is offered only when a `RendererRegistry` holds an adapter for it at the
/// game's state type (see `RendererRegistry.available`).
[<RequireQualifiedAccess>]
type RendererBackend =
    /// Retained SVG over the `FS.GG.UI.Scene` projection — the existing `Adapter` path and the default.
    | Svg
    /// PixiJS (2D WebGL/WebGPU canvas). Declared; no adapter ships here until the PixiJS bindings exist.
    | Pixi
    /// BabylonJS (3D engine canvas). Declared; no adapter ships here until the current Babylon bindings exist.
    | Babylon

/// Public contract module exposed by the FS.GG.Game.Render package.
/// Backend identity: the stable enumeration order and wire tokens a selector UI or saved preference uses.
[<RequireQualifiedAccess>]
module RendererBackend =

    /// Public contract value exposed by the FS.GG.Game.Render package.
    /// Every declared backend in stable order: `Svg`, `Pixi`, `Babylon`.
    val all: RendererBackend list

    /// Public contract value exposed by the FS.GG.Game.Render package.
    /// The default backend, `Svg`. Every `RendererRegistry` holds an adapter for it, so it is always selectable.
    val defaultBackend: RendererBackend

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The stable lowercase token for a backend: `"svg"`, `"pixi"`, `"babylon"`.
    val id: backend: RendererBackend -> string

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Parse a token produced by `RendererBackend.id`. Exact match; anything else yields `None`.
    val tryParse: token: string -> RendererBackend option

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The resources ONE mounted backend owns — DOM nodes, a canvas, an engine, listeners, frames. The
/// host interpreter (`RendererHost`) calls `Present` for each accepted state and `Dispose` exactly
/// once when the backend is unmounted, replaced or the host itself is disposed.
[<NoEquality; NoComparison>]
type RendererMount<'State> =
    {
        /// Present a newer game state on the already-mounted backend.
        Present: 'State -> unit
        /// Release every resource the mount owns. Called at most once per mount.
        Dispose: unit -> unit
    }

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// How one backend realises one game-state type. `Mount` receives the current state, acquires the
/// backend's resources and presents that state; it returns `Error reason` instead of throwing when the
/// backend cannot start (a thrown exception is treated as the same refusal). This record is the
/// extension point the PixiJS and BabylonJS adapters fill once their bindings exist.
[<NoEquality; NoComparison>]
type RendererAdapter<'State> =
    {
        /// The backend this adapter realises.
        Backend: RendererBackend
        /// Acquire the backend's resources and present the initial state.
        Mount: 'State -> Result<RendererMount<'State>, string>
    }

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module RendererAdapter =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The SVG adapter over the existing Scene path: `view` projects a state onto an `FS.GG.UI.Scene`
    /// (typically built from the `Adapter` drawing functions) and `mountScene` is the host's retained
    /// SVG surface for that Scene. Every presented state is projected through `view` first, so the SVG
    /// output is exactly what the Scene path produced before backend selection existed.
    val svg:
        view: ('State -> FS.GG.UI.Scene.Scene) ->
        mountScene: (FS.GG.UI.Scene.Scene -> Result<RendererMount<FS.GG.UI.Scene.Scene>, string>) ->
            RendererAdapter<'State>

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The renderer adapters available for ONE game-state type. The type parameter is the key: an adapter
/// for one state type cannot be registered for, or selected by, another. Built only by
/// `RendererRegistry.svg` or `RendererRegistry.create`, so the default `Svg` backend always has an adapter.
[<NoEquality; NoComparison>]
type RendererRegistry<'State>

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module RendererRegistry =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A registry whose default — and initially only — adapter is `RendererAdapter.svg view mountScene`.
    val svg:
        view: ('State -> FS.GG.UI.Scene.Scene) ->
        mountScene: (FS.GG.UI.Scene.Scene -> Result<RendererMount<FS.GG.UI.Scene.Scene>, string>) ->
            RendererRegistry<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A registry whose default adapter is `adapter`, a ready SVG adapter at the game-state type itself (for a game
    /// that owns a retained SVG surface and has no `FS.GG.UI.Scene` projection to hand to `svg`). `Error` when
    /// `adapter` realises another backend, because only an SVG adapter can be the default; nothing is built then.
    val create: adapter: RendererAdapter<'State> -> Result<RendererRegistry<'State>, string>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Register an adapter for its backend, replacing any adapter already registered for that backend.
    val register: adapter: RendererAdapter<'State> -> registry: RendererRegistry<'State> -> RendererRegistry<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Declare that `backend`'s mount is fixed to the viewport `viewport state`, `(width, height)` in pixels: the
    /// remount rule. While `backend` is mounted and active, a state whose viewport differs from the previous state's is
    /// presented by remounting `backend` with that state (`RendererSelection.update` emits `Unmount` then `Mount`
    /// under a new attempt) instead of `Present`, so the game does not remount inside `Present`. A game whose
    /// viewport comes from the window keeps it in its state. `viewport` must be total and cheap; a `NaN` component
    /// equals another `NaN`, so an invalid viewport cannot remount forever. Replaces an earlier declaration for the same
    /// backend; an adapter registered later for `backend` keeps it.
    val withViewport:
        backend: RendererBackend ->
        viewport: ('State -> float * float) ->
        registry: RendererRegistry<'State> ->
            RendererRegistry<'State>

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The viewport `backend` declared with `withViewport`, if any.
    val tryViewport: backend: RendererBackend -> registry: RendererRegistry<'State> -> ('State -> float * float) option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The adapter registered for `backend`, if any.
    val tryFind: backend: RendererBackend -> registry: RendererRegistry<'State> -> RendererAdapter<'State> option

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Whether `backend` has a registered adapter and is therefore selectable.
    val isAvailable: backend: RendererBackend -> registry: RendererRegistry<'State> -> bool

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The selectable backends, in `RendererBackend.all` order. Always contains `Svg`.
    val available: registry: RendererRegistry<'State> -> RendererBackend list

/// Public contract type exposed by the FS.GG.Game.Render package.
/// Why a requested backend is not the one presenting, or why a backend's resources misbehaved.
[<RequireQualifiedAccess>]
type RendererRefusal =
    /// No adapter is registered for the backend at this state type; the selection is unchanged.
    | Unavailable of backend: RendererBackend
    /// The backend's adapter refused or failed to mount.
    | MountFailed of backend: RendererBackend * reason: string
    /// A released mount's `Dispose` failed. Ownership was still released; the selection is unchanged. It never
    /// replaces a `MountFailed` or `PresentFailed` cause of the SAME backend: the dispose failure is added to that
    /// cause's reason instead. Beside a cause of another backend it is recorded alone.
    | DisposeFailed of backend: RendererBackend * reason: string
    /// The backend's `Present` of an accepted state threw. The host released that mount, and the default backend is
    /// mounted with the latest state.
    | PresentFailed of backend: RendererBackend * reason: string

/// Public contract type exposed by the FS.GG.Game.Render package.
/// Whether the selected backend's latest mount attempt is live. Tracked explicitly rather than inferred
/// from `RendererModel.Refusal`, so an unrelated refusal can never hide a failed mount.
[<RequireQualifiedAccess>]
type RendererStatus =
    /// The latest `Mount` for the selected backend was issued and has not reported failure.
    | Active
    /// Nothing is mounted: the selected backend's latest mount failed. `failures` are the mount failures
    /// that led here, most recent first — a failed SVG fallback keeps the failure that caused the fallback.
    /// Never silent and never final: `Refusal` carries the latest cause, and re-selecting the selected backend or
    /// the next `StateChanged` retries the mount. A retry that fails again lists the failure it retried after its
    /// own, so the earlier cause is not lost (one earlier cause is kept, which bounds the list).
    | Failed of failures: (RendererBackend * string) list

/// Public contract type exposed by the FS.GG.Game.Render package.
/// Elmish messages for renderer selection.
[<RequireQualifiedAccess>]
type RendererMsg<'State> =
    /// Switch to a backend at runtime. Refused (state and selection kept) when it is unavailable.
    | SelectBackend of backend: RendererBackend
    /// A newer game state to present on the selected backend.
    | StateChanged of state: 'State
    /// Host feedback: mount `attempt` of `backend` failed. A failure whose `attempt` is not the model's current
    /// `Attempt` is stale and ignored.
    | MountFailed of backend: RendererBackend * attempt: int * reason: string
    /// Host feedback: a released mount's `Dispose` threw. Recorded only.
    | DisposeFailed of backend: RendererBackend * reason: string
    /// Host feedback: the mounted `backend`'s `Present` of `attempt` threw, and the host has released that mount. A
    /// failure whose `attempt` is not the model's current `Attempt`, or whose backend is not the selected one, is
    /// stale and ignored. A non-default backend falls back to SVG with the latest state; the default backend is
    /// remounted with the latest state (see `RendererSelection`).
    | PresentFailed of backend: RendererBackend * attempt: int * reason: string

/// Public contract type exposed by the FS.GG.Game.Render package.
/// Resource effects `RendererSelection.update` asks the host to perform, in order.
[<RequireQualifiedAccess>]
type RendererEffect<'State> =
    /// Acquire `backend`'s resources as mount `attempt` and present `state`.
    | Mount of backend: RendererBackend * attempt: int * state: 'State
    /// Present `state` on the mounted `backend`.
    | Present of backend: RendererBackend * state: 'State
    /// Release every resource `backend` owns.
    | Unmount of backend: RendererBackend

/// Public contract type exposed by the FS.GG.Game.Render package.
/// The Elmish model for renderer selection. `State` is the game state and survives every switch.
[<NoEquality; NoComparison>]
type RendererModel<'State> =
    {
        /// The adapters for this game-state type.
        Registry: RendererRegistry<'State>
        /// The backend that is (or is being) mounted.
        Selected: RendererBackend
        /// The current game state, independent of the backend presenting it.
        State: 'State
        /// The token of the latest `Mount` effect. Feedback for any other attempt is stale.
        Attempt: int
        /// Whether the selected backend's latest mount is live or failed.
        Status: RendererStatus
        /// The most recent refusal, for display. Cleared by the next accepted selection. Never used to
        /// decide recoverability — that is `Status`.
        Refusal: RendererRefusal option
    }

/// Public contract module exposed by the FS.GG.Game.Render package.
///
/// Pure Elmish init/update for runtime renderer selection. Resource handles never enter the model:
/// `update` returns `RendererEffect` values and a `RendererHost` performs them, the same
/// external-resource-outside-the-model rule the generated Elmish client follows. An Elmish program
/// turns the effects into one command that runs them on its `RendererHost` and dispatches each returned
/// feedback message (wrapped in the program's own message type) back into `update`.
///
/// Ordering contract: unmount-then-mount, with SVG fallback. A switch emits `Unmount` of the current
/// backend strictly before `Mount` of the next, so two backends never own resources at once. If the
/// next backend's mount fails (or it later throws from `Present`), selection falls back to mounting the
/// default SVG backend with the same game state. If SVG itself fails to mount, nothing is mounted, `Status` is
/// `Failed` and re-selecting SVG, selecting any available backend, or the next game state retries.
///
/// Adapter lifecycle (GAME-RENDER-ADOPT-01.3):
///
/// - **Failing `Present`.** The host releases the throwing mount and reports `PresentFailed`. A non-default backend
///   falls back to SVG with the latest state. The DEFAULT backend is remounted with the latest state (`Attempt`
///   advances, `Refusal` records the cause), so a throwing `Present` never leaves the page without a surface while a
///   mount can still succeed. If that remount also fails, `Status` is `Failed` with the cause, and the next
///   `StateChanged` mounts again. Nothing is rethrown: `RendererHost.Run` contains the exception. The retry is bounded to
///   one mount attempt per state the caller dispatches; there is no rate limit, so a game whose mount fails for every
///   state attempts it once per state, and stops by not dispatching states.
/// - **Viewport change.** A mount is fixed to the viewport it was created for. A backend declared with
///   `RendererRegistry.withViewport` is remounted with the new state when the viewport of the state changes, before
///   anything is presented; a backend with no declaration owns its viewport inside its adapter.
[<RequireQualifiedAccess>]
module RendererSelection =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Select the default backend and mount it, as attempt 1, with `state`.
    val init: registry: RendererRegistry<'State> -> state: 'State -> RendererModel<'State> * RendererEffect<'State> list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// Apply one message. Selecting an available backend unmounts the current one and mounts the new one
    /// with the unchanged game state under a new attempt. Selecting an unavailable backend records
    /// `Unavailable` and emits nothing; `Status` is untouched. Re-selecting the current backend is a no-op
    /// while it is `Active` and a retry while it is `Failed`. `StateChanged` replaces the state and
    /// presents it when `Active`, remounts the selected backend with it when its declared viewport changed, and
    /// mounts the selected backend again with it when `Failed`. A current-attempt `MountFailed` for a non-default
    /// backend falls back to mounting the default; for the default it sets `Failed`; a stale attempt is ignored.
    /// A current-attempt `PresentFailed` mounts the default with the latest state, whichever backend failed.
    /// `DisposeFailed` is recorded in `Refusal` only.
    val update:
        msg: RendererMsg<'State> -> model: RendererModel<'State> -> RendererModel<'State> * RendererEffect<'State> list

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// The backends a selector should offer for this model — `RendererRegistry.available` of its registry.
    val available: model: RendererModel<'State> -> RendererBackend list

/// Public contract type exposed by the FS.GG.Game.Render package.
///
/// The effect interpreter that owns the mounted backend's resources. At most one backend is mounted at a
/// time: a `Mount` while another backend is mounted disposes that one first, so no resources leak even
/// when an `Unmount` is missing. Each mount is disposed exactly once; ownership is cleared before its
/// `Dispose` runs. No adapter exception escapes `Run`: a failing `Mount` or `Present` becomes
/// `RendererMsg.MountFailed`, a failing `Present` releases that mount and becomes `RendererMsg.PresentFailed`, and a
/// failing `Dispose`
/// becomes `RendererMsg.DisposeFailed` while the remaining effects, including a pending `Mount`, still
/// run. After `Dispose` the host is inert; a mount's failing `Dispose` during host disposal is not rethrown.
///
/// `Run` is not reentrant. Adapters must not dispatch Elmish messages synchronously from `Mount`,
/// `Present` or `Dispose`; report outcomes through their return value or schedule a later dispatch.
[<Sealed>]
type RendererHost<'State> =
    interface System.IDisposable

    /// The backend currently mounted, if any.
    member Mounted: RendererBackend option

    /// Perform effects in order and return feedback messages to dispatch, in the order they occurred.
    member Run: effects: RendererEffect<'State> list -> RendererMsg<'State> list

/// Public contract module exposed by the FS.GG.Game.Render package.
[<RequireQualifiedAccess>]
module RendererHost =

    /// Public contract function exposed by the FS.GG.Game.Render package.
    /// A host that resolves backends through `registry`. Nothing is mounted until a `Mount` effect runs.
    val create: registry: RendererRegistry<'State> -> RendererHost<'State>
