#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
packages=""

while (($#)); do
  case "$1" in
    --root) root="$(cd "$2" && pwd)"; shift 2 ;;
    --packages) packages="$(cd "$2" && pwd)"; shift 2 ;;
    --source-only) shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

props="$root/Directory.Build.local.props"
workflow="$root/.github/workflows/release.yml"

version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$props")"
[[ "$version" == "0.19.0" ]] || {
  echo "release scalar must be 0.19.0, observed '$version'" >&2
  exit 1
}

python3 - "$workflow" "$props" <<'PY'
import pathlib, sys, xml.etree.ElementTree as ET

text = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
props = ET.parse(sys.argv[2]).getroot()
baselines = props.findall('.//PackageValidationBaselineVersion')
if len(baselines) != 1 or baselines[0].text != '0.18.0':
    raise SystemExit('every package must use the published 0.18.0 API baseline')
condition = baselines[0].attrib.get('Condition', '')
expected = "'$(MSBuildProjectName)' == 'FS.GG.Game.Core' Or '$(MSBuildProjectName)' == 'FS.GG.Game.Render' Or '$(MSBuildProjectName)' == 'FS.GG.Game.Harness' Or '$(MSBuildProjectName)' == 'FS.GG.Game.Physics.Box2D' Or '$(MSBuildProjectName)' == 'FS.GG.Game.Render.Browser'"
if condition != expected or '-p:PackageValidationBaselineVersion=' in text:
    raise SystemExit('API baseline must apply to all five published packages')
if '[ "$count" = 5 ]' not in text or 'len(rows) != 5' not in text:
    raise SystemExit('release custody and dual-feed readback must each require five packages')
required = {
    "verification dependency": "needs: [verify]",
    "repository commit binding": '-p:RepositoryCommit="$GITHUB_SHA"',
    "API compatibility gate": "-p:EnablePackageValidation=true",
    "first release candidate consumer": "tests/release/test-box2d-package-consumer.sh --packages artifacts/packages",
    "public installed consumer": "tests/release/test-box2d-package-consumer.sh --public",
    "pinned bindings materialization": "scripts/materialize-js-bindings.sh all",
    "browser host candidate consumer": "tests/release/test-render-browser-package-consumer.sh --packages artifacts/packages",
    "browser host public consumer": "tests/release/test-render-browser-package-consumer.sh --public",
    "custody checksums": "sha256sum -- *.nupkg",
    "retained custody artifact": "game-release-custody-",
    "org feed": "https://nuget.pkg.github.com/FS-GG/index.json",
    "public feed": "https://api.nuget.org/v3/index.json",
}
for label, token in required.items():
    if token not in text:
        raise SystemExit(f"release workflow is missing {label}: {token}")
if text.count("dotnet pack FS.GG.Game.slnx") != 1:
    raise SystemExit("release workflow must prepare the coherent set exactly once")
browser_pack = "dotnet pack src/Render.Browser/FS.GG.Game.Render.Browser.fsproj"
if text.count(browser_pack) != 1:
    raise SystemExit("release workflow must prepare the browser host exactly once")
if text.index("scripts/materialize-js-bindings.sh all") >= text.index(browser_pack):
    raise SystemExit("the pinned bindings must be materialized before the browser host is packed")
# Push order (FS.GG.Game#745): on each feed the NEW identity, FS.GG.Game.Render.Browser, is pushed first and alone,
# then each existing id. The new id is the one push package-creation authority can refuse, so a refusal publishes
# nothing more to that feed. A glob push has no pinned order and is refused. The org feed goes first in full.
if 'dotnet nuget push "artifacts/packages/*.nupkg"' in text:
    raise SystemExit("release workflow must not push a glob: the new identity must be pushed first and alone")
push = "dotnet nuget push "
pushes = [index for index in range(len(text)) if text.startswith(push, index)]
if len(pushes) != 4:
    raise SystemExit("release workflow must contain exactly four actual dotnet nuget push operations (new identity, then existing ids, per feed)")
push_kinds = ('dotnet nuget push "$new_identity"', 'dotnet nuget push "$existing"')
observed = [next((kind for kind in push_kinds if text.startswith(kind, index)), "other") for index in pushes]
if observed != list(push_kinds) * 2:
    raise SystemExit("on each feed the new identity must be pushed first and alone, then the existing ids")
if text.count('new_identity="artifacts/packages/FS.GG.Game.Render.Browser.$VER.nupkg"') != 2:
    raise SystemExit("each feed must name FS.GG.Game.Render.Browser as the new identity it pushes first")
if text.count('[ "$existing" != "$new_identity" ] || continue') != 2:
    raise SystemExit("each feed's existing-id loop must skip the new identity")
org_url, public_url = "https://nuget.pkg.github.com/FS-GG/index.json", "https://api.nuget.org/v3/index.json"
for position, url in zip(pushes, (org_url, org_url, public_url, public_url)):
    owned = text[position:text.index("\n          ", text.index("--skip-duplicate", position))]
    if url not in owned:
        raise SystemExit("each dotnet nuget push must own its expected feed source")
