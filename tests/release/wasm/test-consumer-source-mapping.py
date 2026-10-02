#!/usr/bin/env python3
"""Exercise the production NuGet.Config generator against both real feed routes."""
import pathlib
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[3]
script = (root / "scripts/verify-wasm-package-consumer.sh").read_text()
begin = 'python3 - "$work/NuGet.Config" "$package_source" <<\'PY\'\n'
generator = script.split(begin, 1)[1].split("\nPY\n", 1)[0]

def validate(config, selected):
    sources = config.find("packageSources")
    assert sources.find("clear") is not None
    rows = {row.attrib["key"]: row.attrib["value"] for row in sources.findall("add")}
    assert len(set(url.rstrip("/") for url in rows.values())) == len(rows), "duplicate feed URLs"
    mappings = {row.attrib["key"]: {p.attrib["pattern"] for p in row.findall("package")} for row in config.find("packageSourceMapping")}
    assert rows["wasm"] == selected
    assert set(mappings) == set(rows)
    assert mappings["wasm"] == ({"FS.GG.Wasm.*", "FSharp.Core"} if len(rows) == 1 else {"FS.GG.Wasm.*"})
    if len(rows) == 2:
        assert rows["dependencies"] == "https://api.nuget.org/v3/index.json"
        assert mappings["dependencies"] == {"FSharp.Core"}

with tempfile.TemporaryDirectory(prefix="wasm-mapping-control.") as raw:
    target = pathlib.Path(raw) / "NuGet.Config"
    for selected in ("https://api.nuget.org/v3/index.json", "https://api.nuget.org/v3/index.json/", "https://nuget.pkg.github.com/FS-GG/index.json", "/tmp/custody"):
        subprocess.run(["python3", "-c", generator, str(target), selected], check=True)
        config = ET.parse(target).getroot()
        validate(config, selected)
    # The previously shipped duplicate-URL configuration must fail admission.
    bad = ET.fromstring('<configuration><packageSources><clear/><add key="wasm" value="https://api.nuget.org/v3/index.json"/><add key="dependencies" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="wasm"><package pattern="FS.GG.Wasm.*"/></packageSource><packageSource key="dependencies"><package pattern="FSharp.Core"/></packageSource></packageSourceMapping></configuration>')
    try:
        validate(bad, "https://api.nuget.org/v3/index.json")
    except AssertionError:
        pass
    else:
        raise SystemExit("duplicate public source mutation accepted")
print("wasm-consumer-source-mapping: public=pass org=pass duplicate-url=refused")
