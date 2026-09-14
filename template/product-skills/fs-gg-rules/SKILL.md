---
name: fs-gg-rules
description: Use when authoring explainable tactical rules and causal effects with FS.GG.Game.Core.
---

# Explainable tactical rules

Put rule meaning in the Game-owned `RuleCatalog`; keep SVG elements, inspector panels, and animations as
projections of that meaning. Each rule needs stable metadata, a pure evaluator, a public explanation, and the
formal-evidence identity that was checked for the shipped ruleset.

Construct the catalog with `RuleCatalog.create`. Duplicate ids, missing titles or explanations, and missing
formal evidence are authoring failures. Evaluate with `RuleCatalog.evaluate` and show the returned
`RuleInspection` to the player. Apply effects through the same authoritative session input path used by
ordinary play so inspection, replay, networking, and live outcomes cannot acquire separate rule semantics.

```fsharp
type Facts = { Energy: int }
type Effect = SpendEnergy

let evidence =
    { ModelId = "arena-rules/1"
      ModelSha256 = "0123456789abcdef"
      Tool = "quint"
      ToolVersion = "0.32.0"
      Invariants = [ "energyNeverNegative" ]
      ImplementationBinding = "Arena.Rules/v1" }

let spend =
    { Metadata =
        { Id = "energy.spend"
          Version = 1
          Title = "Spend energy"
          Summary = "A move costs one energy."
          DependsOn = [] }
      Evaluate = fun facts ->
        { RuleId = "energy.spend"
          Applies = facts.Energy > 0
          Explanation = "A move requires available energy."
          Causes = [ { Code = "energy"; Message = string facts.Energy } ]
          Effects = if facts.Energy > 0 then [ SpendEnergy ] else [] } }

let inspection =
    match RuleCatalog.create evidence [ spend ] with
    | Ok catalog -> RuleCatalog.inspect "energy.spend" { Energy = 2 } catalog
    | Error issues -> failwithf "invalid rules: %A" issues
```

The tactical bundle may map route cells, overlays, and planning intents to rules, but authored adapters do not
copy the rule engine. Do not expose hidden facts in the scene, inspector, replay, audio, or exported content.
When a rule changes, update its literate authority and bindings, rerun the formal obligations, and compare the
first changed causal effect before accepting the new catalog.
