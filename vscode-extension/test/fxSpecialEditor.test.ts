import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { AssetIndex } from '../src/fx/assetIndex';
import { AssetResolver } from '../src/fx/assetResolver';
import { applyTextPatches, prepareFieldTransaction } from '../src/fx/editTransaction';
import { createFxTemplate } from '../src/fx/templates';
import type { FxField } from '../src/fx/previewProtocol';
import { prepareTextureState, streamPreviewTextures } from '../src/fx/texturePipeline';
import type { ResourceReady } from '../src/fx/resourceService';

const wrap = (xml: string): string => `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${xml}</AssetDeclaration>`;
async function setup(t: {after: (callback: () => Promise<unknown>) => void}, xml: string): Promise<{index: AssetIndex; resolver: AssetResolver; file: string}> {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'newsdk-special-editor-')));
    t.after(() => fs.rm(root,{recursive:true,force:true}));
    const file = path.join(root,'entry.xml');
    await fs.writeFile(file,wrap(`<Texture id="Particle" File="particle.tga"/><Texture id="Environment" File="environment.dds"/>${xml}`));
    const index = new AssetIndex({roots:[root]}); t.after(async () => index.dispose());
    await index.load(file); return {index,resolver:new AssetResolver(index),file};
}
function edit(index: AssetIndex, field: FxField, value: string): string {
    const document = index.getDocument(field.uri)!;
    return applyTextPatches(document.text,prepareFieldTransaction(document,[{nodePath:field.nodePath,attribute:field.attribute,value,
        expectedValue:field.defaulted ? undefined : field.value,documentVersion:document.version}]));
}

test('Trail XSD defaults become writable attributes and survive save/reload without changing the particle template', async t => {
    const xml = createFxTemplate('拖尾','Trail','Particle').replace(/ UTile="1" VTile="1" UScrollRate="0.01" VScrollRate="0"/,'');
    const {index,resolver,file} = await setup(t,xml);
    const asset = index.listAssets().find(a=>a.id==='Trail')!;
    const ir = await resolver.resolve(asset.key);
    assert.ok(ir.systems[0].config);
    const field = ir.fields.find(f=>f.attribute==='UScrollRate')!;
    assert.equal(field.defaulted,true); assert.equal(field.readonly,false); assert.equal(field.value,'0.0');
    const updated = edit(index,field,'0.25');
    assert.match(updated,/<Trail TrailLife="24" UScrollRate="0.25"\/>/);
    assert.match(updated,/ParticleTexture="Particle"/);
    await fs.writeFile(file,updated); index.invalidate(file); await index.load(file);
    const loaded = await resolver.resolve(index.listAssets().find(a=>a.id==='Trail')!.key);
    assert.equal(loaded.systems[0].config?.trail?.uScrollRate,0.25);
    assert.equal(loaded.fields.find(f=>f.attribute==='UScrollRate')?.defaulted,false);
});

test('Swarm resolves optional environment texture separately with shared resource identities and editable defaults', async t => {
    const xml = createFxTemplate('群粒子','Swarm','Particle').replace('<Swarm OpaqueSpeed','<Swarm EnvironmentTexture="Environment" OpaqueSpeed');
    const {index,resolver} = await setup(t,xml);
    const ir = await resolver.resolve(index.listAssets().find(a=>a.id==='Swarm')!.key);
    const system = ir.systems[0]; assert.ok(system.config);
    assert.notEqual(system.environmentTextureKey,system.textureKey);
    assert.equal(ir.textures.find(texture=>texture.key===system.environmentTextureKey)?.name,'Environment');
    assert.equal(ir.textures.find(texture=>texture.key===system.textureKey)?.name,'Particle');
    assert.equal(ir.textures.length,2);
    const environmentAsset = index.listAssets().find(asset=>asset.id==='Environment')!;
    assert.ok(index.dependentsOf(environmentAsset.key).has(index.listAssets().find(asset=>asset.id==='Swarm')!.key));
    const environment = ir.fields.find(field=>field.attribute==='EnvironmentTexture')!;
    assert.equal(environment.readonly,false); assert.equal(environment.kind,'text');
    assert.match(edit(index,environment,'Particle'),/EnvironmentTexture="Particle"/);
    const physics = ir.fields.filter(field=>field.nodePath.includes('/Physics['));
    assert.deepEqual(physics.map(field=>field.attribute),['AttractStrength']);
});

