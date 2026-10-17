#!/usr/bin/env python3
"""Inert actual collector controls: no compiler, restore, browser or network."""
import importlib.util
import json
import os
import pathlib
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
sys.dont_write_bytecode=True
ROOT=pathlib.Path(__file__).resolve().parents[3]
spec=importlib.util.spec_from_file_location('evidence',ROOT/'scripts/wasm-release/installed-evidence.py')
e=importlib.util.module_from_spec(spec);spec.loader.exec_module(e)
spec=importlib.util.spec_from_file_location('binder',ROOT/'scripts/wasm-release/bind-installed-release.py')
b=importlib.util.module_from_spec(spec);spec.loader.exec_module(b)


def put(path, body=b'actual control bytes'):
    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(body);return path


class Evidence(unittest.TestCase):
    def test_hidden_metadata_and_colon_cache_exclusion(self):
        with tempfile.TemporaryDirectory() as raw:
            root=pathlib.Path(raw);work=root/'work'
            for name in ('release-manifest.json','NuGet.Config','consumer/packages.lock.json'):
                put(work/name,b'{}')
            put(work/'sdk-artifacts/fsgg-wasm-sdk-0.4.0.tar.gz')
            put(work/'installed-member-receipt.log')
            for ident in e.PACKAGES:
                for leaf in (ident+'.0.4.0.nupkg',ident+'.0.4.0.nupkg.sha512','.nupkg.metadata'):
                    put(work/'consumer-packages'/ident/'0.4.0'/leaf)
            for name in ('fsharp.core.10.1.302.nupkg','fsharp.core.10.1.302.nupkg.sha512','.nupkg.metadata','lib/netstandard2.0/FSharp.Core.dll','lib/netstandard2.1/FSharp.Core.dll'):
                put(work/'consumer-packages/fsharp.core/10.1.302'/name)
            put(work/'consumer/browser/test-results/.last-run.json',b'{"status":"passed"}')
            for name in ('FSharp.Core.dll','Consumer.dll','fable/Program.js','_content/FS.GG.Wasm.Browser/module-worker.mjs','_content/FS.GG.Wasm.Browser/worker-client.mjs','_content/FS.GG.Wasm.Browser/policy/WorkerEntry.js'):
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
            a=e.Projection();a.add(file,'consumer/data.log');a.finish(root/'a',0)
            c=e.Projection();c.add(file,'selected/data.log')
            with patch.dict(e.LIMITS,logs=10):e.merge(c,root/'a')
            self.assertTrue(c.errors)

    def test_owner_trap_retains_original_status_after_collection_failure(self):
        source=(ROOT/'scripts/verify-wasm-package-consumer.sh').read_text()
        trap=source.split('finish_consumer() {',1)[1].split('\ntrap finish_consumer EXIT',1)[0]
        # Run only the maintained shell trap body with inert collection/cleanup
        # functions. No consumer command or downloaded source is executed.
        for runtime,collection,expected in ((37,19,37),(0,19,19),(0,0,0)):
            with tempfile.TemporaryDirectory() as raw:
                script='set -euo pipefail\nmode=--feed-only\nrepo=/inert\nwork=/inert\nWASM_CONSUMER_EVIDENCE=/inert\nWASM_CONSUMER_STATUS='+raw+'/status.json\npython3(){ return '+str(collection)+'; }\nrm(){ return 0; }\nfinish_consumer() {'+trap+'\ntrap finish_consumer EXIT\nexit '+str(runtime)
                result=subprocess.run(['bash','-c',script],capture_output=True,text=True)
                self.assertEqual(result.returncode,expected,result.stderr)
                status=json.loads((pathlib.Path(raw)/'status.json').read_text())
                self.assertEqual(status['runtime'],runtime);self.assertEqual(status['collection'],collection)

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
            allowed='scripts/wasm-release/bind-installed-release.py'
            put(root/allowed,b'old');git('add','.');git('commit','-qm','P');p=git('rev-parse','HEAD');tree=git('rev-parse','HEAD^{tree}')
            put(root/allowed,b'new');git('add','.');git('commit','-qm','Q');q=git('rev-parse','HEAD')
            with patch.object(b,'ROOT',root),patch.object(b,'PUBLISHED',p),patch.object(b,'PUBLISHED_TREE',tree):
                audit=b.source_audit(p,q);self.assertEqual(len(audit['changes']),1)
                row=audit['changes'][0];self.assertEqual(row['new']['sha256'],e.hashlib.sha256(b'new').hexdigest())
                self.assertEqual(row['old']['blob'],git('rev-parse',p+':'+allowed))
                put(root/'src/Wasm.Browser/Host.fs',b'semantic mutation');git('add','.');git('commit','-qm','bad')
                with self.assertRaises(AssertionError):b.source_audit(p,git('rev-parse','HEAD'))
                with self.assertRaises(AssertionError):b.source_audit('a'*40,q)


if __name__=='__main__':unittest.main()
