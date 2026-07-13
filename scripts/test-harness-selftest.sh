#!/usr/bin/env bash
# test-harness-selftest — behavioural tests for scripts/lib/test-harness.sh (FS.GG.Game#264).
#
# THE SUBJECT IS THE FLOOR UNDER BOTH SHELL SUITES. #259 extracted the scaffolding that
# scripts/test-check-skill-refs.sh (the GATE, #243) and scripts/test-skill-refs-sweep.sh (the SWEEP,
# #251) had each grown their own copy of: the counters, the `ok`/`bad`/`expect_*` family, the counted
# fixture builder, and one faithful fake `gh`. That was the right move, and it moved the blast radius:
# before it, a bug in the gate suite's scaffolding broke the gate suite. Now a bug in the harness
# breaks BOTH — and the dangerous direction is not "both go red", which is loud. It is BOTH GO GREEN.
#
#   `expect_has` that returns ok unconditionally. `ok` that increments `n_pass` without a comparison
#   behind it. `fixture_new` that stops writing `bin/gh`, so the fake is never on PATH and every
#   subject silently talks to nothing.
#
# Any one of those makes 169 assertions pass over a subject nobody examined — the FS-GG/.github#416
# shape that both of the suites above exist to refuse, now sitting one level BENEATH both of them.
#
# ────────────────────────────────────────────────────────────────────────────────────────────────
# WHY THIS FILE DOES NOT USE THE HARNESS
#
# The obvious way to write this suite is to source the harness and assert with `expect_has`. That
# suite would be worthless, and it is worth being exact about why: it would be testing `expect_has`
# WITH `expect_has`. Open `ok` up so it passes unconditionally — the first defect this file exists to
# catch — and every assertion in such a suite passes too, including the ones asserting that the
# assertions can fail. It goes green, over a subject it did not examine, while reporting that it
# examined it. That is not a weaker test of the harness; it is a perfect instance of the exact bug.
#
# So this file brings its OWN assertions (`is_rc`/`says`/`says_not`, below). They are three lines
# each and share no code with the subject — deliberately dumber than what they check, because the
# thing checking the checker has to be small enough to audit by eye. The harness's assertions are
# not, which is precisely why they need a gate.
#
# And the harness is driven BLACK-BOX, as a subject: each case writes a PROBE — a real script that
# sources the harness exactly as a suite does, calls it, and exits through `harness_summary` — runs
# it in a child bash, and asserts on its EXIT CODE and OUTPUT. Nothing here reaches inside the
# harness's state; `n_pass` is never read, only the summary line it produces. That is the same
# discipline the two suites it serves already use on their own subjects.
#
# THE CENTRAL CASE IS THE FAILING ONE. For every assertion in the family, §3 runs it twice: once
# satisfied (the probe must end green, having counted exactly one pass) and once VIOLATED (the probe
# must end red, having counted exactly one failure). The second is the one with teeth. An assertion
# family that cannot fail is the whole defect, and it is invisible from the passing direction — which
# is the only direction the suites themselves ever exercise, since a suite in the green does not
# contain a violated assertion.
#
# Checking the COUNT, and not merely the exit code, is what catches the other half: an `ok` that
# increments `n_pass` twice, or a `bad` that forgets to increment `n_fail` and lets `harness_summary`
# exit 0 over a failure it printed.
#
# #259's own verification was a manual mutation pass — four probes, all caught. That was evidence the
# harness worked that day. It was not a gate, and nobody was ever going to run it again. This is.
#
# Usage: scripts/test-harness-selftest.sh [-v]
set -uo pipefail

VERBOSE=0
[[ ${1:-} == -v ]] && VERBOSE=1

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SUT="$REPO_ROOT/scripts/lib/test-harness.sh"
[[ -f $SUT ]] || { echo "test-harness-selftest: cannot find $SUT" >&2; exit 2; }
export SUT

# The harness itself refuses to run without `jq` (exit 2). So does its suite — for the same reason it
# gives: a suite that could not RUN is a different fact from a suite that ran and found a bug.
command -v jq >/dev/null || { echo "test-harness-selftest: jq is required" >&2; exit 2; }

PT=$(mktemp -d)
trap 'rm -rf "$PT"' EXIT

# ── our own assertions ──────────────────────────────────────────────────────────────────────────
#
# Not the harness's. See the header — this is the point of the file, not an oversight. Keep them
# dumb: no shared helper, no cleverness, nothing that could fail in the same way as the thing they
# are checking.

t_pass=0
t_fail=0
PRC=0    # the probe's exit code
POUT=""  # the probe's combined output

case_start() { printf '\n%s\n' "$1"; }

t_ok() { t_pass=$((t_pass + 1)); printf '  ok    %s\n' "$1"; }
t_bad() { # t_bad <what> <why>
  t_fail=$((t_fail + 1))
  printf '  FAIL  %s\n' "$1"
  printf '        %s\n' "$2"
  printf '        ── probe output (exit %s) ──\n' "$PRC"
  printf '%s\n' "$POUT" | sed 's/^/        │ /'
  printf '        ─────────────────────────────\n'
}

