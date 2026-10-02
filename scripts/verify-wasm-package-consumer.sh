#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/wasm-package-consumer.XXXXXX")"
trap 'rm -rf "$work"' EXIT
feed="${WASM_SOURCE3_FEED:-$work/feed}"
mkdir -p "$feed" "$work/sdk-artifacts" "$work/modules" "$work/lock-source" "$work/consumer/browser"
export NUGET_PACKAGES="$work/nuget-packages"

dotnet pack "$repo/src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj" -c Release -o "$feed" --nologo
dotnet pack "$repo/src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj" -c Release -o "$feed" --nologo
contracts="$feed/FS.GG.Wasm.Contracts.0.1.0-source.3.nupkg"
browser="$feed/FS.GG.Wasm.Browser.0.1.0-source.3.nupkg"
[[ -f "$contracts" && -f "$browser" ]]
unzip -Z1 "$browser" > "$work/browser-files.txt"
for path in \
  fable/RuntimeProtocol.fsi fable/Admission.fsi fable/Invocation.fsi fable/WorkerEntry.fsi \
  fable/Lifecycle.fsi fable/Host.fsi fable/js/wasm-primitives.js \
  contentFiles/any/any/_content/FS.GG.Wasm.Browser/module-worker.mjs \
  contentFiles/any/any/_content/FS.GG.Wasm.Browser/worker-client.mjs \
  buildTransitive/FS.GG.Wasm.Browser.targets; do
  grep -Fxq "$path" "$work/browser-files.txt"
done

"$repo/sdk/wasm/build-source-archive.sh" "$work/sdk-artifacts"
archive="$work/sdk-artifacts/fsgg-wasm-sdk-0.1.0-source.3.tar.gz"
if [[ -n "${WASM_WASI_SDK_ROOT:-}" ]]; then
  wasi="$(realpath -e "$WASM_WASI_SDK_ROOT")"
else
  "$repo/sdk/wasm/toolchains/acquire-wasi-sdk.sh" "$work/wasi"
  wasi="$work/wasi/wasi-sdk-34.0-x86_64-linux"
fi
"$repo/sdk/wasm/verify-sdk.sh" "$archive" "$wasi" "$work/modules"

cat > "$work/NuGet.Config" <<CONFIG
<configuration>
  <packageSources><clear/><add key="candidate" value="$feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><packageSource key="candidate"><package pattern="FS.GG.Wasm.*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping>
</configuration>
CONFIG
# NuGet package archives include creation timestamps. Generate a lock from the exact
# candidate bytes, then prove a second clean root can consume those bytes in locked mode.
cp "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj" "$work/lock-source/"
cp "$repo/tests/Wasm.PackageConsumer/Program.fs" "$work/lock-source/"
dotnet restore "$work/lock-source/Consumer.fsproj" --configfile "$work/NuGet.Config" --use-lock-file --force-evaluate
cp "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj" "$work/consumer/"
cp "$repo/tests/Wasm.PackageConsumer/Program.fs" "$work/consumer/"
cp "$work/lock-source/packages.lock.json" "$work/consumer/"
cp -a "$repo/tests/Wasm.PackageConsumer/browser/." "$work/consumer/browser/"
dotnet restore "$work/consumer/Consumer.fsproj" --configfile "$work/NuGet.Config" --locked-mode
dotnet build "$work/consumer/Consumer.fsproj" -c Release --no-restore --nologo
output="$work/consumer/public/sub/app"
for asset in module-worker.mjs worker-client.mjs; do
  test -f "$output/_content/FS.GG.Wasm.Browser/$asset"
done
dotnet tool run fable "$work/consumer/Consumer.fsproj" --outDir "$output/fable" --noCache
node "$output/fable/Program.js" | grep -Fxq 'fresh-worker:fresh-package-host:false'
cp "$work/consumer/browser/index.html" "$work/consumer/browser/package-consumer.mjs" "$output/"
mkdir -p "$output/modules"
cp "$work/modules/"*.wasm "$output/modules/"
(
  cd "$work/consumer/browser"
  npm ci
  ./run.sh "$work/consumer/public"
)
printf 'wasm-package-consumer: sdk-archive-sha256=%s contracts-sha256=%s browser-sha256=%s modules=rust2,c4 base=/sub/app publication=none\n' \
  "$(sha256sum "$archive" | cut -d' ' -f1)" "$(sha256sum "$contracts" | cut -d' ' -f1)" "$(sha256sum "$browser" | cut -d' ' -f1)"
