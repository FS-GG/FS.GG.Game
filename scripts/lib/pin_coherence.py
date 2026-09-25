"""Validate the exact Game release-train roster in the central XML props file.

The XML reader ignores comments and accepts attribute order. This is the live
gate behind check-pin-coherence.sh; any later F# candidate must retain its
refusal cases before receiver adoption.
"""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path


FAMILIES = {
    "FS.GG.UI": (
        "FS.GG.UI.Scene",
        "FS.GG.UI.KeyboardInput",
        "FS.GG.UI.Canvas",
        "FS.GG.UI.Controls.Elmish",
        "FS.GG.UI.SkiaViewer",
        "FS.GG.UI.Symbology",
        "FS.GG.UI.Template",
    ),
    "FS.GG.Audio": ("FS.GG.Audio.Core", "FS.GG.Audio.Host"),
}


def fail(message: str) -> None:
    print(f"::error::check-pin-coherence: {message}")
    print(f"check-pin-coherence: {message}", file=sys.stderr)


def check(path: Path) -> int:
    if not path.is_file():
        fail(f"{path} not found")
        return 1
    try:
        tree = ET.parse(path)
    except ET.ParseError as exc:
        fail(f"{path} malformed XML: {exc}")
        return 1
    if tree.getroot().tag != "Project":
        fail(f"{path} has no Project root")
        return 1

    parents = {child: parent for parent in tree.iter() for child in parent}
    rows: dict[str, tuple[str, str]] = {}
    errors = False
    for element in tree.iter("PackageVersion"):
        identity = element.get("Include")
        if not identity:
            continue
        folded = identity.casefold()
        if any(folded.startswith(family.casefold() + ".") for family in FAMILIES):
            current = element
            while current is not None:
                if "Condition" in current.attrib:
                    fail(f"conditional PackageVersion identity {identity} cannot prove an active pin")
                    errors = True
                    break
                current = parents.get(current)
        if folded in rows:
            previous = rows[folded][0]
            kind = "duplicate" if identity == previous else "case collision"
            fail(f"{kind} PackageVersion identity: {previous} / {identity}")
            errors = True
        else:
            rows[folded] = (identity, element.get("Version") or "")

    for family, roster in FAMILIES.items():
        prefix = family.casefold() + "."
        actual = {key for key in rows if key.startswith(prefix)}
        expected = {item.casefold() for item in roster}
        for identity in roster:
            if identity.casefold() not in actual:
                fail(f"{family} missing required pin {identity}; FOUND {len(actual)} pin(s), expected {len(roster)}")
                errors = True
        for key in sorted(actual - expected):
            fail(f"{family} unexpected pin {rows[key][0]}")
            errors = True
        versions = {rows[key][1] for key in actual}
        if "" in versions:
            fail(f"{family} pin has no Version")
            errors = True
        if len(versions) > 1:
            fail(f"{family} release train is INCOHERENT ({len(versions)} versions): "
                 + ", ".join(f"{rows[key][0]}={rows[key][1]}" for key in sorted(actual)))
            errors = True
        if actual == expected and len(versions) == 1 and "" not in versions:
            print(f"check-pin-coherence: {family}.* — OK, {len(actual)} pin(s), all at {next(iter(versions))}")

    if errors:
        return 1
    print(f"check-pin-coherence: OK — every declared release train in {path} is on a single coherent version.")
    return 0


if __name__ == "__main__":
    sys.exit(check(Path(sys.argv[1])))
