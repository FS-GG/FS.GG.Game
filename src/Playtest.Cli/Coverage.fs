/// The coverage lint — the completeness critic. An AC is covered iff some GP that cites it has an
/// `InputDriven` proof; a `Synthetic`/`Missing`/absent proof never covers (fail closed, #266).
module FS.GG.Playtest.Coverage

open FS.GG.Playtest.Manifest
open FS.GG.Playtest.Proofs

/// The result of linting a manifest against a proof report.
type LintReport =
    { /// Every AC number cited by the manifest (distinct, sorted).
      CitedAcs: int list
      /// Cited ACs with at least one `InputDriven`-proven covering GP.
      CoveredAcs: int list
      /// Cited ACs with no `InputDriven`-proven covering GP — the failure set.
      UncoveredAcs: int list
      /// When a spec is supplied: its §14 ACs that no GP in the manifest cites (advisory gap).
      SpecGap: int list }

/// Lint `manifest` against `proofs`. `specAcs` (optional) enables the advisory completeness gap.
let lint (manifest: GameplayFr list) (proofs: Map<string, Provenance>) (specAcs: int list option) : LintReport =
    let provenanceOf (gp: string) =
        proofs |> Map.tryFind gp |> Option.defaultValue Missing

    let cited =
        manifest |> List.collect (fun fr -> fr.CoversAc) |> List.distinct |> List.sort

    let coveredSet =
        manifest
        |> List.filter (fun fr -> provenanceOf fr.Id = InputDriven)
        |> List.collect (fun fr -> fr.CoversAc)
        |> Set.ofList

    let covered = cited |> List.filter coveredSet.Contains
    let uncovered = cited |> List.filter (coveredSet.Contains >> not)

    let gap =
        match specAcs with
        | Some spec -> spec |> List.filter (fun a -> not (List.contains a cited)) |> List.distinct |> List.sort
        | None -> []

    { CitedAcs = cited
      CoveredAcs = covered
      UncoveredAcs = uncovered
      SpecGap = gap }

/// The lint passes iff every cited AC has an `InputDriven`-proven covering GP.
let passed (report: LintReport) : bool = List.isEmpty report.UncoveredAcs
