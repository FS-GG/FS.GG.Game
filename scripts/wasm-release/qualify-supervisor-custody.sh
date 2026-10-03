#!/usr/bin/env bash
# Cold official tools and immutable baseline, then actual selected qualification.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export DOTNET_PROCESSOR_COUNT=1 MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false
bootstrap() {
  local q=$1
  case "$q/" in "$repo/"*) echo 'supervisor bootstrap must be outside source' >&2; exit 2;; esac
  [[ ! -e "$q" ]] || { echo 'supervisor bootstrap requires a fresh root' >&2; exit 2; }
  [[ "$(dotnet --version)" == 10.0.401 ]]
  mkdir -m 700 -p "$q/feed" "$q/packages" "$q/official-core"
  python3 - "$q" <<'PY'
import hashlib,json,pathlib,shutil,subprocess,sys,urllib.request,xml.etree.ElementTree as ET,zipfile
q=pathlib.Path(sys.argv[1]);sha=lambda b:hashlib.sha256(b).hexdigest();rows=[]
def download(url,path,digest):
 with urllib.request.urlopen(url) as response:body=response.read()
 assert sha(body)==digest,'official archive byte mismatch: '+path.name
 path.write_bytes(body);return body
for name,digest in [('contracts','b5bb9d0217dc31de9649b7c952db514bab5c99d32b30e6480da1f313d446fba4'),('browser','40d11feebb07b01c0846b1b9716db10badc5dfbce1a450eb5319ab89b8f041e6')]:
 ident='fs.gg.wasm.'+name;path=q/'feed'/f'{ident}.0.2.0.nupkg'
 download(f'https://api.nuget.org/v3-flatcontainer/{ident}/0.2.0/{path.name}',path,digest)
sdk=pathlib.Path(subprocess.check_output(['dotnet','--list-sdks'],text=True).split('10.0.401 [',1)[1].split(']',1)[0])/'10.0.401'
shutil.copytree(sdk/'FSharp',q/'compiler')
for version,archiveDigest,entries in [
 ('10.1.401','cf1f4e69fc2d1351af9fa9923ac90ed466088730a00867cbe0eb1b3b2c68963e',{'netstandard2.0':'7516a966abc789eab916e95d429bf3a49e996257069d97f7f3eb5d47373fa72b','netstandard2.1':'39b0b7f06c11bedd93f94b6f101fffd645f1c4437a5a6d3ee79259f855aeb11a'}),
 ('10.1.302','f4eda1b2efb28b38a5526b0b678e889434aa71113a4c2fa8660b62ca75cc7dfc',{'netstandard2.0':'430a20ba7c8b7c226e6bcf9a8e854760a799f5da19000c573148144dcce75742','netstandard2.1':'517c1301e8cb5086fbe63a3c8879049933eb998b1569f754b1e653314ad51232'}),
 ('10.1.203','b8ecba5c6b8713aad28c8f3c2b98a633aeeffa877ae389ea429b99650202fcd5',{'netstandard2.1':'4a626d62bb15815b6404567aa1bd37d9f2208e8e3c99c3c346d0efcfca72af6c'})]:
 archive=q/'official-core'/f'fsharp.core.{version}.nupkg'
 download(f'https://api.nuget.org/v3-flatcontainer/fsharp.core/{version}/{archive.name}',archive,archiveDigest)
 with zipfile.ZipFile(archive) as z:
  for tfm,digest in entries.items():
   entry=f'lib/{tfm}/FSharp.Core.dll';body=z.read(entry);assert sha(body)==digest,'official TFM DLL byte mismatch'
   out=q/'official-core'/f'FSharp.Core.{version}.{tfm}.dll';out.write_bytes(body)
   rows.append(dict(version=version,archiveSha256=archiveDigest,archiveEntry=entry,dllSha256=digest,path=str(out)))
   if version=='10.1.401' and tfm=='netstandard2.0':(q/'compiler/FSharp.Core.dll').write_bytes(body)
