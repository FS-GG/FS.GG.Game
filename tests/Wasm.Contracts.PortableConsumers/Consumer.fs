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
    $"{abi}|{path}|{support}|{descriptor.Limits.MaximumMemoryPages}|{descriptorRule}"
