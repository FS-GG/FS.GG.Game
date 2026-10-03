// Selected SC2 Supervisor V1 scenarios, bound to the canonical producer model.
// Time is a host-observed monotonic integer; each mechanical callback is atomic.
// This models scheduling/limits/freeze effects, not guest execution or native grants.
module compatibleQualification {
  import compatibleLifecycle.* from "./lifecycle"
  var stage: int
  action initQualification = all {initCompatible,stage'=0}
  pure val small: Limits = {deadline:5,output:44,enclosing:0}
  pure val roomy: Limits = {deadline:100,output:64,enclosing:0}
  action prefix =
    if(stage==0)compatibleLoad else if(stage==1)compatibleCompiled
    else if(stage==2)compatibleInitialize(1,{deadline:10,output:44,enclosing:0})
    else compatibleInitialized(1)
  action fifoStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",roomy,1)
    else if(stage==5)compatibleSubmit("ordered",small,2)
    else if(stage==6)compatibleSubmit("ordinary",{deadline:50,output:100,enclosing:0},3)
    else if(stage==7)compatibleComplete(1) else if(stage==8)compatibleComplete(2)
    else if(stage==9)compatibleComplete(3) else compatibleDispose,
  }
  action snapshotStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("snapshot",roomy,1)
    else if(stage==5)compatibleSubmit("ordinary",roomy,2)
    else if(stage==6)compatibleSubmit("snapshot",small,3)
    else if(stage==7)compatibleSubmit("snapshot",{deadline:8,output:64,enclosing:0},4)
    else if(stage==8)compatibleComplete(10) else if(stage==9)compatibleComplete(11)
    else if(stage==10)compatibleComplete(12) else compatibleDispose,
  }
  action ownerExpiryRefusalStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",small,1)
    else if(stage==5)compatibleSubmitAt("ordinary",roomy,1,6) else compatibleDispose,
  }
  action frozenCommitExpiryStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",small,1)
    else if(stage==5)compatiblePrepare else if(stage==6)compatibleCompiled
    else if(stage==7)compatibleInitialize(2,defaultLimits) else if(stage==8)compatibleInitialized(2)
    else if(stage==9)compatibleValidate else if(stage==10)compatibleFreeze
    else if(stage==11)compatibleFrozenCommitAt(6) else compatibleDispose,
  }
  action heldEnclosingStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("snapshot",roomy,1)
    else if(stage==5)compatibleSubmit("snapshot",{deadline:10,output:44,enclosing:5},2)
    else if(stage==6)compatibleComplete(5) else compatibleDispose,
  }
  action freezeStep(activeFinished: bool): bool = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",roomy,1)
    else if(stage==5)compatibleSubmit("ordered",roomy,2)
    else if(stage==6)compatibleSubmit("snapshot",roomy,3)
    else if(stage==7)compatibleSubmit("snapshot",small,4)
    else if(stage==8)compatiblePrepare else if(stage==9)compatibleCompiled
    else if(stage==10)compatibleInitialize(2,{deadline:10,output:44,enclosing:0})
    else if(stage==11)compatibleInitialized(2) else if(stage==12)compatibleValidate
    else if(stage==13)compatibleFreeze
    else if(stage==14 and activeFinished)compatibleComplete(1)
    else if(stage==14)compatibleTimer(0)
    else if(stage==15)compatibleFrozenCommit
    else if(stage==16 and not(activeFinished))compatibleComplete(1)
    else if(stage==16)compatibleTimer(1)
    else if(stage==17)compatibleResume else compatibleDispose,
  }
  action freezeActive = freezeStep(false)
  action freezeCompleted = freezeStep(true)
  action expiryStep(observed: int): bool = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",roomy,1)
    else if(stage==5)compatiblePrepare else if(stage==6)compatibleCompiled
    else if(stage==7)compatibleInitialize(2,roomy) else if(stage==8)compatibleInitialized(2)
    else if(stage==9)compatibleValidate else if(stage==10)compatibleSubmit("ordered",small,2)
    else if(stage==11)compatibleSubmit("ordinary",roomy,3)
    else if(stage==12)compatibleComplete(observed) else compatibleDispose,
  }
  action headBefore = expiryStep(4)
  action headAt = expiryStep(5)
  action headAfter = expiryStep(6)
  action compositeStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",{deadline:10,output:44,enclosing:7},1)
    else if(stage==5)compatiblePhase("allocate-input",3)
    else if(stage==6)compatibleComplete(6) else compatibleDispose,
  }
  action bytesStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",roomy,262144)
    else if(stage<20)compatibleSubmit("ordered",roomy,262144)
    else compatibleDispose,
  }
  action capacityStep = all {stage'=stage+1,
    if(stage<4)prefix else if(stage==4)compatibleSubmit("ordinary",roomy,1)
    else if(stage<260)compatibleSubmit("ordered",roomy,1)
    else compatibleDispose,
  }
}
module compatibleQualificationTest {
  import compatibleLifecycle.* from "./lifecycle"
  run invalidLimitsAreRefusedTest = initCompatible.expect(and {
    not(canAdmit(compatibleState,"loadInitial",{deadline:0,output:44,enclosing:0},1)),
    not(canAdmit(compatibleState,"loadInitial",{deadline:251,output:44,enclosing:0},1)),
    not(canAdmit(compatibleState,"loadInitial",{deadline:10,output:65537,enclosing:0},1)),
  })
  run mixedAdmissionCountsAllClassesTest = initCompatible.then(readyCompatible)
    .then(compatibleSubmit("ordinary",defaultLimits,1))
    .then(compatibleSubmit("ordered",defaultLimits,1)).then(compatibleSubmit("ordered",defaultLimits,1))
    .then(compatibleSubmit("ordered",defaultLimits,1)).then(compatibleSubmit("ordered",defaultLimits,1))
    .expect(not(canAdmit(compatibleState,"ordinary",defaultLimits,1)))
  run frozenReceiptMustMatchTest = initCompatible.then(readyCompatible).then(compatiblePrepare)
    .then(compatibleCompiled).then(compatibleInitialize(2,defaultLimits)).then(compatibleInitialized(2))
    .then(compatibleValidate).then(compatibleFreeze).expect(and {
      canCommitFrozen(compatibleState,7,2,1,2),
      not(canCommitFrozen(compatibleState,8,2,1,2)),
      not(canCommitFrozen(compatibleState,7,3,1,2)),
      not(canCommitFrozen(compatibleState,7,2,2,2)),
      not(canCommitFrozen(compatibleState,7,2,1,3)),
    })
  run enclosingBudgetRefusesFurtherWorkTest = initCompatible.then(readyCompatible).then(compatibleTimer(7))
    .expect(not(canAdmit(compatibleState,"ordinary",{deadline:10,output:44,enclosing:7},1)))
}
