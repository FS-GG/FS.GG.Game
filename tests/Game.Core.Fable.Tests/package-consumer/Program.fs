module FS.GG.Game.Core.Fable.Consumer

open FS.GG.Game.Core.LockstepFixtures

[<EntryPoint>]
let main _ =
    FixtureProtocol.encodeAll ()
    |> FixtureProtocol.toLowerHex
    |> printfn "FSGG_FIXTURES_V1_HEX=%s"

    0
