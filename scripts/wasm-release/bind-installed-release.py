#!/usr/bin/env python3
"""Bind a read-only installed qualification to an immutable published release."""
import hashlib
import sys
sys.dont_write_bytecode = True
import importlib.util
import json
import os
import pathlib
import re
import subprocess
import urllib.request
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[2]
REPO = "FS-GG/FS.GG.Game"


def checked_inputs(environment, tracked_version):
    values = {key: environment[key] for key in ("RELEASE_VERSION", "PUBLISHED_SOURCE", "QUALIFIER_SOURCE", "MANIFEST_SHA256", "SDK_SHA256")}
    assert re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", values["RELEASE_VERSION"]), "invalid stable version"
    assert values["RELEASE_VERSION"] == tracked_version, "qualifier version differs from selected release"
    for key in ("PUBLISHED_SOURCE", "QUALIFIER_SOURCE"):
        assert re.fullmatch(r"[0-9a-f]{40}", values[key]), "source must be an immutable commit"
    for key in ("MANIFEST_SHA256", "SDK_SHA256"):
        assert re.fullmatch(r"[0-9a-f]{64}", values[key]), "receipt must be an exact SHA-256"
    if values["RELEASE_VERSION"] == "0.3.0":
        for key, length in (("ACCEPTED_EXECUTOR",40),("PROMOTION_BINDING_SHA256",64)):
            values[key] = environment[key]
            assert re.fullmatch(r"[0-9a-f]{%d}" % length, values[key]), "invalid explicit executor/transaction binding"
        assert values["QUALIFIER_SOURCE"] == values["ACCEPTED_EXECUTOR"]
    return values


def validate_release(release, values):
    version = values["RELEASE_VERSION"]
    tag = f"wasm/v{version}"
    assert release["tag_name"] == tag and not release["draft"] and not release["prerelease"]
    assert release["published_at"], "release is unpublished"
    assets = {item["name"]: item for item in release["assets"]}
    selected = {}
    for name, expected in (("release-manifest.json", values["MANIFEST_SHA256"]), (f"fsgg-wasm-sdk-{version}.tar.gz", values["SDK_SHA256"])):
        item = assets[name]
        assert item["browser_download_url"] == f"https://github.com/{REPO}/releases/download/{tag}/{name}", "asset identity mismatch"
        assert item.get("digest") == "sha256:" + expected, "native release asset digest mismatch"
        selected[name] = item["browser_download_url"]
    if version == "0.3.0":
        sha = values["PROMOTION_BINDING_SHA256"]
        assert re.fullmatch(r"[0-9a-f]{64}", sha), "invalid promotion record hash"
        row = assets["promotion-binding.json"]
        assert row.get("digest") == "sha256:" + sha, "native transaction digest mismatch"
        assert row["browser_download_url"] == f"https://github.com/{REPO}/releases/download/{tag}/promotion-binding.json"
        selected["promotion-binding.json"] = row["browser_download_url"]
    return selected


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True).strip()


AUDITED_CONSUMER_PATHS = {
    "tests/Wasm.PackageConsumer/Program.fs",
    "tests/Wasm.PackageConsumer/browser/package-consumer.spec.mjs",
}
FROZEN_INPUTS = [
    "src/Wasm.Browser", "src/Wasm.Contracts", "sdk/wasm", "examples/wasm",
    "eng/wasm-shared/version.props", "eng/wasm-shared/lifecycle.qnt",
    "scripts/verify-wasm-package-consumer.sh", "scripts/wasm-release/release_manifest.py",
    "scripts/wasm-release/api-baseline-policy.json", "scripts/wasm-release/build-worker-policy.sh",
    "scripts/wasm-release/prepare.sh", ".github/workflows/wasm-installed-org.yml",
    ".config/dotnet-tools.json", "global.json", "Directory.Build.*",
    "Directory.Packages.*", "nuget.config", "NuGet.Config",
]


def blob_sha(data):
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def fingerprint(data):
    return {"blob": blob_sha(data), "sha256": hashlib.sha256(data).hexdigest()}


