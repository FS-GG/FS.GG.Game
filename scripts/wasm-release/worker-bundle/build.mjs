import { build, version } from "esbuild";
import { readFile, writeFile, realpath, lstat } from "node:fs/promises";
import { createHash } from "node:crypto";
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { validateClosedModule } from "./closed-module.mjs";

const here=path.dirname(fileURLToPath(import.meta.url)),repo=await realpath(process.argv[3]??"");
const generated=await realpath(process.argv[2]??"");
if(generated===repo||generated.startsWith(repo+path.sep))throw new Error("Worker generation requires a private root outside source");
if(version!=="0.28.2"||process.platform!=="linux"||process.arch!=="x64")throw new Error("unselected Worker build tool/profile");
if(process.env.ESBUILD_BINARY_PATH)throw new Error("Worker build refuses a native tool override");
const sha=value=>createHash("sha256").update(value).digest("hex");
const regular=async (file,maximum=8*1024*1024)=>{
  const info=await lstat(file);
  if(await realpath(file)!==file||!info.isFile()||info.size>maximum)throw new Error("linked/nonregular/oversized Worker build input");
  const bytes=await readFile(file);
  if(bytes.length!==info.size||bytes.length>maximum)throw new Error("Worker input byte bound changed");
  return bytes;
};
for(const leaf of ["package.json","package-lock.json","build.mjs","closed-module.mjs","closed-module.test.mjs"])
  if(!(await regular(path.join(here,leaf))).equals(await regular(path.join(repo,"scripts/wasm-release/worker-bundle",leaf))))
    throw new Error("isolated Worker tool differs from selected source");
const entryPath=path.join(repo,"src/Wasm.Browser/js/module-worker.mjs"),entry=await regular(entryPath);
const entryInput=path.join(generated,"module-worker.entry.mjs");
await writeFile(entryInput,entry,{flag:"wx"});
// The native bundler receives these exact captured buffers on both builds.
// Hashing filenames only after compilation would not establish that join.
const snapshots=new Map();let capturedBytes=0;
const snapshot=file=>{
  if(!snapshots.has(file)){
    if(file!==entryInput&&(!file.startsWith(generated+path.sep)||!file.endsWith(".js")))throw new Error("Worker dependency escaped the selected generated tree");
    if(snapshots.size>=256)throw new Error("Worker input population bound exceeded");
    snapshots.set(file,(async()=>{
      const bytes=await regular(file);capturedBytes+=bytes.length;
      if(capturedBytes>32*1024*1024)throw new Error("Worker input closure byte bound exceeded");
      new TextDecoder("utf-8",{fatal:true}).decode(bytes);
      return bytes;
    })());
  }
  return snapshots.get(file);
};
const options={absWorkingDir:generated,entryPoints:["module-worker.entry.mjs"],
  outfile:"module-worker.mjs",bundle:true,format:"esm",platform:"browser",target:"es2022",splitting:false,
  minify:false,treeShaking:false,ignoreAnnotations:true,keepNames:true,charset:"utf8",legalComments:"inline",
  sourcemap:false,metafile:true,write:false,logLevel:"error",
  logOverride:{"direct-eval":"error","unsupported-dynamic-import":"error"},
  plugins:[{name:"actual-generated-worker-policy",setup(api){
    api.onResolve({filter:/^\.\/policy\/WorkerEntry\.js$/},args=>{
      if(args.importer!==entryInput)return {errors:[{text:"unexpected mechanical policy import owner"}]};
      return {path:path.join(generated,"WorkerEntry.js")};
    });
    api.onLoad({filter:/.*/,namespace:"file"},async args=>({contents:await snapshot(args.path),loader:"js",resolveDir:path.dirname(args.path)}));
  }}]};
const first=await build(options),second=await build(options);
if(first.outputFiles.length!==1||second.outputFiles.length!==1
   ||first.outputFiles[0].path!==path.join(generated,"module-worker.mjs")||second.outputFiles[0].path!==first.outputFiles[0].path
   ||!Buffer.from(first.outputFiles[0].contents).equals(Buffer.from(second.outputFiles[0].contents)))
  throw new Error("Worker bundle is not a deterministic single artifact");
