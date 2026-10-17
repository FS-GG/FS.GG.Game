#!/usr/bin/env python3
"""Generate F# correspondence fixtures directly from canonical Quint ITF traces."""

from __future__ import annotations

import hashlib
import json
import argparse
import os
import subprocess
import sys
from pathlib import Path


def run_quint_schedule(command, schedule, trace_path):
    """Run the original call once; retain fresh failed evidence, never old fixtures."""
    trace_path.unlink(missing_ok=True)
    print('canonical Quint schedule: ' + schedule + ' argv=' + json.dumps(command), flush=True)
    try:
        subprocess.run(command, check=True)
    except subprocess.CalledProcessError as failure:
        location = os.environ.get('WASM_TRACE_DIAGNOSTIC_DIR')
        if location:
            try:
                directory = Path(location)
                record = {'schedule': schedule, 'argv': command, 'exitCode': failure.returncode,
                          'counterexample': None, 'gap': 'Quint emitted no fresh ITF file'}
                if trace_path.exists():
                    if trace_path.is_symlink() or not trace_path.is_file() or trace_path.stat().st_nlink != 1:
                        raise ValueError('failed counterexample is linked or nonregular')
                    value = hashlib.sha256()
                    size = 0
                    with trace_path.open('rb') as source, (directory / 'counterexample.itf').open('xb') as target:
                        for chunk in iter(lambda: source.read(1024 * 1024), b''):
                            value.update(chunk)
                            keep = max(0, min(len(chunk), 4 * 1024 * 1024 - size))
                            target.write(chunk[:keep])
                            size += len(chunk)
                    record['counterexample'] = {'originalBytes': size, 'originalSha256': value.hexdigest(),
                        'retainedBytes': min(size, 4 * 1024 * 1024), 'truncated': size > 4 * 1024 * 1024}
                    usable = False
                    if size <= 4 * 1024 * 1024:
                        try:
                            document = json.loads((directory / 'counterexample.itf').read_bytes())
                            usable = (isinstance(document, dict) and isinstance(document.get('#meta'), dict)
                                      and isinstance(document.get('vars'), list)
                                      and isinstance(document.get('states'), list) and len(document['states']) > 0)
                        except (ValueError, UnicodeError):
                            pass
                    record['counterexample']['usableItf'] = usable
                    record['gap'] = (None if usable else 'Fresh output is partial/non-ITF or exceeds retained budget; diagnostic bytes only')
                data = (json.dumps(record, sort_keys=True) + '\n').encode()
                if len(data) > 8192:
                    raise ValueError('failed schedule metadata exceeds 8 KiB')
                (directory / 'failure-schedule.json').write_bytes(data)
            except (OSError, ValueError) as diagnostic_error:
                try:
                    print('failed schedule evidence collection failed: ' + str(diagnostic_error), file=sys.stderr)
                except OSError:
                    pass
        raise


ROOT = Path(__file__).resolve().parent
TRACES = ROOT / "Traces"
OUTPUT = ROOT / "GeneratedTraces.fs"


def bigint(value: object) -> int:
    return int(value["#bigint"])


def tag(value: object) -> str:
    return value["tag"]


def text(value: str) -> str:
    return json.dumps(value)


def worker(value: int) -> str:
    return "" if value == 0 else f"worker-{value}"


def request(value: dict) -> str:
    return (
        "{ Id = %dUL; Worker = %s; Operation = %s; Phase = %s; Correlation = %s; Generation = %dUL; DeadlineMilliseconds = %dL; "
        "Submission = %s; Bytes = %d }"
        % (
            bigint(value["id"]),
            text(worker(bigint(value["worker"]))),
            text(value["operation"]),
            text(value["phase"]),
            text("" if bigint(value["worker"]) == 0 else f"request-{bigint(value['generation'])}-{bigint(value['correlation'])}"),
            bigint(value["generation"]),
            bigint(value["deadline"]),
            text(tag(value["submission"])),
            bigint(value["bytes"]),
        )
    )


def effect(value: dict) -> str:
    return (
        "{ Kind = %s; Request = %dUL; Generation = %dUL; Worker = %s; Operation = %s; Phase = %s; Correlation = %s; Bytes = %d; Due = %dL }"
        % (
            text(value["kind"]),
            bigint(value["request"]),
            bigint(value["generation"]),
            text(worker(bigint(value["worker"]))),
            text(value["operation"]),
            text(value["phase"]),
            text("" if bigint(value["correlation"]) == 0 else f"request-{bigint(value['generation'])}-{bigint(value['correlation'])}"),
            bigint(value["bytes"]),
            bigint(value["due"]),
        )
    )


def fs_list(values: list[str]) -> str:
    return "[ " + "; ".join(values) + " ]" if values else "[]"


