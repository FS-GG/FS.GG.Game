// WebAssembly and memory mechanics only. All admission, ownership, limits,
// invocation order and cleanup decisions execute in the packaged F# policy.
import { WorkerEntry_createWireRuntime } from "./policy/WorkerEntry.js";
let instance = null;
let memoryExport = "memory";
const memory = () => instance.exports[memoryExport];
const call = (name, args) => {
  const fn = instance?.exports[name];
  if (typeof fn !== "function") throw new TypeError(`missing callable export: ${name}`);
  return fn(...args) | 0;
};
const perform = effect => {
  const reply = { Pointer: 0, MemoryBytes: "0", Status: 0, OutputPointer: 0,
    OutputLength: 0, Bytes: new Uint8Array(), Diagnostic: "" };
  try {
    switch (effect.Kind) {
      case "allocate": reply.Pointer = call(effect.Export, [effect.Length]) >>> 0; break;
      case "zero": new Uint8Array(memory().buffer, effect.Pointer, effect.Length).fill(0); break;
      case "copy-input": new Uint8Array(memory().buffer, effect.Pointer, effect.Bytes.length).set(effect.Bytes); break;
      case "invoke": reply.Status = call(effect.Export, [effect.Pointer, effect.Length, effect.Descriptor]); break;
      case "read": {
        const view = new DataView(memory().buffer, effect.Pointer, 8);
        reply.OutputPointer = view.getUint32(0, true); reply.OutputLength = view.getUint32(4, true); break;
      }
      case "copy-output": reply.Bytes = new Uint8Array(memory().buffer, effect.Pointer, effect.Length).slice(); break;
      case "free": call(effect.Export, [effect.Pointer, effect.Length]); break;
      case "shutdown": reply.Status = call(effect.Export, []); break;
      case "terminate": instance = null; break;
      default: throw new Error(`unsupported mechanical effect: ${effect.Kind}`);
    }
  } catch (error) { reply.Diagnostic = String(error); }
  if (instance) reply.MemoryBytes = String(memory().buffer.byteLength);
  return reply;
};
const receive = WorkerEntry_createWireRuntime({
  Sha256(bytes, ok, fail) {
    crypto.subtle.digest("SHA-256", new Uint8Array(bytes)).then(
      hash => ok(Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, "0")).join("")),
      error => fail(String(error)));
  },
  Compile(bytes, exportedMemory, versionExport, ok, fail) {
    WebAssembly.instantiate(new Uint8Array(bytes), Object.create(null)).then(value => {
      try { instance = value.instance; memoryExport = exportedMemory; ok(call(versionExport, []) >>> 0); }
      catch (error) { instance = null; fail(String(error)); }
    }, error => fail(String(error)));
  },
  Perform: perform
}, observation => self.postMessage(observation));
self.onmessage = ({ data }) => receive(data);
