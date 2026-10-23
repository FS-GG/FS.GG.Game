#!/usr/bin/env python3
"""Inert source/census/manifest support for prepare-wasm-traces.sh.

Never imports a generator or executes Quint. Only the maintained shell entry
launches the two generators, under one timeout/prlimit process group.
"""
import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import time
import zipfile

DEFAULT = 'tests/Wasm.Lifecycle.Correspondence'
SELECTED = 'tests/Wasm.Supervisor.Compatibility'
GENERATORS = [DEFAULT + '/regenerate-event-traces.py', SELECTED + '/generate-compatible-traces.py']
TYPECHECK_INPUTS = ['eng/wasm-shared/event-boundary-qualification.qnt', 'eng/wasm-shared/compatible-qualification.qnt']
MODEL_INPUTS = ['eng/wasm-shared/lifecycle.qnt', 'eng/wasm-shared/event-boundary-qualification.qnt',
                'eng/wasm-shared/expiry-qualification.qnt', 'eng/wasm-shared/compatible-qualification.qnt',
                DEFAULT + '/generate-traces.py', *GENERATORS]
MIB = 1024 * 1024
BOUNDS = {'setupSeconds': 300, 'generationSeconds': 1200, 'terminationGraceSeconds': 30, 'nodeOldSpaceMiB': 2048,
          'perFileBytes': 64*MIB, 'payloadBytes': 64*MIB, 'diagnosticBytes': 8*MIB,
          'manifestBytes': 64*1024, 'archiveBytes': 80*MIB}


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args]).decode().strip()


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(MIB), b''):
            value.update(chunk)
    return value.hexdigest()


def schedules(path):
    """Extract only the generators' literal schedule declarations, never import.

    The allowlist admits assignments to schedules and literal loops appending to
    that list; all calls must be list.append or string.lower. Execution/import/
    subprocess nodes are excluded. Unsupported future construction refuses.
    """
    tree = ast.parse(path.read_text(), filename=str(path))
    selected = []
    allowed = (ast.Module, ast.Assign, ast.Name, ast.Store, ast.Load, ast.List, ast.Tuple,
               ast.Constant, ast.For, ast.Expr, ast.Call, ast.Attribute, ast.BinOp, ast.Add)
    for node in tree.body:
        assign = isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name) and node.targets[0].id == 'schedules'
        append = isinstance(node, (ast.For, ast.Expr)) and any(
            isinstance(child, ast.Call) and isinstance(child.func, ast.Attribute)
            and isinstance(child.func.value, ast.Name) and child.func.value.id == 'schedules'
            and child.func.attr == 'append' for child in ast.walk(node))
        if not (assign or append):
            continue
        for child in ast.walk(node):
            if not isinstance(child, allowed):
                raise ValueError('unsupported schedule declaration: ' + type(child).__name__)
            if isinstance(child, ast.Call):
                fn = child.func
                if not isinstance(fn, ast.Attribute) or fn.attr not in ('append', 'lower'):
                    raise ValueError('unsupported schedule call')
                if fn.attr == 'append' and not (isinstance(fn.value, ast.Name) and fn.value.id == 'schedules'):
                    raise ValueError('schedule append target differs')
        selected.append(node)
    namespace = {}
    exec(compile(ast.Module(body=selected, type_ignores=[]), str(path), 'exec'), {'__builtins__': {}}, namespace)
    rows = namespace.get('schedules', [])
    names = [row[0] for row in rows]
    if len(names) != len(set(names)) or any(not re.fullmatch('[a-z0-9-]+', name) for name in names):
        raise ValueError('schedule names are duplicate or unsafe')
    return rows


def census(root):
    first, second = [schedules(root / name) for name in GENERATORS]
    if (len(first), len(second)) != (58, 16):
        raise ValueError('reviewed schedule census changed (expected 58/16)')
    paths = [DEFAULT + '/Traces/' + row[0] + '_0.itf.json' for row in first]
    paths += [SELECTED + '/Traces/' + row[0] + '_0.itf.json.gz' for row in second]
    paths += [DEFAULT + '/GeneratedTraces.fs', SELECTED + '/GeneratedCompatibilityTraces.fs']
    if len(paths) != 76 or len(set(paths)) != 76:
        raise ValueError('reviewed output census is not 76 unique paths')
    return paths, {'default': first, 'compatible': second}


