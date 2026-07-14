#!/usr/bin/env bash
#
# Behavioural tests for scripts/check-pin-coherence.sh (FS.GG.Game#319).
#
# The checker is the only thing standing between an incoherent release train and a green merge, and —
# as with scripts/lint-action-shell.sh — the failure that matters is NOT it going red. It is it finding
# NOTHING (a renamed props file, a re-shaped <PackageVersion> line, a rotted regex) and reporting that
# in GREEN, which is byte-for-byte indistinguishable from "the pins are coherent".
#
# So the suite is organised around the claim the checker makes when it says nothing: "I read every
# FS.GG.UI.* and FS.GG.Audio.* pin in this file, and they each agree." Each case takes that claim away
# in a different direction and demands a red.
#
# The first case is the REGRESSION FIXTURE: the exact pin set of Renovate PR FS.GG.Game#317, which was
# green on every check in CI. If this suite ever passes that, the gate is back to where it started.
#
set -uo pipefail

HARNESS_OUTPUT_LABEL="check output"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/test-harness.sh
# shellcheck source-path=SCRIPTDIR
source "$SCRIPT_DIR/lib/test-harness.sh"

SUT="$SCRIPT_DIR/check-pin-coherence.sh"
[[ -x $SUT ]] || { echo "test-check-pin-coherence: $SUT is not executable" >&2; exit 2; }

harness_init

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ── fixture ─────────────────────────────────────────────────────────────────────────────────────
#
# `props <<EOF … EOF` writes a props file from the <PackageVersion> lines you give it, wrapped in the
# XML the real file has. Only the pins vary; that is the whole subject.
props() {
  { echo '<Project>'
    echo '  <ItemGroup>'
    cat
    echo '  </ItemGroup>'
    echo '</Project>'
  } >"$TMP/props.xml"
}

pin() { printf '    <PackageVersion Include="%s" Version="%s" />\n' "$1" "$2"; }

run_check() {
  OUT="$(bash "$SUT" "$TMP/props.xml" 2>&1)"
  RC=$?
}

# The coherent set as main carries it today (all six FS.GG.UI at 0.10.0, both Audio at 0.2.0).
coherent_pins() {
  pin FS.GG.UI.Scene           0.10.0
  pin FS.GG.UI.KeyboardInput   0.10.0
  pin FS.GG.UI.Canvas          0.10.0
  pin FS.GG.UI.Controls.Elmish 0.10.0
  pin FS.GG.UI.SkiaViewer      0.10.0
  pin FS.GG.UI.Template        0.10.0
  pin FS.GG.Audio.Core         0.2.0
  pin FS.GG.Audio.Host         0.2.0
}

# ── cases ───────────────────────────────────────────────────────────────────────────────────────

case_start "REGRESSION FIXTURE — Renovate PR #317's 5-of-6 split set is a RED (it was GREEN on every check in CI)"
props < <(
  pin FS.GG.UI.Scene           0.10.0
  pin FS.GG.UI.KeyboardInput   0.10.0
  pin FS.GG.UI.Canvas          0.10.0
  pin FS.GG.UI.Controls.Elmish 0.10.0
  pin FS.GG.UI.SkiaViewer      0.10.0
  pin FS.GG.UI.Template        0.9.2
  pin FS.GG.Audio.Core         0.2.0
  pin FS.GG.Audio.Host         0.2.0
)
run_check
expect_rc 1 "the split set → exit 1"
expect_out_has "INCOHERENT"           "says the train is incoherent"
expect_out_has "FS.GG.UI.Template"    "NAMES the pin left behind"
expect_out_has "0.9.2"                "names the version it was left at"
expect_out_has "0.10.0"               "names the version the rest moved to"

case_start "the coherent set on main today is GREEN, and SAYS WHAT IT READ"
props < <(coherent_pins)
run_check
expect_rc 0 "all six + both audio agreeing → exit 0"
expect_out_has "FS.GG.UI.* — OK, 6 pin(s), all at 0.10.0"    "counts the UI train and names its version"
expect_out_has "FS.GG.Audio.* — OK, 2 pin(s), all at 0.2.0"  "counts the audio train independently"

case_start "the two trains are INDEPENDENT — audio moving alone is not a UI incoherence"
props < <(
  pin FS.GG.UI.Scene           0.10.0
  pin FS.GG.UI.KeyboardInput   0.10.0
  pin FS.GG.UI.Canvas          0.10.0
  pin FS.GG.UI.Controls.Elmish 0.10.0
  pin FS.GG.UI.SkiaViewer      0.10.0
  pin FS.GG.UI.Template        0.10.0
  pin FS.GG.Audio.Core         0.3.0
  pin FS.GG.Audio.Host         0.3.0
)
run_check
expect_rc 0 "audio on its own train → exit 0"

case_start "an incoherent AUDIO train is a RED too (Audio.Host peers Audio.Core at its own version)"
props < <(
  pin FS.GG.UI.Scene           0.10.0
  pin FS.GG.UI.KeyboardInput   0.10.0
  pin FS.GG.UI.Canvas          0.10.0
  pin FS.GG.UI.Controls.Elmish 0.10.0
  pin FS.GG.UI.SkiaViewer      0.10.0
  pin FS.GG.UI.Template        0.10.0
  pin FS.GG.Audio.Core         0.2.0
  pin FS.GG.Audio.Host         0.3.0
)
run_check
expect_rc 1 "split audio train → exit 1"
expect_out_has "FS.GG.Audio"  "names the audio family, not the UI one"

# ── the zero-match family: a green here is the bug this script exists to refuse ──────────────────

case_start "NO pins at all is a RED, not a pass — a matched-nothing gate reports success"
props < <(pin Expecto 11.1.0)
run_check
expect_rc 1 "no FS.GG.UI.* pins → exit 1"
expect_out_has "expected at least"  "says it expected a subject and did not find one"

case_start "ONE pin is a RED — a coherence claim over a single pin is a claim about nothing"
props < <(
  pin FS.GG.UI.Scene   0.10.0
  pin FS.GG.Audio.Core 0.2.0
  pin FS.GG.Audio.Host 0.2.0
)
run_check
expect_rc 1 "a lone UI pin → exit 1"
expect_out_has "FOUND 1 pin"  "reports how many it actually found"

case_start "a MISSING props file is a RED — the subject cannot be absent and the gate green"
OUT="$(bash "$SUT" "$TMP/does-not-exist.props" 2>&1)"; RC=$?
expect_rc 1 "missing props file → exit 1"
expect_out_has "not found"  "names the missing subject"

case_start "the REAL Directory.Packages.local.props is coherent (this is the gate running for real)"
OUT="$(cd "$SCRIPT_DIR/.." && bash "$SUT" 2>&1)"; RC=$?
expect_rc 0 "the repo's own pins → exit 0"
expect_out_has "FS.GG.UI.* — OK"  "reads the real file with no argument"

harness_summary "test-check-pin-coherence"
