import * as bridge from "./fable/Program.js";
import { createHostTransport } from "./_content/FS.GG.Wasm.Browser/worker-client.mjs";
const bytes = async name => new Uint8Array(await (await fetch(`./modules/${name}`)).arrayBuffer());
const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", value)), byte => byte.toString(16).padStart(2, "0")).join("");
async function session(artifact, prefix, outputCap = 65536, deadline = 250, inject = false) {
  const transport = createHostTransport(new URL("./_content/FS.GG.Wasm.Browser/", import.meta.url));
  const waiting = new Map(), results = [], posts=[];
  let ignored=0, host;
  const create=transport.CreateWorker;
  transport.CreateWorker=(name,receive)=>create(name,wire=>{
    if(inject){
      for(const wrong of [{...wire,Correlation:"wrong-correlation"},{...wire,Identity:{...wire.Identity,WorkerInstance:"wrong-worker"}},{...wire,Identity:{...wire.Identity,Generation:"99"}}]){
        const before=JSON.stringify(bridge.project(host)), deliveries=results.length;
        receive(wrong);
        if(before!==JSON.stringify(bridge.project(host)) || deliveries!==results.length)throw new Error("foreign observation changed host authority");
        ignored++;
      }
    }
    receive(wire);
  });
  const post=transport.PostCommand;
  transport.PostCommand=(name,command)=>{posts.push({name,Kind:command.Kind,request:command.Identity.Request});post(name,command);};
  host = bridge.createHost(prefix === "barc" ? "bar-protected" : "sc2-imported-strict", await digest(artifact), outputCap, deadline, transport, result => {
    results.push(result); const pending = waiting.get(result.Request); waiting.delete(result.Request); pending?.(result);
  });
  let request = 0;
  const send = (method, value, owner = "installed-worker", generation = "1") => {
    const id = String(++request), identity = {WorkerInstance: owner, Request: id, Generation: generation};
    return new Promise(resolve => { waiting.set(id, resolve); bridge[method](host, transport.Now(), identity, value); });
  };
  return { host, send, results, posts, transport,
    ignored:()=>ignored,
    prepare:async (artifact, transaction)=>{
      const hash=await digest(artifact), id=String(++request), identity={WorkerInstance:"candidate-worker",Request:id,Generation:"2"};
      return new Promise(resolve=>{waiting.set(id,resolve);bridge.prepare(host,transport.Now(),identity,transaction,1n,hash,artifact);});
    },
    dispose() { bridge.dispose(host, transport.Now()); },
    projection() { return bridge.project(host); }
  };
}
window.runGuest = async (name, prefix) => {
  const artifact = await bytes(name), current = await session(artifact, prefix);
  try {
    const loaded = await current.send("load", artifact);
    const initialized = await current.send("initialize", new Uint8Array([1,2,3,4]));
    const result = await current.send("invoke", new Uint8Array([1,2,3,4]));
    const stopped = await current.send("shutdown");
    return { loaded, initialized, state:result.State, diagnostic:result.Diagnostic, active:current.projection().ActiveWorker, result: {status: result.State === "succeeded" ? 0 : -1, output: Array.from(result.Output)},
      shutdown: {status: stopped.State === "succeeded" ? 0 : -1}, phases: [initialized.Phase,result.Phase,stopped.Phase], disposedBefore: current.projection().Disposed };
  } finally { current.dispose(); }
};
window.runDeadline = async name => {
  const artifact = await bytes(name), current = await session(artifact, "sc2c");
  try {
    await current.send("load", artifact);
    await current.send("initialize", new Uint8Array([1]));
    const result = await current.send("invoke", new Uint8Array([1]));
    return {error: result.Diagnostic, disposed: current.projection().ActiveWorker === ""};
  } finally { current.dispose(); current.dispose(); }
};
window.runCounterexample = async (mutation, cap = 65536) => {
  const artifact = await bytes("bar-conformance.wasm");
  function change(pattern, offset, value) {
    const candidates=[];
    for(let start=0;start<=artifact.length-pattern.length;start++) if(pattern.every((byte,index)=>artifact[start+index]===byte)) candidates.push(start);
    if(candidates.length!==1) throw new Error("counterexample pattern is not unique");
    artifact[candidates[0]+offset]=value;
  }
  if(mutation==="empty") change([0x20,0x02,0x41,0x04,0x6a,0x41,0x04,0x36,0x00,0x00],6,0);
  if(mutation==="unaligned") {
    change([0x20,0x02,0x41,0x04,0x6a,0x41,0x04,0x36,0x00,0x00],6,3);
    change([0x20,0x02,0x41,0x80,0x80,0xc0,0x80,0x00,0x36,0x00,0x00],3,0x81);
  }
  const current=await session(artifact,"barc",cap);
  try {
    const loaded=await current.send("load",artifact);
    const result=await current.send("initialize",new Uint8Array([8,1,18,1,65]));
    return {loaded:loaded.State, state:result.State, output:Array.from(result.Output), initialized:current.projection().InitializedWorkers, active:current.projection().ActiveWorker};
  } finally {current.dispose();}
};
window.packageConsumerReady = true;

