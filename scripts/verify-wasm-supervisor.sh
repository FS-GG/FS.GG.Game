#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
[[ -z "$(git status --porcelain)" ]] || { echo 'supervisor qualification requires clean committed source' >&2; exit 2; }
mode=custody
case "${1:-}" in
  --feed-only) mode=feed-only;shift;;
  --compile-source) mode=compile-source;shift;;
  --compile-installed) mode=compile-installed;shift;;
  --compiled) mode=compiled;shift;;
  --*) echo "unknown supervisor mode" >&2;exit 2;;
esac
output="$(realpath -m "${1:?usage: verify-wasm-supervisor.sh [--feed-only|--compile-source|--compile-installed|--compiled] PRIVATE_OUTPUT}")"
case "$output/" in "$repo/"*) echo 'qualification requires a private output outside source' >&2; exit 2;; esac
if [[ "$mode" == compiled ]]; then
  [[ -d "$output" && ! -L "$output" && "$(stat -c '%a:%u' -- "$output")" == "700:$(id -u)" ]] || { echo 'compiled evidence root must be private and owned' >&2;exit 2; }
else
  [[ ! -e "$output" ]] || { echo 'qualification output must be fresh' >&2; exit 2; }
  (umask 077;mkdir -p -- "$(dirname "$output")";mkdir -m 700 -- "$output")
fi
q="${WASM_BASELINE_QUALIFICATION_ROOT:?requires official cache/tool root}"
version="$(sed -n 's:.*<WasmSharedVersion>\([^<]*\)</WasmSharedVersion>.*:\1:p' "$repo/eng/wasm-shared/version.props")"
source_head="$(git rev-parse HEAD)"
source_tree="$(git rev-parse 'HEAD^{tree}')"
export MSBuildSDKsPath=/usr/share/dotnet/sdk/10.0.401/Sdks MSBUILD_EXE_PATH=/usr/share/dotnet/sdk/10.0.401/MSBuild.dll MSBUILDDISABLENODEREUSE=1 DOTNET_PROCESSOR_COUNT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
compiler="${WASM_FSC_COMPILER_PATH:-/usr/share/dotnet/sdk/10.0.401/FSharp/fsc.dll}"
fable="$q/tools/.store/fable/5.18.0/fable/5.18.0/tools/net10.0/any/fable.dll"
source_assembly="$repo/tests/Wasm.Supervisor.Compatibility/bin/Debug/net10.0/Compatibility.dll"
installed_assembly="$repo/tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0/InstalledCompatibility.dll"
build_header() {
  printf 'wasm-supervisor-build phase=%s source=%s tree=%s version=%s' "$1" "$source_head" "$source_tree" "$version"
}
finish_build() {
  local assembly_hash
  [[ -f "$2" && ! -L "$2" ]] || { echo 'compiled assembly missing/unsafe' >&2;return 2; }
  assembly_hash="$(sha256sum -- "$2" | cut -d' ' -f1)"
  printf 'wasm-supervisor-assembly sha256=%s\n' "$assembly_hash" >> "$1"
  printf '%s status=success\n' "$(build_header "$3")" >> "$1"
}
validate_build() {
  local log=$1 phase=$2 assembly=$3 header actual
  header="$(build_header "$phase")"
  [[ -f "$log" && ! -L "$log" && -f "$assembly" && ! -L "$assembly" ]] || { echo 'missing/unsafe earlier compile evidence' >&2;exit 2; }
  [[ "$(stat -c '%u' -- "$log")" == "$(id -u)" && "$(head -n 1 -- "$log")" == "$header" && "$(tail -n 1 -- "$log")" == "$header status=success" ]] || { echo 'earlier compile identity/status differs' >&2;exit 2; }
  actual="$(sha256sum -- "$assembly" | cut -d' ' -f1)"
  [[ "$(grep -Fxc "wasm-supervisor-assembly sha256=$actual" "$log")" == 1 ]] || { echo 'earlier compiled assembly differs' >&2;exit 2; }
}
compile_source() {
  export NUGET_PACKAGES="$q/packages"
  printf '%s\n' "$(build_header source)" > "$output/source-build.log"
  /usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" tests/Wasm.Supervisor.Compatibility/Compatibility.fsproj -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:DotnetFscCompilerPath="$compiler" -p:MSBuildEnableWorkloadResolver=false -p:RestoreConfigFile="$q/NuGet.Config" -p:RestorePackagesPath="$q/packages" >> "$output/source-build.log" 2>&1
/usr/share/dotnet/dotnet exec tests/Wasm.Supervisor.Compatibility/bin/Debug/net10.0/Compatibility.dll > "$output/source-dotnet.log" 2>&1
/usr/share/dotnet/dotnet exec "$fable" tests/Wasm.Supervisor.Compatibility/Compatibility.fsproj --outDir "$output/source-fable" --noCache --noRestore > "$output/source-fable-build.log" 2>&1
node "$output/source-fable"/Program.js > "$output/source-fable.log" 2>&1
  finish_build "$output/source-build.log" "$source_assembly" source
}
if [[ "$mode" == compile-source ]]; then compile_source;exit 0;fi
[[ "${WASM_CANDIDATE_VERSION:?requires exact candidate version}" == "$version" ]] || { echo 'selected candidate version differs' >&2;exit 2; }
if [[ "$mode" == feed-only ]]; then
  [[ -z "${WASM_CANDIDATE_CUSTODY:-}" && -z "${WASM_RELEASE_CUSTODY:-}" && -z "${WASM_SOURCE3_FEED:-}" ]] || { echo 'feed-only refuses local custody/source injection' >&2;exit 2; }
  config="${WASM_INSTALLED_CONFIG:?requires explicit feed-only configuration}"
  python3 scripts/wasm-release/promotion.py check-feed --output "$config" --feed "${WASM_PACKAGE_FEED:?}"
