#!/usr/bin/env python3
"""stage-skills.py — stage FS.GG.Game's owner-authored PRODUCT skill bytes into a
directory the package packs from (ADR-0062 / ADR-0063 / ADR-0014; FS.GG.Game#449, closing the
byte-SOURCE gap under FS.GG.SDD#622 / FS-GG/.github#1308).

WHAT THIS IS. FS.GG.Game owns a `scope: product` skill class (`owner: fs-gg-game`) whose bytes live at
`template/product-skills/<id>/SKILL.md`. Rendering's second copies and their classification are
retired (FS.GG.Game#540 / FS-GG/.github#1862), so this package publishes every product row,
content-addressed, for FS.GG.SDD#623's scaffold-time materializer to pin and verify.

This mirrors `.github`'s `src/FS.GG.Drivers/` substrate (ADR-0063: "reuse ADR-0062's substrate rather
than invent a second one"), one repo over. A product row added or retired in the manifest needs no
edit here.

DERIVED, NOT RESTATED (ADR-0058). The delivered set lives in exactly ONE authored place —
`template/skill-manifest/skill-manifest.json`, emitted by `scripts/generate-skill-manifest.fsx` from the
authored SKILL.md bodies. This stager reads that manifest and stages exactly its `scope: product`
rows; it restates no list of skill names.

WHAT IT STAGES, under <out-dir> (the package packs it under `game-skills/`):

  skill-manifest.json                 the manifest VERBATIM — the delivered set's authority + sha256s
  skills/<id>/<file>                 every closed file of each `scope: product` row

INTEGRITY AT STAGE TIME. Each staged SKILL.md's canonical digest strips a UTF-8 BOM and folds CRLF
to LF before SHA-256, matching generate-skill-manifest.fsx's `sha256Text` and the digest the SDD CLI
verifies at scaffold time (ADR-0014). It is re-checked against the manifest's recorded
`sha256`. A drift here is a build FAILURE, never a silently mis-staged byte.

  stage-skills.py <out-dir>

Exit: 0 staged; 2 on any misconfiguration (manifest missing/unparseable, a source SKILL.md missing, a
digest mismatch). Pure stdlib; no network, no tokens.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile

# Repo root = three levels up from this file (src/FS.GG.Game.Skills/stage-skills.py -> repo root).
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MANIFEST_PATH = os.path.join(REPO_ROOT, "template", "skill-manifest", "skill-manifest.json")
ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")


def die(msg: str) -> "None":
    sys.stderr.write(f"stage-skills: {msg}\n")
    raise SystemExit(2)


def canonical_digest(raw: bytes) -> str:
    """Hash BOM-free, CRLF-folded UTF-8 bytes, matching the F# manifest producer."""
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    return hashlib.sha256(raw.replace(b"\r\n", b"\n")).hexdigest()


def is_delivered(row: dict) -> bool:
    """The delivered class: every owner-authored product row."""
    return row.get("scope") == "product"


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        if key in result:
            die(f"skill manifest contains duplicate JSON key {key!r}")
        result[key] = value
    return result


def relative_file(value: object) -> str:
    if not isinstance(value, str) or not value or value.startswith("/") or "\\" in value:
        die(f"invalid manifest file path {value!r}")
    if any(part in ("", ".", "..") for part in value.split("/")) or any(ord(c) < 32 for c in value):
        die(f"invalid manifest file path {value!r}")
    return value


def source_files(source: Path, skill_id: str) -> dict[str, str]:
    actual = {}
    for path in source.rglob("*"):
        rel = path.relative_to(source).as_posix()
        mode = path.lstat().st_mode
        if stat.S_ISLNK(mode):
            die(f"product skill {skill_id}: source contains symlink {rel!r}")
        if stat.S_ISDIR(mode):
            continue
        if not stat.S_ISREG(mode):
            die(f"product skill {skill_id}: source contains non-regular file {rel!r}")
        actual[rel] = canonical_digest(path.read_bytes())
    return actual


