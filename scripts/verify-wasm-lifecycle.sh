#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/wasm-lifecycle.XXXXXX")"
trap 'rm -rf "$work"' EXIT

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
"$quint" test --main=lifecycleTest --seed=20261002 --out="$work/quint-tests.json" "$model"
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
"$quint" run --main=lifecycle --init=init --step=step --invariant=lifecycleSafe \
  --witnesses=sawBusyRefusal sawSnapshotCoalesced sawHistoricalResult sawQueuedTimeout \
  --max-steps=40 --max-samples=2000 --seed=20261002 "$model" > "$work/quint-sampled.txt"
for witness in sawBusyRefusal sawSnapshotCoalesced sawHistoricalResult sawQueuedTimeout; do
  grep -Eq "^${witness} was witnessed in [1-9][0-9]* trace" "$work/quint-sampled.txt"
done

"$quint" run --main=lifecycle --init=initBar --step=barCorrespondenceStep --invariant=lifecycleSafe \
  --max-steps=5 --max-samples=1 --seed=20261002 --out-itf="$traces/bar_{seq}.itf.json" "$model" >/dev/null
"$quint" run --main=lifecycle --init=initSc2 --step=sc2CorrespondenceStep --invariant=lifecycleSafe \
  --max-steps=14 --max-samples=1 --seed=20261002 --out-itf="$traces/sc2_{seq}.itf.json" "$model" >/dev/null
"$quint" run --main=lifecycle --init=initSc2 --step=timeoutCorrespondenceStep --invariant=lifecycleSafe \
  --max-steps=5 --max-samples=1 --seed=20261002 --out-itf="$traces/timeout_{seq}.itf.json" "$model" >/dev/null

python3 "$repo/tests/Wasm.Lifecycle.Correspondence/generate-traces.py" \
  --traces "$traces" --output "$work/GeneratedTraces.fs"
for name in bar sc2 timeout; do
  cmp "$traces/${name}_0.itf.json" "$repo/tests/Wasm.Lifecycle.Correspondence/Traces/${name}_0.itf.json"
done
cmp "$work/GeneratedTraces.fs" "$repo/tests/Wasm.Lifecycle.Correspondence/GeneratedTraces.fs"

dotnet run --project "$repo/tests/Wasm.Lifecycle.Tests/FS.GG.Wasm.Lifecycle.Tests.fsproj" -c Release -- --summary
dotnet run --project "$repo/tests/Wasm.Invocation.Tests/FS.GG.Wasm.Invocation.Tests.fsproj" -c Release
dotnet run --project "$repo/tests/Wasm.Lifecycle.Correspondence/FS.GG.Wasm.Lifecycle.Correspondence.fsproj" -c Release
dotnet tool run fable "$repo/tests/Wasm.Lifecycle.Correspondence/FS.GG.Wasm.Lifecycle.Correspondence.fsproj" \
  --outDir "$work/fable" --noCache
node "$work/fable/Program.js"

dotnet pack "$repo/src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj" -c Release -o "$work/package"
dotnet pack "$repo/src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj" -c Release -o "$work/package"
package="$work/package/FS.GG.Wasm.Browser.0.1.0-source.2.nupkg"
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
    <PackageReference Include="FS.GG.Wasm.Browser" Version="[0.1.0-source.2]" />
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

printf 'wasm-lifecycle: model=sampled seed=20261002 samples=2000 steps=40 correspondence=dotnet,fable-node consumer=fresh-fable-package package-sha256=%s publication=none\n' \
  "$(sha256sum "$package" | cut -d' ' -f1)"
