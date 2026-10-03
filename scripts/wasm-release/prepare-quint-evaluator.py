#!/usr/bin/env python3
"""Read-only, pinned Rust model readiness before the sole protected-producer pack."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import tarfile
import time
import urllib.request
import urllib.parse

REPO = 'quint-co/quint'  # The former informalsystems URL redirects here.
TAG = 'evaluator/v0.6.0'
RELEASE = 303596741
ASSET = 385413317
NAME = 'quint_evaluator-x86_64-unknown-linux-gnu.tar.gz'
SIZE = 1077099
ARCHIVE = '61755a09d5052d93a4e75e840059edfd0d3674aeda164b9d2464be3d6e21b1c2'
MEMBER = 'quint_evaluator'
MEMBER_SIZE = 2628304
MEMBER_SHA = 'b2efdeac5713d153e41bf2143b94ed75d888fdd5637f4a5d61a04c695313510a'
P = '16a401692f4c0dee7f6da1a86d0cd49e6ea3ec2c'
TREE = 'f06c9fef54b90d9dcd190b5bc1484f2e2e72f67a'
TOOL = {
    'dist/src/cli.js': 'ac12595b1cb7253feec93c79417615c6eb20fc3b6a3df35c5e3530b24e90a501',
    'dist/src/config.js': '19b3e12a9f185f306a9a4045626271ddef7cb42702c6bdd913da59548649fab6',
    'dist/src/rust/binaryManager.js': '23fac70fad430d47794a5aa157efa16f754494bf7874970313c8df565cc6a767',
}
LIMIT = 64 * 1024 * 1024

def need(condition, message):
    if not condition:
        raise ValueError(message)

def sha(body):
    return hashlib.sha256(body).hexdigest()

def metadata(value):
    need(value.get('id') == RELEASE and value.get('tag_name') == TAG and
         value.get('html_url') == f'https://github.com/{REPO}/releases/tag/{TAG}' and
         value.get('draft') is False and value.get('prerelease') is False, 'foreign release tuple')
    assets = [a for a in value.get('assets', []) if a.get('id') == ASSET or a.get('name') == NAME]
    need(len(assets) == 1, 'ambiguous asset')
    a = assets[0]
    need(all(a.get(k) == v for k, v in {
        'id': ASSET, 'name': NAME, 'size': SIZE, 'state': 'uploaded',
        'digest': 'sha256:' + ARCHIVE,
        'url': f'https://api.github.com/repos/{REPO}/releases/assets/{ASSET}',
        'browser_download_url': f'https://github.com/{REPO}/releases/download/{TAG}/{NAME}',
    }.items()), 'foreign asset tuple')
    return a

def binary(body):
    need(len(body) == SIZE and len(body) <= LIMIT and sha(body) == ARCHIVE, 'archive size/digest mismatch')
    with tarfile.open(fileobj=io.BytesIO(body), mode='r:gz') as archive:
        members = archive.getmembers()
        need(len(members) == 1, 'foreign archive members')
        member = members[0]
        need(member.name == MEMBER and member.isreg() and member.size == MEMBER_SIZE,
             'nonregular/foreign archive member')
        data = archive.extractfile(member).read(MEMBER_SIZE + 1)
        need(len(data) == MEMBER_SIZE and sha(data) == MEMBER_SHA, 'binary digest mismatch')
        need(data[:4] == b'\x7fELF', 'non-ELF evaluator')
        return data

def fresh(path, producer):
    need(path.is_absolute() and not path.exists() and not path.is_symlink(), 'cache/evidence must be fresh')
    need(path.resolve() == path and producer not in path.parents and path != producer,
         'cache/evidence must be outside producer with no symlink parents')
    need(path != Path.home() and Path.home() / '.quint' not in [path, *path.parents], 'global Quint cache forbidden')
    path.mkdir(mode=0o700)
    need(path.stat().st_uid == os.getuid() and path.stat().st_mode & 0o777 == 0o700, 'private ownership/mode required')

class OfficialRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, url):
        parsed = urllib.parse.urlsplit(url)
        need(parsed.scheme == 'https' and parsed.hostname in {
            'api.github.com', 'github.com', 'release-assets.githubusercontent.com'}, 'foreign asset redirect')
        redirected = super().redirect_request(request, response, code, message, headers, url)
        if parsed.hostname != 'api.github.com':
            redirected.remove_header('Authorization')
        return redirected


def fetch(url, token, accept, maximum):
    request = urllib.request.Request(url, headers={'Authorization': 'Bearer ' + token,
        'Accept': accept, 'User-Agent': 'FS-GG-WASM-pinned-evaluator'})
    with urllib.request.build_opener(OfficialRedirect()).open(request, timeout=60) as response:
        body = response.read(maximum + 1)
        need(len(body) <= maximum, 'oversize official response')
        return body

def tool(quint):
    entry = Path(quint).resolve()
    root = entry.parents[2]
    need(json.loads((root / 'package.json').read_text()).get('version') == '0.32.0', 'wrong Quint version')
    for name, digest in TOOL.items():
        need(sha((root / name).read_bytes()) == digest, 'Quint source identity mismatch: ' + name)
    return root

def commands(producer, quint):
    # Read the immutable P commands, keeping model, init, step, invariant, witnesses and bounds.
    text = (producer / 'scripts/verify-wasm-supervisor.sh').read_text()
    lines = [line.strip() for line in text.splitlines() if line.strip().startswith('"$quint" test ') or line.strip().startswith('"$quint" run ')]
    need(len(lines) == 2, 'producer qualifier command roster changed')
    witness_line = next(line for line in text.splitlines() if line.startswith('witnesses=('))
    witnesses = shlex.split(witness_line[len('witnesses=('):-1])
    need(len(witnesses) == 24 and len(set(witnesses)) == 24, 'producer witness roster changed')
    test = shlex.split(lines[0].split(' > ', 1)[0])[1:]
    run = shlex.split(lines[1].split(' > ', 1)[0])[1:]
    run = [token for token in run if token != '${witnesses[@]}']
    index = run.index('--witnesses');run[index + 1:index + 1] = witnesses
    result = [('model-controls', [quint, test[0], '--backend=rust', *test[1:]])]
    for name, entry in [('cold', 'initCompatible'), ('ready', 'initCompatible.then(readyCompatible)')]:
        args = [token.replace('$entry', entry) for token in run]
        result.append(('model-' + name, [quint, args[0], '--backend=rust', '--nthreads=1', *args[1:]]))
    return result, witnesses

def readiness(producer, quint, cache, evidence, run=subprocess.run):
    deadline = time.monotonic() + 600
    env = dict(os.environ, QUINT_HOME=str(cache))
    plan, witnesses = commands(producer, quint)
    need(all(args[0] == quint and [token for token in args if token.startswith("--backend=")] == ["--backend=rust"]
             for _, args in plan), "readiness backend/executable changed")
    for name, args in plan:
        with (evidence / (name + '.log')).open('wb') as log:
            result = run(args, cwd=producer, env=env, stdout=log, stderr=subprocess.STDOUT,
                         timeout=max(1, deadline - time.monotonic()))
        need(result.returncode == 0, 'pre-pack Rust readiness failed: ' + name)
    logs = [(evidence / ('model-' + name + '.log')).read_text() for name in ['cold', 'ready']]
    for witness in witnesses:
        need(any(re.search(r'^' + re.escape(witness) + r' was witnessed in [1-9][0-9]* trace', log, re.M) for log in logs),
             'pre-pack Rust witness missing: ' + witness)
    return plan

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--producer', type=Path, required=True)
    parser.add_argument('--quint-home', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args();producer = args.producer.resolve()
    git = lambda *a: subprocess.check_output(['git', '-C', str(producer), *a], text=True).strip()
    need(git('rev-parse', 'HEAD') == P and git('rev-parse', 'HEAD^{tree}') == TREE and not git('status', '--porcelain'), 'not clean immutable producer P')
    need(os.environ.get('QUINT_HOME') == str(args.quint_home), 'workflow Quint cache binding mismatch')
    need(os.environ.get('DOTNET_PROCESSOR_COUNT') == '1', 'resource bound must be one')
    fresh(args.evidence, producer);fresh(args.quint_home, producer)
    token = os.environ.get('GH_TOKEN');need(bool(token), 'authenticated read-only asset access required')
    quint = shutil.which('quint');need(bool(quint), 'Quint unavailable');root = tool(quint)
    version = subprocess.check_output([quint, '--version'], text=True)
    (args.evidence / 'quint-version.log').write_text(version)
    need(version.strip() == '0.32.0', 'wrong Quint CLI version')
    raw = fetch(f'https://api.github.com/repos/{REPO}/releases/tags/evaluator%2Fv0.6.0', token, 'application/vnd.github+json', 2 * 1024 * 1024)
    value = json.loads(raw);asset = metadata(value);(args.evidence / 'official-release.json').write_bytes(raw)
    body = fetch(asset['url'], token, 'application/octet-stream', LIMIT);data = binary(body)
    (args.evidence / NAME).write_bytes(body)
    cache = args.quint_home / 'rust-evaluator-v0.6.0';cache.mkdir(mode=0o700)
    target = cache / MEMBER
    with target.open('xb') as stream:stream.write(data)
    target.chmod(0o700)
    need(sha(target.read_bytes()) == MEMBER_SHA and not target.is_symlink(), 'activated cache mismatch')
    # Version authority is the official release tuple and exact binary, not an invented -V interface.
    receipt = {'schema': 'fsgg.wasm.prepack-rust-readiness/v1', 'producer': P, 'tree': TREE,
        'officialRepository': REPO, 'formerAlias': 'informalsystems/quint', 'release': RELEASE, 'asset': ASSET,
        'tag': TAG, 'archiveSha256': ARCHIVE, 'memberSha256': MEMBER_SHA, 'quintVersion': '0.32.0',
        'quintSource': TOOL, 'quintRoot': str(root), 'cache': str(target), 'backend': 'rust',
        'historicalTypeScriptProof': 'unchanged; this is additional Rust qualification', 'status': 'Pending',
        'producerInputs': {name: sha((producer / name).read_bytes()) for name in [
            'eng/wasm-shared/lifecycle.qnt', 'eng/wasm-shared/compatible-qualification.qnt',
            'scripts/verify-wasm-supervisor.sh', 'tests/Wasm.Supervisor.Compatibility/reviewed-source-proof.json']}}
    receipt_path = args.evidence / 'readiness.json';receipt_path.write_text(json.dumps(receipt, indent=2) + '\n')
    receipt['commands'] = readiness(producer, quint, args.quint_home, args.evidence)
    need(not git('status', '--porcelain'), 'readiness dirtied producer')
    need(sha(target.read_bytes()) == MEMBER_SHA, 'evaluator changed during readiness')
    receipt['status'] = 'Passed';receipt_path.write_text(json.dumps(receipt, indent=2) + '\n')
    print('pre-pack readiness: pinned official Rust evaluator; P unchanged; full model/witness checks passed')

if __name__ == '__main__':
    main()
