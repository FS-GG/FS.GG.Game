#!/usr/bin/env python3
"""Closed installed proof projection. Never copies HTTP/tool caches or follows links."""
import argparse
import hashlib
import io
import json
import os
import pathlib
import stat
import tarfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
MIB = 1024 * 1024
LIMITS = dict(total=512*MIB, file=256*MIB, logs=32*MIB, metadata=MIB, members=1024)
MANIFEST_RESERVE = 256 * 1024
PAYLOAD_RESERVE = 4 * MIB
PACKAGES = ('fs.gg.wasm.contracts', 'fs.gg.wasm.browser')
SUFFIXES = {'.js', '.mjs', '.wasm', '.html', '.json', '.dll', '.pdb', '.xml', '.map'}
LOGS = ('installed-build.log','installed-locked-build.log','installed-dotnet.log',
        'installed-fable-build.log','installed-fable.log','installed-raw-numeric.log',
        'installed-lock-refusal.log','installed-member-receipt.log')


def safe_name(name):
    p = pathlib.PurePosixPath(name)
    if not name or len(name.encode('utf-8'))>240 or p.is_absolute() or any(x in ('', '.', '..') for x in name.split('/')) or '\\' in name:
        raise ValueError('unsafe evidence member')
    return name


def consumer_member(name):
    safe_name(name)
    fixed={'consumer/release-manifest.json','consumer/NuGet.Config','consumer/consumer/packages.lock.json',
           'consumer/fsgg-wasm-sdk-0.4.0.tar.gz','consumer/installed-member-receipt.log','consumer/browser-result.json','consumer/http.log','consumer/public/sub/app/Consumer'}
    if name in fixed:return True
    for ident in PACKAGES:
        if name in {'consumer/packages/'+ident+'/'+leaf for leaf in (ident+'.0.4.0.nupkg',ident+'.0.4.0.nupkg.sha512','.nupkg.metadata')}:return True
    if name in {'consumer/core/'+leaf for leaf in ('fsharp.core.10.1.302.nupkg','fsharp.core.10.1.302.nupkg.sha512','.nupkg.metadata','reference-netstandard2.0.dll','reference-netstandard2.1.dll')}:return True
    return name.startswith('consumer/public/sub/app/') and pathlib.PurePosixPath(name).suffix in SUFFIXES and not set(pathlib.PurePosixPath(name).parts)&{'node_modules','http-cache','lock-http-cache','.git'}


def regular(path):
    for parent in (path, *path.parents):
        if parent.is_symlink(): raise ValueError('linked evidence path')
    row=path.stat()
    if not stat.S_ISREG(row.st_mode) or row.st_nlink != 1: raise ValueError('nonregular/linked evidence file')
    return row


