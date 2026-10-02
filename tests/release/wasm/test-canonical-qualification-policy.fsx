#load "../../../scripts/wasm-release/CanonicalQualificationPolicy.fs"
open CanonicalQualificationPolicy

// Exercise the actual administrative owner. Synthetic identities are labelled test fixtures.
let hash digit = String.replicate 64 digit
let row path previous current : FileBinding = { Path=path; PreviousSha256=previous; QualifierSha256=current }
let model = row modelPath oldModel (hash "a")
let qualifier = row "tests/Wasm.Lifecycle.Correspondence/Correspondence.fs" (hash "b") (hash "c")
let proof = row "tests/Wasm.Lifecycle.Correspondence/qualification-proof.json" "ABSENT" (hash "d")
let trace = row "tests/Wasm.Lifecycle.Correspondence/Traces/sc2_0.itf.json" (hash "e") (hash "e")
let packages : PackageBinding array = [|
    { Name="FS.GG.Wasm.Browser.0.2.0.nupkg"; Sha256=hash "1" }
    { Name="FS.GG.Wasm.Contracts.0.2.0.nupkg"; Sha256=hash "2" }
|]
let audit : Audit = {
    Schema="fsgg.wasm.canonical-qualification-amendment/v1"; Status="APPROVED"
    PublishedSource=producer; ProtectedSource=protectedSource; Version="0.2.0"
    PublishedModelSha256=oldModel; ModelSha256=model.QualifierSha256; ProducerLink="FS-GG/FS.GG.Game@"+producer
    ReleaseManifestSha256=publishedManifest; SdkSha256=publishedSdk; Packages=packages
    Model=model; QualifierInputs=[|qualifier|]; ProofInputs=[|proof|]; OriginalTraces=[|trace|]
    ApprovedChangedPaths=[|model.Path;qualifier.Path;proof.Path|]
}
let observed : Observation = {
    PublishedSource=producer; ProtectedSource=protectedSource; Version="0.2.0"
    CallerHead=String.replicate 40 "f"; QualifierHead=String.replicate 40 "f"
    ProducerLinked=true; ProtectedLinked=true; AuditSha256=hash "5"; ExpectedAuditSha256=hash "5"
    PublishedModelSha256=oldModel; ReleaseManifestSha256=audit.ReleaseManifestSha256
    SdkSha256=audit.SdkSha256; Packages=packages; Model=model; Inputs=[|model;qualifier;proof;trace|]
    ChangedPaths=audit.ApprovedChangedPaths; FrozenChangedPaths=[|modelPath|]
}
if not (decide audit observed).Accepted then failwith "valid synthetic exact binding refused"
let mutateInput path = observed.Inputs |> Array.map (fun input -> if input.Path=path then {input with QualifierSha256=hash "9"} else input)
let mutants : (string * Observation) list = [
    "substituted-model", {observed with Model={model with QualifierSha256=hash "9"}}
    "extra-runtime-file", {observed with FrozenChangedPaths=[|modelPath;"src/Wasm.Browser/extra.fs"|]}
    "extra-qualification-file", {observed with ChangedPaths=Array.append observed.ChangedPaths [|"tests/Wasm.Lifecycle.Correspondence/unreviewed.fs"|]}
    "stale-qualifierhash", {observed with Inputs=mutateInput qualifier.Path}
    "stale-proof", {observed with Inputs=mutateInput proof.Path}
    "altered-original-trace", {observed with Inputs=mutateInput trace.Path}
    "missing-producer-link", {observed with ProducerLinked=false}
    "changed-package", {observed with Packages=[|{packages[0] with Sha256=hash "9"};packages[1]|]}
    "changed-sdk", {observed with SdkSha256=hash "9"}
    "changed-release-manifest", {observed with ReleaseManifestSha256=hash "9"}
    "unreviewed-audit-hash", {observed with ExpectedAuditSha256=hash "9"}
    "wrong-caller-head", {observed with CallerHead=String.replicate 40 "a"}
    "missing-input", {observed with Inputs=observed.Inputs[0..2]}
]
for name, mutant in mutants do
    if (decide audit mutant).Accepted then failwithf "causal administrative mutant accepted: %s" name
if (decide {audit with Status="CANDIDATE_PENDING_PRODUCER_MODEL_AND_PROOF"} observed).Accepted then
    failwith "pending amendment accepted"
printfn "PASS actual F# administrative owner: exact binding + %d refusals + pending refusal" mutants.Length
