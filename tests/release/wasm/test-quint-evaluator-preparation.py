#!/usr/bin/env python3
"""Offline archive/CLI/order controls. Never execute evaluator, model, compiler or pack."""
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('evaluator_preparation', ROOT / 'scripts/wasm-release/prepare-quint-evaluator.py')
e = importlib.util.module_from_spec(spec);spec.loader.exec_module(e)


PRODUCER_SCRIPT_SHA = 'b1cb0116a4e76c592300f3a950e54af5b661c892fbb816ac3a068ea6e2cbeb62'
PRODUCER_SCRIPT = ROOT / 'tests/release/wasm/fixtures/producer-16a-verify-wasm-supervisor.sh'


def producer_script(path=PRODUCER_SCRIPT):
    body = path.read_bytes()
    e.need(e.sha(body) == PRODUCER_SCRIPT_SHA, 'original producer fixture substituted')
    return body


def release():
    return {'id':e.RELEASE, 'tag_name':e.TAG, 'draft':False, 'prerelease':False,
        'html_url':f'https://github.com/{e.REPO}/releases/tag/{e.TAG}', 'assets':[
            {'id':e.ASSET,'name':e.NAME,'size':e.SIZE,'state':'uploaded','digest':'sha256:'+e.ARCHIVE,
             'url':f'https://api.github.com/repos/{e.REPO}/releases/assets/{e.ASSET}',
             'browser_download_url':f'https://github.com/{e.REPO}/releases/download/{e.TAG}/{e.NAME}'}]}


def archive(name='quint_evaluator', kind=tarfile.REGTYPE, extra=False):
    out = io.BytesIO();data=b'\x7fELFoffline-control'
    with tarfile.open(fileobj=out, mode='w:gz') as t:
        m=tarfile.TarInfo(name);m.type=kind;m.size=len(data) if kind==tarfile.REGTYPE else 0;m.linkname='/foreign'
        t.addfile(m,io.BytesIO(data) if m.size else None)
        if extra:t.addfile(tarfile.TarInfo('extra'))
    return out.getvalue(),data


