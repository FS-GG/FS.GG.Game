// Mechanical Worker-side WebAssembly calls. The F# host owns admission,
// scheduling, replacement, settlement, ownership policy and timer decisions.
let instance = null;
let prefix = null;

function exportsForCurrent() {
  if (!instance) throw new Error("guest is not loaded");
  return instance.exports;
}
function span(pointer, length, memory) {
  const end = pointer + length;
  return Number.isSafeInteger(end) && pointer >= 0 && length >= 0 &&
    (length === 0 || pointer !== 0) && end <= memory.buffer.byteLength;
}
function overlaps(a, b) {
  return a[1] !== 0 && b[1] !== 0 && a[0] < b[0] + b[1] && b[0] < a[0] + a[1];
}
function callable(exports, name) {
  const value = exports[name];
  if (typeof value !== "function") throw new TypeError(`missing callable export: ${name}`);
  return value;
}
function reply(requestId, value) { self.postMessage({ requestId, ok: true, value }); }
function refuse(requestId, error) { self.postMessage({ requestId, ok: false, error: String(error) }); }

self.onmessage = async ({ data }) => {
  const { requestId, kind } = data;
  try {
    if (kind === "load") {
      const result = await WebAssembly.instantiate(data.bytes, Object.create(null));
      instance = result.instance;
      prefix = data.prefix;
      const version = callable(instance.exports, `${prefix}_abi_version`)() >>> 0;
      if (version !== data.expectedVersion) throw new Error(`ABI version ${version} does not match ${data.expectedVersion}`);
      reply(requestId, { version });
      return;
    }
    if (kind === "invoke") {
      const exports = exportsForCurrent();
      const memory = exports.memory;
      if (!(memory instanceof WebAssembly.Memory)) throw new TypeError("missing exported memory");
      const allocate = callable(exports, `${prefix}_alloc`);
      const free = callable(exports, `${prefix}_free`);
      const call = callable(exports, `${prefix}_${data.call}`);
      const inputBytes = new Uint8Array(data.input);
      const descriptor = allocate(8) >>> 0;
      const input = allocate(inputBytes.length) >>> 0;
      if (!span(descriptor, 8, memory) || !span(input, inputBytes.length, memory) || overlaps([descriptor, 8], [input, inputBytes.length])) {
        throw new Error("allocator returned an invalid or aliased owned span");
      }
      new Uint8Array(memory.buffer, descriptor, 8).fill(0);
      new Uint8Array(memory.buffer, input, inputBytes.length).set(inputBytes);
      const status = call(input, inputBytes.length, descriptor) | 0;
      const view = new DataView(memory.buffer, descriptor, 8);
      const output = view.getUint32(0, true), length = view.getUint32(4, true);
      if (!span(output, length, memory) || overlaps([descriptor, 8], [output, length]) || overlaps([input, inputBytes.length], [output, length])) {
        throw new Error("guest returned an invalid or aliased output span");
      }
      const copied = new Uint8Array(memory.buffer, output, length).slice();
      if (length) free(output, length);
      free(input, inputBytes.length);
      free(descriptor, 8);
      reply(requestId, { status, output: Array.from(copied) });
      return;
    }
    if (kind === "shutdown") {
      const exports = exportsForCurrent();
      const status = callable(exports, `${prefix}_shutdown`)() | 0;
      instance = null; prefix = null;
      reply(requestId, { status });
      return;
    }
    throw new Error(`unsupported worker mechanics command: ${kind}`);
  } catch (error) { refuse(requestId, error); }
};
