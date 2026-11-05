const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');
const esbuild = require('esbuild');
const { chromium } = require('@playwright/test');
const { AssetIndex } = require('../out/src/fx/assetIndex');
const { AssetResolver } = require('../out/src/fx/assetResolver');
const { ResourceService } = require('../out/src/fx/resourceService');
const fixtureRoot = path.resolve(__dirname, 'fixtures/fx');
const output = path.resolve(__dirname, '../test-results/fx-special.json');
const scratch = path.resolve(__dirname, '../../work');
const zeroHandles = { buffers: 0, textures: 0, programs: 0, vaos: 0, framebuffers: 0 };
async function fixture(id) {
    const index = new AssetIndex({ roots: [fixtureRoot], modRoot: fixtureRoot });
    const resources = new ResourceService({ roots: [fixtureRoot] });
    try {
        await index.load(path.join(fixtureRoot, 'Data/special.xml'));
        const asset = index.listAssets().find(asset => asset.id === id); assert.ok(asset, id);
        const ir = await new AssetResolver(index).resolve(asset.key, { source: [0, 0, 3], seed: 41 });
        assert.ok(ir.systems.some(system => system.config && system.triggered), `${id}: ${JSON.stringify(ir.issues)}`);
        assert.equal(ir.issues.filter(issue => issue.severity === 'error').length, 0, JSON.stringify(ir.issues));
        for (const texture of ir.textures) if (texture.file) {
            const resource = await resources.load(texture.file); assert.equal(resource.status, 'ready', JSON.stringify(resource));
            Object.assign(texture, { status: 'ready', width: resource.texture.width, height: resource.texture.height, pixelsBase64: Buffer.from(resource.texture.pixels).toString('base64') });
            resources.release(resource);
        }
        return ir;
    } finally { index.dispose(); resources.dispose(); }
}
(async () => {
    await fs.mkdir(path.dirname(output), { recursive: true }); await fs.mkdir(scratch, { recursive: true });
    const bundle = await esbuild.build({ entryPoints: [path.join(__dirname, 'fxSpecialBrowserHarness.ts')], bundle: true, write: false, platform: 'browser', target: 'chrome120' });
    const [appScript, appCss] = await Promise.all([fs.readFile(path.join(__dirname, '../media/fx-editor.js')), fs.readFile(path.join(__dirname, '../media/fx.css'))]);
    const server = http.createServer((req, res) => {
        if (req.url === '/app.js') { res.setHeader('Content-Type', 'text/javascript'); res.end(appScript); return; }
        if (req.url === '/app.css') { res.setHeader('Content-Type', 'text/css'); res.end(appCss); return; }
        if (req.url === '/app') {
            res.setHeader('Content-Type', 'text/html');
            res.end('<!doctype html><meta charset="utf-8"><link rel="stylesheet" href="/app.css"><body><script>window.__FX_TEST__=true;window.__FX_BOOTSTRAP__={sessionId:"special-ui",documentVersion:1,requestId:0};window.fxMessages=[];window.acquireVsCodeApi=()=>({postMessage:m=>window.fxMessages.push(m),getState:()=>undefined,setState:s=>{window.fxState=s;}});</script><script src="/app.js"></script>');
            return;
        }
        res.setHeader('Content-Type', req.url === '/test.js' ? 'text/javascript' : 'text/html');
        res.end(req.url === '/test.js' ? bundle.outputFiles[0].contents : '<!doctype html><style>body{margin:0}canvas{width:640px;height:360px}</style><canvas></canvas><script src="/test.js"></script>');
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser, page; const errors = [], cases = [];
    try {
        browser = await chromium.launch({ headless: true, args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--disable-dev-shm-usage'] });
        page = await browser.newPage(); page.on('pageerror', error => errors.push(String(error)));
        await page.goto(`http://127.0.0.1:${server.address().port}`); await page.waitForFunction(() => window.fxSpecial);
        const load = ir => page.evaluate(ir => window.fxSpecial.load(ir), ir);
        const seek = frame => page.evaluate(frame => window.fxSpecial.seek(frame), frame);
        for (const [id, frame] of [['SpecialTrail', 12], ['SpecialAttractedTrail', 12], ['SpecialLightning', 12], ['SpecialSwarm', 12]]) {
            const ir = await fixture(id); await load(ir);
            const first = await seek(frame); assert.equal(first.glError, 0, id); assert.equal(first.budgetExceeded, undefined, id);
            assert.ok(first.lit > 20, `${id}: real geometry must draw visible pixels ${JSON.stringify({ lit: first.lit, stats: first.stats })}`);
            assert.equal(first.picked, ir.systems[0].key, `${id}: clicking visible mesh must select its system`);
            const animated = await seek(frame + 3); assert.notEqual(animated.hash, first.hash, `${id}: animation must change pixels`);
            const replay = await seek(frame); assert.equal(replay.hash, first.hash, `${id}: replay pixels`); assert.equal(replay.state, first.state, `${id}: replay state`);
            const repeated = await page.evaluate(frame => window.fxSpecial.draw(frame), frame);
            assert.equal(repeated.hash, first.hash, `${id}: rendering must not consume RNG or mutate state`); assert.equal(repeated.state, first.state);
            const sameFrame = await page.evaluate(() => window.fxSpecial.restoreAtSameFrame());
            assert.notEqual(sameFrame.changed.hash, first.hash, `${id}: restoring changed state at the same frame must invalidate cached geometry`);
            assert.equal(sameFrame.restored.hash, first.hash, `${id}: restoring the original state must restore pixels`);
            if (id === 'SpecialLightning') {
                const limited = await page.evaluate(() => window.fxSpecial.pickBudget());
                assert.equal(limited.picked, undefined); assert.ok(limited.messages.some(message => message.includes('选择已暂停')), 'picking excessive geometry must report a bounded failure');
            }
            const turned = await page.evaluate(() => window.fxSpecial.view(.3)); assert.equal(turned.glError, 0); assert.notEqual(turned.hash, first.hash, `${id}: camera-facing geometry must update`);
            assert.equal((await page.evaluate(() => window.fxSpecial.hide(true))).lit, 0, `${id}: hiding removes all effect pixels`);
            assert.ok((await page.evaluate(() => window.fxSpecial.hide(false))).lit > 20);
            await load(ir); const original = await seek(frame), changed = structuredClone(ir), config = changed.systems[0].config;
            if (config.trail) config.trail.life = 3;
            else if (config.lightning) config.lightning.end[0] += 15;
            else if (config.swarm) config.swarm.speedStretchAmount = 12;
            await load(changed); const edited = await seek(frame); assert.notEqual(edited.hash, original.hash, `${id}: parameter edit must affect image`);
            for (let cycle = 0; cycle < 2; cycle++) {
                const restored = await page.evaluate(() => window.fxSpecial.contextCycle());
                assert.equal(restored.glError, 0); assert.equal(restored.hash, edited.hash, `${id}: context restore geometry/textures`);
            }
            await page.evaluate(() => window.fxSpecial.close()); assert.deepEqual(await page.evaluate(() => window.fxSpecial.counts), zeroHandles, `${id}: GPU cleanup`);
            cases.push({ id, frame, litPixels: first.lit, draws: first.stats.draws, stateBytes: first.stateBytes, snapshotBytes: first.snapshotBytes });
        }
        const trail = await fixture('SpecialTrail'); trail.systems[0].config.systemLifetime = 1;
        trail.systems[0].config.lifetime = { low: 4, high: 4, type: 'CONSTANT' }; trail.systems[0].config.trail.life = 6;
        await load(trail); const dead = await seek(5); assert.ok(dead.lit > 0, 'dead Trail must render its draining history');
        await seek(1); assert.equal((await seek(5)).hash, dead.hash, 'dead Trail history restores on seek');
        assert.equal((await seek(12)).lit, 0, 'dead Trail eventually drains');
        await page.evaluate(() => window.fxSpecial.close());
        const noEnv = await fixture('SpecialSwarm');
        noEnv.systems.forEach(system => { system.environmentTextureKey = 'missing-env'; });
        await load(noEnv); const fallback = await seek(12); assert.equal(fallback.glError, 0); assert.ok(fallback.lit > 20, 'optional environment texture missing must retain base Swarm');
        await page.evaluate(() => window.fxSpecial.close()); assert.deepEqual(await page.evaluate(() => window.fxSpecial.counts), zeroHandles);
        await page.setViewportSize({ width: 1440, height: 1000 });
        await page.goto(`http://127.0.0.1:${server.address().port}/app`); await page.waitForFunction(() => window.__fxTest);
        await page.evaluate(() => window.__fxTest.pause());
        let request = 1;
        for (const [id, module, attribute, value, configKey, configAttribute] of [
            ['SpecialTrail', '/Draw[1]/Trail[1]', 'TrailLife', '6', 'trail', 'life'],
            ['SpecialAttractedTrail', '/Physics[1]/Swarm[1]', 'AttractStrength', '0.2', 'swarmPhysics', 'attractStrength'],
            ['SpecialLightning', '/Draw[1]/Lightning[1]/EndPoint[1]', 'x', '40', 'lightning', 'end'],
            ['SpecialSwarm', '/Draw[1]/Swarm[1]', 'SpeedStretchAmount', '4', 'swarm', 'speedStretchAmount'],
        ]) {
            const ir = await fixture(id), field = ir.systems[0].fields.find(field => field.nodePath.includes(module) && field.attribute === attribute);
            assert.ok(field, `${id}: special field`);
            await page.evaluate(message => window.__fxTest.receive(message), { type: 'preview', sessionId: 'special-ui', documentVersion: 1, requestId: request++, ir, assets: [], editingUri: ir.documentUri });
            await page.evaluate(async () => { window.__fxTest.pause(); await window.__fxTest.seek(12); document.querySelectorAll('details').forEach(details => { details.open = true; }); window.fxMessages.length = 0; });
            const input = page.locator(`.field-row[data-field-key=${JSON.stringify(field.key)}] input`).first();
            await input.fill(value);
            await page.waitForFunction(({ configKey, configAttribute, value }) => {
                const current = window.__fxTest.world()?.systems[0]?.runtime.config[configKey]?.[configAttribute];
                return (Array.isArray(current) ? current[0] : current) === Number(value);
            }, { configKey, configAttribute, value });
            assert.equal(await page.evaluate(() => window.fxMessages.filter(message => message.type === 'edit').length), 0, `${id}: draft cannot change source before commit`);
            await input.press('Control+s');
            const sent = await page.evaluate(() => window.fxMessages.filter(message => ['edit', 'save'].includes(message.type)));
            assert.deepEqual(sent.map(message => message.type), ['edit', 'save'], `${id}: Ctrl+S commits draft before save`);
            assert.equal(sent[0].edits.length, 1); assert.equal(sent[0].edits[0].key, field.key); assert.equal(sent[0].edits[0].value, value);
            assert.equal(sent[0].documentVersion, sent[1].documentVersion);
        }
        for (const preset of ['拖尾', '闪电', '群粒子']) {
            await page.getByLabel('新建特效', { exact: true }).selectOption(preset);
            assert.equal(await page.evaluate(() => window.fxMessages.filter(message => message.type === 'new').at(-1).preset), preset);
        }
        const environment = await fixture('SpecialSwarm'), originalEnvironmentKey = environment.systems[0].environmentTextureKey;
        assert.ok(originalEnvironmentKey, 'resolved Swarm must expose the environment texture binding');
        const alternateEnvironment = structuredClone(environment.textures[0]); alternateEnvironment.key += ':alternate'; alternateEnvironment.name = 'AlternateEnvironment';
        environment.textures.push(alternateEnvironment);
        await page.evaluate(message => window.__fxTest.receive(message), { type: 'preview', sessionId: 'special-ui', documentVersion: 1, requestId: request++, ir: environment, assets: [], editingUri: environment.documentUri });
        await page.evaluate(async () => { window.__fxTest.pause(); await window.__fxTest.seek(12); document.querySelectorAll('details').forEach(details => { details.open = true; }); window.fxMessages.length = 0; });
        const environmentField = environment.systems[0].fields.find(field => field.attribute === 'EnvironmentTexture'); assert.ok(environmentField);
        const environmentInput = page.locator(`.field-row[data-field-key=${JSON.stringify(environmentField.key)}] input`).first();
        for (const [name, key] of [['', undefined], ['MissingEnvironment', undefined], ['AlternateEnvironment', alternateEnvironment.key]]) {
            await environmentInput.fill(name);
            await page.waitForFunction(({ name, key }) => window.__fxTest.world()?.systems[0]?.runtime.config.swarm.environmentTexture === name && window.__fxTest.renderer()?.layers[0]?.environmentTextureKey === key, { name, key });
            assert.equal(await page.evaluate(() => window.fxMessages.filter(message => message.type === 'edit').length), 0, 'environment draft must not commit the source');
            await environmentInput.press('Escape');
            await page.waitForFunction(originalKey => window.__fxTest.renderer()?.layers[0]?.environmentTextureKey === originalKey && window.__fxTest.world()?.systems[0]?.runtime.config.swarm.environmentTexture === 'FxFixtureTexture', originalEnvironmentKey);
        }
        await page.evaluate(() => window.__fxTest.dispose());
        assert.equal(await page.evaluate(() => window.__fxTest.stats().gpu), null);
        assert.deepEqual(errors, []);
        await fs.writeFile(output, JSON.stringify({ passed: true, cases, checks: ['modular SDK-inspired fixtures', 'real visible animated WebGL geometry', 'deterministic pixel/state seek', 'same-frame restored geometry invalidation', 'mesh picking and bounded pick failures', 'parameter edit image changes', 'camera and visibility', 'two context recoveries per fixture', 'dead Trail drain and replay', 'optional environment fallback', 'GPU release', 'webview special field drafts and atomic edit before save', 'environment clear, missing and cached replacement drafts with cancel', 'three new preset intents'], objectsAfterClose: zeroHandles }, null, 2));
        console.log('FX special: Trail, Lightning, Swarm geometry, replay, edits, context recovery and resource cleanup passed');
    } catch (error) {
        await page?.screenshot({ path: path.join(scratch, 'fx-special-failure.png') }).catch(() => {});
        await fs.writeFile(output, JSON.stringify({ passed: false, error: String(error.stack || error) }, null, 2)); throw error;
    } finally { await browser?.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; });
