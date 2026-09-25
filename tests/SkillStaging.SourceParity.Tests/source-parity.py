#!/usr/bin/env python3
"""Compare read-only #644 source projection with the #650 in-memory F# plan.

Fixture writes create only temporary source trees. This script never calls stage().
"""

from __future__ import annotations

import argparse
import base64
from contextlib import redirect_stderr
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile


REPO = Path(__file__).resolve().parents[2]
CANDIDATE_SHA256 = "ba060dff1a12cdff4c51c13e0dc478de0721d7b4193c1e19a4ec75852819ba44"
OBSERVER = REPO / "tests/SkillStaging.SourceParity.Observer/bin/Release/net10.0/SkillStaging.SourceParity.Observer.dll"


def digest(raw: bytes) -> str:
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    return hashlib.sha256(raw.replace(b"\r\n", b"\n")).hexdigest()


def candidate_module(path: Path):
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual != CANDIDATE_SHA256:
        raise AssertionError(f"#644 candidate byte hash changed: {actual}")
    spec = importlib.util.spec_from_file_location("game_stage_644", path)
    if spec is None or spec.loader is None:
        raise AssertionError("cannot load #644 candidate")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def python_projection(module, root: Path, manifest: Path) -> dict:
    """Run #644 selection and project its source bytes without calling stage()."""
    module.REPO_ROOT = str(root)
    module.MANIFEST_PATH = str(manifest)
    raw = manifest.read_bytes()
    try:
        with redirect_stderr(io.StringIO()):
            doc = json.loads(
                raw,
                object_pairs_hook=module.unique_object,
                parse_constant=lambda value: module.die(f"invalid JSON constant {value}"),
            )
            if not isinstance(doc, dict) or doc.get("schemaVersion") != 2:
                module.die("invalid schema")
            selected = module.selected_skills(doc)
            files = {"skill-manifest.json": base64.b64encode(raw).decode("ascii")}
            for skill_id, source, declared in selected:
                for rel, want in declared.items():
                    path = source / rel
                    if path.is_symlink() or not path.is_file() or any(
                        parent.is_symlink() for parent in path.parents if parent != source
                    ):
                        module.die("source changed before read-only projection")
                    content = path.read_bytes()
                    if module.canonical_digest(content) != want:
                        module.die("source digest changed before read-only projection")
                    files[f"skills/{skill_id}/{rel}"] = base64.b64encode(content).decode("ascii")
            if manifest.read_bytes() != raw:
                module.die("manifest changed before read-only projection")
            return {
                "accepted": True,
                "files": [
                    {"path": path, "bytesBase64": content}
                    for path, content in sorted(files.items())
                ],
            }
    except (SystemExit, json.JSONDecodeError, UnicodeError):
        return {"accepted": False}


def fsharp_projection(observer: Path, root: Path, manifest: Path) -> dict:
    result = subprocess.run(
        ["dotnet", str(observer), str(root), str(manifest)],
        check=True,
        capture_output=True,
        text=True,
    )
    return json.loads(result.stdout)


def fixture(root: Path, variant: str) -> Path:
    source = root / "template/product-skills/audio"
    source.mkdir(parents=True)
    body = b"\xef\xbb\xbf# audio\r\n" if variant == "bom-crlf-nested" else b"# audio\n"
    (source / "SKILL.md").write_bytes(body)
    file_rows = [{"path": "SKILL.md", "sha256": digest(body)}]
    if variant == "bom-crlf-nested":
        nested = source / "docs/guide.md"
        nested.parent.mkdir()
        nested.write_bytes(b"guide\r\n")
        file_rows.append({"path": "docs/guide.md", "sha256": digest(nested.read_bytes())})
    if variant == "declared-case-alias":
        file_rows.append({"path": "skill.MD", "sha256": digest(body)})
    if variant == "declared-unicode-alias":
        file_rows.extend(
            [{"path": name, "sha256": digest(body)} for name in ("Straße.txt", "STRASSE.txt")]
        )
    if variant == "traversal-path":
        file_rows.append({"path": "../SKILL.md", "sha256": digest(body)})
    if variant == "physical-case-alias":
        (source / "skill.MD").write_bytes(body)
    if variant == "empty-extra-directory":
        (source / "unused").mkdir()
    if variant == "empty-extra-root":
        (root / "template/product-skills/unused").mkdir()
    if variant == "symlink-source":
        foreign = root / "foreign.md"
        foreign.write_bytes(body)
        (source / "linked.md").symlink_to(foreign)

    row = {
        "id": "audio",
        "scope": "product",
        "supplied-by": "template/product-skills/audio/",
        "sha256": digest(body),
        "files": file_rows,
    }
    raw = json.dumps(
        {"schemaVersion": 2, "skills": [row]}, ensure_ascii=False, separators=(",", ":")
    ).encode("utf-8")
    if variant == "duplicate-json-key":
        raw = raw.replace(b'"schemaVersion":2,', b'"schemaVersion":2,"schemaVersion":2,', 1)
    manifest = root / "template/skill-manifest/skill-manifest.json"
    manifest.parent.mkdir()
    manifest.write_bytes(raw)
    return manifest


def compare(name: str, expected: str, module, observer: Path, root: Path, manifest: Path) -> None:
    python = python_projection(module, root, manifest)
    fsharp = fsharp_projection(observer, root, manifest)
    python_accepted = python["accepted"]
    fsharp_accepted = fsharp["accepted"]
    if expected == "exact":
        if not python_accepted or not fsharp_accepted or python["files"] != fsharp["files"]:
            raise AssertionError(f"{name}: exact source output differed: {python} != {fsharp}")
        print(f"PASS {name}: exact {len(python['files'])} paths and byte payloads")
    elif expected == "reject":
        if python_accepted or fsharp_accepted:
            raise AssertionError(f"{name}: expected both to refuse: {python} != {fsharp}")
        print(f"PASS {name}: both refused")
    elif expected == "known-divergence":
        if not python_accepted or fsharp_accepted:
            raise AssertionError(f"{name}: expected Python accept / F# refusal: {python} != {fsharp}")
        print(f"PASS {name}: known Python accept / F# refusal")
    else:
        raise AssertionError(f"unknown expectation {expected}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", required=True, type=Path, help="exact #644 stage-skills.py")
    parser.add_argument("--observer", type=Path, default=OBSERVER, help="built F# observer DLL")
    args = parser.parse_args()
    module = candidate_module(args.candidate.resolve())
    observer = args.observer.resolve()
    if not observer.is_file():
        raise AssertionError(f"build the Release observer first: {observer}")

    compare(
        "real 17-skill catalog", "exact", module, observer,
        REPO, REPO / "template/skill-manifest/skill-manifest.json",
    )
    cases = [
        ("bom-crlf-nested", "exact"),
        ("duplicate-json-key", "reject"),
        ("declared-case-alias", "reject"),
        ("declared-unicode-alias", "reject"),
        ("traversal-path", "reject"),
        ("physical-case-alias", "reject"),
        ("symlink-source", "reject"),
        ("empty-extra-directory", "known-divergence"),
        ("empty-extra-root", "known-divergence"),
    ]
    for name, expected in cases:
        with tempfile.TemporaryDirectory(prefix="game-source-parity-") as temporary:
            root = Path(temporary)
            manifest = fixture(root, name)
            compare(name, expected, module, observer, root, manifest)
    print(f"10/10 source-only parity corpus cases passed; candidate SHA-256 {CANDIDATE_SHA256}")


if __name__ == "__main__":
    main()
