module Wasm.Lifecycle.Tests.RuntimeProtocolTests

open Expecto
open FS.GG.Wasm.Contracts
open FS.GG.Wasm.Browser

let private sha character = String.replicate 64 character

let private validated path =
    let descriptor = Profiles.tryFind path |> Option.get

    Validation.validateConfiguration
        {
            Path = path
            ArtifactSha256 = sha "a"
            ConfigurationSha256 = sha "b"
            Limits = descriptor.Limits
            Deadline = descriptor.Deadline
            Scheduling = descriptor.Scheduling
            Replacement = descriptor.Replacement
        }
    |> function
        | Ok value -> value
        | Error issues -> failtestf "expected valid configuration: %A" issues

[<Tests>]
let protocol =
    testList
        "runtime protocol seam"
        [
            testCase "wire identities require canonical lossless unsigned decimals"
            <| fun _ ->
                let identity =
                    Identity.parse
                        {
                            WorkerInstance = "worker-a"
                            Request = "18446744073709551615"
                            Generation = "42"
                        }

                Expect.isOk identity "uint64 maximum remains lossless"

                Expect.equal
                    (Identity.parse
                        {
                            WorkerInstance = "worker-a"
                            Request = "01"
                            Generation = "42"
                        })
                    (Error [ NonCanonicalUnsignedDecimal "request" ])
                    "leading zero identity is not canonical"

                Expect.equal
                    (Identity.parse
                        {
                            WorkerInstance = "worker-a"
                            Request = "9007199254740993"
                            Generation = "42"
                        }
                     |> Result.map Identity.toWire)
                    (Ok
                        {
                            WorkerInstance = "worker-a"
                            Request = "9007199254740993"
                            Generation = "42"
                        })
                    "values above the JavaScript safe integer limit round trip as decimal strings"

            testCase "BAR and SC2 scheduling remain separate"
            <| fun _ ->
                let bar = HostSettings.create (validated BarProtected) None
                Expect.isOk bar "BAR requires no product role"
                let barScheduling = bar |> Result.map HostSettings.scheduling

                Expect.equal
                    barScheduling
                    (Ok
                        {
                            Ordinary = RefuseWhileBusy
                            Realtime = None
                        })
                    "BAR stays single-pending"

                let sc2 = HostSettings.create (validated Sc2ImportedStrict) (Some Controller)

                match sc2 |> Result.map HostSettings.scheduling with
                | Error issues -> failtestf "SC2 settings failed: %A" issues
                | Ok value ->
                    Expect.equal value.Ordinary (BoundedFifo 4) "ordinary SC2 queue remains four"
                    let realtime = value.Realtime |> Option.get
                    Expect.equal realtime.MaximumOrderedIncludingActive 256 "ordered backlog includes active"

                    Expect.equal
                        realtime.MaximumRetainedBytesIncludingActive
                        (4 * 1024 * 1024)
                        "retained bytes include active"

                    Expect.equal
                        realtime.MaximumIndividualInputBytes
                        (256 * 1024)
                        "individual realtime input stays bounded"

                    Expect.isTrue realtime.CoalescePendingSnapshots "only the pending snapshot lane is replaceable"

            testCase "profile role capacity is validated without granting authority"
            <| fun _ ->
                Expect.equal
                    (HostSettings.create (validated BarProtected) (Some Controller))
                    (Error [ RoleNotSupportedByProfile Controller ])
                    "BAR cannot acquire an SC2 capacity role"

                Expect.equal
                    (HostSettings.create (validated Sc2ImportedStrict) None)
                    (Error [ InvalidSchedulingLimit "role" ])
                    "SC2 requires one declared capacity role"
        ]
