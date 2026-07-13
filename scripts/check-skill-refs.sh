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
# ── 4. WHAT A MERGE GATE MAY ASK (FS.GG.Game#238) ───────────────────────────────────────────────
# Sort the three checks by what their verdict is a FUNCTION of, because they do not agree:
#
#   § 1 wiki refs   f(tree)         hermetic — same tree, same answer, forever
#   § 3 bare #N     f(tree)         hermetic — the FORM is the defect; no network can change it
#   § 2 link state  f(tree, WORLD)  NOT hermetic — it decays in place, with no commit
#
# § 2 asks whether the world still agrees with what we published. That is a real and valuable
# question — it is the one that caught the persistence pointer rotting four generations running.
# But its answer moves on its own. FS-GG/FS.GG.Rendering#494 was closed at 14:43:32Z, and from that
# minute `main`@e4c07c2 — green at 14:35:30Z, unchanged since — failed. The gate did not become
# wrong; the world moved, and § 2 correctly reported it. It just reported it AT whoever happened to
# have a PR open.
#
# THE RULE THIS ENFORCES: **a merge gate may only demand a change inside the diff it is gating.**
# The old § 2 broke that rule, and not merely unfairly — IMPOSSIBLY. #235 touched four pathfinding
# files; the only edit that could turn it green was in `fs-gg-persistence/SKILL.md`, which is not in
# that item's declared `Paths:`. Under this repo's parallel-work protocol (ADR-0021/0027) the author
# cannot just fix it: they must `widen` onto a file their item has nothing to do with, and collide
# with whoever holds the skills. The gate MANUFACTURED a false collision, and the worker's only
# honest move was to prove the red was not theirs.
#
# So § 2 is not weakened — a warning is what we already had, and § 2's own header explains why that
# failed. It is SCOPED. `--changed <base>` restricts the link half to the skill bodies THIS diff
# touches:
#
#   * A diff that touches a skill OWNS that skill's pointers. The fix — repoint, or `closed-ok` —
#     is inside a file the diff already holds, so it is always compliable. Every rot § 2 was written
#     to catch is introduced by such a diff, so authorship-time strictness is fully preserved.
#   * A diff that touches no skill CANNOT be reddened by the link half. There is no edit it could
#     make, so there is no verdict it deserves.
#
# AND THE SWEEP IS WHAT MAKES THAT HONEST — the two halves are ONE change, and shipping the scope
# without the sweep would be strictly worse than shipping neither. Scoping alone leaves every link
# in an unchanged file resolved by nobody: a gate green over a subject it never examined, which is
# the exact `.github#416` shape this script exists to close, reintroduced by its own fix. So the
# FULL sweep still runs — on a schedule, over `main`, in .github/workflows/skill-refs-sweep.yml,
# where it gates nothing. There, a red means the world moved and a citation needs maintenance, which
# is TRUE and is addressed to the repo. It is not an accusation aimed at a stranger's diff.
#
# Same subject, same strictness, two questions, and each asked where it can be answered:
#
#   did this DIFF introduce a bad pointer?   → the merge gate, blocking, fix is inside the diff
#   has the WORLD moved under a good one?    → the sweep, non-blocking, fix is a maintenance task
#
# DEGRADE TOWARD MORE CHECKING, NEVER LESS. If `--changed` gets a base it cannot resolve (a force
# push; `github.event.before` all-zeros on a branch's first push), it does NOT quietly conclude
# "no files changed" and pass — that is the silent no-op again, and it would disable the gate at
# exactly the moment history looks strange. It announces the fallback and sweeps EVERYTHING.
#
# Usage: scripts/check-skill-refs.sh                  # full sweep: every link in the tree
#        scripts/check-skill-refs.sh --changed <ref>  # link half only for skills this diff touches
set -euo pipefail

cd "$(dirname "$0")/.."

