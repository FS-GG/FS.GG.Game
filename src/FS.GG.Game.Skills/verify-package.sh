#!/usr/bin/env bash
# verify-package.sh — the FS.GG.Game.Skills gate (ADR-0062, ADR-0063, ADR-0014; FS.GG.Game#449). Meant
# to be run by CI and locally. Nothing executes a workflow, so the verification lives in a script the
# workflow CALLS — the same reason the .github kit/drivers/landable gates moved out of hand-copied YAML
# (#724). Mirrors .github's src/FS.GG.Drivers/verify-package.sh, one repo over.
#
# It proves the things the game-skills package must get right:
#   1. DERIVED, NOT RESTATED — the staged set is exactly template/skill-manifest/skill-manifest.json's
#      `mirrored: false` `scope: product` rows (ADR-0058); the packed manifest is byte-identical to the
#      committed one; and a `mirrored: true` row carries NO bytes (delivered via Rendering's mirror,
#      ADR-0022 §6 — not this package).
#   2. PACKS — `dotnet pack` produces a nupkg carrying the manifest, every delivered SKILL.md, and the
#      consumer handle (build/FS.GG.Game.Skills.props) + README.
#   3. CONTENT-ADDRESSED — every packed SKILL.md's canonical digest matches its manifest sha256
#      (the ADR-0014 record the SDD CLI verifies against at scaffold time).
#   4. FAILS LOUD — a tampered byte is DETECTED by that digest check, never silently delivered.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd "$HERE/../.." && pwd)"
MANIFEST="$SRC_ROOT/template/skill-manifest/skill-manifest.json"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
fail() { echo "verify-package: FAIL — $*" >&2; exit 1; }

[ -f "$MANIFEST" ] || fail "template/skill-manifest/skill-manifest.json not found (is this a FS.GG.Game checkout?)"

# canonical_digest: BOM-stripped body sha256, byte-parity with generate-skill-manifest.fsx / stage-skills.
# A tiny python helper keeps the digest correct even for a BOM'd body (sha256sum would not).
digest() { python3 - "$1" <<'PY'
import hashlib, sys
raw = open(sys.argv[1], "rb").read()
if raw.startswith(b"\xef\xbb\xbf"):
    raw = raw[3:]
print(hashlib.sha256(raw).hexdigest())
PY
}

# The delivered set the manifest declares: (id, sha256) for every `mirrored: false` `scope: product`
# row, and the ids of every `mirrored: true` `scope: product` row (which must carry NO bytes here).
# Parsed once, in python, from the ONE source.
mapfile -t DELIVERED_ROWS < <(python3 - "$MANIFEST" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1]))
for s in doc.get("skills", []):
    if s.get("scope") == "product" and s.get("mirrored") is False:
        print(f"{s['id']}\t{s['sha256']}")
PY
)
mapfile -t MIRRORED_IDS < <(python3 - "$MANIFEST" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1]))
for s in doc.get("skills", []):
    if s.get("scope") == "product" and s.get("mirrored") is True:
        print(s["id"])
PY
)
[ "${#DELIVERED_ROWS[@]}" -gt 0 ] || fail "manifest declares no mirrored:false product rows — nothing to deliver"

echo "== 1. stage + derive parity (mirrored:false rows staged & content-addressed; mirrored:true carries no bytes) =="
python3 "$HERE/stage-skills.py" "$WORK/stage" >/dev/null
# The manifest is carried VERBATIM.
diff -q "$MANIFEST" "$WORK/stage/skill-manifest.json" >/dev/null \
  || fail "staged skill-manifest.json is not byte-identical to template/skill-manifest/skill-manifest.json"
# Every delivered row: staged, and its bytes match the recorded sha256.
for row in "${DELIVERED_ROWS[@]}"; do
  id="${row%%$'\t'*}"; want="${row##*$'\t'}"
  f="$WORK/stage/skills/$id/SKILL.md"
  [ -f "$f" ] || fail "skill '$id' not staged (skills/$id/SKILL.md missing)"
  got="$(digest "$f")"
  [ "$got" = "$want" ] || fail "skill '$id' staged sha256 $got != manifest $want"
