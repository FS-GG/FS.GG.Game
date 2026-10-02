/// Exact producer-approved model-only amendment. No package, runtime or source rewrite.
module CanonicalQualificationPolicy

open System
open System.Text.RegularExpressions

type FileBinding = { Path: string; PreviousSha256: string; QualifierSha256: string }
type PackageBinding = { Name: string; Sha256: string }
type Audit = {
    Schema: string; Status: string; PublishedSource: string; ProtectedSource: string; Version: string
    PublishedModelSha256: string; ModelSha256: string; ProducerLink: string
    ReleaseManifestSha256: string; SdkSha256: string; Packages: PackageBinding array
    Model: FileBinding; QualifierInputs: FileBinding array; ProofInputs: FileBinding array
    OriginalTraces: FileBinding array; ApprovedChangedPaths: string array
}
type Observation = {
    PublishedSource: string; ProtectedSource: string; Version: string
    CallerHead: string; QualifierHead: string; ProducerLinked: bool; ProtectedLinked: bool
    AuditSha256: string; ExpectedAuditSha256: string; PublishedModelSha256: string
    ReleaseManifestSha256: string; SdkSha256: string; Packages: PackageBinding array
    Model: FileBinding; Inputs: FileBinding array; ChangedPaths: string array; FrozenChangedPaths: string array
}
type Decision = { Accepted: bool; Kind: string; Reasons: string array; QualifierHead: string; AuditSha256: string }

let producer = "4afacb501b9371b4cc81494b7bf91b46c880a663"
let protectedSource = "7397d4b408b8e7b44393fee9f837278723934867"
let oldModel = "e463e49a44ded21e1f30379aef25043e2bab2471538b9affde71a721ecfae8a8"
let publishedManifest = "dee8b02ad0e43e7d279492ed6378eb873f76edeb5a72c29e9c336409d5907853"
let publishedSdk = "ddd7b0ea76a1811ec3cdd45c373ac0ee556cf90eb9ae4cecad48dec1095475a3"
let modelPath = "eng/wasm-shared/lifecycle.qnt"
let private digest value = not (isNull value) && Regex.IsMatch(value, "^[0-9a-f]{64}$")
let private commit value = not (isNull value) && Regex.IsMatch(value, "^[0-9a-f]{40}$")
let private safePath (value: string) =
    not (String.IsNullOrWhiteSpace value) && not (value.StartsWith "/")
    && not (value.Contains "\\") && not (value.Split '/' |> Array.contains "..")
let private qualificationPath (value: string) =
    safePath value && (
        value.StartsWith("tests/Wasm.Lifecycle.Correspondence/", StringComparison.Ordinal)
        || (value.StartsWith("eng/wasm-shared/", StringComparison.Ordinal) && value.EndsWith(".qnt", StringComparison.Ordinal))
        || value = "scripts/verify-wasm-lifecycle.sh" || value = "docs/wasm/runtime.md" || value = "docs/roadmaps/wasm-shared-01.md"
        || value = "scripts/wasm-release/bind-installed-release.py"
        || value = "scripts/wasm-release/bind-canonical-qualification.py"
        || value = "scripts/wasm-release/CanonicalQualificationPolicy.fs"
        || value = "scripts/wasm-release/decide-canonical-qualification.fsx"
        || value = "scripts/wasm-release/canonical-qualification-amendment.json"
        || value = "tests/release/wasm/test-canonical-qualification-policy.fsx"
        || value = "tests/release/wasm/test-canonical-qualification-transport.py")
let private unique (values: string array) = Array.distinct values |> Array.length = values.Length

