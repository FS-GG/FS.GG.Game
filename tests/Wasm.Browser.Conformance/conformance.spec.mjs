import { test, expect } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
const here=path.dirname(fileURLToPath(import.meta.url));
const modules=path.resolve(here,"../Wasm.Compatibility/modules");
const bytes=name=>Array.from(fs.readFileSync(path.join(modules,name)));

async function run(page,module,timeout=1000,prefix="sc2c") {
  return page.evaluate(async ({module,timeout,prefix}) => {
    const worker=new Worker("./worker.mjs",{type:"module"});
    const once=()=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error("deadline")),timeout);worker.onmessage=e=>{clearTimeout(timer);resolve(e.data)};worker.onerror=e=>reject(new Error(e.message));});
    worker.postMessage({kind:"load",bytes:new Uint8Array(module),prefix}); const loaded=await once();
    worker.postMessage({kind:"invoke",input:new Uint8Array([1,2,3,4])});
    try { return {loaded,result:await once()}; } finally { worker.terminate(); }
  },{module,timeout,prefix});
}

test.beforeEach(async ({page})=>{ await page.goto("http://127.0.0.1:41783/index.html"); });
test("real SC2 guest executes in an actual module Worker",async({page})=>{
  const value=await run(page,bytes("sc2-rust/sc2-conformance.wasm"));
  expect(value.loaded.version).toBe(0x10000); expect(value.result).toMatchObject({kind:"result",status:0,output:[83,67,50,67]});
});
test("real BAR guest executes in an actual module Worker",async({page})=>{
  const value=await run(page,bytes("bar-rust/bar-conformance.wasm"),1000,"barc");
  expect(value.loaded.version).toBe(1); expect(value.result).toMatchObject({kind:"result",status:0,output:[66,65,82,49]});
});
test("growth refreshes views and trap stays contained",async({page})=>{
  const grown=await run(page,bytes("synthetic-hostile/grow_memory.wasm")); expect(grown.result.pages).toBeGreaterThan(32);
  const trapped=await run(page,bytes("synthetic-hostile/call_trap.wasm")); expect(trapped.result.kind).toBe("error");
});
test("malformed module is contained in the Worker",async({page})=>{
  const malformed=bytes("sc2-rust/sc2-conformance.wasm").slice(0,31);
  const value=await run(page,malformed);
  expect(value.loaded.kind).toBe("error"); expect(value.result.kind).toBe("error");
});
test("malformed alias avoids unsafe free",async({page})=>{
  const value=await run(page,bytes("synthetic-hostile/malformed_descriptor.wasm")); expect(value.result).toEqual({kind:"refused",freeCount:0});
});
test("free trap stops cleanup and infinite guest is terminable",async({page})=>{
  const free=await run(page,bytes("synthetic-hostile/free_trap.wasm")); expect(free.result).toMatchObject({kind:"error",freeCount:1});
  const started=Date.now(); await expect(run(page,bytes("synthetic-hostile/loop_forever.wasm"),250)).rejects.toThrow("deadline"); expect(Date.now()-started).toBeLessThan(2000);
  await expect(page.evaluate(()=>42)).resolves.toBe(42);
});
