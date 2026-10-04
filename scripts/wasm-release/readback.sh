#!/usr/bin/env bash
set -euo pipefail
kind="${1:?usage: readback.sh <org|public|assets> <custody> <output> [exact-package-id]}"
custody="$(realpath -e "${2:?missing custody}")"
output="${3:?missing output}"
selected="${4:-}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mkdir -p "$output"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$custody/release-manifest.json")"
[[ -z "$selected" || "$selected" == FS.GG.Wasm.Contracts || "$selected" == FS.GG.Wasm.Browser ]] || exit 2
# One wall-clock bound includes requests AND backoff, across both members/assets.
deadline=$((SECONDS + 600))
if [[ -n "${WASM_READBACK_DEADLINE:-}" ]]; then
  [[ "$WASM_READBACK_DEADLINE" =~ ^[0-9]+$ ]] || exit 2
  remaining=$((WASM_READBACK_DEADLINE - $(date +%s)))
  (( remaining > 0 )) || exit 4
  (( remaining >= 600 )) || deadline=$((SECONDS + remaining))
fi
download() {
  local url=$1 destination=$2 remaining
  shift 2
  while (( SECONDS < deadline )); do
    remaining=$((deadline - SECONDS))
    (( remaining <= 30 )) || remaining=30
    if curl --max-filesize 536870912 --fail --location --silent --show-error --proto '=https' --tlsv1.2 --connect-timeout 10 --max-time "$remaining" "$@" "$url" --output "$destination"; then return; fi
    (( SECONDS + 10 < deadline )) || break
    sleep 10
  done
  echo 'readback stage deadline exhausted; transaction remains incomplete' >&2
  return 4
}
case "$kind" in
  org|public)
    for id in FS.GG.Wasm.Contracts FS.GG.Wasm.Browser; do
      [[ -z "$selected" || "$selected" == "$id" ]] || continue
      file="$id.$version.nupkg"; original="$custody/$file"; lower="${id,,}"
      [[ -f "$original" ]] || exit 2
      if [[ "$kind" == org ]]; then
        download "https://nuget.pkg.github.com/FS-GG/download/$lower/$version/$lower.$version.nupkg" "$output/$file" --user "fs-gg:${FEED_TOKEN:?FEED_TOKEN required}"
        python3 "$repo/scripts/wasm-release/release_manifest.py" compare-package --original "$original" --served "$output/$file" >> "$output/report.jsonl"
      else
        download "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg" "$output/$file"
        if [[ "$version" == 0.3.0 ]]; then
          python3 "$repo/scripts/wasm-release/promotion.py" verify-signature --archive "$output/$file" > "$output/$id.signature.log"
        fi
        python3 "$repo/scripts/wasm-release/release_manifest.py" compare-package --original "$original" --served "$output/$file" --allow-nuget-signature >> "$output/report.jsonl"
      fi
    done
    ;;
  assets)
    [[ -z "$selected" ]] || exit 2
    python3 -B "$repo/scripts/wasm-release/promotion.py" asset-readback --reviewed "${RUNNER_TEMP:?}/reviewed.json" --custody "$custody" --output "$output"
    ;;
  *) echo "unknown readback kind: $kind" >&2; exit 2 ;;
esac
printf 'wasm-release-readback: kind=%s version=%s verified=true\n' "$kind" "$version"
