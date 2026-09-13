#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/save-migration-portable.XXXXXX")"
mkdir -p "$work/feed" "$work/tools" "$work/packages"
export NUGET_PACKAGES="$work/packages"
dotnet pack "$repo/src/Game.Core/FS.GG.Game.Core.fsproj" -c Release -o "$work/feed" -p:Version=0.16.0-svg-present.1
for runtime in DotNet Fable; do cp -R "$repo/tests/SaveMigration.PortableConsumers/$runtime" "$work/$runtime"; cp "$repo/tests/SaveMigration.PortableConsumers/Correspondence.fs" "$work/$runtime/Correspondence.fs"; done
cat > "$work/NuGet.Config" <<CONFIG
<configuration><packageSources><clear/><add key="candidate" value="$work/feed"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="candidate"><package pattern="FS.GG.Game.*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>
CONFIG
dotnet restore "$work/DotNet/DotNet.fsproj" --configfile "$work/NuGet.Config"
dotnet run --project "$work/DotNet/DotNet.fsproj" --no-restore -- "$work/dotnet.txt"
dotnet restore "$work/Fable/Fable.fsproj" --configfile "$work/NuGet.Config"
dotnet tool install fable --version 5.17.0 --tool-path "$work/tools" --configfile "$work/NuGet.Config"
"$work/tools/fable" "$work/Fable/Fable.fsproj" --outDir "$work/javascript" --noCache
node "$work/javascript/Program.js" > "$work/fable.txt"
python3 - "$work/fable.txt" <<'PY'
import pathlib,sys
p=pathlib.Path(sys.argv[1]);p.write_text(p.read_text().rstrip('\n')+'\n')
PY
cmp "$work/dotnet.txt" "$work/fable.txt"
unzip -l "$work/feed/FS.GG.Game.Core.0.16.0-svg-present.1.nupkg" | grep -q 'fable/SaveMigration.fs'
digest="$(sha256sum "$work/dotnet.txt"|cut -d' ' -f1)"
echo "save-migration-portable: runtimes=dotnet,fable-node sha256=$digest"
