import { test } from "node:test";
import assert from "node:assert/strict";
import { validateClosedModule } from "./closed-module.mjs";

test("closed module accepts ordinary ESM native mechanics without dependencies",()=>{
  assert.equal(validateClosedModule("const memory = new Uint8Array(8); export { memory }; self.onmessage = () => memory.fill(0);").externalImports,0);
});
test("authoritative admission's locally bound require is not a runtime import",()=>{
  assert.equal(validateClosedModule("function inspect(bytes) { const require = count => count <= bytes.length; return require(1); }").externalImports,0);
});
for(const source of [
  'import { x } from "./missing.js";',
  'export * from "./missing.js";',
  'export { x } from "./missing.js";',
  'async function nested() { return import("./missing.js"); }',
  'function nested() { return eval("1"); }',
  'function nested() { return (0, eval)("1"); }',
  'function nested() { return globalThis.eval("1"); }',
  'function nested() { return new Function("return 1"); }',
  'function nested() { return Function`return 1`; }',
  'function nested() { return require("missing"); }',
  'function local() { const require = x => x; return require(1); } function other() { return require("missing"); }',
  'const F = Function; new F("return 1");',
  'const E = globalThis["eval"]; E("1");',
  'self.onmessage = () => fetch("https://example.invalid");',
  'self.onmessage = () => importScripts("elsewhere.js");',
  'self.onmessage = () => new WebSocket("wss://example.invalid");',
  'self.onmessage = () => new XMLHttpRequest();'
])test(`refuses runtime dependency/execution: ${source}`,()=>assert.throws(()=>validateClosedModule(source)));
