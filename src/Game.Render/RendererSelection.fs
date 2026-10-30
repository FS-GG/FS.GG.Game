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

[<RequireQualifiedAccess>]
type RendererMsg<'State> =
    | SelectBackend of backend: RendererBackend
    | StateChanged of state: 'State
    | MountFailed of backend: RendererBackend * reason: string

[<RequireQualifiedAccess>]
type RendererEffect<'State> =
    | Mount of backend: RendererBackend * state: 'State
    | Present of backend: RendererBackend * state: 'State
    | Unmount of backend: RendererBackend

[<NoEquality; NoComparison>]
type RendererModel<'State> =
    {
        Registry: RendererRegistry<'State>
        Selected: RendererBackend
        State: 'State
        Refusal: RendererRefusal option
    }

[<RequireQualifiedAccess>]
module RendererSelection =

    let init (registry: RendererRegistry<'State>) (state: 'State) =
        {
            Registry = registry
            Selected = RendererBackend.defaultBackend
            State = state
            Refusal = None
        },
        [ RendererEffect.Mount(RendererBackend.defaultBackend, state) ]

    let private mountFailed (model: RendererModel<'State>) =
        match model.Refusal with
        | Some(RendererRefusal.MountFailed(backend, _)) -> backend = model.Selected
        | _ -> false

    let update (msg: RendererMsg<'State>) (model: RendererModel<'State>) =
        match msg with
        | RendererMsg.SelectBackend backend when backend = model.Selected ->
            if mountFailed model then
                { model with Refusal = None }, [ RendererEffect.Mount(backend, model.State) ]
            else
                model, []
        | RendererMsg.SelectBackend backend when not (RendererRegistry.isAvailable backend model.Registry) ->
            { model with
                Refusal = Some(RendererRefusal.Unavailable backend)
            },
            []
        | RendererMsg.SelectBackend backend ->
            { model with
                Selected = backend
                Refusal = None
            },
            [
                RendererEffect.Unmount model.Selected
                RendererEffect.Mount(backend, model.State)
            ]
        | RendererMsg.StateChanged state ->
            { model with State = state }, [ RendererEffect.Present(model.Selected, state) ]
        | RendererMsg.MountFailed(backend, _) when backend <> model.Selected -> model, []
        | RendererMsg.MountFailed(backend, reason) when backend = RendererBackend.defaultBackend ->
            { model with
                Refusal = Some(RendererRefusal.MountFailed(backend, reason))
            },
            []
        | RendererMsg.MountFailed(backend, reason) ->
            { model with
                Selected = RendererBackend.defaultBackend
                Refusal = Some(RendererRefusal.MountFailed(backend, reason))
            },
            [ RendererEffect.Mount(RendererBackend.defaultBackend, model.State) ]

    let available (model: RendererModel<'State>) =
        RendererRegistry.available model.Registry

[<Sealed>]
type RendererHost<'State>(registry: RendererRegistry<'State>) =
    let mutable mounted: (RendererBackend * RendererMount<'State>) option = None
    let mutable disposed = false

    /// Clear ownership BEFORE calling Dispose, so a throwing Dispose can never be retried.
    let release () =
        match mounted with
        | Some(_, mount) ->
            mounted <- None
            mount.Dispose()
        | None -> ()

    let mount backend state : RendererMsg<'State> list =
        release ()

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
            mounted <- Some(backend, mount)
            []
        | Error reason -> [ RendererMsg.MountFailed(backend, reason) ]

    let run (effect: RendererEffect<'State>) : RendererMsg<'State> list =
        match effect with
        | RendererEffect.Mount(backend, state) -> mount backend state
        | RendererEffect.Present(backend, state) ->
            match mounted with
            | Some(current, mount) when current = backend -> mount.Present state
            | _ -> ()

            []
        | RendererEffect.Unmount backend ->
            match mounted with
            | Some(current, _) when current = backend -> release ()
            | _ -> ()

            []

    member _.Mounted = mounted |> Option.map fst

    member _.Run(effects: RendererEffect<'State> list) : RendererMsg<'State> list =
        if disposed then [] else effects |> List.collect run

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                release ()

[<RequireQualifiedAccess>]
module RendererHost =

    let create (registry: RendererRegistry<'State>) = new RendererHost<'State>(registry)
