module Game.Core.Tests.InputCommandTests

// InputCommand capsule — the abstract input-command vocabulary: the policy half of the input-config
// split (epic FS-GG/FS.GG.Rendering#330). Pure data, device-free. The load-bearing guarantees are
// that `all` is the whole vocabulary in a stable order, and that `id` is a FROZEN, unique, total
// serialization token that round-trips through `ofId` — a persisted keymap keys on those tokens.
//
// Deterministic by construction: every case iterates `Command.all`, so there is no RNG and no seed.

open Expecto
open FS.GG.Game.Core

[<Tests>]
let tests =
    testList "Game.Core InputCommand (abstract command vocabulary)" [

        // -----------------------------------------------------------------------------------------
        // `all` is the vocabulary. Pin it exactly and in order: it IS the rebindable command list a
        // config screen enumerates, so a reorder or an accidental add/drop is a contract change.

        test "all is exactly the six intents, in the declared order" {
            Expect.equal
                Command.all
                [ Command.MoveNorth
                  Command.MoveSouth
                  Command.MoveWest
                  Command.MoveEast
                  Command.Fire
                  Command.Pause ]
                "the vocabulary and its order are part of the contract"
        }

        test "all has no duplicates" {
            Expect.equal (List.distinct Command.all) Command.all "a command may appear once"
        }

        // -----------------------------------------------------------------------------------------
        // `id` is a frozen wire contract. Pin the exact tokens (golden): a rename here is exactly the
        // silent-rebind bug the .fsi warns about, and this test is what makes it loud.

        test "id emits the frozen tokens" {
            let actual = Command.all |> List.map (fun c -> c, Command.id c)

            Expect.equal
                actual
                [ Command.MoveNorth, "move.north"
                  Command.MoveSouth, "move.south"
                  Command.MoveWest, "move.west"
                  Command.MoveEast, "move.east"
                  Command.Fire, "fire"
                  Command.Pause, "pause" ]
                "id tokens are a serialization contract and must not change once shipped"
        }

        test "id is unique across the vocabulary" {
            let ids = Command.all |> List.map Command.id
            Expect.equal (List.distinct ids) ids "two commands sharing a token would collide in a keymap"
        }

        test "id is never empty" {
            for c in Command.all do
                Expect.isTrue (Command.id c |> String.length > 0) $"id of %A{c} must be non-empty"
        }

        // -----------------------------------------------------------------------------------------
        // `ofId` is the inverse of `id`, and total.

        test "ofId round-trips every command's id" {
            for c in Command.all do
                Expect.equal (Command.ofId (Command.id c)) (Some c) $"ofId (id %A{c}) must be Some %A{c}"
        }

        test "ofId returns None for an unknown token" {
            Expect.equal (Command.ofId "no.such.command") None "an unknown token is dropped, not a crash"
            Expect.equal (Command.ofId "") None "the empty string is not a command"
            Expect.equal (Command.ofId "Move.North") None "tokens are case-sensitive"
        }

        // -----------------------------------------------------------------------------------------
        // `label` is display text: total, non-empty, and distinct so a config screen has no two rows
        // reading the same. It is deliberately NOT pinned to exact wording — labels may be reworded,
        // only `id` is frozen.

        test "label is non-empty and total over the vocabulary" {
            for c in Command.all do
                Expect.isTrue (Command.label c |> String.length > 0) $"label of %A{c} must be non-empty"
        }

        test "labels are distinct" {
            let labels = Command.all |> List.map Command.label
            Expect.equal (List.distinct labels) labels "two commands must not share a screen label"
        }
    ]
