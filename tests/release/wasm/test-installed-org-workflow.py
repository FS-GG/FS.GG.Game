#!/usr/bin/env python3
"""Exercise the read-only workflow's real immutable receipt binding predicates."""
import copy
import sys
sys.dont_write_bytecode = True
import importlib.util
import pathlib
import re

root = pathlib.Path(__file__).resolve().parents[3]
text = (root / ".github/workflows/wasm-installed-org.yml").read_text()
spec = importlib.util.spec_from_file_location("binding", root / "scripts/wasm-release/bind-installed-release.py")
binding = importlib.util.module_from_spec(spec)
spec.loader.exec_module(binding)


def validate(candidate):
    assert "on:\n  workflow_dispatch:\n    inputs:" in candidate and "  push:" not in candidate
    installed=candidate.split('  installed:\n',1)[1]
    grant=installed.split('    permissions:\n',1)[1].split('    env:\n',1)[0]
    assert re.findall(r'(?m)^      (actions|contents|packages): (\S+)$',grant)==[('actions','read'),('contents','read'),('packages','read')], "installed evidence endpoints require scoped Actions read"
    assert "packages: read" in candidate and candidate.count("    runs-on:") == 1
    assert all(value not in candidate for value in ("packages: write", "contents: write", "id-token:", "dotnet pack", "nuget push", "gh release", "git tag"))
    for name in ("version", "published_source", "qualifier_source", "manifest_sha256", "sdk_sha256", "accepted_executor", "promotion_binding_sha256"):
        assert f"      {name}:" in candidate and f"${{{{ inputs.{name} }}}}" in candidate
    assert "feed: [org, public]" in candidate and "max-parallel: 1" in candidate and "fail-fast: false" in candidate
    assert "https://nuget.pkg.github.com/FS-GG/index.json" in candidate and "https://api.nuget.org/v3/index.json" in candidate
    assert "run: python3 scripts/wasm-release/bind-installed-release.py" in candidate
    assert "scripts/verify-wasm-package-consumer.sh --feed-only" in candidate
    assert "scripts/wasm-release/qualify-supervisor-installed.sh --feed-only" in candidate
    assert "matrix.feed == 'org'" in candidate and "NuGetPackageSourceCredentials_wasm:" in candidate
    steps = ["Static installed qualification preflight", "Bind qualification source and immutable published receipts", "Install exact consumer tools", "Qualify fresh installed consumer"]
    positions = [candidate.index(value) for value in steps]
    assert positions == sorted(positions)
    assert 'cp -a "$RUNNER_TEMP/selected-' not in candidate
    assert 'installed-evidence.py assemble' in candidate and 'if-no-files-found: error' in candidate
    assert 'WASM_CONSUMER_EVIDENCE=' in candidate and 'WASM_CONSUMER_STATUS=' in candidate
    assert candidate.count('if: always()') == 2
    assert 'path: ${{ runner.temp }}/installed-${{ matrix.feed }}-evidence/' in candidate
    assert "fetch-depth: 0" in candidate and "ref:" not in candidate


validate(text)
for broken in (
    text.replace("packages: read", "packages: write"),
    text.replace("      actions: read\n", ""),
    text.replace("      actions: read", "      actions: none"),
    text.replace("      actions: read", "      actions: write"),
    text.replace("--feed-only", "--custody artifacts/wasm-release"),
    text.replace("scripts/wasm-release/qualify-supervisor-installed.sh --feed-only", "true"),
    text.replace("max-parallel: 1", "max-parallel: 2"),
    text.replace("fail-fast: false", "fail-fast: true"),
    text.replace("installed-evidence.py assemble", "true"),
    text.replace("if-no-files-found: error", "if-no-files-found: ignore"),
    text.replace("python3 scripts/wasm-release/bind-installed-release.py", "true"),
    text.replace("          fetch-depth: 0", "          fetch-depth: 0\n          ref: wasm/v0.1.1"),
):
    try:
        validate(broken)
    except (AssertionError, ValueError):
        pass
    else:
        raise SystemExit("known-bad installed qualification route accepted")

