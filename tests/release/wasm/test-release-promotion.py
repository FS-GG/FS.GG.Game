#!/usr/bin/env python3
"""Offline decisions against real adapter/YAML. No credentials, actors or feeds."""
import copy
import importlib.util
import json
import os
import pathlib
import re
import sys
import tempfile
import unittest
from unittest.mock import patch
sys.dont_write_bytecode=True
ROOT=pathlib.Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('promotion',ROOT/'scripts/wasm-release/promotion.py')
p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)


def validate_workflow(text):
    assert '  push:' not in text and 'options: [prepare, promote, readback, recovery]' in text and 'default: prepare' in text
    assert 'group: release-wasm-0.3.0' in text and 'cancel-in-progress: false' in text
    env = text.split("\nenv:\n",1)[1].split("\njobs:\n",1)[0]
    assert re.findall(r"(?m)^  DOTNET_PROCESSOR_COUNT: (.+)$",env)==['1'], "actual CPU bound must be one"
    assert not re.search(r"(?m)^  .*[/@].*:",env), "action identifier substituted for an environment key"
    jobs={m[1]:m[2] for m in re.finditer(r'(?ms)^  ([a-z]+):\n(.*?)(?=^  [a-z]+:\n|\Z)',text.split('jobs:\n',1)[1])}
    assert set(jobs)=={'preflight','prepare','admission','begin','org','public','assets','complete','readback'}
    for job,dependency in [('prepare','preflight'),('admission','preflight'),('begin','admission'),('org','begin'),('public','org'),('assets','public'),('complete','org, public, assets'),('readback','admission')]:
        assert 'needs: ['+dependency+']' in jobs[job]
    assert "if: inputs.mode == 'prepare'" in jobs['prepare']
    assert text.count('scripts/wasm-release/prepare.sh')==1 and 'scripts/wasm-release/prepare.sh "$RUNNER_TEMP/wasm-release"' in jobs['prepare']
    assert "ref: "+p.P in jobs['prepare'] and p.TREE in jobs['prepare']
    assert 'EXECUTOR_TREE=' in jobs['prepare'] and 'python3 scripts/wasm-release/promotion.py freeze' in jobs['prepare']
    for gate in p.GATES: assert '- name: '+gate in jobs['prepare']
    assert 'scripts/verify-wasm-contracts.sh' in jobs['prepare'] and 'scripts/verify-wasm-lifecycle.sh' in jobs['prepare']
    assert 'scripts/verify-wasm-package-consumer.sh --custody' in jobs['prepare'] and 'scripts/wasm-release/qualify-supervisor-custody.sh --custody' in jobs['prepare']
    assert jobs['prepare'].index('Qualify full selected custody') < jobs['prepare'].index('Freeze eligible binding') < jobs['prepare'].index('Retain eligible originals')
    assert 'if: failure()' in jobs['prepare'] and 'wasm-ineligible-' in jobs['prepare']
    assert all(x not in jobs['prepare'] for x in ['contents: write','packages: write','id-token: write','GH_TOKEN:','NUGET_API_KEY:'])
    assert 'id-token: write' in jobs['admission'] and 'NuGet/login@8d196754b4036150537f80ac539e15c2f1028841' in jobs['admission']
    assert 'NUGET_EXCHANGE_VERIFIED:' in jobs['admission'] and 'promotion.py admit' in jobs['admission']
    for job in ['begin','org','public','assets','complete']:
        assert "if: inputs.mode == 'promote' || inputs.mode == 'recovery'" in jobs[job]
        assert 'p.download(p.Native()' in jobs[job] and 'promotion.py '+job in jobs[job]
        assert 'gh release create' not in jobs[job] and 'prepare.sh' not in jobs[job] and 'dotnet pack' not in jobs[job]
        assert 'GH_TOKEN: ${{ github.token }}' in jobs[job]
    assert 'TAG_CREDENTIAL_ROUTE: workflow-github-token' in jobs['begin'] and 'contents: write' in jobs['begin']
    assert text.count('packages: write')==1 and 'packages: write' in jobs['org']
    assert 'NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}' in jobs['public']
    assert 'secrets.NUGET_API_KEY' not in text and '--skip-duplicate' not in text
    assert '$GITHUB_SHA" == "$ACCEPTED_EXECUTOR' in jobs['preflight'] and 'git/ref/heads/main' in jobs['preflight']


