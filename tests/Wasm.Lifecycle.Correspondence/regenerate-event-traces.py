#!/usr/bin/env python3
"""Bounded pinned-Quint trace acquisition; expected bytes come only from canonical execution."""
import os,subprocess,tempfile,pathlib,sys,importlib.util
sys.dont_write_bytecode=True
ROOT=pathlib.Path(__file__).resolve().parents[2]
HERE=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('projection',HERE/'generate-traces.py');projection=importlib.util.module_from_spec(spec);spec.loader.exec_module(projection)
quint=os.environ.get('QUINT','quint')
assert subprocess.check_output([quint,'--version'],text=True).strip()=='0.32.0'
check='--check' in sys.argv
out=pathlib.Path(tempfile.mkdtemp(prefix='wasm-event-traces-')) if check else HERE/'Traces'
# Each fresh Worker's readiness receipt is one additional step after its load.
schedules=[('bar','lifecycle','initBar','barCorrespondenceStep',11,'lifecycle.qnt'),('sc2','lifecycle','initSc2','sc2CorrespondenceStep',20,'lifecycle.qnt'),('timeout','lifecycle','initSc2','timeoutCorrespondenceStep',9,'lifecycle.qnt'),('phase','lifecycle','initBar','phaseCorrespondenceStep',17,'lifecycle.qnt'),('cleanup','lifecycle','initSc2','cleanupCorrespondenceStep',17,'lifecycle.qnt')]
for case in ['Before','At','After','Rejected','Cleanup','AdapterRejected']:
 schedules.append(('boundary-terminal-'+case.lower(),'eventBoundaryQualification','initBoundary','terminal'+case,12,'event-boundary-qualification.qnt'))
for timing in ['early','late']:schedules.append(('expiry-'+timing,'expiryQualification','initSc2',timing+'Step',9,'expiry-qualification.qnt'))
for action in ['dispose','completion','timer','phase','freeze','resume','compile','initialize']:
 for boundary in ['Before','At','After']:
  schedules.append(('boundary-'+action+'-'+boundary.lower(),'eventBoundaryQualification','initBoundary',action+boundary,11,'event-boundary-qualification.qnt'))
schedules.append(('boundary-dispose-both-expired','eventBoundaryQualification','initBoundary','disposeBothExpired',11,'event-boundary-qualification.qnt'))
for population in ['candidate','retiring']:
 for boundary in ['Before','At','After']:
  schedules.append(('boundary-'+population+'-'+boundary.lower(),'eventBoundaryQualification','initBoundary',population+boundary,15,'event-boundary-qualification.qnt'))
for stage in ['CurrentExpired','OrderedExpired','AllExpired']:
 schedules.append(('boundary-mixed-'+stage.lower(),'eventBoundaryQualification','initBoundary','mixed'+stage,10,'event-boundary-qualification.qnt'))
schedules.append(('boundary-monotonic-clamp','eventBoundaryQualification','initBoundary','clampStep',10,'event-boundary-qualification.qnt'))
for boundary in ['Before','At','After']:
 schedules.append(('boundary-bar-phase-'+boundary.lower(),'eventBoundaryQualification','initBarBoundary','phase'+boundary,11,'event-boundary-qualification.qnt'))
schedules.append(('pending-load-commit','pendingLoadCommitQualification','initRegression','regressionStep',17,'event-boundary-qualification.qnt'))
schedules.append(('pending-initialize-commit','pendingInitializeCommitQualification','initRegression','regressionStep',12,'event-boundary-qualification.qnt'))
for boundary in ['Before','At','After']:
 schedules.append(('boundary-start-'+boundary.lower(),'eventBoundaryQualification','initBoundary','start'+boundary,4,'event-boundary-qualification.qnt'))
schedules.append(('boundary-start-early-compiled','eventBoundaryQualification','initBoundary','earlyCompiledStep',5,'event-boundary-qualification.qnt'))
schedules.append(('boundary-candidate-start-timeout','eventBoundaryQualification','initBoundary','candidateStartStep',8,'event-boundary-qualification.qnt'))
for name,module,initial,step,count,model in schedules:
 projection.run_quint_schedule([quint,'run','--verbosity=1','--backend=typescript','--main='+module,'--init='+initial,'--step='+step,'--invariant=lifecycleSafe','--max-steps='+str(count),'--max-samples=1','--seed=20261002','--out-itf='+str(out/(name+'_{seq}.itf.json')),str(ROOT/'eng/wasm-shared'/model)],name,out/(name+'_0.itf.json'))
target=out/'GeneratedTraces.fs' if check else HERE/'GeneratedTraces.fs'
subprocess.run([sys.executable,str(HERE/'generate-traces.py'),'--traces',str(out),'--output',str(target)],check=True)
if check:
 assert {p.name for p in out.glob('*.itf.json')}=={p.name for p in (HERE/'Traces').glob('*.itf.json')},'trace census drift'
 for p in out.glob('*.itf.json'):assert p.read_bytes()==(HERE/'Traces'/p.name).read_bytes(),p.name+' stale'
 assert target.read_bytes()==(HERE/'GeneratedTraces.fs').read_bytes(),'generated F# stale'
print('canonical-event-traces: PASS schedules='+str(len(schedules))+' generatedFromCanonical=true')
