#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
workflow="$repo/.github/workflows/wasm-shared.yml"

python3 - "$workflow" "$repo/global.json" "$repo/.config/dotnet-tools.json" "$repo/.github/workflows/gate.yml" <<'PY'
import json
import pathlib
import sys

workflow = pathlib.Path(sys.argv[1])
text = workflow.read_text(encoding="utf-8")

required = [
    "permissions:\n  contents: read",
    "timeout-minutes: 35",
    'dotnet-version: "10.0.401"',
    'node-version: "26"',
    "QUINT_VERSION: 0.32.0",
    "RUST_VERSION: 1.90.0",
    "PLAYWRIGHT_VERSION: 1.63.0",
    "scripts/verify-wasm-contracts.sh",
    "scripts/verify-wasm-lifecycle.sh",
    "scripts/verify-wasm-package-consumer.sh",
    "scripts/wasm-release/prepare.sh",
    "tests/Wasm.Browser.Conformance/run.sh",
    "scripts/wasm-release/qualify-supervisor-custody.sh --bootstrap",
    "scripts/wasm-release/qualify-supervisor-custody.sh --custody",
]
ordered = [
    "Static workflow preflight",
    "Set up .NET",
    "Set up Node",
    "Prepare cold package and tool state",
    "Install pinned Quint",
    "Install pinned Rust",
    "Install pinned Playwright Chromium",
    "Prepare compatible supervisor qualification state",
    "Rebuild pinned WebAssembly fixtures",
    "Verify compatibility contracts",
    "Verify lifecycle, packages, and Fable consumers",
    "Run browser Worker conformance",
    "Verify SDK archive and fresh package consumer",
    "Qualify compatible supervisor source and installed custody",
]
def validate(candidate: str) -> None:
    missing = [value for value in required if value not in candidate]
    if missing:
        raise ValueError(f"missing required declarations: {missing}")
    import re
    jobs = candidate.split("\njobs:\n", 1)[1]
    if set(re.findall(r"^  ([a-z0-9-]+):$", jobs, re.M)) != {"qualify", "generate-traces"}:
        raise ValueError("expected one complete qualify job and one explicit preparation job")
    qualify, generation = jobs.split("\n  generate-traces:\n", 1)
    if "if: ${{ github.event_name != 'workflow_dispatch' || inputs.mode == 'qualify' }}" not in qualify:
        raise ValueError("normal qualification selection changed")
    if "if: ${{ github.event_name == 'workflow_dispatch' && inputs.mode == 'generate-traces' }}" not in generation:
        raise ValueError("generation requires explicit manual mode")
    for token in ("options: [qualify, generate-traces]", "default: qualify",
                  "inputs.mode == 'generate-traces' && 'generate-traces' || 'qualify'",
                  "cancel-in-progress: ${{ inputs.mode != 'generate-traces' }}"):
        if token not in candidate:
            raise ValueError("dispatch/concurrency contract missing: " + token)
    for token in ('timeout-minutes: 30\n', 'ref: ${{ github.sha }}', 'persist-credentials: false',
                  'test "$GITHUB_SHA" = "$WASM_TRACE_EXPECTED_HEAD"',
                  'test "$GITHUB_WORKFLOW_SHA" = "$WASM_TRACE_EXPECTED_HEAD"',
                  'test "$(git rev-parse HEAD)" = "$WASM_TRACE_EXPECTED_HEAD"',
                  'test -z "$(git status --porcelain --untracked-files=all)"',
                  'scripts/prepare-wasm-traces.sh', 'node-version: "26"', 'QUINT_VERSION: 0.32.0',
                  'retention-days: 7', 'wasm-trace-preparation-${{ github.sha }}'):
        if token not in generation:
            raise ValueError("generation guard/bound missing: " + token)
    if candidate.count("\npermissions:\n  contents: read\n") != 1 or "contents: write" in candidate or "packages: write" in candidate:
        raise ValueError("trace preparation may not gain write authority")
    for token in ('setup-dotnet', 'dotnet ', 'rustup ', 'playwright', 'scripts/wasm-release/', 'publish', 'secrets.'):
        if token in generation:
            raise ValueError("generation gained unrelated execution: " + token)
    if "${{ runner." in generation.split("    steps:",1)[0]:
        raise ValueError("runner context is unavailable in job-level env")
    generation_order = [generation.find(value) for value in (
        'Guard dispatch and source before setup', 'Static workflow preflight',
        'Set up Node for trace preparation', 'Install exact trace tool',
        'Prepare bounded canonical traces', 'Retain trace preparation evidence')]
    if min(generation_order) < 0 or generation_order != sorted(generation_order):
        raise ValueError("generation guard/setup/execution ordering differs")
    if any(value not in qualify for value in required[1:]):
        raise ValueError("complete normal qualification lost a required gate")
    positions = [qualify.find(value) for value in ordered]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        raise ValueError("qualification steps are missing or out of order")