else
  custody="${WASM_CANDIDATE_CUSTODY:?requires exact packed candidate feed}"
  config="$custody/NuGet.Config"
fi
export WasmCandidateVersion="$version"
installed_lock="$output/installed.packages.lock.json"
compile_installed() {
  local project=tests/Wasm.Supervisor.Compatibility/Installed/InstalledCompatibility.fsproj
  printf '%s\n' "$(build_header installed-restore)" > "$output/installed-build.log"
  NUGET_PACKAGES="$output/lock-packages" NUGET_HTTP_CACHE_PATH="$output/lock-http-cache" /usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore -m:1 -nr:false -p:UseSharedCompilation=false -p:DotnetFscCompilerPath="$compiler" -p:MSBuildEnableWorkloadResolver=false -p:WasmCandidateVersion="$version" -p:NuGetLockFilePath="$installed_lock" -p:RestoreForceEvaluate=true -p:RestoreConfigFile="$config" -p:RestorePackagesPath="$output/lock-packages" >> "$output/installed-build.log" 2>&1
  printf '%s status=success\n' "$(build_header installed-restore)" >> "$output/installed-build.log"
  printf '%s\n' "$(build_header installed)" > "$output/installed-locked-build.log"
  NUGET_PACKAGES="$output/packages" NUGET_HTTP_CACHE_PATH="$output/http-cache" /usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore,Build -m:1 -nr:false -p:UseSharedCompilation=false -p:DotnetFscCompilerPath="$compiler" -p:MSBuildEnableWorkloadResolver=false -p:WasmCandidateVersion="$version" -p:NuGetLockFilePath="$installed_lock" -p:RestoreLockedMode=true -p:RestoreForce=true -p:RestoreConfigFile="$config" -p:RestorePackagesPath="$output/packages" >> "$output/installed-locked-build.log" 2>&1
  export NUGET_PACKAGES="$output/packages" NUGET_HTTP_CACHE_PATH="$output/http-cache" WasmCandidateVersion="$version"
/usr/share/dotnet/dotnet exec tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0/InstalledCompatibility.dll > "$output/installed-dotnet.log" 2>&1
/usr/share/dotnet/dotnet exec "$fable" "$project" --outDir "$output/installed-fable/Installed" --noCache --noRestore > "$output/installed-fable-build.log" 2>&1
node "$output/installed-fable/Program.js" > "$output/installed-fable.log" 2>&1
node tests/Wasm.Supervisor.Compatibility/raw-numeric-controls.mjs "$output/installed-fable/Program.js" > "$output/installed-raw-numeric.log" 2>&1
# prepare.sh seals the actual current published-baseline classification in custody.

python3 - "$installed_lock" "$output/installed.bad-hash.lock.json" <<'PYLOCK'
import json,pathlib,sys
source,target=map(pathlib.Path,sys.argv[1:]);lock=json.loads(source.read_text())
row=lock['dependencies']['net10.0']['FS.GG.Wasm.Browser'];assert row['resolved'] == __import__('os').environ['WasmCandidateVersion']
row['contentHash']='AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=='
target.write_text(json.dumps(lock,indent=2)+'\n')
PYLOCK
if /usr/share/dotnet/dotnet exec "$MSBUILD_EXE_PATH" "$project" -t:Restore -m:1 -nr:false -p:UseSharedCompilation=false -p:MSBuildEnableWorkloadResolver=false -p:WasmCandidateVersion="$version" -p:NuGetLockFilePath="$output/installed.bad-hash.lock.json" -p:RestoreLockedMode=true -p:RestoreForce=true -p:RestoreConfigFile="$config" -p:RestorePackagesPath="$NUGET_PACKAGES" > "$output/installed-lock-refusal.log" 2>&1; then
  echo 'mutated installed package content hash was accepted' >&2; exit 2
fi
grep -Fq 'NU1403' "$output/installed-lock-refusal.log"
  finish_build "$output/installed-locked-build.log" "$installed_assembly" installed
}
if [[ "$mode" == compile-installed ]]; then compile_installed;exit 0;fi
if [[ "$mode" == compiled ]]; then
  source_log="${WASM_SUPERVISOR_SOURCE_BUILD_LOG:?requires same-job source compile log}"
  validate_build "$source_log" source "$source_assembly"
  validate_build "$output/installed-locked-build.log" installed "$installed_assembly"
  for leaf in installed-build.log installed-dotnet.log installed-fable-build.log installed-fable.log installed-raw-numeric.log installed-lock-refusal.log installed.packages.lock.json; do
    [[ -f "$output/$leaf" && ! -L "$output/$leaf" ]] || { echo "missing/unsafe installed phase output" >&2;exit 2; }
  done
  source_logs="$(dirname "$source_log")"
  for leaf in source-build.log source-dotnet.log source-fable-build.log source-fable.log; do
    [[ -f "$source_logs/$leaf" && ! -L "$source_logs/$leaf" ]] || { echo 'missing/unsafe source phase log' >&2;exit 2; }
    cp -- "$source_logs/$leaf" "$output/$leaf"
  done