def binding():
    return {'schema':p.SCHEMA,'repository':p.REPO,'workflow':'.github/workflows/release-wasm.yml','mode':'prepare','producer':p.P,'producerTree':p.TREE,'executor':'a'*40,'executorTree':'b'*40,'runId':100,'runAttempt':2,'version':'0.3.0','manifestSha256':'c'*64,'archives':{name:'d'*64 for name in p.FILES},'inventories':{name:{'value':'e'*64} for name in p.FILES},'gates':{name:'success' for name in p.GATES}}

def reviewed(): return {'binding':binding(),'bindingSha256':'f'*64,'artifactId':200,'artifactSha256':'0'*64}

def native_data():
    b=binding()
    run={'id':100,'run_attempt':2,'head_sha':'a'*40,'path':b['workflow'],'event':'workflow_dispatch','status':'completed','conclusion':'success','repository':{'full_name':p.REPO}}
    jobs=[{'name':'prepare','conclusion':'success','steps':[{'name':x,'conclusion':'success'} for x in [*p.GATES,'Retain eligible originals']]}]
    artifact={'id':200,'expired':False,'digest':'sha256:'+'0'*64,'name':'wasm-eligible-100-2-0.3.0','workflow_run':{'id':100,'head_sha':'a'*40}}
    return run,jobs,artifact

class Occupancy:
    def __init__(self,mutant=None): self.mutant=mutant; self.writes=[]
    def pages(self,path,key=None):
        if self.mutant=='denied': raise p.Refusal('403 Unknown')
        if self.mutant=='deleted' and 'state=deleted' in path: return [{'name':'0.3.0','id':1}]
        if self.mutant=='active' and 'state=active' in path: return [{'name':'0.3.0','id':1}]
        if self.mutant=='malformed-org': return [{'id':1}]
        return [{'name':'0.2.0','id':1}]
    def request(self,url,**kwargs):
        if url.endswith('/v3/index.json'):
            return 200,{'resources':[{'@type':'PackageBaseAddress/3.0.0','@id':'https://api.nuget.org/v3-flatcontainer/'}]},{}
        if url.endswith('/index.json'):
            if self.mutant=='missing-index': return 404,None,{}
            if self.mutant=='malformed-index': return 200,{'versions':[{}]},{}
            return 200,{'versions':['0.1.1','0.2.0']+(['0.3.0'] if self.mutant=='unlisted' else [])},{}
        return (200 if self.mutant in ('contradiction','unlisted') else 404),b'',{}

