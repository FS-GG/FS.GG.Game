#!/usr/bin/env python3
"""Source-only boundary controls for #644's manifest-selected product roots."""

from __future__ import annotations

from contextlib import redirect_stderr
import hashlib
import io
import json
from pathlib import Path
import tempfile
import types


REPO = Path(__file__).resolve().parents[2]
STAGER = REPO / "src/FS.GG.Game.Skills/stage-skills.py"


def load_candidate():
    # Compile directly to avoid importing a pyc into the owner source tree.
    module = types.ModuleType("game_stage_candidate")
    module.__file__ = str(STAGER)
    exec(compile(STAGER.read_bytes(), str(STAGER), "exec"), module.__dict__)
    return module


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw.removeprefix(b"\xef\xbb\xbf").replace(b"\r\n", b"\n")).hexdigest()


def projection(candidate, root: Path, manifest_bytes: bytes):
    candidate.REPO_ROOT = str(root)
    doc = json.loads(manifest_bytes, object_pairs_hook=candidate.unique_object)
    selected = candidate.selected_skills(doc)
    files = [("skill-manifest.json", manifest_bytes)]
    for skill_id, source, declared in selected:
        for relative, want in declared.items():
            content = (source / relative).read_bytes()
            assert candidate.canonical_digest(content) == want
            files.append((f"skills/{skill_id}/{relative}", content))
    return tuple(sorted(files))


def refused(action) -> None:
    with redirect_stderr(io.StringIO()):
        try:
            action()
        except SystemExit as error:
            assert error.code == 2, error.code
        else:
            raise AssertionError("selected source-root closure accepted an invalid source")


def main() -> None:
    candidate = load_candidate()
    with tempfile.TemporaryDirectory(prefix="game-undeclared-root-") as temporary:
        root = Path(temporary)
        selected_root = root / "template/product-skills/example"
        selected_root.mkdir(parents=True)
        body = b"# selected\n"
        selected_body = selected_root / "SKILL.md"
        selected_body.write_bytes(body)
        row = {
            "id": "example",
            "scope": "product",
            "supplied-by": "template/product-skills/example/",
            "sha256": digest(body),
            "files": [{"path": "SKILL.md", "sha256": digest(body)}],
        }
        manifest_bytes = json.dumps({"schemaVersion": 2, "skills": [row]}).encode("utf-8")
        baseline = projection(candidate, root, manifest_bytes)
        assert baseline == (
            ("skill-manifest.json", manifest_bytes),
            ("skills/example/SKILL.md", body),
        )
        print("PASS selected root projects exact declared paths and bytes")

        undeclared_root = root / "template/product-skills/orphan"
        undeclared_root.mkdir()
        orphan_body = b"# not selected\n"
        (undeclared_root / "SKILL.md").write_bytes(orphan_body)
        assert projection(candidate, root, manifest_bytes) == baseline
        print("PASS nonempty undeclared root leaves projected output unchanged")

        orphan_row = {
            "id": "orphan",
            "scope": "product",
            "supplied-by": "template/product-skills/orphan/",
            "sha256": digest(orphan_body),
            "files": [{"path": "SKILL.md", "sha256": digest(orphan_body)}],
        }
        two_row_manifest = json.dumps({"schemaVersion": 2, "skills": [row, orphan_row]}).encode("utf-8")
        assert projection(candidate, root, two_row_manifest) == (
            ("skill-manifest.json", two_row_manifest),
            ("skills/example/SKILL.md", body),
            ("skills/orphan/SKILL.md", orphan_body),
        )
        print("PASS manifest row selects the second root and its exact bytes")

        extra = selected_root / "EXTRA.txt"
        extra.write_bytes(b"unlisted")
        refused(lambda: projection(candidate, root, manifest_bytes))
        extra.unlink()
        print("PASS undeclared file inside selected root refuses")

        link = selected_root / "linked.md"
        link.symlink_to(undeclared_root / "SKILL.md")
        refused(lambda: projection(candidate, root, manifest_bytes))
        link.unlink()
        print("PASS link inside selected root refuses")

        selected_body.unlink()
        refused(lambda: projection(candidate, root, manifest_bytes))
        print("PASS missing declared file inside selected root refuses")

    print("6/6 source-only selected-root contract controls passed")


if __name__ == "__main__":
    main()
