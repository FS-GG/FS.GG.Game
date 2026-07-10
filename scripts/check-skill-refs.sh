#!/usr/bin/env bash
# check-skill-refs — fail on a dangling `[[wiki-ref]]` in this repo's published product skills.
#
# WHY (FS.GG.Game#35). A skill body that writes `[[fs-gg-scene]]` promises the reader a skill this
# repo publishes. It does not: `fs-gg-scene` is owned by `fs-gg-rendering`. An agent following the
# pointer in a scaffolded product finds nothing. Before this check there were 21 such refs across
# all 5 published skills, and every newly authored skill inherited the defect (fs-gg-ballistics
# shipped two more).
#
# THE CONVENTION. A `[[ref]]` is either
#   * BARE      — `[[fs-gg-game-core]]`            → MUST name a skill this repo publishes, or
#   * QUALIFIED — `[[fs-gg-rendering:fs-gg-scene]]` → `<owner>:<skill-id>`, naming the publishing
#                 repo's registry `owner` id (registry/skills.yml in FS-GG/.github).
# Anything else is a dangling ref and fails. Bare code spans (`fs-gg-scene`) are prose, not
# pointers, and are deliberately NOT checked — only `[[...]]` promises resolvability.
#
# The owner vocabulary is the registry's: a qualified ref whose owner is unknown is a typo, and a
# ref qualified with THIS repo's own owner must resolve locally (write it bare instead).
#
# Usage: scripts/check-skill-refs.sh          # exit 1 and name every offending file:line
set -euo pipefail

cd "$(dirname "$0")/.."

SKILL_ROOT="template/product-skills"
SELF_OWNER="fs-gg-game"
# registry/skills.yml `owner:` vocabulary (FS-GG/.github), one per line.
KNOWN_OWNERS=$'fs-gg-game\nfs-gg-rendering\nfs-gg-sdd'

if [[ ! -d $SKILL_ROOT ]]; then
  echo "check-skill-refs: no $SKILL_ROOT — nothing to check." >&2
  exit 0
fi

# The skills THIS repo publishes: one directory per skill id.
published=$(for d in "$SKILL_ROOT"/*/; do basename "$d"; done | sort)

# -x -F, never -w: `grep -w game` matches inside `fs-gg-game` (a `-` is a word boundary), and an
# unanchored pattern is a REGEX, so `fs.gg.game` would match too. Both would wave a typo'd owner
# through — and a foreign qualified ref is trusted, so nothing downstream would catch it.
is_published() { grep -qxF -- "$1" <<<"$published"; }
is_known_owner() { grep -qxF -- "$1" <<<"$KNOWN_OWNERS"; }

fail=0
report() {
  printf '%s:%s: dangling [[%s]] — %s\n' "$1" "$2" "$3" "$4" >&2
  # A GitHub Actions annotation when running in CI; harmless locally.
  [[ -n ${GITHUB_ACTIONS:-} ]] && printf '::error file=%s,line=%s::dangling [[%s]] — %s\n' "$1" "$2" "$3" "$4"
  fail=1
}

while IFS=: read -r file line ref; do
  [[ -z ${ref:-} ]] && continue
  if [[ $ref == *:* ]]; then
    owner=${ref%%:*}
    id=${ref#*:}
    if ! is_known_owner "$owner"; then
      report "$file" "$line" "$ref" "unknown owner '$owner' (known: $KNOWN_OWNERS)"
    elif [[ $owner == "$SELF_OWNER" ]]; then
      if is_published "$id"; then
        report "$file" "$line" "$ref" "self-qualified; this repo publishes '$id' — write it bare as [[$id]]"
      else
        report "$file" "$line" "$ref" "qualified to this repo, which does not publish '$id'"
      fi
    fi
    # A foreign qualified ref is trusted: this repo cannot see the other repo's tree.
  elif ! is_published "$ref"; then
    report "$file" "$line" "$ref" "this repo does not publish it; qualify it as [[<owner>:$ref]]"
  fi
done < <(grep -rEon '\[\[[A-Za-z0-9._:-]+\]\]' "$SKILL_ROOT" --include='*.md' \
  | sed -E 's/\[\[(.*)\]\]$/\1/')

if ((fail)); then
  echo >&2
  echo "check-skill-refs: FAILED — every [[ref]] must resolve locally or name its publishing repo." >&2
  exit 1
fi

echo "check-skill-refs: ok — every [[ref]] in $SKILL_ROOT resolves ($(grep -c . <<<"$published") skills published)."