retained = text.index("uses: actions/upload-artifact@v7")
if retained >= pushes[0]:
    raise SystemExit("original custody bytes must be retained before the first feed push")
if text.index("NuGet login", pushes[1]) > pushes[2]:
    raise SystemExit("release workflow must push the org feed before nuget.org")
if "dotnet pack" in text[pushes[0]:]:
    raise SystemExit("release workflow must not re-pack after first publication")
preflight = text.index('tests/release/test-game-0-19-release.sh --source-only')
if preflight >= text.index('target: FS.GG.Game.slnx'):
    raise SystemExit('release source preflight must precede restore')
for candidate_check in ('tests/release/test-box2d-package-consumer.sh --packages artifacts/packages',
                        'tests/release/test-render-browser-package-consumer.sh --packages artifacts/packages'):
    if text.index(candidate_check) >= retained:
        raise SystemExit('candidate package-only qualification must precede custody retention/publication')
for public_check in ('tests/release/test-box2d-package-consumer.sh --public',
                     'tests/release/test-render-browser-package-consumer.sh --public'):
    installed = text.index(public_check)
    if installed <= text.index('Read back and compare both feed payloads') or installed <= pushes[3]:
        raise SystemExit('public installed qualification must follow both feed pushes and readback')
# FS.GG.Game#764: the dual-feed readback evidence is retained as soon as readback passes, before any public consumer
# can fail; and the public consumers restore only after nuget.org's flat-container list names the version for every
# released id (a served .nupkg precedes its listing; restore reads the listing).
readback = text.index('Read back and compare both feed payloads')
evidence = text.index('name: Retain dual-feed readback evidence')
listing_wait = 'python3 tests/release/wait-nuget-listing.py --version "$VER" --packages artifacts/packages'
if text.count(listing_wait) != 1:
    raise SystemExit('release workflow must wait for nuget.org to list every released id exactly once, from the release packages')
waited = text.index(listing_wait)
consumers = [text.index(check) for check in ('tests/release/test-box2d-package-consumer.sh --public',
                                             'tests/release/test-render-browser-package-consumer.sh --public')]
if not readback < evidence < min(consumers):
    raise SystemExit('dual-feed readback evidence must be retained after readback and before any public consumer')
if not readback < waited < min(consumers):
    raise SystemExit('public consumers must wait for the nuget.org listing after readback')
if 'python3 tests/release/test-wait-nuget-listing.py' not in text[:text.index('target: FS.GG.Game.slnx')]:
    raise SystemExit('the release preflight must run the listing-wait offline tests')
recovery = text.index('Download retained release bytes')
if 'dotnet pack' in text[recovery:pushes[0]]:
    raise SystemExit('custody recovery must not re-pack')
PY

# The packed Fable views are derived from the source tree (src/Game.Render/Fable and src/Render.Browser), never
# restated here: an unpacked host module or any MSBuild condition in a view refuses before restore (Game#745).
python3 -I "$here/check-fable-views.py" --root "$root"

[[ -z "$packages" ]] && exit 0

