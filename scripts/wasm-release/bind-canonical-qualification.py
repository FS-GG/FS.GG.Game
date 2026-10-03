#!/usr/bin/env python3
"""Acquire immutable inputs; F# owns the exact model amendment eligibility decision."""
import hashlib
import json
import os
import pathlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
OWNER = ROOT / "scripts/wasm-release/decide-canonical-qualification.fsx"


def git_bytes(revision, path):
    result = subprocess.run(["git", "-C", str(ROOT), "show", revision + ":" + path], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        # Absence is evidence only when the path genuinely does not exist at revision.
        listed = subprocess.check_output(["git", "-C", str(ROOT), "ls-tree", revision, "--", path])
        if listed:
            raise RuntimeError("could not acquire tracked qualifier input: " + path)
        return None
    return result.stdout


def sha(data):
    return "ABSENT" if data is None else hashlib.sha256(data).hexdigest()


def git_text(*args):
    return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True).strip()


def linked(ancestor, descendant):
    return subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", ancestor, descendant], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0


def acquire_observation(audit, audit_bytes, expected_audit_sha, source, qualifier, version, frozen_inputs, release_binding):
    protected = audit["protectedSource"]
    paths = [audit["model"]["path"]] + [row["path"] for kind in ("qualifierInputs", "proofInputs", "originalTraces") for row in audit[kind]]
    # Path rows are acquisition requests, never an authorization allowlist. F# validates their exact census.
    rows = [{"path": path, "previousSha256": sha(git_bytes(protected, path)), "qualifierSha256": sha(git_bytes(qualifier, path))} for path in paths]
    return {
        "publishedSource": source, "protectedSource": protected, "version": version,
        "callerHead": git_text("rev-parse", "HEAD"), "qualifierHead": qualifier,
        "producerLinked": linked(source, qualifier), "protectedLinked": linked(protected, qualifier),
        "auditSha256": sha(audit_bytes), "expectedAuditSha256": expected_audit_sha,
        "publishedModelSha256": sha(git_bytes(source, "eng/wasm-shared/lifecycle.qnt")),
        "releaseManifestSha256": release_binding["manifestSha256"], "sdkSha256": release_binding["sdkSha256"],
        "packages": release_binding["packages"], "model": rows[0], "inputs": rows,
        "changedPaths": git_text("diff", "--name-only", protected, qualifier).splitlines(),
        "frozenChangedPaths": git_text("diff", "--name-only", source, qualifier, "--", *frozen_inputs).splitlines(),
    }


def bind(source, qualifier, version, frozen_inputs, release_binding):
    audit_path = pathlib.Path(os.environ["CANONICAL_QUALIFICATION_AUDIT"])
    # Bind committed bytes, not a mutable worktree audit. The exact audit digest is an independent caller receipt.
    relative = audit_path.resolve().relative_to(ROOT).as_posix()
    audit_bytes = git_bytes(qualifier, relative)
    if audit_bytes is None or audit_path.read_bytes() != audit_bytes:
        raise RuntimeError("amendment must be exact committed qualifier input")
    audit = json.loads(audit_bytes)
    observed = acquire_observation(audit, audit_bytes, os.environ["CANONICAL_QUALIFICATION_AUDIT_SHA256"], source, qualifier, version, frozen_inputs, release_binding)
    # F# administrative policy itself must come from this exact immutable qualifier HEAD.
    for path in ("scripts/wasm-release/CanonicalQualificationPolicy.fs", "scripts/wasm-release/decide-canonical-qualification.fsx"):
        if (ROOT / path).read_bytes() != git_bytes(qualifier, path):
            raise RuntimeError("F# owner differs from committed qualifier input")
    with tempfile.TemporaryDirectory(prefix="wasm-model-amendment.") as raw:
        input_path = pathlib.Path(raw) / "observation.json"
        input_path.write_text(json.dumps(observed))
        output = subprocess.check_output(["dotnet", "fsi", "--exec", str(OWNER), str(audit_path), str(input_path)], text=True)
    decision = json.loads(output.strip().splitlines()[-1])
    # This is a process-protocol assertion, not a second implementation of administrative eligibility.
    if decision.get("Accepted") is not True:
        raise RuntimeError("F# owner refused canonical qualification amendment")
    return {"canonicalInputs": "exact-producer-approved-model-only-amendment", "decision": decision,
            "auditSha256": sha(audit_bytes), "publishedModelSha256": observed["publishedModelSha256"],
            "qualifierModelSha256": observed["model"]["qualifierSha256"]}
