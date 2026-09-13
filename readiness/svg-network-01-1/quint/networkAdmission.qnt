module networkAdmission {
  // Finite control model for the portable admission/resync contract. Payload meaning and transport
  // timing are deliberately outside this model and remain product/Net implementation concerns.
  var lastA: int
  var lastB: int
  var acceptedOrder: int
  var acceptedRevision: int
  var clientRevision: int

  action init = all {
    lastA' = 0,
    lastB' = 0,
    acceptedOrder' = 0,
    acceptedRevision' = 0,
    clientRevision' = 0,
  }

  action admitA = all {
    lastA' = lastA + 1,
    lastB' = lastB,
    acceptedOrder' = acceptedOrder + 1,
    acceptedRevision' = acceptedRevision + 1,
    clientRevision' = clientRevision,
  }

  action admitB = all {
    lastA' = lastA,
    lastB' = lastB + 1,
    acceptedOrder' = acceptedOrder + 1,
    acceptedRevision' = acceptedRevision + 1,
    clientRevision' = clientRevision,
  }

  action resync = all {
    lastA' = lastA,
    lastB' = lastB,
    acceptedOrder' = acceptedOrder,
    acceptedRevision' = acceptedRevision,
    clientRevision' = acceptedRevision,
  }

  action stutter = all {
    lastA' = lastA,
    lastB' = lastB,
    acceptedOrder' = acceptedOrder,
    acceptedRevision' = acceptedRevision,
    clientRevision' = clientRevision,
  }

  action step = {
    nondet operation = 0.to(3).oneOf()
    if (operation == 0) admitA
    else if (operation == 1) admitB
    else if (operation == 2) resync
    else stutter
  }

  val sequencesMonotonic = and { lastA >= 0, lastB >= 0 }
  val orderMatchesAcceptedRevision = acceptedOrder == acceptedRevision
  val clientCannotLeadAuthority = clientRevision <= acceptedRevision
  val networkAdmissionSafe = and { sequencesMonotonic, orderMatchesAcceptedRevision, clientCannotLeadAuthority }
}

module networkAdmissionTest {
  import networkAdmission.*

  run twoClientsHaveOneOrder =
    init.then(admitB).then(admitA).expect(and {
      lastA == 1,
      lastB == 1,
      acceptedOrder == 2,
      acceptedRevision == 2,
      networkAdmissionSafe,
    })

  run resyncDoesNotCommitGameplay =
    init.then(admitA).then(resync).expect(and {
      acceptedOrder == 1,
      acceptedRevision == 1,
      clientRevision == 1,
      networkAdmissionSafe,
    })
}
