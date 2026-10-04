#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
custody="$(realpath -e "${1:?usage: stage-assets.sh <custody>}")"
# Exact release/asset IDs are checked by the shared resolver; no tag-based CLI lookup.
exec python3 -B "$repo/scripts/wasm-release/promotion.py" asset-stage --reviewed "${RUNNER_TEMP:?}/reviewed.json" --custody "$custody" --output "$RUNNER_TEMP/readback/assets"
