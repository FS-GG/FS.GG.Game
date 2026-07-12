#!/usr/bin/env bash
# check-skill-refs — fail on a dangling pointer in this repo's published product skills.
#
# A published SKILL.md makes three kinds of promise to its reader, and this script checks all three:
#
#   [[wiki-ref]]        (FS.GG.Game#35)  — "a skill by this name resolves"
#   an issue/PR link    (FS.GG.Game#202) — "there is a live issue at the other end"
#   a bare `#N`         (FS.GG.Game#208) — promises nothing it can keep: § 3 REJECTS it outright
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
# A BARE `#535` IS NOT ONE OF THOSE FORMS. It is not resolved — it is REJECTED. See § 3.
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
# ── 3. BARE `#N` REFS (FS.GG.Game#208) ──────────────────────────────────────────────────────────
# A bare `#999` in a shipped body is an ERROR. Not "unresolvable" — WRONG, and wrong by construction:
#
#   THESE BODIES MATERIALIZE INTO SOMEBODY ELSE'S REPOSITORY (`profile in [game, sample-pack]`).
#   GitHub renders `#999` against the repo it is READ in, so in a scaffolded product it links to
#   that product's OWN tracker — an unrelated issue, or nothing. It never points here.
#
# So its upstream state is beside the point: there is no `#999` we could resolve it against, and a
# bare ref is never correct in a shipped body even when the issue it meant is open. This is why the
# check needs no network, and why — unlike a stale link — it cannot be fixed by repointing it. The
# only fix is to QUALIFY it (`FS.GG.Game#999`), which survives materialization.
#
# #202 excluded bare refs because they are ambiguous with prose: fs-gg-line-drawing says `the design
# doc's "#1 LOS bug"` — the number-one bug, not issue #1. That ambiguity is real, and it is an
# argument for an ESCAPE HATCH, not for silence. Silence is the `.github#416` shape this gate exists
# to close — green over a pointer it never examined — surviving one level down. So: reject by
# default, and let a body declare the one honest exception, naming the exact number:
#
#     <!-- skill-refs: prose-ok #1 — the design doc's number-one bug, not issue #1 -->
#
# NO CARVE-OUT FOR CODE. A `#123` inside a fence or a code span is reported too. GitHub does not
# autolink there, so the RENDERING hazard is absent — but the pointer is still unresolvable to the
# reader, and exempting code would hand anyone a way to park an unchecked ref where the gate cannot
# see it. That is the silent-no-op hole again, reopened as a convenience. The marker costs one line.
#
# THE MARKER IS FILE-SCOPED, and knowingly so — `closed-ok` is too, and a marker that had to sit on
# the ref's own line could not be written above a list, which is where the one real case wants it.
# Own the cost, because it is not free: `prose-ok #1` keys on a BARE INTEGER, so it excuses EVERY
# bare `#1` in that file — including a genuine `see #1` somebody adds later. `closed-ok` keys on a
# whole `owner/repo#num` and is far harder to collide with. So keep prose-ok markers RARE and their
# numbers odd-looking; a file that wants to excuse `#1`, `#2` and `#3` is a file that should be
# rewording its prose instead. Narrowing the scope is FS.GG.Game#208's obvious follow-up if a second
# one ever shows up.
#
# WHY GUESSING IS NOT AN OPTION. `#1` in quotes reads as prose; `#1` after "see" reads as a pointer.
# Teaching the script that difference would make the gate's verdict depend on the surrounding
# sentence — unpredictable to the author, and wrong often enough to be turned off. The author knows
# which one they meant; the marker is how they say it, and it is self-documenting to the next reader.
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
# This repo's GitHub name — the qualification a bare `#N` in a body of OURS almost always wants. It
# is only ever a SUGGESTION in § 3's message: the author may well have meant another repo, and the
# gate does not guess (see § 3).
SELF_REPO="FS.GG.Game"
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
# The leading `[^A-Za-z0-9._/#-]` (or start-of-line) is what keeps a BARE `#1` out of THIS scan: in
# fs-gg-line-drawing's `the design doc's "#1 LOS bug"` the char before `#` is a quote and no repo
# token precedes it, so nothing matches. It is consumed by the match, so the ref is re-trimmed. A
# bare ref is no longer thereby IGNORED — § 3 scans for exactly what this pattern declines to claim.

