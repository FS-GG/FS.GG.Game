#!/usr/bin/env bash
#
# FS.GG.Game#795 (GAME-RENDER-02.8), B4 of the #805 review: the experimental theme derivation gives the same bytes on
# .NET and on Fable (node), run-time custom seeds included (glyph ADR Q12). tests/Game.Render.Themes.Fuzz derives
# SEEDS fuzz seeds through all five preset algorithm lists, runs the full 8-bit OKLCH round trip, and digests the gridded
# inputs of every 8-bit colour and ofOklch over derivation's grid. Both runs must print the summary line, derive every
# seed, fail nothing, and agree byte for byte.
#
#   usage: scripts/test-theme-fuzz.sh [seeds]     (default 2000 fuzz seeds plus the 120 review seeds: 10,600 derivations)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/tests/Game.Render.Themes.Fuzz"
SEEDS="${1:-2000}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

cd "$REPO_ROOT"
dotnet run --project "$PROJECT" -c Release -- "$SEEDS" >"$TMP/dotnet.txt"
dotnet tool restore >/dev/null
dotnet fable "$PROJECT" --outDir "$TMP/js" --noCache >"$TMP/fable.log" 2>&1 || { cat "$TMP/fable.log" >&2; exit 1; }
printf '{"type":"module"}\n' >"$TMP/js/package.json"
node "$TMP/js/Program.js" "$SEEDS" >"$TMP/node.txt"

for run in dotnet node; do
  summary="$(grep -E '^theme-fuzz seeds=[0-9]+ derivations=[0-9]+ ok=[0-9]+ checks=[0-9]+ failed=[0-9]+ digest=[0-9]+$' "$TMP/$run.txt" || true)"
  [ -n "$summary" ] || { echo "test-theme-fuzz: $run printed no summary" >&2; exit 1; }
  derivations="$(sed -E 's/.* derivations=([0-9]+).*/\1/' <<<"$summary")"
  failed="$(sed -E 's/.* failed=([0-9]+).*/\1/' <<<"$summary")"
  [ "$derivations" -eq $(((SEEDS + 120) * 5)) ] || { echo "test-theme-fuzz: $run derived $derivations, not $(((SEEDS + 120) * 5))" >&2; exit 1; }
  [ "$failed" -eq 0 ] || { grep '^FAIL' "$TMP/$run.txt" >&2; exit 1; }
done

cmp "$TMP/dotnet.txt" "$TMP/node.txt" || { diff "$TMP/dotnet.txt" "$TMP/node.txt" | head -20 >&2 || true; echo "test-theme-fuzz: .NET and node differ" >&2; exit 1; }
echo "test-theme-fuzz: OK — $(tail -1 "$TMP/dotnet.txt") on .NET and Fable (node), byte-identical"
