#!/usr/bin/env bash
set -euo pipefail
kind="${1:?usage: readback.sh <org|public|assets> <custody> <output>}"
custody="$(realpath -e "${2:?missing custody}")"
output="${3:?missing output}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mkdir -p "$output"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$custody/release-manifest.json")"
download() {
  local url=$1 destination=$2
  shift 2
  for attempt in {1..60}; do
    if curl --fail --location --silent --show-error "$@" "$url" --output "$destination"; then return; fi
    [[ "$attempt" != 60 ]] || { echo "readback exhausted for $url" >&2; exit 4; }
    sleep 10
  done
}
case "$kind" in
  org|public)
    for original in "$custody"/*.nupkg; do
      file="$(basename "$original")"; id="${file%."$version".nupkg}"; lower="${id,,}"
      if [[ "$kind" == org ]]; then
        download "https://nuget.pkg.github.com/FS-GG/download/$lower/$version/$lower.$version.nupkg" "$output/$file" --user "fs-gg:${FEED_TOKEN:?FEED_TOKEN required}"
        python3 "$repo/scripts/wasm-release/release_manifest.py" compare-package --original "$original" --served "$output/$file" >> "$output/report.jsonl"
      else
        download "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg" "$output/$file"
        python3 "$repo/scripts/wasm-release/release_manifest.py" compare-package --original "$original" --served "$output/$file" --allow-nuget-signature >> "$output/report.jsonl"
      fi
    done
    ;;
  assets)
    for file in "fsgg-wasm-sdk-$version.tar.gz" release-manifest.json SHA256SUMS; do
      for attempt in {1..60}; do
        if gh release download "wasm/v$version" --repo "${GITHUB_REPOSITORY:?}" --pattern "$file" --dir "$output"; then break; fi
        [[ "$attempt" != 60 ]] || exit 4
        sleep 10
      done
      cmp "$custody/$file" "$output/$file"
    done
    ;;
  *) echo "unknown readback kind: $kind" >&2; exit 2 ;;
esac
printf 'wasm-release-readback: kind=%s version=%s verified=true\n' "$kind" "$version"
