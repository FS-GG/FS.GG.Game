#!/usr/bin/env python3
"""Offline decisions against real adapter/YAML. No credentials, actors or feeds."""
import copy
import base64
import hashlib
import io
import struct
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
    assert '  push:' not in text and 'options: [readback, promote, recovery, inspect]' in text and 'default: readback' in text
    assert 'group: release-wasm-current' in text and 'cancel-in-progress: false' in text
    env = text.split("\nenv:\n",1)[1].split("\njobs:\n",1)[0]
    assert re.findall(r"(?m)^  DOTNET_PROCESSOR_COUNT: (.+)$",env)==['1'], "actual CPU bound must be one"
    assert not re.search(r"(?m)^  .*[/@].*:",env), "action identifier substituted for an environment key"
    jobs={m[1]:m[2] for m in re.finditer(r'(?ms)^  ([a-z]+):\n(.*?)(?=^  [a-z]+:\n|\Z)',text.split('jobs:\n',1)[1])}
    assert set(jobs)=={'preflight','admission','begin','org','public','assets','complete','readback','inspect'}
    for job,dependency in [('admission','preflight'),('begin','admission'),('org','begin'),('public','org'),('assets','public'),('complete','org, public, assets'),('readback','admission')]:
        assert 'needs: ['+dependency+']' in jobs[job]
    assert 'id-token: write' in jobs['admission'] and 'NuGet/login@8d196754b4036150537f80ac539e15c2f1028841' in jobs['admission']
    assert 'NUGET_EXCHANGE_VERIFIED:' in jobs['admission'] and 'promotion.py admit' in jobs['admission']
    for job in ['begin','org','public','assets','complete']:
        assert "if: inputs.mode == 'promote' || inputs.mode == 'recovery'" in jobs[job]
        assert 'p.download(p.Native()' in jobs[job] and 'promotion.py '+job in jobs[job]
        assert 'gh release create' not in jobs[job] and 'prepare.sh' not in jobs[job] and 'dotnet pack' not in jobs[job]
        assert 'GH_TOKEN: ${{ github.token }}' in jobs[job]
    assert 'TAG_CREDENTIAL_ROUTE: workflow-github-token' in jobs['begin'] and 'contents: write' in jobs['begin']
    # Every caller of complete active/deleted occupancy needs the workflow-token
    # grant established by native diagnostic 37154778966; scope it to those jobs.
    write_jobs={'admission','begin','org','public'}
    read_jobs={'assets','complete','readback','inspect'}
    for job,block in jobs.items():
        expected=['write'] if job in write_jobs else ['read'] if job in read_jobs else []
        grants=re.findall(r'(?m)^      packages: (\S+)$',block)
        assert grants==expected, 'wrong package permission for '+job
    contents_writers={'admission','begin','org','public','assets','complete','readback','inspect'}
    for job,block in jobs.items():
        grant=re.findall(r'(?m)^      contents: (\S+)$',block)
        assert grant==(['write'] if job in contents_writers else []), 'wrong draft visibility permission '+job
    assert "if: inputs.mode == 'inspect'" in jobs['inspect'] and 'promotion.py inspect' in jobs['inspect']
    assert all(x not in jobs['inspect'] for x in ('id-token:', 'NuGet/login', 'promotion.py admit', 'promotion.py begin', 'nuget push', 'stage-assets.sh'))
    assert 'EXECUTION_BINDING:' not in env and 'execution_binding:' not in text
    assert 'prepare.sh' not in text and 'Read-only pinned Rust model readiness' not in text
    assert 'fetch-depth: 0' in jobs['admission']
    assert text.count('packages: write')==len(write_jobs)
    assert text.split('\nenv:\n',1)[0].split('\npermissions:\n',1)[1]=='  contents: read\n', 'global permission widening'
    # Disable bytecode before SourceFileLoader runs: the module's own setting is
    # too late to prevent an inline import from dirtying the exact checkout.
    for job in ('begin','org','public','assets','complete','readback'):
        imports=re.findall(r"(?m)^          python3(?: -B)? -c 'import importlib\.util,json,pathlib;.*$",jobs[job])
        assert len(imports)==1 and imports[0].startswith("          python3 -B -c "), 'bytecode-writing custody import in '+job
    assert text.count("python3 -B -c 'import importlib.util,json,pathlib;")==6
    assert 'NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}' in jobs['public']
    assert 'secrets.NUGET_API_KEY' not in text and '--skip-duplicate' not in text
    assert '$GITHUB_SHA" == "$ACCEPTED_EXECUTOR' in jobs['preflight'] and 'git/ref/heads/main' in jobs['preflight']


def binding():
    return {'schema':p.SCHEMA,'repository':p.REPO,'workflow':'.github/workflows/wasm-shared.yml','workflowSha':p.P,'job':'qualify','event':'push','mode':'qualify','producer':p.P,'producerTree':p.TREE,'executor':p.P,'executorTree':p.TREE,'runId':100,'runAttempt':1,'version':p.VERSION,'manifestSha256':'c'*64,'archives':{name:'d'*64 for name in p.FILES},'inventories':{name:{'value':'e'*64} for name in p.FILES},'gates':{name:'success' for name in p.GATES},'evidence':{name:'e'*64 for name in p.OUTER_FILES-set(p.FILES)-{'preparation-binding.json'}}}

def reviewed(): return {'binding':binding(),'bindingSha256':'f'*64,'artifactId':200,'artifactSha256':'0'*64}

def native_data():
    b=binding()
    run={'id':100,'run_attempt':1,'head_sha':p.P,'head_branch':'main','path':b['workflow'],'event':'push','status':'completed','conclusion':'success','repository':{'full_name':p.REPO}}
    jobs=[{'name':p.QUALIFY_JOB,'run_id':100,'run_attempt':1,'head_sha':p.P,'conclusion':'success','steps':[{'name':x,'conclusion':'success'} for x in [*p.GATES,'Retain eligible originals']]}]
    artifact={'id':200,'expired':False,'digest':'sha256:'+'0'*64,'name':'wasm-eligible-100-1-'+p.VERSION,'workflow_run':{'id':100,'head_sha':p.P}}
    return run,jobs,artifact

class Occupancy:
    def __init__(self,mutant=None): self.mutant=mutant; self.writes=[]
    def pages(self,path,key=None):
        if self.mutant=='denied': raise p.Refusal('403 Unknown')
        if self.mutant=='deleted' and 'state=deleted' in path: return [{'name':'0.4.0','id':1}]
        if self.mutant=='active' and 'state=active' in path: return [{'name':'0.4.0','id':1}]
        if self.mutant=='malformed-org': return [{'id':1}]
        return [{'name':'0.2.0','id':1}]
    def request(self,url,**kwargs):
        if url.endswith('/v3/index.json'):
            return 200,{'resources':[{'@type':'PackageBaseAddress/3.0.0','@id':'https://api.nuget.org/v3-flatcontainer/'}]},{}
        if url.endswith('/index.json'):
            if self.mutant=='missing-index': return 404,None,{}
            if self.mutant=='malformed-index': return 200,{'versions':[{}]},{}
            return 200,{'versions':['0.1.1','0.2.0']+(['0.4.0'] if self.mutant=='unlisted' else [])},{}
        return (200 if self.mutant in ('contradiction','unlisted') else 404),b'',{}

