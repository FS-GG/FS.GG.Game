#!/usr/bin/env bash
#
# Behavioural tests for scripts/lint-action-shell.sh (FS.GG.Game#135).
#
# The linter is the only thing standing between a composite action's shell and an unread merge, and
# the failure that matters is NOT it going red. It is it finding NOTHING — no actions, no blocks, an
# unreadable dialect — and reporting that in GREEN. Every assertion below that looks pedantic is
# there because the green version of it is indistinguishable from success.
#
# So the suite is organised around the claim the linter makes when it says nothing: "I read every
# shell block in every composite action in this tree." Each case takes that claim away in a different
# direction and demands a red.
#
# It stands on scripts/lib/test-harness.sh, which #259 extracted "before there is a third". This is
# the third — and it does NOT use the harness's `fixture_new`, whose tree is shaped for the skill-refs
# suites (skill bodies, a producer manifest, a fake `gh`). This subject reads none of those; it reads
# .github/actions/ and a git index, so the fixture is built here and the harness supplies what is
# genuinely shared: the temp root, the counters, and the assertions.
#
# Hermetic: fixture git repos under mktemp; no token, no network, no `gh`. A real shellcheck IS
# required — the subject shells out to one — and the fixtures are written so the verdict cannot drift
# with the binary: the clean one is trivially clean, and the dirty one trips SC2046, a check that
# predates every version anyone will run this with.
#
# (No comment line in this file may BEGIN with the linter's name: its first word is read as a
# directive rather than as prose, and the file then fails to parse — SC1072/SC1073. Caught by the
# repo's own shell lint, on this very file, which is the system working.)
set -uo pipefail

HARNESS_OUTPUT_LABEL="lint output"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/test-harness.sh
# shellcheck source-path=SCRIPTDIR
source "$SCRIPT_DIR/lib/test-harness.sh"

SUT="$SCRIPT_DIR/lint-action-shell.sh"
[[ -x $SUT ]] || { echo "test-lint-action-shell: $SUT is not executable" >&2; exit 2; }
command -v shellcheck >/dev/null || { echo "test-lint-action-shell: shellcheck is required" >&2; exit 2; }
SHELLCHECK_BIN="$(command -v shellcheck)"

harness_init

# ── fixture ─────────────────────────────────────────────────────────────────────────────────────
#
# A git repo, because the subject DISCOVERS its work with `git ls-files` — which lists tracked files
# only. That is not incidental to the test: an action nobody committed is an action CI never sees, so
# the fixture stages what it writes, and a case that wants the file invisible simply does not stage it.

repo_new() {
  n_fix=$((n_fix + 1))
  FIX="$TMPROOT/repo-$n_fix"
  mkdir -p "$FIX/.github/actions"
  git -C "$FIX" init -q
  git -C "$FIX" config user.email t@example.com
  git -C "$FIX" config user.name t
}

action() { # action <dir-name>  — action.yml body on stdin
  mkdir -p "$FIX/.github/actions/$1"
  cat >"$FIX/.github/actions/$1/action.yml"
}

# A composite action whose single bash step is the body on stdin. The wrapper is the part every case
# would otherwise re-type, and re-typing it is how a case ends up asserting against a subtly
# different action from the one it meant.
bash_action() { # bash_action <dir-name>  — run: body on stdin
  local body
  body=$(sed 's/^/          /')
  action "$1" <<EOF
name: $1
description: fixture
runs:
  using: composite
  steps:
    - name: step
      shell: bash
      run: |
$body
EOF
}

run_lint() {
  git -C "$FIX" add -A
  OUT=$(cd "$FIX" && SHELLCHECK="$SHELLCHECK_BIN" "$SUT" 2>&1)
  RC=$?
}

# ── cases ───────────────────────────────────────────────────────────────────────────────────────

case_start "a clean composite action passes, and SAYS WHAT IT READ"
repo_new
bash_action ok <<'EOF'
set -euo pipefail
echo "hello"
EOF
run_lint
expect_rc 0 "clean action → exit 0"
expect_out_has "composite actions: 1"  "reports the actions it found"
expect_out_has "extracted: 1"          "reports the blocks it extracted"
expect_out_has "clean at -S warning"   "claims the floor it actually applied"

case_start "a warning-level defect in an action's shell is a RED"
repo_new
bash_action bad <<'EOF'
set -euo pipefail
rm -rf $(mktemp -d)
EOF
run_lint
expect_rc 1 "SC2046 in an action → exit 1"
expect_out_has "SC2046"                       "names the shellcheck code"
expect_out_has "at or above 'warning'"        "names the floor it blocked at"
expect_out_has ".github/actions/bad/action.yml" "maps the finding back to the action that owns it"