is_rc()    { if [[ $PRC == "$1" ]];         then t_ok "$2"; else t_bad "$2" "expected probe exit $1, got $PRC"; fi; }
says()     { if grep -qF -- "$1" <<<"$POUT"; then t_ok "$2"; else t_bad "$2" "probe output lacks: $1";          fi; }
says_not() { if grep -qF -- "$1" <<<"$POUT"; then t_bad "$2" "probe output must NOT contain: $1"; else t_ok "$2"; fi; }

# ── the probe ───────────────────────────────────────────────────────────────────────────────────
#
# A probe is a real suite, in miniature: it sources the harness the way the two real suites source it
# and then does one thing. Running it in a CHILD bash is what makes the black-box claim true — the
# harness `exit`s from `harness_summary`, sets an EXIT trap, and mutates globals, so a probe run in
# THIS shell would take the suite down with it on the very first case, and the failing direction (§3)
# is more than half the cases here.

n_probe=0

# PROBE_ENV — extra `VAR=val` for the NEXT probe's environment, then cleared. The harness reads two
# knobs that a suite sets BEFORE it sources the file (`HARNESS_OUTPUT_LABEL`) or before it calls the
# fake (`GH_NOAUTH`), and "before the source" is not a thing a probe body can express: the preamble
# has already sourced it. Handing them to the child's environment is the faithful reproduction —
# `${VAR:=default}` cannot tell an inherited value from an assigned one, which is exactly the property
# the suites rely on.
PROBE_ENV=()

# probe_raw — the probe body on stdin, run with the harness sourced and NOTHING else done for it.
# For the cases that are about `harness_init` and `fixture_new` themselves.
probe_raw() {
  local body; body=$(cat)
  n_probe=$((n_probe + 1))
  {
    cat <<'PRE'
#!/usr/bin/env bash
set -uo pipefail
# $SUT is exported by the suite. Sourced, exactly as scripts/test-*.sh source it.
# shellcheck source=lib/test-harness.sh
. "$SUT"

# The one thing the harness deliberately does NOT give a suite: a way to RUN its subject. For the
# fake-`gh` cases the subject IS `gh`, so this is that half — and it is three lines, because the fake
# is reached exactly the way a real suite reaches it: through `gh_env`, on PATH.
gh_run() { local -a e; mapfile -t e < <(gh_env); OUT=$(env "${e[@]}" gh "$@" 2>&1); RC=$?; }
PRE
    printf '%s\n' "$body"
  } >"$PT/probe-$n_probe.sh"

  POUT=$(env ${PROBE_ENV[@]+"${PROBE_ENV[@]}"} bash "$PT/probe-$n_probe.sh" 2>&1)
  PRC=$?
  PROBE_ENV=()   # one probe's env is never the next one's
  ((VERBOSE)) && { printf '    ── probe %d (exit %d)\n' "$n_probe" "$PRC"; printf '%s\n' "$POUT" | sed 's/^/    │ /'; }
  return 0
}

# probe — the common case: body on stdin, wrapped in the `harness_init` … `harness_summary` bookends
# every real suite has. The summary is what turns the counters into an exit code, so it is the thing
# most of these cases are actually reading.
#
# A HERE-STRING, never `printf … | probe_raw`. The pipe was the first version, and it was wrong in the
# way this whole file exists to catch: bash runs the right-hand side of a pipeline in a SUBSHELL, so
# `probe_raw`'s assignments to POUT/PRC happened in a child and were discarded. Every `probe` case
# then asserted against the PREVIOUS probe's output — which is to say, against a subject it had not
# run. It did not error; it just quietly stopped examining anything. The failures were loud here only
# because the stale output happened not to match. Had it matched, this suite would have gone green
# over nothing, one level above the harness doing exactly the same thing.
probe() {
  local body; body=$(cat)
  probe_raw <<<"harness_init
$body
harness_summary probe"
}

# The two shapes §3 is made of. Asserting the COUNT and not just the exit code is deliberate: an `ok`
# that double-counts, or a `bad` that prints FAIL and forgets to increment, both pass an exit-code-only
# check.
probe_pass() { # probe_pass <what> — body on stdin, holding exactly ONE satisfied assertion
  probe
  is_rc 0 "$1 — probe ends green"
  says 'probe: ok — 1 assertions passed' "$1 — counted exactly one pass"
}
probe_fail() { # probe_fail <what> — body on stdin, holding exactly ONE violated assertion
  probe
  is_rc 1 "$1 — probe ends RED"
  says 'probe: FAILED — 0 passed, 1 failed' "$1 — counted exactly one failure"
}

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 1  THE HARNESS IS A LIBRARY
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§1 the harness refuses to be EXECUTED — it is sourced, and says so'
POUT=$(bash "$SUT" 2>&1); PRC=$?
is_rc 2 'running the harness directly exits 2, not 1'
says 'sourced by a suite, not run directly' 'says what it is'

