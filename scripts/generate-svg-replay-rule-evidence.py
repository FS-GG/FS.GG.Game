#!/usr/bin/env python3
"""Generate/check the C16 neutral-rule formal evidence identity."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "readiness/svg-replay-01-3/quint/energyRules.qnt"
OUTPUT = ROOT / "readiness/svg-replay-01-3/rule-evidence.v1.json"


def document() -> dict:
    digest = hashlib.sha256(MODEL.read_bytes()).hexdigest()
    return {
        "schema": "fsgg.svg-replay.rule-evidence/v1",
        "catalog": {
            "id": "energy-door-rules/1",
            "rules": [
                {"id": "energy.available", "version": 1, "dependsOn": []},
                {"id": "door.enter", "version": 1, "dependsOn": ["energy.available"]},
            ],
        },
        "model": {"path": str(MODEL.relative_to(ROOT)), "sha256": digest},
        "tool": {"name": "quint", "version": "0.32.0"},
        "invariants": ["energyNeverNegative", "acceptedRevisionMonotonic", "doorRequiresSpentEnergy", "energyRulesSafe"],
        "implementationBinding": {
            "surface": "src/Game.Core/Rules.fsi",
            "implementation": "src/Game.Core/Rules.fs",
            "conformance": "tests/Game.Core.Tests/RuleCatalogTests.fs",
            "fableOperation": 11,
        },
        "authority": "Product RuleDefinition.Evaluate functions execute semantics; Quint and metadata provide scoped evidence and do not run the game.",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    rendered = json.dumps(document(), indent=2, sort_keys=True) + "\n"
    if args.write:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_text(rendered, encoding="utf-8")
        print(f"wrote {OUTPUT.relative_to(ROOT)}")
        return 0
    if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != rendered:
        print("rule evidence is stale; run scripts/generate-svg-replay-rule-evidence.py --write")
        return 1
    print("rule evidence is current")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
