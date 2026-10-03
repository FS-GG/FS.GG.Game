#!/usr/bin/env python3
from __future__ import annotations

import argparse
import sys
sys.dont_write_bytecode = True
import json
import os
import re
import shutil
import shlex
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
    import importlib.util
    spec = importlib.util.spec_from_file_location("promotion_controls", ROOT / "tests/release/wasm/test-release-promotion.py")
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    try: module.validate_workflow(text)
    except (AssertionError, KeyError, ValueError) as error: raise SystemExit("publisher source contract refused") from error


def validate_supervisor_backend(text: str, backend: str) -> None:
    commands = [shlex.split(line) for line in text.splitlines()
                if line.lstrip().startswith('"$quint" test ') or line.lstrip().startswith('"$quint" run ')]
    require([command[1] for command in commands] == ["test", "run"],
            "supervisor model test or sampled run is missing")
    require(backend == "typescript" and all(
        [token for token in command if token.startswith("--backend=")] == ["--backend=" + backend]
        for command in commands), "supervisor backend differs from the historical selected proof")


def supervisor_backend_checks() -> None:
    text = (ROOT / "scripts/verify-wasm-supervisor.sh").read_text()
    proof = json.loads((ROOT / "tests/Wasm.Supervisor.Compatibility/reviewed-source-proof.json").read_text())
    backend = proof["model"]["backend"]
    validate_supervisor_backend(text, backend)
    for action in ("test", "run"):
        original = '"$quint" ' + action + ' --backend=typescript '
        for replacement in ('"$quint" ' + action + ' ', '"$quint" ' + action + ' --backend=rust '):
            mutant = text.replace(original, replacement, 1)
            require(mutant != text, "backend mutation did not reach the production command")
            try:
                validate_supervisor_backend(mutant, backend)
            except SystemExit:
                pass
            else:
                raise SystemExit("supervisor backend omission or replacement was accepted")


