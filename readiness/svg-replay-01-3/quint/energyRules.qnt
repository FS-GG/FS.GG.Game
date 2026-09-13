module energyRules {
  // A neutral energy/door rule set. Accepted and predicted state are separate,
  // matching Game.Core Planning and RuleCatalog rather than defining another game runtime.
  type State = {
    acceptedEnergy: int,
    acceptedDoorOpen: bool,
    acceptedRevision: int,
    predictedEnergy: int,
    predictedDoorOpen: bool,
  }

  var state: State

  action init = state' = {
    acceptedEnergy: 2,
    acceptedDoorOpen: false,
    acceptedRevision: 0,
    predictedEnergy: 2,
    predictedDoorOpen: false,
  }

  pure def energyAvailable(value: State): bool = value.predictedEnergy > 0
  pure def doorEnterDependsOnEnergy(value: State): bool = not(value.predictedDoorOpen) and energyAvailable(value)

  action predictEnterDoor = all {
    doorEnterDependsOnEnergy(state),
    state' = {
      ...state,
      predictedEnergy: state.predictedEnergy - 1,
      predictedDoorOpen: true,
    },
  }

  action cancelPrediction = state' = {
    ...state,
    predictedEnergy: state.acceptedEnergy,
    predictedDoorOpen: state.acceptedDoorOpen,
  }

  action acceptPrediction = state' = {
    ...state,
    acceptedEnergy: state.predictedEnergy,
    acceptedDoorOpen: state.predictedDoorOpen,
    acceptedRevision: state.acceptedRevision + 1,
  }

  action stutter = state' = state

  action step = {
    nondet operation = 0.to(3).oneOf()
    if (operation == 0 and doorEnterDependsOnEnergy(state)) predictEnterDoor
    else if (operation == 1) cancelPrediction
    else if (operation == 2) acceptPrediction
    else stutter
  }

  val energyNeverNegative = and { state.acceptedEnergy >= 0, state.predictedEnergy >= 0 }
  val acceptedRevisionMonotonic = state.acceptedRevision >= 0
  val doorRequiresSpentEnergy = not(state.predictedDoorOpen) or state.predictedEnergy < 2
  val energyRulesSafe = and { energyNeverNegative, acceptedRevisionMonotonic, doorRequiresSpentEnergy }
}

module energyRulesTest {
  import energyRules.*

  run predictionDoesNotCommit =
    init.then(predictEnterDoor).expect(and {
      state.acceptedEnergy == 2,
      not(state.acceptedDoorOpen),
      state.acceptedRevision == 0,
      state.predictedEnergy == 1,
      state.predictedDoorOpen,
      energyRulesSafe,
    })

  run cancellationRestoresAcceptedBasis =
    init.then(predictEnterDoor).then(cancelPrediction).expect(and {
      state.predictedEnergy == state.acceptedEnergy,
      state.predictedDoorOpen == state.acceptedDoorOpen,
      energyRulesSafe,
    })

  run acceptanceCommitsThePrediction =
    init.then(predictEnterDoor).then(acceptPrediction).expect(and {
      state.acceptedEnergy == 1,
      state.acceptedDoorOpen,
      state.acceptedRevision == 1,
      energyRulesSafe,
    })
}
