#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
output="$(realpath -m "${1:?usage: verify-wasm-supervisor.sh <new-private-output>}")"
case "$output/" in "$repo/"*) echo 'qualification requires a private output outside source' >&2; exit 2;; esac
[[ ! -e "$output" ]] || { echo 'qualification output must be fresh' >&2; exit 2; }
mkdir -p "$output"
q="${WASM_BASELINE_QUALIFICATION_ROOT:?requires baseline feed/cache/tool root}"
custody="${WASM_CANDIDATE_CUSTODY:?requires exact packed candidate feed}"
version="${WASM_CANDIDATE_VERSION:?requires exact candidate version}"
quint="${QUINT:-quint}"
[[ "$($quint --version)" == 0.32.0 ]]
"$quint" test eng/wasm-shared/compatible-qualification.qnt --main=compatibleQualificationTest --seed=20261003 > "$output/model-controls.log" 2>&1
python3 tests/Wasm.Supervisor.Compatibility/check-receipt-mutation.py > "$output/receipt-mutation.log"
for entry in initCompatible 'initCompatible.then(readyCompatible)'; do
  name=cold; [[ "$entry" == initCompatible ]] || name=ready
  "$quint" run eng/wasm-shared/lifecycle.qnt --main=compatibleLifecycle --init="$entry" --step=compatibleStep --invariant=compatibilitySafe --max-samples=1000 --max-steps=40 --seed=20261003 > "$output/model-$name.log" 2>&1
done
python3 tests/Wasm.Lifecycle.Correspondence/regenerate-event-traces.py --check > "$output/default-traces.log"
python3 tests/Wasm.Supervisor.Compatibility/generate-compatible-traces.py --check > "$output/selected-traces.log"
export MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.401/Sdks MSBUILD_EXE_PATH=/usr/share/dotnet/sdk/10.0.401/MSBuild.dll MSBUILDDISABLENODEREUSE=1 DOTNET_PROCESSOR_COUNT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES="$q/packages"
fable="$q/tools/.store/fable/5.18.0/fable/5.18.0/tools/net10.0/any/fable.dll"
/usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" tests/Wasm.Supervisor.Compatibility/Compatibility.fsproj -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildEnableWorkloadResolver=false -p:RestoreConfigFile="$q/NuGet.Config" -p:RestorePackagesPath="$q/packages" > "$output/source-build.log" 2>&1
/usr/share/dotnet/dotnet exec tests/Wasm.Supervisor.Compatibility/bin/Debug/net10.0/Compatibility.dll > "$output/source-dotnet.log" 2>&1
/usr/share/dotnet/dotnet exec "$fable" tests/Wasm.Supervisor.Compatibility/Compatibility.fsproj --outDir "$output/source-fable" --noCache --noRestore > "$output/source-fable-build.log" 2>&1
node "$output/source-fable"/Program.js > "$output/source-fable.log" 2>&1
export NUGET_PACKAGES="$output/packages" WasmCandidateVersion="$version"
project=tests/Wasm.Supervisor.Compatibility/Installed/InstalledCompatibility.fsproj
/usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildEnableWorkloadResolver=false -p:RestoreConfigFile="$custody/NuGet.Config" -p:RestorePackagesPath="$NUGET_PACKAGES" > "$output/installed-build.log" 2>&1
/usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildEnableWorkloadResolver=false -p:RestoreLockedMode=true -p:RestoreConfigFile="$custody/NuGet.Config" -p:RestorePackagesPath="$NUGET_PACKAGES" > "$output/installed-locked-build.log" 2>&1
/usr/share/dotnet/dotnet exec tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0/InstalledCompatibility.dll > "$output/installed-dotnet.log" 2>&1
/usr/share/dotnet/dotnet exec "$fable" "$project" --outDir "$output/installed-fable/Installed" --noCache --noRestore > "$output/installed-fable-build.log" 2>&1
node "$output/installed-fable/Program.js" > "$output/installed-fable.log" 2>&1
node tests/Wasm.Supervisor.Compatibility/raw-numeric-controls.mjs "$output/installed-fable/Program.js" > "$output/installed-raw-numeric.log" 2>&1
python3 tests/Wasm.Supervisor.Compatibility/native-api-compat.py --custody "$custody" --baseline-feed "$q/feed" --packages "$q/packages" --candidate-version "$version" --sdk /usr/share/dotnet/sdk/10.0.401 > "$output/native-api.log" 2>&1
printf 'wasm-supervisor: default47+selected11 source=dotnet,fable installed=locked-dotnet,fable numeric=refused native-api=additive publication=none\n'
