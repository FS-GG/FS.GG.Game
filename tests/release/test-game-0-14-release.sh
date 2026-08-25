#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
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
[[ "$version" == "0.14.0" ]] || {
  echo "release scalar must be 0.14.0, observed '$version'" >&2
  exit 1
}

python3 - "$workflow" <<'PY'
import pathlib, sys

text = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
required = {
    "verification dependency": "needs: [verify]",
    "repository commit binding": '-p:RepositoryCommit="$GITHUB_SHA"',
    "API compatibility gate": "-p:EnablePackageValidation=true",
    "0.13.0 API baseline": "-p:PackageValidationBaselineVersion=0.13.0",
    "org feed": "https://nuget.pkg.github.com/FS-GG/index.json",
    "public feed": "https://api.nuget.org/v3/index.json",
}
for label, token in required.items():
    if token not in text:
        raise SystemExit(f"release workflow is missing {label}: {token}")
if text.count("dotnet pack FS.GG.Game.slnx") != 1:
    raise SystemExit("release workflow must prepare the coherent set exactly once")
org = text.index("https://nuget.pkg.github.com/FS-GG/index.json")
public = text.index("https://api.nuget.org/v3/index.json")
if org >= public:
    raise SystemExit("release workflow must push the org feed before nuget.org")
if "dotnet pack" in text[org:public]:
    raise SystemExit("release workflow must not re-pack between feed pushes")
PY

[[ -z "$packages" ]] && exit 0

mapfile -t nupkgs < <(find "$packages" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg' -printf '%f\n' | sort)
expected=(
  FS.GG.Game.Core.0.14.0.nupkg
  FS.GG.Game.Harness.0.14.0.nupkg
  FS.GG.Game.Render.0.14.0.nupkg
)
[[ "${nupkgs[*]}" == "${expected[*]}" ]] || {
  echo "expected exactly the coherent 0.14.0 package set" >&2
  printf 'observed: %s\n' "${nupkgs[*]:-<none>}" >&2
  exit 1
}

expected_commit="$(git -C "$root" rev-parse HEAD)"
for package in "${nupkgs[@]}"; do
  nuspec="$(unzip -Z1 "$packages/$package" | sed -n '/\.nuspec$/p')"
  [[ -n "$nuspec" ]] || { echo "$package has no nuspec" >&2; exit 1; }
  metadata="$(unzip -p "$packages/$package" "$nuspec")"
  grep -q '<version>0.14.0</version>' <<<"$metadata" || {
    echo "$package does not declare version 0.14.0" >&2; exit 1;
  }
  grep -q "commit=\"$expected_commit\"" <<<"$metadata" || {
    echo "$package does not bind repository commit $expected_commit" >&2; exit 1;
  }
done

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
  <ItemGroup><PackageReference Include="FS.GG.Game.Harness" Version="0.14.0" /></ItemGroup>
</Project>
EOF
cat >"$consumer/src/Program.fs" <<'EOF'
open FS.GG.Game.Harness

let receiptDigest : JourneyReceipt -> string = JourneyReceipt.definitionDigest
let coverageVerdict : ActionCoverageReport -> bool = ActionCoverageReport.isClean
let coverageCheck = Journey.checkActionCoverage

printfn "%A %A %A" receiptDigest coverageVerdict coverageCheck
EOF
dotnet restore "$consumer/src/Consumer.fsproj" --configfile "$consumer/NuGet.Config" --packages "$consumer/packages"
dotnet build "$consumer/src/Consumer.fsproj" --no-restore
