#!/usr/bin/env python3
"""Build and verify the closed shared-WASM release custody manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import tarfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path, PurePosixPath

SCHEMA = "fsgg.wasm.release-custody/v2"
LEGACY_SCHEMA = "fsgg.wasm.release-custody/v1"
API_POLICY = json.loads((Path(__file__).parent / "api-baseline-policy.json").read_text())
ROSTER = ("FS.GG.Wasm.Contracts", "FS.GG.Wasm.Browser")
TOOLS = {
    "dotnet": "10.0.401",
    "fable": "5.18.0",
    "node": "26",
    "playwrightChromium": "1.63.0",
    "quint": "0.32.0",
    "rust": "1.90.0",
    "wasiSdk": "34.0",
}


def fail(message: str) -> None:
    raise SystemExit(message)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def safe_members(names: list[str], kind: str) -> None:
    if len(names) != len(set(names)):
        fail(f"{kind} contains duplicate paths")
    for name in names:
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts:
            fail(f"unsafe {kind} path: {name}")


def nuspec(package: Path) -> tuple[dict[str, str], dict[str, str], list[str]]:
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        safe_members(names, package.name)
        specs = [name for name in names if name.endswith(".nuspec")]
        if len(specs) != 1:
            fail(f"{package.name} must contain one nuspec")
        root = ET.fromstring(archive.read(specs[0]))
    ns = {"n": "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"}
    metadata = root.find("n:metadata", ns)
    if metadata is None:
        fail(f"{package.name} has no nuspec metadata")
    values = {}
    for key in ("id", "version"):
        value = metadata.find(f"n:{key}", ns)
        values[key] = "" if value is None or value.text is None else value.text
    repository = metadata.find("n:repository", ns)
    values["repositoryCommit"] = "" if repository is None else repository.attrib.get("commit", "")
    dependencies = {
        item.attrib["id"]: item.attrib.get("version", "")
        for item in metadata.findall(".//n:dependency", ns)
    }
    return values, dependencies, names


def inspect_package(path: Path, expected_id: str, version: str, source: str) -> dict[str, object]:
    values, dependencies, names = nuspec(path)
    if values != {"id": expected_id, "version": version, "repositoryCommit": source}:
        fail(f"{path.name} identity/source mismatch: {values}")
    required = {
        "FS.GG.Wasm.Contracts": {
            "api-surface/Contracts.fsi",
            "fable/Contracts.fsi",
            "fable/Contracts.fs",
            "fable/FS.GG.Wasm.Contracts.fsproj",
            "wasm-compatibility/compatibility-profile.v1.json",
        },
        "FS.GG.Wasm.Browser": {
            "api-surface/RuntimeProtocol.fsi",
            "api-surface/Admission.fsi",
            "api-surface/Invocation.fsi",
            "api-surface/WorkerEntry.fsi",
            "api-surface/Lifecycle.fsi",
            "api-surface/Host.fsi",
            "fable/js/wasm-primitives.js",
            "fable/eng/wasm-shared/version.props",
            "contentFiles/any/any/_content/FS.GG.Wasm.Browser/module-worker.mjs",
            "contentFiles/any/any/_content/FS.GG.Wasm.Browser/worker-client.mjs",
            "contentFiles/any/any/_content/FS.GG.Wasm.Browser/policy/WorkerEntry.js",
            "contentFiles/any/any/_content/FS.GG.Wasm.Browser/policy/Invocation.js",
            "contentFiles/any/any/_content/FS.GG.Wasm.Browser/policy/Admission.js",
            "buildTransitive/FS.GG.Wasm.Browser.targets",
        },
    }[expected_id]
    if version == "0.1.1":
        required = {name for name in required if "/policy/" not in name}
    missing = sorted(required.difference(names))
    if missing:
        fail(f"{path.name} missing package closure: {missing}")
    if expected_id == "FS.GG.Wasm.Browser":
        observed = dependencies.get("FS.GG.Wasm.Contracts")
        if observed != f"[{version}]":
            fail(f"Browser dependency must be exact [{version}], got {observed!r}")
        with zipfile.ZipFile(path) as archive:
            props = archive.read("fable/eng/wasm-shared/version.props").decode("utf-8-sig")
            project = archive.read("fable/FS.GG.Wasm.Browser.fsproj").decode("utf-8-sig")
        if f"<WasmSharedVersion>{version}</WasmSharedVersion>" not in props or 'Version="[$(WasmSharedVersion)]"' not in project:
            fail("packaged Fable dependency does not resolve through the stable version origin")
    return {
        "file": path.name,
        "id": expected_id,
        "sha256": digest(path),
        "members": len(names),
        "repositoryCommit": source,
        "dependencies": dependencies,
    }


def inspect_sdk(path: Path, version: str) -> dict[str, object]:
    prefix = f"fsgg-wasm-sdk-{version}/"
    with tarfile.open(path, "r:gz") as archive:
        members = archive.getmembers()
        names = [item.name for item in members]
        safe_members(names, path.name)
        if any(item.issym() or item.islnk() for item in members):
            fail("SDK archive must not contain links")
        if any(not (name == prefix[:-1] or name.startswith(prefix)) for name in names):
            fail("SDK archive has a member outside the versioned root")
        required = {prefix + "SHA256SUMS", prefix + "sdk/wasm/LICENSE", prefix + "sdk/wasm/PROVENANCE.md", prefix + "sdk/wasm/VERSION"}
        if not required.issubset(names):
            fail("SDK archive is missing version/license/provenance/checksum metadata")
        version_bytes = archive.extractfile(prefix + "sdk/wasm/VERSION")
        if version_bytes is None or version_bytes.read().decode().strip() != version:
            fail("SDK archive VERSION mismatch")
    return {"file": path.name, "sha256": digest(path), "members": len(names)}


def expected_files(version: str) -> dict[str, str]:
    return {
        "FS.GG.Wasm.Contracts": f"FS.GG.Wasm.Contracts.{version}.nupkg",
        "FS.GG.Wasm.Browser": f"FS.GG.Wasm.Browser.{version}.nupkg",
        "sdk": f"fsgg-wasm-sdk-{version}.tar.gz",
    }


def load_manifest(path: Path) -> dict[str, object]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        fail(f"invalid release manifest: {error}")
    return data


def verify_manifest(path: Path, custody: Path, source: str | None = None) -> dict[str, object]:
    data = load_manifest(path)
    verify_identity(data, source)
    version = str(data["version"])
    artifacts = data["artifacts"]
    files = expected_files(version)
    expected_names = set(files.values())
    observed_names = {str(row.get("file")) for row in artifacts if isinstance(row, dict)}
    if observed_names != expected_names:
        fail("manifest artifact roster mismatch")
    custody_artifacts = {path.name for path in custody.iterdir() if path.suffix == ".nupkg" or path.name.endswith(".tar.gz")}
    if custody_artifacts != expected_names:
        fail(f"custody contains an incomplete or foreign artifact set: {sorted(custody_artifacts)}")
    for row in artifacts:
        artifact = custody / str(row["file"])
        if not artifact.is_file() or digest(artifact) != row.get("sha256"):
            fail(f"missing or mismatched custody artifact: {artifact.name}")
    expected_rows = [
        inspect_package(custody / files[identity], identity, version, str(data["source"]))
        for identity in ROSTER
    ] + [inspect_sdk(custody / files["sdk"], version)]
    if artifacts != expected_rows:
        fail("manifest metadata does not match inspected custody")
    return data


def verify_identity(data: dict[str, object], source: str | None = None) -> None:
    required_keys = {"schema", "version", "tag", "source", "tree", "roster", "workflow", "tools", "artifacts", "firstStableBaseline"}
    legacy = data.get("schema") == LEGACY_SCHEMA
    if not legacy:
        required_keys.add("publishedApiComparison")
    if set(data) != required_keys or data.get("schema") not in (SCHEMA, LEGACY_SCHEMA):
        fail("manifest schema or closed key set mismatch")
    version = str(data["version"])
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        fail("manifest version must be stable SemVer")
    if data["tag"] != f"wasm/v{version}" or data["roster"] != list(ROSTER):
        fail("manifest tag or two-package roster mismatch")
    if source is not None and data["source"] != source:
        fail("manifest source mismatch")
    if data["tools"] != TOOLS or data["workflow"] != ".github/workflows/wasm-shared.yml":
        fail("manifest tool or source-workflow identity mismatch")
    if legacy:
        if version != "0.1.1" or data["firstStableBaseline"] != {"version": "0.1.1", "publishedApiCompatBaseline": None}:
            fail("legacy manifest must preserve the actual first published 0.1.1 identity")
    else:
        if version != API_POLICY["candidateVersion"] or data["firstStableBaseline"] != {"version": "0.1.1", "publishedApiCompatBaseline": "0.1.1"}:
            fail("connected migration must preserve and compare the actual 0.1.1 baseline")
        verify_api_comparison(data["publishedApiComparison"], data["artifacts"])
    artifacts = data["artifacts"]
    if not isinstance(artifacts, list) or len(artifacts) != 3:
        fail("manifest must contain exactly three artifacts")
    expected_names = set(expected_files(version).values())
    if {str(row.get("file")) for row in artifacts if isinstance(row, dict)} != expected_names:
        fail("manifest artifact roster mismatch")


def verify_api_comparison(comparison, artifacts):
    keys = {"schema", "tool", "baselineVersion", "baselineSource", "baselineTree", "baselineManifestSha256", "candidateVersion", "packages"}
    if not isinstance(comparison, dict) or set(comparison) != keys or comparison["schema"] != "fsgg.wasm.published-api-comparison/v1" or comparison["tool"] != "SDK10.0.401.ApiCompat":
        fail("published API comparison schema/tool mismatch")
    for key in ("baselineVersion", "baselineSource", "baselineTree", "baselineManifestSha256", "candidateVersion"):
        if comparison[key] != API_POLICY[key]:
            fail("published API baseline provenance mismatch")
    rows = comparison["packages"]
    if not isinstance(rows, list) or len(rows) != 2 or {row.get("id") for row in rows} != set(ROSTER):
        fail("published API comparison roster mismatch")
    for row in rows:
        expected = API_POLICY["packages"][row["id"]]
        if set(row) != {"id", "baselineArchiveSha256", "candidateArchiveSha256", "baselineApiSurface", "candidateApiSurface", "nativeLogSha256", "status", "diagnostics"}:
            fail("published API comparison row keys mismatch")
        artifact = next(item for item in artifacts if item["file"] == f"{row['id']}.{comparison['candidateVersion']}.nupkg")
        if row["baselineArchiveSha256"] != expected["servedArchiveSha256"] or row["candidateArchiveSha256"] != artifact["sha256"] or row["status"] != expected["expectedStatus"]:
            fail("published API comparison archive/status mismatch")
        if not re.fullmatch(r"[0-9a-f]{64}", row["nativeLogSha256"]):
            fail("native API comparison log receipt missing")
        for field in ("baselineApiSurface", "candidateApiSurface"):
            surface = row[field]
            if not isinstance(surface, dict) or not surface or any(not re.fullmatch(r"[0-9a-f]{64}", value) for value in surface.values()):
                fail("published API source surface inventory missing")
        removed = []
        for diagnostic in row["diagnostics"]:
            match = re.match(r"FS\.GG\.Wasm\.Browser\.(\w+)\.\1\(", diagnostic.get("member", ""))
            if set(diagnostic) != {"code", "member"} or diagnostic["code"] != "CP0002" or match is None:
                fail("unexpected public API diagnostic")
            removed.append(match.group(1))
        if sorted(removed) != expected["removedConstructorTypes"]:
            fail("declared public constructor migration mismatch")


def prepare(args: argparse.Namespace) -> None:
    version = args.version
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        fail("release version must be stable SemVer")
    custody = Path(args.custody)
    files = expected_files(version)
    packages = [inspect_package(custody / files[identity], identity, version, args.source) for identity in ROSTER]
    sdk = inspect_sdk(custody / files["sdk"], version)
    data = {
        "schema": SCHEMA,
        "version": version,
        "tag": f"wasm/v{version}",
        "source": args.source,
        "tree": args.tree,
        "roster": list(ROSTER),
        "workflow": ".github/workflows/wasm-shared.yml",
        "tools": TOOLS,
        "artifacts": packages + [sdk],
        "firstStableBaseline": {"version": "0.1.1", "publishedApiCompatBaseline": "0.1.1"},
        "publishedApiComparison": load_manifest(Path(args.api_comparison)),
    }
    destination = custody / "release-manifest.json"
    destination.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    verify_manifest(destination, custody, args.source)
    print(f"wasm-release-manifest: version={version} source={args.source} artifacts=3")


def compare_zip(original: Path, served: Path, allow_signature: bool) -> dict[str, object]:
    def payload(path: Path, exclude_signature: bool) -> dict[str, str]:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
            safe_members(names, path.name)
            return {name: hashlib.sha256(archive.read(name)).hexdigest() for name in names if not (exclude_signature and name == ".signature.p7s")}
    expected = payload(original, False)
    if ".signature.p7s" in expected:
        fail("retained original unexpectedly contains a signing envelope")
    if expected != payload(served, allow_signature):
        fail(f"served package payload differs: {served.name}")
    if not allow_signature and digest(original) != digest(served):
        fail(f"served package archive differs from retained original: {served.name}")
    return {"originalSha256": digest(original), "servedSha256": digest(served), "payloadMatch": True}


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    make = sub.add_parser("prepare")
    make.add_argument("--custody", required=True)
    make.add_argument("--version", required=True)
    make.add_argument("--source", required=True)
    make.add_argument("--tree", required=True)
    make.add_argument("--api-comparison", required=True)
    check = sub.add_parser("verify")
    check.add_argument("--custody", required=True)
    check.add_argument("--manifest", required=True)
    check.add_argument("--source")
    identity = sub.add_parser("verify-identity")
    identity.add_argument("--manifest", required=True)
    identity.add_argument("--source")
    compare = sub.add_parser("compare-package")
    compare.add_argument("--original", required=True)
    compare.add_argument("--served", required=True)
    compare.add_argument("--allow-nuget-signature", action="store_true")
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args)
    elif args.command == "verify":
        data = verify_manifest(Path(args.manifest), Path(args.custody), args.source)
        print(f"wasm-release-custody: version={data['version']} artifacts=3 verified=true")
    elif args.command == "verify-identity":
        data = load_manifest(Path(args.manifest))
        verify_identity(data, args.source)
        print(f"wasm-release-identity: version={data['version']} source={data['source']} verified=true")
    else:
        print(json.dumps(compare_zip(Path(args.original), Path(args.served), args.allow_nuget_signature), sort_keys=True))


if __name__ == "__main__":
    main()