class Projection:
    def __init__(self):
        self.files={}; self.errors=[]; self.total=0; self.logs=0; self.metadata=0
    def add(self, path, name, required=True):
        path=pathlib.Path(path)
        try:
            safe_name(name)
            if name in self.files: raise ValueError('duplicate evidence member')
            if not path.exists() and not path.is_symlink():
                if required: raise ValueError('absent required evidence')
                return
            row=regular(path)
            if row.st_size > LIMITS['file']: raise ValueError('per-file evidence cap')
            category='logs' if name.endswith('.log') else 'metadata' if name.endswith(('.json','.sha512','.Config')) or name.endswith('/.nupkg.metadata') else None
            if self.total+row.st_size > LIMITS['total']-PAYLOAD_RESERVE or len(self.files)+3 > LIMITS['members']: raise ValueError('aggregate evidence cap')
            if category and getattr(self,category)+row.st_size > LIMITS[category]-(2*MANIFEST_RESERVE if category=='metadata' else 0): raise ValueError(category+' evidence cap')
            body=path.read_bytes()
            if len(body)!=row.st_size or (path.stat().st_ino,path.stat().st_size,path.stat().st_mtime_ns)!=(row.st_ino,row.st_size,row.st_mtime_ns): raise ValueError('evidence changed during collection')
            self.files[name]=body; self.total+=len(body)
            if category: setattr(self,category,getattr(self,category)+len(body))
        except (OSError,ValueError) as error:
            self.errors.append({'member':name,'status':'absent' if not path.exists() else 'refused','reason':str(error)})
    def tree(self, path, prefix):
        path=pathlib.Path(path)
        if not path.exists():
            self.errors.append({'member':prefix,'status':'absent','reason':'required compiled output absent'}); return
        try:
            if path.is_symlink(): raise ValueError('linked evidence directory')
            # Enumerate only bounded compiled outputs; hidden/tool/cache paths are
            # not inputs to this operation and cannot enter the archive.
            count=0
            for directory, dirs, files in os.walk(path,followlinks=False):
                for name in list(dirs):
                    if name in ('node_modules','http-cache','lock-http-cache','.git') or pathlib.Path(directory,name).is_symlink():
                        self.errors.append({'member':prefix+'/'+str(pathlib.Path(directory,name).relative_to(path)),'status':'refused','reason':'linked/cache/tool output directory'})
                        dirs.remove(name)
                for name in files:
                    count+=1
                    if count>LIMITS['members']: raise ValueError('compiled output census cap')
                    file=pathlib.Path(directory,name)
                    if file.suffix not in SUFFIXES and name!='http.log' and not (prefix=='consumer/public' and file.relative_to(path).as_posix()=='sub/app/Consumer'):
                        self.errors.append({'member':prefix+'/'+file.relative_to(path).as_posix(),'status':'refused','reason':'unreviewed compiled output suffix'}); continue
                    self.add(file,prefix+'/'+file.relative_to(path).as_posix())
        except (OSError,ValueError) as error:
            self.errors.append({'member':prefix,'status':'refused','reason':str(error)})
    def packages(self, cache, prefix):
        for ident in PACKAGES:
            base=pathlib.Path(cache)/ident/'0.4.0'
            for leaf in (ident+'.0.4.0.nupkg',ident+'.0.4.0.nupkg.sha512','.nupkg.metadata'):
                self.add(base/leaf,prefix+'/'+ident+'/'+leaf)
    def core(self, cache, prefix):
        base=pathlib.Path(cache)/'fsharp.core/10.1.302'
        for name in ('fsharp.core.10.1.302.nupkg','fsharp.core.10.1.302.nupkg.sha512','.nupkg.metadata'):
            self.add(base/name,prefix+'/'+name)
        for tfm in ('netstandard2.0','netstandard2.1'):
            self.add(base/'lib'/tfm/'FSharp.Core.dll',prefix+'/reference-'+tfm+'.dll')
    def require(self, names):
        for name in names:
            if name not in self.files:
                self.errors.append({'member':name,'status':'absent','reason':'mandatory actual Core/Worker/runtime output absent'})
    def finish(self, output, runtime):
        output=pathlib.Path(output)
        if output.exists(): raise ValueError('evidence output must be fresh')
        output.mkdir(mode=0o700,parents=True)
        rows=[{'path':name,'bytes':len(body),'sha256':hashlib.sha256(body).hexdigest()} for name,body in sorted(self.files.items())]
        manifest={'schema':'fsgg.wasm.installed-evidence/v1','runtimeStatus':runtime,
                  'collectorStatus':'failed' if self.errors else 'passed','eligible':runtime==0 and not self.errors,
                  'limits':LIMITS,'members':rows,'errors':self.errors,
                  'cleanup':'recorded separately; descendant settlement unproved','upload':'pending native upload result'}
        data=(json.dumps(manifest,sort_keys=True,indent=2)+'\n').encode()
        if len(data)>MANIFEST_RESERVE or self.total+len(data)>LIMITS['total'] or self.metadata+2*len(data)>LIMITS['metadata']: raise ValueError('manifest cap')
        # Only safe portable outer names reach upload-artifact; inner original
        # hidden metadata is explicitly retained independent of provider defaults.
        def write_archive(files, metadata):
            with tarfile.open(output/'installed-evidence.tar.gz','w:gz') as archive:
                for name,body in sorted(files.items()):
                    row=tarfile.TarInfo(safe_name(name)); row.size=len(body); row.mode=0o600
                    archive.addfile(row,io.BytesIO(body))
                row=tarfile.TarInfo('evidence-manifest.json');row.size=len(metadata);row.mode=0o600;archive.addfile(row,io.BytesIO(metadata))
        write_archive(self.files,data)
        if (output/'installed-evidence.tar.gz').stat().st_size+len(data)>LIMITS['total']:
            # At most one fallback pass: retain independently bounded diagnostics,
            # mark binary proof omitted/ineligible, preserve the original exit.
            retained={name:body for name,body in self.files.items() if name.endswith(('.log','.json','.sha512','.Config','/.nupkg.metadata'))}
            self.errors.append({'member':'archive','status':'truncated','reason':'archive plus sidecar upload cap','omittedMembers':len(self.files)-len(retained)})
            manifest.update(collectorStatus='failed',eligible=False,
                            members=[row for row in rows if row['path'] in retained])
            data=(json.dumps(manifest,sort_keys=True,indent=2)+'\n').encode()
            if len(data)>MANIFEST_RESERVE or self.metadata+2*len(data)>LIMITS['metadata']: raise ValueError('fallback manifest cap')
            write_archive(retained,data)
            if (output/'installed-evidence.tar.gz').stat().st_size+len(data)>LIMITS['total']:
                (output/'installed-evidence.tar.gz').unlink()
                raise ValueError('diagnostic archive plus sidecar cap')
        (output/'evidence-manifest.json').write_bytes(data)
        return 1 if self.errors else 0


