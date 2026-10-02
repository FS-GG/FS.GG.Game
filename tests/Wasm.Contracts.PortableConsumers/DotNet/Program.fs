open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Contracts.PortableConsumer

[<EntryPoint>]
let main _ =
    Profiles.compatibilityPaths |> List.map render |> List.iter (printfn "%s")
    0