mapfile -t nupkgs < <(find "$packages" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg' -printf '%f\n' | sort)
expected=(
  FS.GG.Game.Core.0.19.0.nupkg
  FS.GG.Game.Harness.0.19.0.nupkg
  FS.GG.Game.Physics.Box2D.0.19.0.nupkg
  FS.GG.Game.Render.0.19.0.nupkg
  FS.GG.Game.Render.Browser.0.19.0.nupkg
)
[[ "${nupkgs[*]}" == "${expected[*]}" ]] || {
  echo "expected exactly the coherent 0.19.0 package set" >&2
  printf 'observed: %s\n' "${nupkgs[*]:-<none>}" >&2
  exit 1
}

expected_commit="$(git -C "$root" rev-parse HEAD)"
for package in "${nupkgs[@]}"; do
  nuspec="$(unzip -Z1 "$packages/$package" | sed -n '/\.nuspec$/p')"
  [[ -n "$nuspec" ]] || { echo "$package has no nuspec" >&2; exit 1; }
  metadata="$(unzip -p "$packages/$package" "$nuspec")"
  grep -q '<version>0.19.0</version>' <<<"$metadata" || {
    echo "$package does not declare version 0.19.0" >&2; exit 1;
  }
  grep -q "commit=\"$expected_commit\"" <<<"$metadata" || {
    echo "$package does not bind repository commit $expected_commit" >&2; exit 1;
  }
done

core="$packages/FS.GG.Game.Core.0.19.0.nupkg"
# List once: under pipefail, `unzip -Z1 | grep -q` fails whenever grep exits before unzip finishes writing.
core_entries="$(unzip -Z1 "$core")"
for entry in \
  fable/Replay.fsi fable/Replay.fs \
  fable/Planning.fsi fable/Planning.fs \
  fable/Rules.fsi fable/Rules.fs \
  fable/NetworkSession.fsi fable/NetworkSession.fs \
  fable/FS.GG.Game.Core.fsproj \
  fable-compatibility/compatibility-profile.v1.json \
  fable-compatibility/fixture-schema.v1.json \
  fable-compatibility/fixtures/v1/cases.json \
  fable-compatibility/fixtures/v1/expected.bin; do
  grep -Fxq "$entry" <<<"$core_entries" || {
    echo "FS.GG.Game.Core 0.19.0 is missing required Fable entry $entry" >&2
    exit 1
  }
done

# Validate package APIs, Fable views and runtime dependency boundaries from actual candidate bytes.
python3 - "$packages" <<'PY_PACKAGE'
from hashlib import sha256
from pathlib import Path
from zipfile import ZipFile
import re, sys, xml.etree.ElementTree as ET
packages = Path(sys.argv[1])
def metadata(package):
    with ZipFile(packages / (package + '.0.19.0.nupkg')) as archive:
        names = archive.namelist()
        nuspec = [name for name in names if name.endswith('.nuspec')]
        assert len(nuspec) == 1, 'one genuine nuspec required'
        document = ET.fromstring(archive.read(nuspec[0]))
        dependencies = {row.attrib['id']: row.attrib['version'] for row in document.iter() if row.tag.rsplit('}', 1)[-1] == 'dependency'}
        return dependencies, names
core, _ = metadata('FS.GG.Game.Core')
assert set(core) == {'FSharp.Core'}, 'Core must keep its FSharp.Core/BCL runtime boundary'
adapter, names = metadata('FS.GG.Game.Physics.Box2D')
assert set(adapter) == {'FS.GG.Game.Core', 'FSharp.Core', 'Box2D.NET'}, 'adapter must have no rendering dependency'
assert adapter['FS.GG.Game.Core'].strip('[]()').split(',')[0].strip() == '0.19.0', 'adapter/Core coherent dependency mismatch'
assert adapter['Box2D.NET'].strip('[]()').split(',')[0].strip() == '3.1.654', 'adapter engine dependency differs from qualified pin'
for entry in ('lib/net10.0/FS.GG.Game.Physics.Box2D.dll', 'api-surface/Types.fsi', 'api-surface/Runtime.fsi', 'api-surface/AreaRuntime.fsi'):
    assert entry in names, 'adapter missing package API entry: ' + entry
def entries(package):
    with ZipFile(packages / (package + '.0.19.0.nupkg')) as archive:
        return {name: archive.read(name) for name in archive.namelist()}
browser_deps, _ = metadata('FS.GG.Game.Render.Browser')
assert set(browser_deps) == {'FS.GG.Game.Render', 'FS.GG.UI.Scene', 'FS.GG.UI.Scene.SvgBrowser', 'FS.GG.UI.KeyboardInput', 'Fable.Browser.Dom', 'Fable.Core', 'FSharp.Core'}, 'browser host dependency boundary changed: ' + repr(sorted(browser_deps))
assert browser_deps['FS.GG.Game.Render'].strip('[]()').split(',')[0].strip() == '0.19.0', 'browser host/Render coherent dependency mismatch'
browser = entries('FS.GG.Game.Render.Browser')
for entry in ('lib/net10.0/FS.GG.Game.Render.Browser.dll', 'api-surface/BrowserRenderers.fsi', 'api-surface/RendererDriver.fsi',
              'fable-compatibility/compatibility-profile.v1.json', 'js-bindings/babylon/package-lock.json', 'js-bindings/pixi/package-lock.json'):
    assert entry in browser, 'browser host missing package entry: ' + entry
print('coherent package metadata passed: adapter API, exact engine pin, Core boundary, no rendering dependency and browser host boundary')
PY_PACKAGE
python3 -I "$here/check-fable-views.py" --root "$root" \
  --render "$packages/FS.GG.Game.Render.0.19.0.nupkg" --browser "$packages/FS.GG.Game.Render.Browser.0.19.0.nupkg"

consumer="$(mktemp -d)"
trap 'rm -rf "$consumer"' EXIT
mkdir -p "$consumer/src"
cat >"$consumer/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="candidate" value="$packages"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
EOF
cat >"$consumer/src/Consumer.fsproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
  <ItemGroup><PackageReference Include="FS.GG.Game.Harness" Version="0.19.0" /></ItemGroup>
</Project>
EOF
cat >"$consumer/src/Program.fs" <<'EOF'
open FS.GG.Game.Harness

let receiptDigest : JourneyReceipt -> string = JourneyReceipt.definitionDigest
let coverageVerdict : ActionCoverageReport -> bool = ActionCoverageReport.isClean
let coverageCheck = Journey.checkActionCoverage

printfn "%A %A %A" receiptDigest coverageVerdict coverageCheck
EOF
dotnet restore "$consumer/src/Consumer.fsproj" --configfile "$consumer/NuGet.Config" --packages "$consumer/packages" --disable-parallel
dotnet build "$consumer/src/Consumer.fsproj" --no-restore -m:1 --disable-build-servers
