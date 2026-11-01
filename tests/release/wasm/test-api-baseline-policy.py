#!/usr/bin/env python3
"""Exercise real native diagnostic classification and closed migration receipts."""
import copy
import importlib.util
import json
import pathlib
import sys
import xml.etree.ElementTree as ET
sys.dont_write_bytecode = True
ROOT = pathlib.Path(__file__).resolve().parents[3]


def module(name, file):
    spec = importlib.util.spec_from_file_location(name, file)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


comparison = module("comparison", ROOT / "scripts/wasm-release/compare-published-baseline.py")
manifest = module("manifest", ROOT / "scripts/wasm-release/release_manifest.py")
current_policy = comparison.POLICY
comparison.POLICY = json.loads((ROOT / "scripts/wasm-release/api-baseline-policy-0.2.0.json").read_text())
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

# Current classification uses the selected published baseline, without reusing
# a historical declared constructor-break disposition.
comparison.POLICY = current_policy
for identity in current_policy["packages"]:
    assert comparison.classify(identity, 0, "APICompat ran successfully") == {"status":"compatible", "diagnostics":[]}
    for rc, diagnostics in [(1,"unavailable"),(1,log),(0,log)]:
        try: comparison.classify(identity, rc, diagnostics)
        except AssertionError: pass
        else: raise SystemExit("additive successor accepted unavailable/breaking native verdict")
assert manifest.api_policy(current_policy["candidateVersion"]) == current_policy
assert current_policy["candidateVersion"] == ET.parse(ROOT / "eng/wasm-shared/version.props").findtext("./PropertyGroup/WasmSharedVersion")
assert current_policy["baselineVersion"] == manifest.api_policy("0.6.0")["candidateVersion"]
assert manifest.api_policy("0.6.0")["baselineVersion"] == "0.5.0"
assert manifest.api_policy("0.5.0")["baselineVersion"] == "0.4.0"
assert manifest.api_policy("0.4.0")["baselineVersion"] == "0.3.0"
assert manifest.api_policy("0.3.0")["baselineVersion"] == "0.2.0"
assert manifest.api_policy("0.2.0")["baselineVersion"] == "0.1.1"
# Exercise both exact current and immutable predecessor policy readers through
# the actual receipt validator, including changed valid-looking source/hash data.
for selected in (current_policy, manifest.api_policy("0.6.0")):
    selected_rows = [{"id": identity, "baselineArchiveSha256": expected["servedArchiveSha256"],
                      "candidateArchiveSha256": "e" * 64, "baselineApiSurface": {"api-surface/test.fsi": "f" * 64},
                      "candidateApiSurface": {"api-surface/test.fsi": "f" * 64}, "nativeLogSha256": "a" * 64,
                      "status": "compatible", "diagnostics": []} for identity, expected in selected["packages"].items()]
    current_receipt = {"schema": "fsgg.wasm.published-api-comparison/v1", "tool": "SDK10.0.401.ApiCompat",
                       "packages": selected_rows, **{key: selected[key] for key in
                       ("baselineVersion", "baselineSource", "baselineTree", "baselineManifestSha256", "candidateVersion")}}
    current_artifacts = [{"file": f"{identity}.{selected['candidateVersion']}.nupkg", "sha256": "e" * 64} for identity in selected["packages"]]
    manifest.verify_api_comparison(current_receipt, current_artifacts)
    for field in ("baselineSource", "baselineTree", "baselineManifestSha256", "served-bytes"):
        broken = copy.deepcopy(current_receipt)
        if field == "served-bytes": broken["packages"][0]["baselineArchiveSha256"] = "0" * 64
        else: broken[field] = "0" * len(broken[field])
        try: manifest.verify_api_comparison(broken, current_artifacts)
        except SystemExit: pass
        else: raise SystemExit("changed current/predecessor baseline identity admitted: " + field)
try: manifest.api_policy("0.2.1")
except SystemExit: pass
else: raise SystemExit("unselected patch version accepted")
print(f"wasm-api-baseline-policy: current candidate={current_policy['candidateVersion']} baseline={current_policy['baselineVersion']} unexpected-breaks/unavailable/unselected-version=refused")

# Closed toolchain pair: the retained SDK 10.0.401 manifests of every published release
# keep verifying, a candidate prepared under SDK 10.0.403 verifies, and nothing else does.
# The ApiCompat receipt label must name the manifest's own SDK. The retained 10.0.401
# identity is admitted for the seven published versions only; a later version is admitted
# under 10.0.403 alone.
NEW_VERSION = "0.8.0"
manifest.HISTORICAL_API_POLICIES.append({**current_policy, "candidateVersion": NEW_VERSION})
assert manifest.api_policy(NEW_VERSION)["candidateVersion"] == NEW_VERSION


