#!/usr/bin/env python3
"""Exercise the read-only workflow's real immutable receipt binding predicates."""
import copy
import sys
sys.dont_write_bytecode = True
import importlib.util
import pathlib

root = pathlib.Path(__file__).resolve().parents[3]
text = (root / ".github/workflows/wasm-installed-org.yml").read_text()
spec = importlib.util.spec_from_file_location("binding", root / "scripts/wasm-release/bind-installed-release.py")
binding = importlib.util.module_from_spec(spec)
spec.loader.exec_module(binding)


def validate(candidate):
    assert "on:\n  workflow_dispatch:\n    inputs:" in candidate and "  push:" not in candidate
    assert "packages: read" in candidate and candidate.count("    runs-on:") == 1
    assert all(value not in candidate for value in ("packages: write", "contents: write", "id-token:", "dotnet pack", "nuget push", "gh release", "git tag"))
    for name in ("version", "published_source", "qualifier_source", "manifest_sha256", "sdk_sha256", "accepted_executor", "promotion_binding_sha256"):
        assert f"      {name}:" in candidate and f"${{{{ inputs.{name} }}}}" in candidate
    assert "feed: [org, public]" in candidate and "max-parallel: 1" in candidate
    assert "https://nuget.pkg.github.com/FS-GG/index.json" in candidate and "https://api.nuget.org/v3/index.json" in candidate
    assert "run: python3 scripts/wasm-release/bind-installed-release.py" in candidate
    assert "scripts/verify-wasm-package-consumer.sh --feed-only" in candidate
    assert "scripts/wasm-release/qualify-supervisor-installed.sh --feed-only" in candidate
    assert "matrix.feed == 'org'" in candidate and "NuGetPackageSourceCredentials_wasm:" in candidate
    steps = ["Static installed qualification preflight", "Bind qualification source and immutable published receipts", "Install exact consumer tools", "Qualify fresh installed consumer"]
    positions = [candidate.index(value) for value in steps]
    assert positions == sorted(positions)
    assert "fetch-depth: 0" in candidate and "ref:" not in candidate


validate(text)
for broken in (
    text.replace("packages: read", "packages: write"),
    text.replace("--feed-only", "--custody artifacts/wasm-release"),
    text.replace("scripts/wasm-release/qualify-supervisor-installed.sh --feed-only", "true"),
    text.replace("max-parallel: 1", "max-parallel: 2"),
    text.replace("python3 scripts/wasm-release/bind-installed-release.py", "true"),
    text.replace("          fetch-depth: 0", "          fetch-depth: 0\n          ref: wasm/v0.1.1"),
):
    try:
        validate(broken)
    except (AssertionError, ValueError):
        pass
    else:
        raise SystemExit("known-bad installed qualification route accepted")

values = {"RELEASE_VERSION": "0.2.0", "PUBLISHED_SOURCE": "a" * 40, "QUALIFIER_SOURCE": "b" * 40, "MANIFEST_SHA256": "c" * 64, "SDK_SHA256": "d" * 64}
assert binding.checked_inputs(values, "0.2.0") == values
for key, value in (("PUBLISHED_SOURCE", "main"), ("QUALIFIER_SOURCE", "wasm/v0.2.0"), ("SDK_SHA256", "unknown"), ("RELEASE_VERSION", "0.1.1"), ("MANIFEST_SHA256", "$(false)")):
    try:
        binding.checked_inputs(dict(values, **{key: value}), "0.2.0")
    except AssertionError:
        pass
    else:
        raise SystemExit(f"unbound dispatch {key} accepted")
release = {"tag_name": "wasm/v0.2.0", "draft": False, "prerelease": False, "published_at": "2026-10-02T00:00:00Z", "assets": []}
for name, digest in (("release-manifest.json", "c" * 64), ("fsgg-wasm-sdk-0.2.0.tar.gz", "d" * 64)):
    release["assets"].append({"name": name, "digest": "sha256:" + digest, "browser_download_url": f"https://github.com/FS-GG/FS.GG.Game/releases/download/wasm/v0.2.0/{name}"})
