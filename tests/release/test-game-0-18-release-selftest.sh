#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

make_fixture() {
  local fixture="$1"
  mkdir -p "$fixture/.github/workflows"
  cp "$root/Directory.Build.local.props" "$fixture/Directory.Build.local.props"
  cp "$root/.github/workflows/release.yml" "$fixture/.github/workflows/release.yml"
  # The packed Fable views are derived from these project files (tests/release/check-fable-views.py).
  for file in src/Game.Render/Fable/FS.GG.Game.Render.fsproj src/Render.Browser/FS.GG.Game.Render.Browser.fsproj \
    src/Render.Browser/Fable/FS.GG.Game.Render.Browser.fsproj eng/js-bindings/pin.env; do
    mkdir -p "$(dirname "$fixture/$file")"
    cp "$root/$file" "$fixture/$file"
  done
}

positive="$scratch/positive"
make_fixture "$positive"
"$root/tests/release/test-game-0-18-release.sh" --root "$positive" --source-only
echo "positive control accepted both actual dotnet nuget push operations"

expect_push_mutation_red() {
  local label="$1" indexes="$2" output
  local fixture="$scratch/$label"
  make_fixture "$fixture"
  python3 - "$fixture/.github/workflows/release.yml" "$indexes" <<'PY'
import pathlib, sys

path = pathlib.Path(sys.argv[1])
selected = {int(value) for value in sys.argv[2].split(",")}
needle = 'dotnet nuget push "artifacts/packages/*.nupkg"'
text = path.read_text(encoding="utf-8")
parts = text.split(needle)
if len(parts) != 3:
    raise SystemExit(f"fixture expected two push operations, observed {len(parts) - 1}")
rebuilt = parts[0]
for index, tail in enumerate(parts[1:]):
    rebuilt += ("echo package-push-disabled" if index in selected else needle) + tail
path.write_text(rebuilt, encoding="utf-8")
PY
  if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$fixture" --source-only 2>&1); then
    echo "$label mutation unexpectedly passed" >&2
    exit 1
  fi
  grep -q "exactly two actual dotnet nuget push operations" <<<"$output" || {
    echo "$label mutation failed for the wrong reason: $output" >&2
    exit 1
  }
  echo "$label witness: substituted push operation rejected"
}

expect_push_mutation_red first-push 0
expect_push_mutation_red second-push 1
expect_push_mutation_red both-pushes 0,1

version_fixture="$scratch/version"
make_fixture "$version_fixture"
sed -i 's:<Version>0.18.0</Version>:<Version>0.18.1</Version>:' "$version_fixture/Directory.Build.local.props"
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$version_fixture" --source-only 2>&1); then
  echo "release gate inversion unexpectedly passed" >&2
  exit 1
fi
grep -q "release scalar must be 0.18.0" <<<"$output" || {
  echo "release gate inversion failed for the wrong reason: $output" >&2
  exit 1
}
echo "version witness: 0.18.1 scalar rejected"

baseline_fixture="$scratch/baseline"
make_fixture "$baseline_fixture"
sed -i 's:>0.17.0</PackageValidationBaselineVersion>:>0.16.0</PackageValidationBaselineVersion>:' "$baseline_fixture/Directory.Build.local.props"
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$baseline_fixture" --source-only 2>&1); then
  echo "foreign API baseline mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'existing packages must use the published 0.17.0 API baseline' <<<"$output"
echo "baseline witness: foreign predecessor rejected"

roster_fixture="$scratch/roster"
make_fixture "$roster_fixture"
sed -i 's:\[ "$count" = 5 \]:[ "$count" = 4 ]:' "$roster_fixture/.github/workflows/release.yml"
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$roster_fixture" --source-only 2>&1); then
  echo "missing adapter custody mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'release custody and dual-feed readback must each require five packages' <<<"$output"
echo "roster witness: missing browser host custody rejected"

# These controls exercise exact artifact admission before any metadata/CLR consumer work can launch.
missing="$scratch/missing-adapter"
mkdir "$missing"
for id in Core Harness Render Physics.Box2D; do : > "$missing/FS.GG.Game.$id.0.18.0.nupkg"; done
if output=$("$root/tests/release/test-game-0-18-release.sh" --packages "$missing" 2>&1); then
  echo "missing browser host package mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'expected exactly the coherent 0.18.0 package set' <<<"$output"
echo "artifact witness: missing browser host refused before consumer launch"

extra="$scratch/extra-package"
mkdir "$extra"
for id in Core Harness Render Physics.Box2D Render.Browser Foreign; do : > "$extra/FS.GG.Game.$id.0.18.0.nupkg"; done
if output=$("$root/tests/release/test-game-0-18-release.sh" --packages "$extra" 2>&1); then
  echo "foreign package mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'expected exactly the coherent 0.18.0 package set' <<<"$output"