test('environment GPU keys remap for roots and dynamic systems on first transfer and retained metadata reload', async t => {
    const xml = createFxTemplate('群粒子','Swarm','Particle').replace('<Swarm OpaqueSpeed','<Swarm EnvironmentTexture="Environment" OpaqueSpeed');
    const {index,resolver} = await setup(t,xml);
    const ir = await resolver.resolve(index.listAssets().find(a=>a.id==='Swarm')!.key);
    ir.dynamic = {templates:[structuredClone(ir.systems[0])],fxLists:[{key:'event',systems:[structuredClone(ir.systems[0])],triggered:true,cullTrackingFrames:0,cullTrackingMin:0,cullTrackingMax:0}]};
    const uploaded = new Map<string,{key:string;width:number;height:number}>();
    let transfers = 0, releases = 0;
    await streamPreviewTextures(ir,{load:async file=>({status:'ready',key:file,path:file,generation:1,source:'source-texture',
        texture:{width:1,height:1,pixels:new Uint8Array([128,128,128,255]),mips:[],format:'TGA',byteLength:4,source:'source-texture'}} as ResourceReady),release:()=>{releases++;}},
        {uploaded,maxBytes:1024,current:()=>true,publish:async()=>{},transfer:async()=>{transfers++;return true;}});
    assert.equal(transfers,2); assert.equal(releases,2);
    const key = ir.textures.find(texture=>texture.name==='Environment')!.key;
    assert.match(key,/^gpu:/);
    assert.equal(ir.systems[0].environmentTextureKey,key);
    assert.equal(ir.dynamic.templates[0].environmentTextureKey,key);
    assert.equal(ir.dynamic.fxLists[0].systems[0].environmentTextureKey,key);
    const reloaded = await resolver.resolve(index.listAssets().find(a=>a.id==='Swarm')!.key);
    prepareTextureState(reloaded,uploaded);
    assert.equal(reloaded.systems[0].environmentTextureKey,key);
    assert.equal(reloaded.textures.find(texture=>texture.name==='Environment')!.status,'ready');
});

test('an unresolved optional Swarm environment warns at its field while retaining playable base particles', async t => {
    const xml = createFxTemplate('群粒子','Swarm','Particle').replace('<Swarm OpaqueSpeed','<Swarm EnvironmentTexture="MissingEnvironment" OpaqueSpeed');
    const {index,resolver} = await setup(t,xml);
    const ir = await resolver.resolve(index.listAssets().find(a=>a.id==='Swarm')!.key);
    assert.ok(ir.systems[0].config);
    assert.ok(ir.systems[0].textureKey);
    const issue = ir.systems[0].issues.find(issue=>issue.message.includes('MissingEnvironment'))!;
    assert.equal(issue.severity,'warning'); assert.match(issue.nodePath,/Swarm/);
    assert.match(issue.message,/基础粒子贴图/);
    assert.equal(ir.textures.find(texture=>texture.key===ir.systems[0].environmentTextureKey)?.status,'missing-declaration');
});

test('Lightning wave fields retain unique physical paths and modifying one random set leaves the other unchanged', async t => {
    const {index,resolver,file} = await setup(t,createFxTemplate('闪电','Lightning','Particle'));
    const ir = await resolver.resolve(index.listAssets().find(a=>a.id==='Lightning')!.key);
    assert.ok(ir.systems[0].config);
    const amplitudes = ir.fields.filter(field=>field.nodePath.includes('/StartAmplitude[') && field.attribute==='Low');
    assert.equal(amplitudes.length,2); assert.notEqual(amplitudes[0].key,amplitudes[1].key);
    assert.deepEqual(amplitudes.map(field=>field.group),['闪电波形 1','闪电波形 2']);
    assert.equal(amplitudes[0].readonly,false);
    const second = ir.systems[0].config!.lightning!.randomSets[1].startAmplitude.low;
    await fs.writeFile(file,edit(index,amplitudes[0],'9'));
    index.invalidate(file); await index.load(file);
    const result = await resolver.resolve(index.listAssets().find(a=>a.id==='Lightning')!.key);
    assert.equal(result.systems[0].config?.lightning?.randomSets[0].startAmplitude.low,9);
    assert.equal(result.systems[0].config?.lightning?.randomSets[1].startAmplitude.low,second);
    assert.ok(result.fields.find(field=>field.attribute==='Type')?.choices?.includes('STREAK'));
    assert.ok(result.fields.find(field=>field.attribute==='Shader')?.choices?.includes('ADDITIVE_ALPHA_TEST'));
    assert.ok(result.fields.find(field=>field.attribute==='Shader')?.choices?.includes('ALPHA_TEST'));
    assert.ok(!result.fields.find(field=>field.attribute==='Shader')?.choices?.includes('ALPHA_ALPHA_TEST'));
});
