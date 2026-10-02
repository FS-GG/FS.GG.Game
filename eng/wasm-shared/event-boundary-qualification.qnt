// Deterministic witnesses over the ONE canonical event boundary. No runtime copy.
module eventBoundaryQualification {
  import lifecycle.* from "./lifecycle"
  var stage: int
  var captured: Request
  action initBoundary = all { initSc2,stage'=0,captured'=emptyRequest }
  action initBarBoundary = all { initBar,stage'=0,captured'=emptyRequest }
  action boundaryStep(kind: str,offset: int): bool = all {
    stage'=stage+1,
    captured'=if(stage==6)state.current else captured,
    if(stage==0)loadInitial
    else if(stage==1)compiled
    else if(stage==2)initialize
    else if(stage==3)initialized
    else if(stage==4)submitOrdinary
    else if(stage==5)submitOrdinary
    else if(stage==6 and kind=="resume")freeze
    else if(stage==6 or (stage==7 and kind=="resume"))
      eventAtTime(input(kind,state.current,state.current.worker,if(kind=="phase")"allocate-input" else ""),state.current.deadline+offset)
    else if(stage==7 or (stage==8 and kind=="resume"))dispose
    else eventAtTime(input("complete",captured,captured.worker,""),state.clock+1),
  }
  action controlStep(kind: str,offset: int): bool = all {
    stage'=stage+1,captured'=captured,
    if(stage==0)loadInitial
    else if(stage==1 and kind=="initialized")compiled
    else if(stage==2 and kind=="initialized")initialize
    else if(stage==1 or (stage==3 and kind=="initialized")){
      val r=state.controls.head()
      eventAtTime(input(kind,r,r.worker,if(kind=="phase")"instantiate" else ""),r.deadline+offset)
    }else dispose,
  }
  action disposeBefore = boundaryStep("dispose",-1)
  action disposeAt = boundaryStep("dispose",0)
  action disposeAfter = boundaryStep("dispose",1)
  action disposeBothExpired = boundaryStep("dispose",2)
  action completionBefore = boundaryStep("complete",-1)
  action completionAt = boundaryStep("complete",0)
  action completionAfter = boundaryStep("complete",1)
  action timerBefore = boundaryStep("timer",-1)
  action timerAt = boundaryStep("timer",0)
  action timerAfter = boundaryStep("timer",1)
  action phaseBefore = boundaryStep("phase",-1)
  action phaseAt = boundaryStep("phase",0)
  action phaseAfter = boundaryStep("phase",1)
  action freezeBefore = boundaryStep("freeze",-1)
  action freezeAt = boundaryStep("freeze",0)
  action freezeAfter = boundaryStep("freeze",1)
  action resumeBefore = boundaryStep("resume",-1)
  action resumeAt = boundaryStep("resume",0)
  action resumeAfter = boundaryStep("resume",1)
  action compileBefore = controlStep("compiled",-1)
  action compileAt = controlStep("compiled",0)
  action compileAfter = controlStep("compiled",1)
  action initializeBefore = controlStep("initialized",-1)
  action initializeAt = controlStep("initialized",0)
  action initializeAfter = controlStep("initialized",1)
  // Candidate control survives active expiry; retiring current must not gain authority.
  action populationStep(retiring: bool,offset: int): bool = all {
    stage'=stage+1,captured'=captured,
    if(stage==0)loadInitial else if(stage==1)compiled else if(stage==2)initialize else if(stage==3)initialized
    else if(stage==4)submitOrdinary else if(stage==5)prepareCandidate else if(stage==6)compiled else if(stage==7)initialize
    else if(retiring and stage==8)initialized else if(retiring and stage==9)candidateValidated else if(retiring and stage==10)commitCandidate
    else if(stage==8 or (retiring and stage==11))eventAtTime(input("dispose",emptyRequest,0,""),state.current.deadline+offset)
    else dispose,
  }
  action candidateBefore = populationStep(false,-1)
  action candidateAt = populationStep(false,0)
  action candidateAfter = populationStep(false,1)
  action retiringBefore = populationStep(true,-1)
  action retiringAt = populationStep(true,0)
  action retiringAfter = populationStep(true,1)
  action mixedStep(offset: int): bool = all {
    stage'=stage+1,captured'=captured,
    if(stage==0)loadInitial else if(stage==1)compiled else if(stage==2)initialize else if(stage==3)initialized
    else if(stage==4)submitOrdinary else if(stage==5)submitOrdered else if(stage==6)submitSnapshot
    else if(stage==7)eventAtTime(input("dispose",emptyRequest,0,""),state.current.deadline+offset)
    else dispose,
  }
  action mixedCurrentExpired = mixedStep(1)
  action mixedOrderedExpired = mixedStep(2)
  action mixedAllExpired = mixedStep(3)
  action clampStep = all {stage'=stage+1,captured'=captured,
    if(stage==0)loadInitial else if(stage==1)compiled else if(stage==2)initialize else if(stage==3)initialized
    else if(stage==4)submitOrdinary else if(stage==5)dispose
    else if(stage==6)eventAtTime(input("dispose",emptyRequest,0,""),state.clock-1)
    else eventAtTime(input("timer",emptyRequest,0,""),state.clock-1),
  }
  val originalInputRetained = stage<7 or state.input.name!=""
}

