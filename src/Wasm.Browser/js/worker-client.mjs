// Thin Worker/timer transport consumed by Host.CreateConnected. This transport
// owns handles only; F# decides commands, deadlines, identity and settlement.
export function moduleWorkerUrl(assetBaseUrl = import.meta.url) {
  return new URL("module-worker.mjs", assetBaseUrl);
}
export function createHostTransport(assetBaseUrl = import.meta.url) {
  const workers = new Map(), timers = new Map();
  return {
    Now: () => BigInt(Math.floor(performance.now())),
    CreateWorker(name, receive) {
      const worker = new Worker(moduleWorkerUrl(assetBaseUrl), { type: "module", name });
      worker.onmessage = ({ data }) => receive(data);
      workers.set(name, { worker, receive, command:null });
      worker.onerror = event => {
        event.preventDefault();
        const command=workers.get(name)?.command;
        if(command) receive({Identity:command.Identity,Correlation:command.Correlation,Kind:"failed",Phase:"compile",Digest:"",Version:0,State:"faulted",Status:0,Diagnostic:event.message,CleanupDiagnostic:"",Dispatched:false,Output:new Uint8Array()});
      };
    },
    PostCommand(name, command) { const handle=workers.get(name); handle.command=command; handle.worker.postMessage(command); },
    TerminateWorker(name) { workers.get(name)?.worker.terminate(); workers.delete(name); },
    ArmTimer(key, delay, observe) {
      clearTimeout(timers.get(key));
      timers.set(key, setTimeout(() => { timers.delete(key); observe(); }, delay));
    },
    CancelTimer(key) { clearTimeout(timers.get(key)); timers.delete(key); }
  };
}