case_start '§1 `fixture_new` before `harness_init` is refused, not silently mis-rooted'
# Without TMPROOT the fixture would land somewhere unowned — or, with `mkdir -p "$FIX"` on an empty
# root, at the filesystem root. Exit 2 (a suite that could not RUN), never 1 (a suite that found a bug).
probe_raw <<'SH'
fixture_new
echo 'REACHED-THE-LINE-AFTER'
SH
is_rc 2 'refuses to build a fixture with no temp root'
says 'call harness_init before the first fixture' 'names the missing call'
says_not 'REACHED-THE-LINE-AFTER' 'and it really exits, rather than carrying on'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 2  THE COUNTERS COUNT
#
# `harness_summary` is the only thing in this system that turns assertions into an exit code, i.e.
# into a red CI check. Everything else in this file rests on it.
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§2 N calls to `ok` are N passes, and the run is green'
probe <<'SH'
ok 'first'
ok 'second'
ok 'third'
SH
is_rc 0 'an all-pass run exits 0'
says 'probe: ok — 3 assertions passed' 'reports the count it actually saw'
says_not 'FAILED' 'and does not report a failure it did not have'

case_start '§2 one `bad` reddens the run, whatever else passed'
probe <<'SH'
ok 'this one really passed'
bad 'this one did not' 'because of a reason'
SH
is_rc 1 'a run with any failure exits 1'
says 'probe: FAILED — 1 passed, 1 failed' 'reports BOTH counts, honestly'
says 'FAIL  this one did not' 'names the failing assertion'
says 'because of a reason' 'and prints why'

case_start '§2 a suite with NO assertions at all is not a pass to be proud of, but it is honest'
# 0 assertions is the shape a broken `fixture`/`run` produces when every case dies early. The summary
# must say `0`, so that a suite which examined nothing cannot read as a suite that examined everything.
probe <<'SH'
:
SH
is_rc 0 'no assertions, no failures, exit 0'
says 'probe: ok — 0 assertions passed' 'and it says ZERO, out loud'

case_start '§2 `bad` shows the captured subject output and exit code — the failure is diagnosable'
probe <<'SH'
OUT='the subject said this'
RC=3
bad 'a failing case' 'the reason'
SH
says 'the subject said this' 'dumps the captured output'
says '(exit 3)' 'and the captured exit code'
says 'subject output' 'labelled with the default noun'

case_start '§2 `HARNESS_OUTPUT_LABEL` renames that noun — the gate runs a SCRIPT, the sweep a STEP'
# Set BEFORE the harness is sourced, which is the only way it works and the way both suites do it
# (scripts/test-skill-refs-sweep.sh sets `step output` on its line 58, above its `.` line). Hence
# PROBE_ENV rather than a line in the body: by the time the body runs, the preamble has sourced the
# harness and `${HARNESS_OUTPUT_LABEL:=subject output}` has already fired. A body-assignment case
# would have to source the harness a SECOND time to take effect — and would then be testing
# re-sourcing, not the knob.
PROBE_ENV=(HARNESS_OUTPUT_LABEL='workflow step')
probe <<'SH'
OUT='x'; RC=1
bad 'a failing case' 'the reason'
SH
says '── workflow step (exit 1) ──' 'the label is a knob, honoured when set before the source'
says_not 'subject output' 'and the default does not leak through it'

case_start '§2 ...and the default is what you get when nobody sets it'
probe <<'SH'
OUT='x'; RC=1
bad 'a failing case' 'the reason'
SH
says '── subject output (exit 1) ──' 'unset means `subject output`, not empty'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 3  EVERY ASSERTION CAN FAIL
#
# THE HEART OF THIS FILE. Each assertion is exercised in both directions, and the violated direction
# is the one that matters: an `expect_has` that returns `ok` unconditionally passes every case in
# both real suites, because a suite in the green never violates an assertion. It is invisible from
# where the suites stand, and it silently converts 169 assertions into 169 statements about nothing.
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§3 expect_rc'
probe_pass 'satisfied expect_rc' <<'SH'
RC=0
expect_rc 0 'the subject exited 0'
SH
probe_fail 'VIOLATED expect_rc' <<'SH'
RC=1
expect_rc 0 'the subject exited 0'
SH
says 'expected exit 0, got 1' 'and names both codes'

case_start '§3 expect_eq'
probe_pass 'satisfied expect_eq' <<'SH'
expect_eq 'apple' 'apple' 'the values agree'
SH
probe_fail 'VIOLATED expect_eq' <<'SH'
expect_eq 'apple' 'orange' 'the values agree'
SH
says "expected 'orange', got 'apple'" 'and names both values'

case_start '§3 expect_has'
probe_pass 'satisfied expect_has' <<'SH'
expect_has 'needle' 'a haystack with a needle in it' 'the needle is there'
SH
probe_fail 'VIOLATED expect_has' <<'SH'
expect_has 'needle' 'a haystack with nothing in it' 'the needle is there'
SH
says 'text does not contain: needle' 'and names the missing substring'

