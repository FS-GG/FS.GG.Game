#!/usr/bin/env python3
"""Finite shared-WASM transaction adapter. No preparation, pack or compiler path.

Native calls use bounded HTTPS and workflow-scoped tokens. Offline controls inject
transport into the same decisions; they do not establish live authorization.
"""
from __future__ import annotations
import argparse
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
sys.dont_write_bytecode = True
ROOT = pathlib.Path(__file__).resolve().parents[2]
REPO = 'FS-GG/FS.GG.Game'
P = '16a401692f4c0dee7f6da1a86d0cd49e6ea3ec2c'
TREE = 'f06c9fef54b90d9dcd190b5bc1484f2e2e72f67a'
VERSION = '0.3.0'
PACKAGES = ('FS.GG.Wasm.Contracts', 'FS.GG.Wasm.Browser')
FILES = tuple(x + '.0.3.0.nupkg' for x in PACKAGES) + ('fsgg-wasm-sdk-0.3.0.tar.gz',)
GATES = ('Prepare protected originals once', 'Qualify contracts and lifecycle',
         'Qualify historical packages and SDK', 'Qualify full selected custody', 'Freeze eligible binding')
SCHEMA = 'fsgg.wasm.preparation/v1'
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

def validate_binding(binding):
    fields = {'schema','repository','workflow','mode','producer','producerTree','executor','executorTree',
              'runId','runAttempt','version','manifestSha256','archives','inventories','gates'}
    need(set(binding) == fields, 'unreviewed preparation fields')
    need(binding['schema'] == SCHEMA and binding['repository'] == REPO and binding['workflow'] == '.github/workflows/release-wasm.yml' and binding['mode'] == 'prepare', 'unrelated preparation route')
    need(binding['producer'] == P and binding['producerTree'] == TREE and binding['version'] == VERSION, 'wrong P/tree/version')
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

def validate_tuple(reviewed):
    need(set(reviewed) == {'binding','bindingSha256','artifactId','artifactSha256'}, 'unreviewed promotion inputs')
    validate_binding(reviewed['binding']); sha(reviewed['bindingSha256']); integer(reviewed['artifactId']); sha(reviewed['artifactSha256'])
    return reviewed

def validate_run(reviewed, run, jobs, artifact):
    b = reviewed['binding']
    need(run['head_sha'] == b['executor'] and run['path'] == b['workflow'] and run['event'] == 'workflow_dispatch', 'unrelated native preparation run')
    need(run['id'] == b['runId'] and run['run_attempt'] == b['runAttempt'] and run['status'] == 'completed' and run['conclusion'] == 'success', 'preparation failed/wrong attempt/incomplete')
    need(run['repository']['full_name'] == REPO, 'foreign run repository')
    prepared = [job for job in jobs if job['name'] == 'prepare']
    need(len(prepared) == 1 and prepared[0]['conclusion'] == 'success', 'missing actual successful prepare job')
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
    def request(self, url, method='GET', data=None, github=False, binary=False):
        need(time.monotonic() < self.end, 'stage deadline expired')
        headers = {'Accept':'application/vnd.github+json' if github else 'application/json', 'Cache-Control':'no-cache'}
        if github:
            headers['Authorization'] = 'Bearer ' + os.environ['GH_TOKEN']
            headers['X-GitHub-Api-Version'] = '2022-11-28'
        if data is not None: data = json.dumps(data).encode(); headers['Content-Type'] = 'application/json'
        try:
            with urllib.request.build_opener(SafeRedirect()).open(urllib.request.Request(url, data=data, headers=headers, method=method), timeout=min(30,max(1,self.end-time.monotonic()))) as r:
                chunks=[]; total=0
                while True:
                    need(time.monotonic() < self.end, 'native response stage deadline expired')
                    chunk=r.read(1048576)
                    if not chunk: break
                    total+=len(chunk); need(total <= (536870912 if binary else 8388608),'native response exceeds bounded size')
                    chunks.append(chunk)
                body=b''.join(chunks); return r.status, body if binary else json.loads(body or b'null'), dict(r.headers)
        except urllib.error.HTTPError as e:
            return e.code, None, dict(e.headers)
    def api(self, path, method='GET', data=None): return self.request('https://api.github.com/'+path,method,data,True)
    def checked(self,path,method='GET',data=None):
        status, body, _ = self.api(path,method,data); need(status in (200,201), f'native API {method} {path}: status {status}, Unknown'); return body
    def pages(self,path,key=None):
        rows=[]; page=1
        while True:
            need(page <= 100, 'pagination incomplete')
            value=self.checked(path + ('&' if '?' in path else '?') + f'per_page=100&page={page}')
            current=value[key] if key else value
            need(isinstance(current,list), 'malformed paginated response')
            rows.extend(current)
            if len(current)<100: return rows
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


