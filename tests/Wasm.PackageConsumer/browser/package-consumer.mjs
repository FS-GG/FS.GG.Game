import "./fable/Program.js";
import { WasmWorkerMechanics } from "./_content/FS.GG.Wasm.Browser/worker-client.mjs";
const bytes = async name => new Uint8Array(await (await fetch(`./modules/${name}`)).arrayBuffer());
window.runGuest = async (name, prefix, version, timeout = 1000) => {
  const mechanics = new WasmWorkerMechanics(new URL("./_content/FS.GG.Wasm.Browser/", import.meta.url));
  try {
    const loaded = await mechanics.load(await bytes(name), prefix, version, timeout);
    const result = await mechanics.invoke("process", new Uint8Array([1,2,3,4]), timeout);
    const shutdown = await mechanics.shutdown(timeout);
    return { loaded, result, shutdown, disposedBefore: mechanics.disposed };
  } finally { mechanics.dispose(); }
};
window.runDeadline = async name => {
  const mechanics = new WasmWorkerMechanics(new URL("./_content/FS.GG.Wasm.Browser/", import.meta.url));
  await mechanics.load(await bytes(name), "sc2c", 0x10000, 1000);
  try { await mechanics.invoke("process", new Uint8Array([1]), 100); return { error: null }; }
  catch (error) { return { error: String(error), disposed: mechanics.disposed }; }
  finally { mechanics.dispose(); mechanics.dispose(); }
};
window.packageConsumerReady = true;
