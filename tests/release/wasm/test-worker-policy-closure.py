#!/usr/bin/env python3
"""Check the actual generated module import closure used by packaged Workers."""
import argparse
import pathlib
import re
import shutil
import tempfile


def validate(root):
    root = root.resolve()
    entry = root / "WorkerEntry.js"
    assert "export function WorkerEntry_createWireRuntime(" in entry.read_text(), "Worker entry export is absent"
    seen = set()
    pending = [entry]
    while pending:
        path = pending.pop()
        if path in seen:
            continue
        assert path.is_relative_to(root) and path.is_file(), f"generated Worker dependency is outside/missing: {path}"
        seen.add(path)
        for imported in re.findall(r'\b(?:import|export)\b[^;\n]*?\bfrom\s+["\']([^"\']+)["\']', path.read_text()):
            assert imported.startswith(("./", "../")), f"unpackaged external Worker dependency: {imported}"
            pending.append((path.parent / imported).resolve())
    assert {"WorkerEntry.js", "Invocation.js", "Admission.js", "RuntimeProtocol.js"}.issubset({path.name for path in seen})
    assert any(path.name == "Contracts.js" for path in seen), "Worker lacks authoritative contracts"
    return len(seen)


def negative_controls(root):
    with tempfile.TemporaryDirectory(prefix="wasm-worker-closure-control.") as raw:
        copied = pathlib.Path(raw) / "policy"
        shutil.copytree(root, copied)
        invocation = copied / "Invocation.js"
        content = invocation.read_bytes()
        invocation.unlink()
        try:
            validate(copied)
        except AssertionError:
            pass
        else:
            raise SystemExit("missing generated Invocation dependency was accepted")
        invocation.write_bytes(content)
        entry = copied / "WorkerEntry.js"
        entry.write_text(entry.read_text().replace('"./Invocation.js"', '"https://example.invalid/Invocation.js"'))
        try:
            validate(copied)
        except AssertionError:
            pass
        else:
            raise SystemExit("external generated policy dependency was accepted")



if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--generated", type=pathlib.Path, required=True)
    args = parser.parse_args()
    count = validate(args.generated)
    negative_controls(args.generated)
    print(f"wasm-worker-policy-closure: generated-imports={count} external-dependencies=none policy=fsharp")
