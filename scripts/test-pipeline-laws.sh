#!/usr/bin/env bash
#
# The laws of the standard input pipeline (FS.GG.Game ADR 0001, P1-P20 with P7b, P10b and P18b) on .NET and on Fable
# (node), with byte-identical output (FS.GG.Game#788). The project compiles the bounded Fable view of FS.GG.Game.Render,
# so the Fable run is the code a browser consumer gets.
#
# It asserts its SUBJECT before its verdict: both runs must print the summary line, report at least MIN_CHECKS checks
# and zero failures, and agree byte for byte.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$REPO_ROOT/tests/Game.Render.Pipeline.Laws"
MIN_CHECKS=70
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

cd "$REPO_ROOT"
dotnet tool restore >/dev/null
dotnet run --project "$PROJECT" -c Release >"$TMP/dotnet.txt"
dotnet fable "$PROJECT" --outDir "$TMP/js" --noCache >"$TMP/fable.log" 2>&1 || { cat "$TMP/fable.log" >&2; exit 1; }
printf '{"type":"module"}\n' >"$TMP/js/package.json"
node "$TMP/js/Program.js" >"$TMP/node.txt"

for run in dotnet node; do
  summary="$(grep -E '^pipeline-laws checks=[0-9]+ failed=[0-9]+ digest=[0-9]+$' "$TMP/$run.txt" || true)"
  [ -n "$summary" ] || { echo "test-pipeline-laws: $run printed no summary" >&2; exit 1; }
  checks="$(sed -E 's/^pipeline-laws checks=([0-9]+).*/\1/' <<<"$summary")"
  failed="$(sed -E 's/.* failed=([0-9]+).*/\1/' <<<"$summary")"
  [ "$checks" -ge "$MIN_CHECKS" ] || { echo "test-pipeline-laws: $run ran $checks checks, fewer than $MIN_CHECKS" >&2; exit 1; }
  [ "$failed" -eq 0 ] || { grep '^FAIL' "$TMP/$run.txt" >&2; exit 1; }
done

cmp "$TMP/dotnet.txt" "$TMP/node.txt" || { diff "$TMP/dotnet.txt" "$TMP/node.txt" >&2 || true; exit 1; }
echo "test-pipeline-laws: OK — $(tail -1 "$TMP/dotnet.txt") on .NET and Fable (node), byte-identical"
