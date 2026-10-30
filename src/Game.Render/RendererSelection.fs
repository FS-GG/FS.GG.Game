namespace FS.GG.Game.Render

/// See RendererSelection.fsi for the contract. Pure Elmish selection core plus the one impure piece,
/// `RendererHost`, which owns mounted backend resources outside the model.
[<RequireQualifiedAccess>]
type RendererBackend =
    | Svg
    | Pixi
    | Babylon

[<RequireQualifiedAccess>]
module RendererBackend =

    let all = [ RendererBackend.Svg; RendererBackend.Pixi; RendererBackend.Babylon ]

    let defaultBackend = RendererBackend.Svg

    let id (backend: RendererBackend) : string =
        match backend with
        | RendererBackend.Svg -> "svg"
        | RendererBackend.Pixi -> "pixi"
        | RendererBackend.Babylon -> "babylon"

    let tryParse (token: string) : RendererBackend option =
        all |> List.tryFind (fun backend -> id backend = token)

[<NoEquality; NoComparison>]
type RendererMount<'State> =
    {
        Present: 'State -> unit
        Dispose: unit -> unit
    }

[<NoEquality; NoComparison>]
type RendererAdapter<'State> =
    {
        Backend: RendererBackend
        Mount: 'State -> Result<RendererMount<'State>, string>
    }

