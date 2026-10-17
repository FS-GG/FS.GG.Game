#!/usr/bin/env bash
# Explicit hosted artifact preparation. This never qualifies or publishes packages.
set -euo pipefail
umask 077
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output="${WASM_TRACE_OUTPUT_ROOT:?requires fresh external WASM_TRACE_OUTPUT_ROOT}"
[[ $# == 0 || ( $# == 1 && "$1" == --preflight ) ]] || { echo "unsupported trace preparation argument" >&2; exit 2; }
python3 "$repo/scripts/prepare-wasm-traces.py" prepare --root "$repo" --output "$output"
if [[ "${1:-}" == --preflight ]]; then
  exit 0
fi

# Tool identity probes run only after source/dispatch/clean-checkout admission.
# Their finite setup deadline is independent of, and precedes, generation.
status=0
versions='{}'
node_version=''
quint_version=''
python_version=''
node_version="$(timeout --kill-after=2s 10s node --version)" || status=$?
if (( status == 0 )); then quint_version="$(timeout --kill-after=2s 10s quint --version)" || status=$?; fi
if (( status == 0 )); then python_version="$(timeout --kill-after=2s 10s python3 --version)" || status=$?; fi
if (( status == 0 )); then
  if [[ "$node_version" != v26.* || "$quint_version" != 0.32.0 || "${NODE_OPTIONS:-}" != --max-old-space-size=2048 ]]; then status=2; fi
fi
if (( status == 0 )); then timeout --help >/dev/null || status=$?; fi
if (( status == 0 )); then prlimit --help >/dev/null || status=$?; fi
versions="$(python3 - "$node_version" "$quint_version" "$python_version" <<'PYVERSIONS'
import json,sys
print(json.dumps(dict(zip(('node','quint','python'),sys.argv[1:]))))
PYVERSIONS
)"
if (( status != 0 )); then
  printf '%s\n' "tool version, NODE_OPTIONS or timeout/prlimit prerequisite refused (exit $status)" > "$output/work/generation.log"
fi

if (( status == 0 )); then
  export QUINT
  QUINT="$(command -v quint)"
  export TMPDIR="$output/work"
  # One original 20-minute process-group deadline for BOTH sequential generators.
  # V8 old-space and RLIMIT_FSIZE are not aggregate RSS or filesystem quotas.
  set +e
  timeout --kill-after=30s 20m prlimit --fsize=67108864:67108864 --core=0:0 \
    bash --noprofile --norc -c '
      set -euo pipefail
      cd "$1"
      run_generator() {
        local label=$1 source=$2 started ended code=0 metadata_code=0
        started=$(date +%s)
        python3 "$source" || code=$?
        ended=$(date +%s)
        printf "{\"generator\":\"%s\",\"startedUnix\":%s,\"finishedUnix\":%s,\"exitCode\":%s}\n" "$label" "$started" "$ended" "$code" >> "$TMPDIR/stages.jsonl" || metadata_code=$?
        if (( code != 0 )); then return "$code"; fi
        return "$metadata_code"
      }
      run_generator default tests/Wasm.Lifecycle.Correspondence/regenerate-event-traces.py
      run_generator compatible tests/Wasm.Supervisor.Compatibility/generate-compatible-traces.py
    ' wasm-trace-generation "$repo" > "$output/work/generation.log" 2>&1
  status=$?
  set -e
fi
cleanup=generator-process-returned
if (( status == 124 || status == 137 )); then
  cleanup=unknown-timeout-or-signal
fi
# Collection has its own finite remainder and cannot replace the first failure.
collection_status=0
timeout --kill-after=5s 120s python3 "$repo/scripts/prepare-wasm-traces.py" collect \
  --root "$repo" --output "$output" --status "$status" --versions "$versions" --cleanup "$cleanup" || collection_status=$?
if (( status != 0 )); then exit "$status"; fi
exit "$collection_status"
