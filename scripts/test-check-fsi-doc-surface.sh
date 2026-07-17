#!/usr/bin/env bash
#
# Behavioural tests for scripts/check-fsi-doc-surface.sh (FS.GG.Game#385).
#
# The checker is the only thing standing between a doc naming an unexported symbol and a green merge,
# and — as with scripts/check-pin-coherence.sh — the failure that matters is NOT it going red. It is
# it finding NOTHING (a renamed baseline, a re-shaped member line, a doc convention that drifts off
# backticks) and reporting that in GREEN, which is byte-for-byte indistinguishable from "every doc
# reference resolves".
#
# So the suite is organised around the claim the checker makes when it says nothing: "I read every
# backticked `Module.symbol` in every .fsi doc comment, and the package exports each one." Each case
# takes that claim away in a different direction and demands a red.
#
# The first case is the REGRESSION FIXTURE: the exact doc line #340 shipped and #378/#384 fixed —
# `Physics.circleCircleManifold`, a `let private`. It sat wrong for weeks and every check in this repo
# was green. If this suite ever passes that, the gate is back to where it started.
#
# The false-positive cases are load-bearing in the OTHER direction, and are not padding: a gate that
# flags correct prose gets ignored, and an ignored gate is a gate that is not there. Each pins a
# reference shape that is CORRECT today and must stay green — the private symbol named from a `.fs`
# implementation comment, the nullary DU case, the type the baseline emits top-level, and another
# package's surface.
#
set -uo pipefail

HARNESS_OUTPUT_LABEL="check output"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/test-harness.sh
# shellcheck source-path=SCRIPTDIR
source "$SCRIPT_DIR/lib/test-harness.sh"

SUT="$SCRIPT_DIR/check-fsi-doc-surface.sh"
[[ -x $SUT ]] || { echo "test-check-fsi-doc-surface: $SUT is not executable" >&2; exit 2; }

harness_init

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ── fixture ─────────────────────────────────────────────────────────────────────────────────────
#
# A fixture tree is a baseline pair (types + members) and a src/ of .fsi files. Only the prose and the
# surface vary; that is the whole subject.

fixture_new_tree() {
  rm -rf "$TMP/tree"
  mkdir -p "$TMP/tree/readiness/surface-baselines/members" "$TMP/tree/src/Game.Core"
}

# The exported surface, in the two shapes refresh-surface-baselines.fsx emits.
baseline_members() { cat >"$TMP/tree/readiness/surface-baselines/members/FS.GG.Game.Core.txt"; }
baseline_types()   { cat >"$TMP/tree/readiness/surface-baselines/FS.GG.Game.Core.txt"; }

fsi() { cat >"$TMP/tree/src/Game.Core/$1"; }

run_check() {
  OUT="$(bash "$SUT" "$TMP/tree/readiness/surface-baselines" "$TMP/tree/src" 2>&1)"
  RC=$?
}

