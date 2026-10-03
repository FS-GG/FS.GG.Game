#!/usr/bin/env bash
set -euo pipefail
custody="$(realpath -e "${1:?usage: stage-assets.sh <custody>}")"
version="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$custody/release-manifest.json")"
tag="wasm/v$version"
# Only the adapter creates a causally bound tag/draft. Errors are never absence.
release="$(gh api "repos/${GITHUB_REPOSITORY:?}/releases/tags/wasm%2Fv$version")"
python3 - "$release" "$tag" <<'PY'
import json,sys
release=json.loads(sys.argv[1]);assert release['tag_name']==sys.argv[2] and release['draft'] and not release['prerelease']
assert len([r for r in release['assets'] if r['name']=='promotion-binding.json'])==1,'missing durable transaction'
PY
assets="$(python3 -c 'import json,sys;print("\n".join(r["name"] for r in json.loads(sys.argv[1])["assets"]))' "$release")"
scratch="$(mktemp -d "${TMPDIR:-/tmp}/wasm-assets.XXXXXX")"
trap 'rm -rf "$scratch"' EXIT
for file in "fsgg-wasm-sdk-$version.tar.gz" release-manifest.json SHA256SUMS; do
  if grep -Fxq "$file" <<<"$assets"; then
    timeout 120 gh release download "$tag" --repo "$GITHUB_REPOSITORY" --pattern "$file" --dir "$scratch"
    cmp "$custody/$file" "$scratch/$file" || { echo "existing release asset differs: $file" >&2; exit 3; }
  else
    timeout 120 gh release upload "$tag" "$custody/$file" --repo "$GITHUB_REPOSITORY"
  fi
done
printf 'wasm-release-assets: tag=%s exact=true\n' "$tag"