[<RequireQualifiedAccess>]
module RendererAdapter =

    let svg
        (view: 'State -> FS.GG.UI.Scene.Scene)
        (mountScene: FS.GG.UI.Scene.Scene -> Result<RendererMount<FS.GG.UI.Scene.Scene>, string>)
        : RendererAdapter<'State> =
        {
            Backend = RendererBackend.Svg
            Mount =
                fun state ->
                    mountScene (view state)
                    |> Result.map (fun mounted ->
                        {
                            Present = fun next -> mounted.Present(view next)
                            Dispose = mounted.Dispose
                        })
        }

/// Opaque behind the .fsi: the only constructor is `RendererRegistry.svg`, which is what guarantees
/// the default backend always has an adapter.
[<NoEquality; NoComparison>]
type RendererRegistry<'State> =
    {
        Adapters: Map<RendererBackend, RendererAdapter<'State>>
    }

[<RequireQualifiedAccess>]
module RendererRegistry =

    let register (adapter: RendererAdapter<'State>) (registry: RendererRegistry<'State>) : RendererRegistry<'State> =
        {
            Adapters = Map.add adapter.Backend adapter registry.Adapters
        }

    let svg view mountScene : RendererRegistry<'State> =
        register (RendererAdapter.svg view mountScene) { Adapters = Map.empty }

    let tryFind (backend: RendererBackend) (registry: RendererRegistry<'State>) = Map.tryFind backend registry.Adapters

    let isAvailable (backend: RendererBackend) (registry: RendererRegistry<'State>) =
        Map.containsKey backend registry.Adapters

    let available (registry: RendererRegistry<'State>) =
        RendererBackend.all |> List.filter (fun backend -> isAvailable backend registry)

[<RequireQualifiedAccess>]
type RendererRefusal =
    | Unavailable of backend: RendererBackend
    | MountFailed of backend: RendererBackend * reason: string
    | DisposeFailed of backend: RendererBackend * reason: string

[<RequireQualifiedAccess>]
type RendererStatus =
    | Active
    | Failed of failures: (RendererBackend * string) list

[<RequireQualifiedAccess>]
type RendererMsg<'State> =
    | SelectBackend of backend: RendererBackend
    | StateChanged of state: 'State
    | MountFailed of backend: RendererBackend * attempt: int * reason: string
    | DisposeFailed of backend: RendererBackend * reason: string

[<RequireQualifiedAccess>]
type RendererEffect<'State> =
    | Mount of backend: RendererBackend * attempt: int * state: 'State
    | Present of backend: RendererBackend * state: 'State
    | Unmount of backend: RendererBackend

[<NoEquality; NoComparison>]
type RendererModel<'State> =
    {
        Registry: RendererRegistry<'State>
        Selected: RendererBackend
        State: 'State
        Attempt: int
        Status: RendererStatus
        Refusal: RendererRefusal option
    }

[<RequireQualifiedAccess>]
module RendererSelection =

    let init (registry: RendererRegistry<'State>) (state: 'State) =
        {
            Registry = registry
            Selected = RendererBackend.defaultBackend
            State = state
            Attempt = 1
            Status = RendererStatus.Active
            Refusal = None
        },
        [ RendererEffect.Mount(RendererBackend.defaultBackend, 1, state) ]

    /// Issue a fresh mount attempt of `backend`, after `prefix` effects (an `Unmount`, when switching).
    let private mountNext backend refusal prefix (model: RendererModel<'State>) =
        let attempt = model.Attempt + 1

        { model with
            Selected = backend
            Attempt = attempt
            Status = RendererStatus.Active
            Refusal = refusal
        },
        prefix @ [ RendererEffect.Mount(backend, attempt, model.State) ]

    let update (msg: RendererMsg<'State>) (model: RendererModel<'State>) =
        match msg with
        | RendererMsg.SelectBackend backend when backend = model.Selected ->
            match model.Status with
            | RendererStatus.Failed _ -> mountNext backend None [] model
            | RendererStatus.Active -> model, []
        | RendererMsg.SelectBackend backend when not (RendererRegistry.isAvailable backend model.Registry) ->
            { model with
                Refusal = Some(RendererRefusal.Unavailable backend)
            },
            []
        | RendererMsg.SelectBackend backend -> mountNext backend None [ RendererEffect.Unmount model.Selected ] model
        | RendererMsg.StateChanged state ->
            match model.Status with
            | RendererStatus.Active -> { model with State = state }, [ RendererEffect.Present(model.Selected, state) ]
            | RendererStatus.Failed _ -> { model with State = state }, []
        | RendererMsg.MountFailed(backend, attempt, _) when attempt <> model.Attempt || backend <> model.Selected ->
            model, []
        | RendererMsg.MountFailed(backend, _, reason) when backend = RendererBackend.defaultBackend ->
            // Keep the failure that caused an SVG fallback, if that is what this attempt was.
            let cause =
                match model.Refusal with
                | Some(RendererRefusal.MountFailed(previous, previousReason)) when previous <> backend ->
                    [ previous, previousReason ]
                | _ -> []

            { model with
                Status = RendererStatus.Failed((backend, reason) :: cause)
                Refusal = Some(RendererRefusal.MountFailed(backend, reason))
            },
            []
        | RendererMsg.MountFailed(backend, _, reason) ->
            mountNext RendererBackend.defaultBackend (Some(RendererRefusal.MountFailed(backend, reason))) [] model
        | RendererMsg.DisposeFailed(backend, reason) ->
            { model with
                Refusal = Some(RendererRefusal.DisposeFailed(backend, reason))
            },
            []

    let available (model: RendererModel<'State>) =
        RendererRegistry.available model.Registry

[<Sealed>]
type RendererHost<'State>(registry: RendererRegistry<'State>) =
    let mutable mounted: (RendererBackend * int * RendererMount<'State>) option = None
    let mutable disposed = false

    /// Clear ownership BEFORE calling Dispose, so a throwing Dispose can never be retried, and report
    /// the failure instead of letting it escape `Run`.
    let release () : RendererMsg<'State> list =
        match mounted with
        | Some(backend, _, mount) ->
            mounted <- None

            try
                mount.Dispose()
                []
            with ex ->
                [ RendererMsg.DisposeFailed(backend, ex.Message) ]
        | None -> []

    let mount backend attempt state : RendererMsg<'State> list =
        let released = release ()

        let outcome =
            match RendererRegistry.tryFind backend registry with
            | None -> Error(sprintf "no %s adapter is registered for this state type" (RendererBackend.id backend))
            | Some adapter ->
                try
                    adapter.Mount state
                with ex ->
                    Error ex.Message

        match outcome with
        | Ok mount ->
            mounted <- Some(backend, attempt, mount)
            released
        | Error reason -> released @ [ RendererMsg.MountFailed(backend, attempt, reason) ]

    let run (effect: RendererEffect<'State>) : RendererMsg<'State> list =
        match effect with
        | RendererEffect.Mount(backend, attempt, state) -> mount backend attempt state
        | RendererEffect.Present(backend, state) ->
            match mounted with
            | Some(current, attempt, mount) when current = backend ->
                try
                    mount.Present state
                    []
                with ex ->
                    // A backend that cannot present is broken: release it and report it as failed.
                    RendererMsg.MountFailed(backend, attempt, ex.Message) :: release ()
            | _ -> []
        | RendererEffect.Unmount backend ->
            match mounted with
            | Some(current, _, _) when current = backend -> release ()
            | _ -> []

    member _.Mounted = mounted |> Option.map (fun (backend, _, _) -> backend)

    member _.Run(effects: RendererEffect<'State> list) : RendererMsg<'State> list =
        if disposed then [] else effects |> List.collect run

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                release () |> ignore

[<RequireQualifiedAccess>]
module RendererHost =

    let create (registry: RendererRegistry<'State>) = new RendererHost<'State>(registry)