# Fixture rule (FS-GG/.github#5109): the current version, its history and the published
# identity are derived from observed state (version.props, the retained-release roster
# and the manifest verifier), never hard-coded, so this control survives the next release.
current = binding.tracked_version()
manifest_tool = binding.release_manifest_module()


def semver(version):
    return tuple(int(part) for part in version.split("."))


historical_versions = sorted(manifest_tool.RETAINED_VERSIONS - {current}, key=semver)
assert historical_versions, "no retained historical release observed"
historical = historical_versions[-1]
source = "1" * 40
values = {"RELEASE_VERSION": current, "PUBLISHED_SOURCE": source, "QUALIFIER_SOURCE": "a" * 40, "ACCEPTED_EXECUTOR": "a" * 40, "PROMOTION_BINDING_SHA256": "e" * 64, "MANIFEST_SHA256": "c" * 64, "SDK_SHA256": "d" * 64}
assert binding.checked_inputs(values, current) == values


def refused(call, fragment, label):
    try:
        call()
    except AssertionError as error:
        assert fragment in str(error), f"{label}: refused for the wrong cause: {error}"
    else:
        raise SystemExit(f"{label} accepted")


# Causal control: the identical dispatch differing only in the release version flips from
# accepted (current) to refused (historical), and the refusal names the historical cause.
for old in historical_versions:
    refused(lambda old=old: binding.checked_inputs(dict(values, RELEASE_VERSION=old), current), f"historical release {old} is not the current route", f"historical {old}")
# version.props is the observed selector: a dispatch whose version differs from what it selects is refused
# whichever side is stale.
refused(lambda: binding.checked_inputs(values, historical), f"historical release {current} is not the current route", "version.props mismatch")
for key, value in (("PUBLISHED_SOURCE", "main"), ("QUALIFIER_SOURCE", "wasm/v" + current), ("SDK_SHA256", "unknown"), ("RELEASE_VERSION", "0.1.1"), ("MANIFEST_SHA256", "$(false)")):
    try:
        binding.checked_inputs(dict(values, **{key: value}), current)
    except AssertionError:
        pass
    else:
        raise SystemExit(f"unbound dispatch {key} accepted")
for missing in ('ACCEPTED_EXECUTOR','PROMOTION_BINDING_SHA256'):
    broken=dict(values);broken.pop(missing)
    try:binding.checked_inputs(broken,current)
    except (AssertionError,KeyError):pass
    else:raise SystemExit('missing current transaction/executor accepted')
for key, value in [('QUALIFIER_SOURCE','b'*40),('EXECUTION_BINDING','{}'),('CANONICAL_QUALIFICATION_AUDIT','historical')]:
    try:binding.checked_inputs(dict(values,**{key:value}),current)
    except (AssertionError,KeyError):pass
    else:raise SystemExit('current qualifier/overlay substitution accepted')
# The immutable publication source is no longer a pinned constant: its authority is the
# tag-resolved source bound by the published manifest, exercised below.
assert not hasattr(binding, "PUBLISHED") and not hasattr(binding, "PUBLISHED_TREE") and not hasattr(binding, "INSTALLED_PATHS")


