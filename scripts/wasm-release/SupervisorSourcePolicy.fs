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

let reviewedHead = "b72f6155c3e38d84f37dcfd1a10c01799feb690f"
let reviewedTree = "a9dfa286d8dd656dd698bf09f609a1ef7852eb31"
let reviewedModel = "9eac9183e9b8bdbd555d1a922acdc098e25f92458866c5437c52fdedbe75717a"
let reviewDigest = "57d1c6c3e1ab1bd2db939b9b129b5a3166a9e69c9cc3a301524b3851fb056075"
let proofDigest = "eb11b68a54578306f421eac2f0a719817be37740143fefc2279a74fb89b47c7f"
let preparationPaths = Set [
    "eng/wasm-shared/version.props"; "sdk/wasm/VERSION"
    "sdk/wasm/rust/Cargo.lock"; "sdk/wasm/rust/fsgg-wasm-guest/Cargo.toml"
    "examples/wasm/rust/bar-guest/Cargo.toml"; "examples/wasm/rust/bar-guest/Cargo.lock"
    "examples/wasm/rust/sc2-guest/Cargo.toml"; "examples/wasm/rust/sc2-guest/Cargo.lock"
    "src/Wasm.Contracts/compatibility-profile.v1.json"
    "scripts/wasm-release/api-baseline-policy.json"; "scripts/wasm-release/api-baseline-policy-0.2.0.json"
    "scripts/wasm-release/compare-published-baseline.py"; "scripts/wasm-release/release_manifest.py"
    "scripts/wasm-release/SupervisorSourcePolicy.fs"; "scripts/wasm-release/decide-supervisor-source.fsx"
    "scripts/wasm-release/bind-supervisor-source.py"
    "tests/release/wasm/test-supervisor-source-policy.fsx"; "tests/release/wasm/test-api-baseline-policy.py"; "tests/release/wasm/test-release-wasm.py"
    "tests/Wasm.Supervisor.Compatibility/root-canonical-review.json"
    "tests/Wasm.Supervisor.Compatibility/reviewed-source-proof.json"
    "docs/roadmaps/wasm-shared-01.md"
    "scripts/wasm-release/qualify-supervisor-custody.sh"; "scripts/verify-wasm-contracts.sh"; "scripts/verify-wasm-lifecycle.sh"
    "scripts/wasm-release/prepare.sh"; "scripts/verify-wasm-supervisor.sh"; "scripts/verify-wasm-package-consumer.sh"; "scripts/verify-wasm-workflow.sh"
    ".github/workflows/wasm-shared.yml"
    "tests/Wasm.Contracts.PortableConsumers/DotNet/DotNet.fsproj"
    "tests/Wasm.Contracts.PortableConsumers/Fable/Fable.fsproj"
    "tests/Wasm.PackageConsumer/Consumer.fsproj"
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
