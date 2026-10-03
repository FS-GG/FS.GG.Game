#load "../../../scripts/wasm-release/SupervisorSourcePolicy.fs"
open SupervisorSourcePolicy
let inputs = [| for n in 1..18 -> { Path = string n; Sha256 = String.replicate 64 "a" } |]
let review: Review = { ReviewSha256=reviewDigest; ProofSha256=proofDigest; SourceHead=reviewedHead; SourceTree=reviewedTree; ModelSha256=reviewedModel; Version="0.3.0"; Inputs=inputs; GateRepairs=reviewedGateRepairs }
let observation: Observation = { CallerHead=reviewedHead; ExactHead=reviewedHead; Clean=true; ReviewedAncestor=true; ReviewSha256=reviewDigest; ProofSha256=proofDigest; ModelSha256=reviewedModel; Inputs=inputs; GateRepairs=reviewedGateRepairs; ChangedPaths=[|"tests/Wasm.Supervisor.Compatibility/root-canonical-review.json"|] }
let accepted = decide review observation
assert (accepted.Accepted && not accepted.PublicationAuthorized && not accepted.NativeAccepted)
let refuse r o = assert (not (decide r o).Accepted)
refuse {review with Version="0.2.1"} observation
refuse {review with ReviewSha256=String.replicate 64 "0"} observation
refuse review {observation with Clean=false}
refuse review {observation with ReviewedAncestor=false}
refuse review {observation with ExactHead=String.replicate 40 "0"}
refuse review {observation with ModelSha256=String.replicate 64 "0"}
refuse review {observation with ProofSha256=String.replicate 64 "0"}
refuse review {observation with Inputs=inputs[..16]}
refuse review {observation with ChangedPaths=[|"src/Wasm.Browser/Lifecycle.fs"|]}
refuse {review with ReviewSha256="57d1c6c3e1ab1bd2db939b9b129b5a3166a9e69c9cc3a301524b3851fb056075"} observation
refuse review {observation with ProofSha256="eb11b68a54578306f421eac2f0a719817be37740143fefc2279a74fb89b47c7f"}
refuse {review with SourceHead="b72f6155c3e38d84f37dcfd1a10c01799feb690f"} observation
refuse {review with SourceTree=String.replicate 40 "0"} observation
refuse review {observation with Inputs=Array.append inputs[..16] [|inputs[0]|]}
refuse review {observation with Inputs=Array.mapi (fun n row -> if n=0 then {row with Sha256=String.replicate 64 "0"} else row) inputs}
refuse review {observation with ChangedPaths=[|"eng/wasm-shared/version.props"|]}
refuse {review with ReviewSha256="e7b1af81ccde9da3c9d51bce6316d54c43c48798925e84b6ce3904e4983979df"} observation
refuse review {observation with GateRepairs=reviewedGateRepairs[..2]}
refuse {review with GateRepairs=Array.append reviewedGateRepairs[..2] [|reviewedGateRepairs[0]|]} observation
refuse review {observation with GateRepairs=Array.append reviewedGateRepairs[..2] [|reviewedGateRepairs[0]|]}
refuse review {observation with GateRepairs=Array.append reviewedGateRepairs [|{Path="extra";Sha256=String.replicate 64 "a"}|]}
for n in 0..3 do
    let mutation = Array.mapi (fun i row -> if i=n then {row with Sha256=String.replicate 64 "0"} else row) reviewedGateRepairs
    refuse review {observation with GateRepairs=mutation}
    refuse {review with GateRepairs=mutation} observation
    let allowedChange = {observation with ChangedPaths=[|reviewedGateRepairs[n].Path|]}
    assert ((decide review allowedChange).Accepted)
    refuse review {allowedChange with GateRepairs=mutation}
refuse {review with ReviewSha256="7beeb8bf1145490ab752c0378726620c3052fa33a96c99549be1ea2b350917c4"} observation
refuse {review with ReviewSha256="448a31ffd745e09b16ad88cc568bc46a5f1b580e07c8877f63d0484c8f549eef"} observation
let priorHelper = Array.map (fun row -> if row.Path="scripts/wasm-release/qualify-supervisor-custody.sh" then {row with Sha256="66f5526d3255a1701791ac8af5e3fd54660b543d265ebe1605fefeda3510325c"} else row) reviewedGateRepairs
refuse review {observation with GateRepairs=priorHelper}
refuse {review with GateRepairs=priorHelper} observation
refuse {review with ReviewSha256="1c896e19eb79f0dac793f909c0f32a14cf4af163fe64cb901f8cd7911d25400a"} observation
let unboundHostHelper = Array.map (fun row -> if row.Path="scripts/wasm-release/qualify-supervisor-custody.sh" then {row with Sha256="6001e794d1a09eb24176bcb549bec1edf1eab721eb096b1158c87feeb111dd5f"} else row) reviewedGateRepairs
refuse review {observation with GateRepairs=unboundHostHelper}
refuse {review with GateRepairs=unboundHostHelper} observation
printfn "supervisor-source-policy: reviewed scope accepted; old amendment/patch/stale model/source/out-of-scope refused; public/native authority absent"
