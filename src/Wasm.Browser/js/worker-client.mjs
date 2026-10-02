// Thin main-thread mechanics for Lifecycle effects. Callers supply each bounded
// timeout from the production F# host policy.
export function moduleWorkerUrl(assetBaseUrl = import.meta.url) {
  return new URL("module-worker.mjs", assetBaseUrl);
}

export class WasmWorkerMechanics {
  #worker; #next = 1; #pending = new Map(); #disposed = false;
  constructor(assetBaseUrl = import.meta.url) {
    this.#worker = new Worker(moduleWorkerUrl(assetBaseUrl), { type: "module", name: "fsgg-wasm-package" });
    this.#worker.onmessage = ({ data }) => {
      const pending = this.#pending.get(data.requestId);
      if (!pending) return;
      this.#pending.delete(data.requestId); clearTimeout(pending.timer);
      data.ok ? pending.resolve(data.value) : pending.reject(new Error(data.error));
    };
    this.#worker.onerror = event => this.#failAll(new Error(event.message || "module Worker failed"));
  }
  #failAll(error) {
    for (const pending of this.#pending.values()) { clearTimeout(pending.timer); pending.reject(error); }
    this.#pending.clear();
  }
  #send(message, timeoutMilliseconds) {
    if (this.#disposed) return Promise.reject(new Error("worker mechanics disposed"));
    if (!Number.isSafeInteger(timeoutMilliseconds) || timeoutMilliseconds <= 0) return Promise.reject(new Error("positive finite timeout required"));
    const requestId = this.#next++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.#pending.delete(requestId); this.#worker.terminate(); this.#disposed = true;
        reject(new Error("deadline")); this.#failAll(new Error("worker terminated after deadline"));
      }, timeoutMilliseconds);
      this.#pending.set(requestId, { resolve, reject, timer });
      this.#worker.postMessage({ requestId, ...message });
    });
  }
  load(bytes, prefix, expectedVersion, timeoutMilliseconds) {
    const owned = bytes instanceof Uint8Array ? bytes.slice() : new Uint8Array(bytes);
    return this.#send({ kind: "load", bytes: owned, prefix, expectedVersion }, timeoutMilliseconds);
  }
  invoke(call, input, timeoutMilliseconds) {
    const owned = input instanceof Uint8Array ? input.slice() : new Uint8Array(input);
    return this.#send({ kind: "invoke", call, input: owned }, timeoutMilliseconds);
  }
  shutdown(timeoutMilliseconds) { return this.#send({ kind: "shutdown" }, timeoutMilliseconds); }
  dispose() {
    if (this.#disposed) return;
    this.#disposed = true; this.#worker.terminate(); this.#failAll(new Error("worker mechanics disposed"));
  }
  get disposed() { return this.#disposed; }
}
