// Canonical production lifecycle abstraction. Admission/span mechanics are
// separately checked by the connected WorkerEntry -> Invocation route.
module lifecycle {
  type Profile = Bar | Sc2
  type Submission = Ordinary | Ordered | Snapshot
  type Request = { id: int, worker: int, generation: int, deadline: int,
    submission: Submission, bytes: int, operation: str, phase: str, correlation: int }
  type Effect = { kind: str, request: int, generation: int, worker: int, operation: str, phase: str, correlation: int, bytes: int, due: int, reason: str, outcome: str, terminalPhase: str, dispatched: bool }
  type Input = { name: str, request: Request, worker: int, phase: str, observed: int }
  type State = {
    profile: Profile, clock: int, nextRequest: int, nextGeneration: int,
    activeWorker: int, activeGeneration: int, candidateWorker: int,
    candidateGeneration: int, candidateTransaction: int,
    candidateInitialized: bool, candidateReady: bool, retiringWorkers: Set[int],
    compiledWorkers: Set[int], initializedWorkers: Set[int], controls: List[Request],
    current: Request, ordinary: List[Request], ordered: List[Request], snapshot: Request,
    frozen: bool, freezeToken: int, disposed: bool, deliveries: List[int],
    effects: List[Effect], lastAction: str, input: Input,
  }
  type Connection = {host: State, effects: List[Effect], callbacks: List[Input], beforeEffects: List[State], callbacksQuiescent: bool}
  var state: State
  var connection: Connection
  pure val emptyRequest: Request = {id:0,worker:0,generation:0,deadline:0,
    submission:Ordinary,bytes:0,operation:"none",phase:"process",correlation:0}
  pure def initial(profile: Profile): State = {
    profile:profile,clock:0,nextRequest:1,nextGeneration:1,activeWorker:0,activeGeneration:0,
    candidateWorker:0,candidateGeneration:0,candidateTransaction:0,candidateInitialized:false,
    candidateReady:false,retiringWorkers:Set(),compiledWorkers:Set(),initializedWorkers:Set(),
    controls:List(),current:emptyRequest,ordinary:List(),ordered:List(),snapshot:emptyRequest,
    frozen:false,freezeToken:0,disposed:false,deliveries:List(),effects:List(),lastAction:"init",input:{name:"init",request:emptyRequest,worker:0,phase:"",observed:0},
  }
  pure def effect(kind: str,r: Request): Effect = {
    kind:kind,request:r.id,generation:r.generation,worker:r.worker,
    operation:if(kind=="post")r.operation else "",
    phase:if(kind=="armTimer" or kind=="cancelTimer")r.phase else "",
    correlation:if(kind=="post" or kind=="armTimer" or kind=="cancelTimer")r.correlation else 0,
    bytes:if(kind=="post")r.bytes else 0,due:if(kind=="armTimer")r.deadline else 0,
    reason:if(kind=="settleTimedOut")"DeadlineExpired" else "",outcome:"",terminalPhase:"",dispatched:false,
  }
  pure def termination(worker: int): Effect = {kind:"terminate",request:0,generation:0,worker:worker,operation:"",phase:"",correlation:0,bytes:0,due:0,reason:"",outcome:"",terminalPhase:"",dispatched:false}
  pure def requestFor(s: State,worker: int,generation: int,operation: str,phase: str,submission: Submission): Request = {
    id:s.nextRequest,worker:worker,generation:generation,deadline:s.clock+250,
    submission:submission,bytes:if(operation=="shutdown")0 else 1,operation:operation,phase:phase,
    correlation:s.nextRequest,
  }
  pure def idsFor(rs: List[Request]): List[int] = rs.foldl(List(),(out,r)=>out.append(r.id))
  pure def effectsFor(kind: str,rs: List[Request]): List[Effect] = rs.foldl(List(),(out,r)=>out.append(effect(kind,r)))
  pure def queued(s: State): List[Request] = s.ordinary.concat(s.ordered).concat(if(s.snapshot.worker==0)List() else List(s.snapshot))
  pure def pending(s: State): List[Request] = s.controls.concat(if(s.current.worker==0)List() else List(s.current)).concat(queued(s))
  pure def live(s: State): Set[int] = s.retiringWorkers.union(if(s.activeWorker==0)Set() else Set(s.activeWorker)).union(if(s.candidateWorker==0)Set() else Set(s.candidateWorker))
  pure def refuse(s: State,r: Request,reason: str): State = {
    ...s,clock:s.clock,nextRequest:s.nextRequest+1,deliveries:s.deliveries.append(r.id),
    effects:List(effect(reason,r)),lastAction:"refuse",
  }
  pure def startControl(s: State,r: Request,actionName: str): State = {
    ...s,clock:s.clock,nextRequest:s.nextRequest+1,controls:s.controls.append(r),
    effects:List(effect("armTimer",r),effect("post",r)),lastAction:actionName,
  }
  pure def dispatchNext(s: State,prefix: List[Effect]): State =
    if(s.current.worker!=0){...s,effects:prefix}
    else if(s.disposed or s.frozen or s.activeWorker==0) {...s,current:emptyRequest,effects:prefix}
    else if(s.ordinary.length()>0) {...s,current:s.ordinary.head(),ordinary:s.ordinary.tail(),effects:prefix.concat(List(effect("post",s.ordinary.head()),effect("armTimer",s.ordinary.head())))}
    else if(s.ordered.length()>0) {...s,current:s.ordered.head(),ordered:s.ordered.tail(),effects:prefix.concat(List(effect("post",s.ordered.head()),effect("armTimer",s.ordered.head())))}
    else if(s.snapshot.worker!=0) {...s,current:s.snapshot,snapshot:emptyRequest,effects:prefix.concat(List(effect("post",s.snapshot),effect("armTimer",s.snapshot)))}
    else {...s,current:emptyRequest,effects:prefix}
  pure def realtimeBytes(s: State): int = s.ordered.foldl(0,(n,r)=>n+r.bytes)+s.snapshot.bytes+(if(s.current.submission==Ordinary)0 else s.current.bytes)
  pure def submit(s: State,r: Request): State = {
    val submission=r.submission
    if(s.disposed)refuse(s,r,"refusedDisposed")
    else if(s.frozen)refuse(s,r,"refusedFrozen")
    else if(s.activeWorker==0 or not(s.initializedWorkers.contains(s.activeWorker)))refuse(s,r,"refused")
    else if(s.current.worker==0){...s,clock:s.clock,nextRequest:s.nextRequest+1,current:r,effects:List(effect("post",r),effect("armTimer",r)),lastAction:"begin"}
    else if(s.profile==Bar)refuse(s,r,"refusedBusy")
    else if(submission==Ordinary and s.ordinary.length()>=4)refuse(s,r,"refusedQueueFull")
    else if(submission==Ordinary){...s,clock:s.clock,nextRequest:s.nextRequest+1,ordinary:s.ordinary.append(r),effects:List(),lastAction:"queueOrdinary"}
    else if(submission==Ordered and (s.ordered.length()+(if(s.current.submission==Ordered)1 else 0)>=256 or realtimeBytes(s)+r.bytes>4194304))refuse(s,r,"refusedQueueFull")
    else if(submission==Ordered){...s,clock:s.clock,nextRequest:s.nextRequest+1,ordered:s.ordered.append(r),effects:List(),lastAction:"queueOrdered"}
    else if(realtimeBytes(s)-s.snapshot.bytes+r.bytes>4194304)refuse(s,r,"refusedQueueFull")
    else {...s,clock:s.clock,nextRequest:s.nextRequest+1,snapshot:r,
      deliveries:if(s.snapshot.worker==0)s.deliveries else s.deliveries.append(s.snapshot.id),
      effects:if(s.snapshot.worker==0)List() else List(effect("settleCoalesced",s.snapshot)),
      lastAction:if(s.snapshot.worker==0)"queueSnapshot" else "coalesceSnapshot"}
  }
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
      effects:List(termination(worker)).concat(effectsFor("cancelTimer",controls.concat(current))).concat(removed.foldl(List(),(out,r)=>out.append(if(reason=="settleTimedOut" or reason=="settleFaulted"){...effect(if(reason=="settleFaulted")"settleDiscarded" else reason,r),reason:if(reason=="settleTimedOut")"DeadlineExpired" else "WorkerFault:worker terminated",outcome:if(reason=="settleTimedOut")"TimedOut" else "Faulted",terminalPhase:r.phase,dispatched:(controls.concat(current).foldl(false,(found,p)=>found or p.id==r.id)) and Set("initialize","process","free","shutdown").contains(r.phase)}else effect(reason,r)))),lastAction:actionName}
  }
  pure def expireAtState(s: State,now: int): State = {
    val time=if(now<s.clock)s.clock else now
    val expired=queued(s).select(r=>r.deadline<time)
    val base={...s,clock:time,ordinary:s.ordinary.select(r=>r.deadline>=time),ordered:s.ordered.select(r=>r.deadline>=time),snapshot:if(s.snapshot.worker!=0 and s.snapshot.deadline<time)emptyRequest else s.snapshot,
      deliveries:s.deliveries.concat(idsFor(expired)),effects:effectsFor("settleTimedOut",expired),lastAction:if(expired.length()>0)"expireOrdinary" else s.lastAction}
    val controls=base.controls.select(r=>r.deadline<time)
    val after=controls.foldl(base,(current,r)=>{
      val following=invalidate(current,r.worker,"settleTimedOut","expireControl")
      {...following,effects:current.effects.concat(following.effects)}
    })
    if(after.current.worker!=0 and after.current.deadline<time){
      val following=invalidate(after,after.current.worker,"settleTimedOut","expireCurrent")
      {...following,effects:after.effects.concat(following.effects)}
    } else after
  }
  // Original input is retained even if expiry removes its request or worker.
  // Handler runs at the already-clamped event clock; it never advances time.
  pure def input(name: str,r: Request,worker: int,phase: str): Input = {name:name,request:r,worker:worker,phase:phase,observed:0}
  pure def requestPresent(s: State,r: Request): bool = pending(s).foldl(false,(found,p)=>found or (p.id==r.id and p.worker==r.worker and p.generation==r.generation))
  pure def handle(s: State,e: Input): State = {
    val r=e.request
    if(e.name=="loadInitial" or e.name=="prepareCandidate"){
      val fresh=r
      if(e.name=="loadInitial") {
        val afterActive=if(s.activeWorker!=0)invalidate(s,s.activeWorker,"settleInvalidated","loadInitial")else {...s,effects:List()}
        val afterCandidate=if(afterActive.candidateWorker!=0)invalidate(afterActive,afterActive.candidateWorker,"settleInvalidated","loadInitial")else {...afterActive,effects:List()}
        val prefix=afterActive.effects.concat(afterCandidate.effects)
        {...startControl(afterCandidate,fresh,"loadInitial"),activeWorker:fresh.worker,activeGeneration:fresh.generation,nextGeneration:s.nextGeneration+1,effects:prefix.concat(List(effect("createActive",fresh),effect("armTimer",fresh),effect("post",fresh)))}
      }
      else {...startControl(s,fresh,"prepareCandidate"),candidateWorker:fresh.worker,candidateGeneration:fresh.generation,candidateTransaction:fresh.generation,candidateInitialized:false,candidateReady:false,nextGeneration:s.nextGeneration+1,effects:List(effect("createCandidate",fresh),effect("armTimer",fresh),effect("post",fresh))}
    } else if(e.name=="initialize")
      if(not(live(s).contains(e.worker))) refuse(s,r,"refused") else startControl(s,r,"initialize")
    else if(e.name=="compiled" or e.name=="initialized"){
      if(not(requestPresent(s,r)))s else {
        val following={...s,controls:s.controls.select(p=>p.id!=r.id),deliveries:s.deliveries.append(r.id),effects:List(effect("cancelTimer",r),effect(if(s.frozen or r.worker==s.candidateWorker)"settleHistorical" else "settleCurrent",r))}
        if(e.name=="compiled"){...following,compiledWorkers:s.compiledWorkers.union(Set(r.worker)),lastAction:"compiled"}
        else {...following,initializedWorkers:s.initializedWorkers.union(Set(r.worker)),candidateInitialized:s.candidateInitialized or r.worker==s.candidateWorker,lastAction:"candidateInitialized"}
      }
    } else if(e.name=="ordinary")submit(s,r)
    else if(e.name=="ordered")submit(s,r)
    else if(e.name=="snapshot")submit(s,r)
    else if(e.name=="shutdown")submit(s,r)
    else if(e.name=="complete" or e.name=="rejectedTermination" or e.name=="cleanupFault"){
      if(s.current.id!=r.id or s.current.worker!=r.worker)s else {
        val historical=s.frozen or r.worker!=s.activeWorker or r.generation!=s.activeGeneration
        val kind=if(e.name=="cleanupFault")"settleFaulted" else if(e.name=="rejectedTermination")"settleRejected" else if(historical)"settleHistorical" else if(r.operation=="shutdown")"settleShutdown" else "settleCurrent"
        val prefix=List(effect("cancelTimer",r),effect(kind,r))
        val actionName=if(e.name=="cleanupFault")"cleanupFault" else "complete"
        val base={...s,current:emptyRequest,deliveries:s.deliveries.append(r.id),effects:prefix,lastAction:actionName}
        if(e.name!="complete" or r.operation=="shutdown" or (historical and s.retiringWorkers.contains(r.worker))){
          val after=invalidate(base,r.worker,"settleDiscarded",actionName)
          {...after,effects:prefix.concat(after.effects)}
        }else dispatchNext(base,prefix)
      }
    } else if(e.name=="phase"){
      if(not(requestPresent(s,r)))s else {
        val updated={...r,phase:e.phase,deadline:if(s.profile==Bar)s.clock+250 else r.deadline}
        {...s,controls:s.controls.foldl(List(),(out,p)=>out.append(if(p.id==r.id)updated else p)),current:if(s.current.id==r.id)updated else s.current,effects:List(effect("cancelTimer",r),effect("armTimer",updated)),lastAction:"phase"}
      }
    } else if(e.name=="candidateValidated")
      if(s.candidateWorker==0)s else {...s,candidateReady:true,lastAction:"candidateValidated"}
    else if(e.name=="commitCandidate"){
      if(s.candidateWorker==0)s else {
        val rs=queued(s)
        {...s,activeWorker:s.candidateWorker,activeGeneration:s.candidateGeneration,candidateWorker:0,candidateGeneration:0,candidateTransaction:0,candidateInitialized:false,candidateReady:false,retiringWorkers:s.retiringWorkers.union(if(s.activeWorker==0)Set() else Set(s.activeWorker)),ordinary:List(),ordered:List(),snapshot:emptyRequest,deliveries:s.deliveries.concat(idsFor(rs)),effects:effectsFor("settleInvalidated",rs).concat(if(s.activeWorker!=0 and s.current.worker==0)List(termination(s.activeWorker))else List()),lastAction:"commitCandidate"}
      }
    } else if(e.name=="abortCandidate" or e.name=="candidateFailed")
      if(s.candidateWorker!=e.worker)s else invalidate(s,e.worker,if(e.name=="abortCandidate")"settleInvalidated" else "settleDiscarded",if(e.name=="abortCandidate")"abortCandidate" else "workerFailed")
    else if(e.name=="terminateRetiring" or e.name=="workerTerminated")
      if(s.controls.foldl(false,(found,pendingControl)=>found or pendingControl.worker==e.worker) or s.current.worker==e.worker or s.activeWorker==e.worker or s.candidateWorker==e.worker)invalidate(s,e.worker,"settleFaulted","workerFailed")
      else {...s,retiringWorkers:s.retiringWorkers.exclude(Set(e.worker)),compiledWorkers:s.compiledWorkers.exclude(Set(e.worker)),initializedWorkers:s.initializedWorkers.exclude(Set(e.worker)),lastAction:"terminateRetiring"}
    else if(e.name=="freeze"){...s,frozen:true,freezeToken:7,lastAction:"freeze"}
    else if(e.name=="resume")dispatchNext({...s,frozen:false,freezeToken:0,lastAction:"resume"},List())
    else if(e.name=="dispose"){
      val rs=pending(s)
      val workers=(if(s.activeWorker==0)List() else List(s.activeWorker)).concat(if(s.candidateWorker==0)List() else List(s.candidateWorker)).concat(s.retiringWorkers.fold(List(),(rs,w)=>rs.append(w)))
      if(s.disposed){...s,lastAction:"disposeAgain"}else {...s,activeWorker:0,activeGeneration:0,candidateWorker:0,candidateGeneration:0,candidateTransaction:0,candidateInitialized:false,candidateReady:false,retiringWorkers:Set(),compiledWorkers:Set(),initializedWorkers:Set(),controls:List(),current:emptyRequest,ordinary:List(),ordered:List(),snapshot:emptyRequest,disposed:true,deliveries:s.deliveries.concat(idsFor(rs)),effects:workers.foldl(List(),(out,w)=>out.append(termination(w))).concat(effectsFor("cancelTimer",rs)).concat(rs.foldl(List(),(out,r)=>out.append({...effect("settleDiscarded",r),reason:"HostDisposed"}))),lastAction:"dispose"}
    } else if(e.name=="timer"){...s,lastAction:if(s.effects.length()==0)"stutter" else s.lastAction}
    else s
  }
  pure def eventAt(s: State,e: Input,observed: int): State = {
    val time=if(observed<s.clock)s.clock else observed
    val captured={...s,clock:time}
    val r=if(e.name=="loadInitial" or e.name=="prepareCandidate")requestFor(captured,s.nextGeneration,s.nextGeneration,"load","compile",Ordinary)
      else if(e.name=="initialize")requestFor(captured,e.worker,if(e.worker==s.activeWorker)s.activeGeneration else s.candidateGeneration,"initialize","allocate-descriptor",Ordinary)
      else if(e.name=="ordinary" or e.name=="ordered" or e.name=="snapshot" or e.name=="shutdown")requestFor(captured,s.activeWorker,s.activeGeneration,if(e.name=="shutdown")"shutdown" else "process",if(e.name=="shutdown")"shutdown" else "allocate-descriptor",if(e.name=="ordered")Ordered else if(e.name=="snapshot")Snapshot else Ordinary)
      else e.request
    val original={...e,request:r,observed:observed}
    val expired=expireAtState(s,time)
    val result=handle({...expired,effects:List()},original)
    {...result,effects:expired.effects.concat(result.effects),input:original,lastAction:if(e.name=="timer" and expired.effects.length()>0)expired.lastAction else result.lastAction}
  }
  // Observational mechanical composition, using the SAME canonical eventAt.
  // Connected Host synchronously reports termination before the next ordered effect.
  // Production invalidateWorker removes every role and pending item for this worker.
  // Its emitted second TerminateWorker therefore observes no live/pending work.
  // Preserve adverse further effects, and refuse rather than truncate this bound.
  pure def connectedFinalEffect(out: Connection,fx: Effect,observed: int): Connection = {
    val ordered={...out,effects:out.effects.append(fx),beforeEffects:out.beforeEffects.append(out.host)}
    if(fx.kind!="terminate")ordered else {
      val following=eventAt(out.host,input("workerTerminated",emptyRequest,fx.worker,""),observed)
      {host:following,effects:ordered.effects.concat(following.effects),callbacks:out.callbacks.append(following.input),beforeEffects:ordered.beforeEffects.concat(following.effects.foldl(List(),(snapshots,nestedEffect)=>snapshots.append(following))),callbacksQuiescent:out.callbacksQuiescent and following.effects.length()==0}
    }
  }
  pure def connectedEventAt(c: Connection,e: Input,observed: int): Connection = {
    val base=eventAt(c.host,e,observed)
    base.effects.foldl({host:base,effects:List(),callbacks:List(),beforeEffects:List(),callbacksQuiescent:true},(out,fx)=>{
      val ordered={...out,effects:out.effects.append(fx),beforeEffects:out.beforeEffects.append(out.host)}
      if(fx.kind!="terminate")ordered else {
        val following=eventAt(out.host,input("workerTerminated",emptyRequest,fx.worker,""),observed)
        val called={...ordered,host:following,callbacks:out.callbacks.append(following.input)}
        following.effects.foldl(called,(nested,nestedEffect)=>connectedFinalEffect(nested,nestedEffect,observed))
      }
    })
  }
  pure def initialConnection(profile: Profile): Connection = {host:initial(profile),effects:List(),callbacks:List(),beforeEffects:List(),callbacksQuiescent:true}
  action init = {nondet profile=Set(Bar,Sc2).oneOf() all {state'=initial(profile),connection'=initialConnection(profile)}}
  action initBar = all {state'=initial(Bar),connection'=initialConnection(Bar)}
  action initSc2 = all {state'=initial(Sc2),connection'=initialConnection(Sc2)}
  action eventAtTime(e: Input,observed: int): bool = {
    val followingEvent=eventAt(state,e,observed)
    all {state'=followingEvent,connection'=connectedEventAt(connection,followingEvent.input,observed)}
  }
  action event(e: Input): bool = eventAtTime(e,state.clock+1)
  action loadInitial = all {not(state.disposed),not(state.frozen),state.activeWorker==0,event(input("loadInitial",emptyRequest,0,""))}
  action prepareCandidate = all {state.profile==Sc2,not(state.disposed),not(state.frozen),state.candidateWorker==0,live(state).size()<4,event(input("prepareCandidate",emptyRequest,0,""))}
  action compiled = {val rs=state.controls.select(r=>r.operation=="load") nondet index=rs.indices().oneOf() all {rs.length()>0,event(input("compiled",rs.nth(index),rs.nth(index).worker,""))}}
  action initialize = {val available=state.compiledWorkers.exclude(state.initializedWorkers).filter(w=>not(state.controls.foldl(false,(found,r)=>found or r.worker==w))) nondet worker=available.oneOf() all {not(state.disposed),not(state.frozen),available.size()>0,event(input("initialize",emptyRequest,worker,""))}}
  action initialized = {val rs=state.controls.select(r=>r.operation=="initialize") nondet index=rs.indices().oneOf() all {rs.length()>0,event(input("initialized",rs.nth(index),rs.nth(index).worker,""))}}
  action submitOrdinary = event(input("ordinary",emptyRequest,0,""))
  action submitOrdered = event(input("ordered",emptyRequest,0,""))
  action submitSnapshot = event(input("snapshot",emptyRequest,0,""))
  action shutdown = event(input("shutdown",emptyRequest,0,""))
  action complete = all {state.current.worker!=0,event(input("complete",state.current,state.current.worker,""))}
  action rejectedTermination = all {state.current.worker!=0,event(input("rejectedTermination",state.current,state.current.worker,""))}
  action cleanupFault = all {state.current.worker!=0,event(input("cleanupFault",state.current,state.current.worker,""))}
  action phaseObserved = {val rs=state.controls.concat(if(state.current.worker==0)List()else List(state.current)) nondet index=rs.indices().oneOf() val r=rs.nth(index) val alternatives=if(r.operation=="load" and r.phase=="compile")Set("instantiate")else if(r.phase=="allocate-descriptor")Set("allocate-input")else if(r.phase=="allocate-input")Set(r.operation)else if(r.phase=="initialize" or r.phase=="process")Set("free")else Set() nondet phase=alternatives.oneOf() all {rs.length()>0,alternatives.size()>0,event(input("phase",r,r.worker,phase))}}
  action expireDeadlines = event(input("timer",emptyRequest,0,""))
  action advanceAndExpireDeadlines = all {state.current.worker!=0,eventAtTime(input("timer",emptyRequest,0,""),state.current.deadline+1)}
  action candidateValidated = all {state.candidateWorker!=0,state.candidateInitialized,event(input("candidateValidated",emptyRequest,state.candidateWorker,""))}
  action commitCandidate = all {state.candidateWorker!=0,state.candidateReady,not(state.frozen),event(input("commitCandidate",emptyRequest,state.candidateWorker,""))}
  action abortCandidate = all {state.candidateWorker!=0,event(input("abortCandidate",emptyRequest,state.candidateWorker,""))}
  action candidateFailed = {val rs=state.controls.select(r=>r.worker==state.candidateWorker) all {state.candidateWorker!=0,rs.length()>0,event(input("candidateFailed",rs.head(),state.candidateWorker,""))}}
  action terminateRetiring = {nondet worker=state.retiringWorkers.oneOf() all {state.retiringWorkers.size()>0,event(input("terminateRetiring",state.current,worker,""))}}
  action freeze = all {not(state.disposed),not(state.frozen),event(input("freeze",emptyRequest,0,""))}
  action resume = all {state.frozen,event(input("resume",emptyRequest,0,""))}
  action dispose = event(input("dispose",emptyRequest,0,""))
  action stutter = event(input("timer",emptyRequest,0,""))
  action step = any {loadInitial,prepareCandidate,compiled,initialize,initialized,submitOrdinary,submitOrdered,submitSnapshot,shutdown,complete,rejectedTermination,cleanupFault,phaseObserved,expireDeadlines,advanceAndExpireDeadlines,candidateValidated,commitCandidate,abortCandidate,candidateFailed,terminateRetiring,freeze,resume,dispose,stutter}
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
  val lifecycleSafe = and {connection.callbacksQuiescent, singleTerminalDelivery,staleCannotBeCurrent,candidateHasNoAuthority,workerCapacityIncludesRetiring,queuesBounded,orderedRequestsPreserveOrder,freezeSuppressesCurrentDelivery,cleanupFailureNeverSucceeds,disposedHasNoLiveWork,readinessRequiresCompilation,currentRequiresReadiness,timeoutTerminates }
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

