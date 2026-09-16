namespace FS.GG.Game.Core

type RuleMetadata =
    {
        Id: string
        Version: int
        Title: string
        Summary: string
        DependsOn: string list
    }

type RuleFormalEvidence =
    {
        ModelId: string
        ModelSha256: string
        Tool: string
        ToolVersion: string
        Invariants: string list
        ImplementationBinding: string
    }

type RuleCause = { Code: string; Message: string }

type RuleEvaluation<'effect> =
    {
        RuleId: string
        Applies: bool
        Explanation: string
        Causes: RuleCause list
        Effects: 'effect list
    }

type RuleDefinition<'facts, 'effect> =
    {
        Metadata: RuleMetadata
        Evaluate: 'facts -> RuleEvaluation<'effect>
    }

type RuleCatalog<'facts, 'effect> = private RuleCatalog of RuleFormalEvidence * RuleDefinition<'facts, 'effect> list

[<RequireQualifiedAccess>]
type RuleCatalogIssue =
    | MissingRuleId
    | InvalidRuleVersion of ruleId: string * version: int
    | MissingTitle of ruleId: string
    | DuplicateRuleId of string
    | UnknownDependency of ruleId: string * dependencyId: string
    | SelfDependency of string
    | DependencyCycle of string list
    | MissingModelId
    | MissingModelSha256
    | MissingImplementationBinding
    | MissingInvariant
    | EvaluationRuleMismatch of expected: string * actual: string

type RuleInspection<'effect> =
    {
        RequestedRuleId: string
        Evaluations: RuleEvaluation<'effect> list
        Applies: bool
    }

[<RequireQualifiedAccess>]
module RuleCatalog =
    let private missing value = System.String.IsNullOrWhiteSpace value

    let private cycle rules =
        let dependencies id =
            rules
            |> List.tryFind (fun rule -> rule.Metadata.Id = id)
            |> Option.map (fun rule -> rule.Metadata.DependsOn)
            |> Option.defaultValue []

        let rec visit path visited id =
            if List.contains id path then
                Some(List.rev (id :: path))
            elif Set.contains id visited then
                None
            else
                dependencies id |> List.tryPick (visit (id :: path) (Set.add id visited))

        rules |> List.tryPick (fun rule -> visit [] Set.empty rule.Metadata.Id)

    let create evidence rules =
        let ids = rules |> List.map _.Metadata.Id
        let known = Set.ofList ids

        let duplicateIds =
            ids
            |> List.countBy id
            |> List.choose (fun (id, count) -> if count > 1 then Some id else None)

        let issues =
            [
                for rule in rules do
                    if missing rule.Metadata.Id then
                        RuleCatalogIssue.MissingRuleId

                    if rule.Metadata.Version < 1 then
                        RuleCatalogIssue.InvalidRuleVersion(rule.Metadata.Id, rule.Metadata.Version)

                    if missing rule.Metadata.Title then
                        RuleCatalogIssue.MissingTitle rule.Metadata.Id

                    for dependency in rule.Metadata.DependsOn do
                        if dependency = rule.Metadata.Id then
                            RuleCatalogIssue.SelfDependency rule.Metadata.Id
                        elif not (Set.contains dependency known) then
                            RuleCatalogIssue.UnknownDependency(rule.Metadata.Id, dependency)
                for id in duplicateIds do
                    RuleCatalogIssue.DuplicateRuleId id
                if missing evidence.ModelId then
                    RuleCatalogIssue.MissingModelId
                if missing evidence.ModelSha256 then
                    RuleCatalogIssue.MissingModelSha256
                if missing evidence.ImplementationBinding then
                    RuleCatalogIssue.MissingImplementationBinding
                if evidence.Invariants.IsEmpty || evidence.Invariants |> List.exists missing then
                    RuleCatalogIssue.MissingInvariant
                match cycle rules with
                | Some ids -> RuleCatalogIssue.DependencyCycle ids
                | None -> ()
            ]

        if issues.IsEmpty then
            Ok(RuleCatalog(evidence, rules))
        else
            Error issues

    let evidence (RuleCatalog(evidence, _)) = evidence
    let metadata (RuleCatalog(_, rules)) = rules |> List.map _.Metadata

    let inspect ruleId facts (RuleCatalog(_, rules)) =
        let find id =
            rules |> List.tryFind (fun rule -> rule.Metadata.Id = id)

        match find ruleId with
        | None -> Error(RuleCatalogIssue.UnknownDependency("<inspection>", ruleId))
        | Some _ ->
            let rec ordered seen id =
                if Set.contains id seen then
                    seen, []
                else
                    let rule = find id |> Option.get

                    let seen, dependencies =
                        rule.Metadata.DependsOn
                        |> List.fold
                            (fun (visited, acc) dependency ->
                                let nextVisited, next = ordered visited dependency
                                nextVisited, acc @ next)
                            (Set.add id seen, [])

                    seen, dependencies @ [ rule ]

            let _, orderedRules = ordered Set.empty ruleId
            let evaluations = orderedRules |> List.map (fun rule -> rule, rule.Evaluate facts)

            match
                evaluations
                |> List.tryPick (fun (rule, result) ->
                    if result.RuleId = rule.Metadata.Id then
                        None
                    else
                        Some(rule.Metadata.Id, result.RuleId))
            with
            | Some(expected, actual) -> Error(RuleCatalogIssue.EvaluationRuleMismatch(expected, actual))
            | None ->
                let results = evaluations |> List.map snd

                Ok
                    {
                        RequestedRuleId = ruleId
                        Evaluations = results
                        Applies = results |> List.forall _.Applies
                    }