try:
    validate(text)
except ValueError as error:
    raise SystemExit(f"workflow preflight: {error}") from error

for broken in (
    text.replace("scripts/verify-wasm-contracts.sh", "scripts/missing-contract-gate.sh"),
    text.replace("scripts/wasm-release/qualify-supervisor-custody.sh --custody", "scripts/missing-supervisor-gate.sh"),
    text.replace("scripts/wasm-release/qualify-supervisor-custody.sh --bootstrap", "scripts/missing-supervisor-bootstrap.sh"),
    text.replace("github.event_name == 'workflow_dispatch' && inputs.mode == 'generate-traces'", "inputs.mode == 'generate-traces'"),
    text.replace('test "$GITHUB_WORKFLOW_SHA" = "$WASM_TRACE_EXPECTED_HEAD"', 'true'),
    text.replace("cancel-in-progress: ${{ inputs.mode != 'generate-traces' }}", "cancel-in-progress: true"),
    text.replace('timeout-minutes: 30\n', 'timeout-minutes: 300'),
    text.replace("inputs.mode == 'qualify'", "inputs.mode == 'generate-traces'", 1),
    text.replace('contents: read', 'contents: write'),
    text.replace("      WASM_TRACE_MODE:", "      BAD_JOB_ENV: ${{ runner.temp }}\n      WASM_TRACE_MODE:"),
    text.replace('run: scripts/prepare-wasm-traces.sh', 'run: dotnet test'),
    text.replace(ordered[7], "BROKEN-A").replace(ordered[8], ordered[7]).replace("BROKEN-A", ordered[8]),
):
    try:
        validate(broken)
    except ValueError:
        pass
    else:
        raise SystemExit("workflow preflight: a known-bad mutation was accepted")

global_json = json.loads(pathlib.Path(sys.argv[2]).read_text(encoding="utf-8"))
if global_json["sdk"]["version"] != "10.0.401":
    raise SystemExit("workflow preflight: global.json SDK is not 10.0.401")

tools = json.loads(pathlib.Path(sys.argv[3]).read_text(encoding="utf-8"))["tools"]
if tools["fable"]["version"] != "5.18.0" or tools["fable"].get("rollForward") is not False:
    raise SystemExit("workflow preflight: Fable manifest pin is not exact 5.18.0")

gate = pathlib.Path(sys.argv[4]).read_text(encoding="utf-8")
if "ACTIONLINT_VERSION: 1.7.7" not in gate or "SHELLCHECK_VERSION: 0.11.0" not in gate:
    raise SystemExit("workflow preflight: repository lint tool pins changed")
if "&wasm_paths" in text or "*wasm_paths" in text:
    raise SystemExit("workflow preflight: actionlint 1.7.7 requires literal path sequences")
PY