def source_guard(root, env):
    expected = env.get('WASM_TRACE_EXPECTED_HEAD', '')
    checks = [
        (env.get('GITHUB_EVENT_NAME') == 'workflow_dispatch', 'manual dispatch required'),
        (env.get('WASM_TRACE_MODE') == 'generate-traces', 'explicit generate-traces mode required'),
        (re.fullmatch('[0-9a-f]{40}', expected) is not None, 'expected head must be lowercase 40-hex'),
        (env.get('GITHUB_SHA') == expected, 'workflow SHA differs from expected head'),
        (env.get('GITHUB_WORKFLOW_SHA') == expected, 'workflow definition SHA differs from expected head'),
        (bool(re.fullmatch(r'refs/(heads|tags)/[^\s]+', env.get('GITHUB_REF', ''))), 'dispatch ref is missing or invalid'),
        (git(root, 'rev-parse', 'HEAD') == expected, 'checkout HEAD differs from expected head'),
        (not git(root, 'status', '--porcelain', '--untracked-files=all'), 'initial checkout is not clean'),
    ]
    failures = [message for valid, message in checks if not valid]
    if failures:
        raise ValueError('; '.join(failures))
    return expected


def regular(root, relative):
    path = root / relative
    for parent in [path, *path.parents]:
        if parent == root:
            break
        if parent.is_symlink():
            raise ValueError('linked output/input refused: ' + relative)
    if path.lstat().st_nlink != 1:
        raise ValueError('hardlinked output/input refused: ' + relative)
    if not stat.S_ISREG(path.lstat().st_mode):
        raise ValueError('nonregular output/input refused: ' + relative)
    return path


def ignored_inventory(root):
    names = git(root, 'ls-files', '--others', '--ignored', '--exclude-standard').splitlines()
    if len(names) > 256:
        raise ValueError('initial ignored-file inventory exceeds 256 files')
    result = {}
    total = 0
    for name in names:
        path = regular(root, name)
        total += path.stat().st_size
        if total > 64*MIB:
            raise ValueError('ignored-file inventory exceeds 64 MiB')
        result[name] = digest(path)
    return result


def prepare(root, output, env):
    expected = source_guard(root, env)
    paths, selected = census(root)
    if output != output.resolve() or output.exists() or output.is_symlink() or root == output or root in output.parents:
        raise ValueError('output must be a fresh path outside the checkout')
    if any(parent.is_symlink() for parent in output.parents):
        raise ValueError('linked output parent refused')
    inputs = {name: digest(regular(root, name)) for name in MODEL_INPUTS}
    for directory in (DEFAULT + '/Traces', SELECTED + '/Traces'):
        for path in (root / directory).iterdir():
            name = path.relative_to(root).as_posix()
            if name not in paths:
                raise ValueError('unexpected initial trace path: ' + name)
            regular(root, name)
    tools = {}
    for name in ('python3', 'node', 'quint', 'timeout', 'prlimit', 'bash'):
        tool = shutil.which(name)
        if tool is None:
            raise ValueError('required preparation tool unavailable: ' + name)
        tools[name] = str(Path(tool).resolve())
    selected_quint = shutil.which(env.get('QUINT', 'quint'))
    if selected_quint is None or Path(selected_quint).resolve() != Path(tools['quint']):
        raise ValueError('QUINT and PATH quint must select the same installation')
    output.mkdir(mode=0o700)
    (output / 'work').mkdir(mode=0o700)
    (output / 'artifact').mkdir(mode=0o700)
    record = {'sourceHead': expected, 'sourceTree': git(root, 'rev-parse', 'HEAD^{tree}'),
              'inputs': inputs, 'outputPaths': paths, 'schedules': selected, 'tools': tools,
              'ignoredFiles': ignored_inventory(root),
              'toolSha256': {name: digest(Path(path)) for name, path in tools.items()},
              'toolProvenance': 'actual installed file/version observations; npm dependency closure not independently attested',
              'bounds': BOUNDS, 'setupDeadlineUnix': env.get('WASM_TRACE_SETUP_DEADLINE', ''), 'startedMonotonic': time.monotonic(),
              'workflow': {name: env.get(name, '') for name in ('GITHUB_REPOSITORY', 'GITHUB_REF', 'GITHUB_SHA',
                  'GITHUB_WORKFLOW_REF', 'GITHUB_WORKFLOW_SHA', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT',
                  'ImageOS', 'ImageVersion', 'RUNNER_OS', 'RUNNER_ARCH')}}
    (output / 'preparation.json').write_text(json.dumps(record, sort_keys=True) + '\n')
    return record


