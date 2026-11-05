import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { AssetIndex } from '../src/fx/assetIndex';
import { AssetResolver } from '../src/fx/assetResolver';
import { fxCapabilities } from '../src/fx/capabilities';
import { applyTextPatches, prepareFieldTransaction } from '../src/fx/editTransaction';
const ns = 'uri:ea.com:eala:asset';
const wrap = (contents: string): string => `<AssetDeclaration xmlns="${ns}" xmlns:xai="uri:ea.com:eala:asset:instance">${contents}</AssetDeclaration>`;
const fixtureRoot = path.resolve(__dirname, '../../test/fixtures/fx');
async function temporary(t: {after: (callback: () => Promise<unknown>) => void}): Promise<string> {
    const created = await fs.mkdtemp(path.join(os.tmpdir(), 'newsdk-fx-assets-'));
    // Windows os.tmpdir() may use an 8.3 alias; source identities are physical paths.
    const root = await fs.realpath(created); t.after(() => fs.rm(root,{recursive:true,force:true})); return root;
}
async function templateXml(): Promise<string> {
    const fixture = await fs.readFile(path.join(fixtureRoot,'Data','flash.xml'),'utf8');
    return fixture.slice(fixture.indexOf('<FXParticleSystemTemplate'), fixture.indexOf('</FXParticleSystemTemplate>')+'</FXParticleSystemTemplate>'.length);
}

test('entry scopes preserve namespace/type/source identity and do not leak same-id assets', async t => {
    const root = await temporary(t);
    await fs.writeFile(path.join(root,'a.xml'),wrap('<Texture id="same" File="a.tga"/>'));
    await fs.writeFile(path.join(root,'b.xml'),wrap('<Texture id="same" File="b.tga"/>'));
    const index = new AssetIndex({roots:[root]});
    await index.load(path.join(root,'a.xml')); const first = index.listAssets()[0];
    await index.load(path.join(root,'b.xml')); const second = index.listAssets()[0];
    assert.notEqual(first.key,second.key); assert.equal(index.listAssets().length,1);
    assert.equal(index.getAsset(first.key),undefined); assert.equal(second.node.attributes.File,'b.tga');
    index.dispose(); assert.equal(index.listAssets().length,0);
});

