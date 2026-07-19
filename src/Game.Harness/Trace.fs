namespace FS.GG.Game.Harness

[<RequireQualifiedAccess>]
type Origin =
    | InputDriven
    | Synthetic

[<Sealed>]
type Trace<'f> internal (origin: Origin, frames: 'f list) =
    member _.Origin = origin
    member _.Frames = frames

[<RequireQualifiedAccess>]
module Trace =

    let frames (trace: Trace<'f>) : 'f list = trace.Frames

    let origin (trace: Trace<'f>) : Origin = trace.Origin

    let isSynthetic (trace: Trace<'f>) : bool = trace.Origin = Origin.Synthetic

    let equalFrames (a: Trace<'f>) (b: Trace<'f>) : bool = a.Frames = b.Frames

    let internal create (origin: Origin) (frames: 'f list) : Trace<'f> = Trace<'f>(origin, frames)