module pendingLoadCommitQualification {
  import lifecycle.* from "./lifecycle"
  var stage: int
  action initRegression = all {initSc2,stage'=0}
  // Reach the counterexample through public actions: expire old active, retain
  // ready candidate, load a new active control, then commit that candidate.
  action regressionStep = all {
    stage'=stage+1,
    if(stage==0)loadInitial else if(stage==1)compiled
    else if(stage==2)initialize else if(stage==3)initialized
    else if(stage==4)prepareCandidate else if(stage==5)compiled
    else if(stage==6)initialize else if(stage==7)initialized
    else if(stage==8)candidateValidated else if(stage==9)submitOrdinary
    else if(stage==10)advanceAndExpireDeadlines
    else if(stage==11)loadInitial else if(stage==12)commitCandidate
    else dispose,
  }
}

// Bound controls invoke the same canonical transition and mechanical closure.
module callbackClosureTest {
  import lifecycle.* from "./lifecycle"
  run invalidationClearsWorkerTest = initSc2.then(loadInitial).expect({
    val cleared=eventAt(state,input("workerTerminated",emptyRequest,1,""),state.clock)
    and {pending(cleared).length()==0,live(cleared).size()==0,cleared.compiledWorkers.size()==0,cleared.initializedWorkers.size()==0}
  })
  run clearedSecondCallbackIsQuiescentTest = initSc2.then(loadInitial).expect({
    val cleared=eventAt(state,input("workerTerminated",emptyRequest,1,""),state.clock)
    val again=eventAt(cleared,input("workerTerminated",emptyRequest,1,""),state.clock)
    again.effects.length()==0
  })
  run unfinishedClosureIsRejectedTest = initSc2.then(loadInitial).expect({
    val unfinished={host:state,effects:List(),callbacks:List(),beforeEffects:List(),callbacksQuiescent:true}
    not(connectedFinalEffect(unfinished,termination(1),state.clock).callbacksQuiescent)
  })
}

module pendingInitializeCommitQualification {
  import lifecycle.* from "./lifecycle"
  var stage: int
  action initRegression = all {initSc2,stage'=0}
  action regressionStep = all {
    stage'=stage+1,
    if(stage==0)loadInitial else if(stage==1)compiled
    else if(stage==2)initialize // Keep the active initialization control pending.
    else if(stage==3)prepareCandidate else if(stage==4)compiled
    else if(stage==5)initialize
    else if(stage==6){
      val r=state.controls.select(control=>control.worker==state.candidateWorker and control.operation=="initialize").head()
      event(input("initialized",r,r.worker,""))
    } else if(stage==7)candidateValidated else if(stage==8)commitCandidate
    else dispose,
  }
}
