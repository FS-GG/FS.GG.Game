/// Source-review eligibility for the compatible supervisor successor.
/// This decision grants neither publication authority nor native product acceptance.
module SupervisorSourcePolicy

open System.Text.RegularExpressions

type Binding = { Path: string; Sha256: string }
type Review = {
    ReviewSha256: string; ProofSha256: string; SourceHead: string; SourceTree: string
    ModelSha256: string; Version: string; Inputs: Binding array
}
type Observation = {
    CallerHead: string; ExactHead: string; Clean: bool; ReviewedAncestor: bool
    ReviewSha256: string; ProofSha256: string; ModelSha256: string
    Inputs: Binding array; ChangedPaths: string array
}
type Decision = { Accepted: bool; Kind: string; Reasons: string array; ExactHead: string; PublicationAuthorized: bool; NativeAccepted: bool }

let reviewedHead = "9638363fd64d1d5d99eb90acb2cdb5a0a19787e8"
let reviewedTree = "a2f664f2c403b4c62ecc9e52955c32800a2ce6f0"
let reviewedModel = "9eac9183e9b8bdbd555d1a922acdc098e25f92458866c5437c52fdedbe75717a"
let reviewDigest = "e7b1af81ccde9da3c9d51bce6316d54c43c48798925e84b6ce3904e4983979df"
let proofDigest = "bdf76d88362a3a9512b3deddbb074310c4eee2c0afe63cdaf7f73871892cbf92"
let preparationPaths = Set [
    "scripts/wasm-release/SupervisorSourcePolicy.fs"
    "scripts/wasm-release/bind-supervisor-source.py"
    "scripts/wasm-release/decide-supervisor-source.fsx"
    "tests/release/wasm/test-supervisor-source-policy.fsx"
    "tests/Wasm.Supervisor.Compatibility/root-canonical-review.json"
    "tests/Wasm.Supervisor.Compatibility/reviewed-source-proof.json"
    "tests/Wasm.Supervisor.Compatibility/historical-b72-root-review.json"
    "tests/Wasm.Supervisor.Compatibility/historical-b72-source-proof.json"
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
    require (Array.distinct observed.ChangedPaths |> Array.length = observed.ChangedPaths.Length
             && observed.ChangedPaths |> Array.forall (fun path -> preparationPaths.Contains path)) "change outside admitted source preparation"
    { Accepted = reasons.Count = 0; Kind = if reasons.Count = 0 then "REVIEWED_SUPERVISOR_SOURCE_PREPARATION" else "REFUSED"
      Reasons = reasons.ToArray(); ExactHead = observed.ExactHead; PublicationAuthorized = false; NativeAccepted = false }
