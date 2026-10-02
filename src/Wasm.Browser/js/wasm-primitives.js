// Mechanical WebAssembly/Worker primitives. Admission, profile selection,
// ownership and cleanup order are controlled by the F# worker entry.
export async function sha256Hex(bytes) {
  const copy = bytes instanceof Uint8Array ? bytes.slice() : new Uint8Array(bytes);
  const digest = await crypto.subtle.digest("SHA-256", copy);
  return Array.from(new Uint8Array(digest), value => value.toString(16).padStart(2, "0")).join("");
}

export async function compileAdmitted(bytes) {
  const copy = bytes instanceof Uint8Array ? bytes.slice() : new Uint8Array(bytes);
  return WebAssembly.compile(copy);
}

export async function instantiateCompiled(module) {
  return WebAssembly.instantiate(module, Object.create(null));
}

export function memoryByteLength(memory) {
  return BigInt(memory.buffer.byteLength);
}

export function zeroFresh(memory, pointer, length) {
  new Uint8Array(memory.buffer, pointer, length).fill(0);
}

export function copyIntoFresh(memory, pointer, bytes) {
  new Uint8Array(memory.buffer, pointer, bytes.length).set(bytes);
}

export function copyOutFresh(memory, pointer, length) {
  return new Uint8Array(memory.buffer, pointer, length).slice();
}

export function readDescriptorFresh(memory, pointer) {
  const view = new DataView(memory.buffer, pointer, 8);
  return [view.getUint32(0, true), view.getUint32(4, true)];
}

export function invokeI32(exports, name, args) {
  const fn = exports[name];
  if (typeof fn !== "function") throw new TypeError(`missing callable export: ${name}`);
  return fn(...args) | 0;
}

export function createModuleWorker(url, name) {
  return new Worker(url, { type: "module", name });
}

export function terminateWorker(worker) { worker.terminate(); }
export function postWorkerMessage(worker, message, transfer) { worker.postMessage(message, transfer || []); }