if(JSON.stringify(Object.keys(first.metafile.inputs).sort())!==JSON.stringify(Object.keys(second.metafile.inputs).sort())
   ||Object.keys(first.metafile.inputs).length!==snapshots.size)throw new Error("Worker compilation input census differs");
const output=Buffer.from(first.outputFiles[0].contents);
if(output.length>8*1024*1024)throw new Error("Worker artifact byte bound exceeded");
const syntax=validateClosedModule(output.toString("utf8"));
if(Object.values(first.metafile.outputs).some(row=>row.imports.length))throw new Error("Worker bundle retains external imports");
const inputs=[];let total=entry.length;
for(const name of Object.keys(first.metafile.inputs).sort()){
  if(name==="module-worker.entry.mjs")continue;
  if(path.isAbsolute(name)||name.split(/[\\/]/).includes(".."))throw new Error("Worker input escaped generated root");
  const file=path.join(generated,name),bytes=await snapshots.get(file);
  if(!bytes||!bytes.equals(await regular(file)))throw new Error("Worker input changed after captured compilation");
  if(first.metafile.inputs[name].bytes!==bytes.length||second.metafile.inputs[name].bytes!==bytes.length)
    throw new Error("Native bundler input length differs from the captured bytes");
  total+=bytes.length;
  if(total>32*1024*1024||inputs.length>=256)throw new Error("Worker input closure bound exceeded");
  inputs.push({path:name,bytes:bytes.length,sha256:sha(bytes)});
}
for(const leaf of ["WorkerEntry.js","Admission.js","Invocation.js","RuntimeProtocol.js"])
  if(!inputs.some(row=>row.path===leaf))throw new Error("Worker bundle omitted authoritative policy input");
if(!inputs.some(row=>row.path.endsWith("/Contracts.js")))throw new Error("Worker bundle omitted authoritative contracts");
if(!entry.equals(await snapshots.get(entryInput))||!entry.equals(await regular(entryInput)))throw new Error("Worker mechanical input correspondence differs");
const require=createRequire(import.meta.url),binary=require.resolve("@esbuild/linux-x64/bin/esbuild");
const parser=JSON.parse(await readFile(require.resolve("acorn/package.json"),"utf8"));
const scopeAnalyzer=JSON.parse(await readFile(require.resolve("eslint-scope/package.json"),"utf8"));
if(parser.version!=="8.19.0"||scopeAnalyzer.version!=="9.1.2")throw new Error("unselected Worker syntax/scope tools");
const git=(...args)=>execFileSync("git",["-C",repo,...args],{encoding:"utf8"}).trim();
const manifest={schema:"fsgg.wasm.worker-build/1",sourceCommit:git("rev-parse","HEAD"),sourceTree:git("rev-parse","HEAD^{tree}"),
  tool:{name:"esbuild",version,binarySha256:sha(await regular(binary,16*1024*1024)),lockSha256:sha(await regular(path.join(here,"package-lock.json")))},
  syntaxParser:{name:"acorn",version:parser.version},scopeAnalyzer:{name:"eslint-scope",version:scopeAnalyzer.version},fableVersion:"5.18.0",
  mechanicalEntry:{path:"src/Wasm.Browser/js/module-worker.mjs",bytes:entry.length,sha256:sha(entry)},generatedInputs:inputs,
  output:{path:"module-worker.mjs",bytes:output.length,sha256:sha(output)},
  composition:{format:"esm",platform:"browser",target:"es2022",minify:false,treeShaking:false,keepNames:true,
    deterministicBuildsCompared:2,outputFiles:1,externalImports:syntax.externalImports}};
await writeFile(path.join(generated,"module-worker.mjs"),output,{flag:"wx"});
await writeFile(path.join(generated,"worker-build.json"),JSON.stringify(manifest,null,2)+"\n",{flag:"wx"});
console.log(JSON.stringify({workerBundle:manifest.output,generatedInputs:inputs.length,tool:manifest.tool,externalImports:0}));