case_start '§3 expect_has is a FIXED-string match — a regex metachar is not a wildcard'
# `grep -qF`. Without -F, `expect_has 'a.c'` would pass on `abc`, and every assertion carrying a `.`
# or a `[` — which is most of them, since they assert over prose and file paths — would quietly
# loosen into a pattern that matches more than the author wrote.
probe_fail 'expect_has does not treat `.` as a wildcard' <<'SH'
expect_has 'a.c' 'abc' 'literal, not a pattern'
SH

case_start '§3 expect_hasnt'
probe_pass 'satisfied expect_hasnt' <<'SH'
expect_hasnt 'needle' 'a haystack with nothing in it' 'the needle is absent'
SH
probe_fail 'VIOLATED expect_hasnt' <<'SH'
expect_hasnt 'needle' 'a haystack with a needle in it' 'the needle is absent'
SH
says 'text should NOT contain: needle' 'and says which direction it failed in'

case_start '§3 expect_re'
probe_pass 'satisfied expect_re' <<'SH'
expect_re '^ok: [0-9]+$' 'ok: 42' 'the line matches'
SH
probe_fail 'VIOLATED expect_re' <<'SH'
expect_re '^ok: [0-9]+$' 'ok: forty-two' 'the line matches'
SH
says 'no line matches /^ok: [0-9]+$/' 'and prints the pattern that did not match'

case_start '§3 expect_out_has / expect_out_hasnt read $OUT — the two-argument convenience'
probe_pass 'satisfied expect_out_has' <<'SH'
OUT='the subject said hello'
expect_out_has 'hello' 'the subject greeted us'
SH
probe_fail 'VIOLATED expect_out_has' <<'SH'
OUT='the subject said nothing'
expect_out_has 'hello' 'the subject greeted us'
SH
probe_pass 'satisfied expect_out_hasnt' <<'SH'
OUT='the subject said nothing'
expect_out_hasnt 'hello' 'the subject stayed quiet'
SH
probe_fail 'VIOLATED expect_out_hasnt' <<'SH'
OUT='the subject said hello'
expect_out_hasnt 'hello' 'the subject stayed quiet'
SH

case_start '§3 expect_out_has really tracks $OUT, rather than closing over a stale copy'
# The wrappers exist so the haystack stays visible at the call site. They must read $OUT AT CALL TIME:
# a suite sets it in `run`, once per case, and asserts several times after.
probe <<'SH'
OUT='first'
expect_out_has 'first' 'sees the first value'
OUT='second'
expect_out_has 'second' 'sees the second value, from the same wrapper'
expect_out_hasnt 'first' 'and the first value is gone'
SH
is_rc 0 'the wrappers follow $OUT'
says 'probe: ok — 3 assertions passed' 'all three read the value current at the call'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 4  THE `gh` LOG ASSERTIONS
#
# What the run ASKED THE API FOR. `gh_no_writes` is the load-bearing one: it is the whole promise of a
# `pull_request` run of the sweep — it renders, and it writes NOTHING. A `gh_no_writes` that cannot
# fail is a promise nobody is keeping.
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§4 gh_called / gh_not_called'
probe_pass 'satisfied gh_called' <<'SH'
fixture_new
gh_run api repos/o/r/issues -X GET -f labels=decay
gh_called GET 'repos/o/r/issues' 'the run read the issue list'
SH
probe_fail 'VIOLATED gh_called — the call was never made' <<'SH'
fixture_new
gh_called GET 'repos/o/r/issues' 'the run read the issue list'
SH
says 'no GET matching /repos/o/r/issues/ in the gh log' 'and says what it looked for'

probe_pass 'satisfied gh_not_called' <<'SH'
fixture_new
gh_not_called POST 'repos/o/r/issues' 'the run filed nothing'
SH
probe_fail 'VIOLATED gh_not_called — the call WAS made' <<'SH'
fixture_new
gh_run api repos/o/r/issues -f title=x
gh_not_called POST 'repos/o/r/issues' 'the run filed nothing'
SH
says 'unexpected POST matching /repos/o/r/issues/ in the gh log' 'and says which call it found'

case_start '§4 gh_called discriminates on METHOD — a POST does not satisfy a GET'
# The two suites assert `GET repos/…/issues` (the tracker lookup) and `POST repos/…/issues` (filing a
# tracker) against the SAME path. A `gh_called` that matched on path alone would conflate the lookup
# with the create — which is #249's footgun exactly, and the one this harness's derived method exists
# to expose.
probe_fail 'a POST to the path does not satisfy gh_called GET' <<'SH'
fixture_new
gh_run api repos/o/r/issues -f title=x
gh_called GET 'repos/o/r/issues' 'the run READ the issue list'
SH

case_start '§4 gh_no_writes'
probe_pass 'satisfied gh_no_writes — a read-only run' <<'SH'
fixture_new
gh_run api repos/o/r/issues -X GET -f labels=decay
gh_no_writes 'the pull_request run wrote nothing'
SH
probe_fail 'VIOLATED gh_no_writes — a POST slipped through' <<'SH'
fixture_new
gh_run api repos/o/r/issues -f title=x
gh_no_writes 'the pull_request run wrote nothing'
SH
says 'the run made writes it must not make' 'and shows the offending calls'
says 'POST' 'naming the method'