python3 - "$repo" <<'PYTRACE'
# Inert source/collection controls: no generator import, model/tool invocation,
# supplied runner, package setup, browser, network or publication.
import contextlib,importlib.util,io,json,pathlib,shutil,sys,tempfile
from unittest.mock import patch
sys.dont_write_bytecode=True
repo=pathlib.Path(sys.argv[1]);spec=importlib.util.spec_from_file_location('trace_support',repo/'scripts/prepare-wasm-traces.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
assert m.BOUNDS=={'generationSeconds':1200,'terminationGraceSeconds':30,'nodeOldSpaceMiB':2048,'perFileBytes':67108864,'payloadBytes':67108864,'diagnosticBytes':8388608,'manifestBytes':65536,'archiveBytes':83886080}
paths,rows=m.census(repo);assert len(paths)==71 and len(rows['default'])==53 and len(rows['compatible'])==16
wrapper=(repo/'scripts/prepare-wasm-traces.sh').read_text()
for token in ('timeout --kill-after=30s 20m prlimit --fsize=67108864:67108864 --core=0:0',
              '--max-old-space-size=2048','--kill-after=5s 120s','if (( status != 0 )); then exit "$status"; fi',
              'run_generator default tests/Wasm.Lifecycle.Correspondence/regenerate-event-traces.py',
              'run_generator compatible tests/Wasm.Supervisor.Compatibility/generate-compatible-traces.py'):
 assert token in wrapper,token
assert wrapper.index('prepare --root')<wrapper.index('node --version')<wrapper.index('run_generator default')<wrapper.index('run_generator compatible')
head='a'*40
env={'GITHUB_EVENT_NAME':'workflow_dispatch','WASM_TRACE_MODE':'generate-traces','WASM_TRACE_EXPECTED_HEAD':head,
     'GITHUB_SHA':head,'GITHUB_WORKFLOW_SHA':head,'GITHUB_REF':'refs/heads/source-candidate'}
def fake_git(root,*args):
 if args==('rev-parse','HEAD'):return head
 if args==('rev-parse','HEAD^{tree}'):return 'b'*40
 if args[:1] in (('status',),('diff',),('ls-files',)):return ''
 raise AssertionError(args)
with patch.object(m,'git',fake_git):
 assert m.source_guard(repo,env)==head
 for field,value in [('GITHUB_EVENT_NAME','pull_request'),('WASM_TRACE_MODE','qualify'),('WASM_TRACE_EXPECTED_HEAD','bad'),('GITHUB_SHA','c'*40),('GITHUB_WORKFLOW_SHA','c'*40),('GITHUB_REF','')]:
  broken=dict(env,**{field:value})
  try:m.source_guard(repo,broken)
  except ValueError:pass
  else:raise AssertionError('bad configuration admitted: '+field)
 with patch.object(m,'git',lambda root,*args:'dirty' if args[:1]==('status',) else fake_git(root,*args)):
  try:m.source_guard(repo,env)
  except ValueError:pass
  else:raise AssertionError('dirty checkout admitted')
 with tempfile.TemporaryDirectory(prefix='wasm-trace-inert-controls-') as raw:
  inert=pathlib.Path(raw)/'inert-generator.py';inert.write_text("schedules=[('safe',1)]\nraise RuntimeError('generator import is forbidden')\n");assert m.schedules(inert)==[('safe',1)]
  root=pathlib.Path(raw)/'source';root.mkdir()
  for name in m.MODEL_INPUTS:
   target=root/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(repo/name,target)
  for directory in (m.DEFAULT+'/Traces',m.SELECTED+'/Traces'):(root/directory).mkdir(parents=True)
  inert_tool=pathlib.Path(raw)/'inert-tool';inert_tool.write_bytes(b'inert tool identity; never executed\n')
  output=pathlib.Path(raw)/'missing-tools'
  with patch.object(m.shutil,'which',lambda name:None):
   try:m.prepare(root,output,env)
   except ValueError:pass
   else:raise AssertionError('missing tool admitted')
  assert not output.exists(),'missing prerequisite created execution output'
  for case in ('good','outside-change','ignored-change','extra-trace','linked-trace','failed-generator','tool-prerequisite','unknown-cleanup','collection-failure','primary-and-collection-failure','cleanup-failure'):
   output=pathlib.Path(raw)/case
   with patch.object(m.shutil,'which',lambda name:str(inert_tool)):
    m.prepare(root,output,env)
   for name in paths:
    target=root/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(b'inert output control\n')
   (output/'work/generation.log').write_text('inert diagnostic\n')
   (output/'work/stages.jsonl').write_text('\n'.join(json.dumps({'generator':name,'exitCode':0}) for name in ('default','compatible'))+'\n')
   status=7 if case in ('failed-generator','primary-and-collection-failure') else (2 if case=='tool-prerequisite' else 0)
   cleanup='unknown-timeout-or-signal' if case=='unknown-cleanup' else ('not-started' if case=='tool-prerequisite' else 'generator-process-returned')
   if case=='extra-trace':(root/m.DEFAULT/'Traces/unexpected.itf.json').write_bytes(b'extra')
   if case=='linked-trace':(root/paths[0]).unlink();(root/paths[0]).symlink_to(root/paths[1])
   if case=='ignored-change':(root/'ignored.txt').write_text('inert ignored change')
   def git_impl(root,*args):
    if case=='outside-change' and args==('diff','--name-only','HEAD'):return 'src/changed.fs'
    if case=='ignored-change' and args==('ls-files','--others','--ignored','--exclude-standard'):return 'ignored.txt'
    return fake_git(root,*args)
   with patch.object(m,'git',git_impl),contextlib.redirect_stdout(io.StringIO()):
    if case in ('collection-failure','primary-and-collection-failure'):
     with patch.object(m.zipfile,'ZipFile',side_effect=OSError('inert archive failure')):result=m.collect(root,output,status,{'node':'inert-test-double'},cleanup)
    elif case=='cleanup-failure':
     with patch.object(m.shutil,'rmtree',side_effect=OSError('inert scratch cleanup failure')):result=m.collect(root,output,status,{'node':'inert-test-double'},cleanup)
    else:result=m.collect(root,output,status,{'node':'inert-test-double'},cleanup,case!='tool-prerequisite')
   manifest=json.loads((output/'artifact/manifest.json').read_text())
   assert (result==0)==(case=='good'),case
   assert manifest['fixturePreparationComplete']==(case=='good'),case
   assert manifest['qualification'] is False and manifest['publication'] is False
   if case in ('failed-generator','primary-and-collection-failure'):assert result==7 and manifest['outputs']==[] and manifest['firstFailure']=='generator process exited 7'
   if case=='tool-prerequisite':assert manifest['generationStarted'] is False and manifest['generationExitCode'] is None and manifest['firstFailure']=='tool prerequisite exited 2'
   if case in ('collection-failure','primary-and-collection-failure'):assert not (output/'artifact/trace-preparation.zip').exists()
   if case=='cleanup-failure':assert manifest['scratchCleanup']=='failed'
   if case=='good':
    with m.zipfile.ZipFile(output/'artifact/trace-preparation.zip') as archive:assert set(archive.namelist())==set(paths)|{'manifest.json','generation.log'}
   if case=='ignored-change':(root/'ignored.txt').unlink()
   if case=='extra-trace':(root/m.DEFAULT/'Traces/unexpected.itf.json').unlink()
   if case=='linked-trace':(root/paths[0]).unlink()
print('wasm-trace-preflight: actual 53/16 census; bad dispatch/head/dirty/tool prerequisites refused; output/cause/cleanup/archive controls passed (inert only)')
PYTRACE

grep -Fq "dotnet restore \"\$repo/tests/Wasm.Contracts.Tests/FS.GG.Wasm.Contracts.Tests.fsproj\" --locked-mode" \
  "$repo/scripts/verify-wasm-contracts.sh" || {
  echo "workflow preflight: contract tests lack an explicit locked restore" >&2
  exit 1
}

if grep -n '__SOURCE_DIRECTORY__' \
  "$repo/tests/Wasm.Contracts.Tests/ContractTests.fs" \
  "$repo/tests/Wasm.Invocation.Tests/Program.fs"; then
  echo "workflow preflight: hosted fixture tests depend on a path-mapped source directory" >&2
  exit 1
fi

grep -Fq '<Link>fixtures/%(Filename)%(Extension)</Link>' \
  "$repo/tests/Wasm.Contracts.Tests/FS.GG.Wasm.Contracts.Tests.fsproj" || {
  echo "workflow preflight: contract fixture content boundary is missing" >&2
  exit 1
}
grep -Fq '<Link>modules/%(RecursiveDir)%(Filename)%(Extension)</Link>' \
  "$repo/tests/Wasm.Invocation.Tests/FS.GG.Wasm.Invocation.Tests.fsproj" || {
  echo "workflow preflight: invocation module content boundary is missing" >&2
  exit 1
}

grep -Fq 'quint-lifecycle-tests: passed=' "$repo/scripts/verify-wasm-lifecycle.sh" || {
  echo "workflow preflight: Quint gate does not report its selected named-test count" >&2
  exit 1
}
test "$(grep -Ec '^  run [A-Za-z0-9]+Test =' "$repo/eng/wasm-shared/lifecycle.qnt")" -eq 8 || {
  echo "workflow preflight: expected eight explicitly named Quint lifecycle tests" >&2
  exit 1
}

grep -Fq 'b761e3a0721dbae9c09a0059e5fdb2bf917d1b4a8a7b430fb3b5aafb0984b2c4' \
  "$repo/sdk/wasm/toolchains/acquire-wasi-sdk.sh" || {
  echo "workflow preflight: WASI SDK 34 archive identity is not pinned" >&2
  exit 1
}
grep -Fq '<PackageReference Include="FS.GG.Wasm.Browser" Version="[0.2.0]" Condition="' \
  "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj" || {
  echo "workflow preflight: fresh consumer does not bind the stable 0.2.0 browser package" >&2
  exit 1
}
if grep -Fq '<ProjectReference' "$repo/tests/Wasm.PackageConsumer/Consumer.fsproj"; then
  echo "workflow preflight: fresh package consumer has a sibling project reference" >&2
  exit 1
fi
python3 - "$repo" <<'PYVERSION'
import json,pathlib,sys,xml.etree.ElementTree as ET
repo=pathlib.Path(sys.argv[1]);version=ET.parse(repo/'eng/wasm-shared/version.props').findtext('./PropertyGroup/WasmSharedVersion')
policy=json.loads((repo/'scripts/wasm-release/api-baseline-policy.json').read_text())
def validate(version,sdk,consumer,contracts,lifecycle):
    if version != policy['candidateVersion'] or sdk != version:
        raise ValueError('coherent candidate version mismatch')
    if 'Version="[$(WasmCandidateVersion)]"' not in consumer or 'Version="[0.2.0]"' not in consumer:
        raise ValueError('candidate override or protected default missing')
    if 'FS.GG.Wasm.Contracts.$version.nupkg' not in contracts or 'FS.GG.Wasm.Browser.$version.nupkg' not in lifecycle:
        raise ValueError('qualification package path bypasses coherent metadata')
sdk=(repo/'sdk/wasm/VERSION').read_text().strip();consumer=(repo/'tests/Wasm.PackageConsumer/Consumer.fsproj').read_text();contracts=(repo/'scripts/verify-wasm-contracts.sh').read_text();lifecycle=(repo/'scripts/verify-wasm-lifecycle.sh').read_text()
validate(version,sdk,consumer,contracts,lifecycle)
for broken in [(version,'0.2.0',consumer,contracts,lifecycle),(version,sdk,consumer,contracts.replace('$version.nupkg','0.2.0.nupkg'),lifecycle),(version,sdk,consumer.replace('Version="[$(WasmCandidateVersion)]"','Version="[0.2.0]"'),contracts,lifecycle)]:
    try:validate(*broken)
    except ValueError:pass
    else:raise SystemExit('known-bad candidate version binding accepted')
print('wasm-candidate-preflight: actual coherent metadata=pass old candidate paths/default override omission=refused')
PYVERSION
"$repo/tests/release/wasm/test-release-wasm.sh" --source-only
python3 "$repo/tests/release/wasm/test-installed-org-workflow.py"
python3 "$repo/tests/release/wasm/test-api-baseline-policy.py"
for control in \
  'rust-bar.wasm' 'rust-sc2.wasm' 'c-bar.wasm' 'c-sc2.wasm' \
  'trap remains inside the package Worker' \
  'deadline terminates and repeated disposal is harmless'; do
  grep -Fq "$control" "$repo/tests/Wasm.PackageConsumer/browser/package-consumer.spec.mjs" || {
    echo "workflow preflight: missing installed-package browser control $control" >&2
    exit 1
  }
done
for path in 'sdk/wasm/**' 'examples/wasm/**' 'tests/Wasm.PackageConsumer/**' 'scripts/wasm-release/**' 'tests/release/wasm/**'; do
  grep -Fq -- "- \"$path\"" "$workflow" || {
    echo "workflow preflight: missing stage .3 path filter $path" >&2
    exit 1
  }
done

bash -n \
  "$repo/scripts/verify-wasm-workflow.sh" \
  "$repo/scripts/verify-wasm-contracts.sh" \
  "$repo/scripts/verify-wasm-lifecycle.sh" \
  "$repo/scripts/verify-wasm-package-consumer.sh" \
  "$repo/scripts/wasm-release/prepare.sh" \
  "$repo/scripts/wasm-release/qualify-supervisor-custody.sh" \
  "$repo/scripts/wasm-release/readback.sh" \
  "$repo/scripts/wasm-release/stage-assets.sh" \
  "$repo/sdk/wasm/build-source-archive.sh" \
  "$repo/sdk/wasm/verify-sdk.sh" \
  "$repo/sdk/wasm/toolchains/acquire-wasi-sdk.sh" \
  "$repo/tests/Wasm.Compatibility/modules/build-fixtures.sh" \
  "$repo/tests/Wasm.Browser.Conformance/run.sh" \
  "$repo/tests/Wasm.PackageConsumer/browser/run.sh"

echo "wasm-workflow-preflight: static=pass jobs=qualify+explicit-preparation order=linear inputs=pinned"
