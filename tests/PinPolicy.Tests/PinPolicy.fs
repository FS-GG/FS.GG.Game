namespace FS.GG.Game.PinPolicy

open System
open System.Xml.Linq

/// Source-only candidate. Its decisions are not wired to the live Bash/Python gate.
module PinPolicy =
    let private trains =
        [ "FS.GG.UI",
          [ "FS.GG.UI.Scene"; "FS.GG.UI.KeyboardInput"; "FS.GG.UI.Canvas"
            "FS.GG.UI.Controls.Elmish"; "FS.GG.UI.SkiaViewer"
            "FS.GG.UI.Symbology"; "FS.GG.UI.Template" ]
          "FS.GG.Audio", [ "FS.GG.Audio.Core"; "FS.GG.Audio.Host" ] ]

    let private eq (left: string) (right: string) =
        String.Equals(left, right, StringComparison.OrdinalIgnoreCase)

    let private attr (name: string) (element: XElement) =
        let found = element.Attribute(XName.Get name)
        match found with
        | null -> None
        | value -> Some value.Value

    /// Parse central props bytes as XML and reduce to an exact pin roster verdict.
    let evaluate (xml: string) : Result<unit, string list> =
        try
            let doc = XDocument.Parse(xml)
            let root = doc.Root |> Option.ofObj |> Option.get
            // The installed XML oracle accepts only the literal, unnamespaced
            // MSBuild Project root. LocalName alone admits a foreign namespace
            // while unnamespaced PackageVersion children still satisfy the roster.
            if root.Name <> XName.Get "Project" then
                Error [ "missing Project root" ]
            else
                let rows =
                    doc.Descendants(XName.Get "PackageVersion")
                    |> Seq.choose (fun element ->
                        attr "Include" element
                        |> Option.map (fun identity -> identity, defaultArg (attr "Version" element) "", element))
                    |> Seq.toList
                let errors = ResizeArray<string>()
                let seen = System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                for identity, _, element in rows do
                    if seen.ContainsKey identity then
                        let kind = if seen[identity] = identity then "duplicate" else "case collision"
                        errors.Add(sprintf "%s pin %s" kind identity)
                    else
                        seen.Add(identity, identity)
                    if trains |> List.exists (fun (family, _) -> identity.StartsWith(family + ".", StringComparison.OrdinalIgnoreCase)) then
                        if element.AncestorsAndSelf() |> Seq.exists (fun node -> Option.isSome (attr "Condition" node)) then
                            errors.Add(sprintf "conditional pin %s" identity)
                for family, roster in trains do
                    let actual = rows |> List.filter (fun (identity, _, _) -> identity.StartsWith(family + ".", StringComparison.OrdinalIgnoreCase))
                    for required in roster do
                        if not (actual |> List.exists (fun (identity, _, _) -> eq identity required)) then
                            errors.Add(sprintf "missing pin %s" required)
                    for identity, version, _ in actual do
                        if not (roster |> List.exists (eq identity)) then
                            errors.Add(sprintf "unexpected pin %s" identity)
                        if String.IsNullOrWhiteSpace version then
                            errors.Add(sprintf "missing version %s" identity)
                    let versions = actual |> List.map (fun (_, version, _) -> version) |> List.distinct
                    if versions.Length > 1 then
                        errors.Add(sprintf "incoherent versions in %s" family)
                if errors.Count = 0 then Ok () else Error (List.ofSeq errors)
        with :? System.Xml.XmlException as error -> Error [ sprintf "malformed XML: %s" error.Message ]