def executor_checkout(binding):
    def git(*args): return subprocess.check_output(['git','-C',str(ROOT),*args],text=True).strip()
    need(git('rev-parse','HEAD')==binding['executor']==os.environ['GITHUB_SHA']==os.environ['ACCEPTED_EXECUTOR'],'wrong executor checkout')
    need(git('rev-parse','HEAD^{tree}')==binding['executorTree'] and not git('status','--porcelain'),'dirty or different executor tree')


def download(native, reviewed, target):
    validate_tuple(reviewed); b=reviewed['binding']; executor_checkout(b)
    need(os.environ['GITHUB_SHA']==os.environ['ACCEPTED_EXECUTOR']==b['executor'], 'moving/unreviewed executor')
    need(native.checked(f'repos/{REPO}/git/ref/heads/main')['object']['sha']==b['executor'], 'protected executor moved')
    run=native.checked(f'repos/{REPO}/actions/runs/{b["runId"]}/attempts/{b["runAttempt"]}')
    jobs=native.pages(f'repos/{REPO}/actions/runs/{b["runId"]}/attempts/{b["runAttempt"]}/jobs', 'jobs')
    artifact=native.checked(f'repos/{REPO}/actions/artifacts/{reviewed["artifactId"]}')
    validate_run(reviewed,run,jobs,artifact)
    status,body,_=native.request(f'https://api.github.com/repos/{REPO}/actions/artifacts/{reviewed["artifactId"]}/zip',github=True,binary=True)
    need(status==200 and digest(body)==reviewed['artifactSha256'],'downloaded artifact digest mismatch')
    target=pathlib.Path(target); need(not target.exists(),'custody output must be fresh'); target.mkdir(mode=0o700)
    import io
    with zipfile.ZipFile(io.BytesIO(body)) as z:
        names=z.namelist(); need(len(names)==len(set(names)),'duplicate artifact members')
        for info in z.infolist():
            path=target/info.filename
            need(not pathlib.PurePosixPath(info.filename).is_absolute() and '..' not in pathlib.PurePosixPath(info.filename).parts and (info.external_attr>>16)&0o170000 != 0o120000,'unsafe custody member')
            if info.is_dir(): path.mkdir(parents=True,exist_ok=True)
            else: path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(z.read(info))
    need(digest((target/'preparation-binding.json').read_bytes())==reviewed['bindingSha256'],'preparation binding digest drift')
    need(load(target/'preparation-binding.json')==b,'actual preparation tuple drift')
    verify_originals(target,b)

def transaction(reviewed, first_run):
    integer(first_run)
    return {'schema':'fsgg.wasm.promotion/v1','preparation':reviewed,'firstPromotionRun':first_run,'firstPromotionAttempt':int(os.environ.get('FIRST_PROMOTION_ATTEMPT') or os.environ['GITHUB_RUN_ATTEMPT']),'producer':P,'tag':'wasm/v0.3.0'}

def release_state(native):
    ref_status,ref,_=native.api(f'repos/{REPO}/git/ref/tags/wasm%2Fv{VERSION}')
    release_status,release,_=native.api(f'repos/{REPO}/releases/tags/wasm%2Fv{VERSION}')
    need(ref_status in (200,404) and release_status in (200,404),'tag/release Unknown')
    if ref_status==200: need(ref['object']['type']=='commit' and ref['object']['sha']==P,'foreign or nonliteral producer tag')
    if release_status==200: need(ref_status==200 and release['tag_name']=='wasm/v0.3.0' and not release['prerelease'],'foreign release')
    return ref_status, release

def verify_transaction(native, release, reviewed):
    assets=[x for x in release['assets'] if x['name']=='promotion-binding.json']
    need(len(assets)==1,'missing/duplicate durable transaction binding')
    row=assets[0]
    # Asset API needs octet-stream: use bounded gh download to avoid JSON metadata.
    scratch=pathlib.Path(os.environ['RUNNER_TEMP'])/'promotion-record'; scratch.mkdir(exist_ok=True)
    command(['gh','release','download','wasm/v0.3.0','--repo',REPO,'--pattern','promotion-binding.json','--dir',str(scratch),'--clobber'],stdout=subprocess.DEVNULL)
    value=load(scratch/'promotion-binding.json')
    need(set(value)=={'schema','preparation','firstPromotionRun','firstPromotionAttempt','producer','tag'} and value['schema']=='fsgg.wasm.promotion/v1' and value['preparation']==reviewed and value['producer']==P and value['tag']=='wasm/v0.3.0','foreign durable release transaction')
    integer(value['firstPromotionRun']); integer(value['firstPromotionAttempt'])
    verify_admission(native,reviewed,value['firstPromotionRun'],value['firstPromotionAttempt'])
    run=native.checked(f'repos/{REPO}/actions/runs/{value["firstPromotionRun"]}')
    need(run['head_sha']==reviewed['binding']['executor'] and run['event']=='workflow_dispatch' and run['path']=='.github/workflows/release-wasm.yml','foreign first promotion run')
    return value

