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

    let firstDivergence (a: Trace<'f>) (b: Trace<'f>) : (int * 'f * 'f) option =
        let rec go i xs ys =
            match xs, ys with
            | x :: xs', y :: ys' -> if x = y then go (i + 1) xs' ys' else Some(i, x, y)
            | _ -> None

        go 0 a.Frames b.Frames

    let render (show: 'f -> string) (trace: Trace<'f>) : string =
        trace.Frames
        |> List.mapi (fun i f -> sprintf "%d: %s" i (show f))
        |> String.concat "\n"

    let internal create (origin: Origin) (frames: 'f list) : Trace<'f> = Trace<'f>(origin, frames)
