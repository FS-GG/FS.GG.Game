open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Contracts.PortableConsumer

Profiles.compatibilityPaths |> List.map render |> List.iter (printfn "%s")