def selected_skills(doc: dict) -> list[tuple[str, Path, dict[str, str]]]:
    skills = doc.get("skills")
    if not isinstance(skills, list):
        die("skill manifest has no 'skills' array — the generator's shape changed?")
    selected = []
    seen_ids = set()
    root = Path(REPO_ROOT)
    for row in skills:
        if not isinstance(row, dict):
            die("skill manifest row must be an object")
        if not is_delivered(row):
            continue
        skill_id = row.get("id")
        if not isinstance(skill_id, str) or not ID.fullmatch(skill_id):
            die(f"invalid product skill id {skill_id!r}")
        if skill_id.casefold() in seen_ids:
            die(f"duplicate product skill id {skill_id!r}")
        seen_ids.add(skill_id.casefold())
        supplied_by = f"template/product-skills/{skill_id}/"
        if row.get("supplied-by") != supplied_by:
            die(f"product skill {skill_id}: supplied-by does not match its source identity")
        source = root / "template" / "product-skills" / skill_id
        if any(path.is_symlink() for path in (root / "template", source.parent, source)) or not source.is_dir():
            die(f"product skill {skill_id}: source directory is missing or a symlink")
        files = row.get("files")
        if not isinstance(files, list) or not files:
            die(f"product skill {skill_id}: no closed files list")
        declared = {}
        folded = set()
        for item in files:
            if not isinstance(item, dict):
                die(f"product skill {skill_id}: file row must be an object")
            rel = relative_file(item.get("path"))
            if rel.casefold() in folded:
                die(f"product skill {skill_id}: duplicate or case-colliding path {rel!r}")
            folded.add(rel.casefold())
            sha = item.get("sha256")
            if not isinstance(sha, str) or not SHA256.fullmatch(sha):
                die(f"product skill {skill_id}: invalid file digest for {rel!r}")
            declared[rel] = sha
        if source_files(source, skill_id) != declared:
            die(f"product skill {skill_id}: source files do not match closed manifest")
        if declared.get("SKILL.md") != row.get("sha256"):
            die(f"product skill {skill_id}: body digest does not match closed files")
        selected.append((skill_id, source, declared))
    if not selected:
        die("no scope:product rows in the manifest — nothing to deliver "
            "(a truncated/empty manifest?).")
    return selected


def stage(out: Path, manifest_bytes: bytes, selected: list[tuple[str, Path, dict[str, str]]]) -> None:
    template = Path(REPO_ROOT) / "template"
    if (out == template or out in template.parents or template in out.parents
            or out in Path(__file__).resolve().parents):
        die("output overlaps repository source or manifest")
    out.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=f".{out.name}.stage-", dir=out.parent) as temporary:
        temporary = Path(temporary)
        staged = temporary / "payload"
        staged.mkdir()
        (staged / "skill-manifest.json").write_bytes(manifest_bytes)
        for skill_id, source, declared in selected:
            for rel, want_sha in declared.items():
                src = source / rel
                if src.is_symlink() or not src.is_file() or any(parent.is_symlink() for parent in src.parents if parent != source):
                    die(f"product skill {skill_id}: source changed during staging: {rel!r}")
                raw = src.read_bytes()
                if canonical_digest(raw) != want_sha:
                    die(f"product skill {skill_id}: source changed during staging: {rel!r}")
                dest = staged / "skills" / skill_id / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(raw)
                if canonical_digest(dest.read_bytes()) != want_sha:
                    die(f"product skill {skill_id}: staged bytes differ: {rel!r}")
        if Path(MANIFEST_PATH).read_bytes() != manifest_bytes:
            die("skill manifest changed during staging")
        backup = temporary / "previous"
        if out.exists():
            out.rename(backup)
        try:
            staged.rename(out)
        except OSError:
            if backup.exists():
                backup.rename(out)
            raise


def main(argv: list) -> int:
    if len(argv) != 1:
        die("usage: stage-skills.py <out-dir>")
    output_arg = Path(argv[0]).absolute()
    if output_arg.is_symlink():
        die("output must not be a symlink")
    out = output_arg.resolve()

    try:
        if any(Path(path).is_symlink() for path in
               (Path(REPO_ROOT) / "template", Path(MANIFEST_PATH).parent, Path(MANIFEST_PATH))):
            die("skill manifest path must not contain a symlink")
        with open(MANIFEST_PATH, "rb") as handle:
            manifest_bytes = handle.read()
    except OSError as exc:
        die(f"skill manifest not found at {MANIFEST_PATH}: {exc} (is this a FS.GG.Game checkout? "
            "run: dotnet fsi scripts/generate-skill-manifest.fsx)")
    try:
        doc = json.loads(
            manifest_bytes,
            object_pairs_hook=unique_object,
            parse_constant=lambda value: die(f"skill manifest contains invalid JSON constant {value}"),
        )
    except (json.JSONDecodeError, UnicodeError) as exc:
        die(f"skill manifest is not valid JSON: {exc}")
    if not isinstance(doc, dict) or doc.get("schemaVersion") != 2:
        die("skill manifest must use schemaVersion 2")
    selected = selected_skills(doc)
    stage(out, manifest_bytes, selected)
    sys.stdout.write(f"stage-skills: staged {len(selected)} product skill(s) + the manifest into {out}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