class Tests(unittest.TestCase):
    def setUp(self):
        # Unit fixtures declare their own transaction and mode. Native preflight
        # inherits these inputs from the dispatched workflow; they must not turn
        # an ordinary fixture into a successor inspection or recovery writer.
        isolated = patch.dict(os.environ, {
            'EXECUTION_BINDING': '', 'REVIEWED_TUPLE': '', 'MODE': '',
            'FIRST_PROMOTION_RUN': '', 'FIRST_PROMOTION_ATTEMPT': '',
        })
        isolated.start()
        self.addCleanup(isolated.stop)

    def test_actual_yaml_and_causal_mutants(self):
        text=(ROOT/'.github/workflows/release-wasm.yml').read_text();validate_workflow(text)
        for before,after in [
            ('needs: [org]\n','needs: [begin]\n'),('options: [readback, promote, recovery, inspect]','options: [prepare, publish]'),
            ('DOTNET_PROCESSOR_COUNT: 1','DOTNET_PROCESSOR_COUNT: 2'),
            ('DOTNET_PROCESSOR_COUNT: 1','actions/setup-dotnet@bad_PROCESSOR_COUNT: 1'),
            ('default: readback','default: promote'),('TAG_CREDENTIAL_ROUTE: workflow-github-token','TAG_CREDENTIAL_ROUTE: PAT'),
            ('p.download(p.Native()','p.foreign(p.Native()'),('NUGET_EXCHANGE_VERIFIED:','EXCHANGE_ASSUMED:'),
            ('group: release-wasm-current','group: release-wasm-0.3.0'),('GH_TOKEN: ${{ github.token }}','GH_TOKEN: ${{ secrets.APP_TOKEN }}')]:
            self.assertIn(before,text)
            with self.subTest(before=before),self.assertRaises((AssertionError,KeyError,ValueError)): validate_workflow(text.replace(before,after))
        with self.assertRaises(AssertionError):validate_workflow(text.replace('on:\n','on:\n  push:\n    tags: [wasm/v*]\n',1))
    def test_deleted_occupancy_permissions_are_exactly_job_scoped(self):
        text=(ROOT/'.github/workflows/release-wasm.yml').read_text()
        validate_workflow(text)
        for job in ('preflight','admission','begin','org','public','assets','complete','readback','inspect'):
            pattern=r'(?ms)(^  '+job+r':\n.*?)(?=^  [a-z]+:\n|\Z)'
            block=re.search(pattern,text)[1]
            if job in ('admission','begin','org','public'):
                mutants=[block.replace('      packages: write\n',''),
                         block.replace('      packages: write','      packages: read')]
            else:
                mutants=[block.replace('      packages: read','      packages: write')
                         if '      packages: read' in block else
                         block.replace('    permissions:\n','    permissions:\n      packages: write\n')]
                if '    permissions:\n' not in block:
                    mutants=[block.replace('    runs-on:', '    permissions:\n      packages: write\n    runs-on:')]
            for mutant in mutants:
                with self.subTest(job=job),self.assertRaises(AssertionError):
                    validate_workflow(text.replace(block,mutant,1))
        with self.assertRaises(AssertionError):
            validate_workflow(text.replace('permissions:\n  contents: read',
                                           'permissions:\n  contents: read\n  packages: write',1))

    def test_each_inline_custody_import_requires_no_bytecode(self):
        text=(ROOT/'.github/workflows/release-wasm.yml').read_text()
        for job in ('begin','org','public','assets','complete','readback'):
            block=re.search(r'(?ms)^  '+job+r':\n.*?(?=^  [a-z]+:\n|\Z)',text)[0]
            mutant=block.replace("python3 -B -c 'import importlib.util,json,pathlib;",
                                 "python3 -c 'import importlib.util,json,pathlib;")
            self.assertNotEqual(block,mutant)
            with self.subTest(job=job),self.assertRaises(AssertionError):
                validate_workflow(text.replace(block,mutant,1))

    def test_cold_inline_import_preserves_exact_checkout_guards(self):
        import subprocess
        # A fresh process exercises Python's loader before the adapter body runs.
        # These private Git fixtures contain actual adapter bytes; no native API
        # or product command is invoked.
        code=("import importlib.util,json,os; "
              "s=importlib.util.spec_from_file_location('p','scripts/wasm-release/promotion.py'); "
              "p=importlib.util.module_from_spec(s); s.loader.exec_module(p); "
              "p.executor_checkout(json.loads(os.environ['CONTROL_BINDING']))")
        with tempfile.TemporaryDirectory(prefix='wasm-cold-custody-import.') as raw:
            for no_bytecode in (False,True):
                fixture=pathlib.Path(raw)/('safe' if no_bytecode else 'original')
                source=fixture/'scripts/wasm-release/promotion.py';source.parent.mkdir(parents=True)
                source.write_bytes((ROOT/'scripts/wasm-release/promotion.py').read_bytes())
                props=fixture/'eng/wasm-shared/version.props';props.parent.mkdir(parents=True);props.write_bytes((ROOT/'eng/wasm-shared/version.props').read_bytes())
                def git(*args):
                    return subprocess.check_output(['git','-C',str(fixture),*args],text=True,stderr=subprocess.PIPE).strip()
                git('init','-q');git('add','.')
                git('-c','user.name=Control','-c','user.email=control@example.invalid','commit','-qm','actual adapter fixture')
                head=git('rev-parse','HEAD');tree=git('rev-parse','HEAD^{tree}')
                binding={'executor':head,'executorTree':tree}
                env={'PATH':os.environ['PATH'],'GITHUB_SHA':head,'ACCEPTED_EXECUTOR':head,
                     'CONTROL_BINDING':json.dumps(binding)}
                args=[sys.executable,*(['-B'] if no_bytecode else []),'-c',code]
                def invoke(selected_env):
                    return subprocess.run(args,cwd=fixture,env=selected_env,text=True,
                                          stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=20)
                result=invoke(env)
                if not no_bytecode:
                    self.assertNotEqual(result.returncode,0)
                    self.assertIn('dirty or different executor tree',result.stderr)
                    self.assertEqual(git('status','--porcelain'),'?? scripts/wasm-release/__pycache__/')
                    continue
                self.assertEqual(result.returncode,0,result.stderr)
                self.assertEqual(git('status','--porcelain'),'')
                for change in ('tree','head','dirty'):
                    changed=dict(env)
                    if change=='tree':changed['CONTROL_BINDING']=json.dumps({**binding,'executorTree':'9'*40})
                    elif change=='head':changed['GITHUB_SHA']='9'*40
                    else:source.write_bytes(source.read_bytes()+b'\n# genuine source drift\n')
                    result=invoke(changed)
                    with self.subTest(change=change):
                        self.assertNotEqual(result.returncode,0)
                        self.assertIn('wrong executor checkout' if change=='head' else
                                      'dirty or different executor tree',result.stderr)
                self.assertFalse((fixture/'scripts/wasm-release/__pycache__').exists())

    def test_bounded_freeze_actual_caller_and_bad_inputs(self):
        p.custody_plan()
        with tempfile.TemporaryDirectory(prefix='wasm-freeze-inert.') as raw:
            root=pathlib.Path(raw);custody=root/'originals';custody.mkdir();runner=root/'runner';runner.mkdir()
            for name in (*p.FILES,*p.CUSTODY_METADATA,*p.BASELINE_LOGS):(custody/name).write_bytes(b'inert original/evidence')
            manifest={'artifacts':[{'file':name,'sha256':p.digest((custody/name).read_bytes())} for name in p.FILES]}
            (custody/'release-manifest.json').write_text(json.dumps(manifest))
            for name,path in p.EVIDENCE.items():
                source=runner/path;source.parent.mkdir(parents=True,exist_ok=True);source.write_bytes(b'inert evidence')
            env={'RUNNER_TEMP':str(runner),'GITHUB_OUTPUT':str(root/'outputs'),'GITHUB_REPOSITORY':p.REPO,'GITHUB_REF':'refs/heads/main','GITHUB_SHA':p.P,'GITHUB_WORKFLOW_SHA':p.P,'GITHUB_EVENT_NAME':'push','GITHUB_RUN_ID':'100','GITHUB_RUN_ATTEMPT':'1'}
            inventory={name:{'inert-member':p.digest(b'inert')} for name in p.FILES}
            with patch.dict(os.environ,env),patch.object(p,'git',return_value=''),patch.object(p,'inventories',return_value=inventory),patch.object(p,'verify_originals'):
                output=root/'eligible';p.freeze(custody,output)
                self.assertEqual({f.relative_to(output).as_posix() for f in output.rglob('*') if f.is_file()},p.OUTER_FILES)
                bound=p.load(output/'preparation-binding.json');p.validate_binding(bound)
                self.assertTrue(all(p.digest((output/name).read_bytes())==value for name,value in bound['evidence'].items()))
                for key,value in [('GITHUB_EVENT_NAME','pull_request'),('GITHUB_REF','refs/pull/1/merge'),('GITHUB_SHA','9'*40),('GITHUB_WORKFLOW_SHA','9'*40),('GITHUB_RUN_ATTEMPT','2'),('GITHUB_EVENT_NAME','workflow_dispatch')]:
                    target=root/('bad-'+key+'-'+value.replace('/','_'))
                    with self.subTest(key=key),patch.dict(os.environ,{key:value}),self.assertRaises(p.Refusal):p.freeze(custody,target)
                    self.assertFalse(target.exists())
                with patch.object(p,'MAX_ARCHIVE',1),self.assertRaises(p.Refusal):p.freeze(custody,root/'oversize-original')
                with patch.object(p,'MAX_METADATA',1),self.assertRaises(p.Refusal):p.freeze(custody,root/'oversize-metadata')
                with patch.object(p,'MAX_DIAGNOSTICS',1),self.assertRaises(p.Refusal):p.freeze(custody,root/'oversize-diagnostics')
                source=runner/next(iter(p.EVIDENCE.values()));original=source.read_bytes();source.unlink()
                with self.assertRaises(p.Refusal):p.freeze(custody,root/'missing')
                outside=root/'foreign';outside.write_bytes(original);source.symlink_to(outside)
                with self.assertRaises(p.Refusal):p.freeze(custody,root/'symlink')

    def test_live_protection_population_and_exact_checks_fail_closed(self):
        selected=p.required_checks();self.assertEqual(len(selected),19)
        branch={'protected':True,'protection':{'required_status_checks':{'checks':[{'context':name,'app_id':app} for name,app in selected],'contexts':[name for name,_ in selected]}}}
        rows=[{'name':name,'app':{'id':app},'head_sha':p.P,'status':'completed','conclusion':'success'} for name,app in selected]
        class Transport:
            def checked(self,path):return self.branch
            def pages(self,path,key):return self.rows
        n=Transport();n.branch=branch;n.rows=rows;p.verify_protection(n,binding())
        for mutation in ('not-protected','missing-required','wrong-required-app','missing-check','wrong-check-app','failed','skipped','wrong-source','incomplete','duplicate'):
            n.branch=copy.deepcopy(branch);n.rows=copy.deepcopy(rows)
            if mutation=='not-protected':n.branch['protected']=False
            elif mutation=='missing-required':n.branch['protection']['required_status_checks']['checks'].pop()
            elif mutation=='wrong-required-app':n.branch['protection']['required_status_checks']['checks'][0]['app_id']=1
            elif mutation=='missing-check':n.rows.pop()
            elif mutation=='wrong-check-app':n.rows[0]['app']['id']=1
            elif mutation in ('failed','skipped'):n.rows[0]['conclusion']=mutation
            elif mutation=='wrong-source':n.rows[0]['head_sha']='9'*40
            elif mutation=='incomplete':n.rows[0]['status']='in_progress'
            else:n.rows.append(n.rows[0])
            with self.subTest(mutation=mutation),self.assertRaises(p.Refusal):p.verify_protection(n,binding())

    def test_closed_binding(self):
        p.validate_tuple(reviewed())
        for key,value in [('producer','9'*40),('executor','main'),('producerTree','9'*40),('version','0.2.0'),('mode','publish'),('runAttempt',0),('repository','foreign/repo'),('version','0.3.0'),('event','pull_request'),('workflowSha','9'*40),('job','generate-traces'),('workflow','.github/workflows/release-wasm.yml'),('executor','9'*40)]:
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
        for target,key,value in [('run','run_attempt',2),('run','conclusion','failure'),('run','head_sha','9'*40),('run','event','pull_request'),('run','head_branch','feature'),('artifact','expired',True),('artifact','digest','sha256:'+'9'*64),('artifact','name','wasm-ineligible-100-2')]:
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
            return [{'name':'x','id':i} for i in range(1,101)] if path.endswith('&page=1') else [{'name':'last','id':101}]
        with patch.object(native,'checked',read):self.assertEqual(len(native.pages('orgs/FS-GG/versions')),101)
        self.assertEqual(len(calls),2)
        for mutant in ({'total_count':2,'jobs':[{'id':1}]}, {'total_count':1,'jobs':[]}, {'total_count':'Unknown','jobs':[]}):
            with patch.object(native,'checked',return_value=mutant),self.assertRaises(p.Refusal):native.pages('jobs','jobs')
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
        self.assertIn('$output/packages/fs.gg.wasm.browser/$version/',selected)
        self.assertIn('verify-wasm-supervisor.sh" --feed-only',selected)
        self.assertIn('verify-wasm-supervisor-browser.sh" --feed-only',selected)
        self.assertIn('official-core-joins.json',selected)
        runner=(ROOT/'scripts/verify-wasm-supervisor.sh').read_text()
        self.assertEqual(runner.count('if [[ "$mode" == custody ]]; then'),2)
        self.assertIn('RestoreLockedMode=true',runner);self.assertIn('NU1403',runner)
    def test_installed_cache_member_lock_worker_substitution(self):
        import zipfile
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);cache=root/'packages';record=root/'promotion-binding.json';lock=root/'packages.lock.json'
            r=reviewed();dependencies={}
            for ident in p.PACKAGES:
                folder=cache/ident.lower()/'0.4.0';folder.mkdir(parents=True)
                archive=folder/(ident.lower()+'.0.4.0.nupkg')
                with zipfile.ZipFile(archive,'w') as z:z.writestr('ordinary.dll',b'real-payload');z.writestr('module-worker.mjs',b'real-worker')
                r['binding']['archives'][ident+'.0.4.0.nupkg']=p.digest(archive.read_bytes())
                r['binding']['inventories'][ident+'.0.4.0.nupkg']={'ordinary.dll':p.digest(b'real-payload'),'module-worker.mjs':p.digest(b'real-worker')}
                content=base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()
                (folder/(ident.lower()+'.0.4.0.nupkg.sha512')).write_text(content)
                (folder/'.nupkg.metadata').write_text(json.dumps({'version':2,'contentHash':content,'source':'https://nuget.pkg.github.com/FS-GG/index.json'}))
                dependencies[ident]={'resolved':'0.4.0','contentHash':content}
            record.write_text(json.dumps({'preparation':r}));lock.write_text(json.dumps({'dependencies':{'net10.0':dependencies}}))
            with patch.dict(os.environ,{'ACCEPTED_EXECUTOR':p.P}):
                p.check_installed(record,cache,'https://nuget.pkg.github.com/FS-GG/index.json',lock)
                ident=p.PACKAGES[1];folder=cache/ident.lower()/'0.4.0';archive=folder/(ident.lower()+'.0.4.0.nupkg');original=archive.read_bytes()
                sidecar=folder/(ident.lower()+'.0.4.0.nupkg.sha512');metadata=folder/'.nupkg.metadata';original_sidecar=sidecar.read_text();original_metadata=metadata.read_text()
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
                    if mutation in ('worker','ordinary','signature-disguised-worker'):
                        body=archive.read_bytes();content=p.package_content_hash(body)
                        sidecar.write_text(base64.b64encode(hashlib.sha512(body).digest()).decode())
                        metadata.write_text(json.dumps({'version':2,'contentHash':content,'source':'https://nuget.pkg.github.com/FS-GG/index.json'}));dependencies[ident]['contentHash']=content
                    lock.write_text(json.dumps({'dependencies':{'net10.0':dependencies}}))
                    with self.subTest(mutation=mutation),self.assertRaisesRegex(p.Refusal,'restored payload drift' if mutation in ('worker','ordinary') else ''):p.check_installed(record,cache,'https://nuget.pkg.github.com/FS-GG/index.json',lock)
                    archive.write_bytes(original);sidecar.write_text(original_sidecar);metadata.write_text(original_metadata);dependencies=saved
                    lock.write_text(json.dumps({'dependencies':{'net10.0':dependencies}}))
    def test_native_archive_media_roles_and_cross_host_auth(self):
        class Reply:
            status=200; headers={}
            def __init__(self): self.body=io.BytesIO(b'PK opaque archive bytes')
            def __enter__(self): return self
            def __exit__(self,*args): pass
            def read(self,size): return self.body.read(size)
        observed=[]
        class Opener:
            def open(self,request,**kwargs): observed.append(request); return Reply()
        routes=[('actions/artifacts/200/zip','GET','application/vnd.github+json'),
                ('actions/runs/100/logs','GET','application/vnd.github+json'),
                ('actions/runs/100/attempts/2/logs','GET','application/vnd.github+json'),
                ('releases/assets/200','GET','application/octet-stream'),
                ('actions/artifacts/200/zip','POST','application/octet-stream'),
                ('actions/artifacts/0/zip','GET','application/octet-stream')]
        with patch.dict(os.environ,{'GH_TOKEN':'inert-fixture'}),patch.object(p.urllib.request,'build_opener',return_value=Opener()):
            for path,method,accept in routes:
                with self.subTest(path=path,method=method):
                    status,body,_=p.Native().request('https://api.github.com/repos/'+p.REPO+'/'+path,method=method,github=True,binary=True)
                    self.assertEqual((status,body),(200,b'PK opaque archive bytes'));self.assertEqual(observed[-1].get_header('Accept'),accept)
            p.Native().request('https://api.github.com/repos/other/repo/actions/artifacts/200/zip',github=True,binary=True)
            self.assertEqual(observed[-1].get_header('Accept'),'application/octet-stream')
        request=observed[0]
        redirect=p.SafeRedirect().redirect_request(request,None,302,'',{},'https://objects.githubusercontent.com/archive.zip')
        self.assertIsNone(redirect.get_header('Authorization'))
        same=p.SafeRedirect().redirect_request(request,None,302,'',{},request.full_url)
        self.assertEqual(same.get_header('Authorization'),'Bearer inert-fixture')

    def test_download_http_unknown_is_distinct_from_digest_drift(self):
        run,jobs,artifact=native_data()
        class Transport:
            def checked(self,path): return run if '/attempts/' in path else artifact
            def pages(self,*args): return jobs
            def request(self,*args,**kwargs): return self.response
        for response,reason in [((415,None,{}),'preparation artifact download status 415, Unknown'),
                                ((403,None,{}),'preparation artifact download status 403, Unknown'),
                                ((200,b'wrong immutable bytes',{}),'downloaded artifact digest mismatch')]:
            n=Transport();n.response=response
            with tempfile.TemporaryDirectory() as raw,patch.object(p,'execution_checkout'),patch.object(p,'verify_protection'),self.assertRaisesRegex(p.Refusal,'^'+re.escape(reason)+'$'):
                p.download(n,reviewed(),pathlib.Path(raw)/'custody')

    @staticmethod
    def package_fixture(signature_at=None, names=('a.txt','b.txt'), extra=b'', descriptor=False, payloads=None):
        class Stream(io.BytesIO):
            def seek(self,*args): raise OSError('source fixture is nonseekable')
        out=Stream() if descriptor else io.BytesIO();entries=list(names)
        if signature_at is not None: entries.insert(signature_at,'.signature.p7s')
        with p.zipfile.ZipFile(out,'w') as archive:
            for name in entries:
                info=p.zipfile.ZipInfo(name,(2020,1,1,0,0,0));info.compress_type=0 if name=='.signature.p7s' else 8;info.extra=extra
                archive.writestr(info,b'not a real signature' if name=='.signature.p7s' else (payloads or {}).get(name,('payload:'+name).encode()))
        return out.getvalue()

    def test_compressed_unsigned_content_hash_signature_positions_and_descriptors(self):
        for descriptor in (False,True):
            original=self.package_fixture(descriptor=descriptor);expected=base64.b64encode(hashlib.sha512(original).digest()).decode()
            self.assertEqual(p.package_content_hash(original),expected)
            for position in (0,1,2):
                raw=self.package_fixture(position,descriptor=descriptor)
                self.assertNotEqual(base64.b64encode(hashlib.sha512(raw).digest()).decode(),expected)
                self.assertEqual(p.package_content_hash(raw),expected)
        # Central directory order and physical entry order are distinct roles.
        def reverse_central(raw):
            end=raw.rfind(b'PK\x05\x06');start=struct.unpack_from('<I',raw,end+16)[0];pos=start;rows=[]
            while pos<end:
                lengths=struct.unpack_from('<HHH',raw,pos+28);length=46+sum(lengths);rows.append(raw[pos:pos+length]);pos+=length
            return raw[:start]+b''.join(reversed(rows))+raw[end:]
        unsigned=reverse_central(self.package_fixture());signed=reverse_central(self.package_fixture(1))
        self.assertEqual(p.package_content_hash(signed),base64.b64encode(hashlib.sha512(unsigned).digest()).decode())

    def test_content_hash_refuses_ambiguous_zip_layout_and_paths(self):
        raw=self.package_fixture(2);end=raw.rfind(b'PK\x05\x06');start=struct.unpack_from('<I',raw,end+16)[0]
        def change(offset,fmt,value):
            modified=bytearray(raw);struct.pack_into(fmt,modified,offset,value);return bytes(modified)
        mutants=[raw[:20],raw[:-1],raw+b'unbound tail',change(end+4,'<H',1),change(end+10,'<H',65535),
                 change(end+12,'<I',1),change(start+34,'<H',1),change(start+6,'<H',45),
                 change(start+8,'<H',1),change(start+10,'<H',12),change(start+20,'<I',0xffffffff),
                 change(4,'<H',45),change(18,'<I',1),self.package_fixture(2,names=('../a','b.txt')),
                 self.package_fixture(2,names=('/a.txt','b.txt')),self.package_fixture(2,names=('a\\xx','b.txt')),
                 self.package_fixture(2,extra=struct.pack('<HHQ',1,8,0))]
        # Duplicate offsets, duplicate names and local/central name mismatch.
        second=start+46+sum(struct.unpack_from('<HHH',raw,start+28));mutants.append(change(second+42,'<I',0))
        duplicate=bytearray(raw);duplicate[second+46:second+51]=b'a.txt';mutants.append(bytes(duplicate))
        mismatch=bytearray(raw);mismatch[30:35]=b'c.txt';mutants.append(bytes(mismatch))
        symlink=bytearray(raw);struct.pack_into('<I',symlink,start+38,0o120777<<16);mutants.append(bytes(symlink))
        with p.zipfile.ZipFile(io.BytesIO(raw))as archive:signature_offset=archive.getinfo('.signature.p7s').header_offset
        sig_central=second+46+sum(struct.unpack_from('<HHH',raw,second+28))
        utf8_signature=bytearray(raw);struct.pack_into('<H',utf8_signature,sig_central+8,0x800);struct.pack_into('<H',utf8_signature,signature_offset+6,0x800);mutants.append(bytes(utf8_signature))
        for index,mutant in enumerate(mutants):
            with self.subTest(index=index),self.assertRaises(p.Refusal):p.package_content_hash(mutant)

    def test_installed_signed_hash_roles_are_independent_and_payload_locked(self):
        feed='https://api.nuget.org/v3/index.json'
        with tempfile.TemporaryDirectory() as raw,patch.dict(os.environ,{'ACCEPTED_EXECUTOR':p.P}):
            root=pathlib.Path(raw);cache=root/'packages';record=root/'promotion-binding.json';lock=root/'packages.lock.json';r=reviewed();deps={};originals={}
            for ident in p.PACKAGES:
                folder=cache/ident.lower()/'0.4.0';folder.mkdir(parents=True);archive=folder/(ident.lower()+'.0.4.0.nupkg')
                unsigned=self.package_fixture();signed=self.package_fixture(1);archive.write_bytes(signed);originals[ident]=signed
                r['binding']['archives'][ident+'.0.4.0.nupkg']=p.digest(unsigned)
                with p.zipfile.ZipFile(io.BytesIO(unsigned))as z:r['binding']['inventories'][ident+'.0.4.0.nupkg']={name:p.digest(z.read(name))for name in z.namelist()}
                content=base64.b64encode(hashlib.sha512(unsigned).digest()).decode();self.assertNotEqual(base64.b64encode(hashlib.sha512(signed).digest()).decode(),content);deps[ident]={'resolved':'0.4.0','contentHash':content}
                (folder/(ident.lower()+'.0.4.0.nupkg.sha512')).write_text(base64.b64encode(hashlib.sha512(signed).digest()).decode())
                (folder/'.nupkg.metadata').write_text(json.dumps({'version':2,'contentHash':content,'source':feed}))
            record.write_text(json.dumps({'preparation':r}));lock.write_text(json.dumps({'dependencies':{'net10.0':deps}}))
            with patch.object(p,'repository_signature',return_value='source-controlled verification fixture')as verify:
                p.check_installed(record,cache,feed,lock);self.assertEqual(verify.call_count,2)
            ident=p.PACKAGES[0];folder=cache/ident.lower()/'0.4.0';archive=folder/(ident.lower()+'.0.4.0.nupkg');sidecar=folder/(ident.lower()+'.0.4.0.nupkg.sha512');metadata=folder/'.nupkg.metadata'
            baseline_sidecar=sidecar.read_text();baseline_metadata=metadata.read_text();baseline_deps=copy.deepcopy(deps)
            for mutation in ('sidecar','metadata-hash','metadata-source','metadata-version','metadata-malformed','metadata-missing','metadata-duplicate','lock','unsigned-public','ordinary','payload','corrupt-compression','cache-drift','signature-trust','foreign-feed'):
                if mutation=='sidecar':sidecar.write_text(deps[ident]['contentHash'])
                elif mutation.startswith('metadata-'):
                    value=json.loads(baseline_metadata)
                    if mutation=='metadata-hash':value['contentHash']='wrong'
                    elif mutation=='metadata-source':value['source']='https://other.example/index.json'
                    elif mutation=='metadata-version':value['version']=True
                    elif mutation=='metadata-malformed':metadata.write_text('{')
                    elif mutation=='metadata-missing':metadata.unlink()
                    elif mutation=='metadata-duplicate':metadata.write_text(baseline_metadata[:-1]+',\"source\":'+json.dumps(feed)+'}')
                    if mutation not in ('metadata-malformed','metadata-missing','metadata-duplicate'):metadata.write_text(json.dumps(value))
                elif mutation=='lock':deps[ident]['contentHash']='wrong'
                elif mutation in ('unsigned-public','ordinary','payload','corrupt-compression'):
                    body=self.package_fixture(None if mutation=='unsigned-public'else 1,names=('a.txt','changed.txt')if mutation=='ordinary'else('a.txt','b.txt'),payloads={'a.txt':b'changed payload'}if mutation=='payload'else None)
                    if mutation=='corrupt-compression':
                        changed=bytearray(body);nl,xl=struct.unpack_from('<HH',body,26);changed[30+nl+xl]=255;body=bytes(changed)
                    archive.write_bytes(body)
                    # Keep every cache hash self-consistent to reach signature/member guards.
                    content=p.package_content_hash(body);sidecar.write_text(base64.b64encode(hashlib.sha512(body).digest()).decode());metadata.write_text(json.dumps({'version':2,'contentHash':content,'source':feed}));deps[ident]['contentHash']=content
                lock.write_text(json.dumps({'dependencies':{'net10.0':deps}}))
                def verify_signature(path):
                    if mutation=='signature-trust':raise p.Refusal('genuine trust refuses')
                    if mutation=='cache-drift':path.write_bytes(b'cache changed during verification')
                with self.subTest(mutation=mutation),patch.object(p,'repository_signature',side_effect=verify_signature),self.assertRaises(p.Refusal):
                    p.check_installed(record,cache,'https://other.example/index.json'if mutation=='foreign-feed'else feed,lock)
                archive.write_bytes(originals[ident]);sidecar.write_text(baseline_sidecar);metadata.write_text(baseline_metadata);deps=copy.deepcopy(baseline_deps)
            lock.write_text(json.dumps({'dependencies':{'net10.0':deps}}))
            with patch.object(p,'repository_signature',side_effect=AssertionError('org signature refuses before trust')), self.assertRaises(p.Refusal):
                p.check_installed(record,cache,'https://nuget.pkg.github.com/FS-GG/index.json',lock)

    def test_admission_refuses_before_download_or_writer(self):
        env={'GITHUB_SHA':p.P,'ACCEPTED_EXECUTOR':p.P}
        class Transport:
            def checked(self,path,*args):return {'object':{'sha':'9'*40}}
            def pages(self,*args):raise AssertionError('must refuse before artifact access')
            def request(self,*args,**kwargs):raise AssertionError('must refuse before download')
        with tempfile.TemporaryDirectory() as raw,patch.dict(os.environ,dict(env,GITHUB_REPOSITORY=p.REPO,GITHUB_REF='refs/heads/main',GITHUB_RUN_ATTEMPT='1')),patch.object(p,'executor_checkout'):
            with self.assertRaises(p.Refusal):p.download(Transport(),reviewed(),pathlib.Path(raw)/'custody')
        with patch.dict(os.environ,{'TAG_CREDENTIAL_ROUTE':'app-token'}),patch.object(p,'verify_originals',side_effect=AssertionError('must refuse before custody/writer')):
            with self.assertRaises(p.Refusal):p.begin(Transport(),reviewed(),'.',False)
    def test_artifact_digest_safe_members_and_binding_refusal(self):
        import io,zipfile
        for mutation in ('none','artifact-digest','traversal','missing-binding','changed-binding','changed-evidence','extra-cache','member-size','evidence-total'):
            r=reviewed();body=io.BytesIO();binding_body=(json.dumps(r['binding'])+'\n').encode()
            r['bindingSha256']=p.digest(binding_body)
            with zipfile.ZipFile(body,'w') as z:
                for name in p.OUTER_FILES-{'preparation-binding.json'}:z.writestr(name,b'changed' if mutation=='changed-evidence' and name=='evidence/contracts.log' else b'evidence')
                if mutation=='extra-cache':z.writestr('packages/foreign.nupkg',b'ineligible cache')
                r['binding']['evidence']={name:p.digest(b'evidence') for name in r['binding']['evidence']}
                binding_body=(json.dumps(r['binding'])+'\n').encode();r['bindingSha256']=p.digest(binding_body)
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
            with tempfile.TemporaryDirectory() as raw,patch.dict(os.environ,{'GITHUB_SHA':p.P,'ACCEPTED_EXECUTOR':p.P}),patch.object(p,'verify_originals') as verify,patch.object(p,'executor_checkout'),patch.object(p,'verify_protection'),patch.object(p,'execution_checkout'),patch.object(p,'MAX_METADATA',1 if mutation=='member-size' else p.MAX_METADATA),patch.object(p,'MAX_DIAGNOSTICS',1 if mutation=='evidence-total' else p.MAX_DIAGNOSTICS):
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
                return [{'name':'0.4.0','id':1}] if 'browser' in path and 'state=active' in path else []
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
                            return [{'name':'0.4.0','id':1}] if feed=='org' and 'browser' in path and 'state=active' in path else []
                        def request(self,url,**kwargs):
                            if feed!='public' or url.endswith('/v3/index.json'):return super().request(url,**kwargs)
                            exists='browser' in url
                            return (200,{'versions':['0.2.0']+(['0.4.0'] if exists else [])},{}) if url.endswith('/index.json') else (200 if exists else 404,b'',{})
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
                    return [{'name':'0.4.0','id':1}] if 'browser' in path and 'state=active' in path else []
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
        with patch.dict(os.environ,{'GITHUB_SHA':head,'ACCEPTED_EXECUTOR':head}):
            p.executor_checkout(b)
            wrong=dict(b);wrong['executorTree']='9'*40
            with self.assertRaises(p.Refusal):p.executor_checkout(wrong)
        with patch.dict(os.environ,{'GITHUB_SHA':head,'ACCEPTED_EXECUTOR':'9'*40}),self.assertRaises(p.Refusal):p.executor_checkout(b)
        spec=importlib.util.spec_from_file_location('installed_binding',ROOT/'scripts/wasm-release/bind-installed-release.py')
        binder=importlib.util.module_from_spec(spec);spec.loader.exec_module(binder)
        with patch.dict(os.environ,{'GITHUB_SHA':head,'ACCEPTED_EXECUTOR':head}):
            binder.bind_executor(head,head,p.VERSION)
            for source,executor,version in [('9'*40,head,p.VERSION),(head,'9'*40,p.VERSION),(head,head,'0.3.0')]:
                with self.assertRaises(AssertionError):binder.bind_executor(source,executor,version)
        with patch.dict(os.environ,{'EXECUTION_BINDING':'{"schema":"historical"}'}),self.assertRaises(p.Refusal):p.execution_input()

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
                    return [{'name':'0.4.0','id':1}] if 'state=active' in path and state.get(('org',ident)) else []
                def request(self,url,**kw):
                    if url.endswith('/v3/index.json'):return super().request(url,**kw)
                    ident=p.PACKAGES[0] if 'contracts' in url else p.PACKAGES[1]
                    exists=state.get(('public',ident))
                    return (200,{'versions':['0.2.0']+(['0.4.0'] if exists else [])},{}) if url.endswith('/index.json') else (200 if exists else 404,b'',{})
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

    def test_draft_resolver_actual_transport_pagination_and_unknowns(self):
        release={'id':402763234,'tag_name':'wasm/v0.4.0','target_commitish':p.P,'draft':True,'prerelease':False,'assets':[]}
        class Transport(p.Native):
            def __init__(self,mutation=None):super().__init__();self.mutation=mutation;self.calls=[]
            def api(self,path,method='GET',data=None):
                self.calls.append(path)
                if '/git/ref/' in path:return 200,{'object':{'type':'commit','sha':p.P}},{}
                if '/releases/tags/' in path:return (200,dict(release,draft=False),{}) if self.mutation=='published' else (404,None,{})
                if '/releases/402763234/assets?' in path:return 200,[],{}
                if path.endswith('/releases/402763234'):
                    row=dict(release,draft=False) if self.mutation=='published' else copy.deepcopy(release)
                    if self.mutation=='disagreement':row['assets']=[{'id':1}]
                    return 200,row,{}
                page=int(path.rsplit('=',1)[1])
                if self.mutation=='denied':return 403,None,{}
                if self.mutation=='timeout':raise TimeoutError('bounded injected timeout')
                if self.mutation=='oversized':return 200,[{'id':i,'tag_name':'other'} for i in range(1,102)],{}
                if self.mutation=='repeat':return 200,[{'id':i,'tag_name':'other'} for i in range(1,101)],{}
                if self.mutation=='cap':return 200,[{'id':page*100+i,'tag_name':'other'} for i in range(1,101)],{}
                if self.mutation=='malformed':return 200,[{'id':1}],{}
                if page==1:return 200,[{'id':i,'tag_name':'other'} for i in range(1,101)],{}
                if self.mutation=='hidden':return 200,[],{}
                if self.mutation=='duplicate':return 200,[release,dict(release,id=402763235)],{}
                return 200,[dict(release,draft=False) if self.mutation=='published' else release],{}
        with patch.dict(os.environ,{'EXECUTION_BINDING':''}):
            n=Transport();self.assertEqual(p.release_state(n),(200,release));self.assertTrue(any('page=2' in x for x in n.calls))
            self.assertFalse(p.release_state(Transport('published'))[1]['draft'])
            for mutation in ('disagreement','denied','timeout','oversized','repeat','cap','malformed','duplicate'):
                with self.subTest(mutation=mutation),self.assertRaises((p.Refusal,TimeoutError)):p.release_state(Transport(mutation))

    def test_historical_admission_missing_occupancy_cannot_mean_absence(self):
        import io,zipfile
        r=reviewed();record={'schema':'fsgg.wasm.admission/v1','reviewed':r,'mode':'promote','run':200,'attempt':1,'exchangeVerified':True,'occupancy':{k:False for k in p.OCCUPANCY_KEYS},'observedAt':'now','nugetAccountSha256':p.digest(b'offline'),'publicModeratorRemovalHistory':'Unknown','atomicReservation':'Unknown'}
        class Transport:
            def __init__(self,record):self.record=record
            def checked(self,path):return {'id':200,'run_attempt':1,'repository':{'full_name':p.REPO},'head_sha':r['binding']['executor'],'path':'.github/workflows/release-wasm.yml','event':'workflow_dispatch'}
            def pages(self,path,key=None):
                if path.endswith('/jobs'):return [{'name':'admission','conclusion':'success'}]
                raw=io.BytesIO()
                with zipfile.ZipFile(raw,'w')as z:z.writestr('admission-binding.json',json.dumps(self.record))
                self.body=raw.getvalue();return [{'id':1,'name':'wasm-admission-200-1','expired':False,'workflow_run':{'id':200,'head_sha':r['binding']['executor']},'digest':'sha256:'+p.digest(self.body)}]
            def request(self,*args,**kwargs):return 200,self.body,{}
        with patch.dict(os.environ,{'NUGET_ACCOUNT':'offline','EXECUTION_BINDING':''}):
            p.verify_admission(Transport(record),r,200,1)
            for key in p.OCCUPANCY_KEYS:
                mutant=copy.deepcopy(record);mutant['occupancy'].pop(key)
                with self.assertRaises(p.Refusal):p.verify_admission(Transport(mutant),r,200,1)

    def test_asset_shell_callers_use_actual_shared_resolver(self):
        import subprocess
        # The wrapper injects transport into the real CLI entry, preserving both
        # shell callers and every release/asset decision. Original archive guards
        # are covered separately; no product or provider process runs here.
        with tempfile.TemporaryDirectory(prefix='wasm-asset-caller-control.')as raw:
            root=pathlib.Path(raw);tools=root/'bin';tools.mkdir();custody=root/'custody';custody.mkdir()
            for name in ('fsgg-wasm-sdk-0.4.0.tar.gz','release-manifest.json','SHA256SUMS'):(custody/name).write_bytes(name.encode())
            (custody/'release-manifest.json').write_text(json.dumps({'version':'0.4.0'}))
            (root/'reviewed.json').write_text(json.dumps(reviewed()))
            wrapper=tools/'python3';wrapper.write_text('#!'+sys.executable+'\n'+'''import importlib.util,sys,os,json,pathlib,hashlib,io,zipfile
sys.dont_write_bytecode=True
args=sys.argv[1:];args=args[1:] if args[0]=='-B' else args
if args[0]=='-c':
 import subprocess
 sys.exit(subprocess.call([sys.executable]+args))
s=importlib.util.spec_from_file_location('promotion',args[0]);p=importlib.util.module_from_spec(s);s.loader.exec_module(p)
root=pathlib.Path(os.environ['FIXTURE_ROOT']);c=root/'custody'
r=json.loads((root/'reviewed.json').read_text());journal={'schema':'fsgg.wasm.promotion/v1','preparation':r,'firstPromotionRun':200,'firstPromotionAttempt':1,'producer':p.P,'tag':'wasm/v0.4.0'}
(c/'promotion-binding.json').write_text(json.dumps(journal))
record={'schema':'fsgg.wasm.admission/v1','reviewed':r,'mode':'promote','run':200,'attempt':1,'exchangeVerified':True,'occupancy':{k:False for k in p.OCCUPANCY_KEYS},'observedAt':'now','nugetAccountSha256':p.digest(b'offline'),'publicModeratorRemovalHistory':'Unknown','atomicReservation':'Unknown'}
raw=io.BytesIO()
with zipfile.ZipFile(raw,'w')as z:z.writestr('admission-binding.json',json.dumps(record))
admission=raw.getvalue()
rows=[dict(id=i+1,name=n,url=f'https://api.github.com/repos/{p.REPO}/releases/assets/{i+1}',size=len((c/n).read_bytes()),digest='sha256:'+p.digest((c/n).read_bytes()))for i,n in enumerate(('fsgg-wasm-sdk-0.4.0.tar.gz','release-manifest.json','SHA256SUMS','promotion-binding.json'))]
release=dict(id=402763234,tag_name='wasm/v0.4.0',target_commitish=p.P,draft=True,prerelease=False,assets=rows)
class N(p.Native):
 def api(self,path,method='GET',data=None):
  assert method=='GET'
  if '/actions/runs/200/attempts/1/jobs' in path:return 200,{'total_count':1,'jobs':[dict(id=1,name='admission',conclusion='success')]},{}
  if '/actions/runs/200/artifacts' in path:return 200,{'total_count':1,'artifacts':[dict(id=1,name='wasm-admission-200-1',expired=False,workflow_run=dict(id=200,head_sha=r['binding']['executor']),digest='sha256:'+p.digest(admission))]},{}
  if '/actions/runs/200/attempts/1' in path:return 200,dict(id=200,run_attempt=1,repository=dict(full_name=p.REPO),head_sha=r['binding']['executor'],path='.github/workflows/release-wasm.yml',event='workflow_dispatch'),{}
  if '/git/ref/'in path:return 200,dict(object=dict(type='commit',sha=p.P)),{}
  if '/releases/tags/'in path:return 404,None,{}
  if '/releases/402763234/assets?'in path:return 200,rows,{}
  if '/releases/402763234'in path:return 200,release,{}
  return 200,[release],{}
 def request(self,url,**kwargs):
  assert kwargs.get('method','GET')=='GET'
  if '/actions/artifacts/1/zip' in url:return 200,admission,{}
  row=next(x for x in rows if x['url']==url);body=(c/row['name']).read_bytes()
  return 200,body+ (b'foreign' if os.environ.get('MUTANT') else b''),{}
p.Native=N;p.verify_originals=lambda *a:None
sys.argv=[args[0]]+args[1:];p.main()
''');wrapper.chmod(0o755)
            env=dict(os.environ,PATH=str(tools)+':'+os.environ['PATH'],RUNNER_TEMP=str(root),FIXTURE_ROOT=str(root),EXECUTION_BINDING='',NUGET_ACCOUNT='offline')
            for script,args in [('stage-assets.sh',[str(custody)]),('readback.sh',['assets',str(custody),str(root/'readback-assets')])]:
                command=[str(ROOT/'scripts/wasm-release'/script),*args]
                result=subprocess.run(command,env=env,capture_output=True,text=True,timeout=20);self.assertEqual(result.returncode,0,result.stderr)
                result=subprocess.run(command,env=dict(env,MUTANT='served-substitution'),capture_output=True,text=True,timeout=20);self.assertNotEqual(result.returncode,0)

    def test_inspect_excludes_all_mutating_jobs_and_cannot_admit(self):
        text=(ROOT/'.github/workflows/release-wasm.yml').read_text();validate_workflow(text)
        for job in ('admission','begin','org','public','assets','complete'):
            block=re.search(r'(?ms)^  '+job+r':\n.*?(?=^  [a-z]+:\n|\Z)',text)[0]
            self.assertNotIn("inputs.mode == 'inspect'",block)
        for job in ('admission','org','public','readback','inspect'):
            block=re.search(r'(?ms)^  '+job+r':\n.*?(?=^  [a-z]+:\n|\Z)',text)[0]
            mutant=block.replace('      contents: write','      contents: read')
            with self.assertRaises(AssertionError):validate_workflow(text.replace(block,mutant))

    def test_org_public_binary_readback_cap_at_actual_curl(self):
        import http.server,ssl,threading,subprocess,shutil
        text=(ROOT/'scripts/wasm-release/readback.sh').read_text()
        function=text[text.index('download() {'):text.index('\ncase "$kind"')]
        self.assertIn('curl --max-filesize 536870912 ',function)
        with tempfile.TemporaryDirectory(prefix='wasm-readback-cap-control.')as raw:
            root=pathlib.Path(raw);cert=root/'cert.pem';key=root/'key.pem'
            subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-nodes','-days','1','-subj','/CN=localhost','-keyout',str(key),'-out',str(cert)],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=10)
            class Handler(http.server.BaseHTTPRequestHandler):
                def do_GET(self):
                    self.send_response(200);self.send_header('Content-Length','536870913');self.end_headers()
                    try:self.wfile.write(b'x'*1024)
                    except (BrokenPipeError,ConnectionResetError):pass
                def log_message(self,*args):pass
            server=http.server.HTTPServer(('127.0.0.1',0),Handler)
            context=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER);context.load_cert_chain(cert,key);server.socket=context.wrap_socket(server.socket,server_side=True)
            thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
            tools=root/'bin';tools.mkdir();curl=tools/'curl';actual=shutil.which('curl')
            curl.write_text('#!/bin/sh\nexec '+actual+' --cacert "'+str(cert)+'" "$@"\n');curl.chmod(0o755)
            try:
                for bounded in (True,False):
                    destination=root/('bounded' if bounded else 'known-bad-unbounded')
                    selected=function if bounded else function.replace('--max-filesize 536870912 ','')
                    code='set -euo pipefail\ndeadline=$((SECONDS+1))\n'+selected+'\ndownload "$1" "$2"\n'
                    result=subprocess.run(['bash','-c',code,'control',f'https://localhost:{server.server_port}/oversized',str(destination)],env=dict(os.environ,PATH=str(tools)+':'+os.environ['PATH']),capture_output=True,text=True,timeout=5)
                    self.assertNotEqual(result.returncode,0)
                    if bounded:
                        self.assertIn('Maximum file size exceeded',result.stderr)
                        self.assertTrue(not destination.exists() or destination.stat().st_size==0)
                    else:
                        self.assertNotIn('Maximum file size exceeded',result.stderr)
                        self.assertEqual(destination.stat().st_size,1024)
            finally:server.shutdown();server.server_close();thread.join(timeout=2)


if __name__=='__main__':unittest.main()
