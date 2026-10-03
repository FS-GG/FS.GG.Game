#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/wasm-lifecycle.XXXXXX")"
proof="$work/model-proof"
mkdir -p "$proof"
retain_model_proof() {
  local status=$?
  trap - EXIT
  local retained=0
  python3 - "$proof" "${WASM_LIFECYCLE_EVIDENCE_DIR:-}" "$status" "$repo" <<'PYPROOF' || retained=$?
import hashlib,json,pathlib,shutil,subprocess,sys
source,destination,status,repo=sys.argv[1:]
source=pathlib.Path(source);repo=pathlib.Path(repo)
allowed={"qualification-status.json","quint-sampled-init.txt","quint-sampled-initReady.txt","quint-sampled-init.itf.json","quint-sampled-initReady.itf.json"}
receipt={"schema":"fsgg.wasm.public-model-evidence/v1","exitCode":int(status),"sourceHead":subprocess.check_output(["git","-C",str(repo),"rev-parse","HEAD"],text=True).strip(),"modelSha256":hashlib.sha256((repo/"eng/wasm-shared/lifecycle.qnt").read_bytes()).hexdigest(),"quintVersion":"0.32.0","backend":"typescript","seed":20261002,"maxSamplesPerEntry":1000,"maxSteps":40}
(source/"qualification-status.json").write_text(json.dumps(receipt,sort_keys=True)+"\n")
files=list(source.iterdir())
assert len(files)<=5 and all(p.name in allowed and p.is_file() and not p.is_symlink() for p in files),"unexpected model evidence leaf"
assert sum(p.stat().st_size for p in files)<16*1024*1024,"model evidence exceeds16MiB"
if destination:
 target=pathlib.Path(destination);target.mkdir(parents=True,exist_ok=True)
 assert not any(target.iterdir()),"model evidence destination is not empty"
 for p in files:shutil.copyfile(p,target/p.name)
 print("model-evidence: retainedFiles="+str(len(files))+" bytes="+str(sum(p.stat().st_size for p in files)))
if int(status):
 for p in files:
  if p.suffix==".txt":print(p.name+"\n"+p.read_text(),file=sys.stderr)
PYPROOF
  rm -rf "$work"
  if [[ "$status" == 0 && "$retained" != 0 ]]; then status="$retained"; fi
  exit "$status"
}
trap retain_model_proof EXIT

quint="${QUINT:-$(command -v quint || true)}"
if [[ -z "$quint" || "$($quint --version)" != "0.32.0" ]]; then
  echo "quint 0.32.0 is required" >&2
  exit 2
fi

model="$repo/eng/wasm-shared/lifecycle.qnt"
traces="$work/traces"
mkdir -p "$traces" "$work/fable" "$work/package"
export NUGET_PACKAGES="$work/nuget-packages"

"$quint" typecheck "$model"
"$quint" test --backend=typescript --main=lifecycleTest --seed=20261002 --out="$work/quint-tests.json" "$model"
python3 - "$work/quint-tests.json" <<'PY'
import json
import sys

result = json.load(open(sys.argv[1], encoding="utf-8"))
expected = {
    "barRefusesBusyTest",
    "sc2OrdinaryQueueIsFourTest",
    "latestSnapshotCoalescesTest",
    "candidateFailurePreservesActiveTest",
    "commitMakesOldCompletionHistoricalTest",
    "freezeBlocksAndSuppressesTest",
    "cleanupFaultIsNotSuccessTest",
    "disposalSettlesEverythingOnceTest",
}
passed = set(result["passed"])
if passed != expected or result["failed"] or result["ignored"]:
    raise SystemExit(
        f"quint lifecycle tests mismatch: passed={sorted(passed)} "
        f"failed={result['failed']} ignored={result['ignored']}"
    )
