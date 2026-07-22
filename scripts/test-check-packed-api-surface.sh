#!/usr/bin/env bash
#
# Behavioural tests for scripts/check-packed-api-surface.sh (FS.GG.Game#462).
#
# The checker is the only thing standing between an INCOMPLETE packed api-surface and a green release
# — and, exactly as with scripts/check-fsi-doc-surface.sh, the failure that matters is NOT it going
# red. It is it reading NOTHING (a moved baseline, a re-shaped member line, a `<Compile>` list it no
# longer parses) and reporting that in GREEN, which is byte-for-byte indistinguishable from "the
# packed surface is complete".
#
# So the suite is organised around the two claims the checker makes when it says OK:
#   (1) every module/type the assembly EXPORTS is DECLARED in some packed `.fsi`, and
#   (2) the set of `.fsi` the pack ships is exactly the set the project COMPILES.
# Each case takes one of those claims away in a different direction and demands a red — and the
# zero-match family takes away the SUBJECT and demands a red for that too, because a completeness claim
# over a surface you could not read is a claim about nothing (FS-GG/.github#266).
#
# The false-negative direction is load-bearing the other way: a gate that flags a CORRECT surface gets
# ignored, and an ignored gate is a gate that is not there. The first two cases pin the real tree and
# the legitimate `type X` + `module X` companion pattern as GREEN so they stay that way.
#
set -uo pipefail

HARNESS_OUTPUT_LABEL="check output"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/test-harness.sh
# shellcheck source-path=SCRIPTDIR
source "$SCRIPT_DIR/lib/test-harness.sh"

SUT="$SCRIPT_DIR/check-packed-api-surface.sh"
[[ -x $SUT ]] || { echo "test-check-packed-api-surface: $SUT is not executable" >&2; exit 2; }

harness_init

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ── fixture ─────────────────────────────────────────────────────────────────────────────────────
#
# A fixture tree is a baseline pair (types + members), a src/ of `.fsi` files, and an fsproj whose
# <Compile> list names them. Only those three vary; that is the whole subject.

fixture_new_tree() {
  rm -rf "$TMP/tree"
  mkdir -p "$TMP/tree/readiness/surface-baselines/members" "$TMP/tree/src/Game.Core"
}

baseline_members() { cat >"$TMP/tree/readiness/surface-baselines/members/FS.GG.Game.Core.txt"; }
baseline_types()   { cat >"$TMP/tree/readiness/surface-baselines/FS.GG.Game.Core.txt"; }

fsi() { cat >"$TMP/tree/src/Game.Core/$1"; }

# Write an fsproj whose <Compile> list is exactly the arguments (each an `.fsi` basename). The default
# compile list, when a case does not call this itself, is every `.fsi` the fixture wrote.
fsproj() {
  {
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <ItemGroup>'
    local n
    for n in "$@"; do printf '    <Compile Include="%s" />\n' "$n"; done
    echo '  </ItemGroup>'
    echo '</Project>'
  } >"$TMP/tree/src/Game.Core/FS.GG.Game.Core.fsproj"
}

run_check() {
  # If a case did not write an fsproj, synthesise one naming every .fsi present, so the case exercises
  # the check it means to (a case testing COMPLETENESS must not trip PACK FIDELITY by accident).
  if [[ ! -f $TMP/tree/src/Game.Core/FS.GG.Game.Core.fsproj ]]; then
    local names=()
    while IFS= read -r p; do names+=("$(basename "$p")"); done \
      < <(find "$TMP/tree/src/Game.Core" -name '*.fsi' | sort)
    fsproj "${names[@]}"
  fi
  OUT="$(bash "$SUT" \
    "$TMP/tree/readiness/surface-baselines" \
    "$TMP/tree/src/Game.Core" \
    "$TMP/tree/src/Game.Core/FS.GG.Game.Core.fsproj" \
    FS.GG.Game.Core 2>&1)"
  RC=$?
}

