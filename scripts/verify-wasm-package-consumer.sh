#!/usr/bin/env bash
set -euo pipefail
export DOTNET_PROCESSOR_COUNT=1 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-}"
value="${2:-}"
[[ "$mode" == --custody || "$mode" == --feed-only ]] || {
  echo "usage: verify-wasm-package-consumer.sh --custody <directory> | --feed-only" >&2; exit 2;
}
work="$(mktemp -d "${TMPDIR:-/tmp}/wasm-package-consumer.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/sdk-artifacts" "$work/modules" "$work/lock-source" "$work/consumer/browser"
manifest="$work/release-manifest.json"

if [[ "$mode" == --custody ]]; then
  custody="$(realpath -e "${value:?--custody requires a directory}")"
  cp "$custody/release-manifest.json" "$manifest"
  python3 "$repo/scripts/wasm-release/release_manifest.py" verify \
    --custody "$custody" --manifest "$manifest" --source "$(git -C "$repo" rev-parse HEAD)"
  version="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["version"])' "$manifest")"
  package_source="$custody"
  archive="$custody/fsgg-wasm-sdk-$version.tar.gz"
elif [[ "$mode" == --feed-only ]]; then
  [[ -z "$value" && -z "${WASM_RELEASE_CUSTODY:-}" && -z "${WASM_SOURCE3_FEED:-}" ]] || {
    echo "feed-only mode refuses local package/custody inputs" >&2; exit 2;
  }
  package_source="${WASM_PACKAGE_FEED:?feed-only requires WASM_PACKAGE_FEED}"
  release_base="${WASM_RELEASE_BASE_URL:?feed-only requires WASM_RELEASE_BASE_URL}"
  [[ "$package_source" =~ ^https:// && "$release_base" =~ ^https:// ]] || {
    echo "feed-only sources must be HTTPS URLs" >&2; exit 2;
  }
  curl --fail --location --proto '=https' --tlsv1.2 "$release_base/release-manifest.json" --output "$manifest"
  python3 "$repo/scripts/wasm-release/release_manifest.py" verify-identity --manifest "$manifest"
  version="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["version"])' "$manifest")"
  archive="$work/sdk-artifacts/fsgg-wasm-sdk-$version.tar.gz"
  curl --fail --location --proto '=https' --tlsv1.2 "$release_base/$(basename "$archive")" --output "$archive"
  python3 - "$manifest" "$archive" <<'PY'
import hashlib,json,pathlib,sys
manifest=json.loads(pathlib.Path(sys.argv[1]).read_text())
archive=pathlib.Path(sys.argv[2])
row=next(x for x in manifest['artifacts'] if x['file']==archive.name)
if hashlib.sha256(archive.read_bytes()).hexdigest()!=row['sha256']:
    raise SystemExit('published SDK archive hash mismatch')
PY
fi

if [[ -n "${WASM_WASI_SDK_ROOT:-}" ]]; then
  wasi="$(realpath -e "$WASM_WASI_SDK_ROOT")"
else
  "$repo/sdk/wasm/toolchains/acquire-wasi-sdk.sh" "$work/wasi"
  wasi="$work/wasi/wasi-sdk-34.0-x86_64-linux"
fi
"$repo/sdk/wasm/verify-sdk.sh" "$archive" "$wasi" "$work/modules"
# Consumer guest controls are inputs only, compiled away from producer source.
for control in ordinary bad-descriptor bad-input wrong-version; do
  define=()
  [[ "$control" != bad-descriptor ]] || define=(-DBAD_DESCRIPTOR)
  [[ "$control" != bad-input ]] || define=(-DBAD_INPUT)
  [[ "$control" != wrong-version ]] || define=(-DWRONG_VERSION)
  "$wasi/bin/clang" --target=wasm32 -std=c17 -Oz -nostdlib -mno-bulk-memory -mno-reference-types -mno-multivalue "${define[@]}" \
    "$repo/tests/Wasm.PackageConsumer/browser/fixtures/connected-controls.c" \
    -Wl,--no-entry -Wl,--export-memory -Wl,--initial-memory=2097152 -Wl,--max-memory=8388608 \
    -Wl,--export=sc2c_abi_version -Wl,--export=sc2c_alloc -Wl,--export=sc2c_free \
    -Wl,--export=sc2c_initialize -Wl,--export=sc2c_process -Wl,--export=sc2c_shutdown \
    -o "$work/modules/control-$control.wasm"
done

python3 - "$work/NuGet.Config" "$package_source" <<'PY'
import sys
import xml.etree.ElementTree as ET
public = "https://api.nuget.org/v3/index.json"
selected = sys.argv[2]
root = ET.Element("configuration")
sources = ET.SubElement(root, "packageSources")
ET.SubElement(sources, "clear")
ET.SubElement(sources, "add", key="wasm", value=selected)
mapping = ET.SubElement(root, "packageSourceMapping")
wasm = ET.SubElement(mapping, "packageSource", key="wasm")
ET.SubElement(wasm, "package", pattern="FS.GG.Wasm.*")
# NuGet deduplicates identical source URLs before applying source mapping.
if selected.rstrip("/") == public:
    dependency = wasm
else:
    ET.SubElement(sources, "add", key="dependencies", value=public)
    dependency = ET.SubElement(mapping, "packageSource", key="dependencies")
ET.SubElement(dependency, "package", pattern="FSharp.Core")
ET.ElementTree(root).write(sys.argv[1], encoding="unicode")
PY
export WasmCandidateVersion="$version"
cp "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj" "$work/lock-source/"
cp "$repo/tests/Wasm.PackageConsumer/Program.fs" "$work/lock-source/"
NUGET_PACKAGES="$work/lock-packages" dotnet restore "$work/lock-source/Consumer.fsproj" \
  --configfile "$work/NuGet.Config" --use-lock-file --force-evaluate --no-http-cache
cp "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj" "$work/consumer/"
cp "$repo/tests/Wasm.PackageConsumer/Program.fs" "$work/consumer/"
cp "$work/lock-source/packages.lock.json" "$work/consumer/"
cp -a "$repo/tests/Wasm.PackageConsumer/browser/." "$work/consumer/browser/"
NUGET_PACKAGES="$work/consumer-packages" dotnet restore "$work/consumer/Consumer.fsproj" \
  --configfile "$work/NuGet.Config" --locked-mode --no-http-cache
if grep -Fq "$repo/src/" "$work/consumer/obj/project.assets.json"; then
  echo "fresh consumer resolved a producer source path" >&2; exit 3
fi
NUGET_PACKAGES="$work/consumer-packages" dotnet build "$work/consumer/Consumer.fsproj" -c Release --no-restore --nologo
output="$work/consumer/public/sub/app"
for asset in module-worker.mjs worker-client.mjs policy/WorkerEntry.js policy/Invocation.js policy/Admission.js; do
  test -f "$output/_content/FS.GG.Wasm.Browser/$asset"
done
NUGET_PACKAGES="$work/consumer-packages" dotnet tool run fable "$work/consumer/Consumer.fsproj" --outDir "$output/fable" --noCache
node "$output/fable/Program.js" | grep -Fxq 'fresh-worker:fresh-package-host:false'
cp "$work/consumer/browser/index.html" "$work/consumer/browser/package-consumer.mjs" "$output/"
mkdir -p "$output/modules"
cp "$work/modules/"*.wasm "$output/modules/"
printf '%s  %s\n' 1557e76b7a97b8e301c31c1e082c9060d8b66258a23308dcf9d2d17dbfd9de7d "$work/consumer/browser/fixtures/bar-conformance.wasm" | sha256sum --check --strict
cp "$work/consumer/browser/fixtures/bar-conformance.wasm" "$output/modules/"
(
  cd "$work/consumer/browser"
  npm ci
  ./run.sh "$work/consumer/public"
)
lock_sha="$(sha256sum "$work/consumer/packages.lock.json" | cut -d' ' -f1)"
printf 'wasm-package-consumer: mode=%s version=%s lock-sha256=%s modules=rust2,c4 base=/sub/app publication=none\n' \
  "${mode#--}" "$version" "$lock_sha"
