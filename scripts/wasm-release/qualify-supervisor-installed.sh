#!/usr/bin/env bash
# Genuine installed selected acceptance: no local candidate feed or release pack.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
[[ "${1:-}" == --feed-only && $# == 2 ]] || { echo 'usage: qualify-supervisor-installed.sh --feed-only NEW_PRIVATE_OUTPUT' >&2; exit 2; }
[[ -z "${WASM_CANDIDATE_CUSTODY:-}" && -z "${WASM_RELEASE_CUSTODY:-}" && -z "${WASM_SOURCE3_FEED:-}" ]] || { echo 'feed-only refuses local custody/source injection' >&2; exit 2; }
output="$(realpath -m "$2")"
case "$output/" in "$repo/"*) echo 'installed output must be outside source' >&2; exit 2;; esac
[[ ! -e "$output" ]] || exit 2
feed="${WASM_PACKAGE_FEED:?}"
[[ "$feed" == https://api.nuget.org/v3/index.json || "$feed" == https://nuget.pkg.github.com/FS-GG/index.json ]] || { echo 'feed-only requires a literal native HTTPS feed' >&2; exit 2; }
receipts="${RUNNER_TEMP:?}/wasm-installed-receipts"
[[ -f "$receipts/release-manifest.json" && -f "$receipts/promotion-binding.json" ]] || { echo 'requires genuine binder-downloaded immutable public receipts' >&2; exit 2; }
q="${output}-baseline"
"$repo/scripts/wasm-release/qualify-supervisor-custody.sh" --bootstrap "$q"
config="${output}-NuGet.Config"
python3 - "$config" "$feed" <<'PY'
import sys,xml.etree.ElementTree as ET
root=ET.Element('configuration');sources=ET.SubElement(root,'packageSources');ET.SubElement(sources,'clear');ET.SubElement(sources,'add',key='wasm',value=sys.argv[2]);mapping=ET.SubElement(root,'packageSourceMapping');wasm=ET.SubElement(mapping,'packageSource',key='wasm');ET.SubElement(wasm,'package',pattern='FS.GG.Wasm.*')
if sys.argv[2]=='https://api.nuget.org/v3/index.json':deps=wasm
else:
 ET.SubElement(sources,'add',key='dependencies',value='https://api.nuget.org/v3/index.json');deps=ET.SubElement(mapping,'packageSource',key='dependencies')
ET.SubElement(deps,'package',pattern='FSharp.Core');ET.ElementTree(root).write(sys.argv[1],encoding='unicode')
PY
python3 "$repo/scripts/wasm-release/promotion.py" check-feed --output "$config" --feed "$feed"
export WASM_BASELINE_QUALIFICATION_ROOT="$q" WASM_INSTALLED_CONFIG="$config" WASM_CANDIDATE_VERSION=0.3.0
export WASM_FSC_COMPILER_PATH="$q/compiler/fsc.dll" DOTNET_HOST_PATH=/usr/share/dotnet/dotnet
export NUGET_HTTP_CACHE_PATH="${output}-http-cache"
"$repo/scripts/verify-wasm-supervisor.sh" --feed-only "$output"
python3 "$repo/scripts/wasm-release/promotion.py" check-installed --manifest "$receipts/promotion-binding.json" --cache "$output/packages" --feed "$feed" --lock "$output/installed.packages.lock.json" > "$output/installed-member-receipt.log"
python3 - "$q" "$output" "$repo" <<'PYJOIN'
import hashlib,json,pathlib,sys
q,output,repo=map(pathlib.Path,sys.argv[1:]);rows=[]
for actual,official in [
 (output/'packages/fsharp.core/10.1.302/lib/netstandard2.0/FSharp.Core.dll',q/'official-core/FSharp.Core.10.1.302.netstandard2.0.dll'),
 (output/'packages/fsharp.core/10.1.302/lib/netstandard2.1/FSharp.Core.dll',q/'official-core/FSharp.Core.10.1.302.netstandard2.1.dll'),
 (repo/'tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0/FSharp.Core.dll',q/'official-core/FSharp.Core.10.1.302.netstandard2.1.dll')]:
 assert actual.read_bytes()==official.read_bytes(),'installed runtime/reference differs from exact official TFM DLL'
 rows.append(dict(path=str(actual),officialPath=str(official),sha256=hashlib.sha256(actual.read_bytes()).hexdigest(),byteIdentical=True))
(output/'official-core-joins.json').write_text(json.dumps(rows,indent=2)+'\n')
PYJOIN
# The browser adapter accepts ONLY this actually restored cache path. It does not
# extract any preparation/custody archive to substitute an installed worker.
export WASM_INSTALLED_BROWSER_ARCHIVE="$output/packages/fs.gg.wasm.browser/0.3.0/fs.gg.wasm.browser.0.3.0.nupkg"
export WASM_WASI_SDK_ROOT="$q/wasi/wasi-sdk-34.0-x86_64-linux"
(cd "$repo/tests/Wasm.Supervisor.Compatibility/browser" && npm ci --ignore-scripts && npx playwright install chromium)
"$repo/scripts/verify-wasm-supervisor-browser.sh" --feed-only "$output" "${output}-browser"
cp "$config" "$receipts/release-manifest.json" "$receipts/promotion-binding.json" "$output/"
printf 'wasm-installed-supervisor: feed=%s default47+selected13 dotnet+fable both-facades browsers=8 locked-cache-worker=verified local-fallback=none\n' "$feed"