test('include kinds retain their contract and invalidate changed dependency despite unchanged timestamp/size', async t => {
    const root = await temporary(t);
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Includes><Include type="instance" source="particle.xml"/><Include type="reference" source="texture.xml"/></Includes>'));
    await fs.writeFile(path.join(root,'particle.xml'),wrap('<Includes><Include type="reference" source="texture.xml"/></Includes><FXParticleSystemTemplate id="P" Type="GPU_PARTICLE" ParticleTexture="T"/>'));
    await fs.writeFile(path.join(root,'texture.xml'),wrap('<Texture id="T" File="a.tga"/>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'entry.xml'));
    const template = index.listAssets().find(a=>a.id==='P')!; const texture = index.listAssets().find(a=>a.id==='T')!;
    assert.equal(template.includeType,'instance'); assert.equal(texture.includeType,'reference');
    assert.ok(index.dependentsOf(texture.key).has(template.key));
    const generation = index.generation; assert.ok(index.invalidate(texture.uri).has(index.graph.entry));
    await index.load(path.join(root,'entry.xml')); assert.ok(index.generation > generation);
});

test('repeated imports deduplicate canonical assets and upgrade reference subtrees to concrete instance imports', async t => {
    const root = await temporary(t);
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Includes><Include type="reference" source="bundle.xml"/><Include type="all" source="bundle.xml"/></Includes>'));
    await fs.writeFile(path.join(root,'bundle.xml'),wrap('<Includes><Include type="instance" source="texture.xml"/></Includes>'));
    await fs.writeFile(path.join(root,'texture.xml'),wrap('<Texture id="T" File="a.tga"/>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'entry.xml'));
    assert.equal(index.listAssets().length,1); assert.equal(index.listAssets()[0].includeType,'instance');
});

test('include cycles identify shortest active path, path escape and alias loops are bounded', async t => {
    const root = await temporary(t);
    await fs.writeFile(path.join(root,'a.xml'),wrap('<Includes><Include type="all" source="b.xml"/></Includes>'));
    await fs.writeFile(path.join(root,'b.xml'),wrap('<Includes><Include type="all" source="a.xml"/></Includes>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'a.xml'));
    assert.equal(index.graph.documents.size,2); assert.match(index.graph.issues.find(i=>i.code==='include-cycle')!.message,/a\.xml.*b\.xml.*a\.xml/);
    await assert.rejects(index.resolveFile(path.join(root,'a.xml'),'../outside.xml'),/资源根之外/);
    await assert.rejects(index.resolveFile(path.join(root,'a.xml'),'DATA:http://example.com/file.xml'),/第二个冒号/);
    await assert.rejects(index.resolveFile(path.join(root,'a.xml'),'source.xml:extra'),/非法别名/);
    await fs.symlink(root,path.join(root,'alias'), process.platform==='win32'?'junction':'dir');
    await fs.writeFile(path.join(root,'a.xml'),wrap('<Includes><Include type="all" source="alias/a.xml"/></Includes>'));
    await index.load(path.join(root,'a.xml')); assert.ok(index.graph.issues.some(i=>i.code==='include-cycle'));
});

test('source budget rejects large files before custom snapshot reader is called', async t => {
    const root = await temporary(t); const file = path.join(root,'huge.xml');
    await fs.writeFile(file,'x'.repeat(200)); let reads = 0;
    const index = new AssetIndex({roots:[root],maxSourceBytes:64,readText:async()=>{reads++;return {text:'',version:1};}});
    await index.load(file); assert.equal(reads,0); assert.ok(index.graph.issues.some(i=>i.code==='include-read' && i.message.includes('预算')));
});

test('cross-file FXList distinguishes system count, invocation units, land/water and StopIfPlayed', async t => {
    const root = await temporary(t); const template = await templateXml();
    await fs.writeFile(path.join(root,'particle.xml'),wrap(template));
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Includes><Include type="instance" source="particle.xml"/></Includes><FXList id="Calls"><NuggetList><ParticleSystem Particle="Flash" Count="2" SystemLife="500ms" OnlyIfOnLand="true" StopIfPlayed="true"><Offset x="4" y="5" z="6"/><InitialDelay Low="200" High="200"/></ParticleSystem><ParticleSystem Particle="Flash"/></NuggetList></FXList>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'entry.xml'));
    const ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Calls')!.key,{terrain:'water',source:[1,2,3]});
    assert.equal(ir.systems.length,2); assert.equal(ir.systems[0].triggered,false);
    assert.deepEqual(ir.systems[0].offset,[5,7,9]); assert.equal(ir.systems[0].systemLifeFrames,15);
    assert.equal(ir.systems[0].initialDelayOverrideMs?.low,200); assert.equal(ir.systems[0].config?.initialDelay.low,0); // Invocation override preserves template construction sampling.
    assert.equal(ir.systems[0].config?.burstCount.low,1); // Count does not modify BurstCount.
    assert.equal(ir.systems[0].source.uri,path.join(root,'entry.xml'));
});

test('invalid FXList numeric and boolean inputs never fall back to a fabricated active system', async t => {
    const root = await temporary(t); const template = await templateXml();
    const index = new AssetIndex({roots:[root]});
    for (const attributes of ['Count="NaN"','OnlyIfOnLand="unknown"']) {
        await fs.writeFile(path.join(root,'entry.xml'),wrap(template+`<FXList id="Invalid"><NuggetList><ParticleSystem Particle="Flash" ${attributes}/></NuggetList></FXList>`));
        await index.load(path.join(root,'entry.xml'));
        const ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Invalid')!.key);
        assert.equal(ir.systems.length,0); assert.ok(ir.issues.some(i=>i.severity==='error'));
    }
});

test('ordinary inheritance merges unique children, preserves parent provenance and resolves safe numeric defines', async t => {
    const root = await temporary(t); const parent = (await templateXml()).replace('id="Flash"','id="Parent"');
    await fs.writeFile(path.join(root,'child.xml'),wrap('<Defines><Define name="SIZE" value="12*2"/></Defines>'+parent+'<FXParticleSystemTemplate id="Child" inheritFrom="Parent"><Size Low="= $SIZE" High="= $SIZE"/></FXParticleSystemTemplate>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'child.xml'));
    const ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Child')!.key);
    assert.equal(ir.systems[0].config?.size.low,24);
    const lifetime = ir.fields.find(f=>f.group==='粒子寿命（逻辑帧）' && f.attribute==='Low')!;
    assert.equal(lifetime.uri,path.join(root,'child.xml')); assert.equal(lifetime.inherited,true);
    assert.equal(index.getAsset(lifetime.sourceAsset!)?.id,'Parent');
    const size = ir.fields.find(f=>f.group==='尺寸' && f.attribute==='Low')!;
    assert.equal(size.value,'= $SIZE'); assert.equal(size.inherited,false);
    assert.equal(ir.systems[0].definition?.children.find(n=>n.name==='Size')?.attributes.Low,'24');
});

test('duplicate concrete definitions, inheritance cycles and unresolved expressions never choose an arbitrary value', async t => {
    const root = await temporary(t);
    const template = await templateXml();
    await fs.writeFile(path.join(root,'entry.xml'),wrap(template+template+'<FXList id="Ambiguous"><NuggetList><ParticleSystem Particle="Flash"/></NuggetList></FXList><FXParticleSystemTemplate id="A" inheritFrom="B"/><FXParticleSystemTemplate id="B" inheritFrom="A"/>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'entry.xml'));
    const resolver = new AssetResolver(index);
    const ambiguous = await resolver.resolve(index.listAssets().find(a=>a.id==='Ambiguous')!.key);
    assert.equal(ambiguous.systems.length,0); assert.ok(ambiguous.issues.some(i=>i.code==='asset-ambiguous'));
    const cycle = await resolver.resolve(index.listAssets().find(a=>a.id==='A')!.key); assert.ok(cycle.issues.some(i=>i.code==='inherit-cycle'));
    await fs.writeFile(path.join(root,'entry.xml'),wrap(template.replace('Low="24"','Low="= unknown(5)"')));
    await index.load(path.join(root,'entry.xml'));
    const expression = await resolver.resolve(index.listAssets()[0].key);
    assert.ok(expression.issues.some(i=>i.code==='expression-unresolved')); assert.equal(expression.systems[0].config,undefined);
});

test('non-BaseInheritable FX assets allow pre-XSD same-document inheritance and reject cross-document sources', async t => {
    const root = await temporary(t); const parent = (await templateXml()).replace('id="Flash"','id="Parent"');
    await fs.writeFile(path.join(root,'parent.xml'),wrap(parent));
    await fs.writeFile(path.join(root,'child.xml'),wrap('<Includes><Include type="instance" source="parent.xml"/></Includes><FXParticleSystemTemplate id="Child" inheritFrom="Parent"><Size Low="7" High="7"/></FXParticleSystemTemplate>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'child.xml'));
    const ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Child')!.key);
    assert.equal(ir.systems.length,0); assert.ok(ir.issues.some(i=>i.code==='inherit-cross-document' && i.nodePath.includes('FXParticleSystemTemplate')));
});

test('compiler NodeJoiner appends unkeyed records, merges explicit Append singletons, replaces choices and rejects Merge', async t => {
    const root = await temporary(t); const parent = (await templateXml()).replace('id="Flash"','id="Parent"');
    const child = '<FXParticleSystemTemplate id="Child" inheritFrom="Parent"><Size xai:joinAction="Append" Low="7"/><Colors><Color Frame="3" RelativeAge="90"><Color r="1" g="1" b="1"/></Color></Colors><Volume><Sphere Radius="2"/></Volume></FXParticleSystemTemplate>';
    await fs.writeFile(path.join(root,'entry.xml'),wrap(parent+child)); const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'entry.xml'));
    let ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Child')!.key);
    assert.ok(ir.systems[0].config); assert.equal(ir.systems[0].config.size.low,7); assert.equal(ir.systems[0].config.size.high,24);
    assert.equal(ir.systems[0].config.volume.type,'Sphere');
    assert.equal(ir.systems[0].definition?.children.find(n=>n.name==='Colors')?.children.filter(n=>n.name==='Color').length,3);
    // Recovered provider walks ChildNodes, writes records in order, then native
    // loading overwrites a repeated Frame slot with the later compiled record.
    await fs.writeFile(path.join(root,'entry.xml'),wrap(parent+child.replace('Frame="3"','Frame="2"'))); await index.load(path.join(root,'entry.xml'));
    ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Child')!.key);
    assert.deepEqual(ir.systems[0].config?.colors?.[1].value,[1,1,1]); assert.equal(ir.systems[0].config?.colors?.[1].time,Math.fround(0.9));
    await fs.writeFile(path.join(root,'entry.xml'),wrap(parent+child.replace('<Colors>','<Colors xai:joinAction="Replace">'))); await index.load(path.join(root,'entry.xml'));
    ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Child')!.key);
    assert.ok(ir.systems[0].config); assert.equal(ir.systems[0].definition?.children.find(n=>n.name==='Colors')?.children.length,1);
    await fs.writeFile(path.join(root,'entry.xml'),wrap(parent+child.replace('joinAction="Append"','joinAction="Merge"'))); await index.load(path.join(root,'entry.xml'));
    ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Child')!.key);
    assert.equal(ir.systems[0].config,undefined); assert.ok(ir.issues.some(i=>i.code==='inherit-join-action'));
});

test('definition-local references obey Instance versus All reference propagation and do not leak sibling assets or parent defines', async t => {
    const root = await temporary(t); const template = await templateXml();
    await fs.writeFile(path.join(root,'library.xml'),wrap(template));
    await fs.writeFile(path.join(root,'bundle.xml'),wrap('<Includes><Include type="reference" source="library.xml"/></Includes>'));
    const calls = '<FXList id="Calls"><NuggetList><ParticleSystem Particle="Flash"/></NuggetList></FXList>';
    const index = new AssetIndex({roots:[root]});
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Includes><Include type="instance" source="bundle.xml"/></Includes>'+calls)); await index.load(path.join(root,'entry.xml'));
    let ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Calls')!.key);
    assert.equal(ir.systems.length,0); assert.ok(ir.issues.some(i=>i.code==='asset-missing'));
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Includes><Include type="all" source="bundle.xml"/></Includes>'+calls)); await index.load(path.join(root,'entry.xml'));
    ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Calls')!.key); assert.equal(ir.systems.length,1);
    await fs.writeFile(path.join(root,'texture.xml'),wrap('<Texture id="FxFixtureTexture" File="sibling.tga"/>'));
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Includes><Include type="all" source="library.xml"/><Include type="all" source="texture.xml"/></Includes>'+calls)); await index.load(path.join(root,'entry.xml'));
    ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Calls')!.key);
    assert.equal(ir.textures[0].status,'missing-declaration'); // Texture visible to entry, absent in Flash's defining document.
    await fs.writeFile(path.join(root,'library.xml'),wrap(template.replace('Low="24"','Low="= $ROOT_ONLY"')));
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Includes><Include type="all" source="library.xml"/></Includes><Defines><Define name="ROOT_ONLY" value="999"/></Defines>'+calls)); await index.load(path.join(root,'entry.xml'));
    ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Calls')!.key);
    assert.equal(ir.systems[0].config,undefined); assert.ok(ir.issues.some(i=>i.code==='expression-unresolved'));
});

test('ART basename resolution uses compiler two-character buckets with bounded case-insensitive matching', async t => {
    const root = await temporary(t); const mod = path.join(root,'mod'); const sdk = path.join(root,'sdk');
    await fs.mkdir(path.join(mod,'Data'),{recursive:true}); await fs.mkdir(path.join(sdk,'Art','FX'),{recursive:true});
    const empty = path.join(sdk,'Art','FX','fxlenzflare01.xml'); await fs.writeFile(empty,wrap(''));
    const index = new AssetIndex({roots:[mod,sdk],modRoot:mod,sdkRoot:sdk});
    assert.equal(await index.resolveFile(path.join(mod,'Data','entry.xml'),'art:fxlenzflare01.xml'),empty);
    assert.equal(await index.resolveFile(path.join(mod,'Data','entry.xml'),'ART:FX/FXLENZFLARE01.XML'),empty);
    await fs.mkdir(path.join(mod,'Art','FX'),{recursive:true}); const local = path.join(mod,'Art','FX','fxlenzflare01.xml'); await fs.writeFile(local,wrap(''));
    assert.equal(await index.resolveFile(path.join(mod,'Data','entry.xml'),'art:fxlenzflare01.xml'),local);
    await fs.writeFile(path.join(mod,'Data','entry.xml'),wrap('<Includes><Include type="all" source="art:fxlenzflare01.xml"/></Includes>'));
    await index.load(path.join(mod,'Data','entry.xml')); assert.equal(index.listAssets().length,0); assert.equal(index.graph.documents.size,2);
});

test('NewModCompiler source profile preserves SDK configuration order and never substitutes a full fallback for an existing empty stub', async t => {
    const root = await temporary(t); const mod = path.join(root,'mod'); const sdk = path.join(root,'sdk');
    await fs.mkdir(path.join(mod,'Data'),{recursive:true}); await fs.mkdir(path.join(sdk,'tools'),{recursive:true}); await fs.mkdir(path.join(sdk,'SageXml'),{recursive:true});
    const config = path.join(sdk,'tools','BinaryAssetBuilder.exe.config');
    const configuration = (dataPaths:string) => `<configuration><assetbuilder defaultDataPaths="${dataPaths}" defaultArtPaths=".\\Art" defaultAudioPaths=".\\Audio"/></configuration>`;
    await fs.writeFile(config,configuration('.;.\\Mods;.\\SageXml'));
    await fs.writeFile(path.join(sdk,'Static.xml'),wrap(''));
    await fs.writeFile(path.join(sdk,'SageXml','Static.xml'),wrap('<Texture id="MustNotBeSubstituted" File="old.tga"/>'));
    const entry = path.join(mod,'Data','Mod.xml'); await fs.writeFile(entry,wrap('<Includes><Include type="reference" source="DATA:static.xml"/></Includes>'));
    const index = new AssetIndex({roots:[mod,sdk],modRoot:mod,sdkRoot:sdk}); await index.load(entry);
    assert.equal(index.listAssets().length,0); assert.ok(index.graph.documents.has(path.join(sdk,'Static.xml')));
    assert.deepEqual(index.sourceDataProfile?.dataPaths,[path.join(mod,'Data'),sdk,path.join(sdk,'Mods'),path.join(sdk,'SageXml')]);
    assert.match(index.sourceDataProfile!.source,/mod-prepend\+sdk-config/); assert.ok(index.graph.dependencies.includes(config));
    assert.equal(await index.resolveFile(entry,'ROOT:Static.xml'),path.join(sdk,'Static.xml'));
    await fs.mkdir(path.join(sdk,'CustomData')); await fs.writeFile(path.join(sdk,'CustomData','static.xml'),wrap('<Texture id="ConfiguredSource" File="new.tga"/>'));
    await fs.writeFile(config,configuration('CustomData;.;SageXml')); assert.ok(index.invalidate(config).has(entry)); await index.load(entry);
    assert.equal(index.listAssets()[0].id,'ConfiguredSource');
});

test('two templates sharing one typed Texture declaration produce one transferable/GPU texture key', async t => {
    const root = await temporary(t); const template = await templateXml(); const second = template.replace('id="Flash"','id="SecondFlash"');
    await fs.writeFile(path.join(root,'entry.xml'),wrap('<Texture id="FxFixtureTexture" File="shared.tga"/>'+template+second+'<FXList id="Both"><NuggetList><ParticleSystem Particle="Flash"/><ParticleSystem Particle="SecondFlash"/></NuggetList></FXList>'));
    const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'entry.xml'));
    const ir = await new AssetResolver(index).resolve(index.listAssets().find(asset=>asset.id==='Both')!.key);
    assert.equal(ir.systems.length,2); assert.equal(ir.textures.length,1); assert.equal(ir.systems[0].textureKey,ir.systems[1].textureKey);
});

test('texture resolution separates no declaration, empty declaration, missing/empty File and actual File', async t => {
    const root = await temporary(t); const template = await templateXml();
    const variants: [string,string][] = [['','missing-declaration'],['<Texture id="FxFixtureTexture"/>','empty-declaration'],['<Texture id="FxFixtureTexture" GenerateMipMaps="true"/>','missing-file'],['<Texture id="FxFixtureTexture" File=""/>','empty-file'],['<Texture id="FxFixtureTexture" File="source.tga"/>','pending']];
    const index = new AssetIndex({roots:[root]});
    for (const [declaration,status] of variants) {
        await fs.writeFile(path.join(root,'entry.xml'),wrap(declaration+template)); await index.load(path.join(root,'entry.xml'));
        const ir = await new AssetResolver(index).resolve(index.listAssets().find(a=>a.id==='Flash')!.key);
        assert.equal(ir.textures[0].status,status);
        if (status==='pending') assert.equal(ir.textures[0].file,path.join(root,'source.tga'));
    }
});

test('SDK aliases stay readonly and local instances override reference-stream definitions explicitly', async t => {
    const root = await temporary(t); const mod = path.join(root,'mod'); const sdk = path.join(root,'sdk');
    await fs.mkdir(mod); await fs.mkdir(sdk); await fs.symlink(sdk,path.join(mod,'linked-sdk'),process.platform==='win32'?'junction':'dir');
    await fs.writeFile(path.join(sdk,'source.xml'),wrap('<Texture id="T" File="old.tga"/>'));
    await fs.writeFile(path.join(mod,'entry.xml'),wrap('<Includes><Include type="reference" source="linked-sdk/source.xml"/></Includes><Texture id="T" File="new.tga"/>'));
    const index = new AssetIndex({roots:[mod,sdk],modRoot:mod,sdkRoot:sdk}); await index.load(path.join(mod,'entry.xml'));
    const imported = index.listAssets().find(a=>a.includeType==='reference')!;
    assert.equal(imported.uri,path.join(sdk,'source.xml')); assert.equal(imported.readonly,true);
    // The SDK selector can use a different alias than the allowed resource root.
    const aliasedSelector = new AssetIndex({roots:[mod,sdk],modRoot:mod,sdkRoot:path.join(mod,'linked-sdk')});
    await aliasedSelector.load(path.join(mod,'entry.xml'));
    assert.equal(aliasedSelector.listAssets().find(a=>a.includeType==='reference')!.readonly,true);
    aliasedSelector.dispose();
});

test('a workspace containing the SDK, or selecting the SDK itself, cannot make SDK assets writable', async t => {
    const root = await temporary(t); const sdk = path.join(root,'SDK'); await fs.mkdir(sdk);
    const source = path.join(sdk,'source.xml'); await fs.writeFile(source,wrap('<Texture id="T" File="source.tga"/>'));
    const local = path.join(root,'local.xml'); await fs.writeFile(local,wrap('<Texture id="Local" File="local.tga"/>'));
    for (const mod of [root,sdk]) {
        const index = new AssetIndex({roots:[root,sdk],modRoot:mod,sdkRoot:sdk});
        await index.load(source); assert.equal(index.listAssets()[0].readonly,true); assert.equal(index.isReadonly(sdk),true);
        await index.load(local); assert.equal(index.listAssets()[0].readonly,false); index.dispose();
    }
});

test('a selected MOD strictly inside the SDK is writable while SDK sources remain readonly', async t => {
    const root = await temporary(t); const sdk = path.join(root,'SDK'); const mod = path.join(sdk,'Mods','MyMod');
    await fs.mkdir(mod,{recursive:true});
    const source = path.join(sdk,'source.xml'); const local = path.join(mod,'local.xml');
    await fs.writeFile(source,wrap('<Texture id="T" File="source.tga"/>')); await fs.writeFile(local,wrap('<Texture id="Local" File="local.tga"/>'));
    const index = new AssetIndex({roots:[sdk,mod],modRoot:mod,sdkRoot:sdk});
    await index.load(local); assert.equal(index.listAssets()[0].readonly,false);
    await index.load(source); assert.equal(index.listAssets()[0].readonly,true); assert.equal(index.isReadonly(sdk),true); index.dispose();
});

test('absent Atlas defaults expose authorized insertion fields at the real Gpu node', async t => {
    const root = await temporary(t);
    const template = (await templateXml()).replace(/ SingleRow="0" RandTex="0"/,'');
    await fs.writeFile(path.join(root,'entry.xml'),wrap(template)); const index = new AssetIndex({roots:[root]}); await index.load(path.join(root,'entry.xml'));
    const ir = await new AssetResolver(index).resolve(index.listAssets()[0].key);
    const field = ir.fields.find(f=>f.attribute==='SingleRow')!; assert.equal(field.defaulted,true); assert.equal(field.value,'0'); assert.equal(field.readonly,false);
    const document = index.getDocument(field.uri)!;
    const updated = applyTextPatches(document.text, prepareFieldTransaction(document,[{nodePath:field.nodePath,attribute:field.attribute,value:'2',documentVersion:document.version}]));
    assert.match(updated,/SingleRow="2"/); assert.match(updated,/FramesPerRow="1"/); // existing surrounding source survives.
});

test('versioned capabilities identify required fixture families and explicit unsupported modules', () => {
    assert.equal(fxCapabilities.version,3); assert.ok(fxCapabilities.fixtures.includes('smoke.xml'));
    assert.ok(fxCapabilities.supported.volume.includes('Sphere')); assert.ok(fxCapabilities.explicitGaps.some(gap=>gap.includes('CPU')));
});