def consumer(projection, work):
    work=pathlib.Path(work)
    for name in ('release-manifest.json','NuGet.Config','consumer/packages.lock.json'):
        projection.add(work/name,'consumer/'+name)
    projection.add(work/'sdk-artifacts/fsgg-wasm-sdk-0.4.0.tar.gz','consumer/fsgg-wasm-sdk-0.4.0.tar.gz')
    projection.add(work/'installed-member-receipt.log','consumer/installed-member-receipt.log')
    projection.add(work/'consumer-http.log','consumer/http.log')
    projection.packages(work/'consumer-packages','consumer/packages')
    projection.core(work/'consumer-packages','consumer/core')
    projection.tree(work/'consumer/public','consumer/public')
    projection.add(work/'consumer/browser/test-results/.last-run.json','consumer/browser-result.json')
    projection.require(['consumer/public/sub/app/'+name for name in (
        'FSharp.Core.dll','Consumer.dll','fable/Program.js',
        '_content/FS.GG.Wasm.Browser/module-worker.mjs',
        '_content/FS.GG.Wasm.Browser/worker-client.mjs',
        '_content/FS.GG.Wasm.Browser/policy/WorkerEntry.js')])
    # Consumer.fsproj intentionally places the actual DLL/Core/runtime outputs
    # in consumer/public/sub/app, already included above.



def selected(projection, work):
    work=pathlib.Path(work)
    for name in (*LOGS,'installed.packages.lock.json','installed.bad-hash.lock.json','official-core-joins.json','NuGet.Config','release-manifest.json','promotion-binding.json'):
        original = pathlib.Path(str(work)+'-NuGet.Config') if name=='NuGet.Config' else work/name
        if name in ('release-manifest.json','promotion-binding.json'):
            original=pathlib.Path(os.environ.get('RUNNER_TEMP',''))/'wasm-installed-receipts'/name
        projection.add(original,'selected/'+name)
    projection.packages(work/'packages','selected/packages')
    projection.core(work/'packages','selected/core')
    baseline=pathlib.Path(str(work)+'-baseline')
    for name in ('receipt.json','fsharp.core.10.1.302.nupkg','fsharp.core.10.1.401.nupkg','fsharp.core.10.1.203.nupkg',
                 'FSharp.Core.10.1.302.netstandard2.0.dll','FSharp.Core.10.1.302.netstandard2.1.dll',
                 'FSharp.Core.10.1.401.netstandard2.0.dll','FSharp.Core.10.1.401.netstandard2.1.dll','FSharp.Core.10.1.203.netstandard2.1.dll'):
        projection.add(baseline/'official-core'/name,'official-core/'+name)
    projection.add(baseline/'compiler/FSharp.Core.dll','official-core/actual-compiler-Core.dll')
    projection.add(baseline/'tools/.store/fable/5.18.0/fable/5.18.0/tools/net10.0/any/FSharp.Core.dll','official-core/actual-fable-runtime-Core.dll')
    projection.tree(work/'installed-fable','selected/installed-fable')
    projection.tree(pathlib.Path(str(work)+'-browser'),'selected/browser')
    projection.add(ROOT/'tests/Wasm.Supervisor.Compatibility/browser/test-results/.last-run.json','selected/browser-result.json')
    projection.require(['selected/browser/'+name for name in (
        'fable/Program.js','fable/BrowserBridge.js','limits-guest.wasm','http.log',
        '_content/FS.GG.Wasm.Browser/module-worker.mjs',
        '_content/FS.GG.Wasm.Browser/worker-client.mjs',
        '_content/FS.GG.Wasm.Browser/policy/WorkerEntry.js')])
    projection.require(['selected/installed-fable/Program.js','selected/installed-fable/BrowserBridge.js'])
    runtime=ROOT/'tests/Wasm.Supervisor.Compatibility/Installed/bin/Debug/net10.0'
    for name in ('InstalledCompatibility.dll','InstalledCompatibility.deps.json','InstalledCompatibility.runtimeconfig.json','FSharp.Core.dll'):
        projection.add(runtime/name,'selected/runtime/'+name)


