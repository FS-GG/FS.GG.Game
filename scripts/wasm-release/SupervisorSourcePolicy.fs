/// Source-review eligibility for the compatible supervisor successor.
/// This decision grants neither publication authority nor native product acceptance.
module SupervisorSourcePolicy

open System.Text.RegularExpressions

type Binding = { Path: string; Sha256: string }
type Review = {
    ReviewSha256: string; ProofSha256: string; SourceHead: string; SourceTree: string
    ModelSha256: string; Version: string; Inputs: Binding array; GateRepairs: Binding array
}
type Observation = {
    CallerHead: string; ExactHead: string; Clean: bool; ReviewedAncestor: bool
    ReviewSha256: string; ProofSha256: string; ModelSha256: string
    Inputs: Binding array; GateRepairs: Binding array; ChangedPaths: string array
}
type Decision = { Accepted: bool; Kind: string; Reasons: string array; ExactHead: string; PublicationAuthorized: bool; NativeAccepted: bool }

let reviewedHead = "9638363fd64d1d5d99eb90acb2cdb5a0a19787e8"
let reviewedTree = "a2f664f2c403b4c62ecc9e52955c32800a2ce6f0"
let reviewedModel = "9eac9183e9b8bdbd555d1a922acdc098e25f92458866c5437c52fdedbe75717a"
let reviewDigest = "7457baba75e762f2ffbfb54d63b5ff336c35e82ad705bfd15744084de2ab534e"
let proofDigest = "bdf76d88362a3a9512b3deddbb074310c4eee2c0afe63cdaf7f73871892cbf92"
let reviewedGateRepairs: Binding array = [|
    { Path = "scripts/wasm-release/qualify-supervisor-custody.sh"; Sha256 = "420c259b52e7656b01bc60f33d51654ea1946b5fe06f429f05c34e66e325f988" }
    { Path = "tests/Wasm.Lifecycle.Correspondence/packages.lock.json"; Sha256 = "f174c7bbe6cf624741eb5eb2d541b288ea6e8eb7b0fbc6b51918138838f14194" }
    { Path = "tests/Wasm.Lifecycle.Tests/packages.lock.json"; Sha256 = "4b6621bf5adac09ea5cc175a3fd4311873d68e34c6fc68ae6c6ab167c85f7d58" }
    { Path = "tests/Wasm.Supervisor.Compatibility/generate-compatible-traces.py"; Sha256 = "e267992707b2abcb4256b039661e4cca74c93b2ccb1f96aef7c4c087faca1b0e" }
|]
let preparationPaths = Set [
    "scripts/wasm-release/SupervisorSourcePolicy.fs"
    "scripts/wasm-release/bind-supervisor-source.py"
    "scripts/wasm-release/decide-supervisor-source.fsx"
    "tests/release/wasm/test-supervisor-source-policy.fsx"
    "tests/Wasm.Supervisor.Compatibility/root-canonical-review.json"
    "tests/Wasm.Supervisor.Compatibility/reviewed-source-proof.json"
    "tests/Wasm.Supervisor.Compatibility/historical-b72-root-review.json"
    "tests/Wasm.Supervisor.Compatibility/historical-b72-source-proof.json"
    "tests/Wasm.Supervisor.Compatibility/historical-963-root-review.json"
    "tests/Wasm.Supervisor.Compatibility/historical-6d-root-review.json"
    "tests/Wasm.Supervisor.Compatibility/historical-4477-root-review.json"
    "tests/Wasm.Supervisor.Compatibility/historical-a345-root-review.json"
    "docs/roadmaps/wasm-shared-01.md"
]

let decide (review: Review) (observed: Observation) : Decision =
    let reasons = ResizeArray<string>()
    let require condition reason = if not condition then reasons.Add reason
    require (review.ReviewSha256 = reviewDigest && observed.ReviewSha256 = reviewDigest) "unreviewed root packet"
    require (review.ProofSha256 = proofDigest && observed.ProofSha256 = proofDigest) "substituted historical owner proof"
    require (review.SourceHead = reviewedHead && review.SourceTree = reviewedTree && observed.ReviewedAncestor) "reviewed source lineage mismatch"
    require (review.ModelSha256 = reviewedModel && observed.ModelSha256 = reviewedModel) "reviewed model changed"
    require (review.Version = "0.3.0") "receiver-visible additive API requires selected minor version"
    require (Regex.IsMatch(observed.CallerHead, "^[0-9a-f]{40}$") && observed.CallerHead = observed.ExactHead && observed.Clean) "dirty or mismatched exact source head"
    require (review.Inputs.Length = 18 && Array.distinctBy _.Path review.Inputs |> Array.length = review.Inputs.Length) "incomplete reviewed input set"
    require ((Array.sortBy _.Path review.Inputs) = (Array.sortBy _.Path observed.Inputs)) "reviewed semantic input changed"
    require (review.GateRepairs.Length = 4 && observed.GateRepairs.Length = 4
             && Array.distinctBy _.Path review.GateRepairs |> Array.length = 4
             && Array.distinctBy _.Path observed.GateRepairs |> Array.length = 4) "incomplete or duplicate reviewed gate repair set"
    require ((Array.sortBy _.Path review.GateRepairs) = (Array.sortBy _.Path reviewedGateRepairs)
             && (Array.sortBy _.Path observed.GateRepairs) = (Array.sortBy _.Path reviewedGateRepairs)) "reviewed gate repair changed"
    let admittedPaths = Set.union preparationPaths (reviewedGateRepairs |> Array.map _.Path |> Set.ofArray)
    require (Array.distinct observed.ChangedPaths |> Array.length = observed.ChangedPaths.Length
             && observed.ChangedPaths |> Array.forall (fun path -> admittedPaths.Contains path)) "change outside admitted source preparation"
    { Accepted = reasons.Count = 0; Kind = if reasons.Count = 0 then "REVIEWED_SUPERVISOR_SOURCE_PREPARATION" else "REFUSED"
      Reasons = reasons.ToArray(); ExactHead = observed.ExactHead; PublicationAuthorized = false; NativeAccepted = false }