case_start '§4 gh_no_writes catches PATCH, PUT and DELETE too — not just POST'
# The sweep PATCHes a decay report onto an existing tracker. A `gh_no_writes` that only knew about
# POST would report green over exactly that.
probe_fail 'a PATCH is a write' <<'SH'
fixture_new
gh_run api repos/o/r/issues/5 -X PATCH -f body=x
gh_no_writes 'the pull_request run wrote nothing'
SH

case_start '§4 `bad` prints the gh call log when there is one, and no empty section when there is not'
probe <<'SH'
fixture_new
gh_run api repos/o/r/issues -X GET -f labels=decay
bad 'a failing case' 'the reason'
SH
says '── gh calls ──' 'the call log is there when the run touched the API'
says 'repos/o/r/issues' 'and it holds the call'

probe <<'SH'
fixture_new
bad 'a failing case' 'the reason'
SH
says_not '── gh calls ──' 'and no empty section when the run never called gh'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 5  THE FIXTURE IS REALLY BUILT
#
# `fixture_new` that stops writing `bin/gh` puts NO fake on PATH. Every subject then talks to nothing
# — or, far worse on a developer's laptop, to the REAL `gh`, and thus to the real board.
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§5 the fake `gh` is on PATH, and it is executable'
probe_raw <<'SH'
harness_init
fixture_new
[[ -f $FIX/bin/gh ]] && echo 'GH-FILE=yes' || echo 'GH-FILE=no'
[[ -x $FIX/bin/gh ]] && echo 'GH-EXEC=yes' || echo 'GH-EXEC=no'
# ...and it is the one PATH really finds. `gh_env` puts $FIX/bin FIRST for a reason: on a developer's
# laptop the REAL `gh` is also on PATH, and a fixture that lost this race would run the suite against
# the real board — with a fake `gh.log` that stayed empty, so §4 would report green over it.
declare -a e; mapfile -t e < <(gh_env)
res=$(env "${e[@]}" bash -c 'command -v gh')
[[ $res == "$FIX/bin/gh" ]] && echo 'GH-RESOLVED=the-fake' || echo "GH-RESOLVED=$res"
SH
says 'GH-FILE=yes' 'fixture_new writes bin/gh'
says 'GH-EXEC=yes' 'and chmods it +x — a fake that is not executable is not on PATH at all'
says 'GH-RESOLVED=the-fake' 'and `gh` resolves to the fake, ahead of any real `gh` on the machine'

case_start '§5 the state files the fake answers out of all exist'
probe_raw <<'SH'
harness_init
fixture_new
for f in gh.log gh-state trackers.json unlabelled.json; do
  [[ -f $FIX/$f ]] && echo "FILE=$f" || echo "MISSING=$f"
done
for d in bin scripts gh-bodies template/product-skills; do
  [[ -d $FIX/$d ]] && echo "DIR=$d" || echo "MISSING=$d"
done
# The two JSON files must be VALID EMPTY ARRAYS, not empty files: the fake feeds them to `jq`, and an
# empty file is a parse error rather than "no trackers yet".
echo "TRACKERS=$(jq -c . <"$FIX/trackers.json")"
echo "UNLABELLED=$(jq -c . <"$FIX/unlabelled.json")"
SH
says 'FILE=gh.log' 'gh.log exists — the call log the whole §4 family reads'
says 'FILE=gh-state' 'gh-state exists'
says 'DIR=bin' 'bin/ exists'
says 'DIR=scripts' 'scripts/ exists — where a suite installs its subject'
says 'DIR=gh-bodies' 'gh-bodies/ exists — where the fake records what was PUT on the wire'
says 'DIR=template/product-skills' 'the skill root exists'
says_not 'MISSING=' 'nothing is missing'
says 'TRACKERS=[]' 'trackers.json is a valid empty array, not an empty file jq would choke on'
says 'UNLABELLED=[]' 'and so is unlabelled.json'

case_start '§5 each fixture is FRESH, and counted — the $RANDOM collision reasoning, pinned'
# The comment in the harness explains why this is counted rather than $RANDOM-suffixed: $$ is fixed
# within a run, so $RANDOM was the only entropy — a 1-in-32768 draw taken ~20 times is a ~0.6%
# birthday collision every run, or one run in ~170. `mkdir -p` would then hand the second fixture the
# FIRST one's tree, and a case would inherit a skill body it never wrote and pass or fail on it.
# A flake in a suite whose entire product is trustworthiness is worse than no suite at all.
probe_raw <<'SH'
harness_init
fixture_new
a=$FIX
echo 'I was here' >"$FIX/canary"
skill fs-gg-ghost <<'MD'
# a skill the SECOND fixture never wrote
MD
fixture_new
b=$FIX
echo "FIX-A=$(basename "$a")"
echo "FIX-B=$(basename "$b")"
[[ $a == "$b" ]] && echo 'REUSED-THE-SAME-DIR' || echo 'DISTINCT-DIRS'
[[ -e $b/canary ]] && echo 'INHERITED-THE-CANARY' || echo 'FRESH'
[[ -e $b/template/product-skills/fs-gg-ghost ]] && echo 'INHERITED-THE-SKILL' || echo 'NO-GHOST'
SH
says 'FIX-A=fix-1' 'the first fixture is counted, not random'
says 'FIX-B=fix-2' 'and the second gets the next number'
says 'DISTINCT-DIRS' 'two fixtures never share a directory'
says_not 'REUSED-THE-SAME-DIR' 'never — this is the flake, and it is a birthday problem, not a rarity'
says 'FRESH' 'the second fixture does not inherit the first one'"'"'s files'
says 'NO-GHOST' 'nor a skill body it never wrote'

