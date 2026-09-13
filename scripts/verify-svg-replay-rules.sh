#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODEL="$ROOT/readiness/svg-replay-01-3/quint/energyRules.qnt"

python3 "$ROOT/scripts/generate-svg-replay-rule-evidence.py"
quint typecheck "$MODEL"
quint test --main=energyRulesTest "$MODEL"
quint run --main=energyRules --invariant=energyRulesSafe --max-steps=30 --max-samples=1000 "$MODEL"
dotnet test "$ROOT/tests/Game.Core.Tests/Game.Core.Tests.fsproj" -c Release --no-restore
