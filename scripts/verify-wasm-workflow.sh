#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workflow="$repo/.github/workflows/wasm-shared.yml"

python3 - "$workflow" "$repo/global.json" "$repo/.config/dotnet-tools.json" "$repo/.github/workflows/gate.yml" <<'PY'
import json
import pathlib
import sys

workflow = pathlib.Path(sys.argv[1])
text = workflow.read_text(encoding="utf-8")

required = [
    "permissions:\n  contents: read",
    "timeout-minutes: 35",
    'dotnet-version: "10.0.401"',
    'node-version: "26"',
    "QUINT_VERSION: 0.32.0",
    "RUST_VERSION: 1.90.0",
    "PLAYWRIGHT_VERSION: 1.63.0",
    "scripts/verify-wasm-contracts.sh",
    "scripts/verify-wasm-lifecycle.sh",
    "scripts/verify-wasm-package-consumer.sh",
    "scripts/wasm-release/prepare.sh",
    "tests/Wasm.Browser.Conformance/run.sh",
]
ordered = [
    "Static workflow preflight",
    "Set up .NET",
    "Set up Node",
    "Prepare cold package and tool state",
    "Install pinned Quint",
    "Install pinned Rust",
    "Install pinned Playwright Chromium",
    "Rebuild pinned WebAssembly fixtures",
    "Verify compatibility contracts",
    "Verify lifecycle, packages, and Fable consumers",
    "Run browser Worker conformance",
    "Verify SDK archive and fresh package consumer",
]
def validate(candidate: str) -> None:
    missing = [value for value in required if value not in candidate]
    if missing:
        raise ValueError(f"missing required declarations: {missing}")
    if candidate.count("\n  qualify:\n") != 1:
        raise ValueError("expected exactly one linear qualify job")
    positions = [candidate.find(value) for value in ordered]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        raise ValueError("qualification steps are missing or out of order")

try:
    validate(text)
except ValueError as error:
    raise SystemExit(f"workflow preflight: {error}") from error

for broken in (
    text.replace("scripts/verify-wasm-contracts.sh", "scripts/missing-contract-gate.sh"),
    text.replace(ordered[7], "BROKEN-A").replace(ordered[8], ordered[7]).replace("BROKEN-A", ordered[8]),
):
    try:
        validate(broken)
    except ValueError:
        pass
    else:
        raise SystemExit("workflow preflight: a known-bad mutation was accepted")

global_json = json.loads(pathlib.Path(sys.argv[2]).read_text(encoding="utf-8"))
if global_json["sdk"]["version"] != "10.0.401":
    raise SystemExit("workflow preflight: global.json SDK is not 10.0.401")

tools = json.loads(pathlib.Path(sys.argv[3]).read_text(encoding="utf-8"))["tools"]
if tools["fable"]["version"] != "5.18.0" or tools["fable"].get("rollForward") is not False:
    raise SystemExit("workflow preflight: Fable manifest pin is not exact 5.18.0")

gate = pathlib.Path(sys.argv[4]).read_text(encoding="utf-8")
if "ACTIONLINT_VERSION: 1.7.7" not in gate or "SHELLCHECK_VERSION: 0.11.0" not in gate:
    raise SystemExit("workflow preflight: repository lint tool pins changed")
if "&wasm_paths" in text or "*wasm_paths" in text:
    raise SystemExit("workflow preflight: actionlint 1.7.7 requires literal path sequences")
PY

grep -Fq "dotnet restore \"\$repo/tests/Wasm.Contracts.Tests/FS.GG.Wasm.Contracts.Tests.fsproj\" --locked-mode" \
  "$repo/scripts/verify-wasm-contracts.sh" || {
  echo "workflow preflight: contract tests lack an explicit locked restore" >&2
  exit 1
}

if grep -n '__SOURCE_DIRECTORY__' \
  "$repo/tests/Wasm.Contracts.Tests/ContractTests.fs" \
  "$repo/tests/Wasm.Invocation.Tests/Program.fs"; then
  echo "workflow preflight: hosted fixture tests depend on a path-mapped source directory" >&2
  exit 1
