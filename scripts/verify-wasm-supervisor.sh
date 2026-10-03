#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
[[ -z "$(git status --porcelain)" ]] || { echo 'supervisor qualification requires clean committed source' >&2; exit 2; }
mode=custody
if [[ "${1:-}" == --feed-only ]]; then mode=feed-only; shift; fi
output="$(realpath -m "${1:?usage: verify-wasm-supervisor.sh [--feed-only] <new-private-output>}")"
case "$output/" in "$repo/"*) echo 'qualification requires a private output outside source' >&2; exit 2;; esac
[[ ! -e "$output" ]] || { echo 'qualification output must be fresh' >&2; exit 2; }
mkdir -p "$output"
q="${WASM_BASELINE_QUALIFICATION_ROOT:?requires baseline feed/cache/tool root}"
if [[ "$mode" == feed-only ]]; then
  [[ -z "${WASM_CANDIDATE_CUSTODY:-}" && -z "${WASM_RELEASE_CUSTODY:-}" && -z "${WASM_SOURCE3_FEED:-}" ]] || { echo "feed-only refuses local custody/source injection" >&2; exit 2; }
  config="${WASM_INSTALLED_CONFIG:?requires explicit feed-only configuration}"
  python3 scripts/wasm-release/promotion.py check-feed --output "$config" --feed "${WASM_PACKAGE_FEED:?}"
else
  custody="${WASM_CANDIDATE_CUSTODY:?requires exact packed candidate feed}"
  config="$custody/NuGet.Config"
fi
version="${WASM_CANDIDATE_VERSION:?requires exact candidate version}"
if [[ "$mode" == custody ]]; then
quint="${QUINT:-quint}"
[[ "$($quint --version)" == 0.32.0 ]]
"$quint" test --backend=typescript eng/wasm-shared/compatible-qualification.qnt --main=compatibleQualificationTest --seed=20261003 > "$output/model-controls.log" 2>&1
python3 tests/Wasm.Supervisor.Compatibility/check-receipt-mutation.py > "$output/receipt-mutation.log"
witnesses=(sawSelectedLoad sawSelectedCompile sawSelectedInitialize sawSelectedInitialized sawMixedOrdinary sawMixedOrdered sawAdmittedSnapshot sawHeldSnapshot sawHeldPromotion sawSelectedPhase sawNarrowDeadline sawNarrowOutput sawEnclosingBudget sawCandidatePrepared sawCandidateInitialized sawCandidateValidated sawSelectedFreeze sawAtomicFrozenCommit sawSelectedHistorical sawSelectedResume sawSelectedDisposal sawRefusalAfterExpiry sawInclusiveHeadExpiry sawFrozenCommitExpiryRefused)
for entry in initCompatible 'initCompatible.then(readyCompatible)'; do
  name=cold; [[ "$entry" == initCompatible ]] || name=ready
  "$quint" run --backend=typescript eng/wasm-shared/lifecycle.qnt --main=compatibleLifecycle --init="$entry" --step=compatibleStep --invariant=compatibilitySafe --witnesses "${witnesses[@]}" --max-samples=1000 --max-steps=40 --seed=20261003 > "$output/model-$name.log" 2>&1
done
for witness in "${witnesses[@]}"; do
  grep -Eq "^${witness} was witnessed in [1-9][0-9]* trace" "$output/model-cold.log" "$output/model-ready.log"