class Tests(unittest.TestCase):
    def test_actual_official_pin_and_metadata_mutations(self):
        self.assertEqual(e.ARCHIVE,'61755a09d5052d93a4e75e840059edfd0d3674aeda164b9d2464be3d6e21b1c2')
        self.assertEqual(e.MEMBER_SHA,'b2efdeac5713d153e41bf2143b94ed75d888fdd5637f4a5d61a04c695313510a')
        e.metadata(release())
        for key in ['id','tag_name','html_url','draft','prerelease']:
            x=release();x[key]='foreign'
            with self.subTest(key=key),self.assertRaises(ValueError):e.metadata(x)
        for key in ['id','name','size','state','digest','url','browser_download_url']:
            x=release();x['assets'][0][key]='foreign'
            with self.subTest(key=key),self.assertRaises(ValueError):e.metadata(x)
        x=release();x['assets']*=2
        with self.assertRaises(ValueError):e.metadata(x)

    def test_real_tar_structure_digest_and_size(self):
        for name,kind,extra in [('quint_evaluator',tarfile.REGTYPE,False),('../quint_evaluator',tarfile.REGTYPE,False),('/quint_evaluator',tarfile.REGTYPE,False),('quint_evaluator',tarfile.SYMTYPE,False),('quint_evaluator',tarfile.LNKTYPE,False),('quint_evaluator',tarfile.REGTYPE,True)]:
            body,data=archive(name,kind,extra)
            with patch.multiple(e,SIZE=len(body),ARCHIVE=e.sha(body),MEMBER_SIZE=len(data),MEMBER_SHA=e.sha(data)):
                if name=='quint_evaluator' and kind==tarfile.REGTYPE and not extra:
                    self.assertEqual(e.binary(body),data)
                    for mutant in [body+b'x',body[:-1],bytes([body[0]^1])+body[1:]]:
                        with self.assertRaises(ValueError):e.binary(mutant)
                    with patch.object(e,'LIMIT',len(body)-1),self.assertRaises(ValueError):e.binary(body)
                    with patch.object(e,'MEMBER_SHA','0'*64),self.assertRaises(ValueError):e.binary(body)
                else:
                    with self.assertRaises(ValueError):e.binary(body)

    def test_fresh_private_cache(self):
        with tempfile.TemporaryDirectory() as d:
            base=Path(d);producer=base/'producer';producer.mkdir();cache=base/'cache';e.fresh(cache,producer)
            self.assertEqual(cache.stat().st_mode & 0o777,0o700)
            with self.assertRaises(ValueError):e.fresh(cache,producer)
            with self.assertRaises(ValueError):e.fresh(producer/'cache',producer)
            link=base/'linked';link.symlink_to(cache,target_is_directory=True)
            with self.assertRaises(ValueError):e.fresh(link,producer)
            with self.assertRaises(ValueError):e.fresh(link/'nested',producer)
            with self.assertRaises(ValueError):e.fresh(Path.home()/'.quint',producer)

    def test_real_tool_identity_and_version_mutants(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);entry=root/'dist/src/cli.js';entry.parent.mkdir(parents=True)
            (root/'package.json').write_text(json.dumps({'version':'0.32.0'}))
            for name in e.TOOL:(root/name).parent.mkdir(parents=True,exist_ok=True);(root/name).write_text(name)
            pins={name:e.sha((root/name).read_bytes()) for name in e.TOOL}
            with patch.object(e,'TOOL',pins):
                self.assertEqual(e.tool(str(entry)),root)
                (root/'package.json').write_text(json.dumps({'version':'0.31.0'}))
                with self.assertRaises(ValueError):e.tool(str(entry))
                (root/'package.json').write_text(json.dumps({'version':'0.32.0'}));entry.write_text('foreign')
                with self.assertRaises(ValueError):e.tool(str(entry))

    def test_original_fixture_substitution_refused(self):
        original=producer_script()
        self.assertNotIn(b'--backend=typescript', original)
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'fixture';path.write_bytes(original+b'foreign')
            with self.assertRaises(ValueError):producer_script(path)
            path.write_bytes((ROOT/'scripts/verify-wasm-supervisor.sh').read_bytes())
            with self.assertRaises(ValueError):producer_script(path)

    def test_real_P_commands_and_failed_readiness_stops(self):
        with tempfile.TemporaryDirectory() as d:
            base=Path(d);p=base/'P';(p/'scripts').mkdir(parents=True)
            source=producer_script()
            (p/'scripts/verify-wasm-supervisor.sh').write_bytes(source)
            plan,witnesses=e.commands(p,'actual-quint');self.assertEqual(len(plan),3)
            for _,args in plan:self.assertEqual(args.count('--backend=rust'),1);self.assertIn('--seed=20261003',args)
            self.assertEqual([a for a in plan[1][1] if a.startswith('--init=')],['--init=initCompatible'])
            self.assertEqual([a for a in plan[2][1] if a.startswith('--init=')],['--init=initCompatible.then(readyCompatible)'])
            for _,args in plan[1:]:
                self.assertIn('--max-samples=1000',args);self.assertIn('--max-steps=40',args);self.assertIn('--invariant=compatibilitySafe',args)
            evidence=base/'evidence';evidence.mkdir();calls=[]
            def runner(args,**kwargs):
                calls.append(args);self.assertEqual(kwargs['env']['QUINT_HOME'],str(base/'cache'))
                kwargs['stdout'].write(('\n'.join(w+' was witnessed in 1 trace' for w in witnesses)).encode())
                return subprocess.CompletedProcess(args,0)
            e.readiness(p,'actual-quint',base/'cache',evidence,run=runner);self.assertEqual(len(calls),3)
            mutated=copy.deepcopy(plan);mutated[0][1][2]='--backend=typescript'
            with patch.object(e,'commands',return_value=(mutated,witnesses)),self.assertRaises(ValueError):
                e.readiness(p,'actual-quint',base/'cache',evidence,run=runner)
            for fail_at in range(3):
                calls.clear()
                def fail(args,**kwargs):
                    calls.append(args);return subprocess.CompletedProcess(args,1 if len(calls)-1==fail_at else 0)
                with self.assertRaises(ValueError):e.readiness(p,'actual-quint',base/'cache',evidence,run=fail)
                self.assertEqual(len(calls),fail_at+1)
            def empty(args,**kwargs):return subprocess.CompletedProcess(args,0)
            with self.assertRaises(ValueError):e.readiness(p,'actual-quint',base/'cache',evidence,run=empty)

    def test_redirect_does_not_leak_token(self):
        request=e.urllib.request.Request('https://api.github.com/path',headers={'Authorization':'Bearer offline'})
        result=e.OfficialRedirect().redirect_request(request,None,302,'Found',{},'https://release-assets.githubusercontent.com/asset')
        self.assertIsNone(result.get_header('Authorization'))
        for url in ['https://foreign.invalid/asset','http://api.github.com/asset']:
            with self.assertRaises(ValueError):e.OfficialRedirect().redirect_request(request,None,302,'Found',{},url)

if __name__ == '__main__':unittest.main()
