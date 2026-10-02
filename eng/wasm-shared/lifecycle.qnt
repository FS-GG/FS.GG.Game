module lifecycle {
  type Profile = Bar | Sc2
  type Submission = Ordinary | Ordered | Snapshot

  type Request = {
    id: int,
    generation: int,
    deadline: int,
    submission: Submission,
    bytes: int,
  }

  type Effect = {
    kind: str,
    request: int,
    generation: int,
    worker: int,
  }

  type State = {
    profile: Profile,
    clock: int,
    nextRequest: int,
    nextGeneration: int,
    activeWorker: int,
    activeGeneration: int,
    candidateWorker: int,
    candidateGeneration: int,
    candidateTransaction: int,
    candidateInitialized: bool,
    candidateReady: bool,
    retiringWorkers: Set[int],
    current: Request,
    ordinary: List[Request],
    ordered: List[Request],
    snapshot: Request,
    frozen: bool,
    freezeToken: int,
    disposed: bool,
    deliveries: List[int],
    effects: List[Effect],
    lastAction: str,
  }

  var state: State

  pure val emptyRequest: Request = {
    id: 0,
    generation: 0,
    deadline: 0,
    submission: Ordinary,
    bytes: 0,
  }

  pure def effect(kind: str, request: Request, worker: int): Effect = {
    kind: kind,
    request: request.id,
    generation: request.generation,
    worker: worker,
  }

  pure def initial(profile: Profile): State = {
    profile: profile,
    clock: 0,
    nextRequest: 1,
    nextGeneration: 1,
    activeWorker: 0,
    activeGeneration: 0,
    candidateWorker: 0,
    candidateGeneration: 0,
    candidateTransaction: 0,
    candidateInitialized: false,
    candidateReady: false,
    retiringWorkers: Set(),
    current: emptyRequest,
    ordinary: List(),
    ordered: List(),
    snapshot: emptyRequest,
    frozen: false,
    freezeToken: 0,
    disposed: false,
    deliveries: List(),
    effects: List(),
    lastAction: "init",
  }

  action init = {
    nondet profile = Set(Bar, Sc2).oneOf()
    state' = initial(profile)
  }

  action initBar = state' = initial(Bar)
  action initSc2 = state' = initial(Sc2)

  action loadInitial = all {
    not(state.disposed),
    state.activeWorker == 0,
    state' = {
      ...state,
      clock: state.clock + 1,
      activeWorker: state.nextGeneration,
      activeGeneration: state.nextGeneration,
      nextGeneration: state.nextGeneration + 1,
      effects: List({ kind: "createActive", request: 0, generation: state.nextGeneration, worker: state.nextGeneration }),
      lastAction: "loadInitial",
    },
  }

  pure def requestFor(s: State, submission: Submission): Request = {
    id: s.nextRequest,
    generation: s.activeGeneration,
    deadline: s.clock + 251,
    submission: submission,
    bytes: 1,
  }

  pure def refuse(s: State, request: Request, reason: str): State = {
    ...s,
    clock: s.clock + 1,
    nextRequest: s.nextRequest + 1,
    deliveries: s.deliveries.append(request.id),
    effects: List(effect(reason, request, s.activeWorker)),
    lastAction: "refuse",
  }

  pure def begin(s: State, request: Request): State = {
    ...s,
    clock: s.clock + 1,
    nextRequest: s.nextRequest + 1,
    current: request,
    effects: List(effect("post", request, s.activeWorker), effect("armTimer", request, s.activeWorker)),
    lastAction: "begin",
  }

  pure def submitOrdinaryState(s: State): State = {
    val request = requestFor(s, Ordinary)
    if (s.disposed) refuse(s, request, "refusedDisposed")
    else if (s.frozen) refuse(s, request, "refusedFrozen")
    else if (s.current.id == 0) begin(s, request)
    else if (s.profile == Bar) refuse(s, request, "refusedBusy")
    else if (s.ordinary.length() >= 4) refuse(s, request, "refusedQueueFull")
    else {
      ...s,
      clock: s.clock + 1,
      nextRequest: s.nextRequest + 1,
      ordinary: s.ordinary.append(request),
      effects: List(),
      lastAction: "queueOrdinary",
    }
  }

  action submitOrdinary = state' = submitOrdinaryState(state)

  pure def realtimeBytes(s: State): int =
    s.ordered.foldl(0, (total, request) => total + request.bytes)
      + s.snapshot.bytes
      + (if (s.current.submission == Ordinary) 0 else s.current.bytes)

  pure def submitOrderedState(s: State): State = {
    val request = requestFor(s, Ordered)
    if (s.profile == Bar) refuse(s, request, "refusedProfile")
    else if (s.disposed) refuse(s, request, "refusedDisposed")
    else if (s.frozen) refuse(s, request, "refusedFrozen")
    else if (realtimeBytes(s) + request.bytes > 4194304 or s.ordered.length() + (if (s.current.submission == Ordered) 1 else 0) >= 256)
      refuse(s, request, "refusedQueueFull")
    else if (s.current.id == 0) begin(s, request)
    else {
      ...s,
      clock: s.clock + 1,
      nextRequest: s.nextRequest + 1,
      ordered: s.ordered.append(request),
      effects: List(),
      lastAction: "queueOrdered",
    }
  }

  action submitOrdered = state' = submitOrderedState(state)

  pure def submitSnapshotState(s: State): State = {
    val request = requestFor(s, Snapshot)
    if (s.profile == Bar) refuse(s, request, "refusedProfile")
    else if (s.disposed) refuse(s, request, "refusedDisposed")
    else if (s.frozen) refuse(s, request, "refusedFrozen")
    else if (realtimeBytes(s) + request.bytes - s.snapshot.bytes > 4194304)
      refuse(s, request, "refusedQueueFull")
    else if (s.current.id == 0) begin(s, request)
    else if (s.snapshot.id == 0) {
      ...s,
      clock: s.clock + 1,
      nextRequest: s.nextRequest + 1,
      snapshot: request,
      effects: List(),
      lastAction: "queueSnapshot",
    }
    else {
      ...s,
      clock: s.clock + 1,
      nextRequest: s.nextRequest + 1,
      snapshot: request,
      deliveries: s.deliveries.append(s.snapshot.id),
      effects: List(effect("settleCoalesced", s.snapshot, s.activeWorker)),
      lastAction: "coalesceSnapshot",
    }
  }

  action submitSnapshot = state' = submitSnapshotState(state)

  pure def dispatchNext(s: State, prefix: List[Effect]): State =
    if (s.ordinary.length() > 0) {
      ...s,
      current: s.ordinary.head(),
      ordinary: s.ordinary.tail(),
      effects: prefix.append(effect("post", s.ordinary.head(), s.activeWorker)).append(effect("armTimer", s.ordinary.head(), s.activeWorker)),
    }
    else if (s.ordered.length() > 0) {
      ...s,
      current: s.ordered.head(),
      ordered: s.ordered.tail(),
      effects: prefix.append(effect("post", s.ordered.head(), s.activeWorker)).append(effect("armTimer", s.ordered.head(), s.activeWorker)),
    }
    else if (s.snapshot.id != 0) {
      ...s,
      current: s.snapshot,
      snapshot: emptyRequest,
      effects: prefix.append(effect("post", s.snapshot, s.activeWorker)).append(effect("armTimer", s.snapshot, s.activeWorker)),
    }
    else { ...s, current: emptyRequest, effects: prefix }

  pure def completeState(s: State, cleanupSucceeded: bool): State = {
    val disposition =
      if (not(cleanupSucceeded)) "settleFault"
      else if (s.frozen or s.current.generation != s.activeGeneration) "settleHistorical"
      else "settleCurrent"
    val completed = {
      ...s,
      clock: s.clock + 1,
      current: emptyRequest,
      deliveries: s.deliveries.append(s.current.id),
      lastAction: if (cleanupSucceeded) "complete" else "cleanupFault",
    }
    val completionEffects =
      if (disposition == "settleHistorical")
        List(effect(disposition, s.current, s.current.generation), { kind: "terminate", request: 0, generation: 0, worker: s.current.generation })
      else List(effect(disposition, s.current, s.activeWorker))
    dispatchNext(completed, completionEffects)
  }

  action complete = all { state.current.id != 0, state' = completeState(state, true) }
  action cleanupFault = all { state.current.id != 0, state' = completeState(state, false) }

  action duplicateResult = all {
    state.deliveries.length() > 0,
    state' = { ...state, clock: state.clock + 1, effects: List(), lastAction: "duplicate" },
  }

  pure def expiredAt(requests: List[Request], now: int): List[Request] =
    requests.foldl(List(), (result, request) => if (request.deadline < now) result.append(request) else result)

  pure def retainedAt(requests: List[Request], now: int): List[Request] =
    requests.foldl(List(), (result, request) => if (request.deadline >= now) result.append(request) else result)

  pure def expireAtState(s: State, now: int): State = {
    val expiredOrdinary = expiredAt(s.ordinary, now)
    val expiredOrdered = expiredAt(s.ordered, now)
    val expiredSnapshot = if (s.snapshot.id != 0 and s.snapshot.deadline < now) List(s.snapshot) else List()
    val expiredQueued = expiredOrdinary.concat(expiredOrdered).concat(expiredSnapshot)
    val currentExpired = s.current.id != 0 and s.current.deadline < now
    val base = {
      ...s,
      clock: now,
      ordinary: retainedAt(s.ordinary, now),
      ordered: retainedAt(s.ordered, now),
      snapshot: if (expiredSnapshot.length() == 0) s.snapshot else emptyRequest,
      current: if (currentExpired) emptyRequest else s.current,
      deliveries: s.deliveries.concat(idsFor(expiredQueued)).concat(if (currentExpired) List(s.current.id) else List()),
      effects: effectsFor("settleTimedOut", expiredQueued, s.activeWorker)
        .concat(if (currentExpired) List(effect("settleTimedOut", s.current, s.activeWorker)) else List()),
      lastAction: if (currentExpired) "expireCurrent" else if (expiredQueued.length() > 0) "expireOrdinary" else "stutter",
    }
    if (currentExpired) dispatchNext(base, base.effects) else base
  }

  action expireDeadlines = all {
    state.current.id != 0 or state.ordinary.length() > 0 or state.ordered.length() > 0 or state.snapshot.id != 0,
    state' = expireAtState(state, state.clock + 1),
  }

  action advanceAndExpireDeadlines = all {
    state.ordinary.length() > 0,
    state' = expireAtState(state, state.ordinary.head().deadline + 1),
  }

  pure def prepareCandidateState(s: State): State = {
    ...s,
    clock: s.clock + 1,
    candidateWorker: s.nextGeneration,
    candidateGeneration: s.nextGeneration,
    candidateTransaction: s.nextGeneration,
    candidateInitialized: false,
    candidateReady: false,
    nextGeneration: s.nextGeneration + 1,
    effects: List({ kind: "createCandidate", request: 0, generation: s.nextGeneration, worker: s.nextGeneration }),
    lastAction: "prepareCandidate",
  }

  action prepareCandidate = all {
    state.profile == Sc2,
    not(state.disposed),
    state.candidateWorker == 0,
    state.retiringWorkers.size() + (if (state.activeWorker == 0) 0 else 1) < 4,
    state' = prepareCandidateState(state),
  }

  action candidateReady = all {
    state.candidateWorker != 0,
    state' = { ...state, clock: state.clock + 1, candidateInitialized: true, effects: List(), lastAction: "candidateInitialized" },
  }

  action candidateValidated = all {
    state.candidateWorker != 0,
    state.candidateInitialized,
    state' = { ...state, clock: state.clock + 1, candidateReady: true, effects: List(), lastAction: "candidateValidated" },
  }

  action candidateFailed = all {
    state.candidateWorker != 0,
    state' = {
      ...state,
      clock: state.clock + 1,
      candidateWorker: 0,
      candidateGeneration: 0,
      candidateTransaction: 0,
      candidateInitialized: false,
      candidateReady: false,
      effects: List({ kind: "terminate", request: 0, generation: 0, worker: state.candidateWorker }),
      lastAction: "candidateFailed",
    },
  }

  pure def effectsFor(kind: str, requests: List[Request], worker: int): List[Effect] =
    requests.foldl(List(), (effects, request) => effects.append(effect(kind, request, worker)))

  pure def idsFor(requests: List[Request]): List[int] =
    requests.foldl(List(), (ids, request) => ids.append(request.id))

  pure def invalidationEffects(s: State): List[Effect] =
    effectsFor("settleInvalidated", s.ordinary, s.activeWorker)
      .concat(effectsFor("settleInvalidated", s.ordered, s.activeWorker))
      .concat(if (s.snapshot.id == 0) List() else List(effect("settleInvalidated", s.snapshot, s.activeWorker)))

  pure def invalidatedIds(s: State): List[int] =
    idsFor(s.ordinary)
      .concat(idsFor(s.ordered))
      .concat(if (s.snapshot.id == 0) List() else List(s.snapshot.id))

  action commitCandidate = all {
    state.candidateWorker != 0,
    state.candidateReady,
    state' = {
      ...state,
      clock: state.clock + 1,
      activeWorker: state.candidateWorker,
      activeGeneration: state.candidateGeneration,
      candidateWorker: 0,
      candidateGeneration: 0,
      candidateTransaction: 0,
      candidateInitialized: false,
      candidateReady: false,
      retiringWorkers: if (state.activeWorker == 0) state.retiringWorkers else state.retiringWorkers.union(Set(state.activeWorker)),
      ordinary: List(),
      ordered: List(),
      snapshot: emptyRequest,
      deliveries: state.deliveries.concat(invalidatedIds(state)),
      effects: invalidationEffects(state),
      lastAction: "commitCandidate",
    },
  }

  action abortCandidate = all {
    state.candidateWorker != 0,
    state' = {
      ...state,
      clock: state.clock + 1,
      candidateWorker: 0,
      candidateGeneration: 0,
      candidateTransaction: 0,
      candidateInitialized: false,
      candidateReady: false,
      effects: List({ kind: "terminate", request: 0, generation: 0, worker: state.candidateWorker }),
      lastAction: "abortCandidate",
    },
  }

  action terminateRetiring = all {
    state.retiringWorkers.size() > 0,
    nondet worker = state.retiringWorkers.oneOf()
    state' = {
      ...state,
      clock: state.clock + 1,
      retiringWorkers: state.retiringWorkers.exclude(Set(worker)),
      effects: List({ kind: "terminated", request: 0, generation: 0, worker: worker }),
      lastAction: "terminateRetiring",
    }
  }

  action freeze = all {
    not(state.disposed),
    not(state.frozen),
    state' = { ...state, clock: state.clock + 1, frozen: true, freezeToken: 7, effects: List(), lastAction: "freeze" },
  }

  action resume = all {
    state.frozen,
    state.freezeToken == 7,
    state' = { ...state, clock: state.clock + 1, frozen: false, freezeToken: 0, effects: List(), lastAction: "resume" },
  }

  pure def allPending(s: State): List[Request] =
    (if (s.current.id == 0) List() else List(s.current))
      .concat(s.ordinary)
      .concat(s.ordered)
      .concat(if (s.snapshot.id == 0) List() else List(s.snapshot))

  pure def disposeState(s: State): State = {
    val pending = allPending(s)
    val settle = effectsFor("settleDiscarded", pending, s.activeWorker)
    val terminateActive = if (s.activeWorker == 0) List() else List({ kind: "terminate", request: 0, generation: 0, worker: s.activeWorker })
    val terminateCandidate = if (s.candidateWorker == 0) List() else List({ kind: "terminate", request: 0, generation: 0, worker: s.candidateWorker })
    val terminateRetiring = s.retiringWorkers.fold(List(), (effects, worker) => effects.append({ kind: "terminate", request: 0, generation: 0, worker: worker }))
    {
      ...s,
      clock: s.clock + 1,
      activeWorker: 0,
      activeGeneration: 0,
      candidateWorker: 0,
      candidateGeneration: 0,
      candidateTransaction: 0,
      candidateInitialized: false,
      candidateReady: false,
      retiringWorkers: Set(),
      current: emptyRequest,
      ordinary: List(),
      ordered: List(),
      snapshot: emptyRequest,
      disposed: true,
      deliveries: s.deliveries.concat(idsFor(pending)),
      effects: settle.concat(terminateActive).concat(terminateCandidate).concat(terminateRetiring),
      lastAction: "dispose",
    }
  }

  action dispose = state' = if (state.disposed) { ...state, clock: state.clock + 1, effects: List(), lastAction: "disposeAgain" } else disposeState(state)

  action stutter = state' = { ...state, clock: state.clock + 1, effects: List(), lastAction: "stutter" }

  action step = {
    nondet operation = 0.to(17).oneOf()
    if (operation == 0) submitOrdinary
    else if (operation == 1) submitOrdered
    else if (operation == 2) submitSnapshot
    else if (operation == 3 and state.current.id != 0) complete
    else if (operation == 4 and state.current.id != 0) cleanupFault
    else if (operation == 5 and state.deliveries.length() > 0) duplicateResult
    else if (operation == 6 and (state.current.id != 0 or state.ordinary.length() > 0 or state.ordered.length() > 0 or state.snapshot.id != 0)) expireDeadlines
    else if (operation == 7 and state.profile == Sc2 and not(state.disposed) and state.candidateWorker == 0 and state.retiringWorkers.size() + (if (state.activeWorker == 0) 0 else 1) < 4) prepareCandidate
    else if (operation == 8 and state.candidateWorker != 0) candidateReady
    else if (operation == 9 and state.candidateWorker != 0) candidateFailed
    else if (operation == 10 and state.candidateWorker != 0 and state.candidateReady) commitCandidate
    else if (operation == 11 and not(state.disposed) and not(state.frozen)) freeze
    else if (operation == 12 and state.frozen) resume
    else if (operation == 13) dispose
    else if (operation == 14 and not(state.disposed) and state.activeWorker == 0) loadInitial
    else if (operation == 15 and state.candidateWorker != 0 and state.candidateInitialized) candidateValidated
    else if (operation == 16 and state.ordinary.length() > 0) advanceAndExpireDeadlines
    else stutter
  }

  // Deterministic model-generated traces consumed by the production correspondence suite.
  action barCorrespondenceStep =
    if (state.clock == 0) loadInitial
    else if (state.clock == 1) submitOrdinary
    else if (state.clock == 2) submitOrdinary
    else if (state.clock == 3) complete
    else if (state.clock == 4) dispose
    else stutter

  action sc2CorrespondenceStep =
    if (state.clock == 0) loadInitial
    else if (state.clock == 1) submitOrdinary
    else if (state.clock == 2) submitSnapshot
    else if (state.clock == 3) submitSnapshot
    else if (state.clock == 4) prepareCandidate
    else if (state.clock == 5) candidateReady
    else if (state.clock == 6) candidateValidated
    else if (state.clock == 7) commitCandidate
    else if (state.clock == 8) complete
    else if (state.clock == 9) freeze
    else if (state.clock == 10) submitOrdinary
    else if (state.clock == 11) resume
    else if (state.clock == 12) dispose
    else dispose

  action timeoutCorrespondenceStep =
    if (state.clock == 0) loadInitial
    else if (state.clock == 1) submitOrdinary
    else if (state.clock == 2) submitOrdinary
    else if (state.clock == 3) advanceAndExpireDeadlines
    else dispose

  pure def orderedIdsAreIncreasing(requests: List[Request]): bool = {
    val folded = requests.foldl({ ok: true, last: 0 }, (result, request) => {
      ok: result.ok and request.id > result.last,
      last: request.id,
    })
    folded.ok
  }

  val singleTerminalDelivery = state.deliveries.foldl(Set(), (delivered, request) => delivered.union(Set(request))).size() == state.deliveries.length()
  pure def hasEffect(effects: List[Effect], kind: str): bool =
    effects.foldl(false, (found, value) => found or value.kind == kind)

  pure def currentEffectsMatch(effects: List[Effect], generation: int): bool =
    effects.foldl(true, (matches, value) => matches and (value.kind != "settleCurrent" or value.generation == generation))

  val staleCannotBeCurrent = currentEffectsMatch(state.effects, state.activeGeneration)
  val candidateHasNoAuthority = state.candidateWorker == 0 or state.candidateGeneration != state.activeGeneration
  val workerCapacityIncludesRetiring = state.retiringWorkers.size() + (if (state.activeWorker == 0) 0 else 1) + (if (state.candidateWorker == 0) 0 else 1) <= 4
  val queuesBounded = state.ordinary.length() <= 4 and state.ordered.length() + (if (state.current.submission == Ordered) 1 else 0) <= 256 and realtimeBytes(state) <= 4194304
  val orderedRequestsPreserveOrder = orderedIdsAreIncreasing(state.ordered)
  val freezeSuppressesCurrentDelivery = not(state.frozen) or not(hasEffect(state.effects, "settleCurrent"))
  val cleanupFailureNeverSucceeds = state.lastAction != "cleanupFault" or not(hasEffect(state.effects, "settleCurrent"))
  val disposedHasNoLiveWork = not(state.disposed) or and {
    state.activeWorker == 0,
    state.candidateWorker == 0,
    state.retiringWorkers.size() == 0,
    state.current.id == 0,
    state.ordinary.length() == 0,
    state.ordered.length() == 0,
    state.snapshot.id == 0,
  }

  val lifecycleSafe = and {
    singleTerminalDelivery,
    staleCannotBeCurrent,
    candidateHasNoAuthority,
    workerCapacityIncludesRetiring,
    queuesBounded,
    orderedRequestsPreserveOrder,
    freezeSuppressesCurrentDelivery,
    cleanupFailureNeverSucceeds,
    disposedHasNoLiveWork,
  }

  val sawBusyRefusal = hasEffect(state.effects, "refusedBusy")
  val sawSnapshotCoalesced = hasEffect(state.effects, "settleCoalesced")
  val sawHistoricalResult = hasEffect(state.effects, "settleHistorical")
  val sawQueuedTimeout = hasEffect(state.effects, "settleTimedOut")
}

