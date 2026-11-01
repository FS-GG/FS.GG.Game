#!/usr/bin/env python3
"""The packed Fable views of FS.GG.Game.Render and FS.GG.Game.Render.Browser, derived from the source tree (Game#745).

Fable compiles a package from the project file packed under fable/, and reads that file without evaluating MSBuild
conditions. So, for each package:

  * the view project has no Condition attribute on any element (parsed as XML, however it is spaced);
  * its Compile list is DERIVED from the source tree, never restated here:
      - FS.GG.Game.Render: src/Game.Render/Fable/FS.GG.Game.Render.fsproj, each item's leading ../ dropped;
      - FS.GG.Game.Render.Browser: the two generated bindings, then src/Render.Browser's own Compile items in order;
    a module added to a host and not to its packed view is refused, whatever the count;
  * with packages: the packed project lists exactly that, each listed source is in the package beside it and is the
    source tree's bytes, and the generated bindings are the bytes eng/js-bindings/pin.env pins.

usage: check-fable-views.py --root <repo> [--render <nupkg> --browser <nupkg>]
"""
import argparse
from hashlib import sha256
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
from zipfile import ZipFile

BINDINGS = ["generated/BabylonBindings.fs", "generated/PixiBindings.fs"]


class Refusal(Exception):
    pass


def local(tag):
    return tag.rsplit("}", 1)[-1]


def parse(text, label):
    try:
        return ET.fromstring(text)
    except ET.ParseError as error:
        raise Refusal(f"{label}: not well-formed XML ({error})")


def unconditional_compile_list(text, label):
    """The Compile items of a project Fable reads as-is: refuses any Condition attribute anywhere."""
    project = parse(text, label)
    for element in project.iter():
        for name in element.attrib:
            if local(name) == "Condition":
                raise Refusal(f"{label}: <{local(element.tag)}> has a Condition attribute; Fable ignores MSBuild "
                              "conditions in a package's project file")
    listed = [e.attrib.get("Include", "") for e in project.iter() if local(e.tag) == "Compile"]
    if not listed or any(not item for item in listed):
        raise Refusal(f"{label}: lists no sources, or a Compile item without Include")
    return listed


def host_compile_list(text, label):
    """A host project's own Compile items, in order. A conditional source could not be packed faithfully."""
    project = parse(text, label)
    for group in project.iter():
        if local(group.tag) == "ItemGroup" and any(local(n) == "Condition" for n in group.attrib) and any(
                local(child.tag) == "Compile" for child in group):
            raise Refusal(f"{label}: a conditional item group of Compile items cannot be packed faithfully")
    listed = []
    for element in project.iter():
        if local(element.tag) == "Compile":
            if any(local(name) == "Condition" for name in element.attrib):
                raise Refusal(f"{label}: a conditional Compile item cannot be packed faithfully")
            listed.append(element.attrib["Include"])
    if not listed:
        raise Refusal(f"{label}: lists no sources")
    return listed


def difference(expected, listed):
    missing = [item for item in expected if item not in listed]
    extra = [item for item in listed if item not in expected]
    if not missing and not extra:
        return f"same sources in a different order; expected {expected}, listed {listed}"
    return f"missing {missing}, unexpected {extra}"


def expected_views(root):
    render_view = root / "src/Game.Render/Fable/FS.GG.Game.Render.fsproj"
    render_items = unconditional_compile_list(render_view.read_text(encoding="utf-8"), str(render_view.relative_to(root)))
    if any(not item.startswith("../") for item in render_items):
        raise Refusal("src/Game.Render/Fable/FS.GG.Game.Render.fsproj: every Compile item must name a ../ source")
    render = [item[len("../"):] for item in render_items]

    host = root / "src/Render.Browser/FS.GG.Game.Render.Browser.fsproj"
    browser = BINDINGS + host_compile_list(host.read_text(encoding="utf-8"), str(host.relative_to(root)))

    # The committed packed project of the browser host must already say the same (source-only preflight).
    packed = root / "src/Render.Browser/Fable/FS.GG.Game.Render.Browser.fsproj"
    committed = unconditional_compile_list(packed.read_text(encoding="utf-8"), str(packed.relative_to(root)))
    if committed != browser:
        raise Refusal("src/Render.Browser/Fable/FS.GG.Game.Render.Browser.fsproj does not list the browser host's "
                      "sources (bindings, then src/Render.Browser's Compile items in order): " + difference(browser, committed))
    return render, browser


def pins(root):
    text = (root / "eng/js-bindings/pin.env").read_text(encoding="utf-8")
    return dict(line.split("=", 1) for line in text.splitlines() if re.match(r"^[A-Z0-9_]+=", line))


def check_package(nupkg, package, expected, source_dir, root):
    with ZipFile(nupkg) as archive:
        files = {name: archive.read(name) for name in archive.namelist()}
    project = f"fable/{package}.fsproj"
    if project not in files:
        raise Refusal(f"{nupkg.name}: no {project}")
    listed = unconditional_compile_list(files[project].decode("utf-8-sig"), f"{nupkg.name}:{project}")
    if listed != expected:
        raise Refusal(f"{nupkg.name}: the packed Fable project does not list what the source tree derives: "
                      + difference(expected, listed))
    packed_sources = sorted(name for name in files if name.startswith("fable/") and re.search(r"\.fsi?$", name))
    if packed_sources != sorted("fable/" + item for item in expected):
        raise Refusal(f"{nupkg.name}: fable/ carries {packed_sources}, not exactly the listed sources")
    pin = pins(root)
    for item in expected:
        data = files["fable/" + item]
        if item in BINDINGS:
            corpus = Path(item).stem.removesuffix("Bindings").upper()
            if sha256(data).hexdigest() != pin[f"{corpus}_BINDINGS_SHA256"]:
                raise Refusal(f"{nupkg.name}: fable/{item} is not the pinned bindings bytes")
        elif data != (source_dir / item).read_bytes():
            raise Refusal(f"{nupkg.name}: fable/{item} differs from {source_dir.relative_to(root)}/{item}")
    return len(listed)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--render", type=Path)
    parser.add_argument("--browser", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        render, browser = expected_views(root)
        if bool(args.render) != bool(args.browser):
            raise Refusal("give both --render and --browser, or neither")
        if args.render:
            r = check_package(args.render, "FS.GG.Game.Render", render, root / "src/Game.Render", root)
            b = check_package(args.browser, "FS.GG.Game.Render.Browser", browser, root / "src/Render.Browser", root)
            print(f"fable views: packages match the source tree (FS.GG.Game.Render {r} sources, "
                  f"FS.GG.Game.Render.Browser {b} sources, bindings pinned, no conditions)")
        else:
            print(f"fable views: source tree consistent (FS.GG.Game.Render {len(render)} sources, "
                  f"FS.GG.Game.Render.Browser {len(browser)} sources, no conditions)")
    except Refusal as refusal:
        print(f"fable views refused: {refusal}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
