#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
packages=""
results=""
output=""

while (($#)); do
  case "$1" in
    --packages) packages="$(cd "$2" && pwd)"; shift 2 ;;
    --results) results="$(cd "$2" && pwd)"; shift 2 ;;
    --output) output="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

[[ -n "$packages" && -n "$results" && -n "$output" ]] || {
  echo "usage: $0 --packages DIR --results DIR --output FILE" >&2
  exit 2
}

"$root/tests/release/test-game-0-14-release.sh" --source-only
"$root/tests/release/test-game-0-14-release-selftest.sh"
"$root/tests/release/test-game-0-14-release.sh" --packages "$packages"

mkdir -p "$(dirname "$output")"
python3 - "$root" "$results" "$packages" "$output" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1])
results = pathlib.Path(sys.argv[2])
packages = pathlib.Path(sys.argv[3])
output = pathlib.Path(sys.argv[4])
reports = [
    ("Game.Core.Tests", results / "Game.Core.Tests.trx"),
    ("Game.Render.Tests", results / "Game.Render.Tests.trx"),
    ("Game.Harness.Tests", results / "Game.Harness.Tests.trx"),
    ("Playtest.Cli.Tests", results / "Playtest.Cli.Tests.trx"),
]

observed = []
for name, path in reports:
    tree = ET.parse(path)
    counters = next(node for node in tree.iter() if node.tag.endswith("Counters"))
    passed = int(counters.attrib["passed"])
    failed = int(counters.attrib["failed"])
    skipped = int(counters.attrib.get("notExecuted", "0"))
    if failed or passed <= 0:
        raise SystemExit(f"{name} is not a passing non-empty run: passed={passed} failed={failed}")
    observed.append((name, passed, skipped, path.name))

release_cases = [
    ("release-source-positive-control", "both actual dotnet nuget push operations accepted"),
    ("release-first-push-mutation", "substituting only the GitHub Packages push was rejected"),
    ("release-second-push-mutation", "substituting only the nuget.org push was rejected"),
    ("release-both-pushes-mutation", "substituting both push operations was rejected"),
    ("release-version-mutation", "mutating the coherent scalar from 0.14.0 to 0.14.1 was rejected"),
    ("coherent-package-consumer", "three packages, nuspec commit metadata, and clean Journey API consumer"),
]
suite = ET.Element("testsuite", {
    "name": "FS.GG.Game 0.14.0 release candidate",
    "tests": str(len(observed) + len(release_cases)),
    "failures": "0",
    "errors": "0",
    "skipped": "0",
})
for name, passed, skipped, report in observed:
    case = ET.SubElement(suite, "testcase", {"classname": "repository-suite", "name": name})
    ET.SubElement(case, "system-out").text = f"report={report}; passed={passed}; failed=0; skipped={skipped}"
for name, detail in release_cases:
    case = ET.SubElement(suite, "testcase", {"classname": "release-contract", "name": name})
    ET.SubElement(case, "system-out").text = detail
ET.ElementTree(suite).write(output, encoding="utf-8", xml_declaration=True)
PY
