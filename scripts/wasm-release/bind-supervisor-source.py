#!/usr/bin/env python3
"""Observe exact source preparation; preserve the separately scoped root review."""
import argparse,hashlib,json,pathlib,subprocess
root=pathlib.Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--output',type=pathlib.Path,required=True);args=parser.parse_args()
def git(*args):return subprocess.check_output(['git','-C',str(root),*args],text=True).strip()
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
reviewPath=root/'tests/Wasm.Supervisor.Compatibility/root-canonical-review.json'
proofPath=root/'tests/Wasm.Supervisor.Compatibility/reviewed-source-proof.json'
r=json.loads(reviewPath.read_text());p=json.loads(proofPath.read_text())
assert r['schema']=='fsgg.wasm.root-canonical-review/v1' and r['status']=='reviewed'
assert r['sourceHead']==p['sourceHead'] and r['sourceTree']==p['sourceTree'] and r['modelSha256']==p['model']['sha256']
assert r['ownerProofSha256']==sha(proofPath) and r['provisionalRelease']=='0.3.0' and r['nativeAcceptance'] is False
version=(root/'sdk/wasm/VERSION').read_text().strip()
review=dict(ReviewSha256=sha(reviewPath),ProofSha256=sha(proofPath),SourceHead=r['sourceHead'],SourceTree=r['sourceTree'],ModelSha256=r['modelSha256'],Version=version,Inputs=[dict(Path=x['path'],Sha256=x['sha256']) for x in p['qualificationInputs']],GateRepairs=[dict(Path=x['path'],Sha256=x['sha256']) for x in r['admittedGateRepairs']])
head=git('rev-parse','HEAD')
observed=dict(CallerHead=head,ExactHead=head,Clean=not git('status','--porcelain'),ReviewedAncestor=subprocess.run(['git','-C',str(root),'merge-base','--is-ancestor',r['sourceHead'],head]).returncode==0,ReviewSha256=sha(reviewPath),ProofSha256=sha(proofPath),ModelSha256=sha(root/p['model']['path']),Inputs=[dict(Path=x['path'],Sha256=sha(root/x['path'])) for x in p['qualificationInputs']],GateRepairs=[dict(Path=x['path'],Sha256=sha(root/x['path'])) for x in r['admittedGateRepairs']],ChangedPaths=git('diff','--name-only',r['sourceHead'],head).splitlines())
args.output.mkdir(parents=True,exist_ok=False)
for name,value in [('review.json',review),('observation.json',observed)]: (args.output/name).write_text(json.dumps(value,indent=2)+'\n')
print('supervisor-source-observation: exacthead='+head+' publication=unauthorized native=pending')
