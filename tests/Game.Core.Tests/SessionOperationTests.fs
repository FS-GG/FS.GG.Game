module Game.Core.Tests.SessionOperationTests

open Expecto
open FS.GG.Game.Core

let private config maximum =
    {
        Target = SessionOperationTarget.AuthoritativeServer
        MaxPendingRequired = maximum
    }

let private initial maximum : SessionOperationState<string> =
    match SessionOperations.initialize (config maximum) with
    | Ok state -> state
    | Error refusal -> failtestf "operation initialization failed: %A" refusal

let private required payload state =
    let next, effects =
        SessionOperations.update (SessionOperationObservation.EnqueueRequired payload) state

    match effects with
    | [ SessionOperationEffect.DispatchRequired dispatch ] -> next, dispatch
    | _ -> failtestf "expected one required dispatch, got %A" effects

let private projection state =
    let next, effects =
        SessionOperations.update SessionOperationObservation.DemandProjection state

    match effects with
    | [ SessionOperationEffect.DispatchProjection dispatch ] -> next, dispatch
    | _ -> failtestf "expected one projection dispatch, got %A" effects

[<Tests>]
let tests =
    testList
        "Game.Core portable session operations"
        [
            test "configuration enforces a bounded required-record window" {
                Expect.equal
                    (SessionOperations.initialize (config 0u): Result<SessionOperationState<string>, _>)
                    (Error(SessionOperationRefusal.InvalidMaxPendingRequired 0u))
                    "zero capacity is invalid"

                let state, _ = required "first" (initial 1u)

                let refused, effects =
                    SessionOperations.update (SessionOperationObservation.EnqueueRequired "second") state

                Expect.equal refused state "capacity refusal preserves state"

                Expect.equal
                    effects
                    [
                        SessionOperationEffect.Refused(SessionOperationRefusal.RequiredCapacityReached 1u)
                    ]
                    "capacity is explicit"
            }

            test "required replies commit in request order even when completion is reversed" {
                let firstState, first = required "first-request" (initial 2u)
                let secondState, second = required "second-request" firstState

                let buffered, earlyEffects =
                    SessionOperations.update
                        (SessionOperationObservation.CompleteRequired(second.Id, "second-record"))
                        secondState

                Expect.isEmpty earlyEffects "the second record waits for the first"
                Expect.equal buffered.BufferedRequired.Count 1 "the out-of-order record is bounded ownership"

                let capacityState, capacityEffects =
                    SessionOperations.update (SessionOperationObservation.EnqueueRequired "third-request") buffered

                Expect.equal capacityState buffered "buffered and pending records share the same capacity bound"

                Expect.equal
                    capacityEffects
                    [
                        SessionOperationEffect.Refused(SessionOperationRefusal.RequiredCapacityReached 2u)
                    ]
                    "the reorder buffer cannot be used to evade capacity"

                let committed, effects =
                    SessionOperations.update
                        (SessionOperationObservation.CompleteRequired(first.Id, "first-record"))
                        buffered

                Expect.equal
                    effects
                    [
                        SessionOperationEffect.RequiredCommitted(0UL, "first-record")
                        SessionOperationEffect.RequiredCommitted(1UL, "second-record")
                    ]
                    "both records commit in their assigned order"

                Expect.isEmpty committed.PendingRequired "no required request remains"
                Expect.isEmpty committed.BufferedRequired "the reorder buffer is drained"

                let duplicate, duplicateEffects =
                    SessionOperations.update
                        (SessionOperationObservation.CompleteRequired(first.Id, "duplicate"))
                        committed

                Expect.equal duplicate committed "a duplicate reply cannot mutate accepted state"

                Expect.equal
                    duplicateEffects
                    [
                        SessionOperationEffect.Refused(SessionOperationRefusal.UnknownOrCompletedOperation first.Id)
                    ]
                    "a completed operation cannot commit twice"
            }

            test "projection demand coalesces behind one request and revisions stay monotonic" {
                let waiting, first = projection (initial 2u)

                let queued, queuedEffects =
                    SessionOperations.update SessionOperationObservation.DemandProjection waiting

                Expect.equal
                    queuedEffects
                    [ SessionOperationEffect.ProjectionDemandCoalesced first.Id ]
                    "a slow projection consumer owns only one queued refresh"

                let refreshed, effects =
                    SessionOperations.update
                        (SessionOperationObservation.CompleteProjection(first.Id, 7UL, "projection-7"))
                        queued

                let second =
                    match effects with
                    | [ SessionOperationEffect.ProjectionCommitted(7UL, "projection-7")
                        SessionOperationEffect.DispatchProjection dispatch ] -> dispatch
                    | _ -> failtestf "expected commit followed by one refresh dispatch, got %A" effects

                Expect.equal
                    refreshed.PendingProjection
                    (Some second.Id.Operation)
                    "the refresh is the sole pending projection"

                let stale, staleEffects =
                    SessionOperations.update
                        (SessionOperationObservation.CompleteProjection(second.Id, 7UL, "duplicate-revision"))
                        refreshed

                Expect.equal
                    stale.LastProjectionRevision
                    (Some 7UL)
                    "a stale projection cannot replace the accepted revision"

                Expect.equal
                    staleEffects
                    [
                        SessionOperationEffect.Refused(SessionOperationRefusal.StaleProjectionRevision(7UL, 7UL))
                    ]
                    "the monotonicity refusal is explicit"
            }

            test "replacement cancellation and failure invalidate stale generations" {
                let waiting, dispatch = required "input" (initial 2u)

                let replaced, replaceEffects =
                    SessionOperations.update SessionOperationObservation.Replace waiting

                Expect.equal replaced.Generation 1UL "replacement starts a new generation"
                Expect.isEmpty replaced.PendingRequired "replacement releases old required work"

                Expect.equal
                    replaceEffects
                    [
                        SessionOperationEffect.GenerationInvalidated(0UL, 1UL, SessionOperationStatus.Active)
                    ]
                    "the invalidation is observable"

                let unchanged, staleEffects =
                    SessionOperations.update
                        (SessionOperationObservation.CompleteRequired(dispatch.Id, "late"))
                        replaced

                Expect.equal unchanged replaced "a stale reply preserves the replacement"

                Expect.equal
                    staleEffects
                    [
                        SessionOperationEffect.Refused(SessionOperationRefusal.StaleGeneration(1UL, 0UL))
                    ]
                    "the stale generation is explicit"

                let active, failing = required "will-fail" replaced

                let failure =
                    {
                        Code = "server-unavailable"
                        Message = "try another generation"
                    }

                let failed, _ =
                    SessionOperations.update (SessionOperationObservation.Fail(failing.Id, failure)) active

                Expect.equal failed.Generation 2UL "failure invalidates pending operations"
                Expect.equal failed.Status (SessionOperationStatus.Failed failure) "failure remains inspectable"

                let cancelled, _ =
                    SessionOperations.update SessionOperationObservation.Cancel failed

                Expect.equal cancelled.Generation 3UL "cancellation invalidates the failed generation"

                Expect.equal
                    cancelled.Status
                    SessionOperationStatus.Cancelled
                    "cancellation is terminal until replacement"

                Expect.equal
                    (SessionOperations.update SessionOperationObservation.Cancel cancelled)
                    (cancelled, [])
                    "repeated cancellation owns no new generation"

                let cancelledReplacement, _ =
                    SessionOperations.update SessionOperationObservation.Replace cancelled

                let withProjection, lateProjection = projection cancelledReplacement

                let cancelledProjection, _ =
                    SessionOperations.update SessionOperationObservation.Cancel withProjection

                let preserved, lateEffects =
                    SessionOperations.update
                        (SessionOperationObservation.CompleteProjection(lateProjection.Id, 1UL, "late"))
                        cancelledProjection

                Expect.equal preserved cancelledProjection "a cancellation race cannot accept a late projection"

                Expect.equal
                    lateEffects
                    [
                        SessionOperationEffect.Refused(
                            SessionOperationRefusal.StaleGeneration(
                                cancelledProjection.Generation,
                                lateProjection.Id.Generation
                            )
                        )
                    ]
                    "late cancellation replies identify their stale generation"
            }

            test "disposal is idempotent and cannot be replaced" {
                let disposed, effects =
                    SessionOperations.update SessionOperationObservation.Dispose (initial 1u)

                Expect.equal disposed.Status SessionOperationStatus.Disposed "dispose closes the coordinator"

                Expect.equal
                    effects
                    [
                        SessionOperationEffect.GenerationInvalidated(0UL, 1UL, SessionOperationStatus.Disposed)
                    ]
                    "dispose invalidates owned work"

                Expect.equal
                    (SessionOperations.update SessionOperationObservation.Dispose disposed)
                    (disposed, [])
                    "repeated disposal is inert"

                let unchanged, refused =
                    SessionOperations.update SessionOperationObservation.Replace disposed

                Expect.equal unchanged disposed "disposed coordinators cannot restart"

                Expect.equal
                    refused
                    [
                        SessionOperationEffect.Refused(
                            SessionOperationRefusal.CoordinatorInactive SessionOperationStatus.Disposed
                        )
                    ]
                    "replacement after disposal is refused"
            }
        ]
