#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
packages=""
mode=""
output=""
while (($#)); do
  case "$1" in
    --packages) packages="$(cd "$2" && pwd)"; mode=candidate; shift 2 ;;
    --public) mode=public; shift ;;
    --output) output="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ "$mode" == candidate || "$mode" == public ]] || { echo 'select --packages DIR or --public' >&2; exit 2; }
if [[ -n "$output" ]]; then
  [[ "$output" == /* && ! -e "$output" ]] || { echo '--output must be a new absolute directory' >&2; exit 2; }
  mkdir -m 700 "$output"
else
  output="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/box2d-package-consumer.XXXXXX")"
fi
cp "$root/tests/release/Box2D.PackageConsumer/"{Consumer.fsproj,Program.fs,Scene.fs,PortalScene.fs,global.json,verify-package-boundary.py} "$output/"
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
# Every attempt has a new isolated package cache and public HTTPS source mapping.
# Disable the HTTP cache instead of changing the user's global NuGet cache/configuration.
dotnet restore Consumer.fsproj --configfile NuGet.Config --packages "$output/packages" --no-cache --force-evaluate --disable-parallel
python3 verify-package-boundary.py
dotnet build Consumer.fsproj -c Release --no-restore -m:1 --disable-build-servers
# SDK pin and explicit runtime selection scope replay evidence to this profile.
dotnet exec --fx-version 10.0.12 bin/Release/net10.0/Consumer.dll
printf 'Box2D package-only qualification passed mode=%s; lock and restored inputs retained in %s\n' "$mode" "$output"
