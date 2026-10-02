module FS.GG.Wasm.Contracts.PortableConsumer

open FS.GG.Wasm.Contracts

let render descriptor =
    let abi =
        match descriptor.Abi.Id with
        | BarAbi1 -> "bar:1"
        | Sc2Abi10000 -> "sc2:65536"

    let support =
        match descriptor.Support with
        | SharedRuntimeCandidate -> "candidate"
        | InventoryOnlyMigrationRequired -> "inventory-only"

    let path =
        match descriptor.Path with
        | BarProtected -> "bar-protected"
        | Sc2ImportedStrict -> "sc2-imported-strict"
        | Sc2LegacyDirectUrl -> "sc2-legacy-direct-url"

    let descriptorRule = if descriptor.Spans.ZeroDescriptorBeforeCall then "zeroed" else "uninitialized"
    let descriptorAlignment =
        match Spans.validate descriptor.Spans 65536UL Descriptor { Pointer = 3UL; Length = 8UL } with
        | Ok _ -> "unaligned-descriptor-ok"
        | Error [ MisalignedPointer ] -> "descriptor-aligned"
        | Error issues -> $"descriptor-error:{issues.Length}"
    $"{abi}|{path}|{support}|{descriptor.Limits.MaximumMemoryPages}|{descriptorRule}|{descriptorAlignment}"