# The surface the fixtures resolve against. Real lines, copied in shape from the generator's output:
# a generic val, a plain val, a nested type, a `+Tags` class, and a property with a [get].
default_surface() {
  baseline_members <<'EOF'
FS.GG.Game.Core.Physics.manifold(FS.GG.Game.Core.Physics+World, System.Int32, System.Int32) : Microsoft.FSharp.Core.FSharpValueOption<FS.GG.Game.Core.Manifold>
FS.GG.Game.Core.Physics.step(FS.GG.Game.Core.Physics+Config, System.Double, FS.GG.Game.Core.Physics+World) : FS.GG.Game.Core.Physics+World
FS.GG.Game.Core.Geometry.circleContact(FS.GG.Game.Core.Circle, FS.GG.Game.Core.Circle) : Microsoft.FSharp.Core.FSharpOption<FS.GG.Game.Core.Contact>
FS.GG.Game.Core.Loop.advance<world>(System.Double, FSharpFunc<world, FSharpFunc<System.Double, world>>, System.Double, StepState<world>) : StepState<world>
FS.GG.Game.Core.Ai.view<T>(System.Int32, System.Int32, FSharpList<Sighting<T>>) : TeamView<T>
FS.GG.Game.Core.DefaultKeymap.value : FS.GG.UI.KeyboardInput.Keymap [get]
EOF
  baseline_types <<'EOF'
FS.GG.Game.Core.Physics
FS.GG.Game.Core.Physics+World
FS.GG.Game.Core.Geometry
FS.GG.Game.Core.Loop
FS.GG.Game.Core.Ai
FS.GG.Game.Core.Contact
FS.GG.Game.Core.Manifold
FS.GG.Game.Core.Source
FS.GG.Game.Core.Source+Tags
FS.GG.Game.Core.StepState`1
FS.GG.Game.Core.DefaultKeymap
EOF
}

# Enough resolvable references to clear MIN_REFS, so a case testing something else does not trip the
# zero-match floor by accident. Twelve vals, all exported — comfortably clear of the floor of 10, so a
# case that adds none of its own still runs the check it means to run.
filler_fsi() {
  fsi Filler.fsi <<'EOF'
module FS.GG.Game.Core.Filler

    /// Calls `Physics.step` then `Physics.manifold`, feeding `Geometry.circleContact`.
    /// Drives `Loop.advance` and `Ai.view`; see `Physics.step` and `Loop.advance` again.
    /// Also `Geometry.circleContact` and `Ai.view` for good measure.
    /// And once more: `Physics.manifold`, `Geometry.circleContact`, `Loop.advance`.
    val filler: int -> int
EOF
}

# ── the regression fixture ──────────────────────────────────────────────────────────────────────

case_start "REGRESSION FIXTURE — #340's \`Physics.circleCircleManifold\` doc is a RED (it was GREEN on every check in this repo for weeks)"
fixture_new_tree; default_surface; filler_fsi
fsi Geometry.fsi <<'EOF'
module FS.GG.Game.Core.Geometry

    /// Narrow-phase circle–circle contact manifold. NB the sibling producer
    /// `Physics.circleCircleManifold` makes the OPPOSITE call on coincident centres.
    val circleContact: a: Circle -> b: Circle -> Contact option
EOF
run_check
expect_rc 1 'a doc naming a `let private` → exit 1'
expect_out_has "Physics.circleCircleManifold"  "NAMES the unexported symbol"
expect_out_has "Geometry.fsi"                  "names the file"
expect_out_has "line=4"                        "names the LINE, so the author can go straight to it"
expect_out_has "does NOT export"               "says what is wrong with it"

case_start "the FIXED prose (#384's \`Physics.manifold\`) is GREEN — the gate accepts the repair it asks for"
fixture_new_tree; default_surface; filler_fsi
fsi Geometry.fsi <<'EOF'
module FS.GG.Game.Core.Geometry

    /// Narrow-phase circle–circle contact manifold. NB the sibling producer
    /// `Physics.manifold` makes the OPPOSITE call on coincident centres.
    val circleContact: a: Circle -> b: Circle -> Contact option
EOF
run_check
expect_rc 0 "the exported sibling → exit 0"
expect_out_has "every one exported"  "says what it read"

# ── scope: what this gate must NOT flag (a gate that cries wolf is a gate nobody reads) ──────────

case_start "a private symbol named from a .fs IMPLEMENTATION comment is NOT flagged — .fsi only"
fixture_new_tree; default_surface; filler_fsi
# The real Geometry.fs does exactly this, deliberately, and it is correct for a maintainer.
cat >"$TMP/tree/src/Game.Core/Geometry.fs" <<'EOF'
module FS.GG.Game.Core.Geometry

    // Coincident centres default the normal to (1, 0) — the opposite choice from
    // `Physics.circleCircleManifold`, which returns ValueNone there (see its comment).
    let circleContact (a: Circle) (b: Circle) : Contact option = None
EOF
run_check
expect_rc 0 "the .fs names a private symbol → still exit 0"
expect_out_hasnt "circleCircleManifold"  "does not even mention it — .fs is not the public contract"

case_start "a nullary DU case is NOT flagged — the member baseline does not enumerate one"
fixture_new_tree; default_surface; filler_fsi
fsi Effects.fsi <<'EOF'
module FS.GG.Game.Core.Effects

    /// A `Source.Declared` effect, as opposed to `Source.Periodic`.
    val declared: int -> int
EOF
run_check
expect_rc 0 "PascalCase union cases → exit 0"
expect_out_hasnt "Source.Declared"  "says nothing about union cases (only Source+Tags is in the baseline)"

case_start "a TYPE the baseline emits top-level is NOT flagged (\`Loop.StepState\` → \`StepState\`)"
fixture_new_tree; default_surface; filler_fsi
fsi Physics.fsi <<'EOF'
module FS.GG.Game.Core.Physics

    /// Threads a `Loop.StepState` through the solver, producing a `Contact`.
    val step: int -> int
EOF
run_check
expect_rc 0 "PascalCase type references → exit 0"
expect_out_hasnt "StepState"  "says nothing about types — this cut resolves vals only"

case_start "ANOTHER package's surface is NOT flagged — we have no opinion on FS.GG.UI or the BCL"
fixture_new_tree; default_surface; filler_fsi
fsi Adapter.fsi <<'EOF'
module FS.GG.Game.Core.Adapter

    /// Clamps to `Int32.MaxValue` via `Math.Round`, looks up with `Map.tryFind`, and
    /// returns a `FS.GG.UI.Scene.Rect` built by `Scene.empty`. Field access: `damage.Base`.
    val adapt: int -> int
EOF
run_check
expect_rc 0 "foreign roots → exit 0"
expect_out_hasnt "Int32.MaxValue"  "skips roots our own baseline does not export"

case_start "a FULLY-QUALIFIED reference resolves the same as a short one"
fixture_new_tree; default_surface; filler_fsi
fsi Grids.fsi <<'EOF'
module FS.GG.Game.Core.Grids

    /// Same symbol, spelled long: `FS.GG.Game.Core.Physics.manifold`.
    val g: int -> int
EOF
run_check
expect_rc 0 "the namespace-qualified spelling → exit 0"

case_start "a fully-qualified reference to an UNEXPORTED symbol is still a RED"
fixture_new_tree; default_surface; filler_fsi
fsi Grids.fsi <<'EOF'
module FS.GG.Game.Core.Grids

    /// Spelled long, and wrong: `FS.GG.Game.Core.Physics.circleCircleManifold`.
    val g: int -> int
EOF
run_check
expect_rc 1 "the long spelling of a private symbol → exit 1"
expect_out_has "circleCircleManifold"  "resolves the long form against the same surface"

case_start "a GENERIC val resolves — the baseline spells it \`view<T>(…)\` and the doc spells it \`Ai.view\`"
fixture_new_tree; default_surface; filler_fsi
fsi Ai.fsi <<'EOF'
module FS.GG.Game.Core.Ai

    /// Builds via `Ai.view` and steps with `Loop.advance`.
    val view: int -> int
EOF
run_check
expect_rc 0 "generic members → exit 0"
expect_out_hasnt "UNRESOLVED"  "the <T> in the baseline line is not part of the name"

case_start "a renamed val is caught — this is the drift the gate exists for, beyond the one private symbol"
fixture_new_tree; default_surface; filler_fsi
fsi Physics.fsi <<'EOF'
module FS.GG.Game.Core.Physics

    /// See `Physics.solve`, which nothing exports (it was renamed to `step`).
    val step: int -> int
EOF
run_check
expect_rc 1 "a doc naming a symbol that no longer exists → exit 1"
expect_out_has "Physics.solve"  "names the stale reference"

case_start "MULTIPLE unresolved references on ONE line are each reported"
fixture_new_tree; default_surface; filler_fsi
fsi Physics.fsi <<'EOF'
module FS.GG.Game.Core.Physics

    /// Both `Physics.gone` and `Geometry.alsoGone` are absent.
    val step: int -> int
EOF
run_check
expect_rc 1 "two bad refs on one line → exit 1"
expect_out_has "Physics.gone"       "reports the first"
expect_out_has "Geometry.alsoGone"  "reports the second — the scan does not stop at the first match"
expect_out_has "2 unresolved"       "counts them"

# ── the known boundary: a WRAPPED code span (pinned in BOTH directions, so it stays deliberate) ──

case_start "a reference inside a WRAPPED code span is NOT read — the known false negative, pinned so it stays a decision"
fixture_new_tree; default_surface; filler_fsi
# Twelve spans in the real tree wrap like this; Ai.fsi:51 carries `Ai.view` inside one. The gate reads
# lines, so the reference is invisible. If this case ever goes RED, someone taught it to read `///`
# BLOCKS — which is the improvement the header names, and this case is then the thing to delete.
fsi Wrapped.fsi <<'EOF'
module FS.GG.Game.Core.Wrapped

    /// Smuggled across a line break at the construction site (`Physics.circleCircleManifold : ... ->
    /// Manifold`) where this gate cannot see it.
    val w: int -> int
EOF
run_check
expect_rc 0 "a private symbol inside a wrapped span → exit 0 (NOT read)"
expect_out_hasnt "circleCircleManifold"  "the gate is silent about it rather than claiming it checked"

case_start "...and the mispaired backticks a wrap leaves behind produce NO false positive"
fixture_new_tree; default_surface; filler_fsi
# The dangerous half of the same shape: after an unclosed span, the NEXT line's backticks pair off by
# one, so a closing delimiter can meet the following opening one. Nothing that spans that gap is a
# dotted identifier, so nothing matches — and this case fails the moment that stops being true.
fsi Mispaired.fsi <<'EOF'
module FS.GG.Game.Core.Mispaired

    /// Returns `Some contact` exactly when `intersects a b` (strict edges) — so `(circleContact a b)
    /// |> Option.isSome` agrees with `intersects a b`, and `Physics.step` still resolves.
    val m: int -> int
EOF
run_check
expect_rc 0 "wrapped prose around real spans → exit 0"
expect_out_hasnt "UNRESOLVED"  "no fragment of a wrapped span is mistaken for an unexported symbol"

# ── the zero-match family: a green here is the bug this script exists to refuse ──────────────────

case_start "a MISSING baseline directory is a RED, not a pass — the oracle cannot be absent and the gate green"
fixture_new_tree; filler_fsi
rm -rf "$TMP/tree/readiness/surface-baselines/members"
run_check
expect_rc 1 "no members/ dir → exit 1"
expect_out_has "not found"  "names the missing oracle"

case_start "an EMPTY baseline directory is a RED — a moved generator output is not a clean surface"
fixture_new_tree; filler_fsi   # members/ exists, but no .txt in it
run_check
expect_rc 1 "members/ with no baselines → exit 1"
expect_out_has "no member baselines"  "distinguishes an empty oracle from a missing one"

case_start "EMPTY baselines are a RED — every reference would 'resolve' against nothing"
fixture_new_tree; filler_fsi
: >"$TMP/tree/readiness/surface-baselines/members/FS.GG.Game.Core.txt"
: >"$TMP/tree/readiness/surface-baselines/FS.GG.Game.Core.txt"
run_check
expect_rc 1 "an empty oracle → exit 1"
expect_out_has "0 names"  "says it parsed nothing, rather than passing"

case_start "a baseline whose LINE SHAPE changed is a RED — the parse rotting must not read as clean docs"
fixture_new_tree; filler_fsi
baseline_members <<'EOF'
{"member": "FS.GG.Game.Core.Physics.manifold", "returns": "Manifold"}
EOF
baseline_types <<'EOF'
{"type": "FS.GG.Game.Core.Physics"}
EOF
run_check
expect_rc 1 "the generator emitting JSON instead of text → exit 1"
expect_out_has "0 names"  "says it parsed nothing rather than resolving every reference against a void"

case_start "a baseline whose NAMESPACE no longer matches its filename is a RED — every ref would be skipped as 'not ours'"
fixture_new_tree; filler_fsi
# Parses fine, so the names guard passes — but nothing sits under the namespace the FILENAME declares,
# so no root is ours and every reference would be silently skipped. That is the exact shape this gate
# exists to refuse: a full sweep that examines nothing and reports green.
baseline_members <<'EOF'
Some.Other.Namespace.Physics.manifold(System.Int32) : System.Int32
Some.Other.Namespace.Geometry.circleContact(System.Int32) : System.Int32
EOF
baseline_types <<'EOF'
Some.Other.Namespace.Physics
EOF
run_check
expect_rc 1 "a renamed namespace → exit 1"
expect_out_has "0 module roots"  "says it derived no roots rather than skipping every reference silently"

case_start "NO .fsi files is a RED — the subject cannot be absent and the gate green"
fixture_new_tree; default_surface   # baselines, but no src/*.fsi
run_check
expect_rc 1 "an .fsi-less src tree → exit 1"
expect_out_has "no .fsi files"  "names the missing subject"

case_start "TOO FEW references is a RED — the doc convention drifting off backticks must not read as green"
fixture_new_tree; default_surface
fsi Geometry.fsi <<'EOF'
module FS.GG.Game.Core.Geometry

    /// Prose that resolves nothing at all, because the convention moved to [Physics.manifold].
    val circleContact: a: Circle -> b: Circle -> Contact option
EOF
run_check
expect_rc 1 "a tree with no backticked refs left → exit 1"
expect_out_has "expected at least"  "says it expected a subject and did not find one"
expect_out_has "resolved only 0"    "reports how many it actually found"

case_start "the floor counts CONSIDERED refs, not files — many .fsi with no refs is still a RED"
fixture_new_tree; default_surface
for m in A B C D E F G H I J K L; do
  fsi "$m.fsi" <<EOF
module FS.GG.Game.Core.$m

    /// Nothing backticked here.
    val v$m: int -> int
EOF
done
run_check
expect_rc 1 "12 .fsi files, 0 references → exit 1"
expect_out_has "expected at least"  "a file count is not a coverage claim"

# ── the gate running for real ───────────────────────────────────────────────────────────────────

case_start "the REAL tree is clean (this is the gate running for real, against the checked-in surface)"
OUT="$(cd "$SCRIPT_DIR/.." && bash "$SUT" 2>&1)"; RC=$?
expect_rc 0 "the repo's own .fsi docs → exit 0"
expect_out_has "check-fsi-doc-surface: OK"  "reads the real tree with no arguments"
expect_re "OK — [0-9]+ doc reference" "$OUT" "reports a real count, so 'clean' is distinguishable from 'read nothing'"

harness_summary "test-check-fsi-doc-surface"