/// Administrative eligibility only. Proof hash integrity does not assert that runtime checks passed.
let decide (audit: Audit) (observed: Observation) : Decision =
    let reasons = ResizeArray<string>()
    let require condition reason = if not condition then reasons.Add reason
    require (audit.Schema = "fsgg.wasm.canonical-qualification-amendment/v1" && audit.Status = "APPROVED") "amendment is not approved"
    require (audit.PublishedSource = producer && observed.PublishedSource = producer
             && audit.ProtectedSource = protectedSource && observed.ProtectedSource = protectedSource
             && audit.Version = "0.2.0" && observed.Version = "0.2.0") "published/protected identity mismatch"
    require (observed.ProducerLinked && observed.ProtectedLinked && audit.ProducerLink = "FS-GG/FS.GG.Game@" + producer) "missing producer linkage"
    require (commit observed.CallerHead && observed.CallerHead = observed.QualifierHead) "caller exact HEAD differs from qualifier receipt"
    require (digest observed.ExpectedAuditSha256 && observed.AuditSha256 = observed.ExpectedAuditSha256) "reviewed amendment hash mismatch"
    require (audit.PublishedModelSha256 = oldModel && observed.PublishedModelSha256 = oldModel) "historical canonical hash mismatch"
    require (audit.Model.Path = modelPath && observed.Model = audit.Model && audit.Model.PreviousSha256 = oldModel
             && digest audit.ModelSha256 && audit.ModelSha256 <> oldModel && audit.Model.QualifierSha256 = audit.ModelSha256) "substituted or stale canonical model"
    require (audit.ReleaseManifestSha256 = publishedManifest && audit.ReleaseManifestSha256 = observed.ReleaseManifestSha256
             && audit.SdkSha256 = publishedSdk && audit.SdkSha256 = observed.SdkSha256) "published release receipt changed"
    let packageNames = Set ["FS.GG.Wasm.Contracts.0.2.0.nupkg"; "FS.GG.Wasm.Browser.0.2.0.nupkg"]
    require (audit.Packages.Length = 2 && observed.Packages.Length = 2
             && Set.ofArray (Array.map _.Name audit.Packages) = packageNames
             && (audit.Packages |> Array.forall (fun row -> digest row.Sha256))
             && (audit.Packages |> Array.sortBy _.Name) = (observed.Packages |> Array.sortBy _.Name)) "published package changed"
    require (observed.FrozenChangedPaths |> Array.forall ((=) modelPath)) "executable/runtime/SDK/toolchain input changed"
    require (unique audit.ApprovedChangedPaths && unique observed.ChangedPaths
             && Set.ofArray audit.ApprovedChangedPaths = Set.ofArray observed.ChangedPaths
             && (audit.ApprovedChangedPaths |> Array.forall qualificationPath)) "extra or missing qualification touched path"
    require (audit.QualifierInputs.Length > 0 && audit.ProofInputs.Length > 0 && audit.OriginalTraces.Length > 0) "missing qualifier source/proof/original trace binding"
    let expected = Array.concat [ [|audit.Model|]; audit.QualifierInputs; audit.ProofInputs; audit.OriginalTraces ]
    require (unique (expected |> Array.map _.Path) && unique (observed.Inputs |> Array.map _.Path)) "duplicate input binding"
    require (expected |> Array.forall (fun row -> qualificationPath row.Path && digest row.QualifierSha256
                    && (row.PreviousSha256 = "ABSENT" || digest row.PreviousSha256))) "invalid input fingerprint"
    require ((expected |> Array.sortBy _.Path) = (observed.Inputs |> Array.sortBy _.Path)) "stale qualifier/proof/original trace hash"
    let fingerprinted = expected |> Array.map _.Path |> Set.ofArray
    let administrativeAudit = "scripts/wasm-release/canonical-qualification-amendment.json"
    require (audit.ApprovedChangedPaths |> Array.forall (fun path -> path = administrativeAudit || Set.contains path fingerprinted)) "changed qualification file lacks exact fingerprint"
    { Accepted = reasons.Count = 0; Kind = if reasons.Count = 0 then "EXACT_MODEL_ONLY_QUALIFICATION_AMENDMENT" else "REFUSED"
      Reasons = reasons.ToArray(); QualifierHead = observed.QualifierHead; AuditSha256 = observed.AuditSha256 }