echo "artifact witness: foreign package refused before consumer launch"

wrong="$scratch/wrong-package-version"
mkdir "$wrong"
for id in Core Harness Render Physics.Box2D; do : > "$wrong/FS.GG.Game.$id.0.18.0.nupkg"; done
: > "$wrong/FS.GG.Game.Render.Browser.0.18.1.nupkg"
if output=$("$root/tests/release/test-game-0-18-release.sh" --packages "$wrong" 2>&1); then
  echo "mismatched browser host version mutation unexpectedly passed" >&2; exit 1
fi
grep -q 'expected exactly the coherent 0.18.0 package set' <<<"$output"
echo "artifact witness: mismatched browser host version refused before consumer launch"

browser_order="$scratch/browser-order"
make_fixture "$browser_order"
python3 - "$browser_order/.github/workflows/release.yml" <<'PY'
import pathlib, sys
path = pathlib.Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
assert text.count("scripts/materialize-js-bindings.sh all") == 1
path.write_text(text.replace("scripts/materialize-js-bindings.sh all", "echo bindings-not-materialized"), encoding="utf-8")
PY
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$browser_order" --source-only 2>&1); then
  echo "unmaterialized browser host pack unexpectedly passed" >&2; exit 1
fi
grep -q 'pinned bindings materialization' <<<"$output"
echo "browser witness: packing the browser host without the pinned bindings rejected"

browser_public="$scratch/browser-public"
make_fixture "$browser_public"
sed -i 's:tests/release/test-render-browser-package-consumer.sh --public:echo public-browser-consumer-skipped:' "$browser_public/.github/workflows/release.yml"
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$browser_public" --source-only 2>&1); then
  echo "missing public browser consumer unexpectedly passed" >&2; exit 1
fi
grep -q 'browser host public consumer' <<<"$output"
echo "browser witness: release without the public browser host consumer rejected"

# A module added to the host and not to its packed Fable project refuses, whatever the module count (the shape of a
# rebase that compiles RetainedSvgSurface but forgets to pack it).
unpacked="$scratch/unpacked-host-source"
make_fixture "$unpacked"
sed -i 's:    <Compile Include="BrowserRenderers.fs" />:    <Compile Include="BrowserRenderers.fs" />\n    <Compile Include="RetainedSvgSurface.fsi" />\n    <Compile Include="RetainedSvgSurface.fs" />:' \
  "$unpacked/src/Render.Browser/FS.GG.Game.Render.Browser.fsproj"
grep -q 'RetainedSvgSurface.fsi' "$unpacked/src/Render.Browser/FS.GG.Game.Render.Browser.fsproj"
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$unpacked" --source-only 2>&1); then
  echo "host source added but not packed unexpectedly passed" >&2; exit 1
fi
grep -q "does not list the browser host's sources" <<<"$output" && grep -q "RetainedSvgSurface.fsi" <<<"$output"
echo "view witness: a host source added but not packed rejected"

# Any Condition attribute in a Fable view refuses, however it is spaced.
spaced="$scratch/spaced-condition"
make_fixture "$spaced"
sed -i 's:  <ItemGroup>\(\s*\)$:  <ItemGroup Condition = "true">:' "$spaced/src/Game.Render/Fable/FS.GG.Game.Render.fsproj"
grep -q 'Condition = "true"' "$spaced/src/Game.Render/Fable/FS.GG.Game.Render.fsproj"
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$spaced" --source-only 2>&1); then
  echo "spaced Condition in the Render view unexpectedly passed" >&2; exit 1
fi
grep -q 'has a Condition attribute' <<<"$output"
echo "view witness: a spaced Condition in the Render view rejected"

conditioned="$scratch/conditioned-packed-browser"
make_fixture "$conditioned"
sed -i 's:<Compile Include="generated/PixiBindings.fs" />:<Compile Include="generated/PixiBindings.fs" Condition="Exists('"'"'x'"'"')" />:' \
  "$conditioned/src/Render.Browser/Fable/FS.GG.Game.Render.Browser.fsproj"
grep -q "Condition=\"Exists('x')\"" "$conditioned/src/Render.Browser/Fable/FS.GG.Game.Render.Browser.fsproj"
if output=$("$root/tests/release/test-game-0-18-release.sh" --root "$conditioned" --source-only 2>&1); then
  echo "Condition on a packed browser source unexpectedly passed" >&2; exit 1
fi
grep -q 'has a Condition attribute' <<<"$output"
echo "view witness: a conditional source in the packed browser project rejected"
