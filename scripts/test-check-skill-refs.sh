#!/usr/bin/env bash
# test-check-skill-refs — behavioural tests for scripts/check-skill-refs.sh (FS.GG.Game#243).
#
# The subject is ~600 lines of load-bearing bash that gates every published skill body, and every
# defect it has ever had was found by a human reading it — which is precisely the property it was
# written to end for other things. #241 nearly shipped a one-flag bug (a missing `grep -H`) that
# would have reddened every correctly-marked citation on exactly the PRs that touch a skill, i.e.
# the ones where the gate is supposed to be trustworthy. It would have looked like the gate working.
#
# So these tests are BLACK-BOX: fixture tree in, exit code + output out. Nothing reaches inside the
# script, because the promise being tested is the one the gate makes to its callers — above all the
# one it makes when it says nothing:
#
#     "I will not report green over a subject I did not examine."
#
# Every assertion here is either a verdict (exit code) or a SUBJECT claim (what it says it looked
# at). The second kind is the point: a gate that passes because it checked nothing passes just as
# quietly as one that checked everything.
#
# THE FIXTURE IS A REAL REPO. The subject `cd`s to its own parent, shells out to `git` for
# `--changed`, and calls `gh` for link state — so each fixture is a throwaway git repo holding a
# copy of the script, plus a fake `gh` on PATH whose answers come from a per-fixture state file.
# Nothing here touches the network or the real board.
#
# The counters, the assertions, the fixture builder and that fake `gh` are SHARED with
# scripts/test-skill-refs-sweep.sh — see scripts/lib/test-harness.sh (FS.GG.Game#259). What is local
# to this file is the git-repo fixture and `run`, because the subject here is a script rather than a
# workflow step, and that is the part the two suites do not have in common.
#
# GITHUB_ACTIONS IS CONTROLLED, NEVER INHERITED. The subject's behaviour genuinely forks on it
# (SKILL_REFS_SKIP_LINKS is ignored in CI; an unreadable link is fatal in CI and a note locally),
# so a test that let it leak in from the environment would assert one thing on a laptop and a
# different thing in the very CI run that is supposed to be gating it. `run` scrubs it and each
# test opts in explicitly.
#
# Usage: scripts/test-check-skill-refs.sh [-v]
set -uo pipefail

VERBOSE=0
[[ ${1:-} == -v ]] && VERBOSE=1

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SUT="$REPO_ROOT/scripts/check-skill-refs.sh"
[[ -f $SUT ]] || { echo "test-check-skill-refs: cannot find $SUT" >&2; exit 2; }

# `source=` is resolved against shellcheck's source path, which defaults to the CWD — NOT to this
# script. Without the SCRIPTDIR below, `lib/test-harness.sh` names nothing from the repo root, the
# source is silently NOT followed (SC1091, an `info` that a `-S warning` floor never prints), and
# every variable this file sets FOR the harness — RC, read only by `expect_rc` — looks unused. That
# is where the three SC2034s in #266 came from: not dead code, an unread `source`.
# shellcheck source-path=SCRIPTDIR
# shellcheck source=lib/test-harness.sh
. "$REPO_ROOT/scripts/lib/test-harness.sh"
harness_init

# ── fixture ─────────────────────────────────────────────────────────────────────────────────────

# The shared tree (skill root, fake `gh`, its state files), plus the one thing that is ours: a copy
# of the subject, which the git helpers below then commit into a throwaway repo.
fixture() {
  fixture_new
  cp "$SUT" "$FIX/scripts/check-skill-refs.sh"
  chmod +x "$FIX/scripts/check-skill-refs.sh"
}

git_init() {
  git -C "$FIX" init -q -b main
  git -C "$FIX" config user.email test@example.invalid
  git -C "$FIX" config user.name 'skill-refs tests'
  # The harness's plumbing is not part of the repo under test, and `git add -A` cannot tell the
  # difference. `gh.log` is the one that bites: the fake `gh` APPENDS to it on every call, so a
  # fixture that tracked it would carry a file that mutates while the subject runs — and a case
  # that committed after a `run` would find the harness's own call log inside the diff it is
  # asserting on. Excluded before the first `add`, so none of it is ever tracked.
  printf '%s\n' bin/ gh.log gh-state gh-bodies/ trackers.json unlabelled.json \
    >"$FIX/.git/info/exclude"
  git -C "$FIX" add -A
  git -C "$FIX" commit -qm 'fixture base'
}
git_commit() { git -C "$FIX" add -A && git -C "$FIX" commit -qm "${1:-change}"; }
git_head()   { git -C "$FIX" rev-parse HEAD; }

