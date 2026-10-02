#!/usr/bin/env bash
set -euo pipefail
version=34.0
archive="wasi-sdk-${version}-x86_64-linux.tar.gz"
archive_sha256=b761e3a0721dbae9c09a0059e5fdb2bf917d1b4a8a7b430fb3b5aafb0984b2c4
url="https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-34/$archive"
destination="${1:?usage: acquire-wasi-sdk.sh <private-destination>}"
mkdir -p "$destination"
source_archive="${WASM_WASI_SDK_ARCHIVE:-$destination/$archive}"
if [[ ! -f "$source_archive" ]]; then
  curl --fail --location --proto '=https' --tlsv1.2 --output "$source_archive" "$url"
fi
printf '%s  %s\n' "$archive_sha256" "$source_archive" | sha256sum --check --status || {
  echo "wasi-sdk archive identity mismatch" >&2; exit 2;
}
tar -xzf "$source_archive" -C "$destination"
toolchain="$destination/wasi-sdk-${version}-x86_64-linux"
[[ "$("$toolchain/bin/clang" --version | head -n 1)" == 'clang version 23.1.0-wasi-sdk (https://github.com/llvm/llvm-project 895aa2c896ada719451be2e3673c83da8ddf1141)' ]]
printf 'wasi-sdk-root=%s\n' "$toolchain"
