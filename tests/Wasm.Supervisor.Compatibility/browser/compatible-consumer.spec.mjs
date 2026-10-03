import {test,expect} from '@playwright/test';
test.beforeEach(async({page})=>{await page.goto('http://127.0.0.1:41785/index.html');await expect.poll(()=>page.evaluate(()=>window.compatibleReady===true)).toBe(true);});
test('same immutable loaded guest supports narrow initialize then larger process allowance',async({page})=>{
 const r=await page.evaluate(()=>window.runLimits(false));expect(r).toMatchObject({loaded:'Succeeded',init:'Succeeded',initBytes:44,state:'Succeeded',workers:1,active:'active'});expect(r.output).toHaveLength(60);
});
test('actual guest output above selected request allowance is rejected without output',async({page})=>{
 const r=await page.evaluate(()=>window.runLimits(true));expect(r.state).toContain('Faulted');expect(r.output).toEqual([]);expect(r.active).toBe('');
});
test('actual Worker posts preserve mixed FIFO admission order',async({page})=>{
 const r=await page.evaluate(()=>window.runFifo());expect(r).toEqual({posts:['3','4','5'],outputs:[60,61,62],states:['Succeeded','Succeeded','Succeeded']});
});
test('atomic matching-token promotion retains freeze and never posts invalidated old work',async({page})=>{
 const r=await page.evaluate(()=>window.runFrozenCommit());expect(r).toMatchObject({wrong:false,unchanged:true,accepted:true,frozen:true,historical:'HistoricalOnly',posts:['3','10'],fresh:'Succeeded'});expect(r.drained.every(x=>!x.dispatched&&x.disposition==='Discarded')).toBe(true);
});
test('expired enclosing budget admits no further actual guest work',async({page})=>{
 expect(await page.evaluate(()=>window.runExpiredAdmission())).toEqual({refused:true,postsUnchanged:true});
});

for(const initialize of [true,false])test(`selected ${initialize?'10 ms initialize':'5 ms process'} deadline contains actual looping guest`,async({page})=>{
 const r=await page.evaluate(initialize=>window.runSelectedDeadline(initialize),initialize);expect(r.state).toBe('TimedOut');expect(r.output).toEqual([]);expect(r.active).toBe('');
});
test('absolute enclosing deadline contains a looping guest within a larger selected request ceiling',async({page})=>{
 const r=await page.evaluate(()=>window.runEnclosingDeadline());expect(r).toEqual({state:'TimedOut',output:[],active:''});
});
