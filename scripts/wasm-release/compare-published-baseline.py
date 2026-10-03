#!/usr/bin/env python3
"""Compare the single-packed candidates to actual published DLLs, without suppression."""
import argparse
import hashlib
import json
import os
import pathlib
import re
import subprocess
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
POLICY = json.loads((ROOT / "scripts/wasm-release/api-baseline-policy.json").read_text())


def classify(identity, returncode, log):
    errors = re.findall(r"error (CP[0-9]+): Member '([^']+)'", log)
    all_codes = re.findall(r"error ([A-Z]+[0-9]+):", log)
    expected = POLICY["packages"][identity]["removedConstructorTypes"]
    if not expected:
        assert returncode == 0 and not all_codes, "native baseline comparison did not pass"
        return {"status": "compatible", "diagnostics": []}
    members = sorted(re.match(r"FS\.GG\.Wasm\.Browser\.(\w+)\.\1\(", member).group(1) for code, member in errors if code == "CP0002" and re.match(r"FS\.GG\.Wasm\.Browser\.(\w+)\.\1\(", member))
    assert returncode != 0 and members == expected and all_codes == ["CP0002"] * len(expected), "unexpected/indeterminate Browser baseline delta"
    assert "API breaking changes found" in log, "native tool did not establish a breaking comparison"
    return {"status": "breaking-intentionally-versioned", "diagnostics": [{"code": code, "member": member} for code, member in sorted(errors)]}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def surfaces(path):
    with zipfile.ZipFile(path) as archive:
        return {name: hashlib.sha256(archive.read(name)).hexdigest() for name in sorted(archive.namelist()) if name.startswith("api-surface/") and name.endswith(".fsi")}