def validate_consumer_audit(audit, source, version, changed, producer, qualifier):
    assert set(audit) == {"schema", "publishedSource", "version", "reason", "files"}, "unreviewed audit fields"
    assert audit["schema"] == "fsgg.wasm.installed-qualification-audit/v1"
    assert audit["publishedSource"] == source == "4afacb501b9371b4cc81494b7bf91b46c880a663", "audit names another producer"
    assert audit["version"] == version == "0.2.0" and audit["reason"]
    rows = {row["path"]: row for row in audit["files"]}
    assert len(rows) == len(audit["files"]) == 2 and set(rows) == AUDITED_CONSUMER_PATHS, "unreviewed consumer allowlist"
    assert set(changed) == AUDITED_CONSUMER_PATHS, "extra or missing consumer change"
    for path, row in rows.items():
        assert set(row) == {"path", "producerBlob", "producerSha256", "qualifierBlob", "qualifierSha256"}
        for prefix, data in (("producer", producer[path]), ("qualifier", qualifier[path])):
            assert row[prefix + "Blob"] == data["blob"], "consumer Git blob differs from reviewed audit"
            assert row[prefix + "Sha256"] == data["sha256"], "consumer bytes differ from reviewed audit"
        assert row["producerBlob"] != row["qualifierBlob"], "audit does not describe a correction"
    return audit


# An accepted executor commit is an explicit route input. The producer remains P;
# only this finite publisher/qualification glue delta can differ from P at 0.3.
PUBLISHER_SOURCE = "16a401692f4c0dee7f6da1a86d0cd49e6ea3ec2c"
EXECUTOR_GLUE = {
    "scripts/wasm-release/prepare-quint-evaluator.py", "tests/release/wasm/test-quint-evaluator-preparation.py",
    ".github/workflows/release-wasm.yml", ".github/workflows/wasm-installed-org.yml",
    "scripts/wasm-release/promotion.py", "scripts/wasm-release/readback.sh",
    "scripts/wasm-release/stage-assets.sh", "scripts/wasm-release/bind-installed-release.py",
    "scripts/wasm-release/qualify-supervisor-installed.sh", "scripts/verify-wasm-supervisor.sh",
    "scripts/verify-wasm-supervisor-browser.sh", "tests/release/wasm/test-release-wasm.py",
    "tests/release/wasm/test-release-wasm.sh", "tests/release/wasm/test-installed-org-workflow.py",
    "tests/release/wasm/test-consumer-source-mapping.py", "tests/release/wasm/test-release-promotion.py",
    "docs/roadmaps/wasm-shared-01.md",
}

def bind_executor(source, executor, version):
    assert version == "0.3.0" and source == PUBLISHER_SOURCE, "wrong protected producer"
    assert executor == os.environ["ACCEPTED_EXECUTOR"] == os.environ["GITHUB_SHA"], "executor not selected by reviewed dispatch"
    changed = git("diff", "--name-only", source, executor).splitlines()
    assert changed and len(changed) == len(set(changed)) and set(changed) <= EXECUTOR_GLUE, "executor changes preserved producer inputs"
    rows = []
    for path in sorted(changed):
        body = subprocess.check_output(["git", "-C", str(ROOT), "show", executor + ":" + path])
        rows.append(dict(path=path, **fingerprint(body)))
    return {"consumerInputs": "protected-producer-preserved", "producer": source, "acceptedExecutor": executor, "executorDelta": rows}


def bind_qualification_inputs(source, qualifier_source, version, release_binding=None):
    if version == "0.3.0":
        assert not os.environ.get("CANONICAL_QUALIFICATION_AUDIT"), "historical audit cannot authorize 0.3"
        return bind_executor(source, qualifier_source, version)
    canonical = None
    if os.environ.get("CANONICAL_QUALIFICATION_AUDIT"):
        assert release_binding is not None, "model amendment requires genuine release receipts"
        spec = importlib.util.spec_from_file_location("canonical_transport", ROOT / "scripts/wasm-release/bind-canonical-qualification.py")
        transport = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(transport)
        canonical = transport.bind(source, qualifier_source, version, FROZEN_INPUTS, release_binding)
    frozen = FROZEN_INPUTS if canonical is None else [path for path in FROZEN_INPUTS if path != "eng/wasm-shared/lifecycle.qnt"]
    subprocess.run(["git", "-C", str(ROOT), "diff", "--exit-code", source, qualifier_source, "--", *frozen], check=True)
    changed = git("diff", "--name-only", source, qualifier_source, "--", "tests/Wasm.PackageConsumer").splitlines()
    if not changed:
        return dict({"consumerInputs": "identical-to-published-source"}, **(canonical or {}))
    audit_path = ROOT / "scripts/wasm-release/installed-qualification-audit.json"
    audit = json.loads(audit_path.read_text())
    def read(revision, path):
        return subprocess.check_output(["git", "-C", str(ROOT), "show", revision + ":" + path])
    producer = {path: fingerprint(read(source, path)) for path in AUDITED_CONSUMER_PATHS}
    qualifier = {path: fingerprint(read(qualifier_source, path)) for path in AUDITED_CONSUMER_PATHS}
    validate_consumer_audit(audit, source, version, changed, producer, qualifier)
    return dict({"consumerInputs": "exact-reviewed-qualification-correction", "auditSha256": hashlib.sha256(audit_path.read_bytes()).hexdigest(), "audit": audit}, canonicalAmendment=canonical) if canonical else {"consumerInputs": "exact-reviewed-qualification-correction", "auditSha256": hashlib.sha256(audit_path.read_bytes()).hexdigest(), "audit": audit}


