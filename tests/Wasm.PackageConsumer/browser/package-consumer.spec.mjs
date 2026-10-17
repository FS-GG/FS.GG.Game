import { test, expect } from "@playwright/test";

test.beforeEach(async ({page}) => {
  await page.goto("http://127.0.0.1:41784/sub/app/index.html");
  await expect.poll(() => page.evaluate(() => window.packageConsumerReady === true)).toBe(true);
});
for (const [name,prefix,version,output] of [
  ["rust-bar.wasm","barc",1,[66,65,82,51]],
  ["rust-sc2.wasm","sc2c",0x10000,[83,67,50,51]],
  ["c-bar.wasm","barc",1,[66,65,82,67]],
  ["c-sc2.wasm","sc2c",0x10000,[83,67,50,67]],
]) test(`${name} calls through package Worker assets`, async ({page}) => {
  const value = await page.evaluate(args => window.runGuest(...args), [name,prefix,version]);
  expect(value.loaded.State, JSON.stringify(value)).toBe("succeeded");
  expect(value.initialized.State).toBe("succeeded");
  expect(value.phases).toEqual(["Initialize", "Process", "Shutdown"]);
  expect(value.result).toMatchObject({status:0,output});
  expect(value.shutdown.status).toBe(0);
  expect(value.disposedBefore).toBe(false);
});
test("trap remains inside the package Worker", async ({page}) => {
  const value=await page.evaluate(() => window.runGuest("c-trap.wasm","sc2c"));
  expect(value.state).toBe("faulted");
  expect(value.active).toBe("");
  expect(value.diagnostic).toContain("unreachable");
  await expect(page.evaluate(() => 42)).resolves.toBe(42);
});
test("deadline terminates and repeated disposal is harmless", async ({page}) => {
  const value=await page.evaluate(() => window.runDeadline("c-loop.wasm"));
  expect(value.error.toLowerCase()).toContain("deadline"); expect(value.disposed).toBe(true);
  await expect(page.evaluate(() => 43)).resolves.toBe(43);
});

for(const [mutation, cap] of [["empty",65536],["unaligned",65536],["overcap",1]])
test(`actual admitted BAR ${mutation} output is refused by package F# policy`, async ({page}) => {
  const result=await page.evaluate(args=>window.runCounterexample(...args),[mutation,cap]);
  expect(result.loaded).toBe("succeeded");
  expect(result.state).toBe("faulted");
  expect(result.output).toEqual([]);
  expect(result.initialized).toEqual([]);
  expect(result.active).toBe("");
});

test("distinct init/process/shutdown markers execute actual exports and refreshed grown memory",async({page})=>{
  const result=await page.evaluate(()=>window.runControl("control-ordinary.wasm",18));
  expect(result.initialized.State).toBe("succeeded");
  expect(Array.from(result.result.Output)).toEqual([1,2,3,4]);
  expect(result.shutdown).toMatchObject({State:"rejected",Phase:"Shutdown",GuestStatus:203,Dispatched:true});
  expect(result.active).toBe("");
});
for(const [name,phase,dispatched] of [["bad-descriptor","AllocateDescriptor",false],["bad-input","AllocateInput",false]])
test(`unsafe ${name} refuses before looping extra allocation or free`,async({page})=>{
  const value=await page.evaluate(name=>window.runControl(`control-${name}.wasm`,0),name);
  expect(value.initialized).toMatchObject({State:"faulted",Phase:phase,Dispatched:dispatched});
  expect(value.active).toBe("");
});
for(const [mode,field,phase] of [[10,"initialized","Initialize"],[11,"result","Process"],[12,"initialized","Free"],[14,"shutdown","Shutdown"]])
test(`infinite ${phase} is terminated by actual host watchdog`,async({page})=>{
  const value=await page.evaluate(mode=>window.runControl("control-ordinary.wasm",mode),mode);
  expect(value[field]).toMatchObject({State:"timeout",Phase:phase});
  expect(value.active).toBe("");
});
test("malformed output ownership never calls looping free",async({page})=>{
  const value=await page.evaluate(()=>window.runControl("control-ordinary.wasm",15));
  expect(value.result).toMatchObject({State:"faulted",Phase:"Process"});
  expect(value.active).toBe("");
});
for(const mode of [16,17])test(`cleanup trap preserves primary outcome ${mode}`,async({page})=>{
  const value=await page.evaluate(mode=>window.runControl("control-ordinary.wasm",mode),mode);
  expect(value.result.State).toBe("faulted");
  expect(value.result.CleanupDiagnostic).toContain("unreachable");
  expect(Array.from(value.result.Output)).toEqual([]);
  expect(value.active).toBe("");
});
test("ABI version mismatch never grants initialized authority",async({page})=>{
  const value=await page.evaluate(()=>window.runControl("control-wrong-version.wasm",0));
  expect(value.loaded.Diagnostic).toContain("ABI version mismatch");
  expect(value.active).toBe("");
});
test("compile deadline terminates the actual freshly created Worker",async({page})=>{
  const value=await page.evaluate(()=>window.runControl("control-ordinary.wasm",0,1));
  expect(value.loaded).toMatchObject({State:"timeout",Phase:"Compile"});
  expect(value.active).toBe("");
});