def compare(custody, output):
    assert subprocess.check_output(["dotnet", "--version"], text=True).strip() == "10.0.401"
    candidates = {identity: custody / f"{identity}.{POLICY['candidateVersion']}.nupkg" for identity in POLICY["packages"]}
    assert all(path.is_file() for path in candidates.values()), "exact coherent candidate set absent"
    packages = pathlib.Path(os.environ.get("NUGET_PACKAGES", pathlib.Path.home() / ".nuget/packages"))
    core_root = packages / "fsharp.core/10.1.302"
    metadata = json.loads((core_root / ".nupkg.metadata").read_text())
    assert metadata["source"] == "https://api.nuget.org/v3/index.json", "ApiCompat reference must use official FSharp.Core"
    expected_core = json.loads((ROOT / "src/Wasm.Browser/packages.lock.json").read_text())["dependencies"]["net10.0"]["FSharp.Core"]["contentHash"]
    assert metadata["contentHash"] == expected_core, "ApiCompat FSharp.Core locked reference mismatch"
    core = core_root / "lib/netstandard2.0/FSharp.Core.dll"
    assert core.is_file(), "locked reference assembly missing"
    with urllib.request.urlopen("https://api.nuget.org/v3-flatcontainer/fsharp.core/10.1.302/fsharp.core.10.1.302.nupkg") as response:
        official_archive = response.read()
    assert hashlib.sha256(official_archive).hexdigest() == "f4eda1b2efb28b38a5526b0b678e889434aa71113a4c2fa8660b62ca75cc7dfc", "official reference archive mismatch"
    import io
    with zipfile.ZipFile(io.BytesIO(official_archive)) as archive:
        assert core.read_bytes() == archive.read("lib/netstandard2.0/FSharp.Core.dll"), "actual reference DLL differs from official archive"
    sdk_root = pathlib.Path(subprocess.check_output(["dotnet", "--list-sdks"], text=True).split("10.0.401 [", 1)[1].split("]", 1)[0]) / "10.0.401"
    task = sdk_root / "Sdks/Microsoft.NET.Sdk/tools/net10.0/Microsoft.DotNet.ApiCompat.Task.dll"
    framework = sorted((sdk_root.parents[1] / "packs/Microsoft.NETCore.App.Ref").glob("10.*/ref/net10.0"), key=lambda path: tuple(map(int, path.parents[1].name.split("."))))[-1]
    rows = []
    with tempfile.TemporaryDirectory(prefix="wasm-published-api-baseline.", dir=os.environ.get("RUNNER_TEMP", os.environ.get("TMPDIR"))) as raw:
        work = pathlib.Path(raw)
        contracts = work / "FS.GG.Wasm.Contracts.dll"
        with zipfile.ZipFile(candidates["FS.GG.Wasm.Contracts"]) as archive:
            contracts.write_bytes(archive.read("lib/net10.0/FS.GG.Wasm.Contracts.dll"))
        references = [*sorted(framework.glob("*.dll")), core, contracts]
        for identity, candidate in candidates.items():
            baseline = work / f"{identity}.{POLICY['baselineVersion']}.nupkg"
            url = f"https://api.nuget.org/v3-flatcontainer/{identity.lower()}/{POLICY['baselineVersion']}/{identity.lower()}.{POLICY['baselineVersion']}.nupkg"
            with urllib.request.urlopen(url) as response:
                baseline.write_bytes(response.read())
            assert sha(baseline) == POLICY["packages"][identity]["servedArchiveSha256"], "published baseline archive differs from accepted receipt"
            with zipfile.ZipFile(baseline) as archive:
                assert 'commit="' + POLICY["baselineSource"] + '"' in archive.read(identity + ".nuspec").decode(), "baseline package source mismatch"
            project = ET.Element("Project")
            ET.SubElement(project, "UsingTask", TaskName="Microsoft.DotNet.ApiCompat.Task.ValidatePackageTask", AssemblyFile=str(task))
            item = ET.SubElement(ET.SubElement(project, "ItemGroup"), "References", Include="net10.0")
            ET.SubElement(item, "TargetFrameworkMoniker").text = ".NETCoreApp,Version=v10.0"
            ET.SubElement(item, "ReferencePath").text = ",".join(map(str, references))
            target = ET.SubElement(project, "Target", Name="Compare")
            ET.SubElement(target, "Microsoft.DotNet.ApiCompat.Task.ValidatePackageTask", PackageTargetPath=str(candidate), BaselinePackageTargetPath=str(baseline), RuntimeGraph=str(sdk_root / "PortableRuntimeIdentifierGraph.json"), RoslynAssembliesPath=str(sdk_root / "Roslyn/bincore"), PackageAssemblyReferences="@(References)", RunApiCompat="true", GenerateSuppressionFile="false", EnableStrictModeForBaselineValidation="false")
            project_path = work / "compare.proj"
            ET.ElementTree(project).write(project_path, encoding="unicode")
            run = subprocess.run(["dotnet", "msbuild", str(project_path), "-t:Compare", "-nologo", "-m:1", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-verbosity:normal"], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            result = classify(identity, run.returncode, run.stdout)
            (custody / f"{identity}.published-baseline.log").write_text(run.stdout)
            rows.append({"id": identity, "baselineArchiveSha256": sha(baseline), "candidateArchiveSha256": sha(candidate), "baselineApiSurface": surfaces(baseline), "candidateApiSurface": surfaces(candidate), "nativeLogSha256": hashlib.sha256(run.stdout.encode()).hexdigest(), **result})
    receipt = {"schema": "fsgg.wasm.published-api-comparison/v1", "tool": "SDK10.0.401.ApiCompat", "baselineVersion": POLICY["baselineVersion"], "baselineSource": POLICY["baselineSource"], "baselineTree": POLICY["baselineTree"], "baselineManifestSha256": POLICY["baselineManifestSha256"], "candidateVersion": POLICY["candidateVersion"], "packages": rows}
    output.write_text(json.dumps(receipt, sort_keys=True, indent=2) + "\n")
    print(f"wasm-published-api-comparison: baseline={POLICY['baselineVersion']} Contracts=compatible Browser=compatible candidate={POLICY['candidateVersion']} suppressions=none")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--custody", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    compare(args.custody.resolve(), args.output)