case_start '§5 the fixture writers put their content where the subject will look for it'
probe_raw <<'SH'
harness_init
fixture_new
skill fs-gg-alpha <<'MD'
# alpha
See [[fs-gg-beta]].
MD
issue FS-GG/Other#7 closed
printf '[{"number":3,"state":"open"}]' | trackers
printf '[{"number":9,"state":"open"}]' | unlabelled
echo "SKILL=$(tr '\n' '~' <"$FIX/template/product-skills/fs-gg-alpha/SKILL.md")"
echo "STATE=$(cat "$FIX/gh-state")"
echo "TRACKERS=$(jq -c . <"$FIX/trackers.json")"
echo "UNLABELLED=$(jq -c . <"$FIX/unlabelled.json")"
SH
says 'SKILL=# alpha~See [[fs-gg-beta]].~' '`skill` writes the body to <root>/<id>/SKILL.md, verbatim'
says 'STATE=FS-GG/Other#7	closed' '`issue` records the link state as a tab-separated row'
says 'TRACKERS=[{"number":3,"state":"open"}]' '`trackers` installs the labelled issues'
says 'UNLABELLED=[{"number":9,"state":"open"}]' '`unlabelled` installs the ones without the label'

case_start '§5 `issue` APPENDS — a fixture registers many links, and the second must not erase the first'
probe_raw <<'SH'
harness_init
fixture_new
issue FS-GG/A#1 open
issue FS-GG/B#2 closed
echo "ROWS=$(wc -l <"$FIX/gh-state" | tr -d ' ')"
SH
says 'ROWS=2' 'both rows survive'

# ════════════════════════════════════════════════════════════════════════════════════════════════
# § 6  THE FAKE `gh` IS FAITHFUL — AND CAN BE WRONG
#
# The load-bearing half. #259 exists BECAUSE the gate suite's old fake could not be wrong: it ignored
# `--jq` and printed the issue state whatever it was asked for, so a subject that asked the API for
# entirely the wrong thing was still handed the right answer. A fake that cannot be wrong tests
# nothing — it is the "green over an unexamined subject" shape, committed by the thing that exists to
# refuse it.
#
# So these cases assert the fake can ANSWER WRONG when asked wrongly. That is the property, and it is
# the one a rewrite would quietly lose.
# ════════════════════════════════════════════════════════════════════════════════════════════════

case_start '§6 THE METHOD IS DERIVED, NOT DECLARED — `-f` with no `-X` is a POST'
# The most load-bearing line in the harness. Real `gh api` sends POST the moment a field flag is
# present unless `-X` overrides it — the footgun that had #249's tracker LOOKUP POSTing to /issues,
# the create-issue endpoint, one missing field away from a daily blank-issue factory. Reproduce the
# footgun and a regression that drops `-X GET` shows up as "the lookup was a POST". Hard-code the
# method and the harness blesses the bug it was written to catch.
probe_raw <<'SH'
harness_init
fixture_new
gh_run api repos/o/r/issues -f title=x
echo "METHOD=$(cut -f1 "$FIX/gh.log")"
SH
says 'METHOD=POST' 'a field flag with no -X derives POST, exactly like the real thing'

probe_raw <<'SH'
harness_init
fixture_new
gh_run api repos/o/r/issues -X GET -f labels=decay
echo "METHOD=$(cut -f1 "$FIX/gh.log")"
SH
says 'METHOD=GET' 'and `-X GET` overrides it back — which is why `-X GET` is not decoration'

probe_raw <<'SH'
harness_init
fixture_new
gh_run api repos/o/r/issues/1
echo "METHOD=$(cut -f1 "$FIX/gh.log")"
SH
says 'METHOD=GET' 'no fields, no -X: a plain GET'

case_start '§6 `--jq` is really applied to a real response body — the fake CAN answer the wrong question'
# The heart of #259. The old fake printed the bare state and ignored `--jq`, so it would have answered
# `open` to a subject asking for `.stat`, `.title`, or nothing at all.
probe_raw <<'SH'
harness_init
fixture_new
issue FS-GG/X#1 closed
gh_run api repos/FS-GG/X/issues/1 --jq .state
echo "RIGHT-QUESTION=[$OUT]"
gh_run api repos/FS-GG/X/issues/1 --jq .stat
echo "WRONG-QUESTION=[$OUT]"
gh_run api repos/FS-GG/X/issues/1
echo "NO-QUESTION=[$OUT]"
SH
says 'RIGHT-QUESTION=[closed]' '`--jq .state` gets the state'
says 'WRONG-QUESTION=[null]' 'and `--jq .stat` gets NULL — the fake can be wrong, which is the point'
says 'NO-QUESTION=[{"state":"closed"}]' 'and with no --jq you get the raw body, not a bare word'

