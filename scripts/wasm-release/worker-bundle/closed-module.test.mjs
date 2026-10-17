import { test } from "node:test";
import assert from "node:assert/strict";
import { validateClosedModule } from "./closed-module.mjs";

test("closed module accepts ordinary ESM native mechanics without dependencies",()=>{
  assert.equal(validateClosedModule("const memory = new Uint8Array(8); export { memory }; self.onmessage = () => memory.fill(0);").externalImports,0);
});
test("authoritative admission's locally bound require is not a runtime import",()=>{
  assert.equal(validateClosedModule("function inspect(bytes) { const require = count => count <= bytes.length; return require(1); }").externalImports,0);
});
test("pinned Fable Encoding const global alias retains only its selected Buffer member",()=>{
  assert.equal(validateClosedModule('function encode(x) { const g=globalThis; const h=g; return typeof h.Buffer !== "undefined" ? h.Buffer.from(x) : new Uint8Array(); }').externalImports,0);
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
  ,'const g=globalThis; g.fetch("https://example.invalid");'
  ,'const g=globalThis; const h=g; h.eval("1");'
  ,'const {fetch:f}=globalThis; f("https://example.invalid");'
  ,'const g=globalThis; send(g);'
  ,'const g=globalThis; const property="fetch"; g[property]("https://example.invalid");'
  ,'let g=globalThis; g=other;'
  ,'const g=self; g.importScripts("elsewhere.js");'
  ,'const g=globalThis.self; g.fetch("https://example.invalid");'
])test(`refuses runtime dependency/execution: ${source}`,()=>assert.throws(()=>validateClosedModule(source)));
