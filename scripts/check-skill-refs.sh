#!/usr/bin/env bash
# check-skill-refs — fail on a dangling pointer in this repo's published product skills.
#
# A published SKILL.md makes two kinds of promise to its reader, and this script checks both:
#
#   [[wiki-ref]]        (FS.GG.Game#35)  — "a skill by this name resolves"
#   an issue/PR link    (FS.GG.Game#202) — "there is a live issue at the other end"
#
# ── 1. WIKI REFS (FS.GG.Game#35) ────────────────────────────────────────────────────────────────
# A skill body that writes `[[fs-gg-scene]]` promises the reader a skill this repo publishes. It
# does not: `fs-gg-scene` is owned by `fs-gg-rendering`. An agent following the pointer in a
# scaffolded product finds nothing. Before this check there were 21 such refs across all 5 published
# skills, and every newly authored skill inherited the defect (fs-gg-ballistics shipped two more).
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
# ── 2. ISSUE / PR LINKS (FS.GG.Game#202) ────────────────────────────────────────────────────────
# The same sentence in fs-gg-persistence has pointed at a CLOSED issue for four generations running
# (Rendering#445 → #535 → #587 → #535 again, the last one re-introduced by the very commit that
# adopted the "canonical" body). Every rot was caught by a human reading the prose; none by CI. This
# gate already scanned exactly these files for dangling references — it just never looked at links.
# It had the right scope and the wrong vocabulary.
#
# THE CONVENTION. A pointer is an issue/PR link in one of two QUALIFIED forms:
#   * URL        — https://github.com/FS-GG/FS.GG.Rendering/issues/535
#   * SHORTHAND  — `FS.GG.Rendering#535`, or `FS-GG/FS.GG.Rendering#535` (owner defaults to FS-GG)
# It must resolve, and it must be OPEN — because the prose around it almost always tells the reader
# to go and DO something there ("add your case there"), and a closed issue is a place nobody can act.
#
# A BARE `#535` IS DELIBERATELY NOT A POINTER, for two independent reasons:
#   * It is ambiguous with prose. fs-gg-line-drawing says `the design doc's "#1 LOS bug"` — that is
#     the number-one bug, not issue #1. A gate that resolved it would report a false dangling ref,
#     and a gate that cries wolf is one people turn off.
#   * These bodies are MATERIALIZED INTO SOMEBODY ELSE'S REPOSITORY. A bare `#535` there resolves
#     against the *product's* issue tracker, not ours — so it is already unresolvable to the reader
#     it was written for. The fix for a bare ref is to qualify it, not to teach this script to guess
#     which repo the author meant.
# This mirrors the wiki-ref rule above: only an unambiguous, qualified form promises resolvability.
#
# CLOSED-AND-STILL-CORRECT IS REAL, so there is an opt-out. A body may legitimately cite the issue
# that IMPLEMENTED something — fs-gg-audio's "the scaffold ships it wired (FS.GG.Rendering#245)" is
# history, and history stays closed. Excuse such a citation with a marker naming the exact ref:
#
#     <!-- skill-refs: closed-ok FS.GG.Rendering#245 — cited as the issue that wired the seam -->
#
# WHY AN ERROR WITH AN OPT-OUT, AND NOT A WARNING. #202 floated a warning. A warning is what we
# already have: CI green, and a human expected to read the prose and notice. That is precisely the
# failure this gate exists to end — and the "gate reports green on a missing subject" shape
# (FS-GG/.github#416) the org keeps rediscovering late. The marker is what makes the strict default
# affordable: an honest citation costs one line and is then SELF-DOCUMENTING to the next reader,
# and the intent becomes machine-readable rather than inferred from the surrounding sentence.
# A marker that excuses nothing is itself reported — a stale allowlist is where the next rot hides.
#
# NETWORK. Resolving a link needs the API (REST only; a handful of calls, no GraphQL). The gate must
# never SELF-SKIP: under GITHUB_ACTIONS an unresolvable link is a FAILURE, never a pass. Locally,
# with no `gh` or no auth, the link half announces loudly that it did not run, and the wiki half
# still does. `SKILL_REFS_SKIP_LINKS=1` skips the link half locally; it is IGNORED in CI.
#
# Usage: scripts/check-skill-refs.sh          # exit 1 and name every offending file:line
set -euo pipefail

cd "$(dirname "$0")/.."

