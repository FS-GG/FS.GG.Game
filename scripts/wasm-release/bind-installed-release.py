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
    for key, length in (("ACCEPTED_EXECUTOR",40),("PROMOTION_BINDING_SHA256",64)):
        values[key] = environment[key]
        assert re.fullmatch(r"[0-9a-f]{%d}" % length, values[key]), "invalid explicit executor/transaction binding"
    assert values["RELEASE_VERSION"] == "0.4.0", "historical release is not the selected current route"
    assert values["PUBLISHED_SOURCE"] == values["QUALIFIER_SOURCE"] == values["ACCEPTED_EXECUTOR"], "current producer must equal executor/qualifier"
    assert not environment.get("EXECUTION_BINDING") and not environment.get("CANONICAL_QUALIFICATION_AUDIT"), "historical overlays cannot authorize current qualification"
    return values


def validate_release(release, values):
    version = values["RELEASE_VERSION"]
    assert version == "0.4.0", "only selected current release can qualify"
    tag = f"wasm/v{version}"
    assert release["tag_name"] == tag and not release["draft"] and not release["prerelease"]
    assert release["published_at"], "release is unpublished"
    assets = {item["name"]: item for item in release["assets"]}
    assert len(assets) == len(release["assets"]), "duplicate release assets"
    selected = {}
    for name, expected in (("release-manifest.json", values["MANIFEST_SHA256"]), (f"fsgg-wasm-sdk-{version}.tar.gz", values["SDK_SHA256"])):
        item = assets[name]
        assert item["browser_download_url"] == f"https://github.com/{REPO}/releases/download/{tag}/{name}", "asset identity mismatch"
        assert item.get("digest") == "sha256:" + expected, "native release asset digest mismatch"
        selected[name] = item["browser_download_url"]
    sha = values["PROMOTION_BINDING_SHA256"]
    assert re.fullmatch(r"[0-9a-f]{64}", sha), "invalid promotion record hash"
    row = assets["promotion-binding.json"]
    assert row.get("digest") == "sha256:" + sha, "native transaction digest mismatch"
    assert row["browser_download_url"] == f"https://github.com/{REPO}/releases/download/{tag}/promotion-binding.json"
    selected["promotion-binding.json"] = row["browser_download_url"]
    return selected


def git(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True).strip()


def bind_executor(source, executor, version):
    assert version == '0.4.0' and source == executor == os.environ['ACCEPTED_EXECUTOR'] == os.environ['GITHUB_SHA'] == git('rev-parse','HEAD'), 'current installed inputs require identical protected producer/executor'
    assert not git('status','--porcelain'), 'dirty installed producer'
    return {'consumerInputs':'identical-protected-current-source','producer':source,'acceptedExecutor':executor}


def bind_qualification_inputs(source, qualifier_source, version, release_binding=None):
    assert not os.environ.get('CANONICAL_QUALIFICATION_AUDIT') and not os.environ.get('EXECUTION_BINDING'), 'historical audit/overlay cannot authorize current qualification'
    return bind_executor(source,qualifier_source,version)


def bind_promotion_execution(promotion, transaction, values, release, receipts):
    reviewed=promotion.validate_tuple(transaction['preparation'])
    assert promotion.execution_input() is None
    assert reviewed['binding']['producer'] == reviewed['binding']['executor'] == values['PUBLISHED_SOURCE'] == values['ACCEPTED_EXECUTOR']
    native=promotion.Native()
    promotion.execution_checkout(native,reviewed,installed=True)
    _,actual=promotion.release_state(native)
    assert actual==release, 'native tag/list/by-ID published release disagreement'
    promotion.verify_transaction(native,release,reviewed)
    return reviewed


def main():
    tracked = ET.parse(ROOT / "eng/wasm-shared/version.props").findtext(".//WasmSharedVersion")
    values = checked_inputs(os.environ, tracked)
    assert not git("status", "--porcelain"), "installed qualifier must be a clean accepted executor"
    assert git("rev-parse", "HEAD") == values["QUALIFIER_SOURCE"] == os.environ["GITHUB_SHA"], "wrong qualifier checkout"
    source = values["PUBLISHED_SOURCE"]
    tag = "wasm/v" + values["RELEASE_VERSION"]
    assert git("rev-parse", f"refs/tags/{tag}^{{commit}}") == source, "tag does not name published source"
    subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", source, "origin/main"], check=True)
    # Current installed acceptance uses the identical protected producer inputs.
    spec=importlib.util.spec_from_file_location('promotion',ROOT/'scripts/wasm-release/promotion.py')
    promotion=importlib.util.module_from_spec(spec);spec.loader.exec_module(promotion)
    release=promotion.Native().checked(f"repos/{REPO}/releases/tags/wasm%2Fv{values['RELEASE_VERSION']}")
    selected = validate_release(release, values)
    receipts = pathlib.Path(os.environ["RUNNER_TEMP"]) / "wasm-installed-receipts"
    receipts.mkdir()
    for name, url in selected.items():
        destination = receipts / name
        subprocess.run(["curl", "--max-time", "30", "--max-filesize", "536870912", "--fail", "--location", "--proto", "=https", "--tlsv1.2", url, "--output", str(destination)], check=True)
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
    spec = importlib.util.spec_from_file_location("promotion", ROOT / "scripts/wasm-release/promotion.py")
    promotion = importlib.util.module_from_spec(spec); spec.loader.exec_module(promotion)
    transaction = json.loads((receipts / "promotion-binding.json").read_text())
    reviewed = promotion.validate_tuple(transaction["preparation"])
    bound = reviewed["binding"]
    assert transaction["schema"] == "fsgg.wasm.promotion/v1" and transaction["producer"] == source and transaction["tag"] == tag
    bind_promotion_execution(promotion,transaction,values,release,receipts)
    assert bound["manifestSha256"] == values["MANIFEST_SHA256"]
    assert bound["archives"][sdk["file"]] == values["SDK_SHA256"]
    assert {row["file"]:row["sha256"] for row in manifest["artifacts"]} == bound["archives"]
    input_audit = bind_qualification_inputs(source, values["QUALIFIER_SOURCE"], values["RELEASE_VERSION"], release_binding)
    print("wasm-installed-input-audit: " + json.dumps(input_audit, sort_keys=True))
    print(f"wasm-installed-binding: qualifier={values['QUALIFIER_SOURCE']} published={source} tag={tag} native-assets=verified manifest={values['MANIFEST_SHA256']} sdk={values['SDK_SHA256']}")


if __name__ == "__main__":
    main()