(q/'official-core/receipt.json').write_text(json.dumps(rows,indent=2)+'\n')
root=ET.Element('configuration');sources=ET.SubElement(root,'packageSources');ET.SubElement(sources,'clear');ET.SubElement(sources,'add',key='baseline',value=str(q/'feed'));ET.SubElement(sources,'add',key='official',value='https://api.nuget.org/v3/index.json');mapping=ET.SubElement(root,'packageSourceMapping');ET.SubElement(ET.SubElement(mapping,'packageSource',key='baseline'),'package',pattern='FS.GG.Wasm.*');ET.SubElement(ET.SubElement(mapping,'packageSource',key='official'),'package',pattern='*');ET.ElementTree(root).write(q/'NuGet.Config',encoding='unicode')
(q/'environment').write_text('WASM_SUPERVISOR_BASELINE_ROOT='+str(q)+'\nDotnetFscCompilerPath='+str(q/'compiler/fsc.dll')+'\nWASM_FSC_COMPILER_PATH='+str(q/'compiler/fsc.dll')+'\nDOTNET_PROCESSOR_COUNT=1\nMSBUILDDISABLENODEREUSE=1\nUseSharedCompilation=false\n')
PY
  NUGET_PACKAGES="$q/packages" dotnet tool install fable --version 5.18.0 --tool-path "$q/tools" --configfile "$q/NuGet.Config" > "$q/tool-install.log" 2>&1
  cmp "$q/tools/.store/fable/5.18.0/fable/5.18.0/tools/net10.0/any/FSharp.Core.dll" "$q/official-core/FSharp.Core.10.1.203.netstandard2.1.dll"
  cmp "$q/compiler/FSharp.Core.dll" "$q/official-core/FSharp.Core.10.1.401.netstandard2.0.dll"
  "$repo/sdk/wasm/toolchains/acquire-wasi-sdk.sh" "$q/wasi" > "$q/wasi-acquire.log"
  printf 'WASM_WASI_SDK_ROOT=%s\n' "$q/wasi/wasi-sdk-34.0-x86_64-linux" >> "$q/environment"
  printf 'supervisor-bootstrap: official baseline0.2.0 Fable5.18.0 Core401-TFM20+21 Core302-TFM20+21 Core203-TFM21 pinned; commonSDK=unchanged\n'
}
case "${1:-}" in
  --bootstrap) bootstrap "$(realpath -m "${2:?requires new private baseline root}")";;
  --custody)
    custody="$(realpath -e "${2:?requires exact coherent custody}")"
    output="$(realpath -m "${3:?requires new private output}")"
    q="${WASM_SUPERVISOR_BASELINE_ROOT:-${output}-baseline}"
    [[ -d "$q" ]] || bootstrap "$q"
    version="$(sed -n 's:.*<WasmSharedVersion>\([^<]*\)</WasmSharedVersion>.*:\1:p' "$repo/eng/wasm-shared/version.props")"
    python3 "$repo/scripts/wasm-release/release_manifest.py" verify --custody "$custody" --manifest "$custody/release-manifest.json" --source "$(git -C "$repo" rev-parse HEAD)"
    python3 - "$custody" <<'PYCONFIG'
import pathlib,sys,xml.etree.ElementTree as ET
custody=pathlib.Path(sys.argv[1]);root=ET.Element('configuration');sources=ET.SubElement(root,'packageSources');ET.SubElement(sources,'clear');ET.SubElement(sources,'add',key='candidate',value=str(custody));ET.SubElement(sources,'add',key='official',value='https://api.nuget.org/v3/index.json');mapping=ET.SubElement(root,'packageSourceMapping');ET.SubElement(ET.SubElement(mapping,'packageSource',key='candidate'),'package',pattern='FS.GG.Wasm.*');ET.SubElement(ET.SubElement(mapping,'packageSource',key='official'),'package',pattern='*');ET.ElementTree(root).write(custody/'NuGet.Config',encoding='unicode')
PYCONFIG
    export WASM_BASELINE_QUALIFICATION_ROOT="$q" WASM_CANDIDATE_CUSTODY="$custody" WASM_CANDIDATE_VERSION="$version" WASM_FSC_COMPILER_PATH="$q/compiler/fsc.dll"
    "$repo/scripts/verify-wasm-supervisor.sh" "$output"
    python3 - "$repo" "$q" <<'PYJOIN'
import hashlib,json,pathlib,sys
repo,q=map(pathlib.Path,sys.argv[1:]);rows=[]
for path,expected in [
 (q/'packages/fsharp.core/10.1.302/lib/netstandard2.0/FSharp.Core.dll',q/'official-core/FSharp.Core.10.1.302.netstandard2.0.dll'),
 (q/'packages/fsharp.core/10.1.302/lib/netstandard2.1/FSharp.Core.dll',q/'official-core/FSharp.Core.10.1.302.netstandard2.1.dll'),
 (repo/'tests/Wasm.Supervisor.Compatibility/bin/Debug/net10.0/FSharp.Core.dll',q/'official-core/FSharp.Core.10.1.302.netstandard2.1.dll'),
 (repo/'tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0/FSharp.Core.dll',q/'official-core/FSharp.Core.10.1.302.netstandard2.1.dll')]:
 assert path.read_bytes()==expected.read_bytes(),'actual compiler reference/runtime Core DLL differs from pinned official TFM'
 rows.append(dict(path=str(path),officialPath=str(expected),dllSha256=hashlib.sha256(path.read_bytes()).hexdigest(),byteIdentical=True))
(q/'official-core/executed-runtime-joins.json').write_text(json.dumps(rows,indent=2)+'\n')
print('supervisor-core-joins: actual reference/runtime DLLs byte-identical to official TFM archives')
PYJOIN
    export WASM_WASI_SDK_ROOT="${WASM_WASI_SDK_ROOT:-$q/wasi/wasi-sdk-34.0-x86_64-linux}"
    npm ci --ignore-scripts --prefix "$repo/tests/Wasm.Supervisor.Compatibility/browser"
    (cd "$repo/tests/Wasm.Supervisor.Compatibility/browser" && npx playwright install chromium)
    "$repo/scripts/verify-wasm-supervisor-browser.sh" "$output" "${output}-browser"
    [[ -z "$(git -C "$repo" status --porcelain)" ]] || { echo 'supervisor browser qualification dirtied source' >&2; exit 2; }
    ;;
  *) echo 'usage: qualify-supervisor-custody.sh --bootstrap NEW_ROOT | --custody CUSTODY NEW_OUTPUT' >&2; exit 2;;
esac