def main():
    tracked = ET.parse(ROOT / "eng/wasm-shared/version.props").findtext(".//WasmSharedVersion")
    values = checked_inputs(os.environ, tracked)
    assert not git("status", "--porcelain"), "installed qualifier must be a clean accepted executor"
    assert git("rev-parse", "HEAD") == values["QUALIFIER_SOURCE"] == os.environ["GITHUB_SHA"], "wrong qualifier checkout"
    source = values["PUBLISHED_SOURCE"]
    tag = "wasm/v" + values["RELEASE_VERSION"]
    assert git("rev-parse", f"refs/tags/{tag}^{{commit}}") == source, "tag does not name published source"
    subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", source, "origin/main"], check=True)
    # Preserve every producer/SDK/toolchain input. Only the two exact reviewed
    # consumer projection/assertion blobs may differ, bound by the immutable audit.
    # Model amendments are decided only after genuine immutable release receipts are acquired.
    request = urllib.request.Request(f"https://api.github.com/repos/{REPO}/releases/tags/wasm%2Fv{values['RELEASE_VERSION']}", headers={"Accept": "application/vnd.github+json", "Authorization": "Bearer " + os.environ["GH_TOKEN"], "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request) as response:
        release = json.load(response)
    selected = validate_release(release, values)
    receipts = pathlib.Path(os.environ["RUNNER_TEMP"]) / "wasm-installed-receipts"
    receipts.mkdir()
    for name, url in selected.items():
        destination = receipts / name
        subprocess.run(["curl", "--fail", "--location", "--proto", "=https", "--tlsv1.2", url, "--output", str(destination)], check=True)
        expected = values["MANIFEST_SHA256"] if name == "release-manifest.json" else values["SDK_SHA256"] if name.endswith(".tar.gz") else values["PROMOTION_BINDING_SHA256"]
        assert hashlib.sha256(destination.read_bytes()).hexdigest() == expected, "downloaded release bytes mismatch"
    manifest = json.loads((receipts / "release-manifest.json").read_text())
    spec = importlib.util.spec_from_file_location("release_manifest", ROOT / "scripts/wasm-release/release_manifest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.verify_identity(manifest, source)
    assert manifest["version"] == values["RELEASE_VERSION"] and manifest["tree"] == git("rev-parse", f"{source}^{{tree}}")
    sdk = next(row for row in manifest["artifacts"] if row["file"] == f"fsgg-wasm-sdk-{values['RELEASE_VERSION']}.tar.gz")
    assert sdk["sha256"] == values["SDK_SHA256"]
    release_binding = {"manifestSha256": values["MANIFEST_SHA256"], "sdkSha256": values["SDK_SHA256"],
                       "packages": [{"name": row["file"], "sha256": row["sha256"]} for row in manifest["artifacts"] if row["file"].endswith(".nupkg")]}
    if values["RELEASE_VERSION"] == "0.3.0":
        spec = importlib.util.spec_from_file_location("promotion", ROOT / "scripts/wasm-release/promotion.py")
        promotion = importlib.util.module_from_spec(spec); spec.loader.exec_module(promotion)
        transaction = json.loads((receipts / "promotion-binding.json").read_text())
        reviewed = promotion.validate_tuple(transaction["preparation"])
        bound = reviewed["binding"]
        assert transaction["schema"] == "fsgg.wasm.promotion/v1" and transaction["producer"] == source and transaction["tag"] == tag
        assert bound["executor"] == values["ACCEPTED_EXECUTOR"] and bound["manifestSha256"] == values["MANIFEST_SHA256"]
        assert bound["archives"][sdk["file"]] == values["SDK_SHA256"]
        assert {row["file"]:row["sha256"] for row in manifest["artifacts"]} == bound["archives"]
    input_audit = bind_qualification_inputs(source, values["QUALIFIER_SOURCE"], values["RELEASE_VERSION"], release_binding)
    print("wasm-installed-input-audit: " + json.dumps(input_audit, sort_keys=True))
    print(f"wasm-installed-binding: qualifier={values['QUALIFIER_SOURCE']} published={source} tag={tag} native-assets=verified manifest={values['MANIFEST_SHA256']} sdk={values['SDK_SHA256']}")


if __name__ == "__main__":
    main()