test("closed installed Worker starts fresh initially and after candidate abort under original limited budgets",async({page})=>{
  const requested=[],physical=[];
  const onRequest=request=>{const url=new URL(request.url());if(url.pathname.includes("/_content/FS.GG.Wasm.Browser/"))requested.push(url.pathname);};
  page.context().on("request",onRequest);page.on("worker",worker=>physical.push(worker.url()));
  let value;
  try{
    value=await page.evaluate(()=>window.runObservedFreshWorkers());
    const report={stage:"fresh installed Worker history",states:[value.loaded?.State,value.initialized?.State,value.first?.State,value.second?.State],
      status:value.status,trace:value.trace};
    console.log(JSON.stringify(report));
    expect(value.fixtureFailure,JSON.stringify(report)).toBeNull();
    expect(report.states,JSON.stringify(report)).toEqual(["succeeded","succeeded","succeeded","succeeded"]);
    expect(value.afterAbort.CandidateWorker).toBe("");
    expect(value.final).toMatchObject({Disposed:true,ActiveWorker:"",CandidateWorker:""});
    expect(physical).toHaveLength(3);
    expect(physical.every(url=>new URL(url).pathname==="/sub/app/_content/FS.GG.Wasm.Browser/module-worker.mjs")).toBe(true);
    expect(requested.some(url=>url.endsWith("/module-worker.mjs"))).toBe(true);
    expect(requested.some(url=>url.includes("/policy/"))).toBe(false);
    expect(value.status).toMatchObject({enabled:true,available:true,incomplete:false,truncated:false,readFailed:false});
    for(const id of [1,2,3]){
      const stages=value.trace.filter(row=>row.stage==="worker-stage"&&row.physical===id).map(row=>row.workerStage);
      expect(stages.slice(0,9)).toEqual(["handler-ready","load-received","admission-begin","admission-end","sha256-begin","sha256-end","instantiate-begin","instantiate-end","load-completed"]);
    }
    const retired=value.trace.findIndex(row=>row.stage==="worker-terminate"&&row.physical===2);
    const created=value.trace.findIndex(row=>row.stage==="worker-created"&&row.physical===3);
    expect(retired).toBeGreaterThan(-1);expect(created).toBeGreaterThan(retired);
    await expect.poll(()=>page.workers().length).toBe(0);
  }finally{page.context().off("request",onRequest);}
});

test("observed cold expiry keeps its one-millisecond pre-create deadline and retirement",async({page})=>{
  const value=await page.evaluate(()=>window.runObservedColdExpiry());
  console.log(JSON.stringify({stage:"observed cold expiry",state:value.loaded.State,status:value.status,trace:value.trace}));
  expect(value.loaded).toMatchObject({State:"timeout",Phase:"Compile"});
  expect(value.retired).toContain("installed-worker");expect(value.final.ActiveWorker).toBe("");
  await expect.poll(()=>page.workers().length).toBe(0);
});

test("a genuine loaded message withheld beyond the original deadline cannot restore expired authority",async({page})=>{
  const value=await page.evaluate(()=>window.runObservedLateLoaded());
  console.log(JSON.stringify({stage:"declared late delivery impairment",state:value.loaded.State,held:value.held,status:value.status,trace:value.trace}));
  expect(value.held).toBe(1); // A real compile must occur; an unexercised prefix cannot pass.
  expect(value.loaded.State).toBe("timeout");expect(value.deliveriesAfter).toBe(value.deliveriesBefore);
  expect(value.after).toEqual(value.before);expect(value.after.ActiveWorker).toBe("");
  expect(value.retired).toContain("installed-worker");
  await expect.poll(()=>page.workers().length).toBe(0);
});