module lifecycleTest {
  import lifecycle.*

  run barRefusesBusyTest =
    initBar.then(loadInitial).then(submitOrdinary).then(submitOrdinary).expect(and {
      state.profile == Bar,
      state.current.id == 1,
      state.deliveries == List(2),
      sawBusyRefusal,
      lifecycleSafe,
    })

  run sc2OrdinaryQueueIsFourTest =
    initSc2.then(loadInitial).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).then(submitOrdinary).expect(and {
      state.ordinary.length() == 4,
      state.deliveries == List(6),
      lifecycleSafe,
    })

  run latestSnapshotCoalescesTest =
    initSc2.then(loadInitial).then(submitOrdinary).then(submitSnapshot).then(submitSnapshot).expect(and {
      state.snapshot.id == 3,
      state.deliveries == List(2),
      sawSnapshotCoalesced,
      lifecycleSafe,
    })

  run candidateFailurePreservesActiveTest =
    initSc2.then(loadInitial).then(prepareCandidate).then(candidateReady).then(candidateValidated).then(candidateFailed).expect(and {
      state.activeWorker == 1,
      state.activeGeneration == 1,
      state.candidateWorker == 0,
      lifecycleSafe,
    })

  run commitMakesOldCompletionHistoricalTest =
    initSc2.then(loadInitial).then(submitOrdinary).then(prepareCandidate).then(candidateReady).then(candidateValidated).then(commitCandidate).then(complete).expect(and {
      state.activeGeneration == 2,
      state.retiringWorkers.contains(1),
      sawHistoricalResult,
      lifecycleSafe,
    })

  run freezeBlocksAndSuppressesTest =
    initSc2.then(loadInitial).then(submitOrdinary).then(freeze).then(complete).then(submitOrdinary).expect(and {
      state.frozen,
      hasEffect(state.effects, "refusedFrozen"),
      lifecycleSafe,
    })

  run cleanupFaultIsNotSuccessTest =
    initSc2.then(loadInitial).then(submitOrdinary).then(cleanupFault).expect(and {
      hasEffect(state.effects, "settleFault"),
      lifecycleSafe,
    })

  run disposalSettlesEverythingOnceTest =
    initSc2.then(loadInitial).then(submitOrdinary).then(submitOrdinary).then(prepareCandidate).then(candidateReady).then(candidateValidated).then(dispose).then(dispose).expect(and {
      state.disposed,
      state.effects.length() == 0,
      lifecycleSafe,
    })
}