# A commit that EXISTS but shares no ancestry with HEAD — `rev-parse --verify` says yes and
# `git diff base...HEAD` then dies with `fatal: no merge base`. This is the shape that crashed the
# gate job red on an innocent PR, so the fallback must recognise it and sweep, not explode.
#
# Built with plumbing (an empty tree, committed with no parent) rather than `checkout --orphan`,
# and that is not fussiness: the porcelain route leaves the tracked files untracked, so the
# `checkout` BACK to main fails — "untracked working tree files would be overwritten" — and HEAD
# silently stays on the orphan branch. `merge-base` then compares the commit with ITSELF and
# happily succeeds, so the fixture quietly stops being unrelated and the test passes for the wrong
# reason. commit-tree touches no branch, no index, and no working tree.
git_orphan() {
  local empty; empty=$(git -C "$FIX" mktree </dev/null)
  git -C "$FIX" commit-tree "$empty" -m 'unrelated root'
}

# run [args…] — env knobs: CI_MODE=1, SKIP_LINKS=1, GH_NOAUTH=1
run() {
  local -a e=(env -u GITHUB_ACTIONS -u SKILL_REFS_SKIP_LINKS -u GH_NOAUTH) base
  mapfile -t base < <(gh_env); e+=("${base[@]}")
  [[ ${CI_MODE:-0}   == 1 ]] && e+=("GITHUB_ACTIONS=true")
  [[ ${SKIP_LINKS:-0} == 1 ]] && e+=("SKILL_REFS_SKIP_LINKS=1")
  [[ ${GH_NOAUTH:-0}  == 1 ]] && e+=("GH_NOAUTH=1")
  OUT=$("${e[@]}" "$FIX/scripts/check-skill-refs.sh" "$@" 2>&1)
  RC=$?
  ((VERBOSE)) && { printf '    ── %s\n' "$*"; printf '%s\n' "$OUT" | sed 's/^/    │ /'; }
  return 0
}

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 1  WIKI REFS  — f(tree), hermetic
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§1 a bare [[ref]] to a published skill, and a qualified FOREIGN ref, both resolve'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-beta]] for the other half, and [[fs-gg-rendering:fs-gg-scene]] for the scene graph.
MD
skill fs-gg-beta <<'MD'
# beta
MD
run
expect_rc 0 'clean tree passes'
expect_out_has 'every [[ref]] resolves' 'says the wiki half resolved'

case_start '§1 a bare [[ref]] to a skill this repo does NOT publish fails'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-nowhere]].
MD
run
expect_rc 1 'dangling bare ref fails'
expect_out_has 'dangling [[fs-gg-nowhere]]' 'names the dangling ref'
expect_out_has 'qualify it as' 'tells the author how to fix it'

# The mirror-safe half of FS.GG.Game#273. This case asserted the OPPOSITE until then — "write it
# bare" — and that rejection is precisely what made the convention unmirrorable: it is the reason no
# byte sequence could be green in both this repo and the one ADR-0022 §6 mirrors these bodies into.
# A self-qualified ref that RESOLVES is true, so it passes. See §1's header in the subject.
case_start '§1 a SELF-qualified ref to a skill we DO publish is accepted — it is what makes a body mirrorable'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-game:fs-gg-beta]].
MD
skill fs-gg-beta <<'MD'
# beta
MD
run
expect_rc 0 'self-qualified ref to a published skill passes'

# ...but only because it RESOLVES. The dangling case it used to be conflated with must still fail,
# and this is the assertion that keeps the widening honest: drop the is_published test and this goes
# green, silently trusting a self-qualified ref to a skill that does not exist.
case_start '§1 a SELF-qualified ref to a skill we do NOT publish still fails'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-game:fs-gg-nonesuch]].
MD
run
expect_rc 1 'self-qualified ref to an unpublished skill fails'
expect_out_has 'does not publish' 'names it as unpublished, not as a style error'

