#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import tempfile
import warnings
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
MANIFEST = ROOT / "scripts/wasm-release/release_manifest.py"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def refused(command: list[str]) -> None:
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    require(result.returncode != 0, f"known-bad control was accepted: {' '.join(command)}")


def validate_workflow(text: str) -> None:
    blocks = {match.group(1): match.group(2) for match in re.finditer(r"(?ms)^  ([a-z]+):\n(.*?)(?=^  [a-z]+:\n|\Z)", text[text.index("jobs:\n") + 6:])}
    for name in ("preflight", "custody", "readback", "org", "public", "assets", "complete"):
        require(name in blocks, f"release workflow job missing: {name}")
    require("needs: [preflight]" in blocks["custody"], "custody must follow preflight")
    require("needs: [custody]" in blocks["org"], "org publication must follow custody")
    require("needs: [org]" in blocks["public"], "public publication must follow org readback")
    require("needs: [public]" in blocks["assets"], "release assets must follow public readback")
    require("needs: [org, public, assets]" in blocks["complete"], "completion must join all readbacks")
    require("inputs.mode == 'publish' || inputs.mode == 'recovery'" in blocks["org"], "prepare-only dispatch exposes a publisher")
    for gate in ("scripts/verify-wasm-contracts.sh", "scripts/verify-wasm-lifecycle.sh", "scripts/verify-wasm-package-consumer.sh --custody"):
        require(gate in blocks["custody"], f"custody qualification is missing {gate}")


