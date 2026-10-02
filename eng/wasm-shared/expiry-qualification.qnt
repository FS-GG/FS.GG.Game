// Qualification schedules over the unchanged authoritative published lifecycle.
// Queue deadlines are 256/257; active deadline is 255. Both timer timings are valid.
module expiryQualification {
  import lifecycle.* from "./lifecycle"
  action queuedPrefix =
    if (state.clock == 0) loadInitial
    else if (state.clock == 1) compiled
    else if (state.clock == 2) initialize
    else if (state.clock == 3) initialized
    else submitOrdinary
  action earlyStep = if (state.clock < 7) queuedPrefix else state' = expireAtState(state, 256)
  action lateStep = if (state.clock < 7) queuedPrefix else state' = expireAtState(state, 259)
}
