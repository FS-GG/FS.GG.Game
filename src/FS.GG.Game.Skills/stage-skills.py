#!/usr/bin/env python3
"""stage-skills.py — stage FS.GG.Game's owner-authored `mirrored: false` PRODUCT skill bytes into a
directory the package packs from (ADR-0062 / ADR-0063 / ADR-0014; FS.GG.Game#449, closing the
byte-SOURCE gap under FS.GG.SDD#622 / FS-GG/.github#1308).

WHAT THIS IS. FS.GG.Game owns a `scope: product` skill class (`owner: fs-gg-game`) whose bytes live at
`template/product-skills/<id>/SKILL.md`. Those rows split two ways (`mirrored`, in the manifest):

  mirrored: true    reaches a scaffold via FS.GG.Rendering's frozen `--profile game` mirror — NOT this
                    package's job (ADR-0022 §6). Carried in the manifest, delivered nowhere HERE.
  mirrored: false   has NO mirror. These are the bytes this package publishes, content-addressed, so
                    FS.GG.SDD#623's scaffold-time materializer can pin and verify them.

This mirrors `.github`'s `src/FS.GG.Drivers/` substrate (ADR-0063: "reuse ADR-0062's substrate rather
than invent a second one"), one repo over: the delivered subclass here is `mirrored: false` rather than
`scope: driver`, and the withheld-but-listed subclass is `mirrored: true` rather than `scope: operator`
(ADR-0057). A `mirrored: false` product row added or retired in the manifest needs NO edit here.

DERIVED, NOT RESTATED (ADR-0058). The delivered set lives in exactly ONE authored place —
`template/skill-manifest/skill-manifest.json`, emitted by `scripts/generate-skill-manifest.fsx` from the
authored SKILL.md bodies. This stager reads that manifest and stages exactly its `mirrored: false`
`scope: product` rows; it restates no list of skill names.

WHAT IT STAGES, under <out-dir> (the package packs it under `game-skills/`):

  skill-manifest.json                 the manifest VERBATIM — the delivered set's authority + sha256s
  skills/<id>/SKILL.md                one per `mirrored: false` `scope: product` row (id = the row's id)

INTEGRITY AT STAGE TIME. Each staged SKILL.md's canonical digest (BOM-stripped body sha256 — byte-parity
with generate-skill-manifest.fsx's `Encoding.UTF8.GetBytes(File.ReadAllText …)`, the exact digest the
SDD CLI verifies against at scaffold time, ADR-0014) is re-checked against the manifest's recorded
`sha256`. A drift here is a build FAILURE, never a silently mis-staged byte.

  stage-skills.py <out-dir>

Exit: 0 staged; 2 on any misconfiguration (manifest missing/unparseable, a source SKILL.md missing, a
digest mismatch). Pure stdlib; no network, no tokens.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import sys

# Repo root = three levels up from this file (src/FS.GG.Game.Skills/stage-skills.py -> repo root).
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MANIFEST_PATH = os.path.join(REPO_ROOT, "template", "skill-manifest", "skill-manifest.json")


def die(msg: str) -> "None":
    sys.stderr.write(f"stage-skills: {msg}\n")
    raise SystemExit(2)


def canonical_digest(raw: bytes) -> str:
    """sha256 over the body text's UTF-8 bytes, BOM-free — byte-parity with generate-skill-manifest."""
    if raw.startswith(b"\xef\xbb\xbf"):
        raw = raw[3:]
    return hashlib.sha256(raw).hexdigest()


def is_delivered(row: dict) -> bool:
    """The delivered subclass: an owner:fs-gg-game product row with NO mirror (ADR-0022 §6). A
    `mirrored: true` row is carried in the manifest but delivered NOWHERE here — it reaches a scaffold
    through FS.GG.Rendering's mirror instead."""
    return row.get("scope") == "product" and row.get("mirrored") is False


def main(argv: list) -> int:
    if len(argv) != 1:
        die("usage: stage-skills.py <out-dir>")
    out = argv[0]

    try:
        with open(MANIFEST_PATH, "rb") as handle:
            manifest_bytes = handle.read()
    except OSError as exc:
        die(f"skill manifest not found at {MANIFEST_PATH}: {exc} (is this a FS.GG.Game checkout? "
            "run: dotnet fsi scripts/generate-skill-manifest.fsx)")
    try:
        doc = json.loads(manifest_bytes)
    except json.JSONDecodeError as exc:
        die(f"skill manifest is not valid JSON: {exc}")

    skills = doc.get("skills")
    if not isinstance(skills, list):
        die("skill manifest has no 'skills' array — the generator's shape changed?")

    # Fresh staging every run — a skill a prior manifest named and this one does not must never linger
    # (the staleness a byte-copy fabric suffers, kept out of the producer).
    shutil.rmtree(out, ignore_errors=True)
    os.makedirs(out)

    # The manifest itself is the delivered set's authority + integrity record — carry it VERBATIM.
    with open(os.path.join(out, "skill-manifest.json"), "wb") as handle:
        handle.write(manifest_bytes)

    staged = 0
    for row in skills:
        if not is_delivered(row):
            continue  # mirrored:true (delivered via Rendering's mirror) or any non-product row.
        skill_id = row.get("id")
        supplied_by = row.get("supplied-by")
        want_sha = row.get("sha256")
        if not skill_id or not supplied_by or not want_sha:
            die(f"product row is missing id/supplied-by/sha256: {row!r}")

        src = os.path.join(REPO_ROOT, supplied_by, "SKILL.md")
        try:
            with open(src, "rb") as handle:
                raw = handle.read()
        except OSError as exc:
            die(f"product skill source missing: {supplied_by}SKILL.md ({exc})")

        got_sha = canonical_digest(raw)
        if got_sha != want_sha:
            die(f"product skill {skill_id}: staged bytes sha256 {got_sha} != manifest {want_sha} — "
                "the manifest is stale (run dotnet fsi scripts/generate-skill-manifest.fsx and commit).")

        dest_dir = os.path.join(out, "skills", skill_id)
        os.makedirs(dest_dir, exist_ok=True)
        with open(os.path.join(dest_dir, "SKILL.md"), "wb") as handle:
            handle.write(raw)
        staged += 1

    if staged == 0:
        die("no mirrored:false scope:product rows in the manifest — nothing to deliver "
            "(a truncated/empty manifest?).")

    sys.stdout.write(f"stage-skills: staged {staged} product skill(s) + the manifest into {out}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
