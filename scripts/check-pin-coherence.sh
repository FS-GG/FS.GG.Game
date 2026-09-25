#!/usr/bin/env bash
# The CI and release-train pin gate. Keep this native entry point for callers.
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$SCRIPT_DIR/lib/pin_coherence.py" "${1:-Directory.Packages.local.props}"