# ── THE MIRROR INVARIANT (FS.GG.Game#273) ───────────────────────────────────────────────────────
#
# The two cases below are the ones the old rule could not both satisfy, and they are why it changed.
# ADR-0022 §6 mirrors four of our bodies into FS.GG.Rendering BYTE-IDENTICALLY, and both repos gate
# them with a copy of this subject. The only thing that differs between the two runs is WHO IS
# READING — SELF_OWNER, and the publish set. So the invariant is not "this ref is well-formed"; it is
# "this ref gets the same verdict from both readers", and nothing could test that until the test
# could pose as both.
#
# `as_repo` rewrites the two constants that ARE the subject's identity, in the COPY of it that
# `fixture` just made. That is not reaching inside the script's logic — it is standing the fixture up
# as the OTHER repo's checkout, which is precisely what Rendering's copy of this file is.
#
# Both constants, not just SELF_OWNER: §3 quotes SELF_REPO when it suggests a qualification for a bare
# `#N`, so a fixture that changed only the owner would pose as Rendering while still calling itself
# FS.GG.Game — and the next person to add a `#N` case under `as_repo` would be debugging that instead
# of their test.
as_repo() {
  local owner=$1 repo=$2
  sed -i -e "s/^SELF_OWNER=.*/SELF_OWNER=\"$owner\"/" \
         -e "s/^SELF_REPO=.*/SELF_REPO=\"$repo\"/" \
         "$FIX/scripts/check-skill-refs.sh"
}

# One body. The SAME BYTES are fed to both readers below — if you edit it, edit it once, here. Both
# directions are qualified: a ref to a skill we own, and a ref to one Rendering owns.
#
# `mirror` is what puts it IN the mirror set, and since FS.GG.Game#280 that is a fact the subject
# reads out of the fixture's producer manifest rather than one it carries. The old subject held a
# hardcoded MIRRORED_SKILLS list, so `fs-gg-game-core` was mirrored HERE by the coincidence of being
# named in the script under test — a fixture could not have said otherwise, and no test could reach
# the rule for any other body. Now the fixture declares it, which is why the cases below can mirror
# fs-gg-physics (they could not before) and prove the set is data.
mirrored_body() {
  skill fs-gg-game-core <<'MD'
# game-core
Ballistics: [[fs-gg-game:fs-gg-ballistics]]. Scene: [[fs-gg-rendering:fs-gg-scene]].
MD
  mirror fs-gg-game-core
}

case_start '§1 mirror invariant: the qualified body is green in FS.GG.Game — it owns ballistics, and scene is foreign'
fixture
mirrored_body
skill fs-gg-ballistics <<'MD'
# ballistics
MD
run
expect_rc 0 'green for the repo that owns ballistics'

case_start '§1 mirror invariant: THE SAME BYTES are green in FS.GG.Rendering — it owns scene, and ballistics is foreign'
fixture
as_repo fs-gg-rendering FS.GG.Rendering
mirrored_body
skill fs-gg-scene <<'MD'
# scene
MD
run
# Under the rule this replaced, THIS is the assertion that failed: [[fs-gg-rendering:fs-gg-scene]]
# was refused as self-qualified ("write it bare"), while writing it bare made it dangle in Game. No
# byte sequence satisfied both readers. That is the whole of FS.GG.Game#273, in one case.
expect_rc 0 'green for the repo that owns scene — the same bytes, the other reader'

# ── and the guard that stops the invariant from rotting ─────────────────────────────────────────
#
# Qualifying the four bodies once fixes today. The next bare ref added to one of them restores the
# contradiction — and RESOLVES here, so without this rule the gate calls it green while it dangles in
# the mirror. Both gates green over a broken subject, which is the one thing this script may not do.
case_start '§1 a bare ref in a MIRRORED body fails even though it RESOLVES here — it dangles in the mirror'
fixture
skill fs-gg-game-core <<'MD'
# game-core
See [[fs-gg-ballistics]].
MD
mirror fs-gg-game-core
skill fs-gg-ballistics <<'MD'
# ballistics
MD
run
expect_rc 1 'bare ref in a mirrored body fails'
expect_out_has 'MIRRORED' 'says the body is mirrored'
expect_out_has 'fs-gg-game:fs-gg-ballistics' 'prints the qualified spelling to use'

