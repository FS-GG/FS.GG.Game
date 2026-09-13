module sessionOperations {
  // One coordinator owns transport-neutral request identities. Local workers and
  // authoritative servers interpret the same effects outside this model.
  type State = {
    generation: int,
    status: int, // 0 active, 1 cancelled, 2 failed, 3 disposed
    dispatchedRequired: int,
    committedRequired: int,
    pendingRequired: int,
    bufferedRequired: int,
    pendingProjection: bool,
    projectionQueued: bool,
    lastProjection: int,
  }

  pure val maxPendingRequired = 2

  pure val initialState = {
    generation: 0,
    status: 0,
    dispatchedRequired: 0,
    committedRequired: 0,
    pendingRequired: 0,
    bufferedRequired: 0,
    pendingProjection: false,
    projectionQueued: false,
    lastProjection: -1,
  }

  var state: State

  action init = state' = initialState

  action enqueueRequired = all {
    state.status == 0,
    state.pendingRequired + state.bufferedRequired < maxPendingRequired,
    state' = {
      ...state,
      dispatchedRequired: state.dispatchedRequired + 1,
      pendingRequired: state.pendingRequired + 1,
    },
  }

  action completeRequired(outOfOrder: bool): bool = all {
    state.status == 0,
    state.pendingRequired > 0,
    state' = if (outOfOrder) {
      ...state,
      pendingRequired: state.pendingRequired - 1,
      bufferedRequired: state.bufferedRequired + 1,
    } else {
      ...state,
      pendingRequired: state.pendingRequired - 1,
      committedRequired: state.committedRequired + 1,
    },
  }

  action drainBuffered = all {
    state.status == 0,
    state.bufferedRequired > 0,
    state' = {
      ...state,
      bufferedRequired: state.bufferedRequired - 1,
      committedRequired: state.committedRequired + 1,
    },
  }

  action demandProjection = all {
    state.status == 0,
    state' = if (state.pendingProjection) {
      ...state,
      projectionQueued: true,
    } else {
      ...state,
      pendingProjection: true,
    },
  }

  action completeProjection(revision: int): bool = all {
    state.status == 0,
    state.pendingProjection,
    state' = {
      ...state,
      pendingProjection: state.projectionQueued,
      projectionQueued: false,
      lastProjection: if (revision > state.lastProjection) revision else state.lastProjection,
    },
  }

  pure def invalidate(current: State, nextStatus: int): State = {
    ...current,
    generation: current.generation + 1,
    status: nextStatus,
    dispatchedRequired: 0,
    committedRequired: 0,
    pendingRequired: 0,
    bufferedRequired: 0,
    pendingProjection: false,
    projectionQueued: false,
    lastProjection: -1,
  }

  action replace = all { state.status != 3, state' = invalidate(state, 0) }
  action cancel = all { state.status != 1, state.status != 3, state' = invalidate(state, 1) }
  action failGeneration = state' = invalidate(state, 2)
  action dispose = state' = invalidate(state, 3)

  // A reply carrying an invalidated generation is observed and refused without
  // changing the accepted generation or committing a record/projection.
  action staleReply = state' = state

  action step = {
    nondet operation = 0.to(9).oneOf()
    nondet outOfOrder = Set(true, false).oneOf()
    nondet revision = 0.to(3).oneOf()
    if (operation == 0 and state.status == 0 and state.pendingRequired + state.bufferedRequired < maxPendingRequired) enqueueRequired
    else if (operation == 1 and state.status == 0 and state.pendingRequired > 0) completeRequired(outOfOrder)
    else if (operation == 2 and state.status == 0 and state.bufferedRequired > 0) drainBuffered
    else if (operation == 3 and state.status == 0) demandProjection
    else if (operation == 4 and state.status == 0 and state.pendingProjection) completeProjection(revision)
    else if (operation == 5 and state.status != 3) replace
    else if (operation == 6 and state.status != 1 and state.status != 3) cancel
    else if (operation == 7 and state.status == 0) failGeneration
    else if (operation == 8 and state.status != 3) dispose
    else staleReply
  }

  val requiredOwnershipIsBounded = and {
    state.pendingRequired >= 0,
    state.bufferedRequired >= 0,
    state.pendingRequired + state.bufferedRequired <= maxPendingRequired,
  }

  val requiredRecordsCommitAtMostOnce = and {
    state.committedRequired >= 0,
    state.committedRequired <= state.dispatchedRequired,
    state.committedRequired + state.pendingRequired + state.bufferedRequired == state.dispatchedRequired,
  }

  val projectionOwnershipIsBounded = not(state.projectionQueued) or state.pendingProjection

  val invalidatedGenerationsOwnNothing = state.status == 0 or and {
    state.pendingRequired == 0,
    state.bufferedRequired == 0,
    not(state.pendingProjection),
    not(state.projectionQueued),
  }

  val sessionOperationsSafe = and {
    state.generation >= 0,
    requiredOwnershipIsBounded,
    requiredRecordsCommitAtMostOnce,
    projectionOwnershipIsBounded,
    invalidatedGenerationsOwnNothing,
  }
}

module sessionOperationsTest {
  import sessionOperations.*

  run reversedRequiredRepliesCommitInOrder =
    init
      .then(enqueueRequired)
      .then(enqueueRequired)
      .then(completeRequired(true))
      .then(completeRequired(false))
      .then(drainBuffered)
      .expect(and {
        state.committedRequired == 2,
        state.pendingRequired == 0,
        state.bufferedRequired == 0,
        sessionOperationsSafe,
      })

  run slowProjectionConsumerCoalesces =
    init
      .then(demandProjection)
      .then(demandProjection)
      .then(completeProjection(2))
      .expect(and {
        state.pendingProjection,
        not(state.projectionQueued),
        state.lastProjection == 2,
        sessionOperationsSafe,
      })

  run cancellationRejectsLateReply =
    init
      .then(enqueueRequired)
      .then(demandProjection)
      .then(cancel)
      .then(staleReply)
      .expect(and {
        state.generation == 1,
        state.status == 1,
        invalidatedGenerationsOwnNothing,
        sessionOperationsSafe,
      })

  run replacementRecoversAfterFailure =
    init
      .then(enqueueRequired)
      .then(failGeneration)
      .then(staleReply)
      .then(replace)
      .then(enqueueRequired)
      .then(completeRequired(false))
      .expect(and {
        state.generation == 2,
        state.status == 0,
        state.committedRequired == 1,
        sessionOperationsSafe,
      })
}