def verify_admission(native,reviewed,run_id,attempt):
    run=native.checked(f'repos/{REPO}/actions/runs/{run_id}')
    need(run['head_sha']==reviewed['binding']['executor'] and run['event']=='workflow_dispatch' and run['path']=='.github/workflows/release-wasm.yml','foreign original admission run')
    jobs=native.pages(f'repos/{REPO}/actions/runs/{run_id}/attempts/{attempt}/jobs','jobs')
    admitted=[j for j in jobs if j['name']=='admission']
    need(len(admitted)==1 and admitted[0]['conclusion']=='success','original admission was not successful')
    artifacts=native.pages(f'repos/{REPO}/actions/runs/{run_id}/artifacts','artifacts')
    matching=[a for a in artifacts if a['name']==f'wasm-admission-{run_id}-{attempt}']
    need(len(matching)==1 and not matching[0]['expired'],'original admission evidence absent/expired')
    a=matching[0]; need(a['workflow_run']['head_sha']==reviewed['binding']['executor'],'foreign admission artifact')
    status,body,_=native.request(f'https://api.github.com/repos/{REPO}/actions/artifacts/{a["id"]}/zip',github=True,binary=True)
    need(status==200 and a['digest']=='sha256:'+digest(body),'admission artifact digest mismatch')
    import io
    with zipfile.ZipFile(io.BytesIO(body)) as z:
        need(z.namelist()==['admission-binding.json'],'foreign admission artifact members')
        record=json.loads(z.read('admission-binding.json'))
    need(set(record)=={'schema','reviewed','mode','run','attempt','exchangeVerified','occupancy','observedAt','nugetAccountSha256','publicModeratorRemovalHistory','atomicReservation'} and record['schema']=='fsgg.wasm.admission/v1','unreviewed original admission record')
    need(record['publicModeratorRemovalHistory']==record['atomicReservation']=='Unknown' and not any(record['occupancy'].values()),'original fresh admission did not prove observable absence')
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


def begin(native,reviewed,custody,recovery):
    need(os.environ.get('TAG_CREDENTIAL_ROUTE')=='workflow-github-token','PAT/App/manual tag path refuses')
    verify_originals(custody,reviewed['binding'])
    ref,release=release_state(native)
    occupancy(native,reviewed,recovery or release is not None)
    if release:
        rows=[a for a in release['assets'] if a['name']=='promotion-binding.json']
        if not rows:
            need(recovery,'unbound draft requires explicit interrupted recovery')
            first,attempt=interrupted_begin(native,reviewed,release)
            value=transaction(reviewed,first); value['firstPromotionAttempt']=attempt
            path=pathlib.Path(custody)/'promotion-binding.json'; save(path,value)
            command(['gh','release','upload','wasm/v0.3.0',str(path),'--repo',REPO])
            verify_transaction(native,native.checked(f'repos/{REPO}/releases/{release["id"]}'),reviewed)
        else:
            verify_transaction(native,release,reviewed)
            need(recovery or not release['draft'],'existing draft requires explicit recovery')
        return
    need(ref==404 or recovery,'preexisting tag requires causally bound recovery')
    first=int(os.environ.get('FIRST_PROMOTION_RUN') or os.environ['GITHUB_RUN_ID'])
    if ref==200: first,_=interrupted_begin(native,reviewed)
    if ref==404: native.checked(f'repos/{REPO}/git/refs','POST',{'ref':'refs/tags/wasm/v0.3.0','sha':P})
    release=native.checked(f'repos/{REPO}/releases','POST',{'tag_name':'wasm/v0.3.0','target_commitish':P,'name':'Shared WASM 0.3.0','draft':True,'prerelease':False,'body':'Same-custody promotion incomplete until exact readback and installed acceptance.'})
    path=pathlib.Path(custody)/'promotion-binding.json'; save(path,transaction(reviewed,first))
    command(['gh','release','upload','wasm/v0.3.0',str(path),'--repo',REPO])
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
        for ident in PACKAGES:
            key=ident+(':active' if kind=='org' else ':public')
            if occupied[key]:
                command([str(ROOT/'scripts/wasm-release/readback.sh'),kind,str(custody),str(pathlib.Path(output)/ident),ident])
        for ident in PACKAGES:
            key=ident+(':active' if kind=='org' else ':public')
            original=pathlib.Path(custody)/(ident+'.0.3.0.nupkg')
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