# The exported surface the fixtures resolve against: enough top-level roots to clear MIN_ROOTS, in the
# two shapes refresh-surface-baselines.fsx emits. Includes a top-level MODULE (Pathfinding), a
# nested type (`Pathfinding+Step` — a root of `Pathfinding`, NOT its own root), a generic type
# (`Damage`1`), and a type-plus-companion-module pair (Rng).
default_surface() {
  baseline_members <<'EOF'
FS.GG.Game.Core.Pathfinding.astar(FS.GG.Game.Core.Neighbourhood+Tags, System.Int32) : Microsoft.FSharp.Core.FSharpOption
FS.GG.Game.Core.Ai.view<T>(System.Int32) : System.Int32
FS.GG.Game.Core.Effects.apply(System.Int32) : System.Int32
FS.GG.Game.Core.Ballistics.advance(System.Int32) : System.Int32
FS.GG.Game.Core.Dice.roll(System.Int32) : System.Int32
FS.GG.Game.Core.Los.lineOfSight(System.Int32) : System.Int32
FS.GG.Game.Core.Fov.compute(System.Int32) : System.Int32
FS.GG.Game.Core.Visibility.polygon(System.Int32) : System.Int32
FS.GG.Game.Core.Difficulty.scale(System.Int32) : System.Int32
FS.GG.Game.Core.Rng.next(System.Int32) : System.Int32
FS.GG.Game.Core.Pathfinding.Step.Cost : System.Int32 [get]
EOF
  baseline_types <<'EOF'
FS.GG.Game.Core.Pathfinding
FS.GG.Game.Core.Pathfinding+Step
FS.GG.Game.Core.Ai
FS.GG.Game.Core.Effects
FS.GG.Game.Core.Ballistics
FS.GG.Game.Core.Dice
FS.GG.Game.Core.Los
FS.GG.Game.Core.Fov
FS.GG.Game.Core.Visibility
FS.GG.Game.Core.Difficulty
FS.GG.Game.Core.Rng
FS.GG.Game.Core.Damage`1
EOF
}

# The `.fsi` set that DECLARES every root the default surface exports. `Difficulty` deliberately lives
# INSIDE Ai.fsi (a top-level module that is not its own file) — the case that a filename-based check
# would get wrong. `Damage` is declared in Effects.fsi. Rng.fsi carries the `type Rng` + `module Rng`
# companion pair.
complete_fsi_set() {
  fsi Pathfinding.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Pathfinding =
    type Step = { Cost: int }
    val astar: int -> int
EOF
  fsi Ai.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Ai =
    val view: int -> int
[<RequireQualifiedAccess>]
module Difficulty =
    val scale: int -> int
EOF
  fsi Effects.fsi <<'EOF'
namespace FS.GG.Game.Core
type Damage<'a> = { Base: 'a }
[<RequireQualifiedAccess>]
module Effects =
    val apply: int -> int
EOF
  fsi Ballistics.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Ballistics =
    val advance: int -> int
EOF
  fsi Dice.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Dice =
    val roll: int -> int
EOF
  fsi Los.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Los =
    val lineOfSight: int -> int
EOF
  fsi Fov.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Fov =
    val compute: int -> int
EOF
  fsi Visibility.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Visibility =
    val polygon: int -> int
EOF
  fsi Rng.fsi <<'EOF'
namespace FS.GG.Game.Core
type Rng = { Seed: int }
[<RequireQualifiedAccess>]
module Rng =
    val next: int -> int
EOF
}

# ── the gate running for real ───────────────────────────────────────────────────────────────────

case_start "the REAL tree is clean (the gate running for real, against the checked-in surface + fsproj)"
OUT="$(cd "$SCRIPT_DIR/.." && bash "$SUT" 2>&1)"; RC=$?
expect_rc 0 "the repo's own packed surface → exit 0"
expect_out_has "check-packed-api-surface: OK"  "reads the real tree with no arguments"
expect_re "OK — [0-9]+ exported root" "$OUT" "reports a real count, so 'clean' is distinguishable from 'read nothing'"

# ── completeness (check 1) ──────────────────────────────────────────────────────────────────────

case_start "a COMPLETE fixture is GREEN — including a module (\`Difficulty\`) that lives inside another file"
fixture_new_tree; default_surface; complete_fsi_set
run_check
expect_rc 0 "every exported root declared → exit 0"
expect_out_has "all declared"  "says what it read"

case_start "REGRESSION — an exported module with NO packed .fsi is a RED (the FS.GG.SDD#644 gap)"
fixture_new_tree; default_surface; complete_fsi_set
rm "$TMP/tree/src/Game.Core/Dice.fsi"   # Dice is still in the baseline (exported) but no longer packed
run_check
expect_rc 1 "an exported-but-unpacked module → exit 1"
expect_out_has "Dice"                 "names the missing module"
expect_out_has "NO declaration"       "says what is wrong"
expect_out_has "FS.GG.SDD#644"        "ties it to the gap it exists to close"

case_start "a root declared via \`and\` (a mutually-recursive type group) is DECLARED, not missing"
fixture_new_tree; default_surface; complete_fsi_set
# Export a second root, `Damage`, and declare it as `and Damage` in a recursive group. A grep that only
# knew `type`/`module` would miss it and false-red a genuinely complete surface.
baseline_types <<'EOF'
FS.GG.Game.Core.Pathfinding
FS.GG.Game.Core.Pathfinding+Step
FS.GG.Game.Core.Ai
FS.GG.Game.Core.Effects
FS.GG.Game.Core.Ballistics
FS.GG.Game.Core.Dice
FS.GG.Game.Core.Los
FS.GG.Game.Core.Fov
FS.GG.Game.Core.Visibility
FS.GG.Game.Core.Difficulty
FS.GG.Game.Core.Rng
FS.GG.Game.Core.Damage
FS.GG.Game.Core.Trace
EOF
fsi Effects.fsi <<'EOF'
namespace FS.GG.Game.Core
type Damage = { Base: int; Applied: Trace }
and Trace = { Note: string }
[<RequireQualifiedAccess>]
module Effects =
    val apply: int -> int
EOF
run_check
expect_rc 0 "the \`and\`-declared sibling type resolves → exit 0"
expect_out_hasnt "Trace"  "the recursive-group member is NOT reported missing"

case_start "the module lives inside another file: deleting Ai.fsi drops BOTH \`Ai\` and \`Difficulty\`"
fixture_new_tree; default_surface; complete_fsi_set
rm "$TMP/tree/src/Game.Core/Ai.fsi"
run_check
expect_rc 1 "a file carrying two top-level modules → exit 1"
expect_out_has "Ai"          "reports the eponymous module"
expect_out_has "Difficulty"  "AND the module that only lived inside it — filename is not the subject"

# ── pack fidelity (check 2) ─────────────────────────────────────────────────────────────────────

case_start "a STRAY .fsi (packed by the glob, absent from <Compile>) is a RED — it ships unvalidated"
fixture_new_tree; default_surface; complete_fsi_set
# A leftover .fsi the compiler never sees. Declare its module so completeness stays satisfied and the
# ONLY thing wrong is that it is not compiled — isolating check 2.
fsi Stray.fsi <<'EOF'
namespace FS.GG.Game.Core
[<RequireQualifiedAccess>]
module Stray =
    val ghost: int -> int
EOF
# The compile list names every REAL module but not Stray.fsi.
fsproj Pathfinding.fsi Ai.fsi Effects.fsi Ballistics.fsi Dice.fsi Los.fsi Fov.fsi Visibility.fsi Rng.fsi
run_check
expect_rc 1 "a packed-but-uncompiled file → exit 1"
expect_out_has "Stray.fsi"            "names the stray file"
expect_out_has "never validates"      "says why an uncompiled packed .fsi is dangerous"

case_start "a DROPPED .fsi (compiled but excluded from the pack view) is a RED"
fixture_new_tree; default_surface; complete_fsi_set
# The compile list names a file the fixture never wrote — so it is 'compiled' but not in the packed set.
fsproj Pathfinding.fsi Ai.fsi Effects.fsi Ballistics.fsi Dice.fsi Los.fsi Fov.fsi Visibility.fsi Rng.fsi Phantom.fsi
run_check
expect_rc 1 "a compiled-but-unpacked module → exit 1"
expect_out_has "Phantom.fsi"          "names the dropped module"
expect_out_has "would be missing"     "says its surface would be absent downstream"

# ── the zero-match family: a green here is the bug this script exists to refuse ──────────────────

case_start "a MISSING members baseline is a RED — the oracle cannot be absent and the gate green"
fixture_new_tree; default_surface; complete_fsi_set
rm "$TMP/tree/readiness/surface-baselines/members/FS.GG.Game.Core.txt"
run_check
expect_rc 1 "no members baseline → exit 1"
expect_out_has "not found"  "names the missing oracle"

case_start "an EMPTY baseline is a RED — every module would read as 'not exported'"
fixture_new_tree; default_surface; complete_fsi_set
: >"$TMP/tree/readiness/surface-baselines/members/FS.GG.Game.Core.txt"
run_check
expect_rc 1 "an empty oracle → exit 1"
expect_out_has "is empty"  "says the oracle was empty rather than passing over it"

case_start "a baseline whose LINE SHAPE changed is a RED — the parse rotting must not read as clean"
fixture_new_tree; complete_fsi_set
baseline_members <<'EOF'
{"member": "FS.GG.Game.Core.Pathfinding.astar", "returns": "Option"}
EOF
baseline_types <<'EOF'
{"type": "FS.GG.Game.Core.Pathfinding"}
EOF
run_check
expect_rc 1 "the generator emitting JSON instead of text → exit 1"
expect_out_has "derived only"  "says it derived too few roots rather than resolving against a void"

case_start "a baseline whose NAMESPACE no longer matches its filename is a RED — every root skipped"
fixture_new_tree; complete_fsi_set
baseline_members <<'EOF'
Some.Other.Namespace.Pathfinding.astar(System.Int32) : System.Int32
Some.Other.Namespace.Ai.view(System.Int32) : System.Int32
EOF
baseline_types <<'EOF'
Some.Other.Namespace.Pathfinding
EOF
run_check
expect_rc 1 "a renamed namespace → exit 1"
# The namespace strip leaves the full dotted path, whose first segment is `Some` — one root, below the
# floor — so the too-few-roots guard fires rather than a silent skip.
expect_out_has "derived only"  "refuses to derive a plausible-looking root set from the wrong namespace"

case_start "NO .fsi files is a RED — the packed subject cannot be absent and the gate green"
fixture_new_tree; default_surface   # baselines, but no src/*.fsi
run_check
expect_rc 1 "an .fsi-less src tree → exit 1"
expect_out_has "no .fsi files"  "names the missing subject"

case_start "a MISSING fsproj is a RED — check 2's subject cannot be absent"
fixture_new_tree; default_surface; complete_fsi_set
# Force the SUT to look for a specific (absent) fsproj rather than letting run_check synthesise one.
find "$TMP/tree/src/Game.Core" -name '*.fsi' >/dev/null
OUT="$(bash "$SUT" \
  "$TMP/tree/readiness/surface-baselines" \
  "$TMP/tree/src/Game.Core" \
  "$TMP/tree/src/Game.Core/does-not-exist.fsproj" \
  FS.GG.Game.Core 2>&1)"; RC=$?
expect_rc 1 "an absent fsproj → exit 1"
expect_out_has "not found"  "names the missing compile-list subject"

case_start "an fsproj with NO <Compile> .fsi entries is a RED — the parse rotting must not read as clean"
fixture_new_tree; default_surface; complete_fsi_set
cat >"$TMP/tree/src/Game.Core/FS.GG.Game.Core.fsproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="FSharp.Core" />
  </ItemGroup>
</Project>
EOF
run_check
expect_rc 1 "a compile list this parser cannot read → exit 1"
expect_out_has "parsed 0"  "says it parsed no compile entries rather than passing"

harness_summary "test-check-packed-api-surface"