def collect(root, output, status, versions, cleanup, generation_started=True):
    record = json.loads((output / 'preparation.json').read_text())
    first_failure = None if status == 0 else ('generator process exited ' if generation_started else 'tool prerequisite exited ') + str(status)
    errors = []
    files = []
    def attempt(action):
        try:
            return action()
        except (OSError, ValueError, subprocess.SubprocessError) as error:
            errors.append((type(error).__name__ + ': ' + str(error))[:2048])
            return None
    def closure():
        if git(root, 'rev-parse', 'HEAD') != record['sourceHead']:
            raise ValueError('checkout head changed during preparation')
        for name, expected in record['inputs'].items():
            if digest(regular(root, name)) != expected:
                raise ValueError('model/generator input changed: ' + name)
        if ignored_inventory(root) != record['ignoredFiles']:
            raise ValueError('generation changed ignored paths outside its output set')
        expected = set(record['outputPaths'])
        found = set()
        for directory in (DEFAULT + '/Traces', SELECTED + '/Traces'):
            for path in (root / directory).iterdir():
                found.add(path.relative_to(root).as_posix())
        expected_traces = {p for p in expected if '/Traces/' in p}
        if found != expected_traces:
            raise ValueError('trace filename census differs: ' + repr(sorted(found ^ expected_traces))[:2048])
        changed = set(git(root, 'diff', '--name-only', 'HEAD').splitlines())
        untracked = set(git(root, 'ls-files', '--others', '--exclude-standard').splitlines())
        if (changed | untracked) - expected:
            raise ValueError('generation changed paths outside its closed output set')
        for name in sorted(expected):
            path = regular(root, name)
            files.append({'path': name, 'bytes': path.stat().st_size, 'sha256': digest(path)})
        if sum(item['bytes'] for item in files) > BOUNDS['payloadBytes']:
            raise ValueError('generated payload exceeds 64 MiB')
        return sorted(changed | untracked)
    changed = attempt(closure) if status == 0 else None
    diagnostics = []
    work = output / 'work'
    log = work / 'generation.log'
    remaining_diagnostics = BOUNDS['diagnosticBytes']
    failed_schedule = None
    failed_evidence_present = any((work / name).exists() or (work / name).is_symlink()
                                  for name in ('failure-schedule.json', 'counterexample.itf'))
    def retain_failed_file(name, limit):
        nonlocal remaining_diagnostics, failed_schedule
        source = work / name
        if not source.exists() and not source.is_symlink():
            return
        regular(work, name)
        size = source.stat().st_size
        if size > limit or size > remaining_diagnostics:
            raise ValueError('failed schedule evidence exceeds retained budget: ' + name)
        shutil.copyfile(source, output / 'artifact' / name)
        diagnostics.append({'path': name, 'originalBytes': size, 'originalSha256': digest(source),
                            'retainedBytes': size, 'omittedBytes': 0})
        remaining_diagnostics -= size
        if name == 'failure-schedule.json':
            metadata = json.loads(source.read_text())
            if not isinstance(metadata, dict) or not isinstance(metadata.get('schedule'), str) or not isinstance(metadata.get('argv'), list) or any(not isinstance(value, str) for value in metadata['argv']) or not isinstance(metadata.get('exitCode'), int) or metadata['exitCode'] == 0:
                raise ValueError('failed schedule metadata has invalid shape')
            failed_schedule = metadata
    for name, limit in (('failure-schedule.json', 8192), ('counterexample.itf', 4*MIB)):
        attempt(lambda: retain_failed_file(name, limit))
    if status == 0 and failed_evidence_present:
        errors.append('successful generation unexpectedly left failed-schedule evidence')
    def retain_log():
        if not log.exists():
            return
        original_size = log.stat().st_size
        original_digest = digest(log)
        retained = output / 'artifact' / 'generation.log'
        with log.open('rb') as source, retained.open('xb') as target:
            target.write(source.read(remaining_diagnostics))
        diagnostics.append({'path': 'generation.log', 'originalBytes': original_size,
                            'originalSha256': original_digest, 'retainedBytes': retained.stat().st_size,
                            'omittedBytes': max(0, original_size - retained.stat().st_size)})
    attempt(retain_log)
    stages = []
    def retain_stages():
        path = output / 'work' / 'stages.jsonl'
        if path.exists():
            if path.stat().st_size > 4096:
                raise ValueError('stage metadata exceeds 4 KiB')
            loaded = [json.loads(line) for line in path.read_text().splitlines()]
            if any(not isinstance(item, dict) or not isinstance(item.get('exitCode'), int) or (('typecheck' in item) == ('generator' in item)) for item in loaded):
                raise ValueError('stage metadata has invalid shape')
            stages.extend(loaded)
        expected = [('typecheck', model) for model in TYPECHECK_INPUTS] + [('generator', name) for name in ('default', 'compatible')]
        actual = [('typecheck', item['typecheck']) if 'typecheck' in item else ('generator', item.get('generator')) for item in stages]
        if status == 0 and (actual != expected or any(item.get('exitCode') != 0 for item in stages)):
            raise ValueError('both early typechecks and maintained generator successes must be recorded')
    attempt(retain_stages)
    failed_stage = next((item for item in stages if item.get('exitCode') != 0), None)
    if status != 0 and failed_stage is not None and 'typecheck' in failed_stage:
        first_failure = 'typecheck process exited ' + str(status)
    scratch_cleanup = 'not-attempted-uncertain-process-settlement'
    if cleanup in ('generator-process-returned', 'not-started'):
        if attempt(lambda: shutil.rmtree(output / 'work')) is None and not (output / 'work').exists():
            scratch_cleanup = 'owned-temporary-paths-removed'
        else:
            scratch_cleanup = 'failed'
    accepted = generation_started and status == 0 and not errors and cleanup == 'generator-process-returned'
    manifest = {'schema': 'fsgg.wasm.trace-preparation/1', **record,
                'elapsedSeconds': time.monotonic() - record['startedMonotonic'],
                'commands': [{'argv': ['quint', 'typecheck', model], 'stage': 'typecheck'} for model in TYPECHECK_INPUTS] + [{'argv': ['python3', path], 'seed': 20261002 if index == 0 else 20261003, 'backend': 'typescript', 'quintVerbosity': 1} for index, path in enumerate(GENERATORS)],
                'generationStarted': generation_started, 'generationExitCode': status if generation_started else None, 'prerequisiteExitCode': status if not generation_started else 0, 'firstFailure': first_failure or (errors[0] if errors else ('cleanup outcome unknown' if not accepted else None)),
                'collectionErrors': errors, 'cleanup': cleanup, 'scratchCleanup': scratch_cleanup, 'stages': stages, 'failedStage': failed_stage,
                'descendantSettlement': 'not-established; timeout process group and hosted VM teardown only',
                'toolVersions': versions, 'changedPaths': changed, 'diagnostics': diagnostics,
                'failedSchedule': failed_schedule,
                'diagnosticEvidenceGap': ('generator failed without retained schedule metadata; original log only' if generation_started and status != 0 and failed_schedule is None and (failed_stage is None or 'typecheck' not in failed_stage) else None),
                'fixturePreparationComplete': accepted, 'qualification': False, 'publication': False,
                'outputs': files if accepted else []}
    artifact = output / 'artifact'
    def archive():
        data = (json.dumps(manifest, sort_keys=True, separators=(',', ':')) + '\n').encode()
        if len(data) > BOUNDS['manifestBytes']:
            raise ValueError('manifest exceeds 64 KiB')
        (artifact / 'manifest.json').write_bytes(data)
        target = artifact / 'trace-preparation.pending.zip'
        with zipfile.ZipFile(target, 'x', compression=zipfile.ZIP_DEFLATED) as bundle:
            bundle.write(artifact / 'manifest.json', 'manifest.json')
            for diagnostic in diagnostics:
                bundle.write(artifact / diagnostic['path'], diagnostic['path'])
            if accepted:
                for item in files:
                    bundle.write(root / item['path'], item['path'])
        if target.stat().st_size > BOUNDS['archiveBytes']:
            target.unlink()
            raise ValueError('preparation archive exceeds 80 MiB')
        target.rename(artifact / 'trace-preparation.zip')
    attempt(archive)
    if errors:
        (artifact / 'trace-preparation.zip').unlink(missing_ok=True)
        (artifact / 'trace-preparation.pending.zip').unlink(missing_ok=True)
        for diagnostic in diagnostics:
            original = artifact / diagnostic['path']
            if original.exists():
                retained_name = 'failed-' + diagnostic['path']
                original.rename(artifact / retained_name)
                diagnostic['path'] = retained_name
        manifest['fixturePreparationComplete'] = False
        manifest['outputs'] = []
        manifest['firstFailure'] = first_failure or errors[0]
        updated = (json.dumps(manifest, sort_keys=True, separators=(',', ':')) + '\n').encode()
        if len(updated) <= BOUNDS['manifestBytes']:
            (artifact / 'manifest.json').write_bytes(updated)
        else:
            (artifact / 'manifest.json').unlink(missing_ok=True)
        # Independently retain a bounded terminal failure summary if packing failed.
        fallback = (json.dumps({'schema': manifest['schema'], 'sourceHead': record['sourceHead'],
                    'fixturePreparationComplete': False, 'generationStarted': generation_started,
                    'generationExitCode': status if generation_started else None,
                    'prerequisiteExitCode': status if not generation_started else 0,
                    'sourceTree': record['sourceTree'], 'workflow': record['workflow'],
                    'toolVersions': versions, 'inputs': record['inputs'], 'diagnostics': diagnostics,
                    'failedStage': failed_stage, 'failedSchedule': failed_schedule, 'diagnosticEvidenceGap': manifest['diagnosticEvidenceGap'],
                    'firstFailure': manifest['firstFailure'], 'collectionErrors': errors[:16],
                    'cleanup': cleanup, 'scratchCleanup': scratch_cleanup, 'qualification': False},
                    sort_keys=True) + '\n').encode()
        if len(fallback) <= BOUNDS['manifestBytes']:
            (artifact / 'failure.json').write_bytes(fallback)
    print(json.dumps({'fixturePreparationComplete': accepted and not errors,
                      'generationStarted': generation_started,
                      'generationExitCode': status if generation_started else None,
                      'prerequisiteExitCode': status if not generation_started else 0,
                      'firstFailure': manifest['firstFailure'],
                      'collectionErrors': errors, 'qualification': False}, sort_keys=True))
    return 0 if accepted and not errors else (status if status else 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'collect'))
    parser.add_argument('--root', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--status', type=int, default=1)
    parser.add_argument('--versions', default='{}')
    parser.add_argument('--generation-started', action='store_true')
    parser.add_argument('--cleanup', default='unknown')
    args = parser.parse_args()
    root = args.root.resolve()
    output = args.output.absolute()
    try:
        if args.mode == 'prepare':
            prepare(root, output, os.environ)
            print('trace preparation preflight: accepted source/census/tool presence; generation not run')
            return 0
        return collect(root, output, args.status, json.loads(args.versions), args.cleanup, args.generation_started)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print('trace preparation refused: ' + str(error), file=sys.stderr)
        return args.status if args.mode == 'collect' and args.status else 2

if __name__ == '__main__':
    sys.exit(main())