def freeze(custody,output):
    manifest=load(pathlib.Path(custody)/'release-manifest.json')
    b={'schema':SCHEMA,'repository':REPO,'workflow':'.github/workflows/release-wasm.yml','mode':'prepare','producer':P,'producerTree':TREE,
       'executor':os.environ['ACCEPTED_EXECUTOR'],'executorTree':os.environ['EXECUTOR_TREE'],'runId':int(os.environ['GITHUB_RUN_ID']),
       'runAttempt':int(os.environ['GITHUB_RUN_ATTEMPT']),'version':VERSION,'manifestSha256':digest((pathlib.Path(custody)/'release-manifest.json').read_bytes()),
       'archives':{row['file']:row['sha256'] for row in manifest['artifacts']},'inventories':inventories(custody),'gates':{name:'success' for name in GATES}}
    validate_binding(b); verify_originals(custody,b); save(output,b)
    with open(os.environ['GITHUB_OUTPUT'],'a') as f: f.write('binding_sha256='+digest(pathlib.Path(output).read_bytes())+'\n')

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

def check_installed(record_path,cache,feed,lock):
    value=load(record_path); reviewed=validate_tuple(value['preparation']); b=reviewed['binding']
    need(b['executor']==os.environ['ACCEPTED_EXECUTOR'],'installed transaction wrong executor')
    dependencies=load(lock)['dependencies']['net10.0']
    for ident in PACKAGES:
        archive=pathlib.Path(cache)/ident.lower()/VERSION/(ident.lower()+'.'+VERSION+'.nupkg')
        need(archive.is_file(),'missing actual restored package archive')
        row=dependencies[ident]; need(row['resolved']==VERSION,'installed pair version mismatch')
        hashfile=archive.parent/(ident.lower()+'.'+VERSION+'.nupkg.sha512')
        need(hashfile.read_text().strip()==row['contentHash'],'restored lock/package identity mismatch')
        with zipfile.ZipFile(archive) as z:
            names=z.namelist(); manifest_tool().safe_members(names,archive.name)
            inventory=b['inventories'][ident+'.'+VERSION+'.nupkg']
            if '.signature.p7s' in names:
                need(feed=='https://api.nuget.org/v3/index.json','org original was unexpectedly signed')
                repository_signature(archive); names.remove('.signature.p7s')
            need(set(names)==set(inventory),'restored package roster mismatch')
            need(all(digest(z.read(name))==inventory[name] for name in names),'restored payload drift')
        if feed=='https://nuget.pkg.github.com/FS-GG/index.json': need(digest(archive.read_bytes())==b['archives'][ident+'.'+VERSION+'.nupkg'],'org literal archive differs')
    print('wasm-installed-selected: actual feed cache, exact locked pair and every member verified')


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('action',choices=['freeze','admit','begin','org','public','assets','complete','readback','check-installed','check-feed','verify-signature'])
    parser.add_argument('--custody'); parser.add_argument('--output'); parser.add_argument('--reviewed'); parser.add_argument('--recovery',action='store_true'); parser.add_argument('--manifest'); parser.add_argument('--cache'); parser.add_argument('--feed'); parser.add_argument('--lock'); parser.add_argument('--archive'); args=parser.parse_args()
    if args.action=='freeze': freeze(args.custody,args.output); return
    if args.action=='check-feed': validate_feed_config(args.output,args.feed); return
    if args.action=='check-installed': check_installed(args.manifest,args.cache,args.feed,args.lock); return
    if args.action=='verify-signature': print(repository_signature(args.archive)); return
    reviewed=validate_tuple(load(args.reviewed)); native=Native()
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
        else:
            need(ref==404 and release is None,'fresh tag/release occupied'); observed=occupancy(native,reviewed)
        save(args.output,{'schema':'fsgg.wasm.admission/v1','reviewed':reviewed,'mode':'recovery' if args.recovery else 'promote','run':int(os.environ['GITHUB_RUN_ID']),'attempt':int(os.environ['GITHUB_RUN_ATTEMPT']),'exchangeVerified':True,'occupancy':observed,'observedAt':dt.datetime.now(dt.timezone.utc).isoformat(),'nugetAccountSha256':digest(os.environ['NUGET_ACCOUNT'].encode()),'publicModeratorRemovalHistory':'Unknown','atomicReservation':'Unknown'})
    elif args.action=='begin': begin(native,reviewed,args.custody,args.recovery)
    else: perform(native,args.action,reviewed,args.custody,args.output)

if __name__=='__main__':
    try: main()
    except (Refusal,KeyError,ValueError,subprocess.CalledProcessError,subprocess.TimeoutExpired,urllib.error.URLError) as e:
        # Never emit subprocess argv: it may contain a short-lived push token.
        print('wasm-promotion refused: '+(str(e) if isinstance(e,Refusal) else type(e).__name__),file=sys.stderr); sys.exit(2)
