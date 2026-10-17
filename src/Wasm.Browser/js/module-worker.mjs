// WebAssembly and memory mechanics only. All admission, ownership, limits,
// invocation order and cleanup decisions execute in the packaged F# policy.
import { WorkerEntry_createWireRuntime } from "./policy/WorkerEntry.js";
// Diagnostic messages are advisory, scalar-only and independent of lifecycle
// observations. Worker-local offsets never claim a join to the page clock.
const diagnosticsEnabled=self.location.hash==="#fsgg-worker-diagnostics-v1";
const diagnosticStart=performance.now();
const diagnosticStages=new Set(["handler-ready","load-received","admission-begin","admission-end",
  "sha256-begin","sha256-end","instantiate-begin","instantiate-end","load-completed","terminal","error"]);
let diagnosticEvents=0,diagnosticBytes=0,diagnosticStopped=false;
const diagnosticStatus=status=>{
  if(!diagnosticsEnabled||diagnosticStopped)return;
  diagnosticStopped=true;
  try{self.postMessage({kind:"fsgg-worker-diagnostic/1",stage:"status",status,
    elapsedMilliseconds:performance.now()-diagnosticStart,events:diagnosticEvents,bytes:diagnosticBytes});}catch{}
};
const diagnostic=(stage,wire)=>{
  if(!diagnosticsEnabled||diagnosticStopped)return;
  try{
    if(!diagnosticStages.has(stage))throw new Error("unknown diagnostic stage");
    let identity=null,correlation=null;
    if(wire){
      const value=wire.Identity;
      if(!value||typeof value.WorkerInstance!=="string"||value.WorkerInstance.length<1||value.WorkerInstance.length>128
        ||typeof value.Request!=="string"||typeof value.Generation!=="string"
        ||!/^\d{1,20}$/.test(value.Request)||!/^\d{1,20}$/.test(value.Generation)
        ||typeof wire.Correlation!=="string"||wire.Correlation.length<1||wire.Correlation.length>128)
        throw new Error("unreadable diagnostic identity");
      identity={WorkerInstance:value.WorkerInstance,Request:value.Request,Generation:value.Generation};
      correlation=wire.Correlation;
    }
    const row={kind:"fsgg-worker-diagnostic/1",stage,status:"observed",identity,correlation,
      elapsedMilliseconds:performance.now()-diagnosticStart};
    const bytes=new TextEncoder().encode(JSON.stringify(row)).length;
    if(diagnosticEvents>=63||bytes>768||diagnosticBytes+bytes+512>12288){diagnosticStatus("truncated");return;}
    diagnosticEvents++;diagnosticBytes+=bytes;self.postMessage(row);
  }catch{diagnosticStatus("read-failed");}
};
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
}, observation => {
  if(observation.Kind==="diagnostic"){diagnostic(observation.Phase,observation);return;}
  if(observation.Kind==="loaded")diagnostic("load-completed",observation);
  else if(observation.Kind==="failed")diagnostic("error",observation);
  else if(["outcome","outcome-terminated"].includes(observation.Kind))diagnostic("terminal",observation);
  self.postMessage(observation);
});
self.onmessage = ({ data }) => {
  if(data?.Kind==="load")diagnostic("load-received",data);
  receive(data);
};
diagnostic("handler-ready");