# ── THE MIRROR SET IS DATA (FS.GG.Game#280) ─────────────────────────────────────────────────────
#
# The rule above was only ever reachable for the four bodies named in the subject's own hardcoded
# MIRRORED_SKILLS list. THIS is the case that could not be written before, and it is the one the item
# was filed about: a FIFTH body gets mirrored, and the guard must arm itself — with no edit to the
# subject, which is exactly what nobody remembered to do.
#
# The two cases are the same bytes in the same body, and the ONLY difference is the manifest row. If
# the subject ever goes back to carrying the set, the first goes green and this pair fails loudly.
case_start '§1/#280 a body mirrored in FUTURE is guarded the moment the MANIFEST says so — no edit to the subject'
fixture
skill fs-gg-physics <<'MD'
# physics
See [[fs-gg-ballistics]].
MD
mirror fs-gg-physics
skill fs-gg-ballistics <<'MD'
# ballistics
MD
run
expect_rc 1 'the fifth mirrored body is guarded too — the set came from the manifest'
expect_out_has 'MIRRORED' 'and it is named as a mirror failure, not a dangling ref'

case_start '§1/#280 …and the SAME bytes are green while the manifest does not mirror it — the row is what decides'
fixture
skill fs-gg-physics <<'MD'
# physics
See [[fs-gg-ballistics]].
MD
skill fs-gg-ballistics <<'MD'
# ballistics
MD
run
expect_rc 0 'not mirrored → bare resolves → green, on identical bytes'

# The converse, and it is the one that keeps the rule from being a blanket ban: a body nobody mirrors
# has exactly one reader, that reader can resolve a bare ref, and bare stays correct there.
case_start '§1 the SAME bare ref in a NON-mirrored body is still fine — bare is not deprecated'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-ballistics]].
MD
skill fs-gg-ballistics <<'MD'
# ballistics
MD
run
expect_rc 0 'bare ref in a non-mirrored body passes'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 0  THE MIRROR VERDICT IS MANDATORY  (FS.GG.Game#280)
# ════════════════════════════════════════════════════════════════════════════════════════════════
#
# Reading the set from data replaces one way of being wrong with another, unless the subject refuses
# to GUESS. There is exactly one plausible guess — "not mirrored", the majority answer — and it is
# the old hardcoded list's bug wearing a data structure: the body nobody classified is, by
# construction, the body nobody thought about, which is precisely the body someone just mirrored. The
# single case where the default is wrong is the case that exercises it, and it fails silently.
#
# So these cases assert the subject's refusal, and they are the ones that keep #280 from being a
# relocation. Delete the §0 check and they go green while the gate quietly waves the next mirrored
# body through.

# The manifest as it was BEFORE #280 introduced the field: every row present, not one of them stating
# a verdict. Not a hypothetical — it is on disk in every checkout older than this change, and it is
# what a bad merge or a hand-edit reproduces.
manifest_before_280() {
  local m="$FIX/template/skill-manifest/skill-manifest.json"
  jq 'del(.skills[].mirrored)' "$m" >"$m.tmp" && mv "$m.tmp" "$m"
}

case_start '§0 a published body with NO manifest row is RED — the gate will not guess "not mirrored"'
fixture
skill fs-gg-alpha <<'MD'
# alpha
MD
unclassify fs-gg-alpha
run
expect_rc 1 'an unclassified body fails rather than defaulting'
expect_out_has 'NO mirror verdict' 'names the missing verdict'
expect_out_has 'will not assume' 'and says it is refusing to guess, not that the refs are bad'
expect_out_has 'generate-skill-manifest.fsx' 'points at the catalog that has to answer'

# A row that EXISTS but does not SAY. This is what every row in the manifest looked like before #280
# added the field, so it is the shape a stale artifact, a bad merge, or a hand-edit produces — and it
# is the one that nearly shipped: `select(.mirrored == true)` reads a missing field as FALSE, because
# `null == true` is false. A gate that stopped there would answer "not mirrored" for all twelve
# bodies, confidently, and leave the four Rendering really does ship byte-identically UNGUARDED —
# #280's own bug, walking back in through the data that replaced the list.
#
# The bare ref below is what gives this case teeth: under that reading it RESOLVES, so the run goes
# green. It must not.
case_start '§0 a row with no `mirrored` field does NOT mean "not mirrored" — a stale manifest is unclassified'
fixture
skill fs-gg-game-core <<'MD'
# game-core
See [[fs-gg-ballistics]].
MD
mirror fs-gg-game-core
skill fs-gg-ballistics <<'MD'
# ballistics
MD
manifest_before_280   # del(.skills[].mirrored) — every row present, none of them saying
run
expect_rc 1 'a verdict-less row is unclassified, not silently unmirrored'
expect_out_has 'NO mirror verdict' 'names the verdict as the thing that is missing'
expect_out_hasnt 'every [[ref]] resolves' 'and does not wave the bare ref through'

# The subject reads the mirror set from ONE file. If it cannot read that file it knows nothing about
# ANY body — so a green here would be "I examined 12 bodies and found nothing wrong" spoken by a gate
# that examined none. `expect_rc 2`, not 1: this is not a bad pointer, it is a subject the gate could
# not judge, and the sweep tells those two apart (FS.GG.Game#277).
case_start '§0 a MISSING manifest is exit 2 — "I could not look" is not "I found nothing"'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-alpha]].
MD
rm -f "$FIX/template/skill-manifest/skill-manifest.json"
run
expect_rc 2 'no manifest → exit 2, not a green and not a ref failure'
expect_out_has 'cannot tell which bodies' 'says what it could not determine'
expect_out_hasnt 'every [[ref]] resolves' 'and NEVER claims the refs resolved'

