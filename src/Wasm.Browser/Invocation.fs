namespace FS.GG.Wasm.Browser

open System
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

type private Step =
    | AwaitDescriptorAllocation
    | AwaitInputAllocation of descriptor: ValidatedSpan
    | AwaitGuest of descriptor: ValidatedSpan * input: ValidatedSpan
    | AwaitDescriptorRead of descriptor: ValidatedSpan * input: ValidatedSpan * primary: TerminalState option
    | AwaitOutputCopy of descriptor: ValidatedSpan * input: ValidatedSpan * output: ValidatedSpan
    | AwaitFree of
        current: GuestSpan *
        remaining: GuestSpan list *
        primary: TerminalState *
        output: byte array *
        cleanup: CleanupFault option
    | Terminal

type private InvocationData =
    {
        Configuration: ValidatedConfiguration
        Identity: HostIdentity
        Call: InvocationCall
        Input: byte array
        Dispatched: bool
        Step: Step
    }

type InvocationState = private InvocationState of InvocationData

[<RequireQualifiedAccess>]
module Invocation =
    let private outcome data phase state cleanup output =
        {
            Identity = data.Identity
            Phase = phase
            State = state
            Dispatch = (if data.Dispatched then Dispatched else NotDispatched)
            CleanupFault = cleanup
            CopiedOutput = output
            EffectEligibility = ProductAdapterMustDecide
        }

    let private finish data phase state cleanup output terminate =
        let phase =
            if
                state = Succeeded
                || (match state with
                    | GuestRejected _ -> true
                    | _ -> false)
            then
                (match data.Call with
                 | InitializeCall -> Initialize
                 | ProcessCall -> Process)
            else
                phase

        let terminal = InvocationState { data with Step = Terminal }

        let effects =
            (if terminate then [ TerminateGuest ] else [])
            @ [ InvocationCompleted(outcome data phase state cleanup output) ]

        terminal, effects

    let private protocolFault data detail =
        finish data Process (Faulted("unexpected invocation event: " + detail)) None [||] true

    let private validate data memory purpose span =
        let policy = (Validation.descriptor data.Configuration).Spans

        if
            memory >
                uint64 (Validation.configuration data.Configuration).Limits.MaximumMemoryPages
                * 65536UL
        then
            Error [ SpanOutsideMemory ]
        else
            Spans.validate policy memory purpose span

    let private frees spans =
        spans |> List.choose (fun span -> if span.Length = 0UL then None else Some span)

    let private startCleanup data primary output spans =
        match frees spans with
        | [] -> finish data Process primary None output false
        | first :: rest ->
            InvocationState
                { data with
                    Step = AwaitFree(first, rest, primary, output, None)
                },
            [ FreeOwned first ]

    let start configuration identity call input =
        let bytes =
            if obj.ReferenceEquals(input, null) then
                [||]
            else
                Array.copy input

        let data =
            {
                Configuration = configuration
                Identity = identity
                Call = call
                Input = bytes
                Dispatched = false
                Step = AwaitDescriptorAllocation
            }

        let limits = (Validation.configuration configuration).Limits

        if bytes.Length > limits.MaximumInputBytes then
            finish data AllocateInput (Faulted "input exceeds configured limit") None [||] true
        else
            InvocationState data,
            [
                Allocate(DescriptorAllocation, uint32 (Validation.descriptor configuration).Spans.DescriptorBytes)
            ]

    let update event (InvocationState data as state) =
        let descriptorBytes =
            uint64 (Validation.descriptor data.Configuration).Spans.DescriptorBytes

        match data.Step, event with
        | Terminal, _ -> state, []
        | AwaitDescriptorAllocation, AllocationReturned(DescriptorAllocation, pointer, memory) ->
            let span =
                {
                    Pointer = uint64 pointer
                    Length = descriptorBytes
                }

            match validate data memory Descriptor span with
            | Error _ -> finish data AllocateDescriptor (Faulted "invalid descriptor allocation") None [||] true
            | Ok descriptor ->
                InvocationState
                    { data with
                        Step = AwaitInputAllocation descriptor
                    },
                [ Allocate(InputAllocation, uint32 data.Input.Length) ]
        | AwaitDescriptorAllocation, AllocationTrapped(DescriptorAllocation, diagnostic) ->
            finish data AllocateDescriptor (Faulted diagnostic) None [||] true
        | AwaitInputAllocation descriptor, AllocationReturned(InputAllocation, pointer, memory) ->
            // Both allocations are checked against the refreshed post-call memory.
            let descriptorSpan = Spans.value descriptor

            match
                validate data memory Descriptor descriptorSpan,
                validate
                    data
                    memory
                    Input
                    {
                        Pointer = uint64 pointer
                        Length = uint64 data.Input.Length
                    }
            with
            | Ok refreshedDescriptor, Ok input when not (Spans.overlap refreshedDescriptor input) ->
                let ds, ins = Spans.value refreshedDescriptor, Spans.value input

                InvocationState
                    { data with
                        Step = AwaitGuest(refreshedDescriptor, input)
                        Dispatched = true
                    },
                [
                    ZeroDescriptor ds
                    CopyInput(ins, Array.copy data.Input)
                    InvokeGuest(data.Call, ins, ds)
                ]
            | _ ->
                // Allocator provenance is ambiguous after an invalid/aliased
                // result, so neither allocation is passed back to guest free.
                finish data AllocateInput (Faulted "invalid or overlapping input allocation") None [||] true
        | AwaitInputAllocation _, AllocationTrapped(InputAllocation, diagnostic) ->
            finish data AllocateInput (Faulted diagnostic) None [||] true
        | AwaitGuest(descriptor, input), GuestReturned(status, memory) ->
            match
                validate data memory Descriptor (Spans.value descriptor), validate data memory Input (Spans.value input)
            with
            | Ok refreshedDescriptor, Ok refreshedInput when not (Spans.overlap refreshedDescriptor refreshedInput) ->
                let primary = if status = 0 then None else Some(GuestRejected status)

                InvocationState
                    { data with
                        Step = AwaitDescriptorRead(refreshedDescriptor, refreshedInput, primary)
                    },
                [ ReadDescriptor(Spans.value refreshedDescriptor) ]
            | _ ->
                finish
                    data
                    (match data.Call with
                     | InitializeCall -> Initialize
                     | ProcessCall -> Process)
                    (Faulted "owned spans invalid after guest call")
                    None
                    [||]
                    true
        | AwaitGuest(descriptor, input), GuestTrapped(diagnostic, memory) ->
            match
                validate data memory Descriptor (Spans.value descriptor), validate data memory Input (Spans.value input)
            with
            | Ok d, Ok i when not (Spans.overlap d i) ->
                startCleanup data (Faulted diagnostic) [||] [ Spans.value i; Spans.value d ]
            | _ ->
                finish
                    data
                    (match data.Call with
                     | InitializeCall -> Initialize
                     | ProcessCall -> Process)
                    (Faulted diagnostic)
                    None
                    [||]
                    true
        | AwaitDescriptorRead(descriptor, input, primary), DescriptorRead(pointer, length, memory) ->
            let outputWithinLimit =
                uint64 length
                <= uint64 (Validation.configuration data.Configuration).Limits.MaximumOutputBytes

            match
                validate data memory Descriptor (Spans.value descriptor),
                validate data memory Input (Spans.value input),
                validate
                    data
                    memory
                    Output
                    {
                        Pointer = uint64 pointer
                        Length = uint64 length
                    }
            with
            | Ok d, Ok i, Ok o when
                outputWithinLimit
                && not (Spans.overlap d i)
                && not (Spans.overlap d o)
                && not (Spans.overlap i o)
                ->
                match primary with
                | Some rejected -> startCleanup data rejected [||] [ Spans.value o; Spans.value i; Spans.value d ]
                | None ->
                    InvocationState
                        { data with
                            Step = AwaitOutputCopy(d, i, o)
                        },
                    [ CopyOutput(Spans.value o) ]
            | _ ->
                finish
                    data
                    (match data.Call with
                     | InitializeCall -> Initialize
                     | ProcessCall -> Process)
                    (defaultArg primary (Faulted "invalid or overlapping output descriptor"))
                    None
                    [||]
                    true
        | AwaitDescriptorRead(_, _, primary), DescriptorReadFailed diagnostic ->
            finish
                data
                (match data.Call with
                 | InitializeCall -> Initialize
                 | ProcessCall -> Process)
                (defaultArg primary (Faulted diagnostic))
                None
                [||]
                true
        | AwaitOutputCopy(descriptor, input, output), OutputCopied bytes ->
            let copied =
                if obj.ReferenceEquals(bytes, null) then
                    [||]
                else
                    Array.copy bytes

            let expected = Spans.value output

            if uint64 copied.Length <> expected.Length then
                finish data Process (Faulted "copied output length changed") None [||] true
            else
                startCleanup data Succeeded copied [ expected; Spans.value input; Spans.value descriptor ]
        | AwaitOutputCopy(_, _, _), OutputCopyFailed diagnostic ->
            finish data Process (Faulted diagnostic) None [||] true
        | AwaitFree(current, remaining, primary, output, cleanup), FreeReturned(span, memory) when span = current ->
            let refreshed =
                remaining |> List.map (fun item -> item, validate data memory Output item)

            if
                memory >
                    uint64 (Validation.configuration data.Configuration).Limits.MaximumMemoryPages
                    * 65536UL
                || (refreshed |> List.exists (fun (_, result) -> Result.isError result))
            then
                finish
                    data
                    Free
                    (match primary with
                     | Succeeded -> Faulted "owned span invalid after free"
                     | value -> value)
                    (Some
                        {
                            Phase = Free
                            Diagnostic = "owned span invalid after free"
                        })
                    [||]
                    true
            else
                match remaining with
                | next :: rest ->
                    InvocationState
                        { data with
                            Step = AwaitFree(next, rest, primary, output, cleanup)
                        },
                    [ FreeOwned next ]
                | [] ->
                    let finalState =
                        match primary, cleanup with
                        | Succeeded, Some fault -> Faulted fault.Diagnostic
                        | value, _ -> value

                    finish data Free finalState cleanup (if finalState = Succeeded then output else [||]) false
        | AwaitFree(current, _, primary, _, _), FreeTrapped(span, diagnostic) when span = current ->
            let fault =
                Some
                    {
                        Phase = Free
                        Diagnostic = diagnostic
                    }

            let finalState =
                match primary with
                | Succeeded -> Faulted diagnostic
                | value -> value

            finish data Free finalState fault [||] true
        | _ -> protocolFault data (string event)

    let isTerminal (InvocationState data) = data.Step = Terminal
