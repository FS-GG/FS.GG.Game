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
    for name in ("version", "published_source", "qualifier_source", "manifest_sha256", "sdk_sha256"):
        assert f"      {name}:" in candidate and f"${{{{ inputs.{name} }}}}" in candidate
    assert "feed: [org, public]" in candidate and "max-parallel: 1" in candidate
    assert "https://nuget.pkg.github.com/FS-GG/index.json" in candidate and "https://api.nuget.org/v3/index.json" in candidate
    assert "run: python3 scripts/wasm-release/bind-installed-release.py" in candidate
    assert "scripts/verify-wasm-package-consumer.sh --feed-only" in candidate
    assert "matrix.feed == 'org'" in candidate and "NuGetPackageSourceCredentials_wasm:" in candidate
    steps = ["Static installed qualification preflight", "Bind qualification source and immutable published receipts", "Install exact consumer tools", "Qualify fresh installed consumer"]
    positions = [candidate.index(value) for value in steps]
    assert positions == sorted(positions)
    assert "fetch-depth: 0" in candidate and "ref:" not in candidate


validate(text)
for broken in (
    text.replace("packages: read", "packages: write"),
    text.replace("--feed-only", "--custody artifacts/wasm-release"),
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
print("wasm-installed-preflight: manual=pass read-only=pass serial-two-feeds=pass qualifier=immutable native-receipts=bound negative-controls=pass")