case_start '§1 a ref qualified with an owner outside the registry vocabulary fails'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-bogus:fs-gg-scene]].
MD
run
expect_rc 1 'unknown owner fails'
expect_out_has "unknown owner 'fs-gg-bogus'" 'names the unknown owner'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 2  ISSUE / PR LINKS  — f(tree, world)
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§2 an OPEN issue link passes; a CLOSED one fails'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
issue 'FS-GG/FS.GG.Rendering#100' open
run
expect_rc 0 'open link passes'
expect_out_has 'issue/PR link(s) in the tree are open or marked' 'states what it resolved'

fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
issue 'FS-GG/FS.GG.Rendering#100' closed
run
expect_rc 1 'closed link fails'
expect_out_has 'stale link' 'calls it a stale link'
expect_out_has 'closed-ok' 'offers the marker as the opt-out'

case_start '§2 a CLOSED link with a matching closed-ok marker passes'
fixture
skill fs-gg-alpha <<'MD'
# alpha
<!-- skill-refs: closed-ok FS.GG.Rendering#245 — cited as the issue that wired the seam -->
The scaffold ships it wired (FS.GG.Rendering#245).
MD
issue 'FS-GG/FS.GG.Rendering#245' closed
run
expect_rc 0 'marked history citation passes'

case_start '§2 a marker that excuses NOTHING is reported as dead config'
fixture
skill fs-gg-alpha <<'MD'
# alpha
<!-- skill-refs: closed-ok FS.GG.Rendering#999 — the sentence this guarded is long gone -->
Nothing here links anywhere.
MD
issue 'FS-GG/FS.GG.Rendering#999' closed
run
expect_rc 1 'orphan marker fails'
expect_out_has 'stale closed-ok marker' 'calls out the dead marker'
expect_out_has 'nothing in this file links to' 'says why it is dead'

case_start '§2 a marker whose issue REOPENED is reported'
fixture
skill fs-gg-alpha <<'MD'
# alpha
<!-- skill-refs: closed-ok FS.GG.Rendering#245 — history -->
The scaffold ships it wired (FS.GG.Rendering#245).
MD
issue 'FS-GG/FS.GG.Rendering#245' open
run
expect_rc 1 'reopened issue defeats its marker'
expect_out_has 'is OPEN again; drop the marker' 'tells the author to drop it'

case_start '§2 a link to an issue that does not exist fails as dangling'
fixture
skill fs-gg-alpha <<'MD'
# alpha
See FS.GG.Rendering#4242.
MD
run   # unregistered → the fake gh 404s it
expect_rc 1 'missing issue fails'
expect_out_has 'dangling link' 'calls it dangling'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 3  BARE #N  — f(tree), hermetic, no network
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§3 a bare #N is rejected outright'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Track it in #4242.
MD
run
expect_rc 1 'bare ref fails'
expect_out_has 'bare ref' 'calls it a bare ref'
expect_out_has 'FS.GG.Game#4242' 'suggests the qualified form'

case_start '§3 a bare #N with a matching prose-ok marker passes'
fixture
skill fs-gg-alpha <<'MD'
# alpha
<!-- skill-refs: prose-ok #4242 — the design doc's number-one bug, not an issue -->
The design doc's "#4242 LOS bug" is the one to read first.
MD
run
expect_rc 0 'prose-ok excuses the bare ref'
expect_out_has 'bare #N ref(s); every one is marked prose-ok' 'states the subject it excused'

case_start '§3 a prose-ok marker that excuses nothing is dead config'
fixture
skill fs-gg-alpha <<'MD'
# alpha
<!-- skill-refs: prose-ok #4242 — nothing writes this any more -->
Clean prose.
MD
run
expect_rc 1 'orphan prose-ok fails'
expect_out_has 'stale prose-ok marker' 'calls out the dead prose marker'

case_start '§3 a CSS colour is not an issue ref, and neither is a labelled markdown link'
fixture
skill fs-gg-alpha <<'MD'
# alpha
The accent colour is #1a2b3c and the background is #abc123.
See [#587](https://github.com/FS-GG/FS.GG.Rendering/issues/587) for the rationale,
and [see #459](https://github.com/FS-GG/FS.GG.Rendering/issues/459) too.
A heading marker like ## 3 is not a ref either.
MD
issue 'FS-GG/FS.GG.Rendering#587' open
issue 'FS-GG/FS.GG.Rendering#459' open
run
expect_rc 0 'colours and labelled links do not fire §3'
expect_out_has 'no bare #N refs' 'reports zero bare refs, out loud'
expect_out_hasnt '1a2b3c' 'never mentions the colour'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# THE #241 NEAR-MISS  — closed-ok markers must survive a SINGLE-FILE --changed scope
# ════════════════════════════════════════════════════════════════════════════════════════════════
#
# `grep -r <dir>` prefixes the filename; `grep <file>` given ONE file operand does not. Under
# `--changed` a one-file diff is the COMMON case, so without `-H` every marker row parses as
# `<line>:<match>` instead of `<file>:<line>:<match>`, `is_excused` matches nothing, and every
# correct, deliberately-marked citation goes red — on exactly the PRs that touch a skill.
#
# This is the test that would have caught it. It is a `--changed` run whose diff touches exactly
# one skill body, and that body's closed link is legitimately marked.

case_start '#241 regression: a marked closed link survives a ONE-FILE --changed scope'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Nothing yet.
MD
skill fs-gg-beta <<'MD'
# beta
MD
git_init
BASE=$(git_head)
skill fs-gg-alpha <<'MD'
# alpha
<!-- skill-refs: closed-ok FS.GG.Rendering#245 — cited as the issue that wired the seam -->
The scaffold ships it wired (FS.GG.Rendering#245).
MD
git_commit 'touch exactly one skill'
issue 'FS-GG/FS.GG.Rendering#245' closed
run --changed "$BASE"
expect_rc 0 'the marker still excuses its link when grep sees a single file'
expect_out_hasnt 'stale link' 'the marked citation is not reported as stale'
expect_out_hasnt 'stale closed-ok marker' 'the marker is not reported as dead'
expect_out_has 'skill body/bodies this diff touches' 'names the scoped subject'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# SCOPE  (#238) — the link half may only judge what the diff touches…
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start 'scope: a diff touching NO skill cannot be reddened by a link that rotted elsewhere'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
mkdir -p "$FIX/src"
echo 'let x = 1' >"$FIX/src/thing.fs"
git_init
BASE=$(git_head)
echo 'let x = 2' >"$FIX/src/thing.fs"        # a diff that touches no skill body
git_commit 'unrelated change'
issue 'FS-GG/FS.GG.Rendering#100' closed     # …and the world moved under an untouched skill
run --changed "$BASE"
expect_rc 0 'the innocent diff is not reddened'
expect_out_has 'link check N/A' 'says the link half judged nothing'
expect_out_has 'swept on a schedule' 'points at where that rot DOES surface'
expect_out_hasnt 'stale link' 'does not report the untouched stale link'

case_start 'scope: …but a diff that DOES touch a skill still owns that skill'"'"'s links'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Nothing yet.
MD
git_init
BASE=$(git_head)
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
git_commit 'introduce a stale link'
issue 'FS-GG/FS.GG.Rendering#100' closed
run --changed "$BASE"
expect_rc 1 'scoping did not neuter the gate'
expect_out_has 'stale link' 'still catches the rot the diff introduced'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# FALLBACK — degrade toward MORE checking, never less
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start 'fallback: an all-zeros base (first push) sweeps the tree rather than checking nothing'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
git_init
issue 'FS-GG/FS.GG.Rendering#100' closed
run --changed 0000000000000000000000000000000000000000
expect_rc 1 'the fallback SWEPT — it found the stale link it could have skipped'
expect_out_has 'does not resolve here' 'names the unusable base'
expect_out_has 'Falling back to the FULL link sweep' 'announces the fallback, loudly'
# The `link scope — …` summary line is NOT asserted here: on a failing run the subject prints its
# failure block and exits before reaching the summary. The fallback is still announced up front
# (above), which is the disclosure that matters. The summary wording is pinned in the clean-tree
# fallback case below, which is the run that actually reaches it.

case_start 'fallback: an UNRELATED-HISTORY base sweeps instead of dying with `fatal: no merge base`'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
git_init
ORPHAN=$(git_orphan)
issue 'FS-GG/FS.GG.Rendering#100' closed
run --changed "$ORPHAN"
expect_rc 1 'exits 1 on the stale link it swept up — NOT 128 from a raw git fatal'
expect_out_has 'shares no history with HEAD' 'diagnoses the real problem'
expect_out_has 'Falling back to the FULL link sweep' 'announces the fallback'
# Not `expect_out_hasnt 'no merge base'`: the subject's OWN diagnosis says "there is no merge base, so
# there is no diff to take against it", which is the sentence we want. What must never appear is
# git's raw plumbing error leaking through as the gate's verdict.
expect_out_hasnt 'fatal:' 'never leaks git'"'"'s raw fatal into the gate output'

case_start 'fallback: an unusable base over a CLEAN tree still passes, and still says what it did'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
git_init
issue 'FS-GG/FS.GG.Rendering#100' open
run --changed 0000000000000000000000000000000000000000
expect_rc 0 'clean tree under a bad base passes'
expect_out_has 'Falling back to the FULL link sweep' 'still announces the fallback'
expect_out_has 'swept the whole tree instead' 'the summary restates the scope, so a green run cannot be mistaken for a scoped one'
expect_out_hasnt 'link check N/A' 'a fallback is never reported as "nothing to judge"'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# NEVER SELF-SKIP — the promise that matters most
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start 'CI + unauthenticated gh → FAIL, never a quiet pass'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
issue 'FS-GG/FS.GG.Rendering#100' open
CI_MODE=1 GH_NOAUTH=1 run
expect_rc 1 'a link it cannot read is not a link it may call green'
expect_out_has 'no authenticated' 'says why it failed'
expect_out_hasnt 'ok — all' 'does not claim it resolved anything'

case_start 'CI ignores SKILL_REFS_SKIP_LINKS — the skip is a local convenience, not a CI escape'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
issue 'FS-GG/FS.GG.Rendering#100' closed
CI_MODE=1 SKIP_LINKS=1 run
expect_rc 1 'the stale link is still caught in CI'
expect_out_has 'stale link' 'the link half ran anyway'

case_start 'locally, SKILL_REFS_SKIP_LINKS skips the link half — and SAYS it skipped it'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Add your case at FS.GG.Rendering#100.
MD
issue 'FS-GG/FS.GG.Rendering#100' closed
SKIP_LINKS=1 run
expect_rc 0 'the skipped half cannot fail the run'
expect_out_has 'were NOT checked' 'a skipped subject is announced, never silently passed'
expect_out_has 'SKILL_REFS_SKIP_LINKS is set' 'names the reason it skipped'
expect_out_hasnt 'stale link' 'the link half really was skipped — the stale link went unreported'

case_start 'locally, the hermetic §3 still gates with the link half skipped — it needs no network'
fixture
skill fs-gg-alpha <<'MD'
# alpha
Track it in #4242.
MD
SKIP_LINKS=1 run
expect_rc 1 'a bare ref is caught with no network at all'
expect_out_has 'bare ref' '§3 fired offline'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# ARGUMENT HANDLING
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start 'args: --changed with no base is a usage error, not a silent full sweep'
fixture
skill fs-gg-alpha <<'MD'
# alpha
MD
run --changed
expect_rc 2 'refuses a --changed with no ref'
expect_out_has 'needs a base ref' 'says what is missing'

fixture
skill fs-gg-alpha <<'MD'
# alpha
MD
run --nonsense
expect_rc 2 'refuses an unknown argument'

# ── summary ─────────────────────────────────────────────────────────────────────────────────────
harness_summary test-check-skill-refs
