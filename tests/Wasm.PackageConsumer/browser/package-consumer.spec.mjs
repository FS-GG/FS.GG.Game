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

test("foreign correlation, Worker and generation observations are inert on the installed host",async({page})=>{
  const value=await page.evaluate(()=>window.runForeignObservations());
  expect(value.ignored).toBeGreaterThan(9);
  expect(value.deliveries).toBe(3);
  expect(value.result.State).toBe("succeeded");
});
test("active expiry settles queued work once without dispatching into expired Worker",async({page})=>{
  const value=await page.evaluate(()=>window.runQueuedExpiry());
  expect(value.results.map(x=>x.State)).toEqual(["timeout","timeout","timeout"]);
  expect(value.results.slice(1).every(x=>!x.Dispatched)).toBe(true);
  expect(value.posts.filter(x=>x.Kind==="process").map(x=>x.request)).toEqual(["3"]);
  expect(value.deliveries).toEqual(["1","2","3","4","5"]);
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
