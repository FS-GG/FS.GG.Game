import * as bridge from "./fable/Program.js";
import { createHostTransport } from "./_content/FS.GG.Wasm.Browser/worker-client.mjs";
const bytes = async name => new Uint8Array(await (await fetch(`./modules/${name}`)).arrayBuffer());
const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", value)), byte => byte.toString(16).padStart(2, "0")).join("");
async function session(artifact, prefix, outputCap = 65536, deadline = 250, inject = false, observer, holdLoaded = false, options = {}) {
  const transport = createHostTransport(new URL("./_content/FS.GG.Wasm.Browser/", import.meta.url),observer?{observe:observer}:undefined);
  const waiting = new Map(), results = [], posts=[], terminations=[], deliveryStates=[], admissions=[], terminationStates=[];
  const terminate=transport.TerminateWorker;
  transport.TerminateWorker=name=>{
    // Observe the producer decision BEFORE mechanical termination and the
    // connected Host's subsequent WorkerTerminated/Now reentry.
    terminationStates.push({worker:name,clock:bridge.producerClock(host),disposed:bridge.project(host).Disposed,settledRequests:results.map(x=>x.Request)});
    terminations.push(name);terminate(name);
  };
  let ignored=0, host;const held=[];
  const create=transport.CreateWorker;
  // Page-clock timing is measurement only; the F# producer owns every decision.
  const timing={created:new Map(),ready:new Map(),settled:new Map()};
  transport.CreateWorker=(name,receive)=>{timing.created.set(name,performance.now());return create(name,wire=>{
    // The fresh Worker's first Compile receipt is its readiness report.
    if(wire.Kind==="phase"&&wire.Phase==="compile"){
      if(options.holdReady){held.push({wire,receive});return;}
      if(!timing.ready.has(name))timing.ready.set(name,performance.now());
    }
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
  });};
  const post=transport.PostCommand;
  transport.PostCommand=(name,command)=>{posts.push({name,Kind:command.Kind,request:command.Identity.Request});post(name,command);};
  const artifactDigest=await digest(artifact);
  host = bridge.createHostWithStartup(prefix === "barc" ? "bar-protected" : "sc2-imported-strict", artifactDigest, outputCap, deadline, options.startup ?? 0, transport, result => {
    timing.settled.set(result.Request,performance.now());
    deliveryStates.push({request:result.Request,clock:bridge.producerClock(host),projection:bridge.project(host),terminations:[...terminations],terminationStates:[...terminationStates]});
    results.push(result); const pending = waiting.get(result.Request); waiting.delete(result.Request); pending?.(result);
  });
  let request = 0;
  const send = (method, value, owner = "installed-worker", generation = "1") => {
    const id = String(++request), identity = {WorkerInstance: owner, Request: id, Generation: generation};
    return new Promise(resolve => {
      waiting.set(id, resolve);
      bridge[method](host, transport.Now(), identity, value);
      admissions.push({request:id,queuedDeadline:bridge.queuedDeadline(host,id)});
    });
  };
  return { host, send, results, posts, terminations, deliveryStates, admissions, terminationStates, transport, timing,
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
    loadLimited:()=>{
      const id=String(++request),identity={WorkerInstance:"installed-worker",Request:id,Generation:"1"};
      return new Promise(resolve=>{waiting.set(id,resolve);bridge.loadLimitedSc2(host,transport.Now(),identity,artifactDigest,artifact,deadline);});
    },
    prepareLimited:transaction=>{
      const id=String(++request),identity={WorkerInstance:"candidate-worker",Request:id,Generation:"2"};
      return new Promise(resolve=>{waiting.set(id,resolve);bridge.prepareLimitedSc2(host,transport.Now(),identity,transaction,artifactDigest,artifact);});
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
    result.loaded=await current.loadLimited();
    if(result.loaded.State==="succeeded"){
      result.initialized=await current.send("initialize",new Uint8Array([0]));
      if(result.initialized.State==="succeeded"){
        result.first=await current.prepareLimited("startup-first");
        if(result.first.State==="succeeded"){
          bridge.abortCandidate(current.host,current.transport.Now(),"startup-first");
          result.afterAbort=current.projection();
          result.second=await current.prepareLimited("startup-second");
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
    const loaded=await current.loadLimited(),before=current.projection(),deliveriesBefore=current.results.length,held=current.heldCount();
    current.releaseHeld();
    result={loaded,held,before,after:current.projection(),deliveriesBefore,deliveriesAfter:current.results.length};
  }finally{current.dispose();}
  return {...result,trace,status:current.transport.DiagnosticStatus(),retired:current.terminations};
};
// Declared delivery impairment: withhold the genuine readiness receipt so the
// separate startup bound expires. The real command was posted at creation.
window.runHeldReadiness = async (startup=100) => {
  const artifact=await bytes("control-ordinary.wasm");
  const current=await session(artifact,"sc2c",65536,250,false,undefined,false,{startup,holdReady:true});
  try{
    const started=performance.now();
    const loaded=await current.send("load",artifact);
    const elapsed=performance.now()-started,before=current.projection(),deliveriesBefore=current.results.length,held=current.heldCount();
    current.releaseHeld();
    return {loaded,elapsed,held,before,after:current.projection(),deliveriesBefore,deliveriesAfter:current.results.length,retired:current.terminations,posts:current.posts.map(x=>x.Kind)};
  }finally{current.dispose();}
};

// One cold page load: create the package Worker, wait for its readiness report,
// load (admit/compile/instantiate) the policy, initialize it and request one
// advisory result, exactly as a product receiver does. Page-clock measurements only.
window.runColdLoad = async (name="c-sc2.wasm") => {
  const artifact=await bytes(name),current=await session(artifact,"sc2c");
  const worker="installed-worker";
  try{
    const loaded=await current.send("load",artifact);
    const created=current.timing.created.get(worker),ready=current.timing.ready.get(worker),loadedAt=current.timing.settled.get(loaded.Request);
    const row={loaded:loaded.State,reason:loaded.Reason,phase:loaded.Phase,
      startupMilliseconds:ready===undefined?null:ready-created,
      policyLoadMilliseconds:ready===undefined?null:loadedAt-ready,
      endToEndLoadMilliseconds:loadedAt-created,hardwareConcurrency:navigator.hardwareConcurrency};
    if(loaded.State!=="succeeded")return row;
    const initializeStart=performance.now(),initialized=await current.send("initialize",new Uint8Array([1,2,3,4]));
    row.initialized=initialized.State;row.initializeMilliseconds=performance.now()-initializeStart;
    if(initialized.State!=="succeeded")return row;
    const adviceStart=performance.now(),advice=await current.send("invoke",new Uint8Array([1,2,3,4]));
    row.advice=advice.State;row.adviceMilliseconds=performance.now()-adviceStart;row.adviceOutput=Array.from(advice.Output);
    return row;
  }finally{current.dispose();}
};
// Declared delivery impairment with readiness intact: the genuine loaded receipt is
// withheld, so the one-millisecond policy budget (started at readiness) expires.
window.runPolicyExpiryAfterReadiness = async () => {
  const artifact=await bytes("control-ordinary.wasm");
  const current=await session(artifact,"sc2c",65536,1,false,undefined,true);
  try{
    const loaded=await current.send("load",artifact);
    return {loaded,readyObserved:current.timing.ready.has("installed-worker"),held:current.heldCount(),active:current.projection().ActiveWorker,retired:current.terminations};
  }finally{current.dispose();}
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
    const priorTerminations=s.terminationStates.length;
    const active=s.send("invoke",new Uint8Array([1]));
    const reset=s.send("invokeAndRetire",new Uint8Array([2]));
    const following=s.send(follower,new Uint8Array([3]));
    const [a,r,f]=await Promise.all([active,reset,following]);
    const retirement=s.deliveryStates.find(item=>item.request===r.Request);
    return {active:a,reset:r,follower:f,retirementBatch:retirement.terminationStates.slice(priorTerminations),followerDue:s.admissions.find(x=>x.request===f.Request)?.queuedDeadline,followerClock:s.deliveryStates.find(x=>x.request===f.Request)?.clock,posts:s.posts.filter(item=>item.Kind==="process"||item.Kind==="shutdown").map(item=>({request:item.request,kind:item.Kind})),retirement,deliveries:s.results.map(item=>item.Request),projection:s.projection()};
  }finally{s.dispose();}
};

window.runTerminalRaw = async mode => {
  const artifact=await bytes("control-ordinary.wasm"),s=await session(artifact,"sc2c");
  try {
    const loaded=await s.send("load",artifact),initialized=await s.send("initialize",new Uint8Array([mode]));
    const candidate=await s.prepare(artifact,"terminal-candidate");
    if([loaded,initialized,candidate].some(x=>x.State!=="succeeded"))throw new Error("terminal raw setup did not succeed");
    const priorTerminations=s.terminationStates.length;
    const reset=s.send("invokeAndRetire",new Uint8Array([1]));
    const follower=s.send("shutdown");
    const [r,f]=await Promise.all([reset,follower]);
    const retirement=s.deliveryStates.find(x=>x.request===r.Request);
    return {reset:r,follower:f,retirementBatch:retirement.terminationStates.slice(priorTerminations),followerDue:s.admissions.find(x=>x.request===f.Request)?.queuedDeadline,followerClock:s.deliveryStates.find(x=>x.request===f.Request)?.clock,deliveries:s.results.map(x=>x.Request),posts:s.posts.filter(x=>x.Kind==="process"||x.Kind==="shutdown").map(x=>x.Kind),retirement};
  }finally{s.dispose();}
};