case_start '§6 an unregistered issue 404s — and does not error, because an ERROR costs six seconds of sleep'
# check-skill-refs.sh retries an ERROR three times with 2s+4s backoff, so a typo'd fixture ref would
# read as a hang. 404 is the honest answer for "no such issue" anyway — it is what the real API says.
probe_raw <<'SH'
harness_init
fixture_new
issue FS-GG/X#1 open
gh_run api repos/FS-GG/X/issues/999 --jq .state
echo "RC=$RC"
echo "OUT=[$OUT]"
SH
says 'RC=1' 'an unknown ref exits non-zero'
says 'Not Found (HTTP 404)' 'and says 404, the way the real API does'

case_start '§6 an unmodelled endpoint is REFUSED, never silently answered'
# The property that keeps this fake honest as the subjects grow: a new `gh` call the fake does not
# know about must break the suite loudly, rather than returning empty and letting the subject's error
# path be tested by accident.
probe_raw <<'SH'
harness_init
fixture_new
gh_run api repos/o/r/pulls/1/merge -X PUT -f merge_method=squash
echo "RC=$RC"
echo "OUT=[$OUT]"
SH
says 'RC=1' 'an unmodelled endpoint exits non-zero'
says 'unmodelled endpoint' 'and names the problem'

case_start '§6 `labels=` is load-bearing — drop it and a STRANGER becomes visible'
# Drop it and /issues returns EVERY issue in the repo. `first` is then some unrelated issue, and the
# sweep PATCHes a decay report over it.
probe_raw <<'SH'
harness_init
fixture_new
printf '[{"number":5,"state":"open","labels":["decay"]}]' | trackers
printf '[{"number":2,"state":"open"}]' | unlabelled
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all --jq '[.[].number]|tostring'
echo "WITH-LABEL=$OUT"
gh_run api repos/o/r/issues -X GET -f state=all --jq '[.[].number]|tostring'
echo "WITHOUT-LABEL=$OUT"
SH
says 'WITH-LABEL=[5]' 'with `labels=`, only the tracker is visible'
says 'WITHOUT-LABEL=[2,5]' 'without it, issue #2 — a stranger — is in the list, and sorts FIRST'

case_start '§6 the VALUE of `labels=` is honoured — two trackers, and the fake can tell them apart'
# Presence is not enough. The sweep keeps TWO trackers — `skill-refs-decay` and `skill-refs-infra` —
# and reopening the wrong one announces a decay regression over an expired token (FS.GG.Game#277).
# A fake that answered every label query with the same list could not fail that, so it would never
# have asked. It asks here.
probe_raw <<'SH'
harness_init
fixture_new
printf '[{"number":5,"state":"open","labels":["decay"]},{"number":9,"state":"open","labels":["infra"]}]' | trackers
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all --jq '[.[].number]|tostring'
echo "DECAY=$OUT"
gh_run api repos/o/r/issues -X GET -f labels=infra -f state=all --jq '[.[].number]|tostring'
echo "INFRA=$OUT"
gh_run api repos/o/r/issues -X GET -f labels=nobody-has-this -f state=all --jq '[.[].number]|tostring'
echo "NEITHER=$OUT"
SH
says 'DECAY=[5]'   'the decay label finds only the decay tracker'
says 'INFRA=[9]'   'and the infra label only the infra one — never each other'
says 'NEITHER=[]'  'and a label nobody carries finds nothing, rather than the oldest issue going'

case_start '§6 `labels` accepts the API'"'"'s real `[{"name":…}]` shape, not just our bare strings'
# The real /issues payload labels are OBJECTS. A fake that only understood bare strings would quietly
# stop matching the moment someone pasted a real API response into a fixture.
probe_raw <<'SH'
harness_init
fixture_new
printf '[{"number":5,"state":"open","labels":[{"name":"decay","color":"B60205"}]}]' | trackers
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all --jq '[.[].number]|tostring'
echo "OBJECT-LABEL=$OUT"
SH
says 'OBJECT-LABEL=[5]' 'an object-shaped label matches by its .name'

case_start '§6 `state=` is load-bearing — without it a CLOSED tracker is invisible'
# Drop `state=all` and the API defaults to OPEN. A closed tracker becomes invisible, so every
# regression files a SECOND tracker instead of reopening the first.
probe_raw <<'SH'
harness_init
fixture_new
printf '[{"number":5,"state":"closed","labels":["decay"]}]' | trackers
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all --jq '[.[].number]|tostring'
echo "STATE-ALL=$OUT"
gh_run api repos/o/r/issues -X GET -f labels=decay --jq '[.[].number]|tostring'
echo "STATE-DEFAULT=$OUT"
SH
says 'STATE-ALL=[5]' 'with `state=all`, the closed tracker is found'
says 'STATE-DEFAULT=[]' 'without it, the default is OPEN and the closed tracker vanishes'

