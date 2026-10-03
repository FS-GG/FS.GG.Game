#!/usr/bin/env python3
"""Compare exact candidate packages to immutable 0.2.0 DLLs with native ApiCompat."""
import argparse,hashlib,json,pathlib,subprocess,urllib.request,xml.etree.ElementTree as ET,zipfile
parser=argparse.ArgumentParser()
parser.add_argument('--custody',type=pathlib.Path,required=True)
parser.add_argument('--baseline-feed',type=pathlib.Path,required=True)
parser.add_argument('--packages',type=pathlib.Path,required=True)
parser.add_argument('--candidate-version',required=True)
parser.add_argument('--sdk',type=pathlib.Path,required=True)
args=parser.parse_args()
root=args.custody.resolve();baselineRoot=args.baseline_feed.resolve();sdk=args.sdk.resolve();work=root/'api';work.mkdir(parents=True,exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
packages=args.packages.resolve();core=packages/'fsharp.core/10.1.302/lib/netstandard2.0/FSharp.Core.dll'
metadata=json.loads((core.parents[2]/'.nupkg.metadata').read_text());assert metadata['source']=='https://api.nuget.org/v3/index.json'
expectedCore=json.loads((pathlib.Path(__file__).resolve().parents[2]/'src/Wasm.Browser/packages.lock.json').read_text())['dependencies']['net10.0']['FSharp.Core']['contentHash']
assert metadata['contentHash']==expectedCore, 'official locked FSharp.Core reference mismatch'
archive=work/'fsharp.core.10.1.302.nupkg'
with urllib.request.urlopen('https://api.nuget.org/v3-flatcontainer/fsharp.core/10.1.302/fsharp.core.10.1.302.nupkg') as response:archive.write_bytes(response.read())
assert sha(archive)=='f4eda1b2efb28b38a5526b0b678e889434aa71113a4c2fa8660b62ca75cc7dfc', 'official Core archive differs'
with zipfile.ZipFile(archive) as package:officialCore=package.read('lib/netstandard2.0/FSharp.Core.dll')
assert core.read_bytes()==officialCore, 'actual ApiCompat reference Core DLL differs from official archive'
coreJoin={'version':'10.1.302','archiveSha256':sha(archive),'archiveEntry':'lib/netstandard2.0/FSharp.Core.dll','referencePath':str(core),'referenceDllSha256':sha(core),'byteIdentical':True}
contracts=work/'FS.GG.Wasm.Contracts.dll'
with zipfile.ZipFile(root/('FS.GG.Wasm.Contracts.'+args.candidate_version+'.nupkg')) as z:contracts.write_bytes(z.read('lib/net10.0/FS.GG.Wasm.Contracts.dll'))
framework=sorted((sdk.parents[1]/'packs/Microsoft.NETCore.App.Ref').glob('10.*/ref/net10.0'),key=lambda path:tuple(map(int,path.parents[1].name.split('.'))))[-1]
references=[*framework.glob('*.dll'),core,contracts];rows=[]
for identity in ['FS.GG.Wasm.Contracts','FS.GG.Wasm.Browser']:
 baseline=baselineRoot/(identity.lower()+'.0.2.0.nupkg');candidate=root/(identity+'.'+args.candidate_version+'.nupkg')
 expected={'FS.GG.Wasm.Contracts':'b5bb9d0217dc31de9649b7c952db514bab5c99d32b30e6480da1f313d446fba4','FS.GG.Wasm.Browser':'40d11feebb07b01c0846b1b9716db10badc5dfbce1a450eb5319ab89b8f041e6'}
 assert sha(baseline)==expected[identity], 'immutable published baseline archive mismatch'
 project=ET.Element('Project');ET.SubElement(project,'UsingTask',TaskName='Microsoft.DotNet.ApiCompat.Task.ValidatePackageTask',AssemblyFile=str(sdk/'Sdks/Microsoft.NET.Sdk/tools/net10.0/Microsoft.DotNet.ApiCompat.Task.dll'))
 item=ET.SubElement(ET.SubElement(project,'ItemGroup'),'References',Include='net10.0');ET.SubElement(item,'TargetFrameworkMoniker').text='.NETCoreApp,Version=v10.0';ET.SubElement(item,'ReferencePath').text=','.join(map(str,references))
 target=ET.SubElement(project,'Target',Name='Compare');ET.SubElement(target,'Microsoft.DotNet.ApiCompat.Task.ValidatePackageTask',PackageTargetPath=str(candidate),BaselinePackageTargetPath=str(baseline),RuntimeGraph=str(sdk/'PortableRuntimeIdentifierGraph.json'),RoslynAssembliesPath=str(sdk/'Roslyn/bincore'),PackageAssemblyReferences='@(References)',RunApiCompat='true',GenerateSuppressionFile='false',EnableStrictModeForBaselineValidation='false')
 path=work/(identity+'.proj');ET.ElementTree(project).write(path,encoding='unicode')
 r=subprocess.run(['/usr/share/dotnet/dotnet','exec',str(sdk/'MSBuild.dll'),str(path),'-t:Compare','-nologo','-m:1','-nr:false','-p:UseSharedCompilation=false','-verbosity:normal'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True);(work/(identity+'.log')).write_text(r.stdout);assert r.returncode==0,r.stdout
 rows.append(dict(id=identity,baselineSha256=sha(baseline),candidateSha256=sha(candidate),nativeLogSha256=hashlib.sha256(r.stdout.encode()).hexdigest(),status='compatible'))
(work/'receipt.json').write_text(json.dumps(dict(tool='SDK10.0.401.ApiCompat',baseline='0.2.0',candidate=args.candidate_version,suppressions=False,officialCore=coreJoin,packages=rows),indent=2)+'\n');print('Native ApiCompat 0.2.0: Contracts and Browser compatible, no suppressions')
