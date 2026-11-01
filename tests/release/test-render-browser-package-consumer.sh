#!/usr/bin/env bash
#
# GAME-RENDER-ADOPT-01.1 (FS.GG.Game#745): a clean consumer of the PACKED browser host.
#
# Creates an isolated consumer that references only FS.GG.Game.Render.Browser (FS.GG.Game.Render arrives as its
# dependency), generates its renderer module with scripts/new-game-renderers.sh, and then:
#   1. restores into an empty package folder, from the candidate packages (--packages) or from public nuget.org only
#      (--public);
#   2. builds it with .NET, which type-checks the generated module against the packed assemblies (what an editor
#      sees);
#   3. compiles it with Fable from the packed fable/ sources, and requires the output to import the pinned npm
#      packages of both canvas backends.
# Nothing from this repository's source tree is compiled: only the packages and the generator's output.
#
#   usage: tests/release/test-render-browser-package-consumer.sh (--packages <dir> | --public) [--version <v>]
#   default version: the <Version> in Directory.Build.local.props
#
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
packages=""
public=0
version=""

while (($#)); do
  case "$1" in
    --packages) packages="$(cd "$2" && pwd)"; shift 2 ;;
    --public) public=1; shift ;;
    --version) version="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ -n $packages || $public == 1 ]] || { echo "give --packages <dir> or --public" >&2; exit 2; }
[[ -z $packages || $public == 0 ]] || { echo "--packages and --public are exclusive" >&2; exit 2; }
[[ -n $version ]] || version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$root/Directory.Build.local.props")"
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] || { echo "invalid version '$version'" >&2; exit 2; }

if [[ -n $packages ]]; then
  for id in FS.GG.Game.Render FS.GG.Game.Render.Browser; do
    [[ -f $packages/$id.$version.nupkg ]] || { echo "candidate $id.$version.nupkg is missing from $packages" >&2; exit 1; }
  done
fi

consumer="$(mktemp -d)"
if [[ ${RENDER_CONSUMER_KEEP:-0} == 1 ]]; then
  echo "render-browser consumer: keeping $consumer"
else
  trap 'rm -rf "$consumer"' EXIT
fi
mkdir -p "$consumer/src" "$consumer/js"

{
  echo '<?xml version="1.0" encoding="utf-8"?>'
  echo '<configuration><packageSources><clear/>'
  [[ -z $packages ]] || echo "<add key=\"candidate\" value=\"$packages\"/>"
  echo '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>'
  echo '</packageSources></configuration>'
} >"$consumer/NuGet.Config"
# An empty directory boundary: no repository Directory.Build.* or central package management reaches the consumer.
for file in Directory.Build.props Directory.Build.targets Directory.Packages.props; do
  echo '<Project />' >"$consumer/$file"
done

cat >"$consumer/src/Consumer.fsproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Library</OutputType><TargetFramework>net10.0</TargetFramework><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>
    <Compile Include="State.fs" />
    <Compile Include="Renderers.fs" />
    <Compile Include="Page.fs" />
  </ItemGroup>
  <ItemGroup><PackageReference Include="FS.GG.Game.Render.Browser" Version="[$version]" /></ItemGroup>
</Project>
EOF
cat >"$consumer/src/State.fs" <<'EOF'
namespace Consumer.Client

type Model = { Tick: int; Score: int }
EOF
"$root/scripts/new-game-renderers.sh" --namespace Consumer.Client --module Renderers --state Consumer.Client.Model \
  --label "Consumer" --output "$consumer/src/Renderers.fs"
cat >"$consumer/src/Page.fs" <<'EOF'
module Consumer.Client.Page

open Browser
open FS.GG.Game.Render

// Uses each generated entry point, so both compilers must resolve the packed surface they name.
let start () =
    let container = document.createElement "div"
    let driver = Renderers.start container { Tick = 0; Score = 0 } ignore
    driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Pixi)
    driver.Dispatch(RendererMsg.StateChanged { Tick = 1; Score = 3 })
    driver.Dispatch(RendererMsg.SelectBackend RendererBackend.Babylon)
    let available = RendererSelection.available driver.Model
    let subject = Renderers.conformance container [ { Tick = 0; Score = 0 }; { Tick = 1; Score = 3 } ] (fun _ _ _ -> async { return Ok() })
    available, subject
EOF

cd "$consumer"
dotnet restore src/Consumer.fsproj --configfile NuGet.Config --packages "$consumer/packages" --disable-parallel
for id in fs.gg.game.render fs.gg.game.render.browser; do
  restored="$consumer/packages/$id/$version/$id.$version.nupkg"
  [[ -f $restored ]] || { echo "the consumer did not restore $id $version" >&2; exit 1; }
  if [[ -n $packages ]]; then
    upper="$(find "$packages" -maxdepth 1 -iname "$id.$version.nupkg" -print -quit)"
    cmp "$restored" "$upper" || { echo "the consumer restored $id bytes that differ from the candidate" >&2; exit 1; }
  fi
  echo "render-browser consumer: restored $id $version sha256=$(sha256sum "$restored" | cut -d' ' -f1)"
done
dotnet build src/Consumer.fsproj --no-restore -m:1 --disable-build-servers

cd "$root"
dotnet tool restore >/dev/null
NUGET_PACKAGES="$consumer/packages" dotnet fable "$consumer/src" --outDir "$consumer/js" --noCache
# The canvas surfaces' compiled JavaScript must import the pinned npm packages (Pixi by name, Babylon by subpath).
for spec in 'from "pixi.js"' 'from "@babylonjs/core/'; do
  grep -rqF --include='*.js' "$spec" "$consumer/js" || {
    echo "the Fable output has no import $spec" >&2
    exit 1
  }
done
[[ -f $consumer/js/Renderers.js && -f $consumer/js/Page.js ]] || {
  echo "the Fable output lacks the generated module or the page" >&2
  exit 1
}
echo "render-browser consumer: OK — FS.GG.Game.Render.Browser $version restored, built with .NET and compiled with Fable from $([[ -n $packages ]] && echo "the candidate packages" || echo "public nuget.org")"