done
# Every mirrored:true row: NOT staged (delivered via Rendering's mirror, not this package).
for id in "${MIRRORED_IDS[@]}"; do
  [ -n "$id" ] || continue
  [ ! -e "$WORK/stage/skills/$id" ] || fail "mirrored skill '$id' was staged — it is delivered via Rendering's mirror, not here"
done
echo "   ${#DELIVERED_ROWS[@]} product skill(s) staged & content-addressed; ${#MIRRORED_IDS[@]} mirrored row(s) correctly withheld"

echo "== 2. pack + content assert =="
dotnet pack "$HERE/FS.GG.Game.Skills.csproj" -c Release -o "$WORK/out" >/dev/null
nupkg="$(echo "$WORK"/out/FS.GG.Game.Skills.*.nupkg)"
[ -f "$nupkg" ] || fail "no nupkg produced"
entries="$(unzip -Z1 "$nupkg")"
for want in "build/FS.GG.Game.Skills.props" "README.md" "game-skills/skill-manifest.json"; do
  grep -qx "$want" <<<"$entries" || fail "nupkg is missing $want"
done
for row in "${DELIVERED_ROWS[@]}"; do
  id="${row%%$'\t'*}"
  grep -qx "game-skills/skills/$id/SKILL.md" <<<"$entries" || fail "nupkg is missing game-skills/skills/$id/SKILL.md"
done
echo "   nupkg carries the manifest + every delivered SKILL.md + the consumer handle + README"

# content_addressed_ok <content-dir>: returns 0 iff every delivered SKILL.md under <content-dir>/skills/
# digests to its manifest sha256; non-zero (naming the first mismatch) otherwise. This IS the check the
# SDD CLI performs at scaffold time — the load-bearing content-addressed verify — so the gate both
# asserts it PASSES on the real package (step 3) and asserts it FIRES on a tampered byte (step 4).
# Asserting the digest merely "changed" would be tautological (any appended byte changes a sha256);
# asserting this function's VERDICT flips is what proves the verify.
content_addressed_ok() {
  local dir="$1" row id want got
  for row in "${DELIVERED_ROWS[@]}"; do
    id="${row%%$'\t'*}"; want="${row##*$'\t'}"
    got="$(digest "$dir/skills/$id/SKILL.md")" || return 1
    [ "$got" = "$want" ] || { echo "      content-address mismatch: $id sha256 $got != manifest $want" >&2; return 1; }
  done
  return 0
}

echo "== 3. content-addressed: every packed byte matches its manifest sha256 =="
unzip -q "$nupkg" "game-skills/*" -d "$WORK/unpacked"
# The packed manifest is byte-identical to the committed one.
diff -q "$MANIFEST" "$WORK/unpacked/game-skills/skill-manifest.json" >/dev/null \
  || fail "packed game-skills/skill-manifest.json is not byte-identical to the committed manifest"
content_addressed_ok "$WORK/unpacked/game-skills" \
  || fail "a packed SKILL.md does not match its manifest sha256 (the ADR-0014 record)"
echo "   every packed SKILL.md verifies against the manifest — the ADR-0014 record the CLI uses"

echo "== 4. a tampered byte is REJECTED by that same verify (fail-loud) =="
cp -r "$WORK/unpacked/game-skills" "$WORK/tampered"
first_id="${DELIVERED_ROWS[0]%%$'\t'*}"
echo "CORRUPT" >> "$WORK/tampered/skills/$first_id/SKILL.md"     # bytes drift from the recorded sha256
if content_addressed_ok "$WORK/tampered"; then
  fail "the content-addressed verify PASSED against a tampered '$first_id' — it is not firing"
fi
echo "   tampered skill '$first_id' rejected by the content-addressed verify, as required"

echo "verify-package: OK"
