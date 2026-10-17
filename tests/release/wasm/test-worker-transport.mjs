// Inert injected-mechanics contract. No Worker, browser, guest or real timer runs.
import { test } from "node:test";
import assert from "node:assert/strict";
import { createHostTransport } from "../../../src/Wasm.Browser/js/worker-client.mjs";

function mechanics(run){
  const saved={Worker:globalThis.Worker,setTimeout:globalThis.setTimeout,clearTimeout:globalThis.clearTimeout};
  const workers=[],timers=new Map(),cleared=[];let nextTimer=0;
  globalThis.Worker=class {
    constructor(url,options){this.url=new URL(url);this.options=options;this.posts=[];this.terminations=0;workers.push(this);}
    postMessage(value){this.posts.push(value);}
    terminate(){this.terminations++;}
    message(value){this.onmessage({data:value});}
  };
  globalThis.setTimeout=(receive,delay)=>{const id=++nextTimer;timers.set(id,{receive,delay});return id;};
  globalThis.clearTimeout=id=>{cleared.push(id);timers.delete(id);};
  try{return run({workers,timers,cleared});}finally{Object.assign(globalThis,saved);}
}
const identity={WorkerInstance:"candidate",Request:"3",Generation:"2"};
const command={Identity:identity,Correlation:"operation-3",Kind:"load",Bytes:new Uint8Array([91,92])};
const advisory=(stage,elapsedMilliseconds=1)=>({kind:"fsgg-worker-diagnostic/1",stage,status:"observed",elapsedMilliseconds,
  identity:stage==="handler-ready"?null:{...identity},correlation:stage==="handler-ready"?null:command.Correlation});

test("disabled diagnostics preserve actual transport arguments, timer and delivery",()=>mechanics(({workers,timers})=>{
  const received=[],t=createHostTransport("https://example.invalid/nested/assets/");
  t.CreateWorker("candidate",value=>received.push(value));t.PostCommand("candidate",command);
  assert.equal(workers[0].url.href,"https://example.invalid/nested/assets/module-worker.mjs");
  assert.equal(workers[0].posts[0],command);assert.equal(typeof t.Now(),"bigint");
  let fired=0;t.ArmTimer("actual-key",7,()=>fired++);
  assert.equal([...timers.values()][0].delay,7);[...timers.values()][0].receive();assert.equal(fired,1);
  workers[0].message(advisory("handler-ready"));assert.deepEqual(received,[]);
  const loaded={Kind:"loaded",Identity:identity,Correlation:command.Correlation};workers[0].message(loaded);
  assert.equal(received[0],loaded);t.TerminateWorker("candidate");assert.equal(workers[0].terminations,1);
  assert.equal(t.DiagnosticStatus().state,"disabled");
}));

test("correlated diagnostics cannot deliver or rearm and never copy guest payload",()=>mechanics(({workers,timers})=>{
  const received=[],rows=[],t=createHostTransport("https://example.invalid/sub/app/",{observe:row=>rows.push(row)});
  t.CreateWorker("candidate",value=>received.push(value));t.PostCommand("candidate",command);
  t.ArmTimer("original-key",251,()=>{});
  assert.equal(workers[0].url.hash,"#fsgg-worker-diagnostics-v1");
  workers[0].message(advisory("handler-ready",0));workers[0].message(advisory("load-received",1));
  workers[0].message(advisory("admission-begin",2));workers[0].message(advisory("admission-end",3));
  assert.deepEqual(received,[]);assert.equal(timers.size,1);assert.equal([...timers.values()][0].delay,251);
  assert.deepEqual(rows.filter(row=>row.stage==="worker-stage").map(row=>row.workerStage),
    ["handler-ready","load-received","admission-begin","admission-end"]);
  assert.equal(rows.some(row=>"Bytes"in row||"Output"in row||"Diagnostic"in row),false);
  assert.equal(JSON.stringify(rows).includes("91,92"),false);assert.equal(t.DiagnosticStatus().readFailed,false);
}));

test("throwing observer and unreadable diagnostic scalars cannot replace native effects",()=>mechanics(({workers,timers})=>{
  const t=createHostTransport("https://example.invalid/",{observe(){throw new Error("observer only");}});
  assert.doesNotThrow(()=>t.CreateWorker("candidate",()=>{}));
  assert.doesNotThrow(()=>t.PostCommand("candidate",{Kind:"load"}));
  assert.doesNotThrow(()=>t.ArmTimer("key",11,()=>{}));assert.equal(timers.size,1);
  assert.equal(workers[0].posts.length,1);assert.equal(t.DiagnosticStatus().readFailed,true);
  const other=createHostTransport("https://example.invalid/",{observe(){}});
  assert.doesNotThrow(()=>other.ArmTimer({},13,()=>{}));assert.equal(timers.size,2);
  assert.equal(other.DiagnosticStatus().readFailed,true);
}));

