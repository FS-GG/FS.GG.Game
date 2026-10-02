#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workflow="$repo/.github/workflows/wasm-shared.yml"

python3 - "$workflow" "$repo/global.json" "$repo/.config/dotnet-tools.json" <<'PY'
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
PY

bash -n \
  "$repo/scripts/verify-wasm-workflow.sh" \
  "$repo/scripts/verify-wasm-contracts.sh" \
  "$repo/scripts/verify-wasm-lifecycle.sh" \
  "$repo/tests/Wasm.Compatibility/modules/build-fixtures.sh" \
  "$repo/tests/Wasm.Browser.Conformance/run.sh"

echo "wasm-workflow-preflight: static=pass jobs=1 order=linear inputs=pinned"