def source_checks() -> None:
    workflow = (ROOT / ".github/workflows/release-wasm.yml").read_text()
    core = (ROOT / ".github/workflows/release.yml").read_text()
    props = (ROOT / "eng/wasm-shared/version.props").read_text()
    require("<WasmSharedVersion>0.1.0</WasmSharedVersion>" in props, "stable version origin mismatch")
    for path in (ROOT / "src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj", ROOT / "src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj"):
        require("$(WasmSharedVersion)" in path.read_text(), f"{path} bypasses shared version origin")
    require((ROOT / "sdk/wasm/VERSION").read_text().strip() == "0.1.0", "SDK version mismatch")
    require('"sourceVersion": "0.1.0"' in (ROOT / "src/Wasm.Contracts/compatibility-profile.v1.json").read_text(), "profile version mismatch")
    require('Version="[$(WasmSharedVersion)]"' in (ROOT / "src/Wasm.Browser/Fable/FS.GG.Wasm.Browser.fsproj").read_text(), "Fable dependency is not exact")
    require('Version="[0.1.0]"' in (ROOT / "tests/Wasm.PackageConsumer/Consumer.fsproj").read_text(), "consumer dependency is not exact")
    cargo_files = list((ROOT / "sdk/wasm/rust").rglob("Cargo.toml")) + list((ROOT / "sdk/wasm/rust").rglob("Cargo.lock")) + list((ROOT / "examples/wasm/rust").rglob("Cargo.toml")) + list((ROOT / "examples/wasm/rust").rglob("Cargo.lock"))
    require(all("0.1.0-source" not in path.read_text() for path in cargo_files), "Cargo metadata retains a prerelease identity")
    cargo_manifests = [path for path in cargo_files if path.name == "Cargo.toml" and "[package]" in path.read_text()]
    require(all('version = "0.1.0"' in path.read_text() for path in cargo_manifests), "Cargo package version mismatch")
    validate_workflow(workflow)
    broken = workflow.replace("needs: [org]\n", "needs: [custody]\n", 1)
    try:
        validate_workflow(broken)
    except SystemExit:
        pass
    else:
        raise SystemExit("known-bad publisher ordering was accepted")
    require("cancel-in-progress: false" in workflow and "group: release-wasm-0.1.0" in workflow, "release concurrency is not version-bound")
    require("default: prepare" in workflow, "manual execution must default to prepare-only")
    require(workflow.count("dotnet nuget push") == 2, "release workflow must have exactly two package pushes")
    require(workflow.index("needs: [custody]") < workflow.index("needs: [org]") < workflow.index("needs: [public]"), "publisher job ordering mismatch")
    require("needs: [org, public, assets]" in workflow, "completion does not join all readbacks")
    require("NuGet/login@8d196754b4036150537f80ac539e15c2f1028841" in workflow, "public push lacks pinned OIDC login")
    require("NUGET_API_KEY" in workflow and "NUGET_API_KEY }}" in workflow, "public push does not use OIDC output")
    require("secrets.NUGET_API_KEY" not in workflow, "long-lived public API key fallback is forbidden")
    require(workflow.count("packages: write") == 1 and workflow.count("id-token: write") == 1, "publisher permissions are not job-scoped")
    require(core.count("github.event_name != 'release' || startsWith(github.event.release.tag_name, 'v')") == 2, "core release namespace guard missing")
    consumer = (ROOT / "scripts/verify-wasm-package-consumer.sh").read_text()
    require("--feed-only" in consumer, "feed-only consumer route missing")
    require("dotnet pack" not in consumer and "src/Wasm.Browser" not in consumer and "src/Wasm.Contracts" not in consumer, "consumer verifier can rebuild or read producer package sources")
    local_env = dict(os.environ, WASM_PACKAGE_FEED="file:///tmp/packages", WASM_RELEASE_BASE_URL="file:///tmp/release")
    result = subprocess.run([str(ROOT / "scripts/verify-wasm-package-consumer.sh"), "--feed-only"], env=local_env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    require(result.returncode != 0 and "HTTPS URLs" in result.stdout, "feed-only consumer accepted local sources")
    require(not list((ROOT / "eng/wasm-shared").glob("*release*.qnt")), "stateless release wiring must not add a disconnected Quint model")
    with tempfile.TemporaryDirectory(prefix="wasm-readback-control.") as raw:
        root = Path(raw); original = root / "original.nupkg"; signed = root / "signed.nupkg"; rezipped = root / "rezipped.nupkg"; changed = root / "changed.nupkg"; duplicate = root / "duplicate.nupkg"
        with zipfile.ZipFile(original, "w") as archive: archive.writestr("package.nuspec", b"same"); archive.writestr("lib/value.dll", b"value")
        with zipfile.ZipFile(signed, "w") as archive: archive.writestr("package.nuspec", b"same"); archive.writestr("lib/value.dll", b"value"); archive.writestr(".signature.p7s", b"served-signature")
        subprocess.run(["python3", str(MANIFEST), "compare-package", "--original", str(original), "--served", str(signed), "--allow-nuget-signature"], check=True, stdout=subprocess.DEVNULL)
        with zipfile.ZipFile(rezipped, "w", compression=zipfile.ZIP_DEFLATED) as archive: archive.writestr("package.nuspec", b"same"); archive.writestr("lib/value.dll", b"value")
        refused(["python3", str(MANIFEST), "compare-package", "--original", str(original), "--served", str(rezipped)])
        with zipfile.ZipFile(changed, "w") as archive: archive.writestr("package.nuspec", b"changed"); archive.writestr("lib/value.dll", b"value")
        refused(["python3", str(MANIFEST), "compare-package", "--original", str(original), "--served", str(changed), "--allow-nuget-signature"])
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            with zipfile.ZipFile(duplicate, "w") as archive: archive.writestr("package.nuspec", b"same"); archive.writestr("package.nuspec", b"same")
        refused(["python3", str(MANIFEST), "compare-package", "--original", str(original), "--served", str(duplicate), "--allow-nuget-signature"])


def custody_checks(custody: Path) -> None:
    source = subprocess.check_output(["git", "-C", str(ROOT), "rev-parse", "HEAD"], text=True).strip()
    good = ["python3", str(MANIFEST), "verify", "--custody", str(custody), "--manifest", str(custody / "release-manifest.json"), "--source", source]
    subprocess.run(good, check=True)
    with tempfile.TemporaryDirectory(prefix="wasm-release-negative.") as raw:
        base = Path(raw)
        missing = base / "missing"; shutil.copytree(custody, missing)
        (missing / "FS.GG.Wasm.Browser.0.1.0.nupkg").unlink()
        refused([*good[:3], str(missing), "--manifest", str(missing / "release-manifest.json"), "--source", source])
        wrong = base / "wrong"; shutil.copytree(custody, wrong)
        manifest = json.loads((wrong / "release-manifest.json").read_text())
        manifest["version"] = "0.1.0-preview.1"
        (wrong / "release-manifest.json").write_text(json.dumps(manifest))
        refused([*good[:3], str(wrong), "--manifest", str(wrong / "release-manifest.json"), "--source", source])
        refused([*good[:-1], "0" * 40])
        changed = base / "changed"; shutil.copytree(custody, changed)
        with (changed / "fsgg-wasm-sdk-0.1.0.tar.gz").open("ab") as stream: stream.write(b"different")
        refused([*good[:3], str(changed), "--manifest", str(changed / "release-manifest.json"), "--source", source])
        foreign = base / "foreign"; shutil.copytree(custody, foreign)
        (foreign / "Foreign.0.1.0.nupkg").write_bytes(b"foreign")
        refused([*good[:3], str(foreign), "--manifest", str(foreign / "release-manifest.json"), "--source", source])


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-only", action="store_true")
    parser.add_argument("--custody", type=Path)
    args = parser.parse_args()
    source_checks()
    if args.custody:
        custody_checks(args.custody.resolve())
    print(f"wasm-release-preflight: source=pass custody={'pass' if args.custody else 'not-requested'} model=not-justified")


if __name__ == "__main__":
    main()