for(const mutation of ["foreign","backward","duplicate","unknown"])
test(`malformed/foreign advisory is a gap, not a lifecycle receipt: ${mutation}`,()=>mechanics(({workers})=>{
  const received=[],t=createHostTransport("https://example.invalid/",{observe(){}});
  t.CreateWorker("candidate",value=>received.push(value));t.PostCommand("candidate",command);
  workers[0].message(advisory("handler-ready",2));
  const row=advisory("admission-begin",3);
  if(mutation==="foreign")row.identity.Request="99";
  if(mutation==="backward")row.elapsedMilliseconds=1;
  if(mutation==="duplicate")Object.assign(row,advisory("handler-ready",3));
  if(mutation==="unknown")row.stage="invented";
  workers[0].message(row);assert.deepEqual(received,[]);assert.equal(t.DiagnosticStatus().readFailed,true);
}));

test("late diagnostic cannot become readiness for a new same-name physical Worker",()=>mechanics(({workers})=>{
  const rows=[],t=createHostTransport("https://example.invalid/",{observe:row=>rows.push(row)});
  t.CreateWorker("candidate",()=>{});const old=workers[0];t.TerminateWorker("candidate");
  t.CreateWorker("candidate",()=>{});const count=rows.length;old.message(advisory("handler-ready",0));
  assert.equal(rows.length,count);assert.equal(t.DiagnosticStatus().incomplete,true);
  assert.equal(t.DiagnosticStatus().unavailableWorkers,1);
}));

test("late error cannot borrow a new same-name Worker's request identity",()=>mechanics(({workers})=>{
  const oldResults=[],newResults=[],t=createHostTransport("https://example.invalid/");
  t.CreateWorker("candidate",value=>oldResults.push(value));t.PostCommand("candidate",command);
  const old=workers[0];t.TerminateWorker("candidate");
  t.CreateWorker("candidate",value=>newResults.push(value));
  const next={...command,Identity:{...identity,Request:"4"},Correlation:"operation-4"};t.PostCommand("candidate",next);
  let prevented=0;old.onerror({preventDefault(){prevented++;},message:"retired physical error"});
  assert.equal(prevented,1);assert.deepEqual(oldResults,[]);assert.deepEqual(newResults,[]);
  workers[1].onerror({preventDefault(){},message:"current physical error"});
  assert.equal(newResults.length,1);assert.equal(newResults[0].Identity,next.Identity);
  assert.equal(newResults[0].Correlation,next.Correlation);
}));

test("diagnostic count/bytes stop honestly without changing the returned clock",()=>mechanics(()=>{
  const rows=[],t=createHostTransport("https://example.invalid/",{observe:row=>rows.push(row)});
  for(let index=0;index<300;index++)assert.equal(typeof t.Now(),"bigint");
  const status=t.DiagnosticStatus();assert.equal(status.truncated,true);assert.equal(status.incomplete,true);
  assert.ok(rows.length<=128);assert.ok(status.bytes<=24576);
}));

test("an explicit observation scope excludes old traffic without resetting any clock or timer",()=>mechanics(({workers,timers})=>{
  const rows=[];let selected=false;
  const t=createHostTransport("https://example.invalid/",{observe:row=>rows.push(row),include:()=>selected});
  t.CreateWorker("candidate",()=>{});t.PostCommand("candidate",command);t.ArmTimer("unrelated-active-key",251,()=>{});
  workers[0].message(advisory("handler-ready",0));
  for(let index=0;index<200;index++)t.Now();
  workers[0].message({kind:"fsgg-worker-diagnostic/1",stage:"status",status:"truncated",elapsedMilliseconds:1});
  assert.equal(t.DiagnosticStatus().truncated,false);assert.equal(rows.length,0);
  t.TerminateWorker("candidate");selected=true;
  t.CreateWorker("candidate",()=>{});t.PostCommand("candidate",command);
  workers[1].message(advisory("handler-ready",0));workers[1].message(advisory("admission-begin",1));
  assert.equal(timers.size,1);assert.equal([...timers.values()][0].delay,251);
  assert.equal(t.DiagnosticStatus().available,true);assert.ok(t.DiagnosticStatus().excludedEvents>=200);
  assert.deepEqual(rows.filter(row=>row.stage==="worker-stage").map(row=>row.workerStage),["handler-ready","admission-begin"]);
}));

test("unreadable diagnostic scope is a gap without blocking native creation",()=>mechanics(({workers})=>{
  const t=createHostTransport("https://example.invalid/",{observe(){},include(){return undefined;}});
  assert.doesNotThrow(()=>t.CreateWorker("candidate",()=>{}));assert.equal(workers.length,1);
  assert.equal(t.DiagnosticStatus().state,"read-failed");
}));
