// Canonical production lifecycle abstraction. Admission/span mechanics are
// separately checked by the connected WorkerEntry -> Invocation route.
module lifecycle {
  type Profile = Bar | Sc2
  type Submission = Ordinary | Ordered | Snapshot
  type Request = { id: int, worker: int, generation: int, deadline: int,
    submission: Submission, bytes: int, operation: str, phase: str, correlation: int }
  type Effect = { kind: str, request: int, generation: int, worker: int, operation: str, phase: str, correlation: int, bytes: int, due: int }
  type State = {
    profile: Profile, clock: int, nextRequest: int, nextGeneration: int,
    activeWorker: int, activeGeneration: int, candidateWorker: int,
    candidateGeneration: int, candidateTransaction: int,
    candidateInitialized: bool, candidateReady: bool, retiringWorkers: Set[int],
    compiledWorkers: Set[int], initializedWorkers: Set[int], controls: List[Request],
    current: Request, ordinary: List[Request], ordered: List[Request], snapshot: Request,
    frozen: bool, freezeToken: int, disposed: bool, deliveries: List[int],
    effects: List[Effect], lastAction: str,
  }
  var state: State
  pure val emptyRequest: Request = {id:0,worker:0,generation:0,deadline:0,
    submission:Ordinary,bytes:0,operation:"none",phase:"process",correlation:0}
  pure def initial(profile: Profile): State = {
    profile:profile,clock:0,nextRequest:1,nextGeneration:1,activeWorker:0,activeGeneration:0,
    candidateWorker:0,candidateGeneration:0,candidateTransaction:0,candidateInitialized:false,
    candidateReady:false,retiringWorkers:Set(),compiledWorkers:Set(),initializedWorkers:Set(),
    controls:List(),current:emptyRequest,ordinary:List(),ordered:List(),snapshot:emptyRequest,
    frozen:false,freezeToken:0,disposed:false,deliveries:List(),effects:List(),lastAction:"init",
  }
  action init = { nondet profile=Set(Bar,Sc2).oneOf() state'=initial(profile) }
  action initBar = state'=initial(Bar)
  action initSc2 = state'=initial(Sc2)
  pure def effect(kind: str,r: Request): Effect = {
    kind:kind,request:r.id,generation:r.generation,worker:r.worker,
    operation:if(kind=="post")r.operation else "",
    phase:if(kind=="armTimer" or kind=="cancelTimer")r.phase else "",
    correlation:if(kind=="post" or kind=="armTimer" or kind=="cancelTimer")r.correlation else 0,
    bytes:if(kind=="post")r.bytes else 0,due:if(kind=="armTimer")r.deadline else 0,
  }
  pure def termination(worker: int): Effect = {kind:"terminate",request:0,generation:0,worker:worker,operation:"",phase:"",correlation:0,bytes:0,due:0}
  pure def requestFor(s: State,worker: int,generation: int,operation: str,phase: str,submission: Submission): Request = {
    id:s.nextRequest,worker:worker,generation:generation,deadline:s.clock+251,
    submission:submission,bytes:if(operation=="shutdown")0 else 1,operation:operation,phase:phase,
    correlation:s.nextRequest,
  }
  pure def idsFor(rs: List[Request]): List[int] = rs.foldl(List(),(out,r)=>out.append(r.id))
  pure def effectsFor(kind: str,rs: List[Request]): List[Effect] = rs.foldl(List(),(out,r)=>out.append(effect(kind,r)))
  pure def queued(s: State): List[Request] = s.ordinary.concat(s.ordered).concat(if(s.snapshot.worker==0)List() else List(s.snapshot))
  pure def pending(s: State): List[Request] = s.controls.concat(if(s.current.worker==0)List() else List(s.current)).concat(queued(s))
  pure def live(s: State): Set[int] = s.retiringWorkers.union(if(s.activeWorker==0)Set() else Set(s.activeWorker)).union(if(s.candidateWorker==0)Set() else Set(s.candidateWorker))
  pure def refuse(s: State,r: Request,reason: str): State = {
    ...s,clock:s.clock+1,nextRequest:s.nextRequest+1,deliveries:s.deliveries.append(r.id),
    effects:List(effect(reason,r)),lastAction:"refuse",
  }
  pure def startControl(s: State,r: Request,actionName: str): State = {
    ...s,clock:s.clock+1,nextRequest:s.nextRequest+1,controls:s.controls.append(r),
    effects:List(effect("armTimer",r),effect("post",r)),lastAction:actionName,
  }
  action loadInitial = all {
    not(state.disposed),not(state.frozen),state.activeWorker==0,
    val r=requestFor(state,state.nextGeneration,state.nextGeneration,"load","compile",Ordinary)
    state'={...startControl(state,r,"loadInitial"),activeWorker:r.worker,activeGeneration:r.generation,
      nextGeneration:state.nextGeneration+1,
      effects:List(effect("createActive",r),effect("armTimer",r),effect("post",r))},
  }
  action prepareCandidate = all {
    state.profile==Sc2,not(state.disposed),not(state.frozen),state.candidateWorker==0,live(state).size()<4,
    val r=requestFor(state,state.nextGeneration,state.nextGeneration,"load","compile",Ordinary)
    state'={...startControl(state,r,"prepareCandidate"),candidateWorker:r.worker,candidateGeneration:r.generation,
      candidateTransaction:r.generation,candidateInitialized:false,candidateReady:false,nextGeneration:state.nextGeneration+1,
      effects:List(effect("createCandidate",r),effect("armTimer",r),effect("post",r))},
  }
  action compiled = {
    val rs=state.controls.select(r=>r.operation=="load")
    nondet index=rs.indices().oneOf()
    val r=rs.nth(index)
    all { rs.length()>0, state'={...state,clock:state.clock+1,controls:state.controls.select(p=>p.id!=r.id),
      compiledWorkers:state.compiledWorkers.union(Set(r.worker)),deliveries:state.deliveries.append(r.id),
      effects:List(effect("cancelTimer",r),effect(if(state.frozen or r.worker==state.candidateWorker)"settleHistorical" else "settleCurrent",r)),lastAction:"compiled"} }
  }
  action initialize = {
    val available=state.compiledWorkers.exclude(state.initializedWorkers).filter(w=>not(state.controls.foldl(false,(found,r)=>found or r.worker==w)))
    nondet worker=available.oneOf()
    val generation=if(worker==state.activeWorker)state.activeGeneration else state.candidateGeneration
    val r=requestFor(state,worker,generation,"initialize","allocate-descriptor",Ordinary)
    all {not(state.disposed),not(state.frozen),available.size()>0,state'=startControl(state,r,"initialize")}
  }
  action initialized = {
    val rs=state.controls.select(r=>r.operation=="initialize")
    nondet index=rs.indices().oneOf()
    val r=rs.nth(index)
    all {rs.length()>0,state'={...state,clock:state.clock+1,controls:state.controls.select(p=>p.id!=r.id),
      initializedWorkers:state.initializedWorkers.union(Set(r.worker)),
      candidateInitialized:state.candidateInitialized or r.worker==state.candidateWorker,
      deliveries:state.deliveries.append(r.id),effects:List(effect("cancelTimer",r),effect(if(state.frozen or r.worker==state.candidateWorker)"settleHistorical" else "settleCurrent",r)),
      lastAction:"candidateInitialized"} }
  }
  pure def dispatchNext(s: State,prefix: List[Effect]): State =
    if(s.current.worker!=0){...s,effects:prefix}
    else if(s.disposed or s.frozen or s.activeWorker==0) {...s,current:emptyRequest,effects:prefix}
    else if(s.ordinary.length()>0) {...s,current:s.ordinary.head(),ordinary:s.ordinary.tail(),effects:prefix.concat(List(effect("post",s.ordinary.head()),effect("armTimer",s.ordinary.head())))}
    else if(s.ordered.length()>0) {...s,current:s.ordered.head(),ordered:s.ordered.tail(),effects:prefix.concat(List(effect("post",s.ordered.head()),effect("armTimer",s.ordered.head())))}
    else if(s.snapshot.worker!=0) {...s,current:s.snapshot,snapshot:emptyRequest,effects:prefix.concat(List(effect("post",s.snapshot),effect("armTimer",s.snapshot)))}
    else {...s,current:emptyRequest,effects:prefix}
  pure def realtimeBytes(s: State): int = s.ordered.foldl(0,(n,r)=>n+r.bytes)+s.snapshot.bytes+(if(s.current.submission==Ordinary)0 else s.current.bytes)
  pure def submit(s: State,submission: Submission,operation: str): State = {
    val r=requestFor(s,s.activeWorker,s.activeGeneration,operation,if(operation=="shutdown")"shutdown" else "allocate-descriptor",submission)
    if(s.disposed)refuse(s,r,"refusedDisposed")
    else if(s.frozen)refuse(s,r,"refusedFrozen")
    else if(s.activeWorker==0 or not(s.initializedWorkers.contains(s.activeWorker)))refuse(s,r,"refused")
    else if(s.current.worker==0){...s,clock:s.clock+1,nextRequest:s.nextRequest+1,current:r,effects:List(effect("post",r),effect("armTimer",r)),lastAction:"begin"}
    else if(s.profile==Bar)refuse(s,r,"refusedBusy")
    else if(submission==Ordinary and s.ordinary.length()>=4)refuse(s,r,"refusedQueueFull")
    else if(submission==Ordinary){...s,clock:s.clock+1,nextRequest:s.nextRequest+1,ordinary:s.ordinary.append(r),effects:List(),lastAction:"queueOrdinary"}
    else if(submission==Ordered and (s.ordered.length()+(if(s.current.submission==Ordered)1 else 0)>=256 or realtimeBytes(s)+r.bytes>4194304))refuse(s,r,"refusedQueueFull")
    else if(submission==Ordered){...s,clock:s.clock+1,nextRequest:s.nextRequest+1,ordered:s.ordered.append(r),effects:List(),lastAction:"queueOrdered"}
    else if(realtimeBytes(s)-s.snapshot.bytes+r.bytes>4194304)refuse(s,r,"refusedQueueFull")
    else {...s,clock:s.clock+1,nextRequest:s.nextRequest+1,snapshot:r,
      deliveries:if(s.snapshot.worker==0)s.deliveries else s.deliveries.append(s.snapshot.id),
      effects:if(s.snapshot.worker==0)List() else List(effect("settleCoalesced",s.snapshot)),
      lastAction:if(s.snapshot.worker==0)"queueSnapshot" else "coalesceSnapshot"}
  }
  action submitOrdinary = state'=submit(state,Ordinary,"process")
  action submitOrdered = state'=submit(state,Ordered,"process")
  action submitSnapshot = state'=submit(state,Snapshot,"process")
  action shutdown = state'=submit(state,Ordinary,"shutdown")
  pure def invalidate(s: State,worker: int,reason: str,actionName: str): State = {
    val controls=s.controls.select(r=>r.worker==worker)
    val current=if(s.current.worker==worker)List(s.current) else List()
    val removed=controls.concat(current).concat(queued(s).select(r=>r.worker==worker))
    {...s,activeWorker:if(s.activeWorker==worker)0 else s.activeWorker,
      activeGeneration:if(s.activeWorker==worker)0 else s.activeGeneration,
      candidateWorker:if(s.candidateWorker==worker)0 else s.candidateWorker,
      candidateGeneration:if(s.candidateWorker==worker)0 else s.candidateGeneration,
      candidateTransaction:if(s.candidateWorker==worker)0 else s.candidateTransaction,
      candidateInitialized:if(s.candidateWorker==worker)false else s.candidateInitialized,
      candidateReady:if(s.candidateWorker==worker)false else s.candidateReady,
      retiringWorkers:s.retiringWorkers.exclude(Set(worker)),compiledWorkers:s.compiledWorkers.exclude(Set(worker)),initializedWorkers:s.initializedWorkers.exclude(Set(worker)),
      controls:s.controls.select(r=>r.worker!=worker),current:if(s.current.worker==worker)emptyRequest else s.current,
      ordinary:s.ordinary.select(r=>r.worker!=worker),ordered:s.ordered.select(r=>r.worker!=worker),snapshot:if(s.snapshot.worker==worker)emptyRequest else s.snapshot,
      deliveries:s.deliveries.concat(idsFor(removed)),
      effects:List(termination(worker)).concat(effectsFor("cancelTimer",controls.concat(current))).concat(effectsFor(reason,removed)),lastAction:actionName}
  }
  action complete = all {
    state.current.worker!=0,
    val r=state.current
    val historical=state.frozen or r.worker!=state.activeWorker or r.generation!=state.activeGeneration
    val prefix=List(effect("cancelTimer",r),effect(if(historical)"settleHistorical" else if(r.operation=="shutdown")"settleShutdown" else "settleCurrent",r))
    val base={...state,clock:state.clock+1,current:emptyRequest,deliveries:state.deliveries.append(r.id),effects:prefix,lastAction:"complete"}
    state'=if(r.operation=="shutdown" or (historical and state.retiringWorkers.contains(r.worker)))
      { ...invalidate(base,r.worker,"settleDiscarded","complete"),effects:prefix.concat(invalidate(base,r.worker,"settleDiscarded","complete").effects) }
      else dispatchNext(base,prefix),
  }
  action rejectedTermination = all {
    state.current.worker!=0,
    val r=state.current
    val prefix=List(effect("cancelTimer",r),effect("settleRejected",r))
    val base={...state,clock:state.clock+1,current:emptyRequest,deliveries:state.deliveries.append(r.id),lastAction:"complete"}
    state'={...invalidate(base,r.worker,"settleDiscarded","complete"),effects:prefix.concat(invalidate(base,r.worker,"settleDiscarded","complete").effects)},
  }
  action cleanupFault = all {
    state.current.worker!=0,
    val r=state.current
    val prefix=List(effect("cancelTimer",r),effect("settleFaulted",r))
    val base={...state,clock:state.clock+1,current:emptyRequest,deliveries:state.deliveries.append(r.id),lastAction:"cleanupFault"}
    state'={...invalidate(base,r.worker,"settleDiscarded","cleanupFault"),effects:prefix.concat(invalidate(base,r.worker,"settleDiscarded","cleanupFault").effects)},
  }
  pure def expireAtState(s: State,now: int): State = {
    val expired=queued(s).select(r=>r.deadline<now)
    val base={...s,clock:now,ordinary:s.ordinary.select(r=>r.deadline>=now),ordered:s.ordered.select(r=>r.deadline>=now),snapshot:if(s.snapshot.worker!=0 and s.snapshot.deadline<now)emptyRequest else s.snapshot,
      deliveries:s.deliveries.concat(idsFor(expired)),effects:effectsFor("settleTimedOut",expired),lastAction:if(expired.length()>0)"expireOrdinary" else "stutter"}
    val controls=base.controls.select(r=>r.deadline<now)
    val after=controls.foldl(base,(current,r)=>{
      val following=invalidate(current,r.worker,"settleTimedOut","expireControl")
      {...following,effects:current.effects.concat(following.effects)}
    })
    if(after.current.worker!=0 and after.current.deadline<now){
      val following=invalidate(after,after.current.worker,"settleTimedOut","expireCurrent")
      {...following,effects:after.effects.concat(following.effects)}
    } else after
  }
  action expireDeadlines = state'=expireAtState(state,state.clock+1)
  action advanceAndExpireDeadlines = all {
    state.current.worker!=0,
    state'=expireAtState(state,state.current.deadline+1),
  }
  action phaseObserved = {
    val rs=state.controls.concat(if(state.current.worker==0)List() else List(state.current))
    nondet index=rs.indices().oneOf()
    val r=rs.nth(index)
    val alternatives=if(r.operation=="load" and r.phase=="compile")Set("instantiate")
      else if(r.phase=="allocate-descriptor")Set("allocate-input")
      else if(r.phase=="allocate-input")Set(r.operation)
      else if(r.phase=="initialize" or r.phase=="process")Set("free") else Set()
    nondet phase=alternatives.oneOf()
    val updated={...r,phase:phase,deadline:if(state.profile==Bar)state.clock+251 else r.deadline}
    all { rs.length()>0,alternatives.size()>0,state'={...state,clock:state.clock+1,controls:state.controls.foldl(List(),(out,p)=>out.append(if(p.id==r.id)updated else p)),current:if(state.current.id==r.id)updated else state.current,
      effects:List(effect("cancelTimer",r),effect("armTimer",updated)),lastAction:"phase"} }
  }
  action candidateValidated = all {
    state.candidateWorker!=0,state.candidateInitialized,
    state'={...state,clock:state.clock+1,candidateReady:true,effects:List(),lastAction:"candidateValidated"},
  }
  action commitCandidate = all {
    state.candidateWorker!=0,state.candidateReady,not(state.frozen),
    val rs=queued(state)
    state'={...state,clock:state.clock+1,activeWorker:state.candidateWorker,activeGeneration:state.candidateGeneration,
      candidateWorker:0,candidateGeneration:0,candidateTransaction:0,candidateInitialized:false,candidateReady:false,
      retiringWorkers:state.retiringWorkers.union(if(state.activeWorker==0)Set() else Set(state.activeWorker)),
      ordinary:List(),ordered:List(),snapshot:emptyRequest,deliveries:state.deliveries.concat(idsFor(rs)),
      effects:effectsFor("settleInvalidated",rs).concat(if(state.activeWorker!=0 and state.current.worker==0)List(termination(state.activeWorker)) else List()),lastAction:"commitCandidate"},
  }
  action abortCandidate = all {
    state.candidateWorker!=0,
    state'={...invalidate(state,state.candidateWorker,"settleInvalidated","abortCandidate"),clock:state.clock+1},
  }
  action candidateFailed = all {
    state.candidateWorker!=0,state.controls.foldl(false,(found,r)=>found or r.worker==state.candidateWorker),
    state'={...invalidate(state,state.candidateWorker,"settleDiscarded","workerFailed"),clock:state.clock+1},
  }
  action terminateRetiring = {
    nondet worker=state.retiringWorkers.oneOf()
    all {state.retiringWorkers.size()>0,state'=if(state.current.worker==worker)
      {...invalidate(state,worker,"settleDiscarded","workerFailed"),clock:state.clock+1}
      else {...state,clock:state.clock+1,retiringWorkers:state.retiringWorkers.exclude(Set(worker)),compiledWorkers:state.compiledWorkers.exclude(Set(worker)),initializedWorkers:state.initializedWorkers.exclude(Set(worker)),effects:List(),lastAction:"terminateRetiring"} }
  }
  action freeze = all { not(state.disposed),not(state.frozen),state'={...state,clock:state.clock+1,frozen:true,freezeToken:7,effects:List(),lastAction:"freeze"} }
  action resume = all { state.frozen,state'=dispatchNext({...state,clock:state.clock+1,frozen:false,freezeToken:0,lastAction:"resume"},List()) }
  action dispose = {
    val rs=pending(state)
    val workers=(if(state.activeWorker==0)List() else List(state.activeWorker)).concat(if(state.candidateWorker==0)List() else List(state.candidateWorker)).concat(state.retiringWorkers.fold(List(),(rs,w)=>rs.append(w)))
    state'=if(state.disposed){...state,clock:state.clock+1,effects:List(),lastAction:"disposeAgain"} else {
      ...state,clock:state.clock+1,activeWorker:0,activeGeneration:0,candidateWorker:0,candidateGeneration:0,candidateTransaction:0,candidateInitialized:false,candidateReady:false,
      retiringWorkers:Set(),compiledWorkers:Set(),initializedWorkers:Set(),controls:List(),current:emptyRequest,ordinary:List(),ordered:List(),snapshot:emptyRequest,disposed:true,
      deliveries:state.deliveries.concat(idsFor(rs)),effects:workers.foldl(List(),(out,w)=>out.append(termination(w))).concat(effectsFor("cancelTimer",rs)).concat(effectsFor("settleDiscarded",rs)),lastAction:"dispose"}
  }
  action stutter = state'={...state,clock:state.clock+1,effects:List(),lastAction:"stutter"}
  action step = any { loadInitial,prepareCandidate,compiled,initialize,initialized,submitOrdinary,submitOrdered,submitSnapshot,shutdown,complete,rejectedTermination,cleanupFault,phaseObserved,expireDeadlines,advanceAndExpireDeadlines,candidateValidated,commitCandidate,abortCandidate,candidateFailed,terminateRetiring,freeze,resume,dispose,stutter }
  action readyActive = loadInitial.then(compiled).then(initialize).then(initialized)
  // Reachable initialized entry complements cold-start sampling; readiness is
  // established by the same explicit load/compile/initialize actions.
  action initReady = init.then(readyActive)
  action readyCandidate = prepareCandidate.then(compiled).then(initialize).then(initialized)
  action barCorrespondenceStep =
    if(state.clock==0)loadInitial else if(state.clock==1)compiled else if(state.clock==2)initialize else if(state.clock==3)initialized
    else if(state.clock==4)submitOrdinary else if(state.clock==5)submitOrdinary else if(state.clock==6)complete else if(state.clock==7)shutdown else if(state.clock==8)complete else dispose
  action sc2CorrespondenceStep =
    if(state.clock==0)loadInitial else if(state.clock==1)compiled else if(state.clock==2)initialize else if(state.clock==3)initialized
    else if(state.clock==4)submitOrdinary else if(state.clock==5)submitSnapshot else if(state.clock==6)submitSnapshot else if(state.clock==7)prepareCandidate
    else if(state.clock==8)compiled else if(state.clock==9)initialize else if(state.clock==10)initialized else if(state.clock==11)candidateValidated
    else if(state.clock==12)commitCandidate else if(state.clock==13)complete else if(state.clock==14)freeze else if(state.clock==15)submitOrdinary else if(state.clock==16)resume else dispose
  action timeoutCorrespondenceStep =
    if(state.clock==0)loadInitial else if(state.clock==1)compiled else if(state.clock==2)initialize else if(state.clock==3)initialized
    else if(state.clock==4)submitOrdinary else if(state.clock==5)submitOrdinary else if(state.clock==6)advanceAndExpireDeadlines else dispose
  action phaseCorrespondenceStep =
    if(state.clock==0)loadInitial else if(state.clock==1)phaseObserved else if(state.clock==2)compiled
    else if(state.clock==3)initialize else if(state.clock>=4 and state.clock<=6)phaseObserved else if(state.clock==7)initialized
    else if(state.clock==8)submitOrdinary else if(state.clock>=9 and state.clock<=11)phaseObserved
    else if(state.clock==12)complete else if(state.clock==13)shutdown else if(state.clock==14)complete else dispose
  action cleanupCorrespondenceStep =
    if(state.clock==0)loadInitial else if(state.clock==1)compiled else if(state.clock==2)initialize else if(state.clock==3)initialized
    else if(state.clock==4)submitOrdinary else if(state.clock==5)prepareCandidate else if(state.clock==6)compiled
    else if(state.clock==7)initialize else if(state.clock==8)initialized else if(state.clock==9)candidateValidated
    else if(state.clock==10)commitCandidate else if(state.clock==11)terminateRetiring
    else if(state.clock==12)submitOrdinary else if(state.clock==13)rejectedTermination else dispose
  pure def orderedIdsAreIncreasing(rs: List[Request]): bool = rs.foldl({ok:true,last:0},(p,r)=>{ok:p.ok and r.id>p.last,last:r.id}).ok
  pure def hasEffect(es: List[Effect],kind: str): bool = es.foldl(false,(found,e)=>found or e.kind==kind)
  val singleTerminalDelivery = state.deliveries.foldl(Set(),(seen,id)=>seen.union(Set(id))).size()==state.deliveries.length()
  val staleCannotBeCurrent = state.effects.foldl(true,(valid,e)=>valid and (e.kind!="settleCurrent" or e.generation==state.activeGeneration))
  val candidateHasNoAuthority = state.candidateWorker==0 or state.candidateGeneration!=state.activeGeneration
  val workerCapacityIncludesRetiring = live(state).size()<=4
  val queuesBounded = state.ordinary.length()<=4 and state.ordered.length()+(if(state.current.submission==Ordered)1 else 0)<=256 and realtimeBytes(state)<=4194304
  val orderedRequestsPreserveOrder = orderedIdsAreIncreasing(state.ordered)
  val freezeSuppressesCurrentDelivery = not(state.frozen) or not(hasEffect(state.effects,"settleCurrent"))
  val cleanupFailureNeverSucceeds = state.lastAction!="cleanupFault" or not(hasEffect(state.effects,"settleCurrent"))
  val disposedHasNoLiveWork = not(state.disposed) or (live(state).size()==0 and pending(state).length()==0 and state.compiledWorkers.size()==0 and state.initializedWorkers.size()==0)
  val readinessRequiresCompilation = state.initializedWorkers.subseteq(state.compiledWorkers) and state.compiledWorkers.subseteq(live(state))
  val currentRequiresReadiness = state.current.worker==0 or state.initializedWorkers.contains(state.current.worker)
  val timeoutTerminates = state.lastAction!="expireCurrent" or hasEffect(state.effects,"terminate")
  val lifecycleSafe = and { singleTerminalDelivery,staleCannotBeCurrent,candidateHasNoAuthority,workerCapacityIncludesRetiring,queuesBounded,orderedRequestsPreserveOrder,freezeSuppressesCurrentDelivery,cleanupFailureNeverSucceeds,disposedHasNoLiveWork,readinessRequiresCompilation,currentRequiresReadiness,timeoutTerminates }
  val sawBusyRefusal = hasEffect(state.effects,"refusedBusy")
  val sawSnapshotCoalesced = hasEffect(state.effects,"settleCoalesced")
  val sawHistoricalResult = hasEffect(state.effects,"settleHistorical")
  val sawQueuedTimeout = hasEffect(state.effects,"settleTimedOut")
}
module lifecycleTest {
  import lifecycle.*
  run barRefusesBusyTest = initBar.then(readyActive).then(submitOrdinary).then(submitOrdinary).expect(hasEffect(state.effects,"refusedBusy"))
  run sc2OrdinaryQueueIsFourTest = initSc2.then(readyActive).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).expect(and {state.ordinary.length()==4,hasEffect(state.effects,"refusedQueueFull")})
  run latestSnapshotCoalescesTest = initSc2.then(readyActive).then(submitOrdinary).then(submitSnapshot).then(submitSnapshot).expect(and {state.snapshot.id==5,hasEffect(state.effects,"settleCoalesced")})
  run candidateFailurePreservesActiveTest = initSc2.then(readyActive).then(prepareCandidate).then(candidateFailed).expect(and {state.activeWorker==1,state.candidateWorker==0,state.initializedWorkers.contains(1)})
  run commitMakesOldCompletionHistoricalTest = initSc2.then(readyActive).then(submitOrdinary).then(readyCandidate).then(candidateValidated).then(commitCandidate).then(complete).expect(hasEffect(state.effects,"settleHistorical"))
  run freezeBlocksAndSuppressesTest = initSc2.then(readyActive).then(submitOrdinary).then(freeze).then(complete).then(submitOrdinary).expect(and {state.frozen,hasEffect(state.effects,"refusedFrozen")})
  run cleanupFaultIsNotSuccessTest = initSc2.then(readyActive).then(submitOrdinary).then(cleanupFault).expect(and {state.activeWorker==0,hasEffect(state.effects,"terminate")})
  run disposalSettlesEverythingOnceTest = initSc2.then(readyActive).then(submitOrdinary).then(submitOrdinary).then(prepareCandidate).then(dispose).then(dispose).expect(and {state.disposed,state.controls.length()==0,singleTerminalDelivery})
}
