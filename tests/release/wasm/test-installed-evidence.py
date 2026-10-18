#!/usr/bin/env python3
"""Inert actual collector controls: no compiler, restore, browser or network."""
import importlib.util
import io
import zipfile
import json
import os
import pathlib
import subprocess
import sys
import tarfile
import tempfile
import unittest
import xml.etree.ElementTree as ET
from unittest.mock import patch
sys.dont_write_bytecode=True
ROOT=pathlib.Path(__file__).resolve().parents[3]
CURRENT=ET.parse(ROOT/'eng/wasm-shared/version.props').findtext('.//WasmSharedVersion')
spec=importlib.util.spec_from_file_location('evidence',ROOT/'scripts/wasm-release/installed-evidence.py')
e=importlib.util.module_from_spec(spec);spec.loader.exec_module(e)
spec=importlib.util.spec_from_file_location('binder',ROOT/'scripts/wasm-release/bind-installed-release.py')
b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)


def put(path, body=b'actual control bytes'):
    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(body);return path


def current_cli_topology(root):
    """Inert files for the actual two CLI actions; no file is executed as code.

    The maintained collector bytes and tracked version are copied unchanged to
    an isolated source root so selected() reads its real fixed runtime paths.
    This proves projection/caller composition, never installed/runtime proof.
    """
    source=root/'source';script=source/'scripts/wasm-release/installed-evidence.py'
    put(script,(ROOT/'scripts/wasm-release/installed-evidence.py').read_bytes())
    put(source/'eng/wasm-shared/version.props',(ROOT/'eng/wasm-shared/version.props').read_bytes())
    work=root/'consumer-work';selected=root/'selected';receipts=root/'receipts';logs=root/'logs'
    manifest=json.dumps({'version':CURRENT}).encode();sdk=b'inert current SDK fixture'
    for name,body in (('release-manifest.json',manifest),('NuGet.Config',b'<configuration />'),
                      ('consumer/packages.lock.json',b'{}'),('installed-member-receipt.log',b'fixture'),
                      ('consumer-http.log',b'fixture'),('consumer/browser/test-results/.last-run.json',b'{"status":"passed"}')):
        put(work/name,body)
    put(work/'sdk-artifacts'/f'fsgg-wasm-sdk-{CURRENT}.tar.gz',sdk)
    for name in ('release-manifest.json','promotion-binding.json','installed-input-audit.json','published-native.json'):
        put(receipts/name,manifest if name=='release-manifest.json' else b'{}')
    put(receipts/f'fsgg-wasm-sdk-{CURRENT}.tar.gz',sdk)
    put(root/'wasm-installed-receipts/release-manifest.json',manifest)
    put(root/'wasm-installed-receipts/promotion-binding.json',b'{}')
    archives={}
    for ident in e.PACKAGES:
        out=io.BytesIO()
        with zipfile.ZipFile(out,'w') as z:
            if ident=='fs.gg.wasm.browser':
                z.writestr('fable/Host.fs',b'current Host source fixture')
                z.writestr('contentFiles/any/any/_content/FS.GG.Wasm.Browser/policy/fable_modules/fable-library-js.5.18.0/Array.js',b'current library fixture')
            else:z.writestr('fable/Contracts.fs',b'current Contracts source fixture')
        archives[ident]=out.getvalue()
    for cache in (work/'consumer-packages',selected/'packages'):
        for ident,body in archives.items():
            for name,data in ((f'{ident}.{CURRENT}.nupkg',body),(f'{ident}.{CURRENT}.nupkg.sha512',b'fixture'),('.nupkg.metadata',b'{}')):
                put(cache/ident/CURRENT/name,data)
        for name in ('fsharp.core.10.1.302.nupkg','fsharp.core.10.1.302.nupkg.sha512','.nupkg.metadata',
                     'lib/netstandard2.0/FSharp.Core.dll','lib/netstandard2.1/FSharp.Core.dll'):
            put(cache/'fsharp.core/10.1.302'/name)
    public=work/'consumer/public/sub/app'
    for name in ('FSharp.Core.dll','Consumer.dll','Consumer','fable/Program.js'):
        put(public/name)
    for context in (public,root/'selected-browser'):
        for name in ('module-worker.mjs','worker-client.mjs','worker-build.json','policy/WorkerEntry.js'):
            put(context/'_content/FS.GG.Wasm.Browser'/name)
        put(context/'_content/FS.GG.Wasm.Browser/policy/fable_modules/fable-library-js.5.18.0/Array.js',b'current library fixture')
    for context in (public/'fable',selected/'installed-fable',root/'selected-browser/fable'):
        put(context/'Program.js');put(context/'BrowserBridge.js')
        put(context/f'fable_modules/FS.GG.Wasm.Browser.{CURRENT}/Host.fs',b'current Host source fixture')
        put(context/f'fable_modules/FS.GG.Wasm.Contracts.{CURRENT}/Contracts.fs',b'current Contracts source fixture')
    for name in ('limits-guest.wasm','http.log'):put(root/'selected-browser'/name)
    for name in (*e.LOGS,'installed.packages.lock.json','installed.bad-hash.lock.json','official-core-joins.json'):
        put(selected/name,b'{}' if name.endswith('.json') else b'fixture')
    put(root/'selected-NuGet.Config',b'<configuration />')
    baseline=root/'selected-baseline'
    for name in ('receipt.json','fsharp.core.10.1.302.nupkg','fsharp.core.10.1.401.nupkg','fsharp.core.10.1.203.nupkg',
                 'FSharp.Core.10.1.302.netstandard2.0.dll','FSharp.Core.10.1.302.netstandard2.1.dll',
                 'FSharp.Core.10.1.401.netstandard2.0.dll','FSharp.Core.10.1.401.netstandard2.1.dll','FSharp.Core.10.1.203.netstandard2.1.dll'):
        put(baseline/'official-core'/name,b'{}' if name.endswith('.json') else b'fixture')
    put(baseline/'compiler/FSharp.Core.dll')
    put(baseline/'tools/.store/fable/5.18.0/fable/5.18.0/tools/net10.0/any/FSharp.Core.dll')
    put(source/'tests/Wasm.Supervisor.Compatibility/browser/test-results/.last-run.json',b'{"status":"passed"}')
    for name in ('InstalledCompatibility.dll','InstalledCompatibility.deps.json','InstalledCompatibility.runtimeconfig.json','FSharp.Core.dll'):
        put(source/'tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0'/name)
    for name,body in (('consumer.log',b'fixture'),('selected.log',b'fixture'),('runtime-status.json',b'{"exitCode":0}'),
                      ('consumer-status.json',b'{"runtime":0,"collection":0,"cleanup":0,"descendants":"unproved"}')):
        put(logs/name,body)
    return script,work,selected,receipts,logs