def identity_manifest(version, published_source, tree):
    policy = manifest_tool.api_policy(version)
    names = list(manifest_tool.expected_files(version).values())
    rows = [{"id": identity, "baselineArchiveSha256": expected["servedArchiveSha256"], "candidateArchiveSha256": "e" * 64,
             "baselineApiSurface": {"api-surface/test.fsi": "f" * 64}, "candidateApiSurface": {"api-surface/test.fsi": "f" * 64},
             "nativeLogSha256": "a" * 64, "status": expected["expectedStatus"],
             "diagnostics": [{"code": "CP0002", "member": f"FS.GG.Wasm.Browser.{name}.{name}()"} for name in expected["removedConstructorTypes"]]}
            for identity, expected in policy["packages"].items()]
    receipt = {"schema": "fsgg.wasm.published-api-comparison/v1", "tool": f"SDK{manifest_tool.TOOLS['dotnet']}.ApiCompat", "packages": rows,
               **{key: policy[key] for key in ("baselineVersion", "baselineSource", "baselineTree", "baselineManifestSha256", "candidateVersion")}}
    return {"schema": manifest_tool.SCHEMA, "version": version, "tag": f"wasm/v{version}", "source": published_source, "tree": tree,
            "roster": list(manifest_tool.ROSTER), "workflow": ".github/workflows/wasm-shared.yml", "tools": dict(manifest_tool.TOOLS),
            "artifacts": [{"file": name, "sha256": "e" * 64} for name in names],
            "firstStableBaseline": {"version": "0.1.1", "publishedApiCompatBaseline": policy["baselineVersion"]},
            "publishedApiComparison": receipt}


tree = "2" * 40
manifest = identity_manifest(current, source, tree)
assert binding.bind_published_identity(manifest, source, current, tree) == tree
refused(lambda: binding.bind_published_identity(manifest, "3" * 40, current, tree), "manifest identity refused", "manifest source mismatch")
refused(lambda: binding.bind_published_identity(manifest, source, current, "4" * 40), "manifest tree differs", "manifest tree mismatch")
refused(lambda: binding.bind_published_identity(manifest, source, historical, tree), "differs from version.props", "version.props / manifest mismatch")
refused(lambda: binding.bind_published_identity(identity_manifest(historical, source, tree), source, current, tree), "differs from version.props", "historical manifest under current version.props")
for mutant in ("tag", "roster", "artifacts", "schema"):
    broken = copy.deepcopy(manifest)
    if mutant == "tag": broken["tag"] = "wasm/v" + historical
    elif mutant == "roster": broken["roster"] = list(reversed(broken["roster"]))
    elif mutant == "artifacts": broken["artifacts"].pop()
    else: broken["schema"] = "fsgg.wasm.release-custody/v0"
    refused(lambda broken=broken: binding.bind_published_identity(broken, source, current, tree), "manifest identity refused", f"manifest {mutant} mutation")

# N2: the tag-resolution assertion is a function. A tag naming a different commit (moved tag), an absent
# tag, and the matching tag are each decided on the resolved commit alone.
import subprocess
assert binding.bind_tag_source(current, source, lambda *args: source) == "wasm/v" + current
refused(lambda: binding.bind_tag_source(current, source, lambda *args: "9" * 40), "tag does not name published source", "moved tag")


def absent(*args):
    raise subprocess.CalledProcessError(128, "git")


refused(lambda: binding.bind_tag_source(current, source, absent), "is absent", "absent tag")

# B2: the consumer's Fable tool pin is bound to the published manifest.
import json as _json
import tempfile
with tempfile.TemporaryDirectory() as scratch:
    tools = pathlib.Path(scratch) / "dotnet-tools.json"
    def write(value):
        tools.write_text(_json.dumps(value))
    fable = manifest["tools"]["fable"]
    write({"version": 1, "tools": {"fable": {"version": fable}}})
    assert binding.bind_tool_manifest(manifest, tools) == fable
    write({"version": 1, "tools": {"fable": {"version": "0.0.1"}}})
    refused(lambda: binding.bind_tool_manifest(manifest, tools), "differs from the published manifest", "fable pin mismatch")
    write({"version": 1, "tools": {"other": {"version": fable}}})
    refused(lambda: binding.bind_tool_manifest(manifest, tools), "lacks a fable pin", "missing fable pin")
    tools.write_text("not json")
    refused(lambda: binding.bind_tool_manifest(manifest, tools), "lacks a fable pin", "malformed tool manifest")
