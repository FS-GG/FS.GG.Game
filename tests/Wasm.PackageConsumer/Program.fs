open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser

let fail value = failwithf "fresh package API refused: %A" value

[<EntryPoint>]
let main _ =
    let descriptor = Profiles.tryFind BarProtected |> Option.defaultWith (fun () -> fail "BAR profile")
    let configuration =
        Validation.validateConfiguration
            {
                Path = BarProtected
                ArtifactSha256 = String.replicate 64 "a"
                ConfigurationSha256 = String.replicate 64 "b"
                Limits = descriptor.Limits
                Deadline = descriptor.Deadline
                Scheduling = descriptor.Scheduling
                Replacement = descriptor.Replacement
            }
        |> Result.defaultWith fail
    let settings = HostSettings.create configuration None |> Result.defaultWith fail
    let host = Host.Create(settings, ignore) |> Result.defaultWith fail
    let wire = { WorkerInstance = "fresh-worker"; Request = "1"; Generation = "1" }
    match Identity.parse wire, OperationToken.create "fresh-package-host" with
    | Ok identity, Ok token ->
        printfn "%s:%s:%b" identity.WorkerInstance (OperationToken.value token) host.Projection.Disposed
        0
    | result -> fail result