// Opt-in SC2 Supervisor V1 refinement. The legacy module and its retained traces
// remain unchanged. Selected limits are scoped to requests, never worker ceilings.
module compatibleLifecycle {
  import lifecycle.*
  type Limits = {deadline: int, output: int, enclosing: int}
  type CompatibleState = {host: State, limits: int -> Limits, held: Request, selected: Limits, admitted: List[int]}
  type CompatibleConnection = {host: CompatibleState, effects: List[Effect], callbacks: List[Input], beforeEffects: List[CompatibleState], callbacksQuiescent: bool}
  pure val defaultLimits: Limits = {deadline:250,output:65536,enclosing:0}
  pure def initialCompatible: CompatibleState = {host:initial(Sc2),limits:Map(0->defaultLimits),held:emptyRequest,selected:defaultLimits,admitted:List()}
  var compatibleState: CompatibleState
  var compatibleConnection: CompatibleConnection
  action initCompatible = all {initSc2,compatibleState'=initialCompatible,compatibleConnection'={host:initialCompatible,effects:List(),callbacks:List(),beforeEffects:List(),callbacksQuiescent:true}}
  pure def selectedFor(c: CompatibleState,r: Request): Limits = if(c.limits.keys().contains(r.id))c.limits.get(r.id)else defaultLimits
  pure def dueFor(time: int,limits: Limits): int = if(limits.enclosing==0)time+limits.deadline else if(time+limits.deadline<limits.enclosing)time+limits.deadline else limits.enclosing
  pure def projectedHost(c: CompatibleState): State = {...c.host,snapshot:c.held}
  pure def expireCompatible(s: State,time: int): State = {
    val base={...s,clock:time,effects:List()}
    val expiredControls=base.controls.select(r=>r.deadline<time)
    val afterControls=expiredControls.foldl(base,(current,r)=>{
      val following=invalidate(current,r.worker,"settleTimedOut","expireControl")
      {...following,effects:current.effects.concat(following.effects)}
    })
    if(afterControls.current.worker!=0 and afterControls.current.deadline<time){
      val following=invalidate(afterControls,afterControls.current.worker,"settleTimedOut","expireCurrent")
      {...following,effects:afterControls.effects.concat(following.effects)}
    }else afterControls
  }
  pure def disposeCompatible(c: CompatibleState): CompatibleState = {
    val following=handle(projectedHost(c),input("dispose",emptyRequest,0,""))
    {...c,host:{...following,snapshot:emptyRequest},held:emptyRequest}
  }
  pure def expireHeadCompatible(c: CompatibleState,r: Request): CompatibleState = {
    val terminal={...effect("settleTimedOut",r),outcome:"TimedOut",terminalPhase:r.phase,dispatched:false}
    val timed={...c,host:{...c.host,deliveries:c.host.deliveries.append(r.id)}}
    val destroyed=disposeCompatible(timed)
    val mechanics=destroyed.host.effects.select(fx=>fx.kind!="settleDiscarded")
    val settlements=destroyed.host.effects.select(fx=>fx.kind=="settleDiscarded")
    {...destroyed,host:{...destroyed.host,effects:mechanics.concat(List(terminal)).concat(settlements)}}
  }
  pure def pumpCompatible(c: CompatibleState): CompatibleState = {
    val h=c.host
    val pumped=if(h.current.worker!=0 or h.frozen or h.disposed)c
      else if(h.ordinary.length()==0)c
      else {
        val r=h.ordinary.head()
        val cleared={...c,host:{...h,ordinary:h.ordinary.tail()}}
        if(h.clock>=r.deadline){
          expireHeadCompatible(cleared,r)
        }else {...cleared,host:{...cleared.host,current:r,effects:List(effect("post",r),effect("armTimer",r))}}
      }
    val hasSnapshot=(pumped.host.current.worker!=0 and pumped.host.current.submission==Snapshot) or pumped.host.ordinary.foldl(false,(found,r)=>found or r.submission==Snapshot)
    if(pumped.held.worker==0 or pumped.host.disposed or pumped.host.frozen or hasSnapshot)pumped
    else {
      val r={...pumped.held,deadline:dueFor(pumped.host.clock,selectedFor(pumped,pumped.held))}
      val cleared={...pumped,held:emptyRequest,admitted:pumped.admitted.append(r.id)}
      if(pumped.host.ordinary.length()>=4){...cleared,host:refuse(cleared.host,r,"refusedQueueFull")}
      else if(pumped.host.current.worker==0 and pumped.host.ordinary.length()==0){
        if(pumped.host.clock>=r.deadline){
          val expired=expireHeadCompatible(cleared,r)
          {...expired,host:{...expired.host,effects:pumped.host.effects.concat(expired.host.effects)}}
        }else {...cleared,host:{...cleared.host,current:r,effects:pumped.host.effects.concat(List(effect("post",r),effect("armTimer",r)))}}
      }else {...cleared,host:{...cleared.host,ordinary:cleared.host.ordinary.append(r),lastAction:"queueSnapshot"}}
    }
  }
  pure def submitCompatible(c: CompatibleState,r: Request): CompatibleState = {
    val h=c.host
    val population=(if(h.current.worker==0)List()else List(h.current)).concat(h.ordinary)
    val existingSnapshot=population.foldl(false,(found,p)=>found or p.submission==Snapshot)
    if(h.disposed){...c,host:refuse(h,r,"refusedDisposed")}
    else if(h.frozen){...c,host:refuse(h,r,"refusedFrozen")}
    else if(h.activeWorker==0 or not(h.initializedWorkers.contains(h.activeWorker))){...c,host:refuse(h,r,"refused")}
    else if(r.submission==Snapshot and existingSnapshot){
      val held={...r,deadline:0}
      {...c,held:held,host:{...h,nextRequest:h.nextRequest+1,deliveries:if(c.held.worker==0)h.deliveries else h.deliveries.append(c.held.id),effects:if(c.held.worker==0)List()else List(effect("settleCoalesced",c.held)),lastAction:"holdSnapshot"}}
    }else pumpCompatible({...c,admitted:c.admitted.append(r.id),host:{...h,nextRequest:h.nextRequest+1,ordinary:h.ordinary.append(r),effects:List(),lastAction:"queueMixed"}})
  }
  pure def handleCompatible(c: CompatibleState,e: Input): CompatibleState = {
    val h=c.host
    val r=e.request
    if(e.name=="ordinary" or e.name=="ordered" or e.name=="snapshot" or e.name=="shutdown")submitCompatible(c,r)
    else if(e.name=="complete" and h.current.id==r.id and h.current.worker==r.worker){
      val historical=h.frozen or r.worker!=h.activeWorker or r.generation!=h.activeGeneration
      val kind=if(historical)"settleHistorical"else if(r.operation=="shutdown")"settleShutdown"else "settleCurrent"
      val prefix=List(effect("cancelTimer",r),effect(kind,r))
      val base={...c,host:{...h,current:emptyRequest,deliveries:h.deliveries.append(r.id),effects:List(),lastAction:"complete"}}
      if(r.operation=="shutdown" or (historical and h.retiringWorkers.contains(r.worker))){
        val removed=invalidate(base.host,r.worker,"settleDiscarded","complete")
        {...base,host:{...removed,effects:prefix.concat(removed.effects)}}
      }else {
        val pumped=pumpCompatible(base)
        {...pumped,host:{...pumped.host,effects:prefix.concat(pumped.host.effects)}}
      }
    }else if(e.name=="freeze" and not(h.disposed) and not(h.frozen)){
      val pending=h.ordinary.concat(if(c.held.worker==0)List()else List(c.held))
      {...c,held:emptyRequest,host:{...h,frozen:true,freezeToken:7,ordinary:List(),effects:effectsFor("settleInvalidated",pending),deliveries:h.deliveries.concat(idsFor(pending)),lastAction:"freeze"}}
    }else if(e.name=="frozenCommit"){
      if(not(canCommitFrozen(c,7,e.request.id,e.request.generation,e.worker)))c
      else {
        val promoted=handle({...h,frozen:false},input("commitCandidate",emptyRequest,h.candidateWorker,""))
        {...c,host:{...promoted,frozen:true,freezeToken:h.freezeToken}}
      }
    }else if(e.name=="resume")pumpCompatible({...c,host:{...h,frozen:false,freezeToken:0,lastAction:"resume"}})
    else if(e.name=="dispose")disposeCompatible(c)
    else {
      val following=handle(h,e)
      val retained=c.held.worker==0 or live(following).contains(c.held.worker)
      if(retained){...c,host:following}
      else {...c,held:emptyRequest,host:{...following,deliveries:following.deliveries.append(c.held.id),effects:following.effects.append(effect("settleDiscarded",c.held))}}
    }
  }
  pure def compatibleEventAt(c: CompatibleState,e: Input,observed: int,limits: Limits,bytes: int): CompatibleState = {
    val time=if(observed<c.host.clock)c.host.clock else observed
    val captured={...c.host,clock:time}
    val r=if(e.name=="loadInitial" or e.name=="prepareCandidate")requestFor(captured,c.host.nextGeneration,c.host.nextGeneration,"load","compile",Ordinary)
      else if(e.name=="initialize")requestFor(captured,e.worker,if(e.worker==c.host.activeWorker)c.host.activeGeneration else c.host.candidateGeneration,"initialize","allocate-descriptor",Ordinary)
      else if(e.name=="ordinary" or e.name=="ordered" or e.name=="snapshot" or e.name=="shutdown")requestFor(captured,c.host.activeWorker,c.host.activeGeneration,if(e.name=="shutdown")"shutdown"else "process",if(e.name=="shutdown")"shutdown"else "allocate-descriptor",if(e.name=="ordered")Ordered else if(e.name=="snapshot")Snapshot else Ordinary)
      else e.request
    val admission=Set("loadInitial","prepareCandidate","initialize","ordinary","ordered","snapshot","shutdown").contains(e.name)
    val selected=if(admission){...r,deadline:dueFor(time,limits),bytes:bytes}else r
    val original={...e,request:selected,observed:observed}
    val expired=expireCompatible(c.host,time)
    val following=handleCompatible({...c,host:{...expired,effects:List()},limits:if(admission)c.limits.put(selected.id,limits)else c.limits,selected:limits},original)
    {...following,host:{...following.host,input:original,effects:expired.effects.concat(following.host.effects),lastAction:if(e.name=="timer" and expired.effects.length()>0)expired.lastAction else following.host.lastAction}}
  }
  pure def canAdmit(c: CompatibleState,name: str,limits: Limits,bytes: int): bool = {
    val h=c.host
    val population=(if(h.current.worker==0)List()else List(h.current)).concat(h.ordinary)
    val hasSnapshot=population.foldl(false,(found,r)=>found or r.submission==Snapshot)
    and {
      not(h.disposed),not(h.frozen),limits.deadline>=1,limits.deadline<=250,limits.output>=1,limits.output<=65536,
      limits.enclosing==0 or limits.enclosing>h.clock,
      if(Set("ordinary","ordered","snapshot","shutdown").contains(name))h.activeWorker!=0 and h.initializedWorkers.contains(h.activeWorker)else true,
      bytes>=0,bytes<=262144,
      if(name=="ordered")population.length()<256 and population.foldl(0,(total,r)=>total+r.bytes)+bytes<=4194304
      else if(name=="ordinary" or name=="shutdown" or (name=="snapshot" and not(hasSnapshot)))h.ordinary.length()<4
      else true,
    }
  }
  pure def compatibleFinalEffect(out: CompatibleConnection,fx: Effect,observed: int): CompatibleConnection = {
    val ordered={...out,effects:out.effects.append(fx),beforeEffects:out.beforeEffects.append(out.host)}
    if(fx.kind!="terminate")ordered else {
      val following=compatibleEventAt(out.host,input("workerTerminated",emptyRequest,fx.worker,""),observed,defaultLimits,0)
      {...ordered,host:following,effects:ordered.effects.concat(following.host.effects),callbacks:ordered.callbacks.append(following.host.input),beforeEffects:ordered.beforeEffects.concat(following.host.effects.foldl(List(),(states,unused)=>states.append(following))),callbacksQuiescent:out.callbacksQuiescent and following.host.effects.length()==0}
    }
  }
  pure def connectedCompatibleAt(c: CompatibleConnection,e: Input,observed: int,limits: Limits,bytes: int): CompatibleConnection = {
    val base=compatibleEventAt(c.host,e,observed,limits,bytes)
    base.host.effects.foldl({host:base,effects:List(),callbacks:List(),beforeEffects:List(),callbacksQuiescent:true},(out,fx)=>{
      val ordered={...out,effects:out.effects.append(fx),beforeEffects:out.beforeEffects.append(out.host)}
      if(fx.kind!="terminate")ordered else {
        val following=compatibleEventAt(out.host,input("workerTerminated",emptyRequest,fx.worker,""),observed,defaultLimits,0)
        val called={...ordered,host:following,callbacks:out.callbacks.append(following.host.input)}
        following.host.effects.foldl(called,(nested,nestedEffect)=>compatibleFinalEffect(nested,nestedEffect,observed))
      }
    })
  }
  action compatibleEvent(e: Input,observed: int,limits: Limits,bytes: int): bool = {
    val following=compatibleEventAt(compatibleState,e,observed,limits,bytes)
    all {state'=state,connection'=connection,compatibleState'=following,compatibleConnection'=connectedCompatibleAt(compatibleConnection,following.host.input,observed,limits,bytes)}
  }
  action compatibleLoad = all {canAdmit(compatibleState,"loadInitial",defaultLimits,1),compatibleState.host.activeWorker==0,compatibleEvent(input("loadInitial",emptyRequest,0,""),compatibleState.host.clock,defaultLimits,1)}
  action compatibleCompiled = {
    val requests=compatibleState.host.controls.select(r=>r.operation=="load")
    nondet index=requests.indices().oneOf()
    all {requests.length()>0,compatibleEvent(input("compiled",requests.nth(index),requests.nth(index).worker,""),compatibleState.host.clock,defaultLimits,0)}
  }
  action compatibleInitialize(worker: int,limits: Limits): bool = all {canAdmit(compatibleState,"initialize",limits,1),compatibleState.host.compiledWorkers.contains(worker),not(compatibleState.host.controls.foldl(false,(found,r)=>found or r.worker==worker)),compatibleEvent(input("initialize",emptyRequest,worker,""),compatibleState.host.clock,limits,1)}
  action compatibleInitialized(worker: int): bool = {
    val requests=compatibleState.host.controls.select(r=>r.worker==worker and r.operation=="initialize")
    all {requests.length()>0,compatibleEvent(input("initialized",requests.head(),worker,""),compatibleState.host.clock,defaultLimits,0)}
  }
  action compatibleSubmitAt(name: str,limits: Limits,bytes: int,observed: int): bool = all {canAdmit(compatibleState,name,limits,bytes),limits.enclosing==0 or limits.enclosing>observed,compatibleEvent(input(name,emptyRequest,0,""),observed,limits,bytes)}
  action compatibleSubmit(name: str,limits: Limits,bytes: int): bool = compatibleSubmitAt(name,limits,bytes,compatibleState.host.clock)
  action compatibleComplete(observed: int): bool = all {compatibleState.host.current.worker!=0,compatibleEvent(input("complete",compatibleState.host.current,compatibleState.host.current.worker,""),observed,defaultLimits,0)}
  action compatiblePhase(phase: str,observed: int): bool = all {compatibleState.host.current.worker!=0,compatibleEvent(input("phase",compatibleState.host.current,compatibleState.host.current.worker,phase),observed,defaultLimits,0)}
  action compatiblePrepare = all {canAdmit(compatibleState,"prepareCandidate",defaultLimits,1),compatibleState.host.candidateWorker==0,live(compatibleState.host).size()<4,compatibleEvent(input("prepareCandidate",emptyRequest,0,""),compatibleState.host.clock,defaultLimits,1)}
  action compatibleValidate = all {compatibleState.host.candidateInitialized,compatibleEvent(input("candidateValidated",emptyRequest,compatibleState.host.candidateWorker,""),compatibleState.host.clock,defaultLimits,0)}
  action compatibleFreeze = all {not(compatibleState.host.disposed),not(compatibleState.host.frozen),compatibleEvent(input("freeze",emptyRequest,0,""),compatibleState.host.clock,defaultLimits,0)}
  pure def canCommitFrozen(c: CompatibleState,token: int,transaction: int,expectedActive: int,candidateGeneration: int): bool = and {
    not(c.host.disposed),c.host.frozen,c.host.freezeToken==token,
    c.host.candidateReady,c.host.candidateInitialized,
    c.host.candidateTransaction==transaction,c.host.activeGeneration==expectedActive,c.host.candidateGeneration==candidateGeneration,
  }
  action compatibleFrozenCommitAt(observed: int): bool = all {canCommitFrozen(compatibleState,7,compatibleState.host.candidateTransaction,compatibleState.host.activeGeneration,compatibleState.host.candidateGeneration),compatibleEvent(input("frozenCommit",{...emptyRequest,id:compatibleState.host.candidateTransaction,generation:compatibleState.host.activeGeneration},compatibleState.host.candidateWorker,""),observed,defaultLimits,0)}
  action compatibleFrozenCommit = compatibleFrozenCommitAt(compatibleState.host.clock)
  action compatibleResume = all {compatibleState.host.frozen,compatibleEvent(input("resume",emptyRequest,0,""),compatibleState.host.clock,defaultLimits,0)}
  action compatibleDispose = compatibleEvent(input("dispose",emptyRequest,0,""),compatibleState.host.clock,defaultLimits,0)
  action compatibleTimer(observed: int): bool = compatibleEvent(input("timer",emptyRequest,0,""),observed,defaultLimits,0)
  action readyCompatible = compatibleLoad.then(compatibleCompiled).then(compatibleInitialize(1,{deadline:10,output:44,enclosing:0})).then(compatibleInitialized(1))
  action compatibleProgressPhase = {
    val r=compatibleState.host.current
    val following=if(r.phase=="allocate-descriptor")"allocate-input" else if(r.phase=="allocate-input")r.operation else if(r.phase=="process")"free" else ""
    all {r.worker!=0,following!="",compatiblePhase(following,compatibleState.host.clock+1)}
  }
  action compatibleStep = any {compatibleProgressPhase,compatibleFrozenCommitAt(compatibleState.host.clock+6),compatibleSubmit("ordinary",{deadline:10,output:44,enclosing:compatibleState.host.clock+7},1),compatibleSubmitAt("ordinary",{deadline:100,output:64,enclosing:0},1,compatibleState.host.clock+6),compatibleLoad,compatibleCompiled,compatibleInitialize(compatibleState.host.activeWorker,defaultLimits),compatibleInitialized(compatibleState.host.activeWorker),compatibleSubmit("ordinary",{deadline:5,output:44,enclosing:0},1),compatibleSubmit("ordered",{deadline:10,output:64,enclosing:0},1),compatibleSubmit("snapshot",{deadline:5,output:44,enclosing:0},1),compatibleComplete(compatibleState.host.clock+1),compatiblePrepare,compatibleInitialize(compatibleState.host.candidateWorker,defaultLimits),compatibleInitialized(compatibleState.host.candidateWorker),compatibleValidate,compatibleFreeze,compatibleFrozenCommit,compatibleResume,compatibleDispose,compatibleTimer(compatibleState.host.clock+1)}
  val selectedSingleSettlement = compatibleState.host.deliveries.foldl(Set(),(seen,id)=>seen.union(Set(id))).size()==compatibleState.host.deliveries.length()
  pure def admissionPosition(c: CompatibleState,id: int): int = c.admitted.foldl({next:1,found:0},(p,current)=>{next:p.next+1,found:if(current==id)p.next else p.found}).found
  val mixedFifoOrdered = compatibleState.host.ordinary.foldl({ok:true,last:0},(p,r)=>{
    val position=admissionPosition(compatibleState,r.id)
    {ok:p.ok and position>p.last,last:position}
  }).ok
  val admittedSnapshotUnique = (if(compatibleState.host.current.submission==Snapshot and compatibleState.host.current.worker!=0)1 else 0)+compatibleState.host.ordinary.select(r=>r.submission==Snapshot).length()<=1
  val frozenQueuesEmpty = not(compatibleState.host.frozen) or (compatibleState.host.ordinary.length()==0 and compatibleState.held.worker==0)
  val frozenHasNoCurrentDelivery = not(compatibleState.host.frozen) or not(hasEffect(compatibleState.host.effects,"settleCurrent"))
  val completeDisposal = not(compatibleState.host.disposed) or (live(compatibleState.host).size()==0 and pending(compatibleState.host).length()==0 and compatibleState.held.worker==0)
  val capturedLimitsBounded = compatibleState.limits.keys().forall(id=>{val selected=compatibleState.limits.get(id) selected.deadline>=1 and selected.deadline<=250 and selected.output>=1 and selected.output<=65536})
  val selectedCapacityBounded = compatibleState.host.ordinary.length()+(if(compatibleState.host.current.worker==0)0 else 1)<=256 and compatibleState.held.bytes<=262144
  val sawSelectedLoad = hasEffect(compatibleState.host.effects,"createActive")
  val sawSelectedCompile = compatibleState.host.lastAction=="compiled"
  val sawSelectedInitialize = compatibleState.host.controls.foldl(false,(found,r)=>found or r.operation=="initialize")
  val sawSelectedInitialized = compatibleState.host.initializedWorkers.size()>0
  val sawMixedOrdinary = compatibleState.host.current.worker!=0 and compatibleState.host.current.submission==Ordinary
  val sawMixedOrdered = compatibleState.host.ordinary.foldl(false,(found,r)=>found or r.submission==Ordered) or (compatibleState.host.current.worker!=0 and compatibleState.host.current.submission==Ordered)
  val sawAdmittedSnapshot = compatibleState.host.ordinary.foldl(false,(found,r)=>found or r.submission==Snapshot) or (compatibleState.host.current.worker!=0 and compatibleState.host.current.submission==Snapshot)
  val sawHeldSnapshot = compatibleState.held.worker!=0
  val sawHeldPromotion = compatibleState.host.lastAction=="queueSnapshot"
  val sawSelectedPhase = compatibleState.host.lastAction=="phase" and (compatibleState.host.current.phase=="allocate-input" or compatibleState.host.current.phase=="process" or compatibleState.host.current.phase=="free")
  val sawNarrowDeadline = compatibleState.limits.keys().exists(id=>id!=0 and compatibleState.limits.get(id).deadline<250)
  val sawNarrowOutput = compatibleState.limits.keys().exists(id=>id!=0 and compatibleState.limits.get(id).output<65536)
  val sawEnclosingBudget = compatibleState.limits.keys().exists(id=>id!=0 and compatibleState.limits.get(id).enclosing>0)
  val sawCandidatePrepared = compatibleState.host.candidateWorker!=0
  val sawCandidateInitialized = compatibleState.host.candidateInitialized
  val sawCandidateValidated = compatibleState.host.candidateReady
  val sawSelectedFreeze = compatibleState.host.frozen
  val sawAtomicFrozenCommit = compatibleState.host.frozen and compatibleState.host.freezeToken==7 and compatibleState.host.lastAction=="commitCandidate"
  val sawSelectedHistorical = hasEffect(compatibleState.host.effects,"settleHistorical")
  val sawSelectedResume = compatibleState.host.lastAction=="resume"
  val sawSelectedDisposal = compatibleState.host.disposed
  val sawRefusalAfterExpiry = hasEffect(compatibleState.host.effects,"refused") and hasEffect(compatibleState.host.effects,"settleTimedOut")
  val sawInclusiveHeadExpiry = compatibleState.host.disposed and compatibleState.host.effects.foldl(false,(found,fx)=>found or (fx.kind=="settleTimedOut" and fx.outcome=="TimedOut" and not(fx.dispatched)))
  val sawFrozenCommitExpiryRefused = compatibleState.host.input.name=="frozenCommit" and compatibleState.host.frozen and compatibleState.host.candidateReady and compatibleState.host.activeWorker==0 and hasEffect(compatibleState.host.effects,"settleTimedOut")
  val compatibilitySafe = and {compatibleConnection.callbacksQuiescent,selectedSingleSettlement,mixedFifoOrdered,admittedSnapshotUnique,frozenQueuesEmpty,frozenHasNoCurrentDelivery,completeDisposal,capturedLimitsBounded,selectedCapacityBounded}
}
