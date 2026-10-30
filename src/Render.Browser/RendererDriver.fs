namespace FS.GG.Game.Render.Browser

open FS.GG.Game.Render

[<Sealed>]
type RendererDriver<'State>(registry: RendererRegistry<'State>, state: 'State, onChange: RendererModel<'State> -> unit)
    =
    let host = RendererHost.create registry
    let mutable pending: RendererMsg<'State> list = []
    let mutable running = false
    let mutable disposed = false

    let initial, initialEffects = RendererSelection.init registry state
    let mutable model = initial

    let mutable observerFailure: exn option = None

    /// Tell the observer about a model whose effects have ALREADY run, so it never sees a model the host
    /// has not realised. A throwing observer cannot abort the drain (that would leave the model ahead of
    /// the host, or drop queued messages); its first failure is kept and raised once the drain is done.
    let notify (current: RendererModel<'State>) =
        try
            onChange current
        with ex ->
            if observerFailure.IsNone then
                observerFailure <- Some ex

    let raiseObserverFailure () =
        match observerFailure with
        | Some ex ->
            observerFailure <- None
            raise ex
        | None -> ()

    /// Process pending messages in order. A message's host feedback goes to the FRONT of the queue, so a
    /// failed mount has fallen back before anything dispatched later is looked at.
    let drain () =
        running <- true
        let mutable completed = false

        try
            while not disposed && not pending.IsEmpty do
                let msg = pending.Head
                pending <- pending.Tail
                let next, effects = RendererSelection.update msg model
                model <- next
                pending <- host.Run effects @ pending
                notify model

            completed <- true
        finally
            running <- false

            // A drain that was cut short has no caller to report an observer failure to; keeping it would raise a
            // stale exception from the next, unrelated Dispatch.
            if not completed then
                observerFailure <- None

        raiseObserverFailure ()

    do
        running <- true

        try
            pending <- host.Run initialEffects @ pending
            notify model
        finally
            running <- false

        try
            drain ()
        with _ ->
            // A constructor that raises hands nobody a driver to dispose: release the host here.
            disposed <- true
            (host :> System.IDisposable).Dispose()
            reraise ()

    member _.Model = model

    member _.Mounted = host.Mounted

    member _.Dispatch(msg: RendererMsg<'State>) =
        if not disposed then
            pending <- pending @ [ msg ]

            if not running then
                drain ()

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                pending <- []
                (host :> System.IDisposable).Dispose()

[<RequireQualifiedAccess>]
module RendererDriver =

    let start registry state onChange =
        new RendererDriver<'State>(registry, state, onChange)
