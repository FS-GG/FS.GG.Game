namespace FS.GG.Game.Harness

open FS.GG.Game.Core

type Run<'f> =
    { Trace: Trace<'f>
      Captured: Command list list }

[<RequireQualifiedAccess>]
module Driver =

    let identityFingerprint (world: 'world) : 'world = world

    /// Apply a frame's commands in order, then advance exactly one whole fixed step.
    let private stepFrame (playable: Playable<'world, 'key>) (commands: Command list) (world: 'world) : 'world =
        let applied = commands |> List.fold (fun w c -> playable.Apply c w) world
        playable.Step applied playable.Dt

    let runCommands
        (playable: Playable<'world, 'key>)
        (fingerprint: 'world -> 'f)
        (script: Command list list)
        : Trace<'f> =
        let mutable world = playable.Init
        let frames = ResizeArray<'f>(List.length script)

        for commands in script do
            world <- stepFrame playable commands world
            frames.Add(fingerprint world)

        Trace.create Origin.InputDriven (List.ofSeq frames)

    let runScript
        (playable: Playable<'world, 'key>)
        (fingerprint: 'world -> 'f)
        (keyScript: 'key list list)
        : Trace<'f> =
        let script =
            keyScript
            |> List.map (fun keys -> keys |> List.choose (fun k -> Playable.resolve playable k))

        runCommands playable fingerprint script

    let runBot
        (playable: Playable<'world, 'key>)
        (observe: 'world -> 'view)
        (bot: Bot<'view>)
        (seed: uint64)
        (steps: int)
        (fingerprint: 'world -> 'f)
        : Run<'f> =
        let mutable world = playable.Init
        let mutable rng = Rng.ofSeed seed
        let frames = ResizeArray<'f>(max 0 steps)
        let captured = ResizeArray<Command list>(max 0 steps)

        for _ in 1..steps do
            let view = observe world
            let struct (commands, rng') = bot.Decide view rng
            rng <- rng'
            captured.Add(commands)
            world <- stepFrame playable commands world
            frames.Add(fingerprint world)

        { Trace = Trace.create Origin.InputDriven (List.ofSeq frames)
          Captured = List.ofSeq captured }
