#load "../../../scripts/wasm-release/SupervisorSourcePolicy.fs"
open SupervisorSourcePolicy
let inputs = [| for n in 1..18 -> { Path = string n; Sha256 = String.replicate 64 "a" } |]
let review: Review = { ReviewSha256=reviewDigest; ProofSha256=proofDigest; SourceHead=reviewedHead; SourceTree=reviewedTree; ModelSha256=reviewedModel; Version="0.3.0"; Inputs=inputs }
let observation: Observation = { CallerHead=reviewedHead; ExactHead=reviewedHead; Clean=true; ReviewedAncestor=true; ReviewSha256=reviewDigest; ProofSha256=proofDigest; ModelSha256=reviewedModel; Inputs=inputs; ChangedPaths=[|"tests/Wasm.Supervisor.Compatibility/root-canonical-review.json"|] }
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
printfn "supervisor-source-policy: reviewed scope accepted; old amendment/patch/stale model/source/out-of-scope refused; public/native authority absent"
