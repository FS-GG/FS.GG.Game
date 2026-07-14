#!/usr/bin/env bash
#
# Enforce the RELEASE-TRAIN COHERENCE that Directory.Packages.local.props states in prose and nothing
# ever checked (FS.GG.Game#319).
#
# That file says it twice — "Keep all five Rendering pins on one version" and "Keep all six on one
# version" — and explains why it is load-bearing:
#
#   `_scaffold.fs` binds `Scene.Point`/`Rect` directly, so the template that SHIPS the fragment and the
#   FS.GG.UI.Scene the gate COMPILES it against have to be one coherent set — otherwise the scaffold
#   could be generated from a fragment written against a Scene surface the gate never restores, and the
#   gate would go green over a pairing no reader can reproduce.
#
# It went green over exactly that. Renovate PR FS.GG.Game#317 bumped FIVE of the six FS.GG.UI.* pins to
# 0.10.0 and left FS.GG.UI.Template at 0.9.2 — one file, one PR — and every check passed: gate,
# governance, coordination-coherence, lockfile-sync. The invariant lived only in a comment, and a
# comment is not a gate.
#
# WHY THE SCAFFOLD-DRIFT GATE CANNOT COVER THIS, though it looks like it should. It compares
# `_scaffold.fs` against THE PINNED FS.GG.UI.Template — whichever version that happens to be. Pin and
# artefact therefore stay mutually consistent while drifting away from the REST of the train, so the
# gate is satisfied by the very state it should reject. It is a STALENESS check; this is a COHERENCE
# check, and no amount of the former is the latter.
#
# THE FAILURE THAT MATTERS IS NOT THIS SCRIPT GOING RED. It is this script finding NOTHING — a renamed
# props file, a re-shaped <PackageVersion> line, a regex that quietly stops matching — and reporting
# that in GREEN, which is indistinguishable from "the pins are coherent" (the FS-GG/.github#266 shape,
# and the same reason scripts/lint-action-shell.sh counts its subject twice). So the subject is
# asserted to EXIST before it is asserted to be CLEAN: a family with no pins at all is a RED, and so is
# a family with only one, because a coherence claim over fewer than two pins is a claim about nothing.
#
# The families are the two release trains the props file declares. FS.GG.UI.* is one train (members +
# BOM + template; each depends on FS.GG.UI.Scene at its own version). FS.GG.Audio.* is its own
# ("Audio.Host depends on Audio.Core at the same version"). They are checked independently: a UI bump
# says nothing about audio.
#
#   scripts/check-pin-coherence.sh [props-file]     # default: Directory.Packages.local.props
#
set -uo pipefail

PROPS="${1:-Directory.Packages.local.props}"

# The two coherent sets Directory.Packages.local.props declares, and the minimum number of pins each
# must have for a coherence claim over it to mean anything. The minimum is a ZERO-MATCH GUARD with a
# floor: it is what turns "I matched nothing" from a pass into a red.
FAMILIES=("FS.GG.UI" "FS.GG.Audio")
MIN_PINS=2

if [[ ! -f $PROPS ]]; then
  echo "::error::check-pin-coherence: $PROPS not found. The pin file is the SUBJECT of this gate — a missing subject is a finding, not a pass."
  echo "check-pin-coherence: $PROPS not found" >&2
  exit 1
fi

rc=0

for family in "${FAMILIES[@]}"; do
  # Match `<PackageVersion Include="<family>[.something]" Version="X" />` and emit `id<TAB>version`.
  # Anchored on the family so FS.GG.UI does not swallow FS.GG.UI-ish ids from another train, and
  # tolerant of attribute spacing, which is the shape most likely to drift under a reformat.
  mapfile -t pins < <(
    grep -oE "<PackageVersion[[:space:]]+Include=\"${family//./\\.}(\.[A-Za-z0-9._]+)?\"[[:space:]]+Version=\"[^\"]+\"" "$PROPS" |
      sed -E 's/.*Include="([^"]+)".*Version="([^"]+)".*/\1\t\2/'
  )

  count=${#pins[@]}

  if (( count < MIN_PINS )); then
    echo "::error::check-pin-coherence: found $count ${family}.* pin(s) in $PROPS, expected at least $MIN_PINS. This gate cannot make a coherence claim over fewer than two pins, and it will NOT report that as green — the subject is missing, which is the failure this script exists to refuse (the pins were renamed, the props file moved, or the <PackageVersion> shape changed and the match rotted)."
    echo "check-pin-coherence: ${family}.* — FOUND $count pin(s), expected >= $MIN_PINS (missing subject, not a pass)" >&2
    rc=1
    continue
  fi

  versions=$(printf '%s\n' "${pins[@]}" | cut -f2 | sort -u)
  n_versions=$(printf '%s\n' "$versions" | grep -c .)

  if (( n_versions != 1 )); then
    echo "::error::check-pin-coherence: the ${family}.* release train is INCOHERENT in $PROPS — $count pin(s) across $n_versions different versions ($(printf '%s' "$versions" | tr '\n' ' ')). These pins MUST move as one: each package depends on its siblings at its own version, so a split set compiles one train's source against another train's surface — and the gate goes green over a pairing no reader can reproduce. Move every ${family}.* pin to the same version. (If a Renovate PR produced this, it bumped part of the set: see FS.GG.Game#319 / FS-GG/.github#746.)"
    echo "check-pin-coherence: ${family}.* — INCOHERENT ($n_versions versions across $count pins):" >&2
    printf '%s\n' "${pins[@]}" | sort | awk -F'\t' '{ printf "  %-32s %s\n", $1, $2 }' >&2
    rc=1
    continue
  fi

  # Green — and it SAYS WHAT IT READ, so a future reader can tell "6 pins, all agreeing" from
  # "matched nothing and shrugged".
  printf 'check-pin-coherence: %s.* — OK, %d pin(s), all at %s\n' "$family" "$count" "$versions"
done

if (( rc == 0 )); then
  echo "check-pin-coherence: OK — every declared release train in $PROPS is on a single coherent version."
fi

exit "$rc"
