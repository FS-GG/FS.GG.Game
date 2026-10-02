open System
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser

let check condition message = if not condition then failwith message

let configurationWithDigest path digest =
    let profile = Profiles.tryFind path |> Option.get
    Validation.validateConfiguration
        { Path=path; ArtifactSha256=digest; ConfigurationSha256=String.replicate 64 "b"
          Limits=profile.Limits; Deadline=profile.Deadline; Scheduling=profile.Scheduling; Replacement=profile.Replacement }
    |> Result.defaultWith (fun issues -> failwithf "%A" issues)

let configuration path = configurationWithDigest path (String.replicate 64 "a")
let identity: HostIdentity = { WorkerInstance="worker-1"; Request=2UL; Generation=3UL }
let cfg = configuration BarProtected

let state0, effects0 = Invocation.start cfg identity ProcessCall [|1uy;2uy|]
check (effects0 = [ Allocate(DescriptorAllocation, 8u) ]) "descriptor must allocate first"
let state1, effects1 = Invocation.update (AllocationReturned(DescriptorAllocation, 1024u, 65536UL)) state0
check (effects1 = [ Allocate(InputAllocation, 2u) ]) "input must allocate after descriptor validation"

let _, aliasEffects = Invocation.update (AllocationReturned(InputAllocation, 1024u, 65536UL)) state1
check (aliasEffects |> List.contains TerminateGuest) "alias must terminate"
check (aliasEffects |> List.exists (function FreeOwned _ -> true | _ -> false) |> not) "alias must never be freed"

let oversizedState2, _ = Invocation.update (AllocationReturned(InputAllocation, 2048u, 262144UL)) state1
let oversizedState3, _ = Invocation.update (GuestReturned(0, 262144UL)) oversizedState2
let _, oversizedEffects = Invocation.update (DescriptorRead(8192u, 65537u, 262144UL)) oversizedState3
check (oversizedEffects |> List.contains TerminateGuest) "configured output ceiling must terminate"
check (oversizedEffects |> List.exists (function FreeOwned _ -> true | _ -> false) |> not) "untrusted oversized output must not be freed"

let state2, effects2 = Invocation.update (AllocationReturned(InputAllocation, 2048u, 65536UL)) state1
check (effects2 |> List.map (function ZeroDescriptor _ -> 0 | CopyInput _ -> 1 | InvokeGuest _ -> 2 | _ -> 99) = [0;1;2]) "zero/copy/invoke order"
let state3, _ = Invocation.update (GuestReturned(0, 131072UL)) state2
let state4, copyEffect = Invocation.update (DescriptorRead(4096u, 3u, 131072UL)) state3
check (copyEffect = [ CopyOutput { Pointer=4096UL; Length=3UL } ]) "output must copy before free"
let state5, firstFree = Invocation.update (OutputCopied [|9uy;8uy;7uy|]) state4
check (firstFree = [ FreeOwned { Pointer=4096UL; Length=3UL } ]) "output frees first"
let state6, secondFree = Invocation.update (FreeReturned({Pointer=4096UL;Length=3UL}, 131072UL)) state5
check (secondFree = [ FreeOwned { Pointer=2048UL; Length=2UL } ]) "input frees second"
let state7, thirdFree = Invocation.update (FreeReturned({Pointer=2048UL;Length=2UL}, 131072UL)) state6
check (thirdFree = [ FreeOwned { Pointer=1024UL; Length=8UL } ]) "descriptor frees last"
let terminal, completed = Invocation.update (FreeReturned({Pointer=1024UL;Length=8UL}, 131072UL)) state7
check (Invocation.isTerminal terminal) "completion is terminal"
match completed with
| [ InvocationCompleted outcome ] ->
    check (outcome.State = Succeeded && outcome.CopiedOutput = [|9uy;8uy;7uy|]) "copied output survives cleanup"
| value -> failwithf "unexpected completion %A" value

let _, duplicate = Invocation.update (FreeReturned({Pointer=1024UL;Length=8UL}, 131072UL)) terminal
check duplicate.IsEmpty "terminal invocation settles once"

let trapState, _ = Invocation.update (GuestTrapped("boom", 131072UL)) state2
let afterFirstFree, _ = Invocation.update (FreeReturned({Pointer=2048UL;Length=2UL}, 131072UL)) trapState
let _, cleanupFault = Invocation.update (FreeTrapped({Pointer=1024UL;Length=8UL}, "free boom")) afterFirstFree
match cleanupFault with
| [ TerminateGuest; InvocationCompleted outcome ] ->
    check (outcome.State = Faulted "boom") "primary trap must be preserved"
    check (outcome.CleanupFault = Some {Phase=Free;Diagnostic="free boom"}) "cleanup fault must remain separate"
| value -> failwithf "unexpected cleanup fault %A" value

