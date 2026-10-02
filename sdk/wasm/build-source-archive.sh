#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="$(cat "$repo/sdk/wasm/VERSION")"
out="${1:?usage: build-source-archive.sh <output-directory>}"
work="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-wasm-sdk-archive.XXXXXX")"
trap 'rm -rf "$work"' EXIT
root="$work/fsgg-wasm-sdk-$version"
mkdir -p "$root/sdk" "$root/examples"
cp -a "$repo/sdk/wasm" "$root/sdk/wasm"
cp -a "$repo/examples/wasm" "$root/examples/wasm"
find "$root" -type d -name target -prune -exec rm -rf {} +
find "$root" -type f -name '*.wasm' -delete
(
  cd "$root"
  find sdk examples -type f ! -name SHA256SUMS -print0 | sort -z | xargs -0 sha256sum > "$work/SHA256SUMS"
  mv "$work/SHA256SUMS" SHA256SUMS
)
mkdir -p "$out"
tar --sort=name --mtime='UTC 1970-01-01' --owner=0 --group=0 --numeric-owner -C "$work" -cf - "fsgg-wasm-sdk-$version" | gzip -n > "$out/fsgg-wasm-sdk-$version.tar.gz"
sha256sum "$out/fsgg-wasm-sdk-$version.tar.gz" > "$out/fsgg-wasm-sdk-$version.tar.gz.sha256"
printf 'sdk-archive=%s sha256=%s\n' "$out/fsgg-wasm-sdk-$version.tar.gz" "$(cut -d' ' -f1 "$out/fsgg-wasm-sdk-$version.tar.gz.sha256")"