window.runControl = async (name, mode, deadline = 250, inject = false) => {
  const artifact=await bytes(name), current=await session(artifact,"sc2c",65536,deadline);
  try {
    const loaded=await current.send("load",artifact);
    if(loaded.State!=="succeeded")return {loaded,active:current.projection().ActiveWorker};
    const initialized=await current.send("initialize",new Uint8Array([mode]));
    if(initialized.State!=="succeeded")return {loaded,initialized,active:current.projection().ActiveWorker};
    const result=await current.send("invoke",new Uint8Array([1]));
    if(result.State!=="succeeded")return {loaded,initialized,result,active:current.projection().ActiveWorker};
    const shutdown=await current.send("shutdown");
    return {loaded,initialized,result,shutdown,active:current.projection().ActiveWorker};
  } finally {current.dispose();current.dispose();}
};

window.runForeignObservations = async () => {
  const artifact=await bytes("control-ordinary.wasm"), current=await session(artifact,"sc2c",65536,250,true);
  try {
    await current.send("load",artifact);await current.send("initialize",new Uint8Array([0]));
    const result=await current.send("invoke",new Uint8Array([1]));
    return {result,ignored:current.ignored(), deliveries:current.results.length};
  } finally {current.dispose();}
};
window.runQueuedExpiry = async () => {
  const artifact=await bytes("control-ordinary.wasm"), current=await session(artifact,"sc2c");
  try {
    await current.send("load",artifact);await current.send("initialize",new Uint8Array([11]));
    const results=await Promise.all([current.send("invoke",new Uint8Array([1])),current.send("invoke",new Uint8Array([2])),current.send("invoke",new Uint8Array([3]))]);
    return {results,posts:current.posts,deliveries:current.results.map(x=>x.Request),active:current.projection().ActiveWorker};
  } finally {current.dispose();}
};
window.runCandidate = async () => {
  const artifact=await bytes("control-ordinary.wasm"), current=await session(artifact,"sc2c");
  try {
    await current.send("load",artifact);await current.send("initialize",new Uint8Array([11]));
    const loaded=await current.prepare(artifact,"transaction-2");
    const initialized=await current.send("initialize",new Uint8Array([0]),"candidate-worker","2");
    const old=current.send("invoke",new Uint8Array([1]));
    bridge.validateCandidate(current.host,current.transport.Now(),"wrong-transaction",2n);
    const wrongReady=current.projection().CandidateReady;
    bridge.validateCandidate(current.host,current.transport.Now(),"transaction-2",2n);
    bridge.freeze(current.host,current.transport.Now(),"freeze-2");
    bridge.commitCandidate(current.host,current.transport.Now(),"transaction-2",1n,2n);
    const frozenActive=current.projection().ActiveWorker;
    bridge.resume(current.host,current.transport.Now(),"freeze-2");
    bridge.commitCandidate(current.host,current.transport.Now(),"transaction-2",1n,2n);
    const before=current.projection();
    const oldResult=await old;
    const result=await current.send("invoke",new Uint8Array([1]),"candidate-worker","2");
    return {loaded,initialized,wrongReady,frozenActive,before,oldResult,result,after:current.projection()};
  } finally {current.dispose();}
};