let modules = IO.Path.GetFullPath(IO.Path.Combine(__SOURCE_DIRECTORY__, "../Wasm.Compatibility/modules"))
let admissionCases =
    [ BarProtected, "bar-rust/bar-conformance.wasm", "1557e76b7a97b8e301c31c1e082c9060d8b66258a23308dcf9d2d17dbfd9de7d"
      Sc2ImportedStrict, "sc2-rust/sc2-conformance.wasm", "005a55fdd63fddacfb342e2cb02e12900926c9ba5134aebb9d0088f05223b03c" ]
for path, relative, digest in admissionCases do
    let artifact = IO.File.ReadAllBytes(IO.Path.Combine(modules, relative))
    match Admission.inspect (configurationWithDigest path digest) artifact with
    | Ok evidence -> check (evidence.FunctionCount >= 5 && evidence.MaximumMemoryPages > 0u) (sprintf "real module evidence %A" evidence)
    | Error issues -> failwithf "real module admission failed for %A: %A" path issues

let barBytes = IO.File.ReadAllBytes(IO.Path.Combine(modules, "bar-rust/bar-conformance.wasm"))
let barProfile = Profiles.tryFind BarProtected |> Option.get
let loweredMemory =
    Validation.validateConfiguration
        { Path=BarProtected; ArtifactSha256=String.replicate 64 "a"; ConfigurationSha256=String.replicate 64 "b"
          Limits={barProfile.Limits with MaximumMemoryPages=64}; Deadline=barProfile.Deadline; Scheduling=barProfile.Scheduling; Replacement=barProfile.Replacement }
    |> Result.defaultWith (fun issues -> failwithf "%A" issues)
check (Admission.inspect loweredMemory barBytes |> Result.isError) "module maximum must obey lowered configured memory ceiling"
let badMagic = Array.copy barBytes
badMagic[0] <- 1uy
check (Admission.inspect cfg badMagic |> Result.isError) "bad magic must refuse"
let badLength = Array.copy barBytes
badLength[9] <- 0xffuy
check (Admission.inspect cfg badLength |> Result.isError) "malformed section length must refuse"
let withCustomName (nameBytes: byte array) =
    check (nameBytes.Length < 127) "test custom name must use one-byte LEB"
    Array.concat [ barBytes; [|0uy; byte (nameBytes.Length + 1); byte nameBytes.Length|]; nameBytes ]
let invalidUtf8 =
    [ "invalid-continuation", [|0xe2uy;0x28uy;0xa1uy|]
      "overlong", [|0xc0uy;0xafuy|]
      "surrogate", [|0xeduy;0xa0uy;0x80uy|]
      "out-of-range", [|0xf4uy;0x90uy;0x80uy;0x80uy|]
      "incomplete", [|0xe2uy;0x82uy|] ]
for label, encoded in invalidUtf8 do
    check (Admission.inspect cfg (withCustomName encoded) |> Result.isError) (label + " UTF-8 must refuse")
check (Admission.inspect cfg (withCustomName [|0xf0uy;0x9fuy;0x92uy;0xa9uy|]) |> Result.isOk) "valid four-byte UTF-8 must remain admissible"
let renamedExport = Array.copy barBytes
let marker = Text.Encoding.UTF8.GetBytes("barc_free")
let markerOffset =
    renamedExport
    |> Array.windowed marker.Length
    |> Array.findIndex ((=) marker)
renamedExport[markerOffset + marker.Length - 1] <- byte 'x'
check (Admission.inspect cfg renamedExport |> Result.isError) "exact exports must refuse renamed ABI"

let mutable mechanicsCalled = false
let mechanics =
    { Sha256Hex = fun _ -> mechanicsCalled <- true; async.Return(Ok(String.replicate 64 "a"))
      CompileAfterAdmission = fun _ -> mechanicsCalled <- true; async.Return(Ok())
      PerformInvocationEffect = fun _ -> async.Return(Error "unused") }
let token = OperationToken.create "admission-test" |> Result.defaultWith (fun issue -> failwithf "%A" issue)
WorkerEntry.inspectAndCompile mechanics identity token cfg badMagic |> Async.RunSynchronously |> ignore
check (not mechanicsCalled) "no browser compile/hash mechanics may run before admission"

let legacy = Profiles.tryFind Sc2LegacyDirectUrl |> Option.get
let legacyCandidate =
    { Path=Sc2LegacyDirectUrl; ArtifactSha256=String.replicate 64 "a"; ConfigurationSha256=String.replicate 64 "b"
      Limits=legacy.Limits; Deadline=legacy.Deadline; Scheduling=legacy.Scheduling; Replacement=legacy.Replacement }
check (Validation.validateConfiguration legacyCandidate |> Result.isError) "legacy URL path remains inventory only"

printfn "Wasm.Invocation.Tests: passed"
