const assert=require('node:assert/strict');
const path=require('node:path');
const fs=require('node:fs/promises');
const http=require('node:http');
const esbuild=require('esbuild');
const {chromium}=require('@playwright/test');
const {AssetIndex}=require('../out/src/fx/assetIndex');
const {AssetResolver}=require('../out/src/fx/assetResolver');
(async()=>{
  const bundle=await esbuild.build({entryPoints:[path.join(__dirname,'fxRenderRound2Harness.ts')],bundle:true,write:false,platform:'browser',target:'chrome120'});
  const server=http.createServer((req,res)=>{res.setHeader('Content-Type',req.url==='/test.js'?'text/javascript':'text/html');res.end(req.url==='/test.js'?bundle.outputFiles[0].contents:'<!doctype html><style>body{margin:0}canvas{width:640px;height:360px}</style><canvas></canvas><script src="/test.js"></script>');});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  let browser,page;
  const output=path.resolve(__dirname,'../test-results');await fs.mkdir(output,{recursive:true});
  try{
    browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage']});
    page=await browser.newPage();const errors=[];page.on('pageerror',e=>errors.push(String(e)));
    await page.goto(`http://127.0.0.1:${server.address().port}`);await page.waitForFunction(()=>window.fxRound2);
    const load=patch=>page.evaluate(p=>window.fxRound2.load(p),patch);
    const base=await load({});assert.equal(base.error,0);
    for(const geometryType of ['CENTERED_QUAD','TWO_CONCENTRIC_QUADS']){const subdivided=await load({geometryType});assert.equal(subdivided.hash,base.hash,`${geometryType} must preserve the same plane and UV`);}
    const sample=[128,192,64].map(n=>n/255),tint=[.4,.6,.8],bg=[32,48,64],alpha=128/255*.5;
    for(const backend of ['GPU','CPU'])for(const shader of ['ADDITIVE','ALPHA','MULTIPLY']){
      const result=await load({backend,shader,shaderProfile:backend==='CPU'?'CPU_DEFAULT':'GPU_STANDARD'});
      assert.equal(result.error,0);
      const source=sample.map((v,i)=>backend==='CPU'?v**2.2*tint[i]:(v*(shader==='MULTIPLY'?1-tint[i]:tint[i]))**2.2);
      const expected=source.map((s,i)=>Math.round(shader==='ADDITIVE'?Math.min(255,bg[i]+s*255):shader==='ALPHA'?s*255*alpha+bg[i]*(1-alpha):backend==='CPU'?s*bg[i]:bg[i]*(1-s)));
      result.center.slice(0,3).forEach((value,i)=>assert.ok(Math.abs(value-expected[i])<=2,`${backend}/${shader}: ${result.center} expected ${expected}`));
    }
    const cpuBase=await load({backend:'CPU',shaderProfile:'CPU_DEFAULT'});
    const cpuSubframe=await page.evaluate(()=>window.fxRound2.draw(.5));assert.equal(cpuSubframe.stats.instanceUploads,0,'CPU current state uploads follow logic ticks rather than monitor subframes');
    const cpuLate=await page.evaluate(()=>window.fxRound2.draw(20));assert.equal(cpuLate.hash,cpuBase.hash,'CPU current state must not run GPU age/physics a second time');
    await load({shader:'ALPHA'});const alphaStable=await page.evaluate(()=>window.fxRound2.draw(1));assert.equal(alphaStable.stats.instanceUploads,0,'Unchanged transparent sort order must retain GPU birth buffers');
    const range=low=>({low,high:low,type:'CONSTANT'});
    const negativeAlpha=await load({alphas:[0,.33,.66,1].map(time=>({time,range:range(-2)}))});assert.deepEqual(negativeAlpha.center.slice(0,3),base.center.slice(0,3),'Disabled alpha test must not discard additive RGB for finite negative alpha');
    const geometryUpdate={sizeStart:[range(12),range(12),range(12)],sizeRate:[range(0),range(0),range(0)],sizeDamping:[range(1),range(1),range(1)],angle:[range(.3),range(.5),range(.7)],angleRate:[range(0),range(0),range(0)],angleDamping:range(1),rotation:'ROTATE_IN_WORLD_SPACE'};
    const perpendicular=await load({geometryType:'TWO_QUADS',shaderProfile:'GPU_PERPENDICULAR_BOTTOM',geometryUpdate});assert.equal(perpendicular.error,0);assert.notEqual(perpendicular.hash,base.hash);
    for(const rotation of ['ROTATION_OFF','ROTATE_AROUND_VELOCITY'])assert.equal((await load({geometryType:'TWO_QUADS',shaderProfile:'GPU_PERPENDICULAR_BOTTOM',geometryUpdate:{...geometryUpdate,rotation}})).error,0);
    for(const [file,ids] of [['geometry.xml',['CenteredFlash','ConcentricFlash','PerpendicularFlash']],['cpu.xml',['CpuDefault']]]){
      const root=path.resolve(__dirname,'fixtures/fx'),index=new AssetIndex({roots:[root],modRoot:root});
      try{
        await index.load(path.join(root,'Data',file));
        for(const id of ids){
          const asset=index.listAssets().find(a=>a.id===id);assert.ok(asset,`Missing ${id}`);
          const ir=await new AssetResolver(index).resolve(asset.key),system=ir.systems.find(s=>s.config);
          assert.ok(system,`${id}: ${JSON.stringify(ir.issues)}`);assert.equal((await load(system.config)).error,0,`${id} fixture shader`);
        }
      }finally{index.dispose();}
    }
    await load({shader:'ALPHA'});
    const hard=await page.evaluate(()=>window.fxRound2.draw(0,true,false));
    const soft=await page.evaluate(()=>window.fxRound2.draw(0,true,true));
    assert.equal(soft.error,0);assert.notEqual(soft.hash,hard.hash,'Synthetic depth must soften ground intersections');
    assert.equal(soft.stats.instanceUploads,0,'Ground/soft toggles must not upload unchanged instances');
    // Only the lower edge approaches the plane: fade is evaluated per pixel, not a layer opacity change.
    assert.ok(Math.abs(soft.below[0]-32)<Math.abs(hard.below[0]-32),JSON.stringify({soft,hard}));
    assert.deepEqual(soft.upper,hard.upper,'Pixels far above ground must retain full opacity');
    const noSoft=await load({shader:'ALPHA',softParticles:false});
    const noSoftGround=await page.evaluate(()=>window.fxRound2.draw(0,true,true));
    assert.equal(noSoftGround.hash,hard.hash,'Asset EnableSoftParticles=false must bypass soft fade');
    await load({shader:'ADDITIVE'});
    await page.evaluate(()=>window.fxRound2.draw(0,true));
    const noUpload=await page.evaluate(()=>window.fxRound2.draw(1,true));assert.equal(noUpload.stats.instanceUploads,0,'GPU animation must retain immutable birth buffers');
    for(let i=0;i<2;i++){const result=await page.evaluate(()=>window.fxRound2.contextCycle());assert.equal(result.error,0);assert.equal(result.messages.filter(m=>m.startsWith('GPU 上下文已重建')).length,i+1);assert.ok(result.stats.particles>0);}
    await page.evaluate(()=>window.fxRound2.close());
    const counts=await page.evaluate(()=>window.fxRound2.counts);assert.deepEqual(counts,{buffers:0,textures:0,programs:0,vaos:0,framebuffers:0});
    await load({});assert.match(await page.evaluate(()=>window.fxRound2.budget()),/32 MiB/);await page.evaluate(()=>window.fxRound2.close());
    const workload=await page.evaluate(frames=>window.fxRound2.workload(frames),process.env.NEWSDK_FX_STRESS==='full'?60:5);
    assert.equal(workload.error,0);assert.equal(workload.particles,10000);assert.equal(workload.draws,1);assert.equal(workload.canvasPixels,1280*720);
    await page.evaluate(()=>window.fxRound2.close());
    assert.deepEqual(await page.evaluate(()=>window.fxRound2.counts),{buffers:0,textures:0,programs:0,vaos:0,framebuffers:0});
    await load({});
    const fixedOverlay=await page.evaluate(()=>window.fxRound2.motionOverlay(0,false));
    const motionStart=await page.evaluate(()=>window.fxRound2.motionOverlay(0));
    const motionEnd=await page.evaluate(()=>window.fxRound2.motionOverlay(30));
    assert.equal(motionEnd.error,0);
    assert.ok(motionEnd.endGreen>motionStart.endGreen+20,JSON.stringify({motionStart,motionEnd}));
    assert.ok(motionStart.originGreen>motionEnd.originGreen+20,JSON.stringify({motionStart,motionEnd}));
    await page.evaluate(()=>window.fxRound2.motionOverlay(15));
    assert.equal((await page.evaluate(()=>window.fxRound2.motionOverlay(30))).hash,motionEnd.hash,'Source marker replay must be deterministic');
    assert.equal((await page.evaluate(()=>window.fxRound2.motionOverlay(30,false))).hash,fixedOverlay.hash,'Disabling motion must remove path and restore source marker');
    await page.evaluate(()=>window.fxRound2.close());
    assert.deepEqual(await page.evaluate(()=>window.fxRound2.counts),{buffers:0,textures:0,programs:0,vaos:0,framebuffers:0});
    await fs.writeFile(path.join(output,'fx-render-round2.json'),JSON.stringify({passed:true,checks:['GPU/CPU material pixels','geometry/UV','soft projected ground depth','immutable uploads','two context recoveries','resource budgets','source motion path/marker/replay/reset'],workload,objectsAfterClose:{buffers:0,textures:0,programs:0,vaos:0,framebuffers:0}},null,2));
    assert.deepEqual(errors,[]);console.log('FX round 2 renderer: topology/UV, six material pixels, perpendicular rotations, projected soft depth, immutable GPU uploads and two context recoveries passed');
  }catch(error){await page?.screenshot({path:path.join(output,'fx-render-round2-failure.png')}).catch(()=>{});await fs.writeFile(path.join(output,'fx-render-round2.json'),JSON.stringify({passed:false,error:String(error.stack||error)},null,2));throw error;}finally{await browser?.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});
