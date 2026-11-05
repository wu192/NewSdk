const fs=require('node:fs/promises');
const path=require('node:path');
const assert=require('node:assert/strict');
const {performance}=require('node:perf_hooks');
const {AssetIndex}=require('../out/src/fx/assetIndex');
const {AssetResolver}=require('../out/src/fx/assetResolver');
const {ParticleWorld}=require('../out/webview/fx/runtime');
const full=process.env.NEWSDK_FX_STRESS==='full';
const definition=s=>({id:s.key,templateKey:s.templateKey,sourceId:s.key,orientToObject:s.orientToObject,createAtGroundHeight:s.createAtGroundHeight,config:s.config,options:{offset:s.offset,rotation:s.rotation,rotationXYZ:s.rotationXYZ,rotationMatrix:s.rotationMatrix,startFrame:s.startFrame,systemLifeFrames:s.systemLifeFrames,initialDelayOverrideMs:s.initialDelayOverrideMs,createAtGroundHeight:s.createAtGroundHeight}});
const percentile=(values,p)=>values.slice().sort((a,b)=>a-b)[Math.min(values.length-1,Math.floor(values.length*p))];
function create(ir){return new ParticleWorld(ir.systems.filter(s=>s.config&&s.triggered).map(definition),ir.scene.seed,32768,18000,{...ir.dynamic,templates:ir.dynamic.templates.filter(s=>s.config).map(definition),fxLists:ir.dynamic.fxLists.map(l=>({...l,systems:l.systems.filter(s=>s.config&&s.triggered).map(definition)})),groundHeight:ir.scene.ground,sourceMotion:ir.scene.sourceMotion,snapshotBytes:16*1024*1024});}
function signature(world){return JSON.stringify(world.systems.map(s=>({id:s.id,frame:s.runtime.frame,random:s.runtime.random.calls,particles:s.runtime.particles.map(p=>({id:p.id,sample:s.runtime.sample(p,world.frame)}))})));}
(async()=>{
 const output=path.resolve(__dirname,'../test-results');await fs.mkdir(output,{recursive:true});
 const root=path.resolve(__dirname,'fixtures/fx');const index=new AssetIndex({roots:[root],modRoot:root});
 let world;
 const report={passed:false,mode:full?'full':'smoke',environment:{node:process.version,platform:process.platform,scope:'Node asset/runtime controlled allocations and RSS; not whole VS Code or hardware GPU FPS'},checks:[]};
 try{
  await index.load(path.join(root,'Data/Round2Mod.xml'));
  const asset=index.listAssets().find(a=>a.id==='Round2Acceptance');assert.ok(asset);
  const resolver=new AssetResolver(index);const ir=await resolver.resolve(asset.key);
  assert.ok(ir.dynamic,'Event/attachment catalog must be present');
  for(const s of ir.systems)assert.ok(s.config,`${s.name}: ${s.issues.map(i=>i.message).join(';')}`);
  const timings=[];for(let i=0;i<20;i++){const start=performance.now();await resolver.resolve(asset.key);timings.push(performance.now()-start);}
  report.warmResolveP95Ms=percentile(timings,.95);
  world=create(ir);let dynamicSeen=false,attachmentSeen=false;
  for(let i=0;i<90;i++){world.step();dynamicSeen ||= world.systems.some(s=>s.dynamic&&s.runtime.config.id==='Flash');attachmentSeen ||= world.attachmentHandles.size>0;}
  assert.ok(dynamicSeen,'Life EventFX must create systems');assert.ok(attachmentSeen,'CPU births must create attached systems');assert.equal(world.budgetExceeded,undefined);
  report.checks.push('ordinary CPU/GPU EventFX and attachment scheduling');
  await world.seekAsync(30);const first=signature(world);
  await world.seekAsync(full?18000:3600);assert.equal(world.budgetExceeded,undefined);
  assert.ok(world.snapshotBytes<=16*1024*1024);report.snapshotBytes=world.snapshotBytes;
  await world.seekAsync(30);assert.equal(signature(world),first,'Whole-world seek must restore CPU state, RNG, events and handles');
  report.checks.push('bounded long replay and deterministic rewind');
  const pending=world.seekAsync(18000);await world.seekAsync(12);await assert.rejects(pending,/取消|cancel/i);
  const independent=create(ir);independent.seek(12);assert.equal(signature(world),signature(independent));independent.dispose();
  report.checks.push('superseded seek cancellation keeps latest state');
  world.dispose();assert.equal(world.systems.length,0);assert.equal(world.attachmentHandles.size,0);assert.equal(world.snapshotBytes,0);
  const moving=structuredClone(ir);moving.scene.sourceMotion={playback:'pingPong',points:[{frame:0,offset:[0,0,0]},{frame:150,offset:[60,30,5]}]};
  world=create(moving);await world.seekAsync(60);const movingState=signature(world);
  assert.ok(world.systems.some(s=>!s.dynamic&&s.runtime.options.offset[0]!==ir.systems.find(d=>d.key===s.sourceId)?.offset[0]));
  await world.seekAsync(full?18000:3600);await world.seekAsync(60);assert.equal(signature(world),movingState);
  assert.equal(world.budgetExceeded,undefined);world.dispose();
  report.checks.push('source trajectory mixed CPU/GPU, event and attached world replays deterministically');
  if(global.gc)global.gc();const baseline=process.memoryUsage();const seekTimes=[];
  for(let i=0;i<100;i++){
   world=create(i%2?moving:ir);const start=performance.now();await world.seekAsync(120+(i%5));seekTimes.push(performance.now()-start);
   assert.equal(world.budgetExceeded,undefined);world.dispose();assert.equal(world.systems.length,0);assert.equal(world.snapshotBytes,0);assert.equal(world.attachmentHandles.size,0);
   if(i%10===0&&global.gc)global.gc();
  }
  if(global.gc)global.gc();const after=process.memoryUsage();
  report.memory={baseline,after,heapDelta:after.heapUsed-baseline.heapUsed,rssDelta:after.rss-baseline.rss,arrayBufferDelta:after.arrayBuffers-baseline.arrayBuffers};
  assert.ok(report.memory.heapDelta<64*1024*1024,'Retained controlled heap must not grow across 100 disposed worlds');
  report.seekP95Ms=percentile(seekTimes,.95);report.switches=100;report.checks.push('100 mixed CPU/GPU world lifecycles release particles, handles and snapshots');
  const emitter=ir.systems.find(s=>s.config.events?.length);assert.ok(emitter);
  const recursive=structuredClone(emitter.config);recursive.events[0].eventTime={low:3,high:3,type:'CONSTANT'};recursive.burstCount={low:1,high:1,type:'CONSTANT'};
  const loopDef={id:'loop',templateKey:'loop',sourceId:'loop',config:recursive};
  const loop=new ParticleWorld([loopDef],1,1000,18000,{maxDepth:4,maxSystems:16,maxEventsPerFrame:8,templates:[],fxLists:[{key:recursive.events[0].fx,systems:[loopDef],triggered:true,cullTrackingFrames:0,cullTrackingMin:0,cullTrackingMax:0}]});
  for(let i=0;i<100&&!loop.budgetExceeded;i++)loop.step();assert.ok(loop.budgetExceeded,'Recursive event graph must pause at an explicit budget');report.recursionBudget=loop.budgetExceeded;loop.dispose();report.checks.push('recursive graph remains queued and bounded');
  report.passed=true;await fs.writeFile(path.join(output,'fx-stress.json'),JSON.stringify(report,null,2));console.log(`FX stress ${report.mode}: ${report.checks.length} checks, 100 lifecycles; seek P95 ${report.seekP95Ms.toFixed(2)}ms`);
 }catch(e){report.error=String(e.stack||e);await fs.writeFile(path.join(output,'fx-stress.json'),JSON.stringify(report,null,2));throw e;}
 finally{world?.dispose();index.dispose();}
})().catch(e=>{console.error(e);process.exitCode=1;});