def browser_setup_checks() -> None:
    """Exercise the production setup and prepare guard without downloading browsers."""
    with tempfile.TemporaryDirectory(prefix="wasm-source-clean-control.") as raw:
        base = Path(raw)
        source = base / "source"
        source.mkdir()
        paths = ["scripts/wasm-release/install-browser-tools.sh", "scripts/wasm-release/prepare.sh",
                 "tests/Wasm.PackageConsumer/browser/package.json", "tests/Wasm.PackageConsumer/browser/package-lock.json",
                 "eng/wasm-shared/version.props", "sdk/wasm/VERSION"]
        for name in paths:
            target = source / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, target)
        preflight = source / "tests/release/wasm/test-release-wasm.sh"
        preflight.parent.mkdir(parents=True)
        preflight.write_text("#!/bin/sh\nexit 0\n")
        preflight.chmod(0o755)
        subprocess.run(["git", "init", "-q", str(source)], check=True)
        subprocess.run(["git", "-C", str(source), "add", "."], check=True)
        subprocess.run(["git", "-C", str(source), "-c", "user.name=Control", "-c", "user.email=control@example.invalid", "commit", "-qm", "fixture"], check=True)
        tools = base / "tools"
        tools.mkdir()
        npm = tools / "npm"
        npm.write_text("#!/bin/sh\nset -eu\n[ \"$1\" = ci ]\ncmp package.json \"$EXPECTED_FIXTURE/package.json\"\ncmp package-lock.json \"$EXPECTED_FIXTURE/package-lock.json\"\nmkdir -p node_modules/.bin\nprintf '#!/bin/sh\\n[ \"$*\" = \"install --with-deps chromium\" ]\\n' > node_modules/.bin/playwright\nchmod +x node_modules/.bin/playwright\n")
        npm.chmod(0o755)
        dotnet = tools / "dotnet"
        dotnet.write_text("#!/bin/sh\necho qualification-tool-sentinel >&2\nexit 17\n")
        dotnet.chmod(0o755)
        runner = base / "runner"
        runner.mkdir()
        env = dict(os.environ, PATH=f"{tools}:{os.environ['PATH']}", RUNNER_TEMP=str(runner),
                   EXPECTED_FIXTURE=str(source / "tests/Wasm.PackageConsumer/browser"))
        setup = str(source / "scripts/wasm-release/install-browser-tools.sh")
        subprocess.run([setup], env=env, check=True)
        status = subprocess.check_output(["git", "-C", str(source), "status", "--porcelain"], text=True)
        require(not status and not list(runner.iterdir()), "browser setup dirtied source or leaked dependency root")
        prepare = [str(source / "scripts/wasm-release/prepare.sh"), str(base / "custody")]
        clean = subprocess.run(prepare, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        require("qualification-tool-sentinel" in clean.stdout and "clean exact source" not in clean.stdout,
                "clean setup did not pass the production source guard")
        shutil.rmtree(base / "custody")
        tracked = source / "sdk/wasm/VERSION"
        tracked.write_text(tracked.read_text() + "tracked-change\n")
        dirty = subprocess.run(prepare, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        require(dirty.returncode == 2 and "clean exact source checkout" in dirty.stdout and not (base / "custody").exists(),
                "tracked source edit passed the production clean guard")
        inside = subprocess.run([setup], env=dict(env, RUNNER_TEMP=str(source)), text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        require(inside.returncode == 2 and "inside source checkout" in inside.stdout,
                "browser setup accepted a dependency root inside source")


def source_checks() -> None:
    workflow = (ROOT / ".github/workflows/release-wasm.yml").read_text()
    core = (ROOT / ".github/workflows/release.yml").read_text()
    props = (ROOT / "eng/wasm-shared/version.props").read_text()
    require("<WasmSharedVersion>0.3.0</WasmSharedVersion>" in props, "stable version origin mismatch")
    for path in (ROOT / "src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj", ROOT / "src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj"):
        require("$(WasmSharedVersion)" in path.read_text(), f"{path} bypasses shared version origin")
    require((ROOT / "sdk/wasm/VERSION").read_text().strip() == "0.3.0", "SDK version mismatch")
    require('"sourceVersion": "0.3.0"' in (ROOT / "src/Wasm.Contracts/compatibility-profile.v1.json").read_text(), "profile version mismatch")
    require('Version="[$(WasmSharedVersion)]"' in (ROOT / "src/Wasm.Browser/Fable/FS.GG.Wasm.Browser.fsproj").read_text(), "Fable dependency is not exact")
    require('Version="[0.2.0]"' in (ROOT / "tests/Wasm.PackageConsumer/Consumer.fsproj").read_text(), "consumer dependency is not exact")
    cargo_files = list((ROOT / "sdk/wasm/rust").rglob("Cargo.toml")) + list((ROOT / "sdk/wasm/rust").rglob("Cargo.lock")) + list((ROOT / "examples/wasm/rust").rglob("Cargo.toml")) + list((ROOT / "examples/wasm/rust").rglob("Cargo.lock"))
    require(all("0.2.0-source" not in path.read_text() for path in cargo_files), "Cargo metadata retains a prerelease identity")
    cargo_manifests = [path for path in cargo_files if path.name == "Cargo.toml" and "[package]" in path.read_text()]
    require(all('version = "0.3.0"' in path.read_text() for path in cargo_manifests), "Cargo package version mismatch")
    validate_workflow(workflow)
    supervisor_backend_checks()
    subprocess.run(["python3", str(ROOT / "tests/release/wasm/test-quint-evaluator-preparation.py")], check=True)
    browser_setup_checks()
    subprocess.run(["python3", str(ROOT / "tests/release/wasm/test-release-promotion.py")], check=True)
    require("NuGet/login@8d196754b4036150537f80ac539e15c2f1028841" in workflow, "public push lacks pinned OIDC login")
    require("secrets.NUGET_API_KEY" not in workflow, "long-lived public API key fallback is forbidden")
    require(core.count("github.event_name != 'release' || startsWith(github.event.release.tag_name, 'v')") == 2, "core release namespace guard missing")
    consumer = (ROOT / "scripts/verify-wasm-package-consumer.sh").read_text()
    require("--feed-only" in consumer, "feed-only consumer route missing")
    subprocess.run(["python3", str(ROOT / "tests/release/wasm/test-consumer-source-mapping.py")], check=True)
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
    version = json.loads((custody / "release-manifest.json").read_text())["version"]
    good = ["python3", str(MANIFEST), "verify", "--custody", str(custody), "--manifest", str(custody / "release-manifest.json"), "--source", source]
    subprocess.run(good, check=True)
    with tempfile.TemporaryDirectory(prefix="wasm-release-negative.") as raw:
        base = Path(raw)
        missing = base / "missing"; shutil.copytree(custody, missing)
        (missing / f"FS.GG.Wasm.Browser.{version}.nupkg").unlink()
        refused([*good[:3], str(missing), "--manifest", str(missing / "release-manifest.json"), "--source", source])
        wrong = base / "wrong"; shutil.copytree(custody, wrong)
        manifest = json.loads((wrong / "release-manifest.json").read_text())
        manifest["version"] = version + "-preview.1"
        (wrong / "release-manifest.json").write_text(json.dumps(manifest))
        refused([*good[:3], str(wrong), "--manifest", str(wrong / "release-manifest.json"), "--source", source])
        refused([*good[:-1], "0" * 40])
        changed = base / "changed"; shutil.copytree(custody, changed)
        with (changed / f"fsgg-wasm-sdk-{version}.tar.gz").open("ab") as stream: stream.write(b"different")
        refused([*good[:3], str(changed), "--manifest", str(changed / "release-manifest.json"), "--source", source])
        foreign = base / "foreign"; shutil.copytree(custody, foreign)
        (foreign / "Foreign.0.2.0.nupkg").write_bytes(b"foreign")
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
