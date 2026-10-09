#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
packages=""
mode=""
output=""
presentation=false
while (($#)); do
  case "$1" in
    --packages) packages="$(cd "$2" && pwd)"; mode=candidate; shift 2 ;;
    --public) mode=public; shift ;;
    --output) output="$2"; shift 2 ;;
    --presentation) presentation=true; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ "$mode" == candidate || "$mode" == public ]] || { echo 'select --packages DIR or --public' >&2; exit 2; }
if [[ "$presentation" == true ]]; then
  [[ "$mode" == public && -z "$packages" ]] || { echo '--presentation requires --public without candidate packages' >&2; exit 2; }
  for source in \
    "$root/tests/release/Box2D.PackageConsumer/PresentationConsumer.fsproj" \
    "$root/tests/release/Box2D.PackageConsumer/PortalScene.fs" \
    "$root/tests/release/Box2D.PackageConsumer/global.json" \
    "$root/tests/release/Box2D.PackageConsumer/verify-package-boundary.py" \
    "$root/examples/Box2D.Portals/Presentation.fs" \
    "$root/examples/Box2D.Portals/Program.fs"; do
    [[ -f "$source" && ! -L "$source" ]] || { echo "missing or linked presentation input: $source" >&2; exit 2; }
  done
fi
if [[ -n "$output" ]]; then
  [[ "$output" == /* && ! -e "$output" ]] || { echo '--output must be a new absolute directory' >&2; exit 2; }
  mkdir -m 700 "$output"
else
  output="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/box2d-package-consumer.XXXXXX")"
fi
boundary_args=()
if [[ "$presentation" == true ]]; then
  cp "$root/tests/release/Box2D.PackageConsumer/PresentationConsumer.fsproj" "$output/Consumer.fsproj"
  cp "$root/tests/release/Box2D.PackageConsumer/"{PortalScene.fs,global.json,verify-package-boundary.py} "$output/"
  cp "$root/examples/Box2D.Portals/"{Presentation.fs,Program.fs} "$output/"
  boundary_args=(--presentation)
  export NUGET_HTTP_CACHE_PATH="$output/http-cache"
else
  cp "$root/tests/release/Box2D.PackageConsumer/"{Consumer.fsproj,Program.fs,Scene.fs,PortalScene.fs,global.json,verify-package-boundary.py} "$output/"
fi
python3 - "$output/NuGet.Config" "$mode" "$packages" <<'PY'
import sys, xml.etree.ElementTree as ET
path, mode, packages = sys.argv[1:]
root = ET.Element('configuration'); sources = ET.SubElement(root, 'packageSources'); ET.SubElement(sources, 'clear')
mapping = ET.SubElement(root, 'packageSourceMapping')
if mode == 'candidate':
    ET.SubElement(sources, 'add', key='candidate', value=packages)
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='candidate'), 'package', pattern='FS.GG.Game.*')
ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
public = ET.SubElement(mapping, 'packageSource', key='nuget.org')
for pattern in (['Box2D.NET', 'FSharp.Core'] if mode == 'candidate' else ['*']):
    ET.SubElement(public, 'package', pattern=pattern)
ET.ElementTree(root).write(path, encoding='unicode', xml_declaration=True)
PY
printf 'Box2D package-only qualification mode=%s directory=%s\n' "$mode" "$output"
cd "$output"
python3 verify-package-boundary.py "${boundary_args[@]}" --preflight
# Every attempt has a new isolated package cache and public HTTPS source mapping.
# Disable the HTTP cache instead of changing the user's global NuGet cache/configuration.
dotnet restore Consumer.fsproj --configfile NuGet.Config --packages "$output/packages" --no-cache --force-evaluate --disable-parallel
python3 verify-package-boundary.py "${boundary_args[@]}"
if [[ "$presentation" == true ]]; then
  # Prove the genuinely generated lock survives a second restore unchanged.
  cp packages.lock.json packages.lock.generated.json
  dotnet restore Consumer.fsproj --configfile NuGet.Config --packages "$output/packages" --no-cache --locked-mode --force --disable-parallel
  cmp packages.lock.generated.json packages.lock.json
fi
dotnet build Consumer.fsproj -c Release --no-restore -m:1 --disable-build-servers
# SDK pin and explicit runtime selection scope replay evidence to this profile.
dotnet exec --fx-version 10.0.12 bin/Release/net10.0/Consumer.dll
if [[ "$presentation" == true ]]; then
  dotnet exec --fx-version 10.0.12 bin/Release/net10.0/Consumer.dll --presentation
  invalid_exit=0
  dotnet exec --fx-version 10.0.12 bin/Release/net10.0/Consumer.dll --invalid > invalid-argument.log 2>&1 || invalid_exit=$?
  [[ "$invalid_exit" == 2 ]] && grep -qF 'Usage: Box2D.Portals [--presentation]' invalid-argument.log || { echo 'invalid presentation argument did not refuse with usage/exit 2' >&2; exit 1; }
fi
printf 'Box2D package-only qualification passed mode=%s; lock and restored inputs retained in %s\n' "$mode" "$output"
