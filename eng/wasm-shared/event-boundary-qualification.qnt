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
      state'=eventAt(state,input(kind,state.current,state.current.worker,if(kind=="phase")"allocate-input" else ""),state.current.deadline+offset)
    else if(stage==7 or (stage==8 and kind=="resume"))dispose
    else state'=eventAt(state,input("complete",captured,captured.worker,""),state.clock+1),
  }
  action controlStep(kind: str,offset: int): bool = all {
    stage'=stage+1,captured'=captured,
    if(stage==0)loadInitial
    else if(stage==1 and kind=="initialized")compiled
    else if(stage==2 and kind=="initialized")initialize
    else if(stage==1 or (stage==3 and kind=="initialized")){
      val r=state.controls.head()
      state'=eventAt(state,input(kind,r,r.worker,if(kind=="phase")"instantiate" else ""),r.deadline+offset)
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
  val originalInputRetained = stage<7 or state.input.name!=""
}
