#!/usr/bin/env python3
"""Disposable #644 stage transaction versus the F# read-only source plan.

All stage outputs are below TemporaryDirectory. This script never invokes the
production receiver, package build, or live gate.
"""

from __future__ import annotations

import argparse
import base64
from contextlib import redirect_stderr
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
from unittest.mock import patch
import types


REPO = Path(__file__).resolve().parents[2]
CANDIDATE_SHA256 = "ba060dff1a12cdff4c51c13e0dc478de0721d7b4193c1e19a4ec75852819ba44"
OBSERVER = REPO / "tests/SkillStaging.SourceParity.Observer/bin/Release/net10.0/SkillStaging.SourceParity.Observer.dll"


def check(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def digest(raw: bytes) -> str:
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    return hashlib.sha256(raw.replace(b"\r\n", b"\n")).hexdigest()


def load_candidate(path: Path):
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    check(actual == CANDIDATE_SHA256, f"#644 candidate hash changed: {actual}")
    module = types.ModuleType("stage_skills_644_transaction")
    module.__file__ = str(path)
    exec(compile(path.read_bytes(), str(path), "exec"), module.__dict__)
    return module


def prepare(module, root: Path, manifest: Path):
    module.REPO_ROOT = str(root)
    module.MANIFEST_PATH = str(manifest)
    raw = manifest.read_bytes()
    doc = json.loads(raw, object_pairs_hook=module.unique_object)
    check(doc.get("schemaVersion") == 2, "fixture schema must be 2")
    return raw, module.selected_skills(doc)


def plan(observer: Path, root: Path, manifest: Path) -> dict[str, bytes]:
    result = subprocess.run(
        ["dotnet", str(observer), str(root), str(manifest)],
        capture_output=True, text=True, check=True,
    )
    doc = json.loads(result.stdout)
    check(doc.get("accepted") is True, f"F# plan refused: {doc}")
    return {entry["path"]: base64.b64decode(entry["bytesBase64"])
            for entry in doc["files"]}


def snapshot(root: Path) -> dict[str, tuple]:
    """Record paths, types, modes, owner, inode, and file bytes under a receiver."""
    if not root.exists():
        return {}
    result = {}
    for path in [root, *sorted(root.rglob("*"))]:
        rel = "." if path == root else path.relative_to(root).as_posix()
        info = path.lstat()
        kind = "directory" if stat.S_ISDIR(info.st_mode) else "file" if stat.S_ISREG(info.st_mode) else "other"
        payload = path.read_bytes() if kind == "file" else None
        result[rel] = (kind, stat.S_IMODE(info.st_mode), info.st_uid, info.st_gid,
                       info.st_ino, payload)
    return result


def assert_staged(out: Path, expected: dict[str, bytes], title: str) -> None:
    actual = snapshot(out)
    check(set(actual) == {".", *expected, *{
        str(Path(rel).parent) for rel in expected if Path(rel).parent != Path(".")
    }, *{
        str(parent) for rel in expected for parent in Path(rel).parents
        if parent != Path(".")
    }}, f"{title}: receiver paths differ: {sorted(actual)}")
    check({key: row[-1] for key, row in actual.items() if row[0] == "file"} == expected,
          f"{title}: receiver byte payload differs")
    for rel, (kind, mode, uid, gid, _, _) in actual.items():
        check(kind in ("file", "directory"), f"{title}: nonregular receiver path {rel}")
        check(mode == (0o644 if kind == "file" else 0o755),
              f"{title}: mode {rel} {mode:o}")
        check((uid, gid) == (os.geteuid(), os.getegid()),
              f"{title}: owner {rel} {uid}:{gid}")
    print(f"PASS {title}: {len(expected)} exact files, modes 0644/0755, current uid/gid")


def fixture(root: Path) -> tuple[Path, Path]:
    source = root / "template/product-skills/audio"
    source.mkdir(parents=True)
    body = b"\xef\xbb\xbf# audio\r\n"
    guide = b"guide\r\n"
    skill = source / "SKILL.md"
    nested = source / "docs/guide.md"
    nested.parent.mkdir()
    skill.write_bytes(body)
    nested.write_bytes(guide)
    skill.chmod(0o600)
    nested.chmod(0o640)
    manifest = root / "template/skill-manifest/skill-manifest.json"
    manifest.parent.mkdir()
    manifest.write_bytes(json.dumps({"schemaVersion": 2, "skills": [{
        "id": "audio", "scope": "product",
        "supplied-by": "template/product-skills/audio/",
        "sha256": digest(body),
        "files": [{"path": "SKILL.md", "sha256": digest(body)},
                  {"path": "docs/guide.md", "sha256": digest(guide)}],
    }]}, separators=(",", ":")).encode())
    return manifest, source


def assert_no_stage_debris(out: Path, title: str) -> None:
    check(not list(out.parent.glob(f".{out.name}.stage-*")),
          f"{title}: temporary payload or previous receiver remains")


def assert_refused(stage_call, out: Path, title: str, expected_exception=SystemExit) -> None:
    before = snapshot(out)
    try:
        with redirect_stderr(io.StringIO()):
            stage_call()
    except expected_exception as error:
        if isinstance(error, SystemExit):
            check(error.code == 2, f"{title}: exit code {error.code}")
    else:
        raise AssertionError(f"{title}: staging unexpectedly succeeded")
    check(snapshot(out) == before, f"{title}: old receiver changed")
    assert_no_stage_debris(out, title)
    print(f"PASS {title}: prior receiver intact, no staging debris")


def check_real_catalog(module, observer: Path) -> None:
    manifest = REPO / "template/skill-manifest/skill-manifest.json"
    expected = plan(observer, REPO, manifest)
    raw, selected = prepare(module, REPO, manifest)
    check(len(selected) == 17, f"real catalog selection has {len(selected)} product rows")
    with tempfile.TemporaryDirectory(prefix="game-stage-real-") as temporary:
        out = Path(temporary) / "receiver"
        module.stage(out, raw, selected)
        assert_staged(out, expected, "real 17-skill catalog")
        assert_no_stage_debris(out, "real catalog")


def check_fixture(module, observer: Path) -> None:
    with tempfile.TemporaryDirectory(prefix="game-stage-fixture-") as temporary:
        root = Path(temporary) / "fixture"
        manifest, source = fixture(root)
        expected = plan(observer, root, manifest)
        raw, selected = prepare(module, root, manifest)
        out = Path(temporary) / "receiver"
        out.mkdir()
        (out / "obsolete.txt").write_bytes(b"old receiver")
        module.stage(out, raw, selected)
        assert_staged(out, expected, "BOM/CRLF/nested replacement")
        check((source / "SKILL.md").stat().st_mode & 0o777 == 0o600,
              "source mode control changed")
        check((source / "docs/guide.md").stat().st_mode & 0o777 == 0o640,
              "nested source mode control changed")
        assert_no_stage_debris(out, "fixture replacement")


def check_refusals(module) -> None:
    for title in ("changed file", "removed file", "symlink file", "changed manifest",
                  "commit rename failure", "extra selected-root file"):
        with tempfile.TemporaryDirectory(prefix="game-stage-refusal-") as temporary:
            root = Path(temporary) / "fixture"
            manifest, source = fixture(root)
            raw, selected = prepare(module, root, manifest)
            out = Path(temporary) / "receiver"
            out.mkdir()
            (out / "sentinel.txt").write_bytes(b"prior receiver")
            if title == "changed file":
                (source / "SKILL.md").write_bytes(b"changed")
                assert_refused(lambda: module.stage(out, raw, selected), out, title)
            elif title == "removed file":
                (source / "SKILL.md").unlink()
                assert_refused(lambda: module.stage(out, raw, selected), out, title)
            elif title == "symlink file":
                (source / "SKILL.md").unlink()
                (root / "foreign.md").write_bytes(b"\xef\xbb\xbf# audio\r\n")
                (source / "SKILL.md").symlink_to(root / "foreign.md")
                assert_refused(lambda: module.stage(out, raw, selected), out, title)
            elif title == "changed manifest":
                manifest.write_bytes(b"changed")
                assert_refused(lambda: module.stage(out, raw, selected), out, title)
            elif title == "commit rename failure":
                original_rename = Path.rename

                def fail_payload_rename(path, target):
                    if path.name == "payload" and Path(target) == out:
                        raise OSError("injected payload rename failure")
                    return original_rename(path, target)

                with patch.object(Path, "rename", fail_payload_rename):
                    assert_refused(lambda: module.stage(out, raw, selected), out,
                                   title, OSError)
            elif title == "extra selected-root file":
                (source / "undeclared.txt").write_bytes(b"extra")
                assert_refused(lambda: prepare(module, root, manifest), out, title)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--observer", type=Path, default=OBSERVER)
    args = parser.parse_args()
    observer = args.observer.resolve()
    check(observer.is_file(), f"build the Release observer first: {observer}")
    module = load_candidate(args.candidate.resolve())
    old_umask = os.umask(0o022)
    try:
        check_real_catalog(module, observer)
        check_fixture(module, observer)
        check_refusals(module)
    finally:
        os.umask(old_umask)
    print(f"8/8 disposable transaction controls passed; #644 SHA-256 {CANDIDATE_SHA256}")


if __name__ == "__main__":
    main()