fi

grep -Fq '<Link>fixtures/%(Filename)%(Extension)</Link>' \
  "$repo/tests/Wasm.Contracts.Tests/FS.GG.Wasm.Contracts.Tests.fsproj" || {
  echo "workflow preflight: contract fixture content boundary is missing" >&2
  exit 1
}
grep -Fq '<Link>modules/%(RecursiveDir)%(Filename)%(Extension)</Link>' \
  "$repo/tests/Wasm.Invocation.Tests/FS.GG.Wasm.Invocation.Tests.fsproj" || {
  echo "workflow preflight: invocation module content boundary is missing" >&2
  exit 1
}

grep -Fq 'quint-lifecycle-tests: passed=' "$repo/scripts/verify-wasm-lifecycle.sh" || {
  echo "workflow preflight: Quint gate does not report its selected named-test count" >&2
  exit 1
}
test "$(grep -Ec '^  run [A-Za-z0-9]+Test =' "$repo/eng/wasm-shared/lifecycle.qnt")" -eq 8 || {
  echo "workflow preflight: expected eight explicitly named Quint lifecycle tests" >&2
  exit 1
}

grep -Fq 'b761e3a0721dbae9c09a0059e5fdb2bf917d1b4a8a7b430fb3b5aafb0984b2c4' \
  "$repo/sdk/wasm/toolchains/acquire-wasi-sdk.sh" || {
  echo "workflow preflight: WASI SDK 34 archive identity is not pinned" >&2
  exit 1
}
grep -Fq '<PackageReference Include="FS.GG.Wasm.Browser" Version="[0.1.1]" />' \
  "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj" || {
  echo "workflow preflight: fresh consumer does not bind the stable 0.1.1 browser package" >&2
  exit 1
}
if grep -Fq '<ProjectReference' "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj"; then
  echo "workflow preflight: fresh package consumer has a sibling project reference" >&2
  exit 1
fi
"$repo/tests/release/wasm/test-release-wasm.sh" --source-only
for control in \
  'rust-bar.wasm' 'rust-sc2.wasm' 'c-bar.wasm' 'c-sc2.wasm' \
  'trap remains inside the package Worker' \
  'deadline terminates and repeated disposal is harmless'; do
  grep -Fq "$control" "$repo/tests/Wasm.PackageConsumer/browser/package-consumer.spec.mjs" || {
    echo "workflow preflight: missing installed-package browser control $control" >&2
    exit 1
  }
done
for path in 'sdk/wasm/**' 'examples/wasm/**' 'tests/Wasm.PackageConsumer/**' 'scripts/wasm-release/**' 'tests/release/wasm/**'; do
  grep -Fq -- "- \"$path\"" "$workflow" || {
    echo "workflow preflight: missing stage .3 path filter $path" >&2
    exit 1
  }
done

bash -n \
  "$repo/scripts/verify-wasm-workflow.sh" \
  "$repo/scripts/verify-wasm-contracts.sh" \
  "$repo/scripts/verify-wasm-lifecycle.sh" \
  "$repo/scripts/verify-wasm-package-consumer.sh" \
  "$repo/scripts/wasm-release/prepare.sh" \
  "$repo/scripts/wasm-release/readback.sh" \
  "$repo/scripts/wasm-release/stage-assets.sh" \
  "$repo/sdk/wasm/build-source-archive.sh" \
  "$repo/sdk/wasm/verify-sdk.sh" \
  "$repo/sdk/wasm/toolchains/acquire-wasi-sdk.sh" \
  "$repo/tests/Wasm.Compatibility/modules/build-fixtures.sh" \
  "$repo/tests/Wasm.Browser.Conformance/run.sh" \
  "$repo/tests/Wasm.PackageConsumer/browser/run.sh"

echo "wasm-workflow-preflight: static=pass jobs=1 order=linear inputs=pinned"