print(f"quint-lifecycle-tests: passed={len(passed)} names={','.join(sorted(passed))}")
PY
"$quint" test --backend=typescript --main=callbackClosureTest --seed=20261002 "$repo/eng/wasm-shared/event-boundary-qualification.qnt"
# The same 2,000 sample budget covers cold start and reachable initialized work.
for entry in init initReady; do
  "$quint" run --backend=typescript --main=lifecycle --init="$entry" --step=step --invariant=lifecycleSafe \
    --witnesses=sawBusyRefusal sawSnapshotCoalesced sawHistoricalResult sawQueuedTimeout \
    --max-steps=40 --max-samples=1000 --seed=20261002 --out-itf="$proof/quint-sampled-$entry.itf.json" "$model" > "$proof/quint-sampled-$entry.txt" 2>&1
done
for witness in sawBusyRefusal sawSnapshotCoalesced sawHistoricalResult sawQueuedTimeout; do
  grep -Eq "^${witness} was witnessed in [1-9][0-9]* trace" "$proof/quint-sampled-init.txt" "$proof/quint-sampled-initReady.txt"
done

# Reproduce every retained fixture through the canonical owner generator.
QUINT="$quint" python3 "$repo/tests/Wasm.Lifecycle.Correspondence/regenerate-event-traces.py" --check

dotnet run --project "$repo/tests/Wasm.Lifecycle.Tests/FS.GG.Wasm.Lifecycle.Tests.fsproj" -c Release -- --summary
dotnet run --project "$repo/tests/Wasm.Invocation.Tests/FS.GG.Wasm.Invocation.Tests.fsproj" -c Release
dotnet run --project "$repo/tests/Wasm.Lifecycle.Correspondence/FS.GG.Wasm.Lifecycle.Correspondence.fsproj" -c Release
dotnet tool run fable "$repo/tests/Wasm.Lifecycle.Correspondence/FS.GG.Wasm.Lifecycle.Correspondence.fsproj" \
  --outDir "$work/fable" --noCache
node "$work/fable/Program.js"

dotnet pack "$repo/src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj" -c Release -o "$work/package"
"$repo/scripts/wasm-release/build-worker-policy.sh" "$work/policy"
dotnet pack "$repo/src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj" -c Release -o "$work/package" -p:WasmWorkerPolicyRoot="$work/policy"
package="$work/package/FS.GG.Wasm.Browser.0.2.0.nupkg"
unzip -Z1 "$package" > "$work/package-files.txt"
for path in \
  fable/RuntimeProtocol.fsi fable/Admission.fsi fable/Invocation.fsi fable/WorkerEntry.fsi \
  fable/Lifecycle.fsi fable/Host.fsi fable/js/wasm-primitives.js; do
  grep -Fxq "$path" "$work/package-files.txt"
done

mkdir -p "$work/consumer"
cat > "$work/consumer/Consumer.fsproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.fs" />
    <PackageReference Include="FS.GG.Wasm.Browser" Version="[0.2.0]" />
  </ItemGroup>
</Project>
EOF
cat > "$work/consumer/Program.fs" <<'EOF'
open FS.GG.Wasm.Browser

[<EntryPoint>]
let main _ =
    match OperationToken.create "fresh-package-consumer" with
    | Ok token ->
        printfn "%s" (OperationToken.value token)
        0
    | Error issue -> failwithf "%A" issue
EOF
cat > "$work/NuGet.Config" <<EOF
<configuration>
  <packageSources><clear/><add key="candidate" value="$work/package"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><packageSource key="candidate"><package pattern="FS.GG.Wasm.*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping>
</configuration>
EOF
dotnet restore "$work/consumer/Consumer.fsproj" --configfile "$work/NuGet.Config"
dotnet tool run fable "$work/consumer/Consumer.fsproj" --outDir "$work/consumer-js" --noCache
node "$work/consumer-js/Program.js" | grep -Fxq 'fresh-package-consumer'

printf 'wasm-lifecycle: model=sampled seed=20261002 samples=cold1000+initialized1000 steps=40 initialized-prefix=4 correspondence=dotnet,fable-node consumer=fresh-fable-package package-sha256=%s publication=none\n' \
  "$(sha256sum "$package" | cut -d' ' -f1)"
