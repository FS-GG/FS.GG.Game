namespace FS.GG.Wasm.Browser

open FS.GG.Wasm.Contracts

type InvocationCall =
    | InitializeCall
    | ProcessCall

type AllocationPurpose =
    | DescriptorAllocation
    | InputAllocation

type InvocationEffect =
    | Allocate of purpose: AllocationPurpose * length: uint32
    | ZeroDescriptor of GuestSpan
    | CopyInput of destination: GuestSpan * bytes: byte array
    | InvokeGuest of call: InvocationCall * input: GuestSpan * descriptor: GuestSpan
    | ReadDescriptor of GuestSpan
    | CopyOutput of GuestSpan
    | FreeOwned of GuestSpan
    | TerminateGuest
    | InvocationCompleted of InvocationOutcome

type InvocationEvent =
    | AllocationReturned of purpose: AllocationPurpose * pointer: uint32 * memoryBytes: uint64
    | AllocationTrapped of purpose: AllocationPurpose * diagnostic: string
    | GuestReturned of status: int * memoryBytes: uint64
    | GuestTrapped of diagnostic: string * memoryBytes: uint64
    | DescriptorRead of outputPointer: uint32 * outputLength: uint32 * memoryBytes: uint64
    | DescriptorReadFailed of diagnostic: string
    | OutputCopied of bytes: byte array
    | OutputCopyFailed of diagnostic: string
    | FreeReturned of span: GuestSpan * memoryBytes: uint64
    | FreeTrapped of span: GuestSpan * diagnostic: string

type InvocationState

[<RequireQualifiedAccess>]
module Invocation =
    val start:
        ValidatedConfiguration -> HostIdentity -> InvocationCall -> byte array -> InvocationState * InvocationEffect list

    /// Advances one checked step. An unexpected event terminates the guest and
    /// completes the invocation once; no further guest call is emitted.
    val update: InvocationEvent -> InvocationState -> InvocationState * InvocationEffect list

    val isTerminal: InvocationState -> bool
