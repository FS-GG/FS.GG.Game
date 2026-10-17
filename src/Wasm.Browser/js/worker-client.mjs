// Thin Worker/timer transport consumed by Host.CreateConnected. This transport
// owns handles only; F# decides commands, deadlines, identity and settlement.
export function moduleWorkerUrl(assetBaseUrl = import.meta.url) {
  return new URL("module-worker.mjs", assetBaseUrl);
}
export function createHostTransport(assetBaseUrl = import.meta.url, diagnostics) {
  const workers = new Map(), timers = new Map();
  let callback=null,include=null,readFailed=false,truncated=false,events=0,bytes=0,physical=0,workerMessages=0,unavailableWorkers=0,excludedEvents=0;
  try{
    if(typeof diagnostics?.observe==="function")callback=diagnostics.observe;
    if(diagnostics?.include!==undefined){if(typeof diagnostics.include!=="function")throw new Error("unreadable diagnostic scope");include=diagnostics.include;}
  }catch{readFailed=true;}
  const enabled=callback!==null;
  const scalar=value=>{
    if(typeof value==="string"&&value.length<=512||typeof value==="boolean"||value===null
      ||typeof value==="number"&&Number.isFinite(value))return value;
    throw new Error("unreadable diagnostic scalar");
  };
  const record=(stage,fields=()=>({}))=>{
    if(!enabled||readFailed||truncated)return false;
    try{
      const row=Object.freeze({stage,pageMonotonicMilliseconds:performance.now(),...fields()});
      if(include){const selected=include(row);if(typeof selected!=="boolean")throw new Error("unreadable diagnostic scope");if(!selected){excludedEvents++;return false;}}
      const length=new TextEncoder().encode(JSON.stringify(row)).length;
      if(events>=128||length>2048||bytes+length>24576){truncated=true;return false;}
      events++;bytes+=length;callback(row);return true;
    }catch{readFailed=true;return false;}
  };
  const stages=new Set(["handler-ready","load-received","admission-begin","admission-end","sha256-begin","sha256-end",
    "instantiate-begin","instantiate-end","load-completed","terminal","error","status"]);
  const workerDiagnostic=(handle,data)=>{
    // Never forward even a perfectly correlated diagnostic to the reducer.
    // These are advisory observations, not authenticated lifecycle receipts.
    if(!enabled||readFailed||truncated)return;
    try{
      if(workers.get(handle.name)!==handle)return;
      if(!stages.has(data.stage)||!Number.isFinite(data.elapsedMilliseconds)||data.elapsedMilliseconds<handle.lastElapsed)
        throw new Error("unreadable diagnostic stage/clock");
      handle.lastElapsed=data.elapsedMilliseconds;
      if(data.stage==="status"){
        if(!["truncated","read-failed"].includes(data.status))throw new Error("unreadable diagnostic status");
        if(record("worker-stage",()=>({physical:handle.physical,name:scalar(handle.name),workerStage:"status",status:data.status,
          workerElapsedMilliseconds:data.elapsedMilliseconds}))){
          if(data.status==="truncated")truncated=true;else readFailed=true;
        }
        return;
      }
      if(data.status!=="observed")throw new Error("unreadable diagnostic status");
      let identity=null,correlation=null;
      if(data.stage==="handler-ready"){
        if(handle.ready||data.identity!==null||data.correlation!==null)throw new Error("duplicate/foreign diagnostic readiness");
        handle.ready=true;
      }else{
        const command=handle.command,value=data.identity;
        if(!command||!value||value.WorkerInstance!==command.Identity.WorkerInstance||value.Request!==command.Identity.Request
          ||value.Generation!==command.Identity.Generation||data.correlation!==command.Correlation)
          throw new Error("foreign diagnostic request");
        identity=Object.freeze({WorkerInstance:scalar(value.WorkerInstance),Request:scalar(value.Request),Generation:scalar(value.Generation)});
        correlation=scalar(data.correlation);
      }
      if(record("worker-stage",()=>({physical:handle.physical,name:scalar(handle.name),workerStage:data.stage,
        workerElapsedMilliseconds:data.elapsedMilliseconds,identity,correlation})))workerMessages++;
    }catch{readFailed=true;}
  };
  return {
    // This snapshot does not grant readiness, authority or successful cleanup.
    DiagnosticStatus:()=>Object.freeze({state:readFailed?"read-failed":!enabled?"disabled":truncated?"truncated":workerMessages===0?"unavailable":"observed",
      enabled,available:workerMessages>0,readFailed,truncated,events,bytes,excludedEvents,
      incomplete:!enabled||workerMessages===0||readFailed||truncated||unavailableWorkers>0
        ||[...workers.values()].some(value=>!value.ready),unavailableWorkers}),
    Now() {const value=BigInt(Math.floor(performance.now()));record("now",()=>({value:Number(value)}));return value;},
    CreateWorker(name, receive) {
      const url=moduleWorkerUrl(assetBaseUrl);
      if(enabled)url.hash="fsgg-worker-diagnostics-v1";
      const worker = new Worker(url, { type: "module", name });
      const handle={worker,receive,command:null,name,physical:++physical,ready:false,lastElapsed:0};
      worker.onmessage = ({ data }) => {
        if(data?.kind==="fsgg-worker-diagnostic/1"){workerDiagnostic(handle,data);return;}
        receive(data);
      };
      workers.set(name, handle);
      record("worker-created",()=>({physical:handle.physical,name:scalar(name)}));
      worker.onerror = event => {
        event.preventDefault();
        // A retired physical Worker may report an error after a new candidate
        // reuses its logical name. Never borrow that new Worker's command.
        if(workers.get(name)!==handle)return;
        const command=handle.command;
        if(command) receive({Identity:command.Identity,Correlation:command.Correlation,Kind:"failed",Phase:"compile",Digest:"",Version:0,State:"faulted",Status:0,Diagnostic:event.message,CleanupDiagnostic:"",Dispatched:false,Output:new Uint8Array()});
      };
    },
    PostCommand(name, command) {
      const handle=workers.get(name);handle.command=command;handle.worker.postMessage(command);
      record("post",()=>({physical:handle.physical,name:scalar(name),kind:scalar(command.Kind),
        request:scalar(command.Identity.Request),generation:scalar(command.Identity.Generation),correlation:scalar(command.Correlation)}));
    },
    TerminateWorker(name) {
      const handle=workers.get(name);handle?.worker.terminate();workers.delete(name);
      if(handle){if(enabled&&!handle.ready)unavailableWorkers++;record("worker-terminate",()=>({physical:handle.physical,name:scalar(name)}));}
    },
    ArmTimer(key, delay, observe) {
      clearTimeout(timers.get(key));
      timers.set(key, setTimeout(() => { timers.delete(key);record("timer-fired",()=>({key:scalar(key)}));observe(); }, delay));
      record("timer-arm",()=>({key:scalar(key),delay:scalar(delay)}));
    },
    CancelTimer(key) { clearTimeout(timers.get(key)); timers.delete(key);record("timer-cancel",()=>({key:scalar(key)})); }
  };
}
