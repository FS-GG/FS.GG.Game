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

    /// Process pending messages in order. A message's host feedback goes to the FRONT of the queue, so a
    /// failed mount has fallen back before anything dispatched later is looked at.
    let drain () =
        running <- true

        try
            while not disposed && not pending.IsEmpty do
                let msg = pending.Head
                pending <- pending.Tail
                let next, effects = RendererSelection.update msg model
                model <- next
                onChange model
                pending <- host.Run effects @ pending
        finally
            running <- false

    do
        onChange model
        running <- true

        try
            pending <- host.Run initialEffects
        finally
            running <- false

        drain ()

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
