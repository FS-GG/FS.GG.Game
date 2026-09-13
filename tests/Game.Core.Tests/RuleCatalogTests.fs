namespace Game.Core.Tests

open Expecto
open FS.GG.Game.Core

module RuleCatalogTests =
    type Facts={Energy:int;DoorOpen:bool}
    type Effect=SpendEnergy | EnterDoor
    let private evidence =
        {ModelId="energy-rules/1";ModelSha256="model-sha";Tool="quint";ToolVersion="0.32.0"
         Invariants=["energyNeverNegative";"doorRequiresEnergy"];ImplementationBinding="RuleCatalogTests.energyRules/v1"}
    let private rule id dependencies evaluate =
        {Metadata={Id=id;Version=1;Title=id;Summary=id;DependsOn=dependencies};Evaluate=evaluate}
    let private energyRules =
        [ rule "energy.available" [] (fun facts ->
            {RuleId="energy.available";Applies=facts.Energy>0;Explanation=(if facts.Energy>0 then "energy is available" else "energy is exhausted")
             Causes=[{Code="energy";Message=string facts.Energy}];Effects=(if facts.Energy>0 then [SpendEnergy] else [])})
          rule "door.enter" ["energy.available"] (fun facts ->
            {RuleId="door.enter";Applies=not facts.DoorOpen;Explanation="a closed door may be entered by spending energy"
             Causes=[{Code="door";Message=(if facts.DoorOpen then "open" else "closed")}];Effects=[EnterDoor]}) ]
    let private success = function Ok value -> value | Error error -> failtestf "expected success: %A" error

    [<Tests>]
    let tests=testList "generic rule catalog" [
        testCase "dependencies are inspected before the requested rule" <| fun _ ->
            let catalog=RuleCatalog.create evidence energyRules |> success
            let inspection=RuleCatalog.inspect "door.enter" {Energy=2;DoorOpen=false} catalog |> success
            Expect.equal (inspection.Evaluations |> List.map _.RuleId) ["energy.available";"door.enter"] "dependency order is stable"
            Expect.isTrue inspection.Applies "all required rules apply"
            Expect.equal inspection.Evaluations.Head.Causes.Head.Code "energy" "causes remain inspectable"

        testCase "a failed dependency prevents the aggregate rule" <| fun _ ->
            let catalog=RuleCatalog.create evidence energyRules |> success
            let inspection=RuleCatalog.inspect "door.enter" {Energy=0;DoorOpen=false} catalog |> success
            Expect.isFalse inspection.Applies "dependency failure is visible"
            Expect.equal inspection.Evaluations.Head.Explanation "energy is exhausted" "the causal explanation is retained"

        testCase "unknown dependencies and cycles fail catalog construction" <| fun _ ->
            let unknown=rule "bad" ["missing"] (fun _ -> {RuleId="bad";Applies=true;Explanation="";Causes=[];Effects=[]})
            Expect.contains (RuleCatalog.create evidence [unknown] |> function Error x -> x | Ok _ -> []) (RuleCatalogIssue.UnknownDependency("bad","missing")) "unknown dependency is refused"
            let a=rule "a" ["b"] (fun _ -> {RuleId="a";Applies=true;Explanation="";Causes=[];Effects=[]})
            let b=rule "b" ["a"] (fun _ -> {RuleId="b";Applies=true;Explanation="";Causes=[];Effects=[]})
            Expect.isError (RuleCatalog.create evidence [a;b]) "cycles are refused"

        testCase "evaluation cannot impersonate another rule" <| fun _ ->
            let bad=rule "one" [] (fun _ -> {RuleId="other";Applies=true;Explanation="";Causes=[];Effects=[]})
            let catalog=RuleCatalog.create evidence [bad] |> success
            Expect.equal (RuleCatalog.inspect "one" () catalog) (Error(RuleCatalogIssue.EvaluationRuleMismatch("one","other"))) "metadata and implementation stay bound"
    ]
