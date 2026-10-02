#!/usr/bin/env python3
"""Exercise real native diagnostic classification and closed migration receipts."""
import copy
import importlib.util
import json
import pathlib
import sys
sys.dont_write_bytecode = True
ROOT = pathlib.Path(__file__).resolve().parents[3]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


comparison = module("comparison", ROOT / "scripts/wasm-release/compare-published-baseline.py")
manifest = module("manifest", ROOT / "scripts/wasm-release/release_manifest.py")
fixture = json.loads((ROOT / "tests/release/wasm/fixtures/published-browser-api-break.json").read_text())
log = "\n".join("error " + row["code"] + ": " + row["message"] for row in fixture["diagnostics"]) + "\nAPI breaking changes found"
actual = comparison.classify("FS.GG.Wasm.Browser", 1, log)
assert actual["status"] == "breaking-intentionally-versioned" and len(actual["diagnostics"]) == 4
assert comparison.classify("FS.GG.Wasm.Contracts", 0, "")["status"] == "compatible"
for identity, code, text in (("FS.GG.Wasm.Browser", 0, log), ("FS.GG.Wasm.Browser", 1, log.replace("CP0002", "CP0001", 1)), ("FS.GG.Wasm.Browser", 1, "\n".join(log.splitlines()[1:])), ("FS.GG.Wasm.Contracts", 1, "feed unavailable")):
    try:
        comparison.classify(identity, code, text)
    except AssertionError:
        pass
    else:
        raise SystemExit("missing, unexpected or unavailable native comparison was accepted")
policy = comparison.POLICY
rows = []
artifacts = []
for identity, expectation in policy["packages"].items():
    status = actual if identity.endswith("Browser") else comparison.classify(identity, 0, "")
    row = {"id": identity, "baselineArchiveSha256": expectation["servedArchiveSha256"], "candidateArchiveSha256": "e" * 64, "baselineApiSurface": {"api-surface/test.fsi": "f" * 64}, "candidateApiSurface": {"api-surface/test.fsi": "f" * 64}, "nativeLogSha256": "a" * 64, **status}
    rows.append(row)
    artifacts.append({"file": f"{identity}.0.2.0.nupkg", "sha256": "e" * 64})
receipt = {"schema": "fsgg.wasm.published-api-comparison/v1", "tool": "SDK10.0.401.ApiCompat", "packages": rows, **{key:policy[key] for key in ("baselineVersion", "baselineSource", "baselineTree", "baselineManifestSha256", "candidateVersion")}}
manifest.verify_api_comparison(receipt, artifacts)
for kind in ("null-baseline", "compatible-browser", "changed-baseline-bytes", "wrong-candidate-bytes", "missing-native-receipt", "extra-member"):
    broken = copy.deepcopy(receipt)
    browser = next(row for row in broken["packages"] if row["id"].endswith("Browser"))
    if kind == "null-baseline": broken["baselineVersion"] = None
    elif kind == "compatible-browser": browser["status"] = "compatible"
    elif kind == "changed-baseline-bytes": browser["baselineArchiveSha256"] = "0" * 64
    elif kind == "wrong-candidate-bytes": browser["candidateArchiveSha256"] = "0" * 64
    elif kind == "missing-native-receipt": browser["nativeLogSha256"] = ""
    else: browser["diagnostics"].append({"code":"CP0002", "member":"FS.GG.Wasm.Browser.Unknown.Unknown()"})
    try:
        manifest.verify_api_comparison(broken, artifacts)
    except SystemExit:
        pass
    else:
        raise SystemExit(f"invalid API migration receipt accepted: {kind}")
print("wasm-api-baseline-policy: published=0.1.1 migration=0.2.0 Contracts=compatible Browser=four-declared-constructor-breaks unavailable=refused suppressions=none")