def merge(projection, source):
    source=pathlib.Path(source)
    try:
        meta=source/'evidence-manifest.json'
        if regular(meta).st_size>MIB: raise ValueError('consumer manifest cap')
        manifest=json.loads(meta.read_text())
        if not isinstance(manifest,dict) or manifest.get('schema')!='fsgg.wasm.installed-evidence/v1': raise ValueError('consumer evidence schema')
        if regular(source/'installed-evidence.tar.gz').st_size>LIMITS['total']: raise ValueError('consumer archive cap')
        with tarfile.open(source/'installed-evidence.tar.gz','r:gz') as archive:
            rows={row['path']:row for row in manifest['members']}
            if len(rows)!=len(manifest['members']) or len(rows)+3>LIMITS['members']: raise ValueError('consumer manifest duplicate/census cap')
            seen=set()
            for member in archive:
                safe_name(member.name)
                if not member.isfile() or member.name in seen: raise ValueError('unsafe/duplicate consumer archive')
                seen.add(member.name)
                if member.name=='evidence-manifest.json': continue
                if not consumer_member(member.name): raise ValueError('foreign consumer evidence member')
                expected=rows[member.name]
                if member.size!=expected['bytes'] or member.size>LIMITS['file']: raise ValueError('consumer evidence size drift')
                body=archive.extractfile(member).read(member.size+1)
                if len(body)!=member.size or hashlib.sha256(body).hexdigest()!=expected['sha256']: raise ValueError('consumer evidence hash drift')
                if projection.total+len(body)>LIMITS['total']-PAYLOAD_RESERVE or len(projection.files)+3>LIMITS['members']: raise ValueError('merged evidence cap')
                if member.name in projection.files: raise ValueError('duplicate merged member')
                category='logs' if member.name.endswith('.log') else 'metadata' if member.name.endswith(('.json','.sha512','.Config')) or member.name.endswith('/.nupkg.metadata') else None
                if category and getattr(projection,category)+len(body)>LIMITS[category]-(2*MANIFEST_RESERVE if category=='metadata' else 0): raise ValueError('merged '+category+' cap')
                projection.files[member.name]=body;projection.total+=len(body)
                if category: setattr(projection,category,getattr(projection,category)+len(body))
            if seen != set(rows)|{'evidence-manifest.json'}: raise ValueError('consumer archive roster drift')
        projection.errors.extend(manifest['errors'])
        if manifest.get('runtimeStatus')!=0 or manifest.get('collectorStatus')!='passed' or manifest.get('eligible') is not True:
            projection.errors.append({'member':'consumer','status':'failed','reason':'consumer runtime/collection not eligible'})
    except (OSError,ValueError,KeyError,TypeError,EOFError,tarfile.TarError) as error:
        projection.errors.append({'member':'consumer','status':'absent/refused','reason':str(error)})


def main():
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=('consumer','assemble'));parser.add_argument('--work',required=True);parser.add_argument('--output',required=True);parser.add_argument('--runtime',required=True,type=int);parser.add_argument('--consumer');parser.add_argument('--receipts');parser.add_argument('--logs');args=parser.parse_args()
    if args.action=='assemble' and not all((args.consumer,args.receipts,args.logs)):
        parser.error('assemble requires explicit consumer, receipts and logs inputs')
    p=Projection()
    if args.action=='consumer': consumer(p,args.work)
    else:
        merge(p,args.consumer); selected(p,args.work)
        for name in ('release-manifest.json','promotion-binding.json','installed-input-audit.json','published-native.json','fsgg-wasm-sdk-0.4.0.tar.gz'):
            p.add(pathlib.Path(args.receipts)/name,'receipts/'+name)
        for name in ('consumer.log','selected.log','runtime-status.json','consumer-status.json'):
            p.add(pathlib.Path(args.logs)/name,'execution/'+name)
        for consumer_name,receipt_name in [('consumer/release-manifest.json','receipts/release-manifest.json'),('consumer/fsgg-wasm-sdk-0.4.0.tar.gz','receipts/fsgg-wasm-sdk-0.4.0.tar.gz')]:
            if consumer_name not in p.files or receipt_name not in p.files or p.files[consumer_name]!=p.files[receipt_name]:
                p.errors.append({'member':consumer_name,'status':'absent/refused','reason':'consumer original differs from authenticated publication receipt'})
        try:
            status=p.files['execution/runtime-status.json']
            if len(status)>MIB: raise ValueError('runtime status cap')
            actual=json.loads(status)['exitCode']
            if type(actual) is not int or not 0<=actual<=255: raise ValueError('exact runtime exit absent')
            if (args.runtime==0)!=(actual==0): raise ValueError('native step/runtime status disagreement')
            args.runtime=actual
        except (KeyError,ValueError,TypeError) as error:
            p.errors.append({'member':'execution/runtime-status.json','status':'absent/refused','reason':str(error)})
            args.runtime=None
    raise SystemExit(p.finish(args.output,args.runtime))

if __name__=='__main__':main()
