#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/wasm-contracts.XXXXXX")"
trap 'rm -rf "$work"' EXIT
candidate_feed="${WASM_CONTRACTS_FEED:-$work/feed}"
mkdir -p "$candidate_feed"

export NUGET_PACKAGES="$work/packages"

dotnet build "$repo/src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj" -c Release
dotnet restore "$repo/tests/Wasm.Contracts.Tests/FS.GG.Wasm.Contracts.Tests.fsproj" --locked-mode
dotnet run --project "$repo/tests/Wasm.Contracts.Tests/FS.GG.Wasm.Contracts.Tests.fsproj" -c Release --no-restore
dotnet pack "$repo/src/Wasm.Contracts/FS.GG.Wasm.Contracts.fsproj" -c Release -o "$candidate_feed"

package="$candidate_feed/FS.GG.Wasm.Contracts.0.1.0-source.2.nupkg"
test -f "$package"
unzip -Z1 "$package" > "$work/package-files.txt"
grep -Fxq 'api-surface/Contracts.fsi' "$work/package-files.txt"
grep -Fxq 'fable/Contracts.fsi' "$work/package-files.txt"
grep -Fxq 'fable/Contracts.fs' "$work/package-files.txt"
grep -Fxq 'fable/FS.GG.Wasm.Contracts.fsproj' "$work/package-files.txt"
grep -Fxq 'wasm-compatibility/compatibility-profile.v1.json' "$work/package-files.txt"
grep -Fxq 'wasm-compatibility/baselines.v1.json' "$work/package-files.txt"
grep -Fxq 'wasm-compatibility/expected-decisions.v1.json' "$work/package-files.txt"

cp -R "$repo/tests/Wasm.Contracts.PortableConsumers" "$work/consumer"

config="$work/NuGet.Config"
printf '%s\n' \
  '<configuration>' \
  '  <packageSources><clear/><add key="candidate" value="'"$candidate_feed"'"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>' \
  '  <packageSourceMapping><packageSource key="candidate"><package pattern="FS.GG.Wasm.Contracts"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping>' \
  '</configuration>' > "$config"

dotnet restore "$work/consumer/DotNet/DotNet.fsproj" --configfile "$config"
dotnet run --project "$work/consumer/DotNet/DotNet.fsproj" --no-restore > "$work/dotnet.txt"
dotnet restore "$work/consumer/Fable/Fable.fsproj" --configfile "$config"
dotnet tool run fable -- "$work/consumer/Fable/Fable.fsproj" --outDir "$work/javascript" --noCache
node "$work/javascript/Program.js" > "$work/fable.txt"
cmp "$work/dotnet.txt" "$work/fable.txt"

package_sha256="$(sha256sum "$package" | cut -d' ' -f1)"
output_sha256="$(sha256sum "$work/dotnet.txt" | cut -d' ' -f1)"
printf 'wasm-contracts: package=%s package-sha256=%s consumers=dotnet,fable-node output-sha256=%s publication=none\n' \
  "$(basename "$package")" "$package_sha256" "$output_sha256"