done
python3 tests/Wasm.Lifecycle.Correspondence/regenerate-event-traces.py --check > "$output/default-traces.log"
python3 tests/Wasm.Supervisor.Compatibility/generate-compatible-traces.py --check > "$output/selected-traces.log"
fi
export MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.401/Sdks MSBUILD_EXE_PATH=/usr/share/dotnet/sdk/10.0.401/MSBuild.dll MSBUILDDISABLENODEREUSE=1 DOTNET_PROCESSOR_COUNT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 NUGET_PACKAGES="$q/packages"
compiler="${WASM_FSC_COMPILER_PATH:-/usr/share/dotnet/sdk/10.0.401/FSharp/fsc.dll}"
fable="$q/tools/.store/fable/5.18.0/fable/5.18.0/tools/net10.0/any/fable.dll"
if [[ "$mode" == custody ]]; then
/usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" tests/Wasm.Supervisor.Compatibility/Compatibility.fsproj -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:DotnetFscCompilerPath="$compiler" -p:MSBuildEnableWorkloadResolver=false -p:RestoreConfigFile="$q/NuGet.Config" -p:RestorePackagesPath="$q/packages" > "$output/source-build.log" 2>&1
/usr/share/dotnet/dotnet exec tests/Wasm.Supervisor.Compatibility/bin/Debug/net10.0/Compatibility.dll > "$output/source-dotnet.log" 2>&1
/usr/share/dotnet/dotnet exec "$fable" tests/Wasm.Supervisor.Compatibility/Compatibility.fsproj --outDir "$output/source-fable" --noCache --noRestore > "$output/source-fable-build.log" 2>&1
node "$output/source-fable"/Program.js > "$output/source-fable.log" 2>&1
fi
export NUGET_PACKAGES="$output/packages" WasmCandidateVersion="$version"
project=tests/Wasm.Supervisor.Compatibility/Installed/InstalledCompatibility.fsproj
installed_lock="$output/installed.packages.lock.json"
/usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:DotnetFscCompilerPath="$compiler" -p:MSBuildEnableWorkloadResolver=false -p:NuGetLockFilePath="$installed_lock" -p:RestoreForceEvaluate=true -p:RestoreConfigFile="$config" -p:RestorePackagesPath="$NUGET_PACKAGES" > "$output/installed-build.log" 2>&1
/usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:DotnetFscCompilerPath="$compiler" -p:MSBuildEnableWorkloadResolver=false -p:NuGetLockFilePath="$installed_lock" -p:RestoreLockedMode=true -p:RestoreForce=true -p:RestoreConfigFile="$config" -p:RestorePackagesPath="$NUGET_PACKAGES" > "$output/installed-locked-build.log" 2>&1
/usr/share/dotnet/dotnet exec tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0/InstalledCompatibility.dll > "$output/installed-dotnet.log" 2>&1
/usr/share/dotnet/dotnet exec "$fable" "$project" --outDir "$output/installed-fable/Installed" --noCache --noRestore > "$output/installed-fable-build.log" 2>&1
node "$output/installed-fable/Program.js" > "$output/installed-fable.log" 2>&1
node tests/Wasm.Supervisor.Compatibility/raw-numeric-controls.mjs "$output/installed-fable/Program.js" > "$output/installed-raw-numeric.log" 2>&1
if [[ "$mode" == custody ]]; then
python3 tests/Wasm.Supervisor.Compatibility/native-api-compat.py --custody "$custody" --baseline-feed "$q/feed" --packages "$q/packages" --candidate-version "$version" --sdk /usr/share/dotnet/sdk/10.0.401 > "$output/native-api.log" 2>&1
fi
python3 - "$installed_lock" "$output/installed.bad-hash.lock.json" <<'PYLOCK'
import json,pathlib,sys
source,target=map(pathlib.Path,sys.argv[1:]);lock=json.loads(source.read_text())
row=lock['dependencies']['net10.0']['FS.GG.Wasm.Browser'];assert row['resolved'] == __import__('os').environ['WasmCandidateVersion']
row['contentHash']='AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=='
target.write_text(json.dumps(lock,indent=2)+'\n')
PYLOCK
if /usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore -m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildEnableWorkloadResolver=false -p:NuGetLockFilePath="$output/installed.bad-hash.lock.json" -p:RestoreLockedMode=true -p:RestoreForce=true -p:RestoreConfigFile="$config" -p:RestorePackagesPath="$NUGET_PACKAGES" > "$output/installed-lock-refusal.log" 2>&1; then
  echo 'mutated installed package content hash was accepted' >&2; exit 2
fi
grep -Fq 'NU1403' "$output/installed-lock-refusal.log"
[[ -z "$(git status --porcelain)" ]] || { echo 'supervisor qualification dirtied source' >&2; exit 2; }
printf 'wasm-supervisor: mode=%s default47+selected13 installed=locked-dotnet,fable numeric=refused publication=none\n' "$mode"
