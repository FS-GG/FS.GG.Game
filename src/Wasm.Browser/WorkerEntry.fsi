namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type WorkerMechanics =
    {
        Sha256Hex: byte array -> Async<Result<string, string>>
        CompileAfterAdmission: byte array -> Async<Result<unit, string>>
        PerformInvocationEffect: InvocationEffect -> Async<Result<InvocationEvent option, string>>
    }

type WorkerMechanicalEffect =
    {
        Kind: string
        Export: string
        Pointer: uint32
        Length: uint32
        Descriptor: uint32
        Bytes: byte array
    }

type WorkerMechanicalReply =
    {
        Pointer: uint32
        MemoryBytes: string
        Status: int
        OutputPointer: uint32
        OutputLength: uint32
        Bytes: byte array
        Diagnostic: string
    }

type WorkerWireMechanics =
    {
        Sha256: byte array -> (string -> unit) -> (string -> unit) -> unit
        Compile: byte array -> string -> string -> (uint32 -> unit) -> (string -> unit) -> unit
        Perform: WorkerMechanicalEffect -> WorkerMechanicalReply
    }

[<RequireQualifiedAccess>]
module WorkerEntry =
    val inspectAndCompile:
        WorkerMechanics ->
        HostIdentity ->
        OperationToken ->
        ValidatedConfiguration ->
        byte array ->
            Async<WorkerObservation list>

    val driveInvocation:
        WorkerMechanics ->
        ValidatedConfiguration ->
        HostIdentity ->
        OperationToken ->
        InvocationCall ->
        byte array ->
            Async<WorkerObservation list>

    val configurationToWire: ValidatedConfiguration -> CandidateConfigurationBoundary
    val commandToWire: ValidatedConfiguration -> WorkerCommand -> WorkerWireCommand
    val observationFromWire: WorkerWireObservation -> Result<WorkerObservation, string>
    val createWireRuntime: WorkerWireMechanics -> (WorkerWireObservation -> unit) -> (WorkerWireCommand -> unit)
