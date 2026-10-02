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

"$quint" typecheck "$model"
"$quint" test --main=lifecycleTest --seed=20261002 "$model"
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
dotnet run --project "$repo/tests/Wasm.Lifecycle.Correspondence/FS.GG.Wasm.Lifecycle.Correspondence.fsproj" -c Release
dotnet tool run fable "$repo/tests/Wasm.Lifecycle.Correspondence/FS.GG.Wasm.Lifecycle.Correspondence.fsproj" \
  --outDir "$work/fable" --noCache
node "$work/fable/Program.js"

dotnet pack "$repo/src/Wasm.Browser/FS.GG.Wasm.Browser.fsproj" -c Release -o "$work/package"
package="$work/package/FS.GG.Wasm.Browser.0.1.0-source.2.nupkg"
unzip -Z1 "$package" > "$work/package-files.txt"
for path in \
  fable/RuntimeProtocol.fsi fable/Admission.fsi fable/Invocation.fsi fable/WorkerEntry.fsi \
  fable/Lifecycle.fsi fable/Host.fsi fable/js/wasm-primitives.js; do
  grep -Fxq "$path" "$work/package-files.txt"
done

printf 'wasm-lifecycle: model=sampled seed=20261002 samples=2000 steps=40 correspondence=dotnet,fable-node package-sha256=%s publication=none\n' \
  "$(sha256sum "$package" | cut -d' ' -f1)"
