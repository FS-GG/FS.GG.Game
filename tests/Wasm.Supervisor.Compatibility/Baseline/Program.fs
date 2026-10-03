module Wasm.Supervisor.Compatibility.Baseline
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
let id worker request generation : HostIdentity = { WorkerInstance=worker; Request=request; Generation=generation }
let token (identity:HostIdentity) = OperationToken.create $"request-{identity.Generation}-{identity.Request}" |> Result.defaultWith (failwithf "%A")
let success phase identity = { Identity=identity; Phase=phase; State=Succeeded; Dispatch=Dispatched; CleanupFault=None; CopiedOutput=Array.empty; EffectEligibility=ProductAdapterMustDecide }
let settings () =
    let d=Profiles.tryFind Sc2ImportedStrict |> Option.get
    Validation.validateConfiguration {Path=d.Path;ArtifactSha256=String.replicate 64 "a";ConfigurationSha256=String.replicate 64 "b";Limits=d.Limits;Deadline=d.Deadline;Scheduling=d.Scheduling;Replacement=d.Replacement}
    |> Result.bind (fun configuration -> HostSettings.create configuration (Some Controller) |> Result.mapError (fun _ -> []))
    |> Result.defaultWith (failwithf "%A")
let host () =
    let mutable effects=[]
    let h=Host.Create(settings(),fun values -> effects<-effects@values) |> Result.defaultWith (failwithf "%A")
    let ready worker generation first =
        let load=id worker first generation
        h.Load(0L,load,[|0uy|]);h.Observe(0L,worker,AbiVersionObserved(load,token load,0x10000u))
        let initialize=id worker (first+1UL) generation
        h.Initialize(0L,initialize,[|1uy|]);h.Observe(0L,worker,InvocationObserved(success Initialize initialize,token initialize))
    ready "active" 1UL 1UL
    h,(fun () -> effects),ready
[<EntryPoint>]
let main _ =
    let h,effects,_=host()
    let a=id "active" 6UL 1UL
    let b=id "active" 8UL 1UL
    let c=id "active" 9UL 1UL
    h.Invoke(0L,a,Ordinary,[|1uy|]);h.Invoke(0L,b,OrderedEvent,[|2uy|]);h.Invoke(0L,c,Ordinary,[|3uy|])
    h.Observe(1L,"active",InvocationObserved(success Process a,token a))
    h.Observe(2L,"active",InvocationObserved(success Process c,token c))
    let actual=effects() |> List.choose(function PostToWorker(_,command) when command.Identity.Request>=6UL -> Some command.Identity.Request | _->None)
    if actual<>[6UL;9UL;8UL] then failwithf "installed baseline unexpectedly changed %A" actual
    printfn "GENUINE_INSTALLED_020_FIFO_GAP actual=%A required=[6;8;9]" actual
    let h,effects,_=host()
    h.Invoke(0L,a,Ordinary,[|1uy|]);h.Invoke(0L,b,OrderedEvent,[|2uy|])
    let candidate=id "candidate" 10UL 2UL
    h.PrepareCandidate(0L,candidate,"tx",Some 1UL,[|0uy|]);h.Observe(0L,"candidate",AbiVersionObserved(candidate,token candidate,0x10000u))
    let init=id "candidate" 11UL 2UL
    h.Initialize(0L,init,[|1uy|]);h.Observe(0L,"candidate",InvocationObserved(success Initialize init,token init));h.ValidateCandidate(0L,"tx",2UL)
    h.Freeze(1L,"recovery");h.Observe(2L,"active",InvocationObserved(success Process a,token a))
    h.Resume(3L,"recovery");h.CommitCandidate(3L,"tx",1UL,2UL);h.Freeze(3L,"recovery")
    let posted=effects() |> List.exists(function PostToWorker(_,command) when command.Identity=b -> true | _->false)
    if not posted then failwith "installed baseline unexpectedly avoids frozen old queue dispatch"
    printfn "GENUINE_INSTALLED_020_FROZEN_COMMIT_GAP oldQueuedRequestPosted=true"
    0
