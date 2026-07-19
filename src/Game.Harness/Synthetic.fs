namespace FS.GG.Game.Harness

[<RequireQualifiedAccess>]
module Synthetic =

    let trace (fingerprint: 'world -> 'f) (worlds: 'world list) : Trace<'f> =
        Trace.create Origin.Synthetic (worlds |> List.map fingerprint)
