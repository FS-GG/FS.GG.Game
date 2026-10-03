#!/usr/bin/env python3
"""Static acquisition controls. F# eligibility execution remains a separately granted gate."""
import importlib.util
import pathlib
import sys
sys.dont_write_bytecode = True

ROOT = pathlib.Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("transport", ROOT / "scripts/wasm-release/bind-canonical-qualification.py")
transport = importlib.util.module_from_spec(spec)
spec.loader.exec_module(transport)
protected = "b" * 40
source = "a" * 40
qualifier = "c" * 40
paths = ["eng/wasm-shared/lifecycle.qnt", "tests/Wasm.Lifecycle.Correspondence/Correspondence.fs", "tests/Wasm.Lifecycle.Correspondence/proof.json", "tests/Wasm.Lifecycle.Correspondence/Traces/sc2_0.itf.json"]
audit = {"protectedSource": protected, "model": {"path": paths[0]}, "qualifierInputs": [{"path": paths[1]}], "proofInputs": [{"path": paths[2]}], "originalTraces": [{"path": paths[3]}]}
transport.git_bytes = lambda revision, path: (revision + ":" + path).encode()
transport.git_text = lambda *args: qualifier if args[0] == "rev-parse" else "\n".join(paths)
transport.linked = lambda ancestor, descendant: True
receipts = {"manifestSha256": "d" * 64, "sdkSha256": "e" * 64, "packages": [{"name": "package", "sha256": "f" * 64}]}
observed = transport.acquire_observation(audit, b"committed audited bytes", "1" * 64, source, qualifier, "0.2.0", [paths[0], "src/Wasm.Browser"], receipts)
assert observed["callerHead"] == observed["qualifierHead"] == qualifier
assert observed["auditSha256"] == transport.sha(b"committed audited bytes")
assert observed["expectedAuditSha256"] == "1" * 64
assert observed["publishedModelSha256"] == transport.sha((source + ":" + paths[0]).encode())
assert observed["model"]["previousSha256"] == transport.sha((protected + ":" + paths[0]).encode())
assert observed["model"]["qualifierSha256"] == transport.sha((qualifier + ":" + paths[0]).encode())
assert observed["packages"] == receipts["packages"]
assert [row["path"] for row in observed["inputs"]] == paths
assert observed["changedPaths"] == observed["frozenChangedPaths"] == paths
# Acquisition must preserve adverse evidence for the F# owner, never filter it into acceptance.
transport.git_text = lambda *args: qualifier if args[0] == "rev-parse" else "src/Wasm.Browser/unreviewed.fs"
transport.linked = lambda ancestor, descendant: False
adverse = transport.acquire_observation(audit, b"substituted", "1" * 64, source, qualifier, "0.2.0", paths, receipts)
assert adverse["frozenChangedPaths"] == ["src/Wasm.Browser/unreviewed.fs"]
assert not adverse["producerLinked"] and not adverse["protectedLinked"]
assert adverse["auditSha256"] != adverse["expectedAuditSha256"]
text = (ROOT / "scripts/wasm-release/bind-installed-release.py").read_text()
assert '"eng/wasm-shared/version.props", "eng/wasm-shared/lifecycle.qnt"' in text
assert 'transport.bind(source, qualifier_source, version, FROZEN_INPUTS, release_binding)' in text
assert 'validate_consumer_audit(audit, source, version, changed, producer, qualifier)' in text
print("PASS_STATIC_TRANSPORT_CONTROLS; actual F# eligibility and installed proof remain pending; no CLR launched")
