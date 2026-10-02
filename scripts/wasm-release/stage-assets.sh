#!/usr/bin/env bash
set -euo pipefail
custody="$(realpath -e "${1:?usage: stage-assets.sh <custody>}")"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$custody/release-manifest.json")"
tag="wasm/v$version"
if ! gh release view "$tag" --repo "${GITHUB_REPOSITORY:?}" >/dev/null 2>&1; then
  gh release create "$tag" --repo "$GITHUB_REPOSITORY" --verify-tag --draft --title "Shared WASM $version" --notes "Unadvertised release-set staging; verification completes before publication."
fi
assets="$(gh release view "$tag" --repo "$GITHUB_REPOSITORY" --json assets --jq '.assets[].name')"
scratch="$(mktemp -d "${TMPDIR:-/tmp}/wasm-assets.XXXXXX")"
trap 'rm -rf "$scratch"' EXIT
for file in "fsgg-wasm-sdk-$version.tar.gz" release-manifest.json SHA256SUMS; do
  if grep -Fxq "$file" <<<"$assets"; then
    gh release download "$tag" --repo "$GITHUB_REPOSITORY" --pattern "$file" --dir "$scratch"
    cmp "$custody/$file" "$scratch/$file" || { echo "existing release asset differs: $file" >&2; exit 3; }
  else
    gh release upload "$tag" "$custody/$file" --repo "$GITHUB_REPOSITORY"
  fi
done
printf 'wasm-release-assets: tag=%s exact=true\n' "$tag"