def normalize(path: Path) -> None:
    document = json.loads(path.read_text(encoding="utf-8"))
    # Backend declaration order does not change the complete named ITF state.
    document["vars"] = sorted(document["vars"])
    document["#meta"].pop("description", None)
    document["#meta"].pop("timestamp", None)
    if path.name.startswith("boundary-"):
        document["#meta"]["source"] = "eng/wasm-shared/event-boundary-qualification.qnt"
        document["#meta"]["canonicalSource"] = "eng/wasm-shared/lifecycle.qnt"
    elif path.name.startswith("expiry-"):
        document["#meta"]["source"] = "eng/wasm-shared/expiry-qualification.qnt"
        document["#meta"]["canonicalSource"] = "eng/wasm-shared/lifecycle.qnt"
    else:
        document["#meta"]["source"] = "eng/wasm-shared/lifecycle.qnt"
    path.write_text(
        json.dumps(document, sort_keys=True, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )


def state(value: dict) -> str:
    retiring = sorted(bigint(item) for item in value["retiringWorkers"]["#set"])
    freeze_token = bigint(value["freezeToken"])
    return "; ".join(
        [
            "{ Profile = %s" % text(tag(value["profile"])),
            "  Clock = %dL" % bigint(value["clock"]),
            "  NextRequest = %dUL" % bigint(value["nextRequest"]),
            "  NextGeneration = %dUL" % bigint(value["nextGeneration"]),
            "  ActiveWorker = %s" % text(worker(bigint(value["activeWorker"]))),
            "  ActiveGeneration = %dUL" % bigint(value["activeGeneration"]),
            "  CandidateWorker = %s" % text(worker(bigint(value["candidateWorker"]))),
            "  CandidateGeneration = %dUL" % bigint(value["candidateGeneration"]),
            "  CandidateTransaction = %s" % text("" if bigint(value["candidateTransaction"]) == 0 else str(bigint(value["candidateTransaction"]))),
            "  CandidateInitialized = %s" % str(value["candidateInitialized"]).lower(),
            "  CandidateReady = %s" % str(value["candidateReady"]).lower(),
            "  RetiringWorkers = %s" % fs_list([text(worker(item)) for item in retiring]),
            "  CompiledWorkers = %s" % fs_list(sorted(text(worker(bigint(item))) for item in value["compiledWorkers"]["#set"])),
            "  InitializedWorkers = %s" % fs_list(sorted(text(worker(bigint(item))) for item in value["initializedWorkers"]["#set"])),
            "  Controls = %s" % fs_list([request(item) for item in sorted(value["controls"], key=lambda row: worker(bigint(row["worker"])))]),
            "  Current = %s" % request(value["current"]),
            "  Ordinary = %s" % fs_list([request(item) for item in value["ordinary"]]),
            "  Ordered = %s" % fs_list([request(item) for item in value["ordered"]]),
            "  Snapshot = %s" % request(value["snapshot"]),
            "  Frozen = %s" % str(value["frozen"]).lower(),
            "  FreezeToken = %s" % text("" if freeze_token == 0 else str(freeze_token)),
            "  Disposed = %s" % str(value["disposed"]).lower(),
            "  Deliveries = %s" % fs_list([f"{bigint(item)}UL" for item in value["deliveries"]]),
            "  LastAction = %s }" % text(value["lastAction"]),
        ]
    )


def original_input(value):
    return "{ Name = %s; Request = (%s : RequestProjection); Worker = %s; Phase = %s; Observed = %dL }" % (text(value["name"]), request(value["request"]), text(worker(bigint(value["worker"]))), text(value["phase"]), bigint(value["observed"]))


def terminals(values):
    return fs_list(["{ Request = %dUL; Reason = %s; Outcome = %s; Phase = %s; Dispatched = %s }" % (bigint(e["request"]),text(e["reason"]),text(e["outcome"]),text(e["terminalPhase"]),str(e["dispatched"]).lower()) for e in values if e["reason"]])


def trace(path: Path) -> str:
    raw = path.read_bytes()
    document = json.loads(raw)
    steps = []
    for item in document["states"]:
        model = item["state"]
        connected = item["connection"]
        steps.append(
            "{ State = (%s : HostProjection); Effects = (%s : EffectProjection list); Input = %s; Terminals = %s; Connected = (%s : HostProjection); ConnectedEffects = (%s : EffectProjection list); ConnectedTerminals = %s; ConnectedCallbacks = %s; ConnectedBeforeEffects = %s }"
            % (state(model),fs_list([effect(value) for value in model["effects"]]),original_input(model["input"]),terminals(model["effects"]),
               state(connected["host"]),fs_list([effect(value) for value in connected["effects"]]),terminals(connected["effects"]),
               fs_list([original_input(value) for value in connected["callbacks"]]),fs_list(["("+state(value)+" : HostProjection)" for value in connected["beforeEffects"]]))
        )
    name = path.name.split("_", 1)[0]
    digest = hashlib.sha256(raw).hexdigest()
    return "{ Name = %s; SourceSha256 = %s; Steps =\n%s }" % (
        text(name),
        text(digest),
        fs_list(steps),
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--traces", type=Path, default=TRACES)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    paths = sorted(args.traces.glob("*.itf.json"))
    for path in paths:
        normalize(path)
    values = [trace(path) for path in paths]
    output = "\n".join(
        [
            "// Generated from canonical Quint ITF traces by generate-traces.py. Do not hand edit.",
            "module Wasm.Lifecycle.Correspondence.GeneratedTraces",
            "",
            "open FS.GG.Wasm.Browser",
            "open Wasm.Lifecycle.Correspondence.CorrespondenceTypes",
            "",
            "let traces: ModelTrace list =",
            "    " + fs_list(values).replace("\n", "\n    "),
            "",
        ]
    )
    args.output.write_text(output, encoding="utf-8")


if __name__ == "__main__":
    main()
