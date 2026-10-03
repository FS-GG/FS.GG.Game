import * as bridge from './fable/BrowserBridge.js';
import {createHostTransport} from './_content/FS.GG.Wasm.Browser/worker-client.mjs';
async function session(){
  const artifact=new Uint8Array(await(await fetch('./limits-guest.wasm')).arrayBuffer());
  const digest=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',artifact)),x=>x.toString(16).padStart(2,'0')).join('');
  const transport=createHostTransport(new URL('./_content/FS.GG.Wasm.Browser/',import.meta.url));
  const waiting=new Map(),results=[],posts=[];let workers=0;
  const create=transport.CreateWorker;transport.CreateWorker=(...args)=>{workers++;create(...args);};
  const post=transport.PostCommand;transport.PostCommand=(name,command)=>{posts.push({name,request:command.Identity.Request,kind:command.Kind});post(name,command);};
  const host=bridge.create(digest,transport,result=>{results.push({...result,Output:Array.from(result.Output)});waiting.get(result.Request)?.(result);waiting.delete(result.Request);});
  const send=(id,call)=>new Promise((resolve,reject)=>{waiting.set(String(id),resolve);try{call(transport.Now());}catch(error){waiting.delete(String(id));reject(error);}});
  const loaded=await send(1,now=>bridge.load(host,now,'active',1n,1n,digest,artifact,250));
  const initialized=await send(2,now=>bridge.initialize(host,now,'active',2n,1n,new Uint8Array([44]),250,44));
  return {host,transport,send,results,posts,digest,artifact,loaded,initialized,workers:()=>workers,dispose:()=>bridge.dispose(host,transport.Now())};
}
window.runLimits=async overcap=>{
  const s=await session();try{
    const result=await s.send(3,now=>bridge.invoke(s.host,now,'active',3n,1n,0,new Uint8Array([overcap?65:60]),250,64,undefined));
    return {loaded:s.loaded.State,init:s.initialized.State,initBytes:s.initialized.Output.length,state:result.State,output:Array.from(result.Output),workers:s.workers(),active:bridge.projection(s.host).ActiveWorker};
  }finally{s.dispose();}
};
window.runFifo=async()=>{
  const s=await session();try{
    const calls=[0,1,0].map((kind,index)=>s.send(index+3,now=>bridge.invoke(s.host,now,'active',BigInt(index+3),1n,kind,new Uint8Array([60+index]),250,64,undefined)));
    const results=await Promise.all(calls);return {posts:s.posts.filter(p=>p.kind==='process').map(p=>p.request),outputs:results.map(r=>r.Output.length),states:results.map(r=>r.State)};
  }finally{s.dispose();}
};
window.runFrozenCommit=async()=>{
  const s=await session();try{
    await s.send(7,now=>bridge.candidate(s.host,now,'candidate',7n,2n,s.digest,s.artifact));
    await s.send(8,now=>bridge.initialize(s.host,now,'candidate',8n,2n,new Uint8Array([44]),250,44));
    bridge.validate(s.host,s.transport.Now());
    const active=s.send(3,now=>bridge.invoke(s.host,now,'active',3n,1n,0,new Uint8Array([60]),250,64,undefined));
    const queued=[1,2,2].map((kind,index)=>s.send(index+4,now=>bridge.invoke(s.host,now,'active',BigInt(index+4),1n,kind,new Uint8Array([60]),250,64,undefined)));
    bridge.freeze(s.host,s.transport.Now());
    const before=JSON.stringify(bridge.projection(s.host)),wrong=bridge.commit(s.host,s.transport.Now(),'wrong'),unchanged=before===JSON.stringify(bridge.projection(s.host));
    const accepted=bridge.commit(s.host,s.transport.Now(),'fence'),frozen=bridge.projection(s.host).Frozen;
    const historical=await active,drained=await Promise.all(queued);
    bridge.resume(s.host,s.transport.Now());
    const fresh=await s.send(10,now=>bridge.invoke(s.host,now,'candidate',10n,2n,0,new Uint8Array([60]),250,64,undefined));
    return {wrong,unchanged,accepted,frozen,historical:historical.Disposition,drained:drained.map(r=>({dispatched:r.Dispatched,disposition:r.Disposition})),posts:s.posts.filter(p=>p.kind==='process').map(p=>p.request),fresh:fresh.State};
  }finally{s.dispose();}
};
window.runExpiredAdmission=async()=>{const s=await session();try{const before=s.posts.length;return {refused:bridge.expiredAdmission(s.host,s.transport.Now()),postsUnchanged:s.posts.length===before};}finally{s.dispose();}};
window.compatibleReady=true;