test("foreign correlation, Worker and generation observations are inert on the installed host",async({page})=>{
  const value=await page.evaluate(()=>window.runForeignObservations());
  expect(value.ignored).toBeGreaterThan(9);
  expect(value.deliveries).toBe(3);
  expect(value.result.State).toBe("succeeded");
});
test("active expiry settles queued work once without dispatching into expired Worker",async({page})=>{
  const value=await page.evaluate(()=>window.runQueuedExpiry());
  // All requests expire; a late timer may expire pending requests before Worker invalidation.
  expect(value.results.every(x=>x.Reason==="DeadlineExpired" && x.Disposition==="Discarded" && x.Output.length===0)).toBe(true);
  expect(value.results[0]).toMatchObject({Request:"3",State:"timeout",HasOutcome:true});
  for(const [index,request] of [[1,"4"],[2,"5"]]) {
    const result=value.results[index];
    expect(result).toMatchObject({Request:request,Dispatched:false});
    expect(result.State).toBe(result.HasOutcome ? "timeout" : "stopped");
  }
  expect(value.posts.filter(x=>x.Kind==="process").map(x=>x.request)).toEqual(["3"]);
  expect(value.deliveries.length).toBe(5);
  expect(new Set(value.deliveries).size).toBe(5);
  expect([...value.deliveries].sort()).toEqual(["1","2","3","4","5"]);
  expect(value.active).toBe("");
});
test("candidate init, transaction validation, freeze and retiring expiry preserve new installed authority",async({page})=>{
  const value=await page.evaluate(()=>window.runCandidate());
  expect(value.loaded.State).toBe("succeeded");
  expect(value.initialized).toMatchObject({State:"succeeded",Disposition:"HistoricalOnly"});
  expect(value.wrongReady).toBe(false);
  expect(value.frozenActive).toBe("installed-worker");
  expect(value.before.ActiveWorker).toBe("candidate-worker");
  expect(value.before.RetiringWorkers).toEqual(["installed-worker"]);
  expect(value.oldResult).toMatchObject({State:"timeout",Disposition:"Discarded"});
  expect(value.result.State).toBe("succeeded");
  expect(value.after.ActiveWorker).toBe("candidate-worker");
  expect(value.after.RetiringWorkers).toEqual([]);
});

test("unsafe output after guest rejection preserves status and removes dead Worker authority",async({page})=>{
  const value=await page.evaluate(()=>window.runControl("control-ordinary.wasm",19));
  expect(value.result).toMatchObject({State:"rejected",GuestStatus:92,Dispatched:true});
  expect(Array.from(value.result.Output)).toEqual([]);
  expect(value.active).toBe("");
});

for(const follower of ["invokeAndRetire","shutdown"])test(`terminal Process retires actual package Workers before result and ${follower}`,async({page})=>{
 const value=await page.evaluate(follower=>window.runTerminalProcess(18,follower),follower);
 expect(value.reset).toMatchObject({State:"succeeded",Phase:"Process",Dispatched:true});
 expect(Array.from(value.reset.Output)).toEqual([83,67,50,51]);
 expect(value.posts.map(item=>item.kind)).toEqual(["process","process"]);
 expect(value.follower).toMatchObject({HasOutcome:false,Disposition:"Discarded",Reason:"HostDisposed"});
 expect(value.retirement.projection).toMatchObject({Disposed:true,ActiveWorker:"",CandidateWorker:""});
 expect(new Set(value.retirement.terminations)).toEqual(new Set(["installed-worker","candidate-worker"]));
 expect(value.deliveries.indexOf(value.follower.Request)).toBeLessThan(value.deliveries.indexOf(value.reset.Request));
 expect(new Set(value.deliveries).size).toBe(value.deliveries.length);
});

for(const [mode,state,status] of [[18,"succeeded",0],[19,"rejected",92],[16,"faulted",0],[17,"faulted",0],[11,"timeout",0]])
test(`terminal Process preserves actual installed ABI and cleanup evidence mode ${mode}`,async({page})=>{
 const r=await page.evaluate(mode=>window.runTerminalRaw(mode),mode);
 expect(r.reset).toMatchObject({State:state,GuestStatus:status,Dispatched:true});
 if(mode===16||mode===17)expect(r.reset.CleanupDiagnostic).toContain("unreachable");
 expect(r.posts).toEqual(["process"]);
 expect(r.retirement.projection).toMatchObject({Disposed:true,ActiveWorker:"",CandidateWorker:""});
 expect(new Set(r.retirement.terminations)).toEqual(new Set(["installed-worker","candidate-worker"]));
 expect(r.follower).toMatchObject({HasOutcome:false,Disposition:"Discarded",Reason:"HostDisposed"});
});
