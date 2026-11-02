#!/usr/bin/env bash
#
# ADR GAME-RENDER-02.4 D11 (FS.GG.Game#789): theme colour derivation is deterministic. The probe resolves the baked
# five presets and fresh derivations of their recipes, exports and re-imports DTCG, derives 48 custom seeds, maps an
# OKLCH grid into sRGB and runs the full 8-bit round trip, on .NET and on Fable (node). The project compiles the bounded
# Fable view of FS.GG.Game.Render, so the Fable run is the code a browser consumer gets.
#
# It asserts its SUBJECT before its verdict: both runs must print the summary line, report at least MIN_CHECKS checks and
# zero failures, agree byte for byte, and equal the committed expected output, which the Windows job (.NET on Windows)
# compares against too.
#
#   usage: scripts/test-theme-determinism.sh [--dotnet-only]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/tests/Game.Render.Theme.Determinism"
EXPECTED="$PROJECT/expected.txt"
MIN_CHECKS=15
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

summary_ok() {
  local run=$1 summary checks failed
  # .NET on Windows ends lines with CRLF, and a Windows checkout may carry expected.txt with CRLF: compare text lines.
  tr -d '\r' <"$TMP/$run.txt" >"$TMP/$run.lf" && mv "$TMP/$run.lf" "$TMP/$run.txt"
  summary="$(grep -E '^theme-determinism checks=[0-9]+ failed=[0-9]+ digest=[0-9]+$' "$TMP/$run.txt" || true)"
  [ -n "$summary" ] || { echo "test-theme-determinism: $run printed no summary" >&2; exit 1; }
  checks="$(sed -E 's/^theme-determinism checks=([0-9]+).*/\1/' <<<"$summary")"
  failed="$(sed -E 's/.* failed=([0-9]+).*/\1/' <<<"$summary")"
  [ "$checks" -ge "$MIN_CHECKS" ] || { echo "test-theme-determinism: $run ran $checks checks, fewer than $MIN_CHECKS" >&2; exit 1; }
  [ "$failed" -eq 0 ] || { grep '^FAIL' "$TMP/$run.txt" >&2; exit 1; }
  tr -d '\r' <"$EXPECTED" >"$TMP/expected.txt"
  cmp "$TMP/expected.txt" "$TMP/$run.txt" || { diff "$TMP/expected.txt" "$TMP/$run.txt" >&2 || true; echo "test-theme-determinism: $run differs from $EXPECTED" >&2; exit 1; }
}

cd "$REPO_ROOT"
dotnet run --project "$PROJECT" -c Release >"$TMP/dotnet.txt"
summary_ok dotnet

if [ "${1:-}" = "--dotnet-only" ]; then
  echo "test-theme-determinism: OK — $(tail -1 "$TMP/dotnet.txt") on .NET ($(uname -s)), equal to expected.txt"
  exit 0
fi

dotnet tool restore >/dev/null
dotnet fable "$PROJECT" --outDir "$TMP/js" --noCache >"$TMP/fable.log" 2>&1 || { cat "$TMP/fable.log" >&2; exit 1; }
printf '{"type":"module"}\n' >"$TMP/js/package.json"
node "$TMP/js/Program.js" >"$TMP/node.txt"
summary_ok node

cmp "$TMP/dotnet.txt" "$TMP/node.txt" || { diff "$TMP/dotnet.txt" "$TMP/node.txt" >&2 || true; exit 1; }
echo "test-theme-determinism: OK — $(tail -1 "$TMP/dotnet.txt") on .NET and Fable (node), byte-identical and equal to expected.txt"
