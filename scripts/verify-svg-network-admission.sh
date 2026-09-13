#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
model="$root/readiness/svg-network-01-1/quint/networkAdmission.qnt"
quint typecheck "$model"
quint test --main=networkAdmissionTest "$model"
quint run --main=networkAdmission --invariant=networkAdmissionSafe --max-steps=30 --max-samples=1000 "$model"
dotnet test "$root/tests/Game.Core.Tests/Game.Core.Tests.fsproj" -c Release --no-restore
bash "$root/scripts/test-fable-package-consumer.sh" --all
