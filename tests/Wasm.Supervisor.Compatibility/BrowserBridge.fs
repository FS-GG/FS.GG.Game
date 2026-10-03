module Wasm.Supervisor.Compatibility.BrowserBridge
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser
let private unwrap = function Ok value -> value | Error issues -> failwithf "%A" issues
let private configuration digest =
    let descriptor=Profiles.tryFind Sc2ImportedStrict |> Option.get
    Validation.validateConfiguration { Path=Sc2ImportedStrict; ArtifactSha256=digest; ConfigurationSha256=String.replicate 64 "b"; Limits=descriptor.Limits; Deadline=descriptor.Deadline; Scheduling=descriptor.Scheduling; Replacement=descriptor.Replacement } |> unwrap
let private identity worker request generation : HostIdentity = {WorkerInstance=worker;Request=request;Generation=generation}
let private limits deadline output enclosing : RequestLimits = {MaximumDeadlineMilliseconds=deadline;MaximumOutputBytes=output;EnclosingDeadlineMilliseconds=enclosing}
type ResultView = {Request:string;State:string;Disposition:string;Reason:string;Dispatched:bool;Output:byte array}
let create digest transport receive =
    let settings=HostSettings.createCompatible (configuration digest) (Some Controller) Sc2SupervisorV1 |> unwrap
    Host.CreateConnected(settings,transport,fun result ->
        receive {Request=string result.Identity.Request; State=result.Outcome |> Option.map(fun o->string o.State) |> Option.defaultValue "None";Disposition=string result.Disposition;Reason=string result.Reason;Dispatched=result.Outcome |> Option.exists(fun o->o.Dispatch=Dispatched);Output=result.Outcome |> Option.map _.CopiedOutput |> Option.defaultValue Array.empty}) |> unwrap
let load (host:Host) now worker request generation digest artifact deadline =
    host.SubmitLimited(now,LimitedLoad(identity worker request generation,configuration digest,artifact),limits deadline 65536 None) |> unwrap
let initialize (host:Host) now worker request generation input deadline output =
    host.SubmitLimited(now,LimitedInitialize(identity worker request generation,input),limits deadline output None) |> unwrap
let invoke (host:Host) now worker request generation submission input deadline output enclosing =
    let kind=if submission=0 then Ordinary elif submission=1 then OrderedEvent else ReplaceableSnapshot
    host.SubmitLimited(now,LimitedInvoke(identity worker request generation,kind,input),limits deadline output enclosing) |> unwrap
let candidate (host:Host) now worker request generation digest artifact =
    host.SubmitLimited(now,LimitedCandidate(identity worker request generation,"recovery",Some 1UL,configuration digest,artifact),limits 250 65536 None) |> unwrap
let validate (host:Host) now = host.ValidateCandidate(now,"recovery",2UL)
let freeze (host:Host) now = host.Freeze(now,"fence")
let commit (host:Host) now token = host.CommitCandidateFrozen(now,token,"recovery",1UL,2UL) |> Result.isOk
let resume (host:Host) now = host.Resume(now,"fence")
let dispose (host:Host) now = host.Dispose now
let projection (host:Host) = host.Projection
let expiredAdmission (host:Host) now = host.SubmitLimited(now,LimitedInvoke(identity "active" 99UL 1UL,Ordinary,[|4uy|]),limits 250 64 (Some now)) |> Result.isError