def identity_manifest(dotnet, label, version=None):
    version = version or current_policy["candidateVersion"]
    names = list(manifest.expected_files(version).values())
    rows = [{"id": identity, "baselineArchiveSha256": expected["servedArchiveSha256"],
             "candidateArchiveSha256": "e" * 64, "baselineApiSurface": {"api-surface/test.fsi": "f" * 64},
             "candidateApiSurface": {"api-surface/test.fsi": "f" * 64}, "nativeLogSha256": "a" * 64,
             "status": "compatible", "diagnostics": []} for identity, expected in current_policy["packages"].items()]
    receipt = {"schema": "fsgg.wasm.published-api-comparison/v1", "tool": label, "packages": rows,
               **{key: current_policy[key] for key in ("baselineVersion", "baselineSource", "baselineTree", "baselineManifestSha256")},
               "candidateVersion": version}
    return {"schema": manifest.SCHEMA, "version": version, "tag": f"wasm/v{version}", "source": "1" * 40, "tree": "2" * 40,
            "roster": list(manifest.ROSTER), "workflow": ".github/workflows/wasm-shared.yml",
            "tools": {**manifest.TOOLS, "dotnet": dotnet}, "artifacts": [{"file": name, "sha256": "e" * 64} for name in names],
            "firstStableBaseline": {"version": "0.1.1", "publishedApiCompatBaseline": current_policy["baselineVersion"]},
            "publishedApiComparison": receipt}


def legacy_manifest(dotnet):
    """The 0.1.1-shaped legacy schema has no ApiCompat receipt, so only the tools closure can refuse it."""
    names = list(manifest.expected_files("0.1.1").values())
    return {"schema": manifest.LEGACY_SCHEMA, "version": "0.1.1", "tag": "wasm/v0.1.1", "source": "1" * 40, "tree": "2" * 40,
            "roster": list(manifest.ROSTER), "workflow": ".github/workflows/wasm-shared.yml",
            "tools": {**manifest.TOOLS, "dotnet": dotnet}, "artifacts": [{"file": name, "sha256": "e" * 64} for name in names],
            "firstStableBaseline": {"version": "0.1.1", "publishedApiCompatBaseline": None}}


def refused(data, why):
    try: manifest.verify_identity(data)
    except SystemExit: return
    raise SystemExit("unadmitted or mismatched release toolchain accepted: " + why)


assert manifest.TOOLS["dotnet"] == "10.0.403" and manifest.RETAINED_TOOLS["dotnet"] == "10.0.401"
assert {key: value for key, value in manifest.TOOLS.items() if key != "dotnet"} == {key: value for key, value in manifest.RETAINED_TOOLS.items() if key != "dotnet"}
assert manifest.RETAINED_VERSIONS == {"0.1.1", "0.2.0", "0.3.0", "0.4.0", "0.5.0", "0.6.0", "0.7.0"}
# Admitted: retained 401 for a published version (v2 and legacy), current 403 for the candidate and a new version.
manifest.verify_identity(identity_manifest("10.0.401", "SDK10.0.401.ApiCompat"))
manifest.verify_identity(identity_manifest("10.0.403", "SDK10.0.403.ApiCompat"))
manifest.verify_identity(identity_manifest("10.0.403", "SDK10.0.403.ApiCompat", NEW_VERSION))
manifest.verify_identity(legacy_manifest("10.0.401"))
# Label must name the manifest's own SDK.
refused(identity_manifest("10.0.401", "SDK10.0.403.ApiCompat"), "401 tools with 403 label")
refused(identity_manifest("10.0.403", "SDK10.0.401.ApiCompat"), "403 tools with 401 label")
# Receipt label outside the admitted set.
for dotnet in ("10.0.402", "10.0.404"):
    refused(identity_manifest(dotnet, f"SDK{dotnet}.ApiCompat"), f"unadmitted ApiCompat label {dotnet}")
# Isolates the tools closure: no receipt or label exists to refuse these first.
for dotnet in ("10.0.402", "10.0.404"):
    refused(legacy_manifest(dotnet), f"legacy manifest with unadmitted tools.dotnet {dotnet}")
# Isolates the retained-version restriction: a new version claiming the retained 401 identity with a
# matching 401 label is otherwise indistinguishable from a published one.
refused(identity_manifest("10.0.401", "SDK10.0.401.ApiCompat", NEW_VERSION), "new version claiming retained SDK 10.0.401")
print("wasm-release-toolchain: admitted={10.0.401 retained for 7 published versions, 10.0.403 current} label-mismatch/unadmitted-sdk/new-version-on-401=refused")
