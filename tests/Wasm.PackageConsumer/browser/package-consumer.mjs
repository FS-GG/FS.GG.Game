import * as bridge from "./fable/Program.js";
import { createHostTransport } from "./_content/FS.GG.Wasm.Browser/worker-client.mjs";
const bytes = async name => new Uint8Array(await (await fetch(`./modules/${name}`)).arrayBuffer());
const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", value)), byte => byte.toString(16).padStart(2, "0")).join("");
async function session(artifact, prefix, outputCap = 65536, deadline = 250, inject = false, observer, holdLoaded = false) {
  const transport = createHostTransport(new URL("./_content/FS.GG.Wasm.Browser/", import.meta.url),observer?{observe:observer}:undefined);
  const waiting = new Map(), results = [], posts=[], terminations=[], deliveryStates=[];
  const terminate=transport.TerminateWorker;
  transport.TerminateWorker=name=>{terminations.push(name);terminate(name);};
  let ignored=0, host;const held=[];
  const create=transport.CreateWorker;
  transport.CreateWorker=(name,receive)=>create(name,wire=>{
    // Declared delivery-impairment control: retain a genuine loaded message,
    // not a manufactured ABI result. The real original timer still expires.
    if(holdLoaded&&wire.Kind==="loaded"){held.push({wire,receive});return;}
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
  const artifactDigest=await digest(artifact);
  host = bridge.createHost(prefix === "barc" ? "bar-protected" : "sc2-imported-strict", artifactDigest, outputCap, deadline, transport, result => {
    deliveryStates.push({request:result.Request,projection:bridge.project(host),terminations:[...terminations]});
    results.push(result); const pending = waiting.get(result.Request); waiting.delete(result.Request); pending?.(result);
  });
  let request = 0;
  const send = (method, value, owner = "installed-worker", generation = "1") => {
    const id = String(++request), identity = {WorkerInstance: owner, Request: id, Generation: generation};
    return new Promise(resolve => { waiting.set(id, resolve); bridge[method](host, transport.Now(), identity, value); });
  };
  return { host, send, results, posts, terminations, deliveryStates, transport,
    heldCount:()=>held.length,
    releaseHeld:()=>{for(const row of held.splice(0))row.receive(row.wire);},
    ignored:()=>ignored,
    prepare:async (artifact, transaction)=>{
      const hash=await digest(artifact), id=String(++request), identity={WorkerInstance:"candidate-worker",Request:id,Generation:"2"};
      return new Promise(resolve=>{waiting.set(id,resolve);bridge.prepare(host,transport.Now(),identity,transaction,1n,hash,artifact);});
    },
    loadBounded:()=>{
      const id=String(++request),identity={WorkerInstance:"installed-worker",Request:id,Generation:"1"};
      return new Promise(resolve=>{waiting.set(id,resolve);bridge.loadBoundedSc2(host,transport.Now(),identity,artifactDigest,artifact,deadline);});
    },
    prepareBounded:transaction=>{
      const id=String(++request),identity={WorkerInstance:"candidate-worker",Request:id,Generation:"2"};
      return new Promise(resolve=>{waiting.set(id,resolve);bridge.prepareBoundedSc2(host,transport.Now(),identity,transaction,artifactDigest,artifact);});
    },
    dispose() { bridge.dispose(host, transport.Now()); },
    projection() { return bridge.project(host); }
  };
}

// One declared history: fresh initial Worker, candidate abort, then a fresh
// same-name candidate. No priming, retry, synthetic clock or renewed budget.
// Own public control module plus 65,536 genuine nop instructions in its first
// function. No private guest, clock, synthetic result or priming Worker is used.
function instructionDense(artifact) {
  const leb=value=>{const out=[];do{const part=value&127;value>>>=7;out.push(part|(value?128:0));}while(value);return out;};
  const read=cursor=>{let value=0,shift=0,part;do{part=artifact[cursor.offset++];value|=(part&127)<<shift;shift+=7;}while(part&128);return value;};
  const cursor={offset:8};
  while(cursor.offset<artifact.length){
    const start=cursor.offset,kind=artifact[cursor.offset++],length=read(cursor),payload=cursor.offset,end=payload+length;
    if(kind===10){
      if(read(cursor)<1)throw new Error("owned dense fixture requires a function");
      const sizeStart=cursor.offset,size=read(cursor),body=cursor.offset,groups=read(cursor);
      for(let i=0;i<groups;i++){read(cursor);cursor.offset++;}
      const insert=cursor.offset,padding=new Uint8Array(65536).fill(1),bodyLength=leb(size+padding.length);
      const header=[kind,...leb(length+padding.length+bodyLength.length-(body-sizeStart))];
      const result=new Uint8Array(artifact.length+padding.length+bodyLength.length-(body-sizeStart)+header.length-(payload-start));
      let offset=0;for(const part of [artifact.subarray(0,start),header,artifact.subarray(payload,sizeStart),bodyLength,artifact.subarray(body,insert),padding,artifact.subarray(insert)]){result.set(part,offset);offset+=part.length;}
      if(offset!==result.length)throw new Error("dense fixture length mismatch");
      return result;
    }
    cursor.offset=end;
  }
  throw new Error("owned dense fixture has no code section");
}
window.runObservedFreshWorkers = async (dense=false) => {
  const trace=[],original=await bytes("control-ordinary.wasm"),artifact=dense?instructionDense(original):original,current=await session(artifact,"sc2c",65536,250,false,row=>trace.push(row));
  let result={fixtureFailure:null};
  try{
    result.loaded=await current.loadBounded();
    if(result.loaded.State==="succeeded"){
      result.initialized=await current.send("initialize",new Uint8Array([0]));
      if(result.initialized.State==="succeeded"){
        result.first=await current.prepareBounded("startup-first");
        if(result.first.State==="succeeded"){
          bridge.abortCandidate(current.host,current.transport.Now(),"startup-first");
          result.afterAbort=current.projection();
          result.second=await current.prepareBounded("startup-second");
        }
      }
    }
  }catch(error){result.fixtureFailure=String(error).slice(0,256);}
  finally{current.dispose();}
  return {...result,artifactBytes:artifact.length,trace,status:current.transport.DiagnosticStatus(),retired:current.terminations,final:current.projection()};
};

window.runObservedColdExpiry = async () => {
  const trace=[],artifact=await bytes("control-ordinary.wasm"),current=await session(artifact,"sc2c",65536,1,false,row=>trace.push(row));
  let loaded;
  try{loaded=await current.loadBounded();}finally{current.dispose();}
  return {loaded,trace,status:current.transport.DiagnosticStatus(),retired:current.terminations,final:current.projection()};
};

window.runObservedLateLoaded = async () => {
  const trace=[],artifact=await bytes("control-ordinary.wasm");
  const current=await session(artifact,"sc2c",65536,250,false,row=>trace.push(row),true);
  let result;
  try{
    const loaded=await current.loadBounded(),before=current.projection(),deliveriesBefore=current.results.length,held=current.heldCount();
    current.releaseHeld();
    result={loaded,held,before,after:current.projection(),deliveriesBefore,deliveriesAfter:current.results.length};
  }finally{current.dispose();}
  return {...result,trace,status:current.transport.DiagnosticStatus(),retired:current.terminations};
};
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

// Installed package, real Worker, unchanged ProcessGuest ABI. Transport records
// observations only; retirement decisions remain entirely in the F# producer.
window.runTerminalProcess = async (mode=18, follower="invokeAndRetire") => {
  const artifact=await bytes("rust-sc2.wasm"), s=await session(artifact,"sc2c");
  try {
    await s.send("load",artifact);await s.send("initialize",new Uint8Array([mode]));
    const candidate=await s.prepare(artifact,"terminal-candidate");
    if(candidate.State!=="succeeded")throw new Error("candidate preparation failed");
    const active=s.send("invoke",new Uint8Array([1]));
    const reset=s.send("invokeAndRetire",new Uint8Array([2]));
    const following=s.send(follower,new Uint8Array([3]));
    const [a,r,f]=await Promise.all([active,reset,following]);
    const retirement=s.deliveryStates.find(item=>item.request===r.Request);
    return {active:a,reset:r,follower:f,posts:s.posts.filter(item=>item.Kind==="process"||item.Kind==="shutdown").map(item=>({request:item.request,kind:item.Kind})),retirement,deliveries:s.results.map(item=>item.Request),projection:s.projection()};
  }finally{s.dispose();}
};

window.runTerminalRaw = async mode => {
  const artifact=await bytes("control-ordinary.wasm"),s=await session(artifact,"sc2c");
  try {
    await s.send("load",artifact);await s.send("initialize",new Uint8Array([mode]));
    await s.prepare(artifact,"terminal-candidate");
    const reset=s.send("invokeAndRetire",new Uint8Array([1]));
    const follower=s.send("shutdown");
    const [r,f]=await Promise.all([reset,follower]);
    return {reset:r,follower:f,posts:s.posts.filter(x=>x.Kind==="process"||x.Kind==="shutdown").map(x=>x.Kind),retirement:s.deliveryStates.find(x=>x.request===r.Request)};
  }finally{s.dispose();}
};
