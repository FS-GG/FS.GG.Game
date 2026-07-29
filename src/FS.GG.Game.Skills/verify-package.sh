#!/usr/bin/env bash
# verify-package.sh — the FS.GG.Game.Skills gate (ADR-0062, ADR-0063, ADR-0014; FS.GG.Game#449). Meant
# to be run by CI and locally. Nothing executes a workflow, so the verification lives in a script the
# workflow CALLS — the same reason the .github kit/drivers/landable gates moved out of hand-copied YAML
# (#724). Mirrors .github's src/FS.GG.Drivers/verify-package.sh, one repo over.
#
# It proves the things the game-skills package must get right:
#   1. DERIVED, NOT RESTATED — the staged set is exactly template/skill-manifest/skill-manifest.json's
#      `scope: product` rows (ADR-0058), and the packed manifest is byte-identical to the committed one.
#   2. NEW ROWS FLOW — a synthetic product row is staged without joining any hand-maintained set.
#   3. PACKS — `dotnet pack` produces a nupkg carrying the manifest, every delivered SKILL.md, and the
#      consumer handle (build/FS.GG.Game.Skills.props) + README.
#   4. CONTENT-ADDRESSED — every packed SKILL.md's canonical digest matches its manifest sha256
#      (the ADR-0014 record the SDD CLI verifies against at scaffold time).
#   5. FAILS LOUD — a tampered byte is DETECTED by that digest check, never silently delivered.
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

# The delivered set the manifest declares: (id, sha256) for every `scope: product` row.
# Parsed once, in Python, from the one source.
mapfile -t DELIVERED_ROWS < <(python3 - "$MANIFEST" <<'PY'
import json, sys
doc = json.load(open(sys.argv[1]))
for s in doc.get("skills", []):
    if s.get("scope") == "product":
        print(f"{s['id']}\t{s['sha256']}")
PY
)
[ "${#DELIVERED_ROWS[@]}" -gt 0 ] || fail "manifest declares no product rows — nothing to deliver"

echo "== 1. stage + derive parity (all product rows staged and content-addressed) =="
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
echo "   ${#DELIVERED_ROWS[@]} product skill(s) staged & content-addressed"

echo "== 2. a newly added product row is staged without joining another set =="
fixture="$WORK/new-row-repo"
mkdir -p "$fixture/src/FS.GG.Game.Skills" "$fixture/template/skill-manifest" \
         "$fixture/template/product-skills/fs-gg-new-row"
cp "$HERE/stage-skills.py" "$fixture/src/FS.GG.Game.Skills/stage-skills.py"
printf 'new row body\n' > "$fixture/template/product-skills/fs-gg-new-row/SKILL.md"
new_sha="$(digest "$fixture/template/product-skills/fs-gg-new-row/SKILL.md")"
python3 - "$fixture/template/skill-manifest/skill-manifest.json" "$new_sha" <<'PY'
import json, sys
doc = {"schemaVersion": 1, "skills": [{
    "id": "fs-gg-new-row",
    "scope": "product",
    "sha256": sys.argv[2],
    "resolvablePath": ".agents/skills/fs-gg-new-row/SKILL.md",
    "materializes-when": "profile in [game]",
    "supplied-by": "template/product-skills/fs-gg-new-row/",
}]}
with open(sys.argv[1], "w") as handle:
    json.dump(doc, handle)
PY
python3 "$fixture/src/FS.GG.Game.Skills/stage-skills.py" "$fixture/stage" >/dev/null
[ -f "$fixture/stage/skills/fs-gg-new-row/SKILL.md" ] \
  || fail "a new product row was not staged from the manifest"
echo "   synthetic product row flowed from manifest to staged bytes"

echo "== 3. pack + content assert =="
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

echo "== 4. content-addressed: every packed byte matches its manifest sha256 =="
unzip -q "$nupkg" "game-skills/*" -d "$WORK/unpacked"
# The packed manifest is byte-identical to the committed one.
diff -q "$MANIFEST" "$WORK/unpacked/game-skills/skill-manifest.json" >/dev/null \
  || fail "packed game-skills/skill-manifest.json is not byte-identical to the committed manifest"
content_addressed_ok "$WORK/unpacked/game-skills" \
  || fail "a packed SKILL.md does not match its manifest sha256 (the ADR-0014 record)"
echo "   every packed SKILL.md verifies against the manifest — the ADR-0014 record the CLI uses"

echo "== 5. a tampered byte is REJECTED by that same verify (fail-loud) =="
cp -r "$WORK/unpacked/game-skills" "$WORK/tampered"
first_id="${DELIVERED_ROWS[0]%%$'\t'*}"
echo "CORRUPT" >> "$WORK/tampered/skills/$first_id/SKILL.md"     # bytes drift from the recorded sha256
if content_addressed_ok "$WORK/tampered"; then
  fail "the content-addressed verify PASSED against a tampered '$first_id' — it is not firing"
fi
echo "   tampered skill '$first_id' rejected by the content-addressed verify, as required"

echo "verify-package: OK"
