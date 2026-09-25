#!/usr/bin/env python3
"""Independent black-box closure and rollback controls for the Game skill stager."""

import copy
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

STAGER = Path(__file__).resolve().parents[2] / "src/FS.GG.Game.Skills/stage-skills.py"


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw.removeprefix(b"\xef\xbb\xbf").replace(b"\r\n", b"\n")).hexdigest()


with tempfile.TemporaryDirectory() as temporary:
    root = Path(temporary)
    script = root / "src/FS.GG.Game.Skills/stage-skills.py"
    script.parent.mkdir(parents=True)
    shutil.copyfile(STAGER, script)
    source = root / "template/product-skills/example"
    (source / "nested").mkdir(parents=True)
    body = source / "SKILL.md"
    extra = source / "nested/EXTRA.txt"
    raw_body = b"\xef\xbb\xbf# Example\r\n"
    raw_extra = b"second file\r\n"
    body.write_bytes(raw_body)
    extra.write_bytes(raw_extra)
    row = {
        "id": "example",
        "scope": "product",
        "sha256": digest(raw_body),
        "supplied-by": "template/product-skills/example/",
        "files": [
            {"path": "SKILL.md", "sha256": digest(raw_body)},
            {"path": "nested/EXTRA.txt", "sha256": digest(raw_extra)},
        ],
    }
    manifest = root / "template/skill-manifest/skill-manifest.json"
    manifest.parent.mkdir()
    out = root / "stage"

    def run(expected: int, rows=None, manifest_text=None) -> str:
        if manifest_text is None:
            manifest_text = json.dumps({"schemaVersion": 2, "skills": rows if rows is not None else [row]})
        manifest.write_text(manifest_text, encoding="utf-8")
        result = subprocess.run([sys.executable, str(script), str(out)], capture_output=True, text=True)
        assert result.returncode == expected, (result.returncode, result.stdout, result.stderr)
        return result.stderr

    run(0)
    assert (out / "skills/example/SKILL.md").read_bytes() == raw_body
    assert (out / "skills/example/nested/EXTRA.txt").read_bytes() == raw_extra
    sentinel = out / "previous.txt"
    sentinel.write_text("previous stage", encoding="utf-8")
    previous_manifest = (out / "skill-manifest.json").read_bytes()

    def refused(rows=None, manifest_text=None) -> None:
        run(2, rows, manifest_text)
        assert sentinel.read_text(encoding="utf-8") == "previous stage"
        assert (out / "skill-manifest.json").read_bytes() == previous_manifest

    extra.unlink()
    refused()  # Manifest-declared file missing from source must not stage a partial package.
    extra.write_bytes(raw_extra)

    undeclared = source / "unlisted.txt"
    undeclared.write_text("extra", encoding="utf-8")
    refused()
    undeclared.unlink()

    link = source / "nested/escape"
    link.symlink_to(root, target_is_directory=True)
    refused()
    link.unlink()

    pipe = source / "nested/pipe"
    os.mkfifo(pipe)
    refused()
    pipe.unlink()

    bad = copy.deepcopy(row)
    bad["id"] = "../../escaped"
    refused([bad])
    assert not (root / "escaped/SKILL.md").exists()

    bad = copy.deepcopy(row)
    bad["supplied-by"] = "../outside/"
    refused([bad])

    bad = copy.deepcopy(row)
    bad["files"].append({"path": "../escape", "sha256": digest(raw_body)})
    refused([bad])

    bad = copy.deepcopy(row)
    bad["sha256"] = "0" * 64
    refused([bad])

    refused([row, {**row, "id": "EXAMPLE"}])
    refused(manifest_text='{"schemaVersion":2,"skills":[],"skills":' + json.dumps([row]) + "}")
    refused(manifest_text='{"schemaVersion":2,"skills":' + json.dumps([row]) + ',"unused":NaN}')
    refused(manifest_text="[]")

    manifest.write_text(json.dumps({"schemaVersion": 2, "skills": [row]}), encoding="utf-8")
    result = subprocess.run([sys.executable, str(script), str(source)], capture_output=True, text=True)
    assert result.returncode == 2
    assert body.read_bytes() == raw_body and extra.read_bytes() == raw_extra

print("Game skill staging closure and rollback controls passed")
