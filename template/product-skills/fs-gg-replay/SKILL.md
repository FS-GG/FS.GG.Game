---
name: fs-gg-replay
description: Use when recording, validating, seeking, or exporting deterministic FS.GG browser-game sessions.
---

# Deterministic game replay

Keep the replay at the semantic session boundary. Record accepted `SessionInput` values and fixed-step
advances only; browser events, animation-frame timestamps, audio callbacks, and hidden server facts never
belong in the portable log.

Use `ReplayRecorder.create`, `appendInput`, `appendAdvance`, and `addCheckpoint` from
`FS.GG.Game.Core`. Supply the canonical product-state digest after each accepted operation. Before playback,
run `Replay.validate`; then use `Replay.seek` with the same `SessionContract`, compatibility identity,
snapshot codec, and digest function as the live game. Treat `Diverged` as the first semantic mismatch and
show its event index rather than continuing with misleading state.

Export through `ReplayExport.canonicalText` using stable product encoders. The same recording must produce
the same bytes under .NET and Fable. Keep private or undisclosed authority state out of input, snapshot,
projection, audio, and export encoders.

```fsharp
let compatibility =
    { ContractVersion = 1
      EngineId = "arena"
      EngineVersion = "0.16.0"
      ProfileId = "browser/1"
      SchemaId = "arena"
      SchemaVersion = 1 }

let initialSnapshot =
    { SessionId = "session-1"
      Revision = 0UL
      Compatibility = compatibility
      Value = 0 }

let acceptedMove =
    { SessionId = "session-1"
      InputId = "player.move"
      Sequence = 1UL
      Value = 2 }

let recording =
    ReplayRecorder.create initialSnapshot "0"
    |> Result.bind (ReplayRecorder.appendInput acceptedMove "2")
    |> Result.bind (ReplayRecorder.appendAdvance 1UL "3")

let exported =
    recording
    |> Result.bind (ReplayExport.canonicalText string string)
```

Cancellation is a resumable boundary: retain the returned next-event index and last accepted state. Refuse a
wrong session, incompatible snapshot, stale input sequence, invalid checkpoint, missing digest, or target
beyond the recording before claiming playback.
