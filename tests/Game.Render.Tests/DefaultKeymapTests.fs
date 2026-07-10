module Game.Render.Tests.DefaultKeymapTests

// Game#109 (ADR-0022, epic FS-GG/FS.GG.Rendering#330): the default keymap is the POLICY half of the
// input-config split. Game.Core owns the abstract Command vocabulary; FS.GG.UI.KeyboardInput owns the
// command-id-agnostic Keymap mechanism (Rendering#331); FS.GG.Game.Render.DefaultKeymap binds one to
// the other. Headless (Skia-free): pure data. The acceptance is "the default keymap resolves" — these
// pin exactly that, keyed by the canonical KeyId (ViewerKeyboard.toKeyId) the live dispatch path uses.

open Expecto
open FS.GG.Game.Core
open FS.GG.UI.KeyboardInput
open FS.GG.Game.Render

let private keyId (key: ViewerKey) : KeyId = ViewerKeyboard.toKeyId key

[<Tests>]
let tests =
    testList "FS.GG.Game.Render DefaultKeymap (Game#109)" [

        test "every command in the vocabulary is bound exactly once" {
            let bound = DefaultKeymap.bindings |> List.map fst
            Expect.equal (List.sort bound) (List.sort Command.all) "bindings cover Command.all"
            Expect.equal (List.length bound) (List.length (List.distinct bound)) "no command bound twice"
        }

        test "bindings are listed in Command.all order (agrees row-for-row with a config screen)" {
            Expect.equal (DefaultKeymap.bindings |> List.map fst) Command.all "same order as Command.all"
        }

        test "no two commands share a key — the built keymap carries no conflict" {
            let keys = DefaultKeymap.bindings |> List.map (snd >> keyId)
            Expect.equal (List.length keys) (List.length (List.distinct keys)) "every default key is distinct"
            Expect.equal (Keymap.validate DefaultKeymap.value) [] "Keymap.validate reports no conflicts"
        }

        test "the keymap resolves every default binding to its command's id (the acceptance)" {
            for (command, key) in DefaultKeymap.bindings do
                Expect.equal
                    (Keymap.resolve DefaultKeymap.value (keyId key))
                    (Some(Command.id command))
                    $"{keyId key} resolves to {Command.id command}"
        }

        test "count matches the vocabulary size — nothing collapsed on build" {
            Expect.equal (Keymap.count DefaultKeymap.value) (List.length Command.all) "one binding per command"
        }

        test "the arrow cluster drives movement, Space fires, Escape pauses" {
            let resolves key command =
                Expect.equal (Keymap.resolve DefaultKeymap.value (keyId key)) (Some(Command.id command)) "default binding"
            resolves ArrowUp Command.MoveNorth
            resolves ArrowDown Command.MoveSouth
            resolves ArrowLeft Command.MoveWest
            resolves ArrowRight Command.MoveEast
            resolves Space Command.Fire
            resolves Escape Command.Pause
        }

        test "every bound command token round-trips through Command.ofId (a valid, persistable keymap)" {
            for binding in Keymap.toBindings DefaultKeymap.value do
                Expect.isSome (Command.ofId binding.Command) $"'{binding.Command}' is a real command token"
        }

        test "an unbound key resolves to nothing" {
            Expect.isNone (Keymap.resolve DefaultKeymap.value (keyId (Letter 'Z'))) "Z is not a default binding"
        }
    ]
