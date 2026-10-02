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
    return selected


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True).strip()


def main():
    tracked = ET.parse(ROOT / "eng/wasm-shared/version.props").findtext(".//WasmSharedVersion")
    values = checked_inputs(os.environ, tracked)
    assert git("rev-parse", "HEAD") == values["QUALIFIER_SOURCE"] == os.environ["GITHUB_SHA"], "wrong qualifier checkout"
    source = values["PUBLISHED_SOURCE"]
    tag = "wasm/v" + values["RELEASE_VERSION"]
    assert git("rev-parse", f"refs/tags/{tag}^{{commit}}") == source, "tag does not name published source"
    subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", source, "origin/main"], check=True)
    # Repaired qualification runs from its own immutable revision. Consumer,
    # SDK and toolchain provenance must still match the published producer.
    subprocess.run(["git", "-C", str(ROOT), "diff", "--exit-code", source, "HEAD", "--", "tests/Wasm.PackageConsumer", "sdk/wasm", "scripts/wasm-release/release_manifest.py", "scripts/wasm-release/api-baseline-policy.json", ".config/dotnet-tools.json", "global.json"], check=True)
    request = urllib.request.Request(f"https://api.github.com/repos/{REPO}/releases/tags/wasm%2Fv{values['RELEASE_VERSION']}", headers={"Accept": "application/vnd.github+json", "Authorization": "Bearer " + os.environ["GH_TOKEN"], "X-GitHub-Api-Version": "2022-11-28"})
    with urllib.request.urlopen(request) as response:
        release = json.load(response)
    selected = validate_release(release, values)
    receipts = pathlib.Path(os.environ["RUNNER_TEMP"]) / "wasm-installed-receipts"
    receipts.mkdir()
    for name, url in selected.items():
        destination = receipts / name
        subprocess.run(["curl", "--fail", "--location", "--proto", "=https", "--tlsv1.2", url, "--output", str(destination)], check=True)
        expected = values["MANIFEST_SHA256"] if name == "release-manifest.json" else values["SDK_SHA256"]
        assert hashlib.sha256(destination.read_bytes()).hexdigest() == expected, "downloaded release bytes mismatch"
    manifest = json.loads((receipts / "release-manifest.json").read_text())
    spec = importlib.util.spec_from_file_location("release_manifest", ROOT / "scripts/wasm-release/release_manifest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.verify_identity(manifest, source)
    assert manifest["version"] == values["RELEASE_VERSION"] and manifest["tree"] == git("rev-parse", f"{source}^{{tree}}")
    sdk = next(row for row in manifest["artifacts"] if row["file"] == f"fsgg-wasm-sdk-{values['RELEASE_VERSION']}.tar.gz")
    assert sdk["sha256"] == values["SDK_SHA256"]
    print(f"wasm-installed-binding: qualifier={values['QUALIFIER_SOURCE']} published={source} tag={tag} native-assets=verified manifest={values['MANIFEST_SHA256']} sdk={values['SDK_SHA256']}")


if __name__ == "__main__":
    main()