assert binding.bind_tool_manifest(manifest) == manifest["tools"]["fable"], "tracked tool manifest must agree with the current toolchain"

# B1: derive the installed-consumer input closure from what the installed run actually builds
# and require every resolved path to be covered, so an omitted prefix fails here.
import os
import xml.etree.ElementTree as ET


def project_references(project):
    """Repo-relative paths a project compiles, references or imports (MSBuild properties are not resolved)."""
    found = {project}
    for element in ET.parse(root / project).iter():
        name = element.tag.split("}")[-1]
        if name in ("Compile", "ProjectReference", "Content", "EmbeddedResource", "None"):
            include = element.get("Include")
        elif name == "Import":
            include = element.get("Project")
        else:
            continue
        if include and "$(" not in include:
            found.add(os.path.normpath(os.path.join(os.path.dirname(project), include)).replace(os.sep, "/"))
    return found


scripts_text = {}
def script_closure(seeds):
    queue, seen = list(seeds), set()
    while queue:
        name = queue.pop()
        if name in seen or not (root / name).is_file():
            continue
        seen.add(name)
        scripts_text[name] = (root / name).read_text()
        queue.extend(re.findall(r"scripts/[A-Za-z0-9_./-]+\.(?:sh|py)", scripts_text[name]))
    return seen


workflow_text = (root / ".github/workflows/wasm-installed-org.yml").read_text()
installed_scripts = script_closure(set(re.findall(r"scripts/[A-Za-z0-9_./-]+\.(?:sh|py)", workflow_text)))
assert {"scripts/verify-wasm-package-consumer.sh", "scripts/wasm-release/qualify-supervisor-installed.sh", "scripts/verify-wasm-supervisor.sh"} <= installed_scripts

projects = {"tests/Wasm.PackageConsumer/Consumer.fsproj", "tests/Wasm.Supervisor.Compatibility/Compatibility.fsproj",
            "tests/Wasm.Supervisor.Compatibility/Installed/InstalledCompatibility.fsproj"}
for name in installed_scripts:
    projects.update(re.findall(r"tests/[A-Za-z0-9_./-]+\.fsproj", scripts_text[name]))
derived, queue = set(), sorted(projects)
while queue:
    project = queue.pop()
    for path in project_references(project):
        if path not in derived and path.endswith("proj") and path != project:
            queue.append(path)
        derived.add(path)
# Repo paths the installed drivers read (outside /usr/share/dotnet): fixtures, models, SDK helpers.
for name in installed_scripts:
    derived.update(re.findall(r"(?<![A-Za-z0-9_./-])((?:tests|src|sdk|eng)/[A-Za-z0-9_./-]*[A-Za-z0-9_])", scripts_text[name]))
derived = {path for path in derived if "/bin/" not in path and "/obj/" not in path and "/test-results" not in path}
assert len(derived) > 20 and any(path.startswith("tests/Wasm.Lifecycle.Correspondence/") for path in derived), "closure derivation found too little"
uncovered = sorted(path for path in derived if not (path + "/").startswith(binding.CONSUMER_INPUTS) and not path.startswith(binding.CONSUMER_INPUTS))
assert not uncovered, "installed run builds or reads paths outside CONSUMER_INPUTS: " + ", ".join(uncovered)
# Causal control: removing the prefix that covers the Correspondence sources makes the derivation fail.
reduced = tuple(prefix for prefix in binding.CONSUMER_INPUTS if prefix != "tests/Wasm.Lifecycle.Correspondence/")
assert [path for path in derived if not (path + "/").startswith(reduced) and not path.startswith(reduced)], "closure derivation cannot detect an omitted prefix"

# B3: no installed-path script pins a release-version literal (history roster in release_manifest.py is exempt).
import ast
import io
import tokenize
versions = set(manifest_tool.RETAINED_VERSIONS) | {current}
literal = re.compile(r"(?<![0-9.])(?:" + "|".join(re.escape(version) for version in sorted(versions)) + r")(?![0-9.])")


