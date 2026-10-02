#!/usr/bin/env python3
"""Generate F# correspondence fixtures directly from canonical Quint ITF traces."""

from __future__ import annotations

import hashlib
import json
import argparse
from pathlib import Path


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
    document["#meta"].pop("description", None)
    document["#meta"].pop("timestamp", None)
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


def trace(path: Path) -> str:
    raw = path.read_bytes()
    document = json.loads(raw)
    steps = []
    for item in document["states"]:
        model = item["state"]
        steps.append(
            "{ State = (%s : HostProjection); Effects = (%s : EffectProjection list) }"
            % (
                state(model),
                fs_list([effect(value) for value in model["effects"]]),
            )
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