CHANGED_BASE=""
while (($#)); do
  case $1 in
    --changed)
      [[ $# -ge 2 ]] || { echo "check-skill-refs: --changed needs a base ref" >&2; exit 2; }
      CHANGED_BASE=$2; shift 2 ;;
    --changed=*) CHANGED_BASE=${1#*=}; shift ;;
    -h|--help)
      echo "usage: check-skill-refs.sh [--changed <base-ref>]"
      echo "  (no args)          full sweep: every issue/PR link in the tree"
      echo "  --changed <ref>    link half only for skill bodies changed since <ref>"
      echo "                     ([[refs]] and bare #N are hermetic and always sweep the tree)"
      exit 0 ;;
    *) echo "check-skill-refs: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

SKILL_ROOT="template/product-skills"
SELF_OWNER="fs-gg-game"
# This repo's GitHub name — the qualification a bare `#N` in a body of OURS almost always wants. It
# is only ever a SUGGESTION in § 3's message: the author may well have meant another repo, and the
# gate does not guess (see § 3).
SELF_REPO="FS.GG.Game"
# registry/skills.yml `owner:` vocabulary (FS-GG/.github), one per line.
KNOWN_OWNERS=$'fs-gg-game\nfs-gg-rendering\nfs-gg-sdd'
# The bodies ADR-0022 §6 mirrors BYTE-IDENTICALLY into FS.GG.Rendering. A [[ref]] in one of these is
# read by TWO gates with different publish sets, so it must be QUALIFIED: a bare ref is repo-relative,
# and repo-relative is exactly what FS.GG.Game#273 proved cannot survive the mirror. Bare stays legal
# everywhere else — a body that is never mirrored has one reader, and that reader can check it.
#
# Without this rule the #273 fix is FOLKLORE: qualifying the refs once fixes today, and the very next
# bare ref added to one of these bodies silently restores the contradiction — green here (we publish
# it) and dangling there. Both gates green, because the one that would object has been told to NOTE
# these bodies rather than fail on them. That is the shape this whole script exists to refuse, so it
# does not get to hold it.
#
# THE SET IS DATA NOW, NOT A LIST HERE (FS.GG.Game#280). It used to be a hardcoded MIRRORED_SKILLS
# list on this line — a copy of a fact that lives in ADR-0022, in another repo, checked against
# nothing. And it failed in the SILENT direction: a body mirrored in FUTURE was simply absent from
# it, so bare refs in it stayed legal, so it dangled in Rendering while BOTH gates said green. That
# is #273 verbatim, reintroduced by the guard written to prevent it, and it happens the next time the
# org does something entirely normal — mirror a fifth body.
#
# It now comes from the producer manifest, which is generated (scripts/generate-skill-manifest.fsx),
# drift-gated in CI, and — the part that matters — REQUIRES a verdict: a new skill's catalog row does
# not compile without one. The question can no longer be skipped, only answered.
MANIFEST="template/skill-manifest/skill-manifest.json"
# The org an unqualified `FS.GG.Rendering#535` belongs to.
DEFAULT_OWNER="FS-GG"

if [[ ! -d $SKILL_ROOT ]]; then
  echo "check-skill-refs: no $SKILL_ROOT — nothing to check." >&2
  exit 0
fi

# The mirror set is only as good as the file it comes from, so a manifest this gate cannot READ is
# fatal (exit 2), never a shrug. Carrying on without it would mean every body looks unmirrored — the
# gate would examine each one and find nothing to say, which is indistinguishable from a clean tree.
# "I found nothing" and "I could not look" must not be the same output; that is the silent-no-op this
# script exists to refuse, and it does not get to commit it either.
REGEN="run \`dotnet fsi scripts/generate-skill-manifest.fsx\`"
if [[ ! -f $MANIFEST ]]; then
  echo "check-skill-refs: no $MANIFEST — cannot tell which bodies ADR-0022 §6 mirrors." >&2
  echo "check-skill-refs: $REGEN." >&2
  exit 2
fi
if ! command -v jq >/dev/null 2>&1; then
  echo "check-skill-refs: jq is required to read $MANIFEST (the ADR-0022 §6 mirror set)." >&2
  exit 2
fi

# The skills THIS repo publishes: one directory per skill id.
published=$(for d in "$SKILL_ROOT"/*/; do basename "$d"; done | sort)

# ADR-0022 §6, machine-readable: the bodies FS.GG.Rendering ships byte-identically.
#
# `classified` is a STRICTLY DIFFERENT QUESTION, and conflating the two would rebuild the bug: it is
# the rows that state a verdict — `mirrored` present AND boolean — not merely the rows that exist.
#
# The distinction is not pedantry, it is the whole defect one level down. `select(.mirrored == true)`
# reads a row with NO mirrored field as false, because `null == true` is false. So a manifest written
# before this field existed — a stale artifact, a bad merge, a hand-edit — would answer "not mirrored"
# for ALL TWELVE bodies, every one of them confidently, and the four that Rendering really does ship
# byte-identically would go UNGUARDED while this gate reported green. That is the exact failure #280
# removes, walking back in through the data it replaced the list with. A row that does not SAY is a
# row that does not KNOW, and it is handled below with every other unclassified body: loudly.
#
# A manifest that is not readable as JSON at all fails the same way for the same reason (exit 2, not
# an empty set: an empty set is an answer, and "I could not parse it" is not).
# NOT `jq -e`: it exits 4 when a filter yields NO output, and an empty mirror set is a perfectly good
# answer (a tree with no mirrored bodies), not a broken file. Plain jq already exits non-zero on the
# states that must be fatal — unparseable JSON, a `.skills` that is not an array — and zero, with no
# output, on the states that must not be.
if ! mirrored=$(jq -r '.skills[] | select(.mirrored == true) | .id' "$MANIFEST" 2>/dev/null) \
  || ! classified=$(jq -r '.skills[] | select(.mirrored == true or .mirrored == false) | .id' "$MANIFEST" 2>/dev/null); then
  echo "check-skill-refs: cannot read $MANIFEST as a skill manifest — cannot tell which bodies ADR-0022 §6 mirrors." >&2
  echo "check-skill-refs: $REGEN." >&2
  exit 2
fi
mirrored=$(sort <<<"$mirrored")
classified=$(sort <<<"$classified")

# -x -F, never -w: `grep -w game` matches inside `fs-gg-game` (a `-` is a word boundary), and an
# unanchored pattern is a REGEX, so `fs.gg.game` would match too. Both would wave a typo'd owner
# through — and a foreign qualified ref is trusted, so nothing downstream would catch it.
is_published() { grep -qxF -- "$1" <<<"$published"; }
is_known_owner() { grep -qxF -- "$1" <<<"$KNOWN_OWNERS"; }
is_mirrored() { grep -qxF -- "$1" <<<"$mirrored"; }
is_classified() { grep -qxF -- "$1" <<<"$classified"; }
# `template/product-skills/fs-gg-game-core/SKILL.md` → `fs-gg-game-core`.
skill_of() { local rel=${1#"$SKILL_ROOT"/}; printf '%s' "${rel%%/*}"; }

fail=0
report() {
  printf '%s:%s: %s\n' "$1" "$2" "$3" >&2
  # A GitHub Actions annotation when running in CI; harmless locally.
  [[ -n ${GITHUB_ACTIONS:-} ]] && printf '::error file=%s,line=%s::%s\n' "$1" "$2" "$3"
  fail=1
}

# ────────────────────────────────────────────────────────────────────────────────────────────────
# 0. THE MIRROR VERDICT IS MANDATORY  (FS.GG.Game#280)
# ────────────────────────────────────────────────────────────────────────────────────────────────
#
# A published body the manifest reaches NO verdict on is UNCLASSIFIED, and this gate will not guess.
#
# Guessing has exactly one plausible default — "not mirrored", the majority answer — and that default
# IS the bug this item removed, wearing a different hat. Read the failure the other way round: an
# unclassified body is, by construction, a body nobody thought about, and the body nobody thought
# about is precisely the one someone just added to the mirror. So the one case where the default is
# WRONG is the one case it gets exercised, and it is wrong in the silent direction — bare refs stay
# legal, the body dangles in Rendering, and both gates report green. Defaulting here would have
# faithfully rebuilt the hardcoded list's failure mode on top of the data structure that replaced it.
#
# So: unclassified is RED, and it names the file to fix. The cost is a chore — add a catalog row when
# you add a skill — and the chore is loud, immediate, and lands on the author who has the context.
#
# This also closes the hole one level up, which nothing else covered: a new skill DIRECTORY with no
# catalog row was invisible. The manifest simply lacked it, `--check` compared the on-disk manifest to
# the generator's output, both agreed it did not exist, and the drift gate went green over a body it
# had never heard of.
while read -r id; do
  # An EMPTY skill root leaves the glob unexpanded, so `published` is the literal `*` — not a skill,
  # and not this check's business to shout about.
  [[ -z $id || ! -d $SKILL_ROOT/$id ]] && continue
  is_classified "$id" && continue
  report "$SKILL_ROOT/$id/SKILL.md" 1 \
    "'$id' is published but has NO mirror verdict in $MANIFEST (no row, or a row with no \`mirrored\` field) — the gate cannot tell whether ADR-0022 §6 mirrors it, and it will not assume. Add it to the catalog in scripts/generate-skill-manifest.fsx (Mirrored or NotMirrored) and regenerate (FS.GG.Game#280)"
done <<<"$published"

# ────────────────────────────────────────────────────────────────────────────────────────────────
# 1. WIKI REFS
# ────────────────────────────────────────────────────────────────────────────────────────────────
#
# A SELF-QUALIFIED ref that RESOLVES is fine, and this used to reject it ("write it bare"). That
# rejection is what made the convention unmirrorable, and it had to go (FS.GG.Game#273).
#
# The verdict on a bare ref is a function of the EVALUATING REPO'S PUBLISH SET, not of the bytes. So
# the same body, mirrored byte-identically into another repo (ADR-0022 §6 requires exactly that of
# four of ours), gets OPPOSITE verdicts in the two repos:
#
#     [[fs-gg-ballistics]]              here: correct bare   |  in Rendering: DANGLING (no ballistics)
#     [[fs-gg-rendering:fs-gg-scene]]   here: correct foreign|  in Rendering: "self-qualified, write it bare"
#
# Byte-identity and a publish-set-relative convention are not in tension — they are arithmetically
# incompatible, and both were mandated. There was NO edit to those bytes that made both repos green:
# every fix for one was a defect in the other, and byte-identity forbade diverging.
#
# A fully-qualified ref breaks the tie, because it is the one spelling whose verdict does NOT depend
# on who is reading:
#
#     [[fs-gg-game:fs-gg-ballistics]]   here: self + published → checked, OK
#                                       in Rendering: foreign → trusted, OK
#     [[fs-gg-rendering:fs-gg-scene]]   here: foreign → trusted, OK
#                                       in Rendering: self + published → checked, OK
#
# And note what that buys beyond merely agreeing: each ref is now VALIDATED EXACTLY ONCE, by the only
# repo that can see the tree it names, and trusted everywhere else. The mirrored bodies are qualified
# throughout for this reason. Bare stays legal — it is correct and checkable in a body that is never
# mirrored — so this is a widening, not a new obligation.
#
# What is NOT weakened: a typo'd owner is still caught, a self-qualified ref to a skill we do NOT
# publish is still dangling, and a bare ref we do not publish is still dangling. The only thing that
# stopped being an error is a ref that was always TRUE.
while IFS=: read -r file line ref; do
  [[ -z ${ref:-} ]] && continue
  if [[ $ref == *:* ]]; then
    owner=${ref%%:*}
    id=${ref#*:}
    if ! is_known_owner "$owner"; then
      report "$file" "$line" "dangling [[$ref]] — unknown owner '$owner' (known: $KNOWN_OWNERS)"
    elif [[ $owner == "$SELF_OWNER" ]] && ! is_published "$id"; then
      report "$file" "$line" "dangling [[$ref]] — qualified to this repo, which does not publish '$id'"
    fi
    # A foreign qualified ref is trusted: this repo cannot see the other repo's tree.
    # A self-qualified ref that IS published resolves, and is accepted — see the header.
  elif ! is_published "$ref"; then
    report "$file" "$line" "dangling [[$ref]] — this repo does not publish it; qualify it as [[<owner>:$ref]]"
  elif is_mirrored "$(skill_of "$file")"; then
    # It RESOLVES here — and that is the trap. This body is mirrored byte-identically into a repo
    # with a different publish set, where the very same bare ref dangles. Only a qualified ref gets
    # the same verdict from both readers.
    report "$file" "$line" "bare [[$ref]] in a MIRRORED body — it resolves here and dangles in the repo ADR-0022 §6 mirrors this into; write it [[$SELF_OWNER:$ref]] (FS.GG.Game#273)"
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
#
# WHOLE-TREE, and it stays that way even under `--changed`. § 1 and § 3 are f(tree): they need no
# network, they cost nothing, and they cannot decay — so there is no reason to scope them and one
# good reason not to. Scoping a hermetic check buys nothing and can only lose coverage.
md_files() { find "$SKILL_ROOT" -name '*.md' -print0; }

# The files § 2 resolves links in — the ONLY check that is f(world), and so the only one scoped.
# See § 4. Deletions are excluded (--diff-filter=ACMR): a body the diff REMOVED has no pointers
# left to keep, and scanning a path that is no longer on disk would just fail to open it.
link_scope="tree"          # tree | diff
link_scope_note=""
if [[ -n $CHANGED_BASE ]]; then
  # USABLE means two things, and checking only the first is a bug this script already made once.
  #
  #   1. it RESOLVES — `github.event.before` is all-zeros on a branch's first push, and a base that
  #      was never fetched is not here either;
  #   2. it SHARES HISTORY with HEAD — `git diff A...HEAD` is defined via the merge base, so a
  #      commit that EXISTS but has no common ancestor (a force push that rewrote the root; a ref
  #      from an unrelated repo) makes git exit 128 with `fatal: no merge base`.
  #
  # (2) is not hypothetical and existence does not imply it: `git rev-parse --verify` says yes to an
  # orphan commit, and the diff then dies — killing the gate job with a raw `fatal:` on a PR that did
  # nothing wrong. That is the FALSE RED of #238 walking back in through the fallback that exists to
  # prevent it. Both conditions, or sweep the tree.
  base_problem=""; base_hint=""
  if ! git rev-parse --verify --quiet "$CHANGED_BASE^{commit}" >/dev/null; then
    base_problem="does not resolve here"
    base_hint="It was never fetched, or it is the all-zeros of a first/force push."
  elif ! git merge-base "$CHANGED_BASE" HEAD >/dev/null 2>&1; then
    base_problem="shares no history with HEAD"
    base_hint="There is no merge base, so there is no diff to take against it."
  fi

  if [[ -z $base_problem ]]; then
    link_scope="diff"
    # NOT `${CHANGED_BASE:0:12}` — that is a SHA abbreviation applied to something that need not be a
    # SHA, and it turns `--changed origin/some-long-branch` into `origin/some` in the report: a ref
    # that does not exist, named as the thing we diffed against. Abbreviate only what git says is one.
    link_scope_note="changed since $(git rev-parse --short "$CHANGED_BASE" 2>/dev/null || printf '%s' "$CHANGED_BASE")"
  else
    # DEGRADE TOWARD MORE CHECKING (§ 4). An unusable base is not "nothing changed" — reading it that
    # way would switch the gate off precisely when history looks strange. Say so, and sweep everything.
    echo "check-skill-refs: NOTE — base '$CHANGED_BASE' $base_problem. $base_hint" >&2
    echo "  Falling back to the FULL link sweep rather than checking nothing." >&2
    link_scope_note="base '$CHANGED_BASE' $base_problem — swept the whole tree instead"
  fi
fi

link_md_files() {
  if [[ $link_scope == diff ]]; then
    git diff --name-only -z --diff-filter=ACMR "$CHANGED_BASE...HEAD" -- "$SKILL_ROOT" \
      | while IFS= read -r -d '' f; do
          [[ $f == *.md && -f $f ]] && printf '%s\0' "$f"
        done
  else
    md_files
  fi
}

emit_links() {
  link_md_files | xargs -0 -r awk -v OFS='\t' -v def="$DEFAULT_OWNER" "$AWK_STRIP"'
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

# How many bodies the link half actually LOOKED at — reported, so a scoped run states its subject
# rather than leaving the reader to infer it from a count of zero.
n_link_files=$(link_md_files | tr '\0' '\n' | grep -c . || true)

# The closed-ok allowlist, normalised to `file<TAB>line<TAB>owner/repo#num` — one row per marker.
#
# SCOPED WITH § 2, because it IS § 2: auditing a marker means resolving its ref, so it is f(world)
# and decays the same way. A `closed-ok` whose issue REOPENS goes red with no commit — the identical
# time-bomb, one level down — and left tree-wide it would redden the innocent PRs the scope exists
# to protect, through the very mechanism we just closed. The sweep audits every marker.
#
# -H, and it is load-bearing: `grep -r <dir>` always prefixes the filename, but grep given a SINGLE
# file operand does not, and under `--changed` a one-file diff is the common case. Without it the
# row would parse as `<line>:<match>` and every marker in that file would silently stop excusing
# anything — turning a correct, marked citation red. `|| true` covers both grep's no-match 1 and
# xargs' 123, which `set -e` would otherwise take as fatal.
markers=$( { link_md_files | xargs -0 -r grep -HEon \
    '<!--[[:space:]]*skill-refs:[[:space:]]*closed-ok[[:space:]]+[A-Za-z0-9._/-]+#[0-9]+' \
    || true; } | while IFS= read -r m; do
    [[ -z $m ]] && continue
    mfile=${m%%:*}; mrest=${m#*:}; mline=${mrest%%:*}; mref=${mrest##* }
    [[ $mref == */* ]] || mref="$DEFAULT_OWNER/$mref"
    printf '%s\t%s\t%s\n' "$mfile" "$mline" "$mref"
  done)