def code_text(name):
    text = scripts_text[name]
    if name.endswith(".py"):
        docstrings = set()
        for node in ast.walk(ast.parse(text)):
            if isinstance(node, (ast.Module, ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)) and node.body and isinstance(node.body[0], ast.Expr) and isinstance(getattr(node.body[0], "value", None), ast.Constant) and isinstance(node.body[0].value.value, str):
                docstrings.add(node.body[0].value.lineno)
        kept = [token.string for token in tokenize.generate_tokens(io.StringIO(text).readline) if token.type == tokenize.STRING and token.start[0] not in docstrings] + \
               [token.string for token in tokenize.generate_tokens(io.StringIO(text).readline) if token.type in (tokenize.NAME, tokenize.NUMBER)]
        return "\n".join(kept)
    return "\n".join(line for line in text.splitlines() if not line.lstrip().startswith("#"))


pinned = {name: literal.findall(code_text(name)) for name in sorted(installed_scripts - {"scripts/wasm-release/release_manifest.py"})}
pinned = {name: found for name, found in pinned.items() if found}
assert not pinned, "installed-path scripts pin a release version literal: " + repr(pinned)
# Causal control: the detector flags the very pin this removed.
assert literal.search("[[ \"$version\" == " + current + " ]]") and not literal.search("[[ \"$version\" == \"${RELEASE_VERSION:?}\" ]]")

release = {"tag_name": f"wasm/v{current}", "draft": False, "prerelease": False, "published_at": "2026-10-02T00:00:00Z", "assets": []}
for name, digest in (("release-manifest.json", "c" * 64), (f"fsgg-wasm-sdk-{current}.tar.gz", "d" * 64), ("promotion-binding.json", "e" * 64)):
    release["assets"].append({"name": name, "digest": "sha256:" + digest, "browser_download_url": f"https://github.com/FS-GG/FS.GG.Game/releases/download/wasm/v{current}/{name}"})
assert len(binding.validate_release(release, values)) == 3
for mutant in ("digest", "url", "draft", "version", "missing"):
    broken = copy.deepcopy(release)
    if mutant == "digest": broken["assets"][0]["digest"] = "sha256:" + "0" * 64
    elif mutant == "url": broken["assets"][1]["browser_download_url"] = "https://example.invalid/archive.tar.gz"
    elif mutant == "draft": broken["draft"] = True
    elif mutant == "version": broken["tag_name"] = "wasm/v" + historical
    else: broken["assets"].pop()
    try:
        binding.validate_release(broken, values)
    except (AssertionError, KeyError):
        pass
    else:
        raise SystemExit(f"native release {mutant} mismatch accepted")

import json
# Both traces come from schedules over the unchanged canonical model and preserve full ordered effects.
trace_root = root / "tests/Wasm.Lifecycle.Correspondence/Traces"
for timing, order, kinds in (
    ("early", [1, 2, 3, 4, 5], ["terminate", "cancelTimer", "settleTimedOut", "settleTimedOut", "settleTimedOut"]),
    ("late", [1, 2, 4, 5, 3], ["settleTimedOut", "settleTimedOut", "terminate", "cancelTimer", "settleTimedOut"]),
):
    trace = json.loads((trace_root / f"expiry-{timing}_0.itf.json").read_text())
    state = trace["states"][-1]["state"]
    assert [int(value["#bigint"]) for value in state["deliveries"]] == order
    assert [effect["kind"] for effect in state["effects"]] == kinds
    assert state["activeWorker"] == {"#bigint": "0"} and not state["ordinary"]
    assert len(set(order)) == 5
print("wasm-installed-input-audit: immutable-publisher=current-qualifier-separated promotion=mandatory expiry-early-late=canonical")

print("wasm-installed-preflight: manual=pass read-only=pass serial-two-feeds=pass qualifier=immutable native-receipts=bound negative-controls=pass")