class Tests(unittest.TestCase):
    def test_actual_yaml_and_causal_mutants(self):
        text=(ROOT/'.github/workflows/release-wasm.yml').read_text();validate_workflow(text)
        for before,after in [
            ('needs: [org]\n','needs: [begin]\n'),('options: [prepare, promote, readback, recovery]','options: [prepare, publish]'),
            ('DOTNET_PROCESSOR_COUNT: 1','DOTNET_PROCESSOR_COUNT: 2'),
            ('DOTNET_PROCESSOR_COUNT: 1','actions/setup-dotnet@bad_PROCESSOR_COUNT: 1'),
            ('default: prepare','default: promote'),('contents: read\n    defaults:','contents: write\n    defaults:'),
            ('Qualify full selected custody','Skipped selected custody'),('TAG_CREDENTIAL_ROUTE: workflow-github-token','TAG_CREDENTIAL_ROUTE: PAT'),
            ('p.download(p.Native()','p.foreign(p.Native()'),('NUGET_EXCHANGE_VERIFIED:','EXCHANGE_ASSUMED:'),
            ('group: release-wasm-0.3.0','group: release-wasm-0.2.0'),('GH_TOKEN: ${{ github.token }}','GH_TOKEN: ${{ secrets.APP_TOKEN }}')]:
            self.assertIn(before,text)
            with self.subTest(before=before),self.assertRaises((AssertionError,KeyError,ValueError)): validate_workflow(text.replace(before,after))
        with self.assertRaises(AssertionError):validate_workflow(text.replace('on:\n','on:\n  push:\n    tags: [wasm/v*]\n',1))
    def test_closed_binding(self):
        p.validate_tuple(reviewed())
        for key,value in [('producer','9'*40),('executor','main'),('producerTree','9'*40),('version','0.2.0'),('mode','publish'),('runAttempt',0),('repository','foreign/repo')]:
            x=reviewed();x['binding'][key]=value
            with self.subTest(key=key),self.assertRaises(p.Refusal):p.validate_tuple(x)
        for mutation in ('gate','roster','inventory','extra','duplicate-surrogate'):
            x=reviewed()
            if mutation=='gate':x['binding']['gates'].pop(p.GATES[-2])
            elif mutation=='roster':x['binding']['archives']['Foreign.nupkg']='d'*64
            elif mutation=='inventory':x['binding']['inventories'].pop(p.FILES[-1])
            elif mutation=='extra':x['authority']='approved'
            else:x['binding']['gates']['Unrelated success']='success'
            with self.subTest(mutation=mutation),self.assertRaises(p.Refusal):p.validate_tuple(x)
    def test_real_native_run_attempt_and_gate_join(self):
        run,jobs,artifact=native_data();p.validate_run(reviewed(),run,jobs,artifact)
        for target,key,value in [('run','run_attempt',1),('run','conclusion','failure'),('run','head_sha','9'*40),('run','event','push'),('artifact','expired',True),('artifact','digest','sha256:'+'9'*64),('artifact','name','wasm-ineligible-100-2')]:
            r,j,a=copy.deepcopy((run,jobs,artifact)); (r if target=='run' else a)[key]=value
            with self.subTest(target=target,key=key),self.assertRaises(p.Refusal):p.validate_run(reviewed(),r,j,a)
        broken=copy.deepcopy(jobs);broken[0]['steps'][3]['conclusion']='skipped'
        with self.assertRaises(p.Refusal):p.validate_run(reviewed(),run,broken,artifact)
        with self.assertRaises(p.Refusal):p.validate_run(reviewed(),run,jobs*2,artifact)
    def test_observable_public_and_authenticated_deleted_occupancy(self):
        result=p.occupancy(Occupancy(),reviewed());self.assertFalse(any(result.values()))
        for mutation in ('denied','deleted','active','malformed-org','missing-index','malformed-index','contradiction','unlisted'):
            with self.subTest(mutation=mutation),self.assertRaises(p.Refusal):p.occupancy(Occupancy(mutation),reviewed())
        self.assertTrue(p.occupancy(Occupancy('unlisted'),reviewed(),True)[p.PACKAGES[0]+':public'])
    def test_full_pagination_denial_and_deadline(self):
        native=p.Native();calls=[]
        def read(path,*args):
            calls.append(path)
            return [{'name':'x','id':i} for i in range(100)] if path.endswith('&page=1') else [{'name':'last','id':100}]
        with patch.object(native,'checked',read):self.assertEqual(len(native.pages('orgs/FS-GG/versions')),101)
        self.assertEqual(len(calls),2)
        native.end=0
        with self.assertRaises(p.Refusal):native.request('https://example.invalid')
    def test_signature_trust_before_exception(self):
        import subprocess
        good=subprocess.CompletedProcess([],0,stdout='Signature type: Repository\nService index: https://api.nuget.org/v3/index.json')
        with patch.object(p.subprocess,'run',return_value=good):p.repository_signature('served.nupkg')
        for body in ('Signature type: Author\nhttps://api.nuget.org/v3/index.json','Signature type: Repository\nhttps://foreign.invalid','served-signature'):
            bad=subprocess.CompletedProcess([],0,stdout=body)
            with patch.object(p.subprocess,'run',return_value=bad),self.assertRaises(p.Refusal):p.repository_signature('served.nupkg')
        with patch.object(p.subprocess,'run',side_effect=subprocess.CalledProcessError(1,['verify'])),self.assertRaises(subprocess.CalledProcessError):p.repository_signature('served.nupkg')
    def test_feed_mapping_known_bad_sources(self):
        with tempfile.TemporaryDirectory() as raw:
            target=pathlib.Path(raw)/'NuGet.Config'
            good='<configuration><packageSources><clear/><add key="wasm" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="wasm"><package pattern="FS.GG.Wasm.*"/><package pattern="FSharp.Core"/></packageSource></packageSourceMapping></configuration>'
            target.write_text(good);p.validate_feed_config(target,'https://api.nuget.org/v3/index.json')
            for old,new in [('https://api.nuget.org/v3/index.json','/tmp/custody'),('FS.GG.Wasm.*','*'),('<clear/>',''),('FSharp.Core','FS.GG.Wasm.Contracts')]:
                target.write_text(good.replace(old,new))
                with self.assertRaises(p.Refusal):p.validate_feed_config(target,'https://api.nuget.org/v3/index.json')
    def test_true_installed_workflow_and_phase_selection(self):
        text=(ROOT/'.github/workflows/wasm-installed-org.yml').read_text()
        self.assertIn('feed: [org, public]',text);self.assertIn('max-parallel: 1',text)
        self.assertIn('qualify-supervisor-installed.sh --feed-only',text)
        selected=(ROOT/'scripts/wasm-release/qualify-supervisor-installed.sh').read_text()
        self.assertNotIn('dotnet pack',selected);self.assertNotIn('--custody',selected)
        self.assertIn('check-installed --manifest',selected)
        self.assertIn('$output/packages/fs.gg.wasm.browser/0.3.0/',selected)
        self.assertIn('verify-wasm-supervisor.sh" --feed-only',selected)
        self.assertIn('verify-wasm-supervisor-browser.sh" --feed-only',selected)
        self.assertIn('official-core-joins.json',selected)
        runner=(ROOT/'scripts/verify-wasm-supervisor.sh').read_text()
        self.assertEqual(runner.count('if [[ "$mode" == custody ]]; then'),3)
        self.assertIn('RestoreLockedMode=true',runner);self.assertIn('NU1403',runner)
    def test_installed_cache_member_lock_worker_substitution(self):
        import zipfile
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);cache=root/'packages';record=root/'promotion-binding.json';lock=root/'packages.lock.json'
            r=reviewed();dependencies={}
            for ident in p.PACKAGES:
                folder=cache/ident.lower()/'0.3.0';folder.mkdir(parents=True)
                archive=folder/(ident.lower()+'.0.3.0.nupkg')
                with zipfile.ZipFile(archive,'w') as z:z.writestr('ordinary.dll',b'real-payload');z.writestr('module-worker.mjs',b'real-worker')
                r['binding']['archives'][ident+'.0.3.0.nupkg']=p.digest(archive.read_bytes())
                r['binding']['inventories'][ident+'.0.3.0.nupkg']={'ordinary.dll':p.digest(b'real-payload'),'module-worker.mjs':p.digest(b'real-worker')}
                (folder/(ident.lower()+'.0.3.0.nupkg.sha512')).write_text('actual-restored-content-hash')
                dependencies[ident]={'resolved':'0.3.0','contentHash':'actual-restored-content-hash'}
            record.write_text(json.dumps({'preparation':r}));lock.write_text(json.dumps({'dependencies':{'net10.0':dependencies}}))
            with patch.dict(os.environ,{'ACCEPTED_EXECUTOR':'a'*40}):
                p.check_installed(record,cache,'https://nuget.pkg.github.com/FS-GG/index.json',lock)
                folder=cache/p.PACKAGES[1].lower()/'0.3.0';archive=folder/(p.PACKAGES[1].lower()+'.0.3.0.nupkg');original=archive.read_bytes()
                for mutation in ('worker','ordinary','signature-disguised-worker','lock','version','missing-cache'):
                    saved=copy.deepcopy(dependencies)
                    if mutation in ('worker','ordinary','signature-disguised-worker'):
                        with zipfile.ZipFile(archive,'w') as z:
                            z.writestr('ordinary.dll',b'changed' if mutation=='ordinary' else b'real-payload')
                            z.writestr('module-worker.mjs',b'changed' if mutation!='ordinary' else b'real-worker')
                            if mutation=='signature-disguised-worker':z.writestr('.signature.p7s',b'fake')
                    elif mutation=='lock':dependencies[p.PACKAGES[1]]['contentHash']='wrong'
                    elif mutation=='version':dependencies[p.PACKAGES[1]]['resolved']='0.2.0'
                    else:archive.unlink()
                    lock.write_text(json.dumps({'dependencies':{'net10.0':dependencies}}))
                    with self.subTest(mutation=mutation),self.assertRaises(p.Refusal):p.check_installed(record,cache,'https://nuget.pkg.github.com/FS-GG/index.json',lock)
                    archive.write_bytes(original);dependencies=saved
                    lock.write_text(json.dumps({'dependencies':{'net10.0':dependencies}}))
    def test_admission_refuses_before_download_or_writer(self):
        env={'GITHUB_SHA':'a'*40,'ACCEPTED_EXECUTOR':'a'*40}
        class Transport:
            def checked(self,path,*args):return {'object':{'sha':'9'*40}}
            def pages(self,*args):raise AssertionError('must refuse before artifact access')
            def request(self,*args,**kwargs):raise AssertionError('must refuse before download')
        with tempfile.TemporaryDirectory() as raw,patch.dict(os.environ,env),patch.object(p,'executor_checkout'):
            with self.assertRaises(p.Refusal):p.download(Transport(),reviewed(),pathlib.Path(raw)/'custody')
        with patch.dict(os.environ,{'TAG_CREDENTIAL_ROUTE':'app-token'}),patch.object(p,'verify_originals',side_effect=AssertionError('must refuse before custody/writer')):
            with self.assertRaises(p.Refusal):p.begin(Transport(),reviewed(),'.',False)
    def test_artifact_digest_safe_members_and_binding_refusal(self):
        import io,zipfile
        for mutation in ('none','artifact-digest','traversal','missing-binding','changed-binding'):
            r=reviewed();body=io.BytesIO();binding_body=(json.dumps(r['binding'])+'\n').encode()
            r['bindingSha256']=p.digest(binding_body)
            with zipfile.ZipFile(body,'w') as z:
                if mutation!='missing-binding':z.writestr('preparation-binding.json',b'{}' if mutation=='changed-binding' else binding_body)
                if mutation=='traversal':z.writestr('../escape',b'bad')
            payload=body.getvalue();r['artifactSha256']=p.digest(payload)
            run,jobs,artifact=native_data();artifact['digest']='sha256:'+r['artifactSha256']
            if mutation=='artifact-digest':r['artifactSha256']='9'*64
            class Transport:
                def checked(self,path,*args):
                    if '/git/ref/' in path:return {'object':{'sha':'a'*40}}
                    return artifact if '/artifacts/' in path else run
                def pages(self,*args):return jobs
                def request(self,*args,**kwargs):return 200,payload,{}
            with tempfile.TemporaryDirectory() as raw,patch.dict(os.environ,{'GITHUB_SHA':'a'*40,'ACCEPTED_EXECUTOR':'a'*40}),patch.object(p,'verify_originals') as verify,patch.object(p,'executor_checkout'):
                if mutation=='none':
                    p.download(Transport(),r,pathlib.Path(raw)/'custody');verify.assert_called_once()
                else:
                    with self.subTest(mutation=mutation),self.assertRaises((p.Refusal,FileNotFoundError)):p.download(Transport(),r,pathlib.Path(raw)/'custody')
                    verify.assert_not_called()
    def test_interrupted_tag_and_draft_require_native_origin(self):
        started='2026-10-03T12:00:00Z';ended='2026-10-03T12:02:00Z'
        jobs=[{'name':'begin','started_at':started,'completed_at':ended,'steps':[{'name':'Create bound draft','conclusion':'failure'}]}]
        class Transport:
            def pages(self,*args,**kwargs):return jobs
        release={'draft':True,'target_commitish':p.P,'author':{'login':'github-actions[bot]'},'created_at':'2026-10-03T12:01:00Z'}
        env={'GITHUB_RUN_ID':'201','GITHUB_RUN_ATTEMPT':'1','FIRST_PROMOTION_RUN':'200','FIRST_PROMOTION_ATTEMPT':'2'}
        with patch.dict(os.environ,env),patch.object(p,'verify_admission') as admitted:
            self.assertEqual(p.interrupted_begin(Transport(),reviewed()),(200,2))
            self.assertEqual(p.interrupted_begin(Transport(),reviewed(),release),(200,2))
            for key,value in [('draft',False),('target_commitish','9'*40),('created_at','2026-10-03T13:00:00Z'),('author',{'login':'someone'})]:
                broken=dict(release);broken[key]=value
                with self.subTest(key=key),self.assertRaises(p.Refusal):p.interrupted_begin(Transport(),reviewed(),broken)
        with patch.dict(os.environ,env),patch.object(p,'verify_admission',side_effect=p.Refusal('foreign original admission')):
            with self.assertRaises(p.Refusal):p.interrupted_begin(Transport(),reviewed(),release)

    def test_foreign_occupied_second_member_blocks_first_writer(self):
        class Transport(Occupancy):
            def pages(self,path,key=None):
                return [{'name':'0.3.0','id':1}] if 'browser' in path and 'state=active' in path else []
        calls=[]
        def reject(args,**kwargs):
            calls.append(args[0])
            if str(args[0]).endswith('readback.sh'):raise p.Refusal('foreign occupied Browser bytes')
            raise AssertionError('must not write missing Contracts before verifying occupied Browser')
        with tempfile.TemporaryDirectory() as raw,patch.object(p,'verify_originals'),patch.object(p,'verify_transaction'),patch.object(p,'release_state',return_value=(200,{'draft':True})),patch.object(p,'command',side_effect=reject):
            with self.assertRaises(p.Refusal):p.perform(Transport(),'org',reviewed(),raw,raw)
        self.assertEqual(len(calls),1);self.assertTrue(str(calls[0]).endswith('readback.sh'))

    def test_recovery_begin_checks_occupied_payload_before_any_writer(self):
        for draft in (False,True):
            for feed in ('org','public'):
                for outcome in ('foreign','unknown','same-original'):
                    events=[];r=reviewed();release={'id':1,'draft':True,'assets':[]} if draft else None
                    class Transport(Occupancy):
                        def pages(self,path,key=None):
                            if outcome=='unknown':raise p.Refusal('403 occupancy Unknown')
                            return [{'name':'0.3.0','id':1}] if feed=='org' and 'browser' in path and 'state=active' in path else []
                        def request(self,url,**kwargs):
                            if feed!='public' or url.endswith('/v3/index.json'):return super().request(url,**kwargs)
                            exists='browser' in url
                            return (200,{'versions':['0.2.0']+(['0.3.0'] if exists else [])},{}) if url.endswith('/index.json') else (200 if exists else 404,b'',{})
                        def checked(self,path,method='GET',data=None):
                            if method=='POST':events.append(('writer','draft'))
                            return {'id':1,'draft':True,'assets':[]}
                    def cmd(args,**kwargs):
                        if str(args[0]).endswith('readback.sh'):
                            events.append(('readback',args[1],args[-1]))
                            if outcome=='foreign':raise p.Refusal('occupied payload differs from original')
                        else:events.append(('writer','binding'))
                    with tempfile.TemporaryDirectory() as raw,patch.dict(os.environ,{'TAG_CREDENTIAL_ROUTE':'workflow-github-token','RUNNER_TEMP':raw,'GITHUB_RUN_ID':'201','GITHUB_RUN_ATTEMPT':'1','FIRST_PROMOTION_RUN':'200','FIRST_PROMOTION_ATTEMPT':'2'}),patch.object(p,'verify_originals'),patch.object(p,'verify_transaction'),patch.object(p,'interrupted_begin',return_value=(200,2)),patch.object(p,'release_state',return_value=(200,release)),patch.object(p,'command',side_effect=cmd):
                        if outcome=='same-original':
                            p.begin(Transport(),r,raw,True)
                            self.assertEqual(events[0],('readback',feed,p.PACKAGES[1]))
                            self.assertTrue(any(e[0]=='writer' for e in events))
                        else:
                            with self.assertRaises(p.Refusal):p.begin(Transport(),r,raw,True)
                            self.assertFalse(any(e[0]=='writer' for e in events))
    def test_recovery_admission_requires_occupied_payload_readback(self):
        for outcome in ('foreign','unknown','same-original'):
            events=[]
            class Transport(Occupancy):
                def pages(self,path,key=None):
                    if outcome=='unknown':raise p.Refusal('403 occupancy Unknown')
                    return [{'name':'0.3.0','id':1}] if 'browser' in path and 'state=active' in path else []
            def cmd(args,**kwargs):
                self.assertTrue(str(args[0]).endswith('readback.sh'))
                events.append(('readback',args[1],args[-1]))
                if outcome=='foreign':raise p.Refusal('foreign occupied archive')
            with tempfile.TemporaryDirectory() as raw:
                receipt=pathlib.Path(raw)/'admission-binding.json';request=pathlib.Path(raw)/'reviewed.json';request.write_text(json.dumps(reviewed()))
                argv=['promotion.py','admit','--reviewed',str(request),'--custody',raw,'--output',str(receipt),'--recovery']
                env={'RUNNER_TEMP':raw,'NUGET_EXCHANGE_VERIFIED':'true','GITHUB_RUN_ID':'201','GITHUB_RUN_ATTEMPT':'1','NUGET_ACCOUNT':'offline'}
                with patch.dict(os.environ,env),patch.object(sys,'argv',argv),patch.object(p,'Native',return_value=Transport()),patch.object(p,'download'),patch.object(p,'release_state',return_value=(200,None)),patch.object(p,'interrupted_begin',return_value=(200,2)),patch.object(p,'command',side_effect=cmd):
                    if outcome=='same-original':
                        p.main();self.assertTrue(receipt.exists());self.assertEqual(events,[('readback','org',p.PACKAGES[1])])
                    else:
                        with self.assertRaises(p.Refusal):p.main()
                        self.assertFalse(receipt.exists())

    def test_actual_clean_executor_and_finite_glue_delta(self):
        import subprocess
        head=subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()
        tree=subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD^{tree}'],text=True).strip()
        b=binding();b['executor']=head;b['executorTree']=tree
        spec=importlib.util.spec_from_file_location('installed_binding',ROOT/'scripts/wasm-release/bind-installed-release.py')
        binder=importlib.util.module_from_spec(spec);spec.loader.exec_module(binder)
        with patch.dict(os.environ,{'GITHUB_SHA':head,'ACCEPTED_EXECUTOR':head}):
            p.executor_checkout(b)
            audit=binder.bind_executor(p.P,head,'0.3.0')
            self.assertTrue(audit['executorDelta'])
            for row in audit['executorDelta']:self.assertEqual(row['sha256'],p.digest((ROOT/row['path']).read_bytes()))
            wrong=dict(b);wrong['executorTree']='9'*40
            with self.assertRaises(p.Refusal):p.executor_checkout(wrong)
            with patch.object(binder,'git',return_value='src/Wasm.Contracts/Contracts.fs'),self.assertRaises(AssertionError):binder.bind_executor(p.P,head,'0.3.0')
        with patch.dict(os.environ,{'GITHUB_SHA':head,'ACCEPTED_EXECUTOR':'9'*40}),self.assertRaises(p.Refusal):p.executor_checkout(b)

    def test_native_cross_host_redirect_drops_token(self):
        import urllib.request
        req=urllib.request.Request('https://api.github.com/archive',headers={'Authorization':'Bearer offline'})
        redirected=p.SafeRedirect().redirect_request(req,None,302,'redirect',{},'https://objects.githubusercontent.com/custody')
        self.assertIsNone(redirected.get_header('Authorization'))
        with self.assertRaises(p.Refusal):p.SafeRedirect().redirect_request(req,None,302,'redirect',{},'http://example.invalid/archive')

    def test_transition_writers_and_partial_recovery(self):
        # Transport and served-byte stub; production stage decisions are exercised.
        for stop in (None,'push-contracts','push-browser','public-contracts','public-browser','assets','complete'):
            state={};events=[];b=reviewed();release={'draft':True,'assets':[],'id':1}
            class Transport(Occupancy):
                def checked(self,path,method='GET',data=None):
                    if method=='PATCH':
                        events.append('complete')
                        if stop=='complete':raise p.Refusal('interruption')
                        release['draft']=False
                    return release
                def pages(self,path,key=None):
                    ident=p.PACKAGES[0] if 'contracts' in path else p.PACKAGES[1]
                    return [{'name':'0.3.0','id':1}] if 'state=active' in path and state.get(('org',ident)) else []
                def request(self,url,**kw):
                    if url.endswith('/v3/index.json'):return super().request(url,**kw)
                    ident=p.PACKAGES[0] if 'contracts' in url else p.PACKAGES[1]
                    exists=state.get(('public',ident))
                    return (200,{'versions':['0.2.0']+(['0.3.0'] if exists else [])},{}) if url.endswith('/index.json') else (200 if exists else 404,b'',{})
            def rb(kind,custody,output):
                if kind in ('org','public'):
                    if not all(state.get((kind,x)) for x in p.PACKAGES):raise p.Refusal('pair absent')
                elif not state.get('assets'):raise p.Refusal('assets absent')
                events.append('readback-'+kind)
            def cmd(args,**kw):
                if args[0]=='dotnet':
                    ident=p.PACKAGES[0] if 'Contracts' in args[3] else p.PACKAGES[1]
                    feed='public' if 'api.nuget.org' in args[5] else 'org'
                    if feed=='public':self.assertTrue(all(state.get(('org',x)) for x in p.PACKAGES));self.assertIn('readback-org',events)
                    state[(feed,ident)]=True;label=('push-' if feed=='org' else 'public-')+('contracts' if ident==p.PACKAGES[0] else 'browser');events.append(label)
                    if stop==label:raise p.Refusal('interruption after writer; receipt lost')
                elif str(args[0]).endswith('readback.sh'):
                    ident=args[-1];self.assertTrue(state.get((args[1],ident)));events.append('member-readback')
                else:
                    self.assertTrue(all(state.get((feed,x)) for feed in ('org','public') for x in p.PACKAGES));state['assets']=True;events.append('assets')
                    if stop=='assets':raise p.Refusal('interrupted assets receipt')
            with tempfile.TemporaryDirectory() as raw,patch.object(p,'verify_originals'),patch.object(p,'verify_transaction'),patch.object(p,'release_state',return_value=(200,release)),patch.object(p,'readback',side_effect=rb),patch.object(p,'command',side_effect=cmd),patch.dict(os.environ,{'FEED_TOKEN':'offline','NUGET_API_KEY':'offline'}):
                with self.assertRaises(p.Refusal):p.perform(Transport(),'public',b,pathlib.Path(raw),pathlib.Path(raw)/'premature')
                self.assertEqual(events,[])
                interrupted=False
                for phase in ('org','public','assets','complete'):
                    try:p.perform(Transport(),phase,b,pathlib.Path(raw),pathlib.Path(raw)/'receipts')
                    except p.Refusal:interrupted=True;break
                # Resume without repack; already-served members receive exact readback.
                stop=None
                for phase in ('org','public','assets','complete'):p.perform(Transport(),phase,b,pathlib.Path(raw),pathlib.Path(raw)/'resume')
                self.assertFalse(release['draft'])
                count=len(events)
                p.perform(Transport(),'complete',b,pathlib.Path(raw),pathlib.Path(raw)/'again')
                self.assertEqual(events[count:],['readback-org','readback-public','readback-assets'])
                self.assertEqual(events.count('push-contracts'),1);self.assertEqual(events.count('push-browser'),1)
                self.assertEqual(events.count('public-contracts'),1);self.assertEqual(events.count('public-browser'),1)

if __name__=='__main__':unittest.main()