class Evidence(unittest.TestCase):
    def test_actual_current_cli_consumer_merge_and_assemble(self):
        self.assertEqual(e.VERSION,CURRENT)
        for defect in (None,'old-cache-only','different-authenticated-sdk'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as raw:
                root=pathlib.Path(raw);script,work,selected,receipts,logs=current_cli_topology(root)
                if defect=='old-cache-only':
                    for ident in e.PACKAGES:
                        folder=work/'consumer-packages'/ident/CURRENT
                        folder.rename(folder.with_name('0.5.0'))
                        for leaf in folder.with_name('0.5.0').iterdir():
                            if CURRENT in leaf.name:leaf.rename(leaf.with_name(leaf.name.replace(CURRENT,'0.5.0')))
                environment=dict(os.environ,RUNNER_TEMP=raw,RELEASE_VERSION='0.5.0',WASM_CANDIDATE_VERSION='0.5.0')
                consumer=subprocess.run([sys.executable,str(script),'consumer','--work',str(work),'--output',str(root/'consumer-output'),'--runtime','0'],env=environment,capture_output=True,text=True)
                manifest=json.loads((root/'consumer-output/evidence-manifest.json').read_text())
                if defect=='old-cache-only':
                    self.assertEqual(consumer.returncode,1);self.assertFalse(manifest['eligible'])
                    self.assertTrue(any(f'.{CURRENT}.nupkg' in row['member'] for row in manifest['errors']))
                    continue
                self.assertEqual(consumer.returncode,0,consumer.stderr);self.assertTrue(manifest['eligible'])
                if defect:put(receipts/f'fsgg-wasm-sdk-{CURRENT}.tar.gz',b'wrong authenticated receipt fixture')
                assembled=subprocess.run([sys.executable,str(script),'assemble','--work',str(selected),'--consumer',str(root/'consumer-output'),
                                          '--receipts',str(receipts),'--logs',str(logs),'--output',str(root/'assembled'),'--runtime','0'],env=environment,capture_output=True,text=True)
                final=json.loads((root/'assembled/evidence-manifest.json').read_text())
                self.assertEqual(assembled.returncode,int(defect is not None),assembled.stderr)
                self.assertEqual(final['eligible'],defect is None)
                if defect:self.assertTrue(any(row['reason']=='consumer original differs from authenticated publication receipt' for row in final['errors']))
                else:
                    self.assertFalse(final['errors'])
                    names={row['path'] for row in final['members']}
                    self.assertIn(f'receipts/fsgg-wasm-sdk-{CURRENT}.tar.gz',names)
                    self.assertIn(f'selected/packages/fs.gg.wasm.browser/fs.gg.wasm.browser.{CURRENT}.nupkg',names)
                    self.assertTrue(all('0.5.0' not in name for name in names))

    def test_old_fable_and_package_names_are_not_current_fallbacks(self):
        self.assertNotEqual(CURRENT,'0.5.0')
        for project in ('FS.GG.Wasm.Browser','FS.GG.Wasm.Contracts'):
            with self.assertRaisesRegex(ValueError,'unreviewed Fable dependency module'):
                e.fable_dependency(f'consumer/public/sub/app/fable/fable_modules/{project}.0.5.0/Host.fs')
        self.assertFalse(e.consumer_member('consumer/packages/fs.gg.wasm.browser/fs.gg.wasm.browser.0.5.0.nupkg'))
        self.assertFalse(e.consumer_member('consumer/fsgg-wasm-sdk-0.5.0.tar.gz'))

    def test_ambiguous_or_invalid_tracked_version_refuses_before_collection(self):
        for xml in ('<Project />','<Project><WasmSharedVersion>0.6.0</WasmSharedVersion><WasmSharedVersion>0.5.0</WasmSharedVersion></Project>',
                    '<Project><WasmSharedVersion>current</WasmSharedVersion></Project>'):
            with self.subTest(xml=xml),tempfile.TemporaryDirectory() as raw:
                root=pathlib.Path(raw);script,work,*_=current_cli_topology(root)
                put(root/'source/eng/wasm-shared/version.props',xml.encode())
                result=subprocess.run([sys.executable,str(script),'consumer','--work',str(work),'--output',str(root/'out'),'--runtime','0'],capture_output=True,text=True)
                self.assertNotEqual(result.returncode,0)
                self.assertFalse((root/'out').exists())

    def test_actual_owner_trap_runs_current_collector_and_preserves_cleanup(self):
        import shlex
        source=(ROOT/'scripts/verify-wasm-package-consumer.sh').read_text()
        trap=source.split('finish_consumer() {',1)[1].split('\ntrap finish_consumer EXIT',1)[0]
        for missing in (False,True):
            with self.subTest(missing=missing),tempfile.TemporaryDirectory() as raw:
                root=pathlib.Path(raw);script,work,*_=current_cli_topology(root)
                if missing:
                    (work/'consumer-packages/fs.gg.wasm.browser'/CURRENT/f'fs.gg.wasm.browser.{CURRENT}.nupkg').unlink()
                status=root/'status.json';output=root/'consumer-output'
                values={'mode':'--feed-only','repo':str(root/'source'),'work':str(work),
                        'WASM_CONSUMER_EVIDENCE':str(output),'WASM_CONSUMER_STATUS':str(status)}
                command='set -euo pipefail\n'+'\n'.join(key+'='+shlex.quote(value) for key,value in values.items())
                command+='\nfinish_consumer() {'+trap+'\ntrap finish_consumer EXIT\nexit 0\n'
                result=subprocess.run(['bash','-c',command],capture_output=True,text=True)
                self.assertEqual(result.returncode,int(missing),result.stderr)
                actual=json.loads(status.read_text())
                self.assertEqual(actual,{'runtime':0,'collection':int(missing),'cleanup':0,'descendants':'unproved'})
                self.assertFalse(work.exists())
                merged=e.Projection();e.merge(merged,output)
                self.assertEqual(bool(merged.errors),missing,merged.errors)

    def test_hidden_metadata_and_colon_cache_exclusion(self):
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);work=root/'work'
            for name in ('release-manifest.json','NuGet.Config','consumer/packages.lock.json'):
                put(work/name,b'{}')
            put(work/'sdk-artifacts'/f'fsgg-wasm-sdk-{CURRENT}.tar.gz')
            put(work/'installed-member-receipt.log')
            put(work/'consumer-http.log')
            for ident in e.PACKAGES:
                for leaf in (f'{ident}.{CURRENT}.nupkg',f'{ident}.{CURRENT}.nupkg.sha512','.nupkg.metadata'):
                    put(work/'consumer-packages'/ident/CURRENT/leaf)
            for name in ('fsharp.core.10.1.302.nupkg','fsharp.core.10.1.302.nupkg.sha512','.nupkg.metadata','lib/netstandard2.0/FSharp.Core.dll','lib/netstandard2.1/FSharp.Core.dll'):
                put(work/'consumer-packages/fsharp.core/10.1.302'/name)
            put(work/'consumer/browser/test-results/.last-run.json',b'{"status":"passed"}')
            for name in ('FSharp.Core.dll','Consumer.dll','Consumer','fable/Program.js','_content/FS.GG.Wasm.Browser/module-worker.mjs','_content/FS.GG.Wasm.Browser/worker-client.mjs','_content/FS.GG.Wasm.Browser/worker-build.json','_content/FS.GG.Wasm.Browser/policy/WorkerEntry.js'):
                put(work/'consumer/public/sub/app'/name)
            put(work/'http-cache/cache:https.json/list.dat',b'discard HTTP cache')
            p=e.Projection();e.consumer(p,work)
            self.assertEqual(p.finish(root/'output',0),0)
            with tarfile.open(root/'output/installed-evidence.tar.gz') as archive:
                names=archive.getnames()
                self.assertEqual(sum(x.endswith('/.nupkg.metadata') for x in names),3)
                self.assertTrue(all('http-cache' not in x for x in names))
                self.assertTrue(json.load(archive.extractfile('evidence-manifest.json'))['eligible'])
            joined=e.Projection();e.merge(joined,root/'output')
            self.assertFalse(joined.errors);self.assertEqual(joined.files,p.files)

    def test_complete_fable_dependency_roles_and_original_archive_joins(self):
        # Inert topology mirrors all three compiler/adapter outputs plus both
        # restored Worker policy trees. Source/support is distinct from runtime.
        members = {
            'fs.gg.wasm.browser': {'fable/'+stem+suffix: (stem+suffix).encode()
                for stem in ('Admission','Host','Invocation','Lifecycle','RuntimeProtocol','WorkerEntry')
                for suffix in ('.fs','.fsi')},
            'fs.gg.wasm.contracts': {'fable/Contracts.fs':b'Contracts.fs','fable/Contracts.fsi':b'Contracts.fsi'}}
        for package,project in (('fs.gg.wasm.browser','FS.GG.Wasm.Browser'),('fs.gg.wasm.contracts','FS.GG.Wasm.Contracts')):
            members[package]['fable/'+project+'.fsproj']=b'<Project />'
        members['fs.gg.wasm.browser'].update({
            'fable/eng/wasm-shared/version.props':b'<Project />',
            'fable/js/wasm-primitives.js':b'actual primitives',
            'contentFiles/any/any/_content/FS.GG.Wasm.Browser/policy/fable_modules/fable-library-js.5.18.0/Array.js':b'actual runtime',
            'contentFiles/any/any/_content/FS.GG.Wasm.Browser/policy/fable_modules/fable-library-js.5.18.0/lib/big.js':b'actual nested runtime'})
        archives={}
        for package,rows in members.items():
            output=io.BytesIO()
            with zipfile.ZipFile(output,'w') as archive:
                for name,body in rows.items():archive.writestr(name,body)
            archives[package]=output.getvalue()
        contexts=('consumer/public/sub/app/fable','selected/installed-fable','selected/browser/fable', 'selected/installed-fable/Installed','selected/browser/fable/Installed',
                  'consumer/public/sub/app/_content/FS.GG.Wasm.Browser/policy',
                  'selected/browser/_content/FS.GG.Wasm.Browser/policy')
        for context in contexts:
            with self.subTest(context=context),tempfile.TemporaryDirectory() as raw:
                root=pathlib.Path(raw);p=e.Projection();owner=context.split('/')[0]
                for package,body in archives.items():
                    p.add(put(root/package,body),owner+'/packages/'+package+'/'+f'{package}.{CURRENT}.nupkg')
                dependency=root/'compiled/fable_modules'
                put(dependency/'.gitignore',b'*')
                library=dependency/'fable-library-js.5.18.0'
                for leaf,body in (('README.md',b'readme'),('CHANGELOG.md',b'changelog'),('big.d.ts',b'declare type'),
                                  ('package.json',b'{"name":"@fable-org/fable-library-js","version":"2.8.0","type":"module"}'),
                                  ('Array.js',b'actual runtime'),('lib/big.js',b'actual nested runtime')):put(library/leaf,body)
                for package,module in (('fs.gg.wasm.browser','FS.GG.Wasm.Browser'),('fs.gg.wasm.contracts','FS.GG.Wasm.Contracts')):
                    folder=dependency/(module+'.'+CURRENT)
                    for member,body in members[package].items():
                        if not member.startswith('fable/'):continue
                        leaf=member[len('fable/'):]
                        if leaf.endswith('.fsproj'):put(folder/(module+'.fableproj'),b'<Project><PropertyGroup /></Project>')
                        else:put(folder/leaf,body)
                        if leaf.endswith('.fs'):put(folder/(leaf+'.js'),b'generated current source')
                p.tree(root/'compiled',context)
                self.assertFalse(p.errors,p.errors)
                self.assertIn(context+f'/fable_modules/FS.GG.Wasm.Browser.{CURRENT}/Host.fs',p.files)
                self.assertIn(context+f'/fable_modules/FS.GG.Wasm.Browser.{CURRENT}/Host.fs.js',p.files)
                if owner=='consumer':
                    self.assertEqual(p.finish(root/'out',0),0)
                    joined=e.Projection();e.merge(joined,root/'out')
                    self.assertFalse(joined.errors,joined.errors);self.assertEqual(joined.files,p.files)
                    copied=context+f'/fable_modules/FS.GG.Wasm.Browser.{CURRENT}/Host.fs'
                    original=p.files[copied];p.files[copied]=b'changed copied source'
                    p.finish(root/'forged',0)
                    refused=e.Projection();e.merge(refused,root/'forged')
                    self.assertTrue(any(x['member']==copied and x['reason']=='Fable dependency differs from original package member' for x in refused.errors),refused.errors)
                    p.files[copied]=original
                # Same named runtime drift and same-suffix foreign executables
                # must reach their actual provenance/role refusal predicates.
                put(library/'Array.js',b'changed runtime')
                bad=e.Projection();bad.files.update(p.files)
                # dependency validates the collected bytes, not the path again.
                bad.files[context+'/fable_modules/fable-library-js.5.18.0/Array.js']=b'changed runtime'
                bad.dependency(context+'/fable_modules/fable-library-js.5.18.0/Array.js')
                self.assertTrue(any(x['reason']=='Fable dependency differs from original package member' for x in bad.errors),bad.errors)
                for leaf in ('fable-library-js.5.18.0/unexpected.exe','fable-library-js.5.18.0/foreign.js',
                             f'Other.{CURRENT}/Host.fs',f'FS.GG.Wasm.Browser.{CURRENT}/Foreign.fs',
                             f'FS.GG.Wasm.Browser.{CURRENT}/Foreign.fs.js'):
                    name=context+'/fable_modules/'+leaf
                    if leaf.endswith('foreign.js'):
                        bad.files[name]=b'foreign runtime';bad.dependency(name)
                        self.assertTrue(any(x['member']==name and 'absent/ambiguous' in x['reason'] for x in bad.errors))
                    elif leaf.endswith('unexpected.exe'):
                        bad.files[name]=b'foreign executable';bad.dependency(name)
                        self.assertTrue(any(x['member']==name and 'absent/ambiguous' in x['reason'] for x in bad.errors))
                    else:
                        with self.assertRaisesRegex(ValueError,'unreviewed Fable'):e.fable_dependency(name)
                for name in ('consumer/public/sub/app/arbitrary/fable_modules/fable-library-js.5.18.0/Array.js',
                             'consumer/public/sub/app/fable/fable_modules/Unknown/runtime.js'):
                    self.assertFalse(e.consumer_member(name))

    def test_consumer_merge_refuses_selected_dependency_context(self):
        for name in ('selected/installed-fable/fable_modules/.gitignore',
                     'selected/browser/fable/fable_modules/fable-library-js.5.18.0/README.md'):
            with self.subTest(name=name),tempfile.TemporaryDirectory() as raw:
                root=pathlib.Path(raw);p=e.Projection();p.add(put(root/'data',b'foreign support'),name)
                self.assertEqual(p.finish(root/'forged-consumer',0),0)
                joined=e.Projection();e.merge(joined,root/'forged-consumer')
                self.assertTrue(any(x['reason']=='foreign consumer evidence member' for x in joined.errors),joined.errors)
                self.assertNotIn(name,joined.files)

    def test_refusals_retain_independent_evidence_and_runtime_cause(self):
        for defect in ('symlink','hardlink','traversal','duplicate','oversize','missing'):
            with self.subTest(defect=defect),tempfile.TemporaryDirectory() as raw:
                root=pathlib.Path(raw);good=put(root/'good',b'good');bad=put(root/'bad',b'x'*20)
                p=e.Projection();p.add(good,'selected/good.log')
                limits=dict(e.LIMITS,file=10)
                with patch.dict(e.LIMITS,limits):
                    if defect=='symlink': bad.unlink();bad.symlink_to(good);p.add(bad,'selected/bad')
                    elif defect=='hardlink':bad.unlink();os.link(good,bad);p.add(bad,'selected/bad')
                    elif defect=='traversal':p.add(bad,'../escape')
                    elif defect=='duplicate':p.add(good,'selected/good.log')
                    elif defect=='oversize':p.add(bad,'selected/bad')
                    else:p.add(root/'absent','selected/lock.json')
                self.assertEqual(p.finish(root/'output',37),1)
                manifest=json.loads((root/'output/evidence-manifest.json').read_text())
                self.assertEqual(manifest['runtimeStatus'],37);self.assertFalse(manifest['eligible'])
                with tarfile.open(root/'output/installed-evidence.tar.gz') as archive:
                    self.assertEqual(archive.extractfile('selected/good.log').read(),b'good')

    def test_missing_core_lock_worker_is_ineligible(self):
        with tempfile.TemporaryDirectory() as raw:
            p=e.Projection();e.selected(p,pathlib.Path(raw)/'selected')
            self.assertTrue(any('installed.packages.lock' in x['member'] for x in p.errors))
            self.assertTrue(any('core/' in x['member'] for x in p.errors))
            self.assertTrue(any(x['member']=='selected/browser' for x in p.errors))
            self.assertTrue(any(x['member'].endswith('/policy/WorkerEntry.js') for x in p.errors))
            self.assertEqual(p.finish(pathlib.Path(raw)/'out',0),1)

    def test_combined_budget_is_enforced_on_merge(self):
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);file=put(root/'file',b'x'*8)
            a=e.Projection();a.add(file,'consumer/installed-member-receipt.log');a.finish(root/'a',0)
            c=e.Projection();c.add(file,'selected/data.log')
            with patch.dict(e.LIMITS,logs=10):e.merge(c,root/'a')
            self.assertTrue(any(x['reason']=='merged logs cap' for x in c.errors),c.errors)

    def test_owner_trap_retains_original_status_after_collection_failure(self):
        source=(ROOT/'scripts/verify-wasm-package-consumer.sh').read_text()
        trap=source.split('finish_consumer() {',1)[1].split('\ntrap finish_consumer EXIT',1)[0]
        # Run only the maintained shell trap body with inert collection/cleanup
        # functions. No consumer command or downloaded source is executed.
        for runtime,collection,cleanup,expected in ((37,19,23,37),(0,19,23,19),(0,0,23,23),(0,0,0,0)):
            with tempfile.TemporaryDirectory() as raw:
                script='set -euo pipefail\nmode=--feed-only\nrepo=/inert\nwork=/inert\nWASM_CONSUMER_EVIDENCE=/inert\nWASM_CONSUMER_STATUS='+raw+'/status.json\npython3(){ return '+str(collection)+'; }\nrm(){ return '+str(cleanup)+'; }\nfinish_consumer() {'+trap+'\ntrap finish_consumer EXIT\nexit '+str(runtime)
                result=subprocess.run(['bash','-c',script],capture_output=True,text=True)
                self.assertEqual(result.returncode,expected,result.stderr)
                status=json.loads((pathlib.Path(raw)/'status.json').read_text())
                self.assertEqual(status['runtime'],runtime);self.assertEqual(status['collection'],collection)
                self.assertEqual(status['cleanup'],cleanup)

    def test_workflow_preserves_command_first_cause_and_native_log_failure(self):
        text=(ROOT/'.github/workflows/wasm-installed-org.yml').read_text()
        block=text.split('          phase=initial\n',1)[1].split('          phase=complete',1)[0]
        block='phase=initial\n'+block.split('          run_logged consumer',1)[0]
        block='\n'.join(line[10:] if line.startswith('          ') else line for line in block.splitlines())
        for command_exit,log_exit,expected in ((37,19,37),(0,19,19),(0,0,0)):
            with tempfile.TemporaryDirectory() as raw:
                block_here=block.replace('artifacts/wasm-installed-${{ matrix.feed }}/runtime-status.json',raw+'/status.json')
                script='set -euo pipefail\n'+block_here+'\ncmd(){ echo fixture; return '+str(command_exit)+'; }\ntee(){ cat >/dev/null; return '+str(log_exit)+'; }\nrun_logged consumer /inert cmd\nphase=complete\n'
                result=subprocess.run(['bash','-c',script],capture_output=True,text=True)
                self.assertEqual(result.returncode,expected,result.stderr)
                status=json.loads((pathlib.Path(raw)/'status.json').read_text())
                self.assertEqual(status['exitCode'],expected)
                self.assertEqual(status['commandExit'],command_exit);self.assertEqual(status['logExit'],log_exit)

    def test_archive_and_external_manifest_share_one_upload_cap(self):
        import random
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);p=e.Projection()
            p.add(put(root/'binary',random.Random(42).randbytes(60000)),'selected/runtime.dll')
            p.add(put(root/'log',b'original log'),'selected/first.log')
            with patch.dict(e.LIMITS,total=61200):
                self.assertEqual(p.finish(root/'out',37),1)
            manifest=json.loads((root/'out/evidence-manifest.json').read_text())
            self.assertEqual(manifest['runtimeStatus'],37);self.assertFalse(manifest['eligible'])
            self.assertTrue(any(x['reason']=='archive plus sidecar upload cap' for x in manifest['errors']))
            self.assertLessEqual(sum(x.stat().st_size for x in (root/'out').iterdir()),61200)
            with tarfile.open(root/'out/installed-evidence.tar.gz') as archive:
                self.assertEqual(archive.extractfile('selected/first.log').read(),b'original log')
                self.assertNotIn('selected/runtime.dll',archive.getnames())

    def test_both_physical_manifest_copies_count_as_metadata(self):
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);p=e.Projection();p.add(put(root/'lock',b'{}'),'selected/lock.json')
            p.finish(root/'reference',0)
            metadata=len((root/'reference/evidence-manifest.json').read_bytes())
            # One manifest fits but two cannot. The actual finish predicate,
            # not a second implementation of it, must refuse before upload.
            with patch.dict(e.LIMITS,metadata=p.metadata+metadata+32):
                with self.assertRaisesRegex(ValueError,'manifest cap'):p.finish(root/'refused',0)

    def test_fresh_http_capture_refuses_stale_log_and_preserves_runtime(self):
        source=(ROOT/'scripts/verify-wasm-package-consumer.sh').read_text()
        trap=source.split('finish_consumer() {',1)[1].split('\ntrap finish_consumer EXIT',1)[0]
        for changed,copy_exit,runtime,expected in ((True,0,0,0),(False,0,0,1),(True,19,37,37)):
            with self.subTest(changed=changed,copy_exit=copy_exit),tempfile.TemporaryDirectory() as raw:
                script='set -euo pipefail\nmode=--feed-only\nrepo=/inert\nwork=/inert\nconsumer_http_started=1\nconsumer_http_before=old\nWASM_CONSUMER_EVIDENCE=/inert\nWASM_CONSUMER_STATUS='+raw+'/status.json\nstat(){ echo '+('fresh' if changed else 'old')+'; }\ncp(){ touch '+raw+'/copied; return '+str(copy_exit)+'; }\npython3(){ return 0; }\nrm(){ return 0; }\nfinish_consumer() {'+trap+'\ntrap finish_consumer EXIT\nexit '+str(runtime)
                result=subprocess.run(['bash','-c',script],capture_output=True,text=True)
                self.assertEqual(result.returncode,expected,result.stderr)
                status=json.loads((pathlib.Path(raw)/'status.json').read_text())
                self.assertEqual(status['runtime'],runtime)
                self.assertEqual((pathlib.Path(raw)/'copied').exists(),changed)
                self.assertEqual(status['collection'],copy_exit if changed else 1)

    def test_only_named_apphost_is_admitted(self):
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);put(root/'sub/app/Consumer');put(root/'sub/app/UnexpectedHost')
            p=e.Projection();p.tree(root,'consumer/public')
            self.assertIn('consumer/public/sub/app/Consumer',p.files)
            self.assertTrue(any(x['member'].endswith('UnexpectedHost') for x in p.errors))
            self.assertTrue(e.consumer_member('consumer/public/sub/app/Consumer'))
            self.assertFalse(e.consumer_member('consumer/public/sub/app/UnexpectedHost'))

    def test_bad_consumer_retention_configuration_refuses_before_work(self):
        import shlex
        source=(ROOT/'scripts/verify-wasm-package-consumer.sh').read_text()
        prelude=source.split('mode="${1:-}"',1)[1].split('work="$(mktemp',1)[0]
        prelude='mode="${1:-}"'+prelude
        with tempfile.TemporaryDirectory() as raw:
            env=dict(os.environ,MODE='installed',WASM_CONSUMER_EVIDENCE=raw+'/fresh',WASM_CONSUMER_STATUS=raw+'/status')
            script='set -euo pipefail\nrepo='+shlex.quote(str(ROOT))+'\n'+prelude+'\necho ADMITTED\n'
            result=subprocess.run(['bash','-c',script,'inert','--feed-only'],env=env,capture_output=True,text=True)
            self.assertEqual(result.returncode,0,result.stderr)
            for bad in ({'WASM_CONSUMER_EVIDENCE':str(ROOT/'forbidden-evidence')},{'WASM_CONSUMER_EVIDENCE':raw},{'WASM_CONSUMER_EVIDENCE':''},{'WASM_CONSUMER_STATUS':''}):
                result=subprocess.run(['bash','-c',script,'inert','--feed-only'],env=dict(env,**bad),capture_output=True,text=True)
                self.assertNotEqual(result.returncode,0);self.assertNotIn('ADMITTED',result.stdout)

    def test_complete_diff_binds_exact_blobs_and_refuses_semantic_change(self):
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw)
            def git(*args):return subprocess.check_output(['git','-C',raw,*args],text=True,stderr=subprocess.DEVNULL).strip()
            git('init','-q');git('config','user.name','inert control');git('config','user.email','control@example.invalid')
            allowed=('scripts/wasm-release/installed-evidence.py','tests/release/wasm/test-installed-evidence.py')
            excluded=('eng/wasm-shared/version.props','src/Wasm.Browser/Host.fs','sdk/wasm/verify-sdk.sh',
                      '.github/workflows/wasm-installed-org.yml','scripts/wasm-release/promotion.py','unlisted.txt')
            for name in (*allowed,*excluded):put(root/name,b'old')
            git('add','.');git('commit','-qm','P');p=git('rev-parse','HEAD');tree=git('rev-parse','HEAD^{tree}')
            for name in allowed:put(root/name,b'new')
            git('add','.');git('commit','-qm','Q');q=git('rev-parse','HEAD')
            with patch.object(b,'ROOT',root),patch.object(b,'PUBLISHED',p),patch.object(b,'PUBLISHED_TREE',tree):
                audit=b.source_audit(p,q);self.assertEqual({row['path'] for row in audit['changes']},set(allowed))
                row=audit['changes'][0];self.assertEqual(row['new']['sha256'],e.hashlib.sha256(b'new').hexdigest())
                self.assertEqual(row['old']['blob'],git('rev-parse',p+':'+row['path']))
                with patch.object(b,'INSTALLED_PATHS',b.INSTALLED_PATHS-{allowed[0]}),self.assertRaises(AssertionError):b.source_audit(p,q)
                for name in excluded:
                    git('reset','--hard',q);put(root/name,b'forbidden change');git('add','.');git('commit','-qm','excluded leaf')
                    with self.subTest(excluded=name),self.assertRaises(AssertionError):b.source_audit(p,git('rev-parse','HEAD'))
                for defect in ('mode','delete','rename','symlink'):
                    git('reset','--hard',q);path=root/allowed[0]
                    if defect=='mode':path.chmod(0o755)
                    elif defect=='delete':path.unlink()
                    elif defect=='rename':path.rename(path.with_name('renamed.py'))
                    else:path.unlink();path.symlink_to(root/allowed[1])
                    git('add','.');git('commit','-qm',defect)
                    with self.subTest(defect=defect),self.assertRaises(AssertionError):b.source_audit(p,git('rev-parse','HEAD'))
                with self.assertRaises(AssertionError):b.source_audit('a'*40,q)
                with patch.object(b,'PUBLISHED_TREE','a'*40),self.assertRaises(AssertionError):b.source_audit(p,q)


if __name__=='__main__':unittest.main()
