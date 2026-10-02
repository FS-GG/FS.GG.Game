let instance, prefix;
function span(pointer, length, memory) {
  const end = pointer + length;
  return Number.isSafeInteger(end) && (length === 0 || pointer !== 0) && end <= memory.buffer.byteLength;
}
function overlap(a,b) { return a[1] !== 0 && b[1] !== 0 && a[0] < b[0]+b[1] && b[0] < a[0]+a[1]; }
self.onmessage = async ({data}) => {
  try {
    if (data.kind === "load") {
      instance = await WebAssembly.instantiate(data.bytes, {});
      prefix = data.prefix;
      self.postMessage({kind:"loaded", version: instance.instance.exports[`${prefix}_abi_version`]()});
      instance = instance.instance;
      return;
    }
    const x=instance.exports, memory=x.memory;
    const allocate=x[`${prefix}_alloc`], process=x[`${prefix}_process`], free=x[`${prefix}_free`];
    const descriptor=allocate(8), input=allocate(data.input.length);
    new Uint8Array(memory.buffer, descriptor, 8).fill(0);
    new Uint8Array(memory.buffer, input, data.input.length).set(data.input);
    const status=process(input,data.input.length,descriptor);
    const view=new DataView(memory.buffer,descriptor,8), output=view.getUint32(0,true), length=view.getUint32(4,true);
    const valid=span(descriptor,8,memory)&&span(input,data.input.length,memory)&&span(output,length,memory)
      && !overlap([descriptor,8],[input,data.input.length]) && !overlap([descriptor,8],[output,length]) && !overlap([input,data.input.length],[output,length]);
    if (!valid) { self.postMessage({kind:"refused",freeCount:x.test_free_count?.()??0}); return; }
    const copied=new Uint8Array(memory.buffer,output,length).slice();
    for (const [p,n] of [[output,length],[input,data.input.length],[descriptor,8]]) free(p,n);
    self.postMessage({kind:"result",status,output:Array.from(copied),pages:memory.buffer.byteLength/65536,freeCount:x.test_free_count?.()??0});
  } catch(error) { self.postMessage({kind:"error",message:String(error),freeCount:instance?.exports?.test_free_count?.()??0}); }
};
