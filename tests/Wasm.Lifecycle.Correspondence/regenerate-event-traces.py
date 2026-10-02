#!/usr/bin/env python3
"""Bounded pinned-Quint trace acquisition; expected bytes come only from canonical execution."""
import os,subprocess,tempfile,pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
HERE=pathlib.Path(__file__).resolve().parent
quint=os.environ.get('QUINT','quint')
assert subprocess.check_output([quint,'--version'],text=True).strip()=='0.32.0'
check='--check' in sys.argv
out=pathlib.Path(tempfile.mkdtemp(prefix='wasm-event-traces-')) if check else HERE/'Traces'
schedules=[('bar','lifecycle','initBar','barCorrespondenceStep',10,'lifecycle.qnt'),('sc2','lifecycle','initSc2','sc2CorrespondenceStep',18,'lifecycle.qnt'),('timeout','lifecycle','initSc2','timeoutCorrespondenceStep',8,'lifecycle.qnt'),('phase','lifecycle','initBar','phaseCorrespondenceStep',16,'lifecycle.qnt'),('cleanup','lifecycle','initSc2','cleanupCorrespondenceStep',15,'lifecycle.qnt')]
for timing in ['early','late']:schedules.append(('expiry-'+timing,'expiryQualification','initSc2',timing+'Step',8,'expiry-qualification.qnt'))
for action in ['dispose','completion','timer','phase','freeze','resume','compile','initialize']:
 for boundary in ['Before','At','After']:
  schedules.append(('boundary-'+action+'-'+boundary.lower(),'eventBoundaryQualification','initBoundary',action+boundary,10,'event-boundary-qualification.qnt'))
schedules.append(('boundary-dispose-both-expired','eventBoundaryQualification','initBoundary','disposeBothExpired',10,'event-boundary-qualification.qnt'))
for population in ['candidate','retiring']:
 for boundary in ['Before','At','After']:
  schedules.append(('boundary-'+population+'-'+boundary.lower(),'eventBoundaryQualification','initBoundary',population+boundary,13,'event-boundary-qualification.qnt'))
for stage in ['CurrentExpired','OrderedExpired','AllExpired']:
 schedules.append(('boundary-mixed-'+stage.lower(),'eventBoundaryQualification','initBoundary','mixed'+stage,9,'event-boundary-qualification.qnt'))
schedules.append(('boundary-monotonic-clamp','eventBoundaryQualification','initBoundary','clampStep',9,'event-boundary-qualification.qnt'))
for boundary in ['Before','At','After']:
 schedules.append(('boundary-bar-phase-'+boundary.lower(),'eventBoundaryQualification','initBarBoundary','phase'+boundary,10,'event-boundary-qualification.qnt'))
schedules.append(('pending-load-commit','pendingLoadCommitQualification','initRegression','regressionStep',14,'event-boundary-qualification.qnt'))
for name,module,initial,step,count,model in schedules:
 subprocess.run([quint,'run','--backend=typescript','--main='+module,'--init='+initial,'--step='+step,'--invariant=lifecycleSafe','--max-steps='+str(count),'--max-samples=1','--seed=20261002','--out-itf='+str(out/(name+'_{seq}.itf.json')),str(ROOT/'eng/wasm-shared'/model)],check=True,stdout=subprocess.DEVNULL)
target=out/'GeneratedTraces.fs' if check else HERE/'GeneratedTraces.fs'
subprocess.run([sys.executable,str(HERE/'generate-traces.py'),'--traces',str(out),'--output',str(target)],check=True)
if check:
 assert {p.name for p in out.glob('*.itf.json')}=={p.name for p in (HERE/'Traces').glob('*.itf.json')},'trace census drift'
 for p in out.glob('*.itf.json'):assert p.read_bytes()==(HERE/'Traces'/p.name).read_bytes(),p.name+' stale'
 assert target.read_bytes()==(HERE/'GeneratedTraces.fs').read_bytes(),'generated F# stale'
print('canonical-event-traces: PASS schedules='+str(len(schedules))+' generatedFromCanonical=true')
