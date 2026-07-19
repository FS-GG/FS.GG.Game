module Game.Harness.Tests.TraceSyntheticTests

open Expecto
open FS.GG.Game.Harness
open Game.Harness.Tests.PongSim

[<Tests>]
let tests =
    testList
        "Trace/Synthetic"
        [ testCase "FR-006 a synthetic trace is identifiable as synthetic"
          <| fun _ ->
              let worlds = [ init (FS.GG.Game.Core.Rng.ofSeed 1UL); step (init (FS.GG.Game.Core.Rng.ofSeed 1UL)) 1.0 ]
              let t = Synthetic.trace Driver.identityFingerprint worlds
              Expect.isTrue (Trace.isSynthetic t) "a synthetic trace reports isSynthetic = true"
              Expect.equal (Trace.origin t) Origin.Synthetic "its origin is Synthetic"
              Expect.equal (List.length (Trace.frames t)) 2 "it carries a fingerprint per hand-built world"

          testCase "FR-006 an input-driven trace is NOT synthetic and is distinct in the API"
          <| fun _ ->
              let t = Driver.runScript playable Driver.identityFingerprint [ [ "w" ]; [ "s" ] ]
              Expect.isFalse (Trace.isSynthetic t) "a driven trace reports isSynthetic = false"
              Expect.equal (Trace.origin t) Origin.InputDriven "its origin is InputDriven"

          testCase "FR-006 the two provenances are distinct even with identical frames"
          <| fun _ ->
              // Build a synthetic trace from the very worlds a driven run visits: the frames are equal,
              // but the provenance still separates them — the label is not derived from the frames.
              let driven = Driver.runScript playable Driver.identityFingerprint [ [ "w" ]; [ "s" ] ]
              let synthetic = Synthetic.trace Driver.identityFingerprint (Trace.frames driven)
              Expect.isTrue (Trace.equalFrames driven synthetic) "the frame sequences are identical"
              Expect.notEqual (Trace.origin driven) (Trace.origin synthetic) "yet their origins differ"
              Expect.isFalse (Trace.isSynthetic driven) "the driven one stays non-synthetic"
              Expect.isTrue (Trace.isSynthetic synthetic) "the synthetic one stays synthetic" ]
