#!/usr/bin/env python3
"""Finite shared-WASM transaction adapter. No preparation, pack or compiler path.

Native calls use bounded HTTPS and workflow-scoped tokens. Offline controls inject
transport into the same decisions; they do not establish live authorization.
"""
from __future__ import annotations
from collections import namedtuple
import argparse
import base64
import io
import struct
import datetime as dt
import hashlib
import importlib.util
import json
import os
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
import zlib
import shutil
import stat
import xml.etree.ElementTree as ET
sys.dont_write_bytecode = True
ROOT = pathlib.Path(__file__).resolve().parents[2]
REPO = 'FS-GG/FS.GG.Game'
# This adapter is selected only for the coherent current 0.6 release. Historical
# consumed transactions remain in immutable history; no executor overlay is admitted.
P = subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True,timeout=10).strip()
TREE = subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD^{tree}'],text=True,timeout=10).strip()
VERSION = ET.parse(ROOT/'eng/wasm-shared/version.props').findtext('.//WasmSharedVersion')
PACKAGES = ('FS.GG.Wasm.Contracts', 'FS.GG.Wasm.Browser')
FILES = tuple(x + '.' + VERSION + '.nupkg' for x in PACKAGES) + ('fsgg-wasm-sdk-' + VERSION + '.tar.gz',)
QUALIFY_JOB = 'Shared WASM source, model, package, and browser qualification'
GATES = ('Static workflow preflight', 'Compile current supervisor source',
         'Prepare coherent candidate custody', 'Compile current installed supervisor', 'Rebuild pinned WebAssembly fixtures',
         'Verify compatibility contracts', 'Verify lifecycle, packages, and Fable consumers',
         'Run browser Worker conformance', 'Verify SDK archive and fresh package consumer',
         'Qualify compatible supervisor source and installed custody', 'Freeze eligible binding')
SCHEMA = 'fsgg.wasm.preparation/v2'
MAX_TOTAL = 512 * 1024 * 1024
MAX_ARCHIVE = 256 * 1024 * 1024
MAX_DIAGNOSTICS = 32 * 1024 * 1024
MAX_METADATA = 1024 * 1024
# Closed outer artifact roster. Values identify exact files produced by the
# maintained full qualifier, never a recursive package/cache/workspace copy.
EVIDENCE = {
    **{'lifecycle/' + name: 'wasm-lifecycle-evidence/' + name for name in
       ('qualification-status.json','quint-sampled-init.txt','quint-sampled-initReady.txt',
        'quint-sampled-init.itf.json','quint-sampled-initReady.itf.json')},
    **{'supervisor/' + name: 'wasm-supervisor-evidence/' + name for name in
       ('model-controls.log','receipt-mutation.log','model-cold.log','model-ready.log',
        'default-traces.log','selected-traces.log','source-build.log','source-dotnet.log',
        'source-fable-build.log','source-fable.log','installed-build.log','installed-locked-build.log',
        'installed-dotnet.log','installed-fable-build.log','installed-fable.log',
        'installed-raw-numeric.log','installed-lock-refusal.log','installed.packages.lock.json')},
    'official-core/receipt.json': 'wasm-supervisor-baseline/official-core/receipt.json',
    'official-core/executed-runtime-joins.json': 'wasm-supervisor-baseline/official-core/executed-runtime-joins.json',
    **{name: 'wasm-custody-logs/' + name for name in
       ('contracts.log','lifecycle.log','browser.log','package-consumer.log','supervisor.log')},
}
CUSTODY_METADATA = ('release-manifest.json','SHA256SUMS','api-baseline-comparison.json')
BASELINE_LOGS = tuple(ident + '.published-baseline.log' for ident in PACKAGES)
OUTER_FILES = set(FILES) | set(CUSTODY_METADATA) | set(BASELINE_LOGS) | {'preparation-binding.json'} | {'evidence/' + name for name in EVIDENCE}
STAGE_END = time.monotonic() + 600

class Refusal(ValueError): pass

def need(ok, reason):
    if not ok: raise Refusal(reason)

def digest(body): return hashlib.sha256(body).hexdigest()
def load(path): return json.loads(pathlib.Path(path).read_text())
def save(path, value): pathlib.Path(path).write_text(json.dumps(value, sort_keys=True, indent=2) + '\n')
def sha(value, length=64): need(isinstance(value, str) and re.fullmatch('[0-9a-f]{%d}' % length, value), 'invalid immutable digest')
def integer(value): need(type(value) is int and value > 0, 'invalid native numeric identity')
def command(args, **kwargs):
    need(time.monotonic() < STAGE_END, 'stage deadline expired')
    return subprocess.run(args, check=True, timeout=max(1, STAGE_END-time.monotonic()), **kwargs)