assert len(binding.validate_release(release, values)) == 2
for mutant in ("digest", "url", "draft", "version", "missing"):
    broken = copy.deepcopy(release)
    if mutant == "digest": broken["assets"][0]["digest"] = "sha256:" + "0" * 64
    elif mutant == "url": broken["assets"][1]["browser_download_url"] = "https://example.invalid/archive.tar.gz"
    elif mutant == "draft": broken["draft"] = True
    elif mutant == "version": broken["tag_name"] = "wasm/v0.1.1"
    else: broken["assets"].pop()
    try:
        binding.validate_release(broken, values)
    except (AssertionError, KeyError):
        pass
    else:
        raise SystemExit(f"native release {mutant} mismatch accepted")

# Exercise the production hash predicate with reviewed producer inventory and actual qualifier bytes.
# The installed binding separately reads BOTH immutable Git revisions; this static check needs no history fetch.
import hashlib
import json
source = "4afacb501b9371b4cc81494b7bf91b46c880a663"
audit = json.loads((root / "scripts/wasm-release/installed-qualification-audit.json").read_text())
producer = {row["path"]: {"blob": row["producerBlob"], "sha256": row["producerSha256"]} for row in audit["files"]}
qualifier = {path: binding.fingerprint((root / path).read_bytes()) for path in binding.AUDITED_CONSUMER_PATHS}
changed = sorted(binding.AUDITED_CONSUMER_PATHS)
binding.validate_consumer_audit(audit, source, "0.2.0", changed, producer, qualifier)
for mutation in ("extra-consumer", "changed-spec", "changed-projection", "wrong-producer", "extra-audit-path", "duplicate-row", "wrong-blob"):
    broken = copy.deepcopy(audit)
    names = list(changed)
    bytes_ = dict(qualifier)
    producer_source = source
    if mutation == "extra-consumer": names.append("tests/Wasm.PackageConsumer/browser/package-consumer.mjs")
    elif mutation == "changed-spec": bytes_["tests/Wasm.PackageConsumer/browser/package-consumer.spec.mjs"] = binding.fingerprint((root / "tests/Wasm.PackageConsumer/browser/package-consumer.spec.mjs").read_bytes() + b"\n// unreviewed relaxation")
    elif mutation == "changed-projection": bytes_["tests/Wasm.PackageConsumer/Program.fs"] = binding.fingerprint((root / "tests/Wasm.PackageConsumer/Program.fs").read_bytes() + b"\n// unreviewed policy")
    elif mutation == "wrong-producer": producer_source = "a" * 40
    elif mutation == "extra-audit-path": broken["files"][0]["path"] = "tests/Wasm.PackageConsumer/browser/package-consumer.mjs"
    elif mutation == "duplicate-row": broken["files"].append(broken["files"][0])
    else: broken["files"][0]["producerBlob"] = "0" * 40
    try:
        binding.validate_consumer_audit(broken, producer_source, "0.2.0", names, producer, bytes_)
    except (AssertionError, KeyError):
        pass
    else:
        raise SystemExit(f"unreviewed qualification correction {mutation} accepted")
# Both traces come from schedules over the unchanged canonical model and preserve full ordered effects.
trace_root = root / "tests/Wasm.Lifecycle.Correspondence/Traces"
for timing, order, kinds in (
    ("early", [1, 2, 3, 4, 5], ["terminate", "cancelTimer", "settleTimedOut", "settleTimedOut", "settleTimedOut"]),
    ("late", [1, 2, 4, 5, 3], ["settleTimedOut", "settleTimedOut", "terminate", "cancelTimer", "settleTimedOut"]),
):
    trace = json.loads((trace_root / f"expiry-{timing}_0.itf.json").read_text())
    state = trace["states"][-1]["state"]
    assert [int(value["#bigint"]) for value in state["deliveries"]] == order
    assert [effect["kind"] for effect in state["effects"]] == kinds
    assert state["activeWorker"] == {"#bigint": "0"} and not state["ordinary"]
    assert len(set(order)) == 5
print("wasm-installed-input-audit: exact-two-reviewed-blobs=pass extra-or-unreviewed-consumer=refused expiry-early-late=canonical")

print("wasm-installed-preflight: manual=pass read-only=pass serial-two-feeds=pass qualifier=immutable native-receipts=bound negative-controls=pass")
