namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type WorkerMechanics =
    { Sha256Hex: byte array -> Async<Result<string, string>>
      CompileAfterAdmission: byte array -> Async<Result<unit, string>>
      PerformInvocationEffect: InvocationEffect -> Async<Result<InvocationEvent option, string>> }

[<RequireQualifiedAccess>]
module WorkerEntry =
    val inspectAndCompile:
        WorkerMechanics -> HostIdentity -> OperationToken -> ValidatedConfiguration -> byte array -> Async<WorkerObservation list>

    val driveInvocation:
        WorkerMechanics -> ValidatedConfiguration -> HostIdentity -> OperationToken -> InvocationCall -> byte array -> Async<WorkerObservation list>
