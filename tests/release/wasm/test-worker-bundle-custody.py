#!/usr/bin/env python3
"""Actual package-reader controls over synthetic bytes; no compiler/JS executes."""
import copy
import hashlib
import importlib.util
import json
import pathlib
import sys
import tempfile
import unittest
import zipfile

ROOT=pathlib.Path(__file__).resolve().parents[3]
sys.dont_write_bytecode=True
spec=importlib.util.spec_from_file_location("worker_manifest",ROOT/"scripts/wasm-release/release_manifest.py")
manifest=importlib.util.module_from_spec(spec);spec.loader.exec_module(manifest)
PREFIX="contentFiles/any/any/_content/FS.GG.Wasm.Browser/"
SOURCE="1"*40
sha=lambda value:hashlib.sha256(value).hexdigest()

def fixture():
    data={}
    for name in ("RuntimeProtocol","Admission","Invocation","WorkerEntry","Lifecycle","Host"):
        data["api-surface/"+name+".fsi"]=b"// synthetic API role, not compiled\n"
    data.update({"fable/js/wasm-primitives.js":b"// synthetic mechanical role\n",
        "fable/eng/wasm-shared/version.props":b"<WasmSharedVersion>0.5.0</WasmSharedVersion>",
        "fable/FS.GG.Wasm.Browser.fsproj":b'<Project><PackageReference Version="[$(WasmSharedVersion)]" /></Project>',
        "fable/js/module-worker.entry.mjs":(ROOT/"src/Wasm.Browser/js/module-worker.mjs").read_bytes(),
        "buildTransitive/FS.GG.Wasm.Browser.targets":b"<Project />",
        PREFIX+"module-worker.mjs":b"self.onmessage=()=>{}; // synthetic byte-join fixture only\n",
        PREFIX+"worker-client.mjs":b"// synthetic transport role\n",
        "build-provenance/worker-bundle/package-lock.json":(ROOT/"scripts/wasm-release/worker-bundle/package-lock.json").read_bytes()})
    inputs=[]
    for name in sorted(["WorkerEntry.js","Admission.js","Invocation.js","RuntimeProtocol.js","Wasm.Contracts/Contracts.js"]):
        value=b"// synthetic generated role: "+name.encode()+b"\n";data[PREFIX+"policy/"+name]=value
        inputs.append({"path":name,"bytes":len(value),"sha256":sha(value)})
    data["FS.GG.Wasm.Browser.nuspec"]=(f'<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>FS.GG.Wasm.Browser</id><version>0.5.0</version><repository commit="{SOURCE}" /><dependencies><dependency id="FS.GG.Wasm.Contracts" version="[0.5.0]" /></dependencies></metadata></package>').encode()
    bundle=data[PREFIX+"module-worker.mjs"];entry=data["fable/js/module-worker.entry.mjs"]
    record={"schema":"fsgg.wasm.worker-build/1","sourceCommit":SOURCE,"sourceTree":"2"*40,
        "tool":{"name":"esbuild","version":"0.28.2","binarySha256":"a"*64,"lockSha256":sha(data["build-provenance/worker-bundle/package-lock.json"])},
        "syntaxParser":{"name":"acorn","version":"8.19.0"},"scopeAnalyzer":{"name":"eslint-scope","version":"9.1.2"},"fableVersion":"5.18.0",
        "mechanicalEntry":{"path":"src/Wasm.Browser/js/module-worker.mjs","bytes":len(entry),"sha256":sha(entry)},
        "generatedInputs":inputs,"output":{"path":"module-worker.mjs","bytes":len(bundle),"sha256":sha(bundle)},
        "composition":{"format":"esm","platform":"browser","target":"es2022","minify":False,"treeShaking":False,"keepNames":True,
            "deterministicBuildsCompared":2,"outputFiles":1,"externalImports":0}}
    return data,record

def inspect(data,record):
    with tempfile.TemporaryDirectory(prefix="worker-custody-inert.") as raw:
        package=pathlib.Path(raw)/"FS.GG.Wasm.Browser.0.5.0.nupkg"
        with zipfile.ZipFile(package,"w") as archive:
            for name,value in data.items():archive.writestr(name,value)
            archive.writestr(PREFIX+"worker-build.json",json.dumps(record))
        return manifest.inspect_package(package,"FS.GG.Wasm.Browser","0.5.0",SOURCE)

class WorkerCustody(unittest.TestCase):
    def test_actual_reader_joins_all_selected_byte_roles(self):
        data,record=fixture();self.assertEqual(inspect(data,record)["id"],"FS.GG.Wasm.Browser")

    def test_changed_missing_and_renamed_bytes_refuse(self):
        for target in [PREFIX+"module-worker.mjs",PREFIX+"policy/WorkerEntry.js","fable/js/module-worker.entry.mjs","build-provenance/worker-bundle/package-lock.json"]:
            for mutation in ["changed","missing","renamed"]:
                with self.subTest(target=target,mutation=mutation):
                    data,record=fixture();value=data.pop(target)
                    if mutation=="changed":data[target]=value+b"changed"
                    if mutation=="renamed":data[target+".other"]=value
                    with self.assertRaises(SystemExit):inspect(data,record)

    def test_incomplete_or_false_build_metadata_refuses(self):
        for change in ["source","tool","external","split","minify","nondeterministic","duplicate","escape","negative","boolean","missingpolicy"]:
            with self.subTest(change=change):
                data,record=fixture()
                if change=="source":record["sourceCommit"]="3"*40
                if change=="tool":record["tool"]["version"]="unselected"
                if change=="external":record["composition"]["externalImports"]=1
                if change=="split":record["composition"]["outputFiles"]=2
                if change=="minify":record["composition"]["minify"]=True
                if change=="nondeterministic":record["composition"]["deterministicBuildsCompared"]=1
                if change=="duplicate":record["generatedInputs"].append(copy.deepcopy(record["generatedInputs"][0]))
                if change=="escape":record["generatedInputs"][0]["path"]="../escape.js"
                if change=="negative":record["generatedInputs"][0]["bytes"]=-1
                if change=="boolean":record["generatedInputs"][0]["bytes"]=True
                if change=="missingpolicy":record["generatedInputs"]=record["generatedInputs"][:-1]
                with self.assertRaises(SystemExit):inspect(data,record)

if __name__=="__main__":unittest.main()