case_start "an EMPTY tree is an honest green, not a claim to have read something"
repo_new
run_lint
expect_rc 0 "no composite actions → exit 0"
expect_out_has "nothing to lint"   "says there was no subject"
expect_out_hasnt "clean at -S"     "does NOT claim to have linted anything"

case_start "a dialect shellcheck cannot read is a RED, not a silent skip"
# THE CASE THIS SUITE EXISTS FOR. The first draft of the linter extracted only `shell: bash`, and its
# independent count skipped the same steps — so the two agreed, and it reported "clean" over a
# `shell: pwsh` block it had never opened. Green over an unexamined subject, inside the gate written
# to refuse exactly that.
repo_new
action pwsh-step <<'EOF'
name: pwsh-step
description: fixture
runs:
  using: composite
  steps:
    - name: step
      shell: pwsh
      run: Write-Host "hi"
EOF
run_lint
expect_rc 1 "an unreadable dialect → exit 1"
expect_out_has "shellcheck cannot read"  "says why it stopped"
expect_out_has "pwsh"                    "names the dialect"
expect_out_hasnt "clean at -S warning"   "does NOT report green over the block it could not read"

case_start "a NON-bash dialect shellcheck CAN read is linted, in its own dialect"
repo_new
action sh-step <<'EOF'
name: sh-step
description: fixture
runs:
  using: composite
  steps:
    - name: step
      shell: sh
      run: |
        rm -rf $(mktemp -d)
EOF
run_lint
expect_rc 1 "SC2046 in a 'sh' block → exit 1 (it is read, not skipped)"
expect_out_has "SC2046" "the sh block was actually linted"

case_start "'shell: bash' with a trailing comment is still counted (no false accusation)"
# The independent count greps the raw bytes. Matching the literal `bash` would miss this line, and the
# count-mismatch guard would then red a CORRECT action with a message blaming the extractor — the
# false accusation #238 taught this repo to refuse.
repo_new
action commented <<'EOF'
name: commented
description: fixture
runs:
  using: composite
  steps:
    - name: step
      shell: bash # composite steps must declare a shell
      run: |
        echo "hello"
EOF
run_lint
expect_rc 0 "a commented 'shell:' key → exit 0"
expect_out_has "extracted: 1"      "counted the block rather than accusing the extractor"
expect_out_hasnt "not reading"     "no extractor-is-blind error on a correct action"

case_start "quoted 'shell: \"bash\"' is still counted"
repo_new
action quoted <<'EOF'
name: quoted
description: fixture
runs:
  using: composite
  steps:
    - name: step
      shell: "bash"
      run: |
        echo "hello"
EOF
run_lint
expect_rc 0 "a quoted shell value → exit 0"
expect_out_has "extracted: 1" "counted the block"

case_start "an action the parser cannot see is a RED, not a green over nothing"
# The blind-extractor guard. `runs:` is renamed, so PyYAML yields no steps while the raw bytes still
# declare a `shell:` — the two derivations disagree, which is the whole reason there are two.
repo_new
action blind <<'EOF'
name: blind
description: fixture
runsX:
  using: composite
  steps:
    - name: step
      shell: bash
      run: |
        echo "hello"
EOF
run_lint
expect_rc 1 "parser sees 0 blocks, bytes declare 1 → exit 1"
expect_out_has "never saw"   "refuses to lint a subject it could not read"

case_start "MULTIPLE actions are all read, and the count says so"
repo_new
bash_action one <<'EOF'
echo "one"
EOF
bash_action two <<'EOF'
echo "two"
EOF
run_lint
expect_rc 0 "two clean actions → exit 0"
expect_out_has "composite actions: 2" "found both actions"
expect_out_has "extracted: 2"         "read a block from each"

case_start "a defect in the SECOND action is not masked by a clean first one"
repo_new
bash_action clean <<'EOF'
echo "fine"
EOF
bash_action dirty <<'EOF'
rm -rf $(mktemp -d)
EOF
run_lint
expect_rc 1 "one dirty of two → exit 1"
expect_out_has ".github/actions/dirty/action.yml" "names the offending action, not its innocent neighbour"

case_start "an UNSTAGED action is not linted — the subject is what CI would see"
repo_new
bash_action tracked <<'EOF'
echo "fine"
EOF
git -C "$FIX" add -A
mkdir -p "$FIX/.github/actions/untracked"
cat >"$FIX/.github/actions/untracked/action.yml" <<'EOF'
name: untracked
description: fixture
runs:
  using: composite
  steps:
    - name: step
      shell: bash
      run: |
        rm -rf $(mktemp -d)
EOF
# deliberately NOT staged
OUT=$(cd "$FIX" && SHELLCHECK="$SHELLCHECK_BIN" "$SUT" 2>&1)
RC=$?
expect_rc 0 "an uncommitted action → exit 0 (git ls-files lists tracked files; CI sees a commit)"
expect_out_has "extracted: 1" "read only the tracked action"

harness_summary test-lint-action-shell