# The prose-ok allowlist, normalised to `file<TAB>line<TAB>num` — one row per marker.
# WHOLE-TREE, pairing with § 3: no network, no decay, so nothing to scope away from.
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
echo "check-skill-refs: ok — $n_skills skills published; every [[ref]] resolves."

# The link half reports its SUBJECT, not just its verdict. "I found no stale links" and "I did not
# look at any" are different sentences, and a gate that prints one when it means the other is the
# `.github#416` shape this script exists to close. Under `--changed` the second is the NORMAL case —
# most diffs touch no skill — so it is the one that must never read as a clean bill of health.
case $link_mode in
  checked)
    if [[ $link_scope == diff ]]; then
      echo "check-skill-refs: ok — all $checked issue/PR link(s) in the $n_link_files skill body/bodies this diff touches are open or marked."
    else
      echo "check-skill-refs: ok — all $checked issue/PR link(s) in the tree are open or marked."
    fi
    ;;
  empty)
    if [[ $link_scope == diff ]]; then
      echo "check-skill-refs: link check N/A — this diff touches no published skill body, so it owns no"
      echo "  pointers and cannot be judged on them. Every link in the tree is swept on a schedule"
      echo "  (.github/workflows/skill-refs-sweep.yml) — that is where a link the WORLD broke surfaces."
    else
      echo "check-skill-refs: ok — no issue/PR links in the tree to check."
    fi
    ;;
  skipped)
    echo "check-skill-refs: NOTE — issue/PR links were NOT checked ($skip_reason)." >&2
    ;;
esac
if [[ -n $link_scope_note ]]; then
  echo "check-skill-refs: link scope — $link_scope_note."
fi

# Said out loud even at zero. § 3 is the half that still runs when the link half is skipped, so a
# silent pass here is indistinguishable from a check that did not happen — and "I found nothing" and
# "I did not look" being the same output is the defect this gate keeps being extended to kill.
if ((n_bare > 0)); then
  echo "check-skill-refs: ok — $n_bare bare #N ref(s); every one is marked prose-ok."
else
  echo "check-skill-refs: ok — no bare #N refs."
fi
