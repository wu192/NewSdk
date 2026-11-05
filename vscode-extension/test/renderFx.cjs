const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const {chromium} = require('@playwright/test');
const esbuild = require('esbuild');
const {AssetIndex} = require('../out/src/fx/assetIndex');
const {AssetResolver} = require('../out/src/fx/assetResolver');
const {ResourceService} = require('../out/src/fx/resourceService');
const fixtureRoot = path.resolve(__dirname, 'fixtures/fx');
const output = path.resolve(__dirname, '../test-results');
async function fixture(file, id) {
  const index = new AssetIndex({roots: [fixtureRoot], modRoot: fixtureRoot});
  const resources = new ResourceService({roots: [fixtureRoot]});
  try {
    await index.load(path.join(fixtureRoot, 'Data', file));
    const asset = index.listAssets().find(a => a.id === id);
    assert.ok(asset, `Missing fixture ${id}`);
    const ir = await new AssetResolver(index).resolve(asset.key);
    assert.ok(ir.systems.some(s => s.config && s.triggered), JSON.stringify(ir.issues));
    for (const texture of ir.textures) if (texture.file) {
      const resource = await resources.load(texture.file);
      assert.equal(resource.status, 'ready', JSON.stringify(resource));
      Object.assign(texture, {status: 'ready', width: resource.texture.width, height: resource.texture.height,
        pixelsBase64: Buffer.from(resource.texture.pixels).toString('base64')});
      resources.release(resource);
    }
    return ir;
  } finally {index.dispose(); resources.dispose();}
}
(async () => {
  await fs.mkdir(output, {recursive: true});
  const bundled = await esbuild.build({entryPoints: [path.join(__dirname, 'fxBrowserHarness.ts')], bundle: true, platform: 'browser', write: false, target: 'chrome120'});
  const harnessHtml = '<!doctype html><meta charset="utf-8"><style>body{margin:0}canvas{width:960px;height:540px}</style><canvas></canvas><script src="/harness.js"></script>';
  const server = http.createServer(async (req, res) => {
    try {
      if (req.url === '/harness.js') {res.setHeader('Content-Type', 'text/javascript'); res.end(bundled.outputFiles[0].contents);}
      else if (req.url === '/fx-editor.js') {res.setHeader('Content-Type', 'text/javascript');res.end(await fs.readFile(path.join(__dirname, '../media/fx-editor.js')));}
      else if (req.url === '/fx.css') {res.setHeader('Content-Type', 'text/css');res.end(await fs.readFile(path.join(__dirname, '../media/fx.css')));}
      else if (req.url === '/app') {res.setHeader('Content-Type', 'text/html');res.end('<!doctype html><meta charset="utf-8"><link rel="stylesheet" href="/fx.css"><body><div id="app"></div><script>window.__FX_TEST__=true;window.__FX_BOOTSTRAP__={sessionId:"browser-test",documentVersion:1,requestId:0};window.fxMessages=[];window.acquireVsCodeApi=()=>({postMessage:m=>window.fxMessages.push(m),getState:()=>undefined,setState:s=>{window.fxSavedState=s}});</script><script src="/fx-editor.js"></script>');}
      else {res.setHeader('Content-Type', 'text/html');res.end(harnessHtml);}
    } catch (error) {res.statusCode=500;res.end(String(error));}
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser, page;
  const cases = [], errors = [];
  try {
    browser = await chromium.launch({headless: true, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--disable-dev-shm-usage']});
    page = await browser.newPage({viewport: {width: 1280, height: 800}, deviceScaleFactor: 1});
    page.on('pageerror', error => errors.push(String(error)));
    const origin = `http://127.0.0.1:${server.address().port}`;
    await page.goto(origin);
    await page.waitForFunction(() => window.fxHarness);
    let aura;
    for (const [file, id, frame] of [['flash.xml','Flash',3],['smoke.xml','Smoke',20],['sparks.xml','Sparks',6],['aura.xml','Aura',60],['Mod.xml','MultiLayerAura',30]]) {
      const ir = await fixture(file,id);
      if (id === 'MultiLayerAura') aura = ir;
      await page.evaluate(ir => window.fxHarness.load(ir), ir);
      const first = await page.evaluate(frame => window.fxHarness.seek(frame), frame);
      assert.equal(first.glError, 0, JSON.stringify(first));
      assert.ok(first.counts.textures > 0, `${id}: source texture was not uploaded`);
      assert.ok(first.stats.particles > 0 && first.lit > 80, `${id} did not draw: ${JSON.stringify(first)}`);
      await page.evaluate(frame => window.fxHarness.seek(frame + 5), frame);
      const repeated = await page.evaluate(frame => window.fxHarness.seek(frame), frame);
      assert.equal(repeated.hash, first.hash, `${id}: seek did not restore identical pixels`);
      cases.push({id, frame, ...first});
    }
    for (let i=0;i<100;i++) {
      await page.evaluate(ir => {window.fxHarness.load(ir);window.fxHarness.seek(12);window.fxHarness.close();}, aura);
      const counts = await page.evaluate(() => ({...window.fxHarness.counts}));
      assert.deepEqual(counts, {buffers:0,textures:0,programs:0,vaos:0}, `GPU handles after switch ${i}`);
    }
    await page.goto(`${origin}/app`);
    await page.waitForFunction(() => window.__fxTest);
    const uiIr = await fixture('flash.xml','Flash');
    const envelope = {sessionId:'browser-test', documentVersion:1, requestId:1};
    await page.evaluate(({ir,envelope}) => window.__fxTest.receive({...envelope,type:'preview',ir,assets:[{key:ir.assetKey,name:ir.name,type:'FXParticleSystemTemplate'}],editingUri:ir.documentUri}), {ir:uiIr,envelope});
    await page.evaluate(async () => {window.__fxTest.pause();await window.__fxTest.seek(3);});
    await page.waitForFunction(() => window.__fxTest.stats().systems > 0);
    await page.waitForTimeout(80);
    const paused = await page.evaluate(() => window.__fxTest.stats());
    assert.equal(paused.playing,false); assert.equal(paused.raf,false,'Paused unchanged preview must stop RAF');
    const field = uiIr.systems[0].fields.find(f=>f.attribute==='Low' && /Lifetime/.test(f.nodePath));
    assert.ok(field);
    const selector = `.field-row[data-field-key=${JSON.stringify(field.key)}] input`;
    await page.locator(selector).fill('27');
    await page.waitForTimeout(100);
    assert.equal(await page.evaluate(()=>window.fxMessages.filter(m=>m.type==='edit').length),0,'Draft must not create document edits');
    await page.locator(selector).press('Enter');
    assert.equal(await page.evaluate(()=>window.fxMessages.filter(m=>m.type==='edit').length),1,'A gesture must submit once');
    await page.locator('#step-button').click();
    assert.ok((await page.locator('#time-label').textContent()).includes('帧'));
    await page.locator('#save-build').click();
    assert.equal(await page.evaluate(()=>window.fxMessages.filter(m=>m.type==='saveAndBuild').length),1);
    await page.evaluate(()=>window.__fxTest.receive({sessionId:'browser-test',documentVersion:1,requestId:2,type:'active',active:false}));
    await page.waitForTimeout(80);
    const inactive=await page.evaluate(()=>window.__fxTest.stats());
    assert.equal(inactive.raf,false,'Inactive page must stop RAF');
    await page.evaluate(()=>window.__fxTest.dispose());
    assert.equal((await page.evaluate(()=>window.__fxTest.stats())).disposed,true);
    cases.push({id:'Chinese editor UI',checks:['paused RAF','draft without document write','one gesture one commit','single step','save/build intent','inactive/dispose']});
    assert.deepEqual(errors, []);
    await fs.writeFile(path.join(output, 'fx-render.json'), JSON.stringify({passed: true, renderer:'Chromium SwiftShader; regression only, no hardware FPS claim', cases, switches:100, gpuHandlesAfterClose:{buffers:0,textures:0,programs:0,vaos:0}}, null, 2));
    console.log(`FX renderer: ${cases.length} fixed fixtures, deterministic seek, 100 dispose cycles passed`);
  } catch (error) {
    await page?.screenshot({path: path.join(output,'fx-render-failure.png'), fullPage: true}).catch(()=>{});
    await fs.writeFile(path.join(output,'fx-render.json'), JSON.stringify({passed:false,error:String(error.stack||error),errors,cases}, null, 2));
    throw error;
  } finally {await browser?.close(); await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});
