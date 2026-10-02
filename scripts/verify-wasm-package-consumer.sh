#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-}"
value="${2:-}"
[[ "$mode" == --custody || "$mode" == --feed-only ]] || {
  echo "usage: verify-wasm-package-consumer.sh --custody <directory> | --feed-only" >&2; exit 2;
}
work="$(mktemp -d "${TMPDIR:-/tmp}/wasm-package-consumer.XXXXXX")"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/sdk-artifacts" "$work/modules" "$work/lock-source" "$work/consumer/browser"
version="0.1.1"
manifest="$work/release-manifest.json"

if [[ "$mode" == --custody ]]; then
  custody="$(realpath -e "${value:?--custody requires a directory}")"
  cp "$custody/release-manifest.json" "$manifest"
  python3 "$repo/scripts/wasm-release/release_manifest.py" verify \
    --custody "$custody" --manifest "$manifest" --source "$(git -C "$repo" rev-parse HEAD)"
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

cat > "$work/NuGet.Config" <<CONFIG
<configuration>
  <packageSources><clear/><add key="wasm" value="$package_source"/><add key="dependencies" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><packageSource key="wasm"><package pattern="FS.GG.Wasm.*"/></packageSource><packageSource key="dependencies"><package pattern="FSharp.Core"/></packageSource></packageSourceMapping>
</configuration>
CONFIG
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
for asset in module-worker.mjs worker-client.mjs; do
  test -f "$output/_content/FS.GG.Wasm.Browser/$asset"
done
NUGET_PACKAGES="$work/consumer-packages" dotnet tool run fable "$work/consumer/Consumer.fsproj" --outDir "$output/fable" --noCache
node "$output/fable/Program.js" | grep -Fxq 'fresh-worker:fresh-package-host:false'
cp "$work/consumer/browser/index.html" "$work/consumer/browser/package-consumer.mjs" "$output/"
mkdir -p "$output/modules"
cp "$work/modules/"*.wasm "$output/modules/"
(
  cd "$work/consumer/browser"
  npm ci
  ./run.sh "$work/consumer/public"
)
lock_sha="$(sha256sum "$work/consumer/packages.lock.json" | cut -d' ' -f1)"
printf 'wasm-package-consumer: mode=%s version=%s lock-sha256=%s modules=rust2,c4 base=/sub/app publication=none\n' \
  "${mode#--}" "$version" "$lock_sha"