else
  if [[ "$mode" == custody ]]; then compile_source;fi
  compile_installed
fi
# All compilation prerequisites above must pass before model or guest work.
if [[ "$mode" != feed-only ]]; then
quint="${QUINT:-quint}"
[[ "$($quint --version)" == 0.32.0 ]]
"$quint" test --backend=typescript eng/wasm-shared/compatible-qualification.qnt --main=compatibleQualificationTest --seed=20261003 > "$output/model-controls.log" 2>&1
python3 tests/Wasm.Supervisor.Compatibility/check-receipt-mutation.py > "$output/receipt-mutation.log"
witnesses=(sawSelectedLoad sawSelectedCompile sawSelectedInitialize sawSelectedInitialized sawMixedOrdinary sawMixedOrdered sawAdmittedSnapshot sawHeldSnapshot sawHeldPromotion sawSelectedPhase sawNarrowDeadline sawNarrowOutput sawEnclosingBudget sawCandidatePrepared sawCandidateInitialized sawCandidateValidated sawSelectedFreeze sawAtomicFrozenCommit sawSelectedHistorical sawSelectedResume sawSelectedDisposal sawRefusalAfterExpiry sawInclusiveHeadExpiry sawFrozenCommitExpiryRefused)
# Fresh-Worker readiness is an explicit step, so an initialized-candidate entry is
# sampled too; it appends to the retained ready log rather than adding an evidence leaf.
for entry in initCompatible 'initCompatible.then(readyCompatible)' 'initCompatible.then(readyCandidateCompatible)'; do
  name=cold; [[ "$entry" == initCompatible ]] || name=ready
  if [[ "$entry" == *readyCandidateCompatible* ]]; then exec 3>>"$output/model-$name.log"; else exec 3>"$output/model-$name.log"; fi
  "$quint" run --backend=typescript eng/wasm-shared/lifecycle.qnt --main=compatibleLifecycle --init="$entry" --step=compatibleStep --invariant=compatibilitySafe --witnesses "${witnesses[@]}" --max-samples=1000 --max-steps=40 --seed=20261003 >&3 2>&1
  exec 3>&-
done
for witness in "${witnesses[@]}"; do
  grep -Eq "^${witness} was witnessed in [1-9][0-9]* trace" "$output/model-cold.log" "$output/model-ready.log"
done
python3 tests/Wasm.Lifecycle.Correspondence/regenerate-event-traces.py --check > "$output/default-traces.log"
python3 tests/Wasm.Supervisor.Compatibility/generate-compatible-traces.py --check > "$output/selected-traces.log"
fi
[[ -z "$(git status --porcelain)" ]] || { echo 'supervisor qualification dirtied source' >&2; exit 2; }
printf 'wasm-supervisor: mode=%s default58+selected16 installed=locked-dotnet,fable numeric=refused publication=none\n' "$mode"