# Strip skill-refs markers, terminating on `-->` rather than on the first `>`. A rationale is prose
# and may well contain one ("superseded -> #9"), and a marker left un-stripped is scanned as a ref
# and EXCUSES ITSELF — the exact defect the strip exists to prevent. Other HTML comments are kept: a
# ref inside one is still a ref.
#
# Shared by BOTH scans, and load-bearing for each. For § 2 an un-stripped `closed-ok FS.GG.X#9` is a
# link that vouches for itself; for § 3 an un-stripped `prose-ok #9` is *itself a bare `#9`* — it
# would excuse itself and every marker would be self-justifying. Config must not be its own subject.
AWK_STRIP='
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
  }'

# -r: with no *.md at all, xargs would otherwise run awk with NO file operands, and awk would read
# the script's stdin — yielding zero hits from whatever it found there. A silent no-op is the one
# outcome this gate may never produce.
md_files() { find "$SKILL_ROOT" -name '*.md' -print0; }

emit_links() {
  md_files | xargs -0 -r awk -v OFS='\t' -v def="$DEFAULT_OWNER" "$AWK_STRIP"'
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

# § 3's scan: every BARE `#N`, normalised to `file<TAB>line<TAB>num`. No API call — the FORM is the
# defect, so there is nothing to resolve and nothing the network could tell us.
#
# `#[0-9][A-Za-z0-9_-]*` over-matches on purpose, and only an all-digit run is kept. A CSS colour
# `#1a2b3c` opens exactly like an issue ref, and a bare `#[0-9]+` would match its leading `#1` and
# report a dangling ref that was never there — a false positive in a gate people would then turn
# off. Taking the WHOLE run and then requiring it to be all digits rejects `1a2b3c` and `abc123`.
# An all-NUMERIC colour (`#123456`) is genuinely indistinguishable from an issue ref — no pattern
# can separate them — so it is reported, and `prose-ok` is the answer. That is the honest limit:
# the gate declines to GUESS, here as everywhere, and asks the author who knows.
#
# The leading class carries `/` and `#`, which is what excludes the three near-misses: a URL fragment
# (`…/page#1`), a markdown heading (`## 3`), and the `#535` of a qualified `FS.GG.Rendering#535` —
# that last one is § 2's ref, and reporting it here would double-report every honest link.
emit_bare() {
  md_files | xargs -0 -r awk -v OFS='\t' "$AWK_STRIP"'
      {
        s = strip_markers($0)

        # An EXPLICIT markdown link is not a bare ref, and this is the difference between a gate and
        # a nuisance: `[#587](https://github.com/FS-GG/FS.GG.Rendering/issues/587)` is the single most
        # idiomatic way to cite an issue, and it is CORRECT — the `#587` is a link LABEL, so GitHub
        # renders it as display text and never autolinks it. It cannot resolve against the reader`s
        # tracker, which is the whole hazard § 3 exists for. Reporting it would reject the right
        # answer, and this gate would be the one people turn off.
        #
        # Drop the whole `[label](absolute-url)` construct — the label of ANY http(s) link, not just a
        # `[#N]` one, so `[see #459](https://…)` is spared too. Nothing is lost: § 2 scans the
        # UNSTRIPPED line, so the URL is still resolved and a closed or missing target still fails.
        # `[#587]` with NO target keeps its report, and must: GitHub autolinks that one.
        gsub(/\[[^]]*\]\(https?:\/\/[^)]+\)/, " ", s)

        while (match(s, /(^|[^A-Za-z0-9._\/#-])#[0-9][A-Za-z0-9_-]*/)) {
          t = substr(s, RSTART, RLENGTH); s = substr(s, RSTART + RLENGTH)
          sub(/^[^#]*#/, "", t)          # drop the consumed boundary char and the `#`
          if (t ~ /^[0-9]+$/) print FILENAME, FNR, t
        }
      }'
}

# `sort -u` must dedupe on the WHOLE row — keying it would collapse two distinct refs sharing a line.
# Order for display in a second, non-unique pass.
links=$(emit_links | sort -u | sort -t$'\t' -k1,1 -k2,2n)
bares=$(emit_bare | sort -u | sort -t$'\t' -k1,1 -k2,2n)

# The closed-ok allowlist, normalised to `file<TAB>line<TAB>owner/repo#num` — one row per marker.
markers=$( { grep -rEon --include='*.md' \
    '<!--[[:space:]]*skill-refs:[[:space:]]*closed-ok[[:space:]]+[A-Za-z0-9._/-]+#[0-9]+' \
    "$SKILL_ROOT" || true; } | while IFS= read -r m; do
    [[ -z $m ]] && continue
    mfile=${m%%:*}; mrest=${m#*:}; mline=${mrest%%:*}; mref=${mrest##* }
    [[ $mref == */* ]] || mref="$DEFAULT_OWNER/$mref"
    printf '%s\t%s\t%s\n' "$mfile" "$mline" "$mref"
  done)

# The prose-ok allowlist, normalised to `file<TAB>line<TAB>num` — one row per marker.
prose_markers=$( { grep -rEon --include='*.md' \
    '<!--[[:space:]]*skill-refs:[[:space:]]*prose-ok[[:space:]]+#[0-9]+' \
    "$SKILL_ROOT" || true; } | while IFS= read -r m; do
    [[ -z $m ]] && continue
    mfile=${m%%:*}; mrest=${m#*:}; mline=${mrest%%:*}; mnum=${m##*#}
    printf '%s\t%s\t%s\n' "$mfile" "$mline" "$mnum"
  done)

is_prose() { # file num
  [[ -n $prose_markers ]] &&
    awk -F'\t' -v f="$1" -v n="$2" '$1==f && $3==n {found=1} END{exit !found}' <<<"$prose_markers"
}

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

# ────────────────────────────────────────────────────────────────────────────────────────────────
# 3. BARE `#N` REFS
# ────────────────────────────────────────────────────────────────────────────────────────────────
# UNGATED BY `link_mode`, and that is the point. § 2 needs the API to learn whether an issue is open;
# § 3 needs nothing — a bare ref is wrong by its FORM, in every repo, whatever the issue's state. So
# it must still run with SKILL_REFS_SKIP_LINKS set, and on a laptop with no `gh`. Hanging it off
# `link_mode` would make an offline check silently skippable — the very shape § 3 exists to close.
n_bare=0
if [[ -n $bares ]]; then
  while IFS=$'\t' read -r file line num; do
    [[ -z ${num:-} ]] && continue
    n_bare=$((n_bare + 1))
    is_prose "$file" "$num" && continue
    report "$file" "$line" "bare ref — #$num is read against the repo this body is MATERIALIZED into, so it points at the reader's own tracker, never ours. Qualify it (e.g. $SELF_REPO#$num), or, if it is prose and not a pointer, say so: <!-- skill-refs: prose-ok #$num — why -->"
  done <<<"$bares"
fi

# A prose-ok marker that excuses nothing is dead config, exactly as a stale closed-ok is: the
# sentence it guarded was rewritten, or its number changed and the marker kept the old one. Markers
# are stripped before extraction, so one cannot vouch for itself and this check means something.
if [[ -n $prose_markers ]]; then
  while IFS=$'\t' read -r mfile mline mnum; do
    [[ -z ${mnum:-} ]] && continue
    if ! awk -F'\t' -v f="$mfile" -v n="$mnum" \
         '$1==f && $3==n {found=1} END{exit !found}' <<<"$bares"; then
      report "$mfile" "$mline" "stale prose-ok marker — nothing in this file writes a bare #$mnum; drop it"
    fi
  done <<<"$prose_markers"
fi

if ((fail)); then
  echo >&2
  echo "check-skill-refs: FAILED — every pointer in a published skill must resolve: a [[ref]] to a" >&2
  echo "  skill, and an issue/PR link to a LIVE issue (or a marked, deliberate citation of history)." >&2
  echo "  A bare #N is not a pointer at all — qualify it, or mark it as prose." >&2
  exit 1
fi

n_skills=$(grep -c . <<<"$published")
case $link_mode in
  checked) echo "check-skill-refs: ok — $n_skills skills published; every [[ref]] resolves and all $checked issue/PR link(s) are open or marked." ;;
  empty)   echo "check-skill-refs: ok — $n_skills skills published; every [[ref]] resolves (no issue/PR links to check)." ;;
  skipped) echo "check-skill-refs: ok — $n_skills skills published; every [[ref]] resolves."
           echo "check-skill-refs: NOTE — issue/PR links were NOT checked ($skip_reason)." >&2 ;;
esac

# Said out loud even at zero. § 3 is the half that still runs when the link half is skipped, so a
# silent pass here is indistinguishable from a check that did not happen — and "I found nothing" and
# "I did not look" being the same output is the defect this gate keeps being extended to kill.
if ((n_bare > 0)); then
  echo "check-skill-refs: ok — $n_bare bare #N ref(s); every one is marked prose-ok."
else
  echo "check-skill-refs: ok — no bare #N refs."
fi