SKILL_ROOT="template/product-skills"
SELF_OWNER="fs-gg-game"
# registry/skills.yml `owner:` vocabulary (FS-GG/.github), one per line.
KNOWN_OWNERS=$'fs-gg-game\nfs-gg-rendering\nfs-gg-sdd'
# The org an unqualified `FS.GG.Rendering#535` belongs to.
DEFAULT_OWNER="FS-GG"

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
  printf '%s:%s: %s\n' "$1" "$2" "$3" >&2
  # A GitHub Actions annotation when running in CI; harmless locally.
  [[ -n ${GITHUB_ACTIONS:-} ]] && printf '::error file=%s,line=%s::%s\n' "$1" "$2" "$3"
  fail=1
}

# ────────────────────────────────────────────────────────────────────────────────────────────────
# 1. WIKI REFS
# ────────────────────────────────────────────────────────────────────────────────────────────────
while IFS=: read -r file line ref; do
  [[ -z ${ref:-} ]] && continue
  if [[ $ref == *:* ]]; then
    owner=${ref%%:*}
    id=${ref#*:}
    if ! is_known_owner "$owner"; then
      report "$file" "$line" "dangling [[$ref]] — unknown owner '$owner' (known: $KNOWN_OWNERS)"
    elif [[ $owner == "$SELF_OWNER" ]]; then
      if is_published "$id"; then
        report "$file" "$line" "dangling [[$ref]] — self-qualified; this repo publishes '$id' — write it bare as [[$id]]"
      else
        report "$file" "$line" "dangling [[$ref]] — qualified to this repo, which does not publish '$id'"
      fi
    fi
    # A foreign qualified ref is trusted: this repo cannot see the other repo's tree.
  elif ! is_published "$ref"; then
    report "$file" "$line" "dangling [[$ref]] — this repo does not publish it; qualify it as [[<owner>:$ref]]"
  fi
done < <(grep -rEon '\[\[[A-Za-z0-9._:-]+\]\]' "$SKILL_ROOT" --include='*.md' \
  | sed -E 's/\[\[(.*)\]\]$/\1/')

# ────────────────────────────────────────────────────────────────────────────────────────────────
# 2. ISSUE / PR LINKS
# ────────────────────────────────────────────────────────────────────────────────────────────────

# Every hit is normalised to a `file<TAB>line<TAB>owner<TAB>repo<TAB>num` row, in ONE pass.
#
# A `skill-refs:` marker is STRIPPED before extraction: it is this gate's config, not the body's
# prose. Left in, a marker would be scanned as a link and so EXCUSE ITSELF — a typo'd ref would
# validate against nothing but its own mention, and a marker outliving the sentence it was written
# for would still look live. Config must not be its own subject.
#
# The persistence pointer is BOTH forms at once — `[FS-GG/FS.GG.Rendering#587](https://…/587)` — so
# the two scans overlap by design and `sort -u` collapses them to one row (and so to one API call).
#
# The leading `[^A-Za-z0-9._/#-]` (or start-of-line) is what keeps a BARE `#1` out: in fs-gg-line-
# drawing's `the design doc's "#1 LOS bug"` the char before `#` is a quote and no repo token
# precedes it, so nothing matches. It is consumed by the match, so the ref is re-trimmed.
emit_links() {
  # -r: with no *.md at all, xargs would otherwise run awk with NO file operands, and awk would read
  # the script's stdin — yielding zero links from whatever it found there. A silent no-op is the one
  # outcome this gate may never produce.
  find "$SKILL_ROOT" -name '*.md' -print0 \
    | xargs -0 -r awk -v OFS='\t' -v def="$DEFAULT_OWNER" '
      # Strip skill-refs markers, terminating on `-->` rather than on the first `>`. A rationale is
      # prose and may well contain one ("superseded -> #9"), and a marker left un-stripped is scanned
      # as a link and EXCUSES ITSELF — the exact defect the strip exists to prevent. Other HTML
      # comments are kept: a ref inside one is still a ref.
      function strip_markers(s,   res, i, j, seg) {
        res = ""
        while ((i = index(s, "<!--")) > 0) {
          j = index(substr(s, i), "-->")
          if (j == 0) {                       # unterminated — drop it if it is ours, else stop
            if (substr(s, i) ~ /^<!--[[:space:]]*skill-refs:/) s = substr(s, 1, i - 1)
            break
          }
          seg = substr(s, i, j + 2)
          res = res substr(s, 1, i - 1)
          if (seg !~ /^<!--[[:space:]]*skill-refs:/) res = res seg
          s = substr(s, i + j + 2)
        }
        return res s
      }
      {
        line = strip_markers($0)

        s = line
        while (match(s, /https:\/\/github\.com\/[A-Za-z0-9._-]+\/[A-Za-z0-9._-]+\/(issues|pull)\/[0-9]+/)) {
          u = substr(s, RSTART, RLENGTH); s = substr(s, RSTART + RLENGTH)
          split(u, p, "/")            # p[4]=owner  p[5]=repo  p[7]=num
          print FILENAME, FNR, p[4], p[5], p[7]
        }

        s = line
        while (match(s, /(^|[^A-Za-z0-9._\/#-])([A-Za-z0-9._-]+\/)?[A-Za-z][A-Za-z0-9._-]*#[0-9]+/)) {
          t = substr(s, RSTART, RLENGTH); s = substr(s, RSTART + RLENGTH)
          sub(/^[^A-Za-z0-9]+/, "", t)
          h = index(t, "#"); num = substr(t, h + 1); nr = substr(t, 1, h - 1)
          sl = index(nr, "/")
          if (sl) print FILENAME, FNR, substr(nr, 1, sl - 1), substr(nr, sl + 1), num
          else    print FILENAME, FNR, def, nr, num
        }
      }'
}

# `sort -u` must dedupe on the WHOLE row — keying it would collapse two distinct refs sharing a line.
# Order for display in a second, non-unique pass.
links=$(emit_links | sort -u | sort -t$'\t' -k1,1 -k2,2n)

# The closed-ok allowlist, normalised to `file<TAB>line<TAB>owner/repo#num` — one row per marker.
markers=$( { grep -rEon --include='*.md' \
    '<!--[[:space:]]*skill-refs:[[:space:]]*closed-ok[[:space:]]+[A-Za-z0-9._/-]+#[0-9]+' \
    "$SKILL_ROOT" || true; } | while IFS= read -r m; do
    [[ -z $m ]] && continue
    mfile=${m%%:*}; mrest=${m#*:}; mline=${mrest%%:*}; mref=${mrest##* }
    [[ $mref == */* ]] || mref="$DEFAULT_OWNER/$mref"
    printf '%s\t%s\t%s\n' "$mfile" "$mline" "$mref"
  done)

is_excused() { # file owner/repo#num
  [[ -n $markers ]] && awk -F'\t' -v f="$1" -v r="$2" '$1==f && $3==r {found=1} END{exit !found}' <<<"$markers"
}

# ── link resolution ─────────────────────────────────────────────────────────────────────────────
# THREE states, and conflating the first two is a bug this gate has already made once: "there is
# nothing to check" is not "I could not check". A file holding only a stale MARKER has no links —
# and is exactly the dead config the marker audit exists to catch, so it must still reach the API.
link_mode=checked          # checked | empty | skipped
skip_reason=""
if [[ -z $links && -z $markers ]]; then
  link_mode=empty
elif [[ -n ${SKILL_REFS_SKIP_LINKS:-} && -z ${GITHUB_ACTIONS:-} ]]; then
  link_mode=skipped
  skip_reason="SKILL_REFS_SKIP_LINKS is set"
elif ! command -v gh >/dev/null 2>&1 || ! gh auth status >/dev/null 2>&1; then
  if [[ -n ${GITHUB_ACTIONS:-} ]]; then
    # NEVER let the gate self-skip: a link it cannot read is one it cannot call green.
    echo "check-skill-refs: FAILED — no authenticated \`gh\` in CI, so issue/PR links cannot be" >&2
    echo "  resolved. Give the step a token (env: GH_TOKEN: \${{ secrets.GITHUB_TOKEN }}); do not" >&2
    echo "  skip the check — a green gate over an unread link is the defect this gate exists for." >&2
    exit 1
  fi
  link_mode=skipped
  skip_reason="no authenticated \`gh\` (run \`gh auth login\`)"
fi

# The cache is a FILE, not a variable, because every call site is a `$(resolve_state …)` command
# substitution — a subshell, whose variable writes are discarded the moment it exits. A shell-var
# cache here would silently never hit, and each ref would be re-fetched: once as a link, again in
# the marker audit. A file write outlives the subshell.
state_cache=$(mktemp)
trap 'rm -f "$state_cache"' EXIT

resolve_state() { # owner repo num -> open | closed | missing | unresolved
  local owner=$1 repo=$2 num=$3 key="$1/$2#$3" hit out err attempt
  hit=$(awk -F'\t' -v k="$key" '$1==k {print $2; exit}' "$state_cache")
  if [[ -n $hit ]]; then printf '%s' "$hit"; return 0; fi

  err=$(mktemp)
  out="unresolved"
  for attempt in 1 2 3; do
    if out=$(gh api "repos/$owner/$repo/issues/$num" --jq .state 2>"$err"); then
      break
    fi
    if grep -q 'HTTP 404' "$err"; then out="missing"; break; fi
    out="unresolved"
    # A transient 5xx / secondary limit, not a verdict — but do not sleep after the LAST attempt.
    if ((attempt < 3)); then sleep $((attempt * 2)); fi
  done
  rm -f "$err"

  printf '%s\t%s\n' "$key" "$out" >>"$state_cache"
  printf '%s' "$out"
}

checked=0
if [[ $link_mode == checked && -n $links ]]; then
  while IFS=$'\t' read -r file line owner repo num; do
    [[ -z ${num:-} ]] && continue
    ref="$owner/$repo#$num"
    checked=$((checked + 1))
    case "$(resolve_state "$owner" "$repo" "$num")" in
      open) ;;
      closed)
        if ! is_excused "$file" "$ref"; then
          # Name the owner unless it is the default one — a marker is matched on the CANONICAL
          # owner/repo#num, so suggesting a bare `Repo#n` for a foreign-owned link would hand the
          # author a marker that normalises to the wrong org and never silences anything.
          sugg="$repo#$num"
          [[ $owner == "$DEFAULT_OWNER" ]] || sugg="$owner/$repo#$num"
          report "$file" "$line" "stale link — $ref is CLOSED. Repoint it at the live issue, or, if it is cited as history, excuse it: <!-- skill-refs: closed-ok $sugg — why -->"
        fi
        ;;
      missing)
        report "$file" "$line" "dangling link — $ref does not exist"
        ;;
      *)
        # Could not read it. In CI that is a failure, never a pass.
        report "$file" "$line" "unresolvable link — could not read $ref from the API after 3 tries"
        ;;
    esac
  done <<<"$links"
fi

# A marker that excuses nothing is dead config — and dead config is where the next rot hides. Three
# ways it goes dead: the sentence it guarded was rewritten and the link is gone; the issue reopened;
# the issue never existed (a typo). Markers are excluded from `links` above, so a marker can no
# longer vouch for itself and these checks mean something.
if [[ $link_mode == checked && -n $markers ]]; then
  while IFS=$'\t' read -r mfile mline mref; do
    [[ -z ${mref:-} ]] && continue
    if ! awk -F'\t' -v f="$mfile" -v r="$mref" \
         '$1==f && ($3"/"$4"#"$5)==r {found=1} END{exit !found}' <<<"$links"; then
      report "$mfile" "$mline" "stale closed-ok marker — nothing in this file links to $mref; drop it"
      continue
    fi
    mowner=${mref%%/*}; mrest=${mref#*/}; mrepo=${mrest%#*}; mnum=${mref##*#}
    case "$(resolve_state "$mowner" "$mrepo" "$mnum")" in
      closed) ;;   # doing its job
      open)
        report "$mfile" "$mline" "stale closed-ok marker — $mref is OPEN again; drop the marker"
        ;;
      missing)
        report "$mfile" "$mline" "stale closed-ok marker — $mref does not exist"
        ;;
    esac
  done <<<"$markers"
fi

if ((fail)); then
  echo >&2
  echo "check-skill-refs: FAILED — every pointer in a published skill must resolve: a [[ref]] to a" >&2
  echo "  skill, and an issue/PR link to a LIVE issue (or a marked, deliberate citation of history)." >&2
  exit 1
fi

n_skills=$(grep -c . <<<"$published")
case $link_mode in
  checked) echo "check-skill-refs: ok — $n_skills skills published; every [[ref]] resolves and all $checked issue/PR link(s) are open or marked." ;;
  empty)   echo "check-skill-refs: ok — $n_skills skills published; every [[ref]] resolves (no issue/PR links to check)." ;;
  skipped) echo "check-skill-refs: ok — $n_skills skills published; every [[ref]] resolves."
           echo "check-skill-refs: NOTE — issue/PR links were NOT checked ($skip_reason)." >&2 ;;
esac