case_start '§6 `direction=` is load-bearing — flip it and the NEWEST duplicate wins'
# Flip it and the newest duplicate steals the thread from the original.
probe_raw <<'SH'
harness_init
fixture_new
printf '[{"number":5,"state":"open","labels":["decay"]},{"number":11,"state":"open","labels":["decay"]}]' | trackers
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all -f direction=asc --jq 'first(.[]).number'
echo "ASC-FIRST=$OUT"
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all -f direction=desc --jq 'first(.[]).number'
echo "DESC-FIRST=$OUT"
SH
says 'ASC-FIRST=5' 'ascending: the ORIGINAL tracker is first'
says 'DESC-FIRST=11' 'descending: the newest duplicate is first — a different issue, silently'

case_start '§6 `--slurp` changes the SHAPE — drop it and the caller'"'"'s `.[][]` dies'
# `--paginate --slurp` makes gh emit an array OF PAGES, which is why the caller flattens with `.[][]`.
# Without --slurp it is a flat array and that flatten dies — so the nesting belongs in the fake.
probe_raw <<'SH'
harness_init
fixture_new
printf '[{"number":5,"state":"open","labels":["decay"]}]' | trackers
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all --slurp
echo "SLURPED=$OUT"
gh_run api repos/o/r/issues -X GET -f labels=decay -f state=all
echo "FLAT=$OUT"
SH
says 'SLURPED=[[{"number":5,"state":"open","labels":["decay"]}]]' 'with --slurp the response is an array OF PAGES'
says 'FLAT=[{"number":5,"state":"open","labels":["decay"]}]' 'and without it, a flat array — so `.[][]` really would die'

case_start '§6 the call log records the method and the path, and every call'
probe_raw <<'SH'
harness_init
fixture_new
gh_run api repos/o/r/issues -X GET -f labels=decay
gh_run api repos/o/r/issues -f title=x
gh_run api repos/o/r/issues/5/comments -f body=y
echo "CALLS=$(wc -l <"$FIX/gh.log" | tr -d ' ')"
echo "LOG=$(cut -f1,2 "$FIX/gh.log" | tr '\n' '~')"
SH
says 'CALLS=3' 'every call is logged, not just the last'
says 'LOG=GET	repos/o/r/issues~POST	repos/o/r/issues~POST	repos/o/r/issues/5/comments~' \
     'method and path, tab-separated — the two fields the §4 family reads'

case_start '§6 a request BODY sent with `--input -` is recorded, so a suite can assert what went on the wire'
probe_raw <<'SH'
harness_init
fixture_new
gh_run api repos/o/r/issues --input - <<<'{"title":"a decayed pointer"}'
echo "BODIES=$(ls "$FIX/gh-bodies")"
echo "BODY=$(cat "$FIX"/gh-bodies/body-*.json)"
SH
says 'BODIES=body-1.json' 'the body is recorded, numbered by call'
says 'BODY={"title":"a decayed pointer"}' 'verbatim — this is what the sweep PUT on the wire'

case_start '§6 `--input -` implies a field, and therefore a POST — the same derived-method rule'
probe_raw <<'SH'
harness_init
fixture_new
gh_run api repos/o/r/issues --input - <<<'{}'
echo "METHOD=$(cut -f1 "$FIX/gh.log")"
SH
says 'METHOD=POST' 'an --input body derives POST, exactly as a -f field does'

case_start '§6 `GH_NOAUTH` makes `gh auth` fail — the fixture can be an unauthenticated machine'
probe_raw <<'SH'
harness_init
fixture_new
declare -a e; mapfile -t e < <(gh_env)
env "${e[@]}" gh auth status >/dev/null 2>&1;             echo "AUTHED=$?"
env "${e[@]}" GH_NOAUTH=1 gh auth status >/dev/null 2>&1; echo "NOAUTH=$?"
SH
says 'AUTHED=0' 'by default the fixture is logged in'
says 'NOAUTH=1' 'and GH_NOAUTH=1 makes `gh auth` fail, the way a machine with no token does'

case_start '§6 the fake refuses a `gh` subcommand it does not model'
probe_raw <<'SH'
harness_init
fixture_new
declare -a e; mapfile -t e < <(gh_env)
env "${e[@]}" gh pr merge 5 --squash >/dev/null 2>&1; echo "RC=$?"
SH
says 'RC=1' '`gh pr` is not modelled, and the fake says so rather than exiting 0 over it'

# ── summary ─────────────────────────────────────────────────────────────────────────────────────

printf '\n────────────────────────────────────────\n'
if ((t_fail)); then
  printf 'test-harness-selftest: FAILED — %d passed, %d failed\n' "$t_pass" "$t_fail"
  exit 1
fi
printf 'test-harness-selftest: ok — %d assertions passed (%d probes)\n' "$t_pass" "$n_probe"