def manifest_tool():
    spec = importlib.util.spec_from_file_location('manifest', ROOT/'scripts/wasm-release/release_manifest.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module

# Immutable context used only by explicitly selected read-only validators.
PublicationSubject = namedtuple('PublicationSubject', 'producer tree version')


def publication_identity(subject):
    need(type(subject) is PublicationSubject, 'explicit publication subject required')
    sha(subject.producer, 40); sha(subject.tree, 40)
    need(subject.version == VERSION == '0.6.0', 'wrong published subject version')
    return subject.producer, subject.tree


def installed_subject():
    # The binder independently validates this exact whole-tree installed policy
    # before any restore; package checking repeats the local source identity audit.
    spec = importlib.util.spec_from_file_location('installed_binding', ROOT/'scripts/wasm-release/bind-installed-release.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    module.bind_qualification_inputs(os.environ['PUBLISHED_SOURCE'], os.environ['QUALIFIER_SOURCE'], VERSION)
    subject = PublicationSubject(os.environ['PUBLISHED_SOURCE'], git('rev-parse', os.environ['PUBLISHED_SOURCE']+'^{tree}'), VERSION)
    publication_identity(subject)
    return subject


def verify_originals(custody, expected):
    custody = pathlib.Path(custody)
    manifest = load(custody/'release-manifest.json')
    manifest_tool().verify_identity(manifest, P)
    need(manifest['tree'] == TREE and manifest['version'] == VERSION, 'wrong producer tree/version')
    rows = {row['file']: row for row in manifest['artifacts']}
    need(len(rows) == len(manifest['artifacts']) == 3 and set(rows) == set(FILES), 'wrong coherent archive roster')
    need(expected['manifestSha256'] == digest((custody/'release-manifest.json').read_bytes()), 'manifest digest drift')
    need(set(expected['archives']) == set(FILES), 'wrong reviewed archive roster')
    for name in FILES:
        sha(expected['archives'][name]); need(rows[name]['sha256'] == expected['archives'][name], 'manifest/archive binding mismatch')
        need(digest((custody/name).read_bytes()) == expected['archives'][name], 'frozen original drift')
    manifest_tool().verify_manifest(custody/'release-manifest.json', custody, P)
    need(expected['inventories'] == inventories(custody), 'complete frozen member inventory drift')
    return manifest

def validate_binding(binding, *, subject=None):
    producer, tree = (P, TREE) if subject is None else publication_identity(subject)
    fields = {'schema','repository','workflow','mode','producer','producerTree','executor','executorTree',
              'runId','runAttempt','version','manifestSha256','archives','inventories','gates',
              'workflowSha','job','event','evidence'}
    need(set(binding) == fields, 'unreviewed preparation fields')
    need(binding['schema'] == SCHEMA and binding['repository'] == REPO and binding['workflow'] == '.github/workflows/wasm-shared.yml' and binding['mode'] == 'qualify', 'unrelated preparation route')
    need(VERSION == '0.6.0' and binding['producer'] == binding['executor'] == binding['workflowSha'] == producer and binding['producerTree'] == binding['executorTree'] == tree and binding['version'] == VERSION, 'wrong selected producer/executor/tree/version')
    need(binding['job'] == 'qualify' and binding['event'] in ('push','workflow_dispatch'), 'premerge/generation-only custody refuses')
    need(binding['runAttempt'] == 1, 'rerun custody refuses')
    need(set(binding['evidence']) == OUTER_FILES - {'preparation-binding.json'} - set(FILES), 'wrong closed evidence roster')
    for value in binding['evidence'].values(): sha(value)
    sha(binding['executor'],40); sha(binding['executorTree'],40); integer(binding['runId']); integer(binding['runAttempt'])
    sha(binding['manifestSha256']); need(set(binding['archives']) == set(FILES), 'wrong preparation roster')
    for value in binding['archives'].values(): sha(value)
    need(set(binding['inventories'])==set(FILES), 'missing all-member inventories')
    for rows in binding['inventories'].values():
        need(type(rows) is dict and rows, 'empty/malformed inventory')
        for name,value in rows.items():
            need(type(name) is str and not name.startswith('/') and '..' not in pathlib.PurePosixPath(name).parts,'unsafe member')
            sha(value)
    need(binding['gates'] == {name:'success' for name in GATES}, 'missing/failed required preparation gate')
    return binding

def validate_tuple(reviewed, *, subject=None):
    need(set(reviewed) == {'binding','bindingSha256','artifactId','artifactSha256'}, 'unreviewed promotion inputs')
    validate_binding(reviewed['binding'], subject=subject); sha(reviewed['bindingSha256']); integer(reviewed['artifactId']); sha(reviewed['artifactSha256'])
    return reviewed

def validate_run(reviewed, run, jobs, artifact):
    b = reviewed['binding']
    need(run['head_sha'] == b['executor'] and run['path'] == b['workflow'] and run['event'] == b['event'] and run['head_branch'] == 'main', 'unrelated native preparation run')
    need(run['id'] == b['runId'] and run['run_attempt'] == b['runAttempt'] and run['status'] == 'completed' and run['conclusion'] == 'success', 'preparation failed/wrong attempt/incomplete')
    need(run['repository']['full_name'] == REPO, 'foreign run repository')
    prepared = [job for job in jobs if job['name'] == QUALIFY_JOB]
    need(len(prepared) == 1 and prepared[0]['conclusion'] == 'success' and prepared[0]['head_sha'] == b['producer'] and prepared[0]['run_id'] == b['runId'] and prepared[0]['run_attempt'] == b['runAttempt'], 'missing actual successful full qualify job')
    steps = {row['name']: row['conclusion'] for row in prepared[0]['steps']}
    need(len(steps)==len(prepared[0]['steps']), 'duplicate native gate names')
    need(all(steps.get(name) == 'success' for name in GATES) and steps.get('Retain eligible originals') == 'success', 'actual preparation gates absent/failed')
    need(artifact['id'] == reviewed['artifactId'] and not artifact['expired'] and artifact['digest'] == 'sha256:' + reviewed['artifactSha256'], 'wrong/expired native artifact')
    need(artifact['workflow_run']['id'] == b['runId'] and artifact['workflow_run']['head_sha'] == b['executor'], 'artifact unrelated to preparation')
    need(artifact['name'] == f"wasm-eligible-{b['runId']}-{b['runAttempt']}-{VERSION}", 'diagnostic artifact is ineligible')

class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, url):
        need(urllib.parse.urlsplit(url).scheme == 'https', 'non-HTTPS native redirect')
        redirected = super().redirect_request(request,fp,code,msg,headers,url)
        if urllib.parse.urlsplit(url).netloc != urllib.parse.urlsplit(request.full_url).netloc:
            redirected.remove_header('Authorization')
        return redirected

class Native:
    def __init__(self): self.end = STAGE_END
    def request(self, url, method='GET', data=None, github=False, binary=False, max_bytes=None):
        need(time.monotonic() < self.end, 'stage deadline expired')
        headers = {'Accept':'application/vnd.github+json' if github else 'application/json', 'Cache-Control':'no-cache'}
        if github:
            headers['Authorization'] = 'Bearer ' + os.environ['GH_TOKEN']
            headers['X-GitHub-Api-Version'] = '2022-11-28'
        if isinstance(data, bytes): headers['Content-Type'] = 'application/octet-stream'
        elif data is not None: data = json.dumps(data).encode(); headers['Content-Type'] = 'application/json'
        if binary and github:
            # Actions archive endpoints negotiate an API redirect, not an asset
            # representation. The final redirected body remains opaque bytes.
            parsed = urllib.parse.urlsplit(url)
            actions_archive = (method == 'GET' and parsed.scheme == 'https' and parsed.netloc == 'api.github.com'
                and re.fullmatch(r'/repos/' + re.escape(REPO) + r'/actions/(?:artifacts/[1-9][0-9]*/zip|jobs/[1-9][0-9]*/logs|runs/[1-9][0-9]*/(?:attempts/[1-9][0-9]*/)?logs)', parsed.path))
            headers['Accept'] = 'application/vnd.github+json' if actions_archive else 'application/octet-stream'
        try:
            with urllib.request.build_opener(SafeRedirect()).open(urllib.request.Request(url, data=data, headers=headers, method=method), timeout=min(30,max(1,self.end-time.monotonic()))) as r:
                chunks=[]; total=0
                while True:
                    need(time.monotonic() < self.end, 'native response stage deadline expired')
                    chunk=r.read(1048576)
                    if not chunk: break
                    total+=len(chunk); need(total <= (max_bytes if max_bytes is not None else 536870912 if binary else 8388608),'native response exceeds bounded size')
                    chunks.append(chunk)
                body=b''.join(chunks); receipt(url,method,r.status,dict(r.headers)); return r.status, body if binary else json.loads(body or b'null'), dict(r.headers)
        except urllib.error.HTTPError as e:
            receipt(url,method,e.code,dict(e.headers)); return e.code, None, dict(e.headers)
    def api(self, path, method='GET', data=None): return self.request('https://api.github.com/'+path,method,data,True)
    def checked(self,path,method='GET',data=None):
        status, body, _ = self.api(path,method,data); need(status in (200,201), f'native API {method} {path}: status {status}, Unknown'); return body
    def pages(self,path,key=None):
        rows=[]; page=1; seen=set(); total=None
        while True:
            need(page <= 100, 'pagination incomplete')
            value=self.checked(path + ('&' if '?' in path else '?') + f'per_page=100&page={page}')
            if key:
                need(type(value) is dict and type(value.get('total_count')) is int and value['total_count']>=0,'malformed pagination total')
                if total is None: total=value['total_count']
                need(value['total_count']==total,'moving pagination total')
            current=value[key] if key else value
            need(isinstance(current,list) and len(current)<=100, 'malformed paginated response')
            for row in current:
                need(type(row) is dict and type(row.get('id')) is int and row['id']>0, 'malformed native page identity')
                need(row['id'] not in seen, 'repeated/duplicate native page identity'); seen.add(row['id'])
            rows.extend(current)
            if len(current)<100:
                need(total is None or len(rows)==total,'incomplete enumeration'); return rows
            page+=1

def occupancy(native, reviewed, recovering=False):
    observed={}
    for ident in PACKAGES:
        for state in ('active','deleted'):
            rows=native.pages(f'orgs/FS-GG/packages/nuget/{ident.lower()}/versions?state={state}')
            need(all(type(x.get('name')) is str and type(x.get('id')) is int for x in rows), 'malformed package occupancy')
            matches=[x for x in rows if x['name']==VERSION]
            need(len(matches)<=1, 'ambiguous package version occupancy')
            if state=='deleted': need(not matches, 'deleted occupied version refuses')
            observed[ident+':'+state]=bool(matches)
    status, service, _=native.request('https://api.nuget.org/v3/index.json')
    need(status==200 and type(service) is dict and type(service.get('resources')) is list,'public service index Unknown')
    bases={row.get('@id') for row in service['resources'] if row.get('@type')=='PackageBaseAddress/3.0.0'}
    need(len(bases)==1,'public base-address resource missing/ambiguous')
    base=bases.pop()
    need(base=='https://api.nuget.org/v3-flatcontainer/','unreviewed public package base address')
    for ident in PACKAGES:
        status,index,_=native.request(base+ident.lower()+'/index.json')
        need(status==200 and type(index) is dict and set(index)=={'versions'} and type(index['versions']) is list,'public version index Unknown')
        versions=index['versions']
        need(versions and all(type(x) is str and x==x.lower() and re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9a-z.-]+)?',x) for x in versions) and len(set(versions))==len(versions),'malformed public version index')
        status,_,_=native.request(base+f'{ident.lower()}/{VERSION}/{ident.lower()}.{VERSION}.nupkg',method='HEAD',binary=True)
        need(status==(200 if VERSION in versions else 404),'public index/exact-endpoint contradictory or Unknown')
        observed[ident+':public']=VERSION in versions
    if not recovering: need(not any(observed.values()),'fresh promotion currently published version occupied')
    # Ordinary NuGet unlisting retains both index and exact downloads. Exceptional
    # moderator removal/history and atomic reservation remain Unknown; an actual
    # writer rejection enters incomplete same-custody recovery, never skip-duplicate.
    return observed


OCCUPANCY_KEYS = {ident+':'+state for ident in PACKAGES for state in ('active','deleted','public')}
def receipt(url,method,status,headers):
    # Credential-free request identity/status only; redirects/query credentials
    # and complete headers are never retained.
    if os.environ.get('WASM_API_RECEIPTS'):
        clean=urllib.parse.urlsplit(url)
        value={'method':method,'host':clean.netloc,'path':clean.path,'status':status,
               'headers':{k.lower():v for k,v in headers.items() if k.lower() in ('x-github-request-id','x-ratelimit-remaining','retry-after')}}
        with open(os.environ['WASM_API_RECEIPTS'],'a') as f: f.write(json.dumps(value,sort_keys=True)+'\n')


def execution_input():
    need(not os.environ.get('EXECUTION_BINDING'), 'historical executor overlay is sealed')
    return None


def git(*args):
    need(time.monotonic()<STAGE_END,'stage deadline expired')
    return subprocess.check_output(['git','-C',str(ROOT),*args],text=True,stderr=subprocess.PIPE,timeout=min(30,max(1,STAGE_END-time.monotonic()))).strip()


def fingerprint(revision,path):
    blob=git('rev-parse',revision+':'+path)
    body=subprocess.check_output(['git','-C',str(ROOT),'show',revision+':'+path],timeout=min(30,max(1,STAGE_END-time.monotonic())))
    return {'blob':blob,'sha256':digest(body)}


def execution_checkout(native,reviewed,installed=False):
    need(execution_input() is None, 'historical executor overlays cannot authorize current release')
    b=validate_tuple(reviewed)['binding']
    need(os.environ['GITHUB_REPOSITORY']==REPO and os.environ['GITHUB_REF']=='refs/heads/main' and os.environ['GITHUB_RUN_ATTEMPT']=='1', 'foreign/rerun current execution')
    executor_checkout(b)
    need(native.checked(f'repos/{REPO}/git/ref/heads/main')['object']['sha']==b['executor'], 'protected producer moved')


def executor_checkout(binding):
    def git(*args): return subprocess.check_output(['git','-C',str(ROOT),*args],text=True).strip()
    need(git('rev-parse','HEAD')==binding['executor']==os.environ['GITHUB_SHA']==os.environ['ACCEPTED_EXECUTOR'],'wrong executor checkout')
    need(git('rev-parse','HEAD^{tree}')==binding['executorTree'] and not git('status','--porcelain'),'dirty or different executor tree')


# Share the maintained Game required-check contract with the ordinary native
# delivery observer; live protection must still agree before publication.
def required_checks():
    policy=load(ROOT/'policy/v2-ci-ordinary-settlement.json')['qualification']
    return tuple((name,policy['requiredCheckAppId']) for name in policy['requiredGateChecks'])


def job_log_lines(log):
    need(len(log)<=16*1024*1024, 'checkout job log exceeds bound')
    text=log.decode('utf-8-sig',errors='strict')
    text=re.sub(r'\x1b\[[0-?]*[ -/]*[@-~]', '', text)
    return [re.sub(r'^\d{4}-\d\d-\d\dT\S+Z ', '', line) for line in text.splitlines()]


def metadata_head(log,head,expected):
    # This exact existing producer validates PR metadata and does not check out
    # source. Its explicit runner environment binds the subject, not tested code.
    need(expected=={'workflowId':316870217,'path':'.github/workflows/kit-materialize.yml','event':'pull_request'}, 'metadata-only producer contract changed')
    lines=job_log_lines(log)
    heads=[line.removeprefix('  PR_HEAD: ') for line in lines if line.startswith('  PR_HEAD: ')]
    need(heads==[head], 'metadata-only native PR_HEAD missing/mismatched/ambiguous')
    need('  SELF_REPO: '+REPO in lines, 'metadata-only repository differs')


CHECKOUT_SHA = '3d3c42e5aac5ba805825da76410c181273ba90b1'


def checkout_commit(log):
    """Join runner download resolution to the Game checkout's own git-log result."""
    lines=job_log_lines(log)
    # Only runner setup can resolve a mutable action ref. A later script echo
    # cannot supply the missing resolution for an executed checkout step.
    setup_end=next((i for i,line in enumerate(lines) if line.startswith('##[group]Run ')),len(lines))
    resolutions={}
    for line in lines[:setup_end]:
        match=re.fullmatch(r"Download action repository 'actions/checkout@([^']+)' \(SHA:([0-9a-f]{40})\)",line)
        if match:resolutions.setdefault(match[1],[]).append(match[2])
    starts=[(i,line.removeprefix('##[group]Run actions/checkout@')) for i,line in enumerate(lines) if line.startswith('##[group]Run actions/checkout@')]
    selected=[]
    for start,ref in starts:
        end=next((i for i in range(start+1,len(lines)) if lines[i].startswith('##[group]Run ')),len(lines))
        block=lines[start:end]
        repositories=[line.removeprefix('Syncing repository: ') for line in block if line.startswith('Syncing repository: ')]
        if REPO not in repositories:continue
        need(repositories==[REPO], 'ambiguous checkout repository')
        header_end=next((i for i,line in enumerate(block) if line=='##[endgroup]'),len(block))
        declared=[line.removeprefix('  repository: ') for line in block[:header_end] if line.startswith('  repository: ')]
        need(declared==[REPO], 'checkout declared/synced repository mismatch')
        need(ref in (CHECKOUT_SHA,'v7') and resolutions.get(ref)==[CHECKOUT_SHA], 'checkout ref lacks exact accepted native download resolution')
        positions=[i for i,line in enumerate(block) if re.fullmatch(r'\[command\].*(?:/|\\)git(?:\.exe)?"? log -1 --format=%H',line)]
        need(len(positions)==1 and positions[0]+1<len(block), 'checkout commit missing/ambiguous')
        commit=block[positions[0]+1];sha(commit,40);selected.append(commit)
    need(len(selected)==1, 'actual selected repository checkout missing/ambiguous')
    return selected[0]


def verify_protection(native,binding):
    branch=native.checked(f'repos/{REPO}/branches/main')
    need(branch['protected'] is True, 'main protection unavailable')
    protection=branch['protection']['required_status_checks'];required=required_checks()
    checks=protection['checks']
    need(type(checks) is list and len(checks)==19 and {(row['context'],row['app_id']) for row in checks}==set(required), 'selected required protection checks changed/Unknown')
    need(set(protection['contexts'])=={name for name,_ in required}, 'required context projection drift')
    # Reuse the existing Game native check-producer policy, but admit only an
    # exact checkout tree. The ordinary observer's broader three-way semantic
    # merge path does not authorize this selected publication window.
    policy=load(ROOT/'policy/v2-ci-ordinary-settlement.json')['qualification']
    pulls=native.pages(f'repos/{REPO}/commits/{binding["producer"]}/pulls')
    need(len(pulls)==1, 'current producer needs unique native merged PR')
    pull=native.checked(f'repos/{REPO}/pulls/{pulls[0]["number"]}')
    need(all(pull[key]==pulls[0][key] for key in ('number','node_id','merged_at','merge_commit_sha')), 'native merged PR association drift')
    need(pull['state']=='closed' and pull['merged_at'] and pull['merge_commit_sha']==binding['producer'] and pull['base']['ref']=='main' and pull['base']['repo']['full_name']==REPO and pull['head']['repo']['full_name']==REPO, 'foreign/unmerged producer association')
    head=pull['head']['sha'];sha(head,40)
    trees={}
    def exact_tree(commit):
        if commit not in trees:
            value=native.checked(f'repos/{REPO}/git/commits/{commit}')
            need(value['sha']==commit and value['tree']['sha']==binding['producerTree'], 'tested checkout tree differs from protected custody producer')
            trees[commit]=value['tree']['sha']
        return trees[commit]
    exact_tree(binding['producer']);exact_tree(head)
    rows=native.pages(f'repos/{REPO}/commits/{head}/check-runs?filter=latest','check_runs')
    runs={};log_bytes=0
    for name,app in required:
        matches=[row for row in rows if row['name']==name]
        need(len(matches)==1, 'required check absent/ambiguous: '+name)
        check=matches[0]
        need(check['app']['id']==app and check['head_sha']==head and check['status']=='completed' and check['conclusion']=='success', 'required native check failed/foreign: '+name)
        match=re.fullmatch(r'https://github\.com/'+re.escape(REPO)+r'/actions/runs/([1-9][0-9]*)/job/([1-9][0-9]*)',check['details_url'])
        need(match is not None, 'required check lacks native Actions job: '+name)
        run_id,job_id=map(int,match.groups());need(job_id==check['id'],'check/job identity differs')
        if run_id not in runs:runs[run_id]=native.checked(f'repos/{REPO}/actions/runs/{run_id}')
        run=runs[run_id];job=native.checked(f'repos/{REPO}/actions/jobs/{job_id}');expected=policy['checkProducers'][name]
        need(run['id']==run_id and run['repository']['full_name']==REPO and run['workflow_id']==expected['workflowId'] and run['path']==expected['path'] and run['event']==expected['event'] and run['head_sha']==head and run['check_suite_id']==check['check_suite']['id'], 'required check producer differs: '+name)
        need(job['id']==job_id and job['run_id']==run_id and job['head_sha']==head and job['name']==name and job['status']==check['status'] and job['conclusion']==check['conclusion'] and job['check_run_url']==f'https://api.github.com/repos/{REPO}/check-runs/{job_id}' and type(job['run_attempt']) is int and type(run['run_attempt']) is int and 1<=job['run_attempt']==run['run_attempt'], 'required native job/attempt differs: '+name)
        status,body,_=native.request(f'https://api.github.com/repos/{REPO}/actions/jobs/{job_id}/logs',github=True,binary=True,max_bytes=min(16*1024*1024,64*1024*1024-log_bytes))
        need(status==200, 'required checkout logs unavailable: '+name)
        log_bytes+=len(body);need(log_bytes<=64*1024*1024, 'required checkout logs exceed total bound')
        if name=='materialize / receiver-validate':metadata_head(body,head,expected)
        else:exact_tree(checkout_commit(body))


def download(native, reviewed, target):
    validate_tuple(reviewed); b=reviewed['binding']; execution_checkout(native,reviewed)
    run=native.checked(f'repos/{REPO}/actions/runs/{b["runId"]}/attempts/{b["runAttempt"]}')
    jobs=native.pages(f'repos/{REPO}/actions/runs/{b["runId"]}/attempts/{b["runAttempt"]}/jobs', 'jobs')
    artifact=native.checked(f'repos/{REPO}/actions/artifacts/{reviewed["artifactId"]}')
    validate_run(reviewed,run,jobs,artifact)
    verify_protection(native,b)
    status,body,_=native.request(f'https://api.github.com/repos/{REPO}/actions/artifacts/{reviewed["artifactId"]}/zip',github=True,binary=True)
    need(status==200, f'preparation artifact download status {status}, Unknown')
    need(digest(body)==reviewed['artifactSha256'],'downloaded artifact digest mismatch')
    target=pathlib.Path(target); need(not target.exists(),'custody output must be fresh'); target.mkdir(mode=0o700)
    import io
    with zipfile.ZipFile(io.BytesIO(body)) as z:
        names=z.namelist()
        need(len(names)==len(set(names)) and set(names)==OUTER_FILES, 'wrong closed custody archive roster')
        need(len(body)<=MAX_TOTAL and sum(info.file_size for info in z.infolist())<=MAX_TOTAL, 'custody archive total budget exceeded')
        for info in z.infolist():
            name=info.filename
            need(not info.is_dir() and not pathlib.PurePosixPath(name).is_absolute() and '..' not in pathlib.PurePosixPath(name).parts and (info.external_attr>>16)&0o170000 in (0,stat.S_IFREG), 'unsafe custody member')
            limit=MAX_ARCHIVE if name in FILES else MAX_METADATA if name=='preparation-binding.json' or name in CUSTODY_METADATA or name.endswith('.json') else MAX_DIAGNOSTICS
            need(info.file_size<=limit, 'custody member budget exceeded')
        diagnostics=sum(info.file_size for info in z.infolist() if info.filename not in FILES and info.filename!='preparation-binding.json')
        metadata=sum(info.file_size for info in z.infolist() if info.filename=='preparation-binding.json' or info.filename in CUSTODY_METADATA or info.filename.endswith('.json'))
        need(diagnostics<=MAX_DIAGNOSTICS and metadata<=MAX_METADATA, 'custody evidence budget exceeded')
        for info in z.infolist():
            path=target/info.filename; path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(z.read(info))
    need(digest((target/'preparation-binding.json').read_bytes())==reviewed['bindingSha256'],'preparation binding digest drift')
    need(load(target/'preparation-binding.json')==b,'actual preparation tuple drift')
    need(all(digest((target/name).read_bytes())==value for name,value in b['evidence'].items()), 'custody evidence digest drift')
    verify_originals(target,b)

def transaction(reviewed, first_run):
    integer(first_run)
    return {'schema':'fsgg.wasm.promotion/v1','preparation':reviewed,'firstPromotionRun':first_run,'firstPromotionAttempt':int(os.environ.get('FIRST_PROMOTION_ATTEMPT') or os.environ['GITHUB_RUN_ATTEMPT']),'producer':P,'tag':'wasm/v'+VERSION}

def release_state(native, *, subject=None):
    producer = P if subject is None else publication_identity(subject)[0]
    ref_status,ref,_=native.api(f'repos/{REPO}/git/ref/tags/wasm%2Fv{VERSION}')
    release_status,published,_=native.api(f'repos/{REPO}/releases/tags/wasm%2Fv{VERSION}')
    need(ref_status in (200,404) and release_status in (200,404),'tag/release Unknown')
    if ref_status==200: need(ref['object']['type']=='commit' and ref['object']['sha']==producer,'foreign or nonliteral producer tag')
    # Complete authenticated discovery is necessary even when by-tag returns404:
    # GitHub's by-tag API addresses published releases, not bound drafts.
    rows=native.pages(f'repos/{REPO}/releases')
    need(all(type(x.get('tag_name')) is str for x in rows), 'malformed release list')
    matches=[x for x in rows if x['tag_name']=='wasm/v'+VERSION]
    need(len(matches)<=1, 'duplicate selected release')
    release=matches[0] if matches else None
    execution_input()
    if release is not None:
        actual=native.checked(f'repos/{REPO}/releases/{release["id"]}')
        need(actual==release, 'release list/by-ID disagreement')
        assets=native.pages(f'repos/{REPO}/releases/{release["id"]}/assets')
        need(assets==release['assets'], 'release asset enumeration disagreement')
        need(ref_status==200 and release['tag_name']=='wasm/v'+VERSION and release['target_commitish']==producer and not release['prerelease'] and type(release['draft']) is bool, 'foreign release')
        need((release_status==404 and release['draft']) or (release_status==200 and not release['draft'] and published==release), 'published/list contradiction')
    else: need(release_status==404, 'published release hidden in listing')
    return ref_status,release


def asset_bytes(native, row):
    integer(row['id']); need(row['url']==f'https://api.github.com/repos/{REPO}/releases/assets/{row["id"]}', 'foreign asset API identity')
    need(type(row.get('size')) is int and 0<=row['size']<=536870912, 'unbounded asset')
    status,body,_=native.request(row['url'],github=True,binary=True)
    need(status==200 and len(body)==row['size'] and row.get('digest')=='sha256:'+digest(body), 'asset bytes/digest Unknown')
    return body

def verify_transaction(native, release, reviewed, *, subject=None):
    producer = P if subject is None else publication_identity(subject)[0]
    validate_tuple(reviewed, subject=subject)
    assets=[x for x in release['assets'] if x['name']=='promotion-binding.json']
    need(len(assets)==1,'missing/duplicate durable transaction binding')
    row=assets[0]
    body=asset_bytes(native,row); value=json.loads(body)
    execution_input()
    need(set(value)=={'schema','preparation','firstPromotionRun','firstPromotionAttempt','producer','tag'} and value['schema']=='fsgg.wasm.promotion/v1' and value['preparation']==reviewed and value['producer']==producer and value['tag']=='wasm/v'+VERSION,'foreign durable release transaction')
    integer(value['firstPromotionRun']); integer(value['firstPromotionAttempt'])
    verify_admission(native,reviewed,value['firstPromotionRun'],value['firstPromotionAttempt'])
    run=native.checked(f'repos/{REPO}/actions/runs/{value["firstPromotionRun"]}/attempts/{value["firstPromotionAttempt"]}')
    need(run['head_sha']==reviewed['binding']['executor'] and run['event']=='workflow_dispatch' and run['path']=='.github/workflows/release-wasm.yml','foreign first promotion run')
    return value

def verify_admission(native,reviewed,run_id,attempt):
    run=native.checked(f'repos/{REPO}/actions/runs/{run_id}/attempts/{attempt}')
    need(run['head_sha']==reviewed['binding']['executor'] and run['event']=='workflow_dispatch' and run['path']=='.github/workflows/release-wasm.yml','foreign original admission run')
    jobs=native.pages(f'repos/{REPO}/actions/runs/{run_id}/attempts/{attempt}/jobs','jobs')
    need(run['id']==run_id and run['run_attempt']==attempt and run['repository']['full_name']==REPO,'wrong original native attempt')
    admitted=[j for j in jobs if j['name']=='admission']
    need(len(admitted)==1 and admitted[0]['conclusion']=='success','original admission was not successful')
    artifacts=native.pages(f'repos/{REPO}/actions/runs/{run_id}/artifacts','artifacts')
    matching=[a for a in artifacts if a['name']==f'wasm-admission-{run_id}-{attempt}']
    need(len(matching)==1 and not matching[0]['expired'],'original admission evidence absent/expired')
    a=matching[0]
    need(a['workflow_run']['id']==run_id and a['workflow_run']['head_sha']==reviewed['binding']['executor'],'foreign admission artifact')
    status,body,_=native.request(f'https://api.github.com/repos/{REPO}/actions/artifacts/{a["id"]}/zip',github=True,binary=True)
    need(status==200, f'original admission artifact download status {status}, Unknown')
    need(a['digest']=='sha256:'+digest(body),'admission artifact digest mismatch')
    import io
    with zipfile.ZipFile(io.BytesIO(body)) as z:
        need(z.namelist()==['admission-binding.json'],'foreign admission artifact members')
        record=json.loads(z.read('admission-binding.json'))
    need(set(record)=={'schema','reviewed','mode','run','attempt','exchangeVerified','occupancy','observedAt','nugetAccountSha256','publicModeratorRemovalHistory','atomicReservation'} and record['schema']=='fsgg.wasm.admission/v1','unreviewed original admission record')
    need(set(record['occupancy'])==OCCUPANCY_KEYS and all(v is False for v in record['occupancy'].values()) and record['publicModeratorRemovalHistory']==record['atomicReservation']=='Unknown','original fresh admission did not prove observable absence')
    need(record['nugetAccountSha256']==digest(os.environ['NUGET_ACCOUNT'].encode()),'native account differs from original exchange')
    need(record['reviewed']==reviewed and record['mode']=='promote' and record['run']==run_id and record['attempt']==attempt and record['exchangeVerified'] is True,'original admission not bound to this custody')


def interrupted_begin(native,reviewed,release=None):
    first=int(os.environ.get('FIRST_PROMOTION_RUN') or '0')
    attempt=int(os.environ.get('FIRST_PROMOTION_ATTEMPT') or '0')
    integer(first); integer(attempt)
    need((first,attempt)!=(int(os.environ['GITHUB_RUN_ID']),int(os.environ['GITHUB_RUN_ATTEMPT'])),'interrupted recovery needs original native attempt')
    verify_admission(native,reviewed,first,attempt)
    jobs=native.pages(f'repos/{REPO}/actions/runs/{first}/attempts/{attempt}/jobs','jobs')
    candidates=[j for j in jobs if j['name']=='begin' and any(s['name']=='Create bound draft' and s['conclusion'] in ('failure','success') for s in j['steps'])]
    need(len(candidates)==1,'interrupted recovery lacks actual original begin stage')
    if release is not None:
        job=candidates[0]
        need(release['draft'] and release['target_commitish']==P and release['author']['login']=='github-actions[bot]','foreign or completed unbound draft')
        parse=lambda x:dt.datetime.fromisoformat(x.replace('Z','+00:00'))
        need(job.get('started_at') and job.get('completed_at') and parse(job['started_at']) <= parse(release['created_at']) <= parse(job['completed_at']),'draft not created during original native begin job')
    return first,attempt


def readback_occupied(observed,custody,output,feeds=('org','public')):
    # Recovery may only recognize occupancy after actual same-original payload
    # readback, including repository-signature verification on the public feed.
    for feed in feeds:
        for ident in PACKAGES:
            key=ident+(':active' if feed=='org' else ':public')
            if observed[key]:
                command([str(ROOT/'scripts/wasm-release/readback.sh'),feed,str(custody),str(pathlib.Path(output)/feed/ident),ident],
                        env=dict(os.environ,WASM_READBACK_DEADLINE=str(int(time.time()+max(0,STAGE_END-time.monotonic())))))


def begin(native,reviewed,custody,recovery):
    need(os.environ.get('TAG_CREDENTIAL_ROUTE')=='workflow-github-token','PAT/App/manual tag path refuses')
    verify_originals(custody,reviewed['binding'])
    execution_input()
    ref,release=release_state(native)
    observed=occupancy(native,reviewed,recovery or release is not None)
    if recovery or release is not None:
        readback_occupied(observed,custody,pathlib.Path(os.environ['RUNNER_TEMP'])/'begin-occupied-readback')
    if release:
        rows=[a for a in release['assets'] if a['name']=='promotion-binding.json']
        if not rows:
            need(recovery,'unbound draft requires explicit interrupted recovery')
            first,attempt=interrupted_begin(native,reviewed,release)
            value=transaction(reviewed,first); value['firstPromotionAttempt']=attempt
            path=pathlib.Path(custody)/'promotion-binding.json'; save(path,value)
            command(['gh','release','upload','wasm/v'+VERSION,str(path),'--repo',REPO])
            verify_transaction(native,native.checked(f'repos/{REPO}/releases/{release["id"]}'),reviewed)
        else:
            verify_transaction(native,release,reviewed)
            need(recovery or not release['draft'],'existing draft requires explicit recovery')
        return
    need(ref==404 or recovery,'preexisting tag requires causally bound recovery')
    first=int(os.environ.get('FIRST_PROMOTION_RUN') or os.environ['GITHUB_RUN_ID'])
    if ref==200: first,_=interrupted_begin(native,reviewed)
    if ref==404: native.checked(f'repos/{REPO}/git/refs','POST',{'ref':'refs/tags/wasm/v'+VERSION,'sha':P})
    release=native.checked(f'repos/{REPO}/releases','POST',{'tag_name':'wasm/v'+VERSION,'target_commitish':P,'name':'Shared WASM '+VERSION,'draft':True,'prerelease':False,'body':'Same-custody promotion incomplete until exact readback and installed acceptance.'})
    path=pathlib.Path(custody)/'promotion-binding.json'; save(path,transaction(reviewed,first))
    command(['gh','release','upload','wasm/v'+VERSION,str(path),'--repo',REPO])
    verify_transaction(native,native.checked(f'repos/{REPO}/releases/{release["id"]}'),reviewed)

def repository_signature(archive):
    # Payload allowance is applied ONLY after NuGet itself verifies the genuine
    # repository signature/trust chain and identifies the nuget.org repository.
    result=subprocess.run(['dotnet','nuget','verify',str(archive),'--all','--verbosity','detailed'],check=True,timeout=120,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    need('Signature type: Repository' in result.stdout and 'https://api.nuget.org/v3/index.json' in result.stdout,'not a verified nuget.org repository signature')
    return result.stdout

def readback(kind,custody,output):
    command([str(ROOT/'scripts/wasm-release/readback.sh'),kind,str(custody),str(output)], env=dict(os.environ, WASM_READBACK_DEADLINE=str(int(time.time()+max(0,STAGE_END-time.monotonic())))))

def perform(native,kind,reviewed,custody,output):
    verify_originals(custody,reviewed['binding'])
    execution_input()
    _,release=release_state(native); need(release is not None,'no durable transaction before writer')
    verify_transaction(native,release,reviewed)
    if kind=='readback':
        for feed in ('org','public','assets'): readback(feed,custody,pathlib.Path(output)/feed)
        return
    if not release['draft']:
        for feed in ('org','public','assets'): readback(feed,custody,pathlib.Path(output)/feed)
        return # completed exact transaction is an effect-free readback
    if kind in ('org','public'):
        if kind=='public': readback('org',custody,pathlib.Path(output)/'org-before-public')
        occupied=occupancy(native,reviewed,True)
        # Verify EVERY occupied member before the first missing-member writer.
        # A foreign second sibling cannot permit a first-sibling partial push.
        readback_occupied(occupied,custody,output,(kind,))
        for ident in PACKAGES:
            key=ident+(':active' if kind=='org' else ':public')
            original=pathlib.Path(custody)/(ident+'.'+VERSION+'.nupkg')
            verify_originals(custody,reviewed['binding'])
            if not occupied[key]:
                token=os.environ['FEED_TOKEN'] if kind=='org' else os.environ['NUGET_API_KEY']
                url='https://nuget.pkg.github.com/FS-GG/index.json' if kind=='org' else 'https://api.nuget.org/v3/index.json'
                # Token supplied to dotnet only in its child environment? dotnet
                # push requires --api-key; never print the command or its token.
                command(['dotnet','nuget','push',str(original),'--source',url,'--api-key',token,'--no-symbols'],stdout=subprocess.DEVNULL)
        readback(kind,custody,pathlib.Path(output)/kind)
    elif kind=='assets':
        readback('org',custody,pathlib.Path(output)/'org'); readback('public',custody,pathlib.Path(output)/'public')
        command([str(ROOT/'scripts/wasm-release/stage-assets.sh'),str(custody)])
        readback('assets',custody,pathlib.Path(output)/'assets')
    elif kind=='complete':
        for feed in ('org','public','assets'): readback(feed,custody,pathlib.Path(output)/feed)
        native.checked(f'repos/{REPO}/releases/{release["id"]}','PATCH',{'draft':False})
    else: raise Refusal('unknown finite transaction stage')


def release_assets(native,reviewed,custody,output,write=False):
    verify_originals(custody,reviewed['binding'])
    execution_input()
    _,release=release_state(native); need(release is not None,'release assets Unknown')
    verify_transaction(native,release,reviewed)
    rows=release['assets']; need(len({r['name'] for r in rows})==len(rows),'duplicate release assets')
    output=pathlib.Path(output);output.mkdir(parents=True,exist_ok=True)
    for name in ('fsgg-wasm-sdk-'+VERSION+'.tar.gz','release-manifest.json','SHA256SUMS'):
        matches=[a for a in rows if a['name']==name]; original=(pathlib.Path(custody)/name).read_bytes()
        if matches:
            served=asset_bytes(native,matches[0]); need(served==original,'existing release asset differs');(output/name).write_bytes(served)
        else:
            need(write and release['draft'],'missing immutable release asset')
            url=f'https://uploads.github.com/repos/{REPO}/releases/{release["id"]}/assets?name='+urllib.parse.quote(name)
            status,row,_=native.request(url,method='POST',data=original,github=True)
            need(status==201 and row['name']==name,'asset upload Unknown')
            served=asset_bytes(native,row);need(served==original,'uploaded asset differs');(output/name).write_bytes(served)


def custody_plan():
    need(len(OUTER_FILES)<=64 and len(OUTER_FILES)==len(FILES)+len(CUSTODY_METADATA)+len(BASELINE_LOGS)+len(EVIDENCE)+1, 'custody roster collision/file budget')
    print(f'wasm-custody-plan: files={len(OUTER_FILES)} total<=512MiB originals<=256MiB each diagnostics<=32MiB metadata<=1MiB; actual sizes required at freeze')


def freeze(custody,output):
    custody_plan()
    need(VERSION=='0.6.0', 'only selected current release can freeze')
    need(os.environ['GITHUB_REPOSITORY']==REPO and os.environ['GITHUB_REF']=='refs/heads/main' and os.environ['GITHUB_SHA']==os.environ['GITHUB_WORKFLOW_SHA']==P, 'premerge/source/workflow substitution')
    event=os.environ['GITHUB_EVENT_NAME']
    need(event=='push' or (event=='workflow_dispatch' and os.environ.get('WASM_QUALIFY_EXPECTED_HEAD')==P), 'qualification dispatch needs selected protected head')
    need(os.environ['GITHUB_RUN_ATTEMPT']=='1', 'rerun custody refuses')
    need(not git('status','--porcelain'), 'dirty qualification source')
    custody=pathlib.Path(custody); output=pathlib.Path(output)
    need(not output.exists() and not output.is_symlink() and not output.resolve().is_relative_to(ROOT.resolve()), 'eligible output must be fresh and outside source')
    selected={name:custody/name for name in (*FILES,*CUSTODY_METADATA,*BASELINE_LOGS)}
    selected.update({'evidence/'+name:pathlib.Path(os.environ['RUNNER_TEMP'])/relative for name,relative in EVIDENCE.items()})
    # Validate the complete input roster before copying anything. Symlinks in any
    # ancestor cannot import a foreign cache/workspace under a selected leaf.
    for name,path in selected.items():
        need(path.is_file() and not any(parent.is_symlink() for parent in (path,*path.parents)), 'missing/unsafe mandatory evidence: '+name)
    sizes={name:path.stat().st_size for name,path in selected.items()}
    need(all(sizes[name]<=MAX_ARCHIVE for name in FILES), 'original archive budget exceeded')
    need(sum(size for name,size in sizes.items() if name not in FILES)<=MAX_DIAGNOSTICS, 'diagnostics budget exceeded')
    need(sum(size for name,size in sizes.items() if name in CUSTODY_METADATA or name.endswith('.json'))<=MAX_METADATA, 'metadata budget exceeded')
    manifest=load(custody/'release-manifest.json')
    evidence={name:digest(path.read_bytes()) for name,path in selected.items() if name not in FILES}
    b={'schema':SCHEMA,'repository':REPO,'workflow':'.github/workflows/wasm-shared.yml','workflowSha':P,'job':'qualify','event':event,
       'mode':'qualify','producer':P,'producerTree':TREE,'executor':P,'executorTree':TREE,'runId':int(os.environ['GITHUB_RUN_ID']),
       'runAttempt':int(os.environ['GITHUB_RUN_ATTEMPT']),'version':VERSION,'manifestSha256':digest((custody/'release-manifest.json').read_bytes()),
       'archives':{row['file']:row['sha256'] for row in manifest['artifacts']},'inventories':inventories(custody),
       'gates':{name:'success' for name in GATES},'evidence':evidence}
    validate_binding(b); verify_originals(custody,b)
    binding_bytes=(json.dumps(b,sort_keys=True,indent=2)+'\n').encode()
    sizes={name:path.stat().st_size for name,path in selected.items()}
    need(all(sizes[name]<=MAX_ARCHIVE for name in FILES), 'original archive budget exceeded')
    need(sum(sizes[name] for name in evidence)<=MAX_DIAGNOSTICS, 'diagnostics budget exceeded')
    metadata=sum(size for name,size in sizes.items() if name in CUSTODY_METADATA or name.endswith('.json'))+len(binding_bytes)
    need(metadata<=MAX_METADATA and sum(sizes.values())+len(binding_bytes)<=MAX_TOTAL, 'metadata/total custody budget exceeded')
    output.mkdir(mode=0o700)
    for name,path in selected.items():
        destination=output/name; destination.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(path,destination)
        need(digest(destination.read_bytes())==(b['archives'][name] if name in FILES else evidence[name]), 'custody changed during retention')
    (output/'preparation-binding.json').write_bytes(binding_bytes)
    print(f'wasm-custody-size: files={len(OUTER_FILES)} totalBytes={sum(sizes.values())+len(binding_bytes)} diagnosticBytes={sum(sizes[name] for name in evidence)} metadataBytes={metadata}')
    with open(os.environ['GITHUB_OUTPUT'],'a') as f: f.write('binding_sha256='+digest(binding_bytes)+'\nversion='+VERSION+'\n')

def validate_feed_config(path,feed):
    import xml.etree.ElementTree as ET
    need(feed in ('https://api.nuget.org/v3/index.json','https://nuget.pkg.github.com/FS-GG/index.json'),'feed-only requires literal native feed')
    root=ET.parse(path).getroot(); sources=root.find('packageSources'); need(sources.find('clear') is not None,'implicit fallback source')
    rows={r.attrib['key']:r.attrib['value'] for r in sources.findall('add')}
    need(len(rows)==len(sources.findall('add')) and rows.get('wasm')==feed,'duplicate or wrong feed')
    need(set(rows)<= {'wasm','dependencies'} and (len(rows)==1 or rows['dependencies']=='https://api.nuget.org/v3/index.json'),'local/fallback source injection')
    maps={r.attrib['key']:{p.attrib['pattern'] for p in r} for r in root.find('packageSourceMapping')}
    need(set(rows)==set(maps) and maps['wasm']==({'FS.GG.Wasm.*','FSharp.Core'} if len(rows)==1 else {'FS.GG.Wasm.*'}),'package mapping injection')
    if len(rows)==2: need(maps['dependencies']=={'FSharp.Core'},'WASM fallback to dependency feed')

def inventories(custody):
    import tarfile
    result={}
    for name in FILES:
        path=pathlib.Path(custody)/name
        if name.endswith('.nupkg'):
            with zipfile.ZipFile(path) as z:
                manifest_tool().safe_members(z.namelist(),name)
                result[name]={n:digest(z.read(n)) for n in z.namelist()}
        else:
            with tarfile.open(path) as t:
                members=t.getmembers(); manifest_tool().safe_members([m.name for m in members],name)
                need(all(m.isfile() or m.isdir() for m in members),'unsafe SDK member type')
                result[name]={m.name:digest(t.extractfile(m).read()) if m.isfile() else digest(b'') for m in members}
    return result

def package_content_hash(raw):
    """SDK 10.0.401 NuGet 7.9.0 compressed unsigned-content SHA512.

    Data-only projection of SignedPackageArchiveUtility.GetPackageContentHash
    at dotnet/dotnet e34a38d2ae1fc26406a317517196e55c68ff83ab. Remove the
    signature's physical span/CD record, relocate surviving local offsets,
    adjust EOCD counts/size/offset, and hash original compressed bytes. This
    does not verify a signature. Support a bounded classic single-disk ZIP;
    ZIP64, encryption, duplicate/path/layout ambiguity fail closed.
    """
    need(isinstance(raw, bytes) and 22 <= len(raw) <= 536870912, 'unbounded/malformed package archive')
    end = raw.rfind(b'PK\x05\x06', max(0, len(raw)-65557))
    need(end >= 0 and end+22 <= len(raw), 'missing package EOCD')
    disk, cdisk, count_disk, count, cd_size, cd_start, comment = struct.unpack_from('<HHHHIIH', raw, end+4)
    need(disk == cdisk == 0 and count_disk == count and 0 < count < 4096
         and count != 65535 and cd_size != 0xffffffff and cd_start != 0xffffffff,
         'unsupported multidisk/ZIP64 package')
    need(cd_start+cd_size == end and end+22+comment == len(raw), 'package central/EOCD layout differs')
    def extra_fields(extra):
        pos = 0
        while pos < len(extra):
            need(pos+4 <= len(extra), 'malformed package extra field')
            kind, size = struct.unpack_from('<HH', extra, pos); pos += 4
            need(kind != 1 and pos+size <= len(extra), 'ZIP64/malformed package extra field')
            pos += size
    records = []; pos = cd_start; names = set(); offsets = set(); expanded = 0
    for _ in range(count):
        need(pos+46 <= end and raw[pos:pos+4] == b'PK\x01\x02', 'malformed package central record')
        version, flags, method, _, _, crc, compressed, size, nl, xl, cl, entry_disk, _, external, offset = struct.unpack_from('<HHHHHIIIHHHHHII', raw, pos+6)
        length = 46+nl+xl+cl
        need(pos+length <= end and compressed != 0xffffffff and size != 0xffffffff
             and offset != 0xffffffff and entry_disk == 0 and version in (10,20) and (method != 8 or version == 20)
             and flags & ~0x808 == 0 and method in (0,8), 'unsupported package entry/ZIP64/encryption')
        encoded = raw[pos+46:pos+46+nl]
        try: name = encoded.decode('utf-8' if flags & 0x800 else 'cp437')
        except UnicodeError: raise Refusal('malformed package name encoding')
        # NuGet IsPackageSignatureFileEntry requires the exact CP437/ASCII name
        # with UTF8 flag clear; a visually identical UTF8 marker is ambiguous.
        need(name != '.signature.p7s' or not flags & 0x800, 'unsupported package signature encoding')
        path = pathlib.PurePosixPath(name)
        need(name and not path.is_absolute() and not any(x in ('','.', '..') for x in name.rstrip('/').split('/'))
             and '\\' not in name and '\x00' not in name and not re.match(r'^[A-Za-z]:', name), 'unsafe package member path')
        need(name not in names and offset not in offsets and ((external>>16)&0o170000) != 0o120000, 'duplicate/symlink package member')
        names.add(name); offsets.add(offset); expanded += size
        need(expanded <= 536870912 and offset+30 <= cd_start and raw[offset:offset+4] == b'PK\x03\x04', 'unbounded/local package entry')
        extra_fields(raw[pos+46+nl:pos+46+nl+xl])
        local_version, local_flags, local_method, _, _, local_crc, local_compressed, local_size, local_nl, local_xl = struct.unpack_from('<HHHHHIIIHH', raw, offset+4)
        data_start = offset+30+local_nl+local_xl
        need(local_version == version and local_flags == flags and local_method == method
             and data_start+compressed <= cd_start and raw[offset+30:offset+30+local_nl] == encoded,
             'local/central package identity mismatch')
        extra_fields(raw[offset+30+local_nl:data_start])
        if flags & 8:
            need(local_crc in (0,crc) and local_compressed in (0,compressed) and local_size in (0,size), 'wrong local data descriptor values')
        else: need((local_crc,local_compressed,local_size) == (crc,compressed,size), 'local/central package sizes differ')
        records.append({'pos':pos,'length':length,'offset':offset,'name':name,'dataEnd':data_start+compressed,
                        'descriptor':bool(flags & 8),'crc':crc,'compressed':compressed,'size':size})
        pos += length
    need(pos == end, 'package central count/size differs')
    order = sorted(records, key=lambda row:row['offset'])
    need(order[0]['offset'] == 0, 'unsupported package preamble')
    for index, row in enumerate(order):
        finish = order[index+1]['offset'] if index+1 < len(order) else cd_start
        trailing = raw[row['dataEnd']:finish]
        if row['descriptor']:
            if trailing.startswith(b'PK\x07\x08'): trailing = trailing[4:]
            need(len(trailing) == 12 and struct.unpack('<III', trailing) == (row['crc'],row['compressed'],row['size']), 'malformed package data descriptor/span')
        else: need(row['dataEnd'] == finish, 'overlap/gap package local span')
        row['span'] = finish-row['offset']
    signatures = [row for row in records if row['name'] == '.signature.p7s']
    if not signatures: return base64.b64encode(hashlib.sha512(raw).digest()).decode()
    need(len(signatures) == 1 and count > 1, 'invalid package signature roster')
    signature = signatures[0]; selected = [row for row in order if row is not signature]
    previous = 0; hasher = hashlib.sha512()
    for row in selected:
        row['change'] = previous-row['offset']; previous += row['span']
        hasher.update(raw[row['offset']:row['offset']+row['span']])
    for row in sorted(selected, key=lambda item:item['pos']):
        start = row['pos']; hasher.update(raw[start:start+42])
        hasher.update(struct.pack('<I', row['offset']+row['change']))
        hasher.update(raw[start+46:start+row['length']])
    hasher.update(raw[end:end+8]); hasher.update(struct.pack('<HHII',count_disk-1,count-1,cd_size-signature['length'],cd_start-signature['span']))
    hasher.update(raw[end+20:])
    return base64.b64encode(hasher.digest()).decode()


def check_installed(record_path,cache,feed,lock):
    value=load(record_path)
    subject = installed_subject() if os.environ.get('MODE') == 'installed' else None
    reviewed=validate_tuple(value['preparation'], subject=subject); b=reviewed['binding']
    execution_input()
    need(b['executor']==(subject.producer if subject else os.environ['ACCEPTED_EXECUTOR']),'installed transaction wrong publisher')
    need(feed in ('https://api.nuget.org/v3/index.json','https://nuget.pkg.github.com/FS-GG/index.json'), 'foreign installed feed')
    dependencies=load(lock)['dependencies']['net10.0']
    for ident in PACKAGES:
        archive=pathlib.Path(cache)/ident.lower()/VERSION/(ident.lower()+'.'+VERSION+'.nupkg')
        need(archive.is_file(),'missing actual restored package archive')
        row=dependencies[ident]; need(row['resolved']==VERSION,'installed pair version mismatch')
        hashfile=archive.parent/(ident.lower()+'.'+VERSION+'.nupkg.sha512')
        need(archive.stat().st_size <= 536870912 and hashfile.is_file(), 'missing/unbounded installed archive hash')
        with archive.open('rb') as stream: raw = stream.read(536870913)
        need(len(raw) <= 536870912, 'unbounded installed archive')
        whole_hash = base64.b64encode(hashlib.sha512(raw).digest()).decode()
        with hashfile.open() as stream: sidecar = stream.read(129)
        need(len(sidecar) <= 128 and sidecar.strip() == whole_hash, 'restored archive/sidecar hash mismatch')
        metadata_path = archive.parent/'.nupkg.metadata'
        need(metadata_path.is_file(), 'missing restored package metadata')
        try:
            with metadata_path.open() as stream: text = stream.read(8193)
            need(len(text) <= 8192, 'unbounded restored package metadata')
            def unique_fields(pairs):
                need(len(pairs) == len({key for key,_ in pairs}), 'duplicate restored metadata field')
                return dict(pairs)
            metadata = json.loads(text, object_pairs_hook=unique_fields)
        except (OSError, ValueError): raise Refusal('malformed restored package metadata')
        need(type(metadata) is dict and set(metadata) == {'version','contentHash','source'}
             and type(metadata['version']) is int and metadata['version'] == 2 and metadata['source'] == feed,
             'restored package metadata/source mismatch')
        content_hash = package_content_hash(raw)
        need(metadata['contentHash'] == content_hash == row['contentHash'], 'restored lock/package content hash mismatch')
        try:
            with zipfile.ZipFile(io.BytesIO(raw)) as z:
                names=z.namelist(); manifest_tool().safe_members(names,archive.name)
                inventory=b['inventories'][ident+'.'+VERSION+'.nupkg']
                need(feed != 'https://api.nuget.org/v3/index.json' or '.signature.p7s' in names, 'public restored package lacks repository signature')
                if '.signature.p7s' in names:
                    need(feed=='https://api.nuget.org/v3/index.json','org original was unexpectedly signed')
                    print(repository_signature(archive))
                    with archive.open('rb') as stream: need(stream.read(536870913) == raw, 'restored archive changed during signature verification')
                    names.remove('.signature.p7s')
                need(set(names)==set(inventory),'restored package roster mismatch')
                need(all(digest(z.read(name))==inventory[name] for name in names),'restored payload drift')
        except (zipfile.BadZipFile, zlib.error, EOFError): raise Refusal('malformed restored package payload')
        if feed=='https://nuget.pkg.github.com/FS-GG/index.json': need(digest(raw)==b['archives'][ident+'.'+VERSION+'.nupkg'],'org literal archive differs')
    print('wasm-installed-selected: actual feed cache, exact locked pair and every member verified')


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('action',choices=['freeze','admit','begin','org','public','assets','complete','readback','check-installed','check-feed','verify-signature','inspect','asset-stage','asset-readback','check-custody-plan'])
    parser.add_argument('--custody'); parser.add_argument('--output'); parser.add_argument('--reviewed'); parser.add_argument('--recovery',action='store_true'); parser.add_argument('--manifest'); parser.add_argument('--cache'); parser.add_argument('--feed'); parser.add_argument('--lock'); parser.add_argument('--archive'); args=parser.parse_args()
    if args.action=='check-custody-plan': custody_plan(); return
    if args.action=='freeze': freeze(args.custody,args.output); return
    if args.action=='check-feed': validate_feed_config(args.output,args.feed); return
    if args.action=='check-installed': check_installed(args.manifest,args.cache,args.feed,args.lock); return
    if args.action=='verify-signature': print(repository_signature(args.archive)); return
    reviewed=validate_tuple(load(args.reviewed)); native=Native()
    if args.action in ('asset-stage','asset-readback'):
        release_assets(native,reviewed,args.custody,args.output,write=args.action=='asset-stage'); return
    if args.action=='inspect':
        need(os.environ.get('MODE')=='inspect' and execution_input() is None,'inspection requires current route')
        download(native,reviewed,args.custody)
        _,release=release_state(native); need(release is not None,'inspection draft Unknown')
        value=verify_transaction(native,release,reviewed)
        save(args.output,{'schema':'fsgg.wasm.inspection/v1','executionBinding':execution_input(),'transaction':value,'releaseId':release['id'],'draft':release['draft'],'authority':'read-only; not a recovery admission'}); return
    if args.action=='admit':
        download(native,reviewed,args.custody)
        need(os.environ.get('NUGET_EXCHANGE_VERIFIED')=='true','native OIDC mapping/exchange Unknown')
        ref,release=release_state(native)
        if args.recovery:
            need(ref==200,'recovery has no tag');
            if release:
                if any(a['name']=='promotion-binding.json' for a in release['assets']): verify_transaction(native,release,reviewed)
                else: interrupted_begin(native,reviewed,release)
            else: interrupted_begin(native,reviewed)
            observed=occupancy(native,reviewed,True)
            readback_occupied(observed,args.custody,pathlib.Path(os.environ['RUNNER_TEMP'])/'admission-occupied-readback')
        else:
            need(ref==404 and release is None,'fresh tag/release occupied'); observed=occupancy(native,reviewed)
        record={'schema':'fsgg.wasm.admission/v1','reviewed':reviewed,'mode':os.environ.get('MODE') or ('recovery' if args.recovery else 'promote'),'run':int(os.environ['GITHUB_RUN_ID']),'attempt':int(os.environ['GITHUB_RUN_ATTEMPT']),'exchangeVerified':True,'occupancy':observed,'observedAt':dt.datetime.now(dt.timezone.utc).isoformat(),'nugetAccountSha256':digest(os.environ['NUGET_ACCOUNT'].encode()),'publicModeratorRemovalHistory':'Unknown','atomicReservation':'Unknown'}
        save(args.output,record)
    elif args.action=='begin': begin(native,reviewed,args.custody,args.recovery)
    else: perform(native,args.action,reviewed,args.custody,args.output)

if __name__=='__main__':
    try: main()
    except (Refusal,KeyError,ValueError,subprocess.CalledProcessError,subprocess.TimeoutExpired,urllib.error.URLError) as e:
        # Never emit subprocess argv: it may contain a short-lived push token.
        print('wasm-promotion refused: '+(str(e) if isinstance(e,Refusal) else type(e).__name__),file=sys.stderr); sys.exit(2)
