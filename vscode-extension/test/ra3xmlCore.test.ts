import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import {DocumentStore} from '../src/ra3xml/index/documentStore';
import {AssetIndex} from '../src/ra3xml/index/assetIndex';
import {AssetService} from '../src/ra3xml/services/assetService';
import {scanW3xDeclarations} from '../src/ra3xml/core/w3xDeclarations';
const ns='uri:ea.com:eala:asset';
const xml=(contents:string)=>`<AssetDeclaration xmlns="${ns}">${contents}</AssetDeclaration>`;
async function temporary(t:{after:(fn:()=>Promise<unknown>)=>void}):Promise<string>{const root=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'newsdk-xml-core-')));t.after(()=>fs.rm(root,{recursive:true,force:true}));return root;}

test('shared entry views reuse one parsed record and concurrent entries retain their own scope',async t=>{
    const root=await temporary(t);const a=path.join(root,'a.xml'),b=path.join(root,'b.xml');
    await fs.writeFile(a,xml('<GameObject id="A"/>'));await fs.writeFile(b,xml('<WeaponTemplate id="B"/>'));
    const index=new AssetIndex({roots:[root],modRoot:root,trustInvalidation:true});const first=index.createView(),second=index.createView();
    const [ga,gb]=await Promise.all([first.load(a),second.load(b)]);assert.equal(first.listAssets()[0].type,'GameObject');assert.equal(second.listAssets()[0].type,'WeaponTemplate');assert.notEqual(ga.entry,gb.entry);
    const third=index.createView();assert.equal(await third.load(a),ga);assert.equal(index.documentStore.statistics.parses,2);
    first.dispose();assert.equal((await third.load(a)).assets.size,1);second.dispose();third.dispose();index.dispose();
});

test('single-document overlay invalidates shared references without reparsing unchanged sources',async t=>{
    const root=await temporary(t),entry=path.join(root,'entry.xml'),defs=path.join(root,'defs.xml');
    await fs.writeFile(entry,xml('<Includes><Include type="all" source="defs.xml"/></Includes><GameObject id="User" Target="Target"/>'));
    await fs.writeFile(defs,xml('<GameObject id="Target"/>'));
    const index=new AssetIndex({roots:[root],modRoot:root,trustInvalidation:true,referenceType:(_node,attribute)=>attribute==='Target'?'GameObject':undefined});
    const a=index.createView(),b=index.createView();await a.load(entry);await b.load(entry);const before=index.documentStore.statistics;
    index.documentStore.set(defs,xml('<GameObject id="Other"/>'),2);index.invalidate(defs);const graph=await b.load(entry);
    assert.equal(index.documentStore.statistics.parses,before.parses+1);assert.deepEqual(graph.references!.records[0].targets,[]);assert.equal(a.graph.references!.records[0].targets.length,1);
    const refreshed=await a.load(entry);assert.equal(refreshed,graph);assert.equal(refreshed.assets.size,2);a.dispose();b.dispose();index.dispose();
});

test('typed reference lists retain individual lexical ranges and generic candidates use assignability',async t=>{
    const root=await temporary(t),entry=path.join(root,'entry.xml');const text=xml('<Texture id="A"/><Texture id="B"/><GameObject id="User" Targets="A B"/>');await fs.writeFile(entry,text);
    const index=new AssetIndex({roots:[root],referenceType:(_node,attribute)=>attribute==='Targets'?'BaseAssetType':undefined,referenceIsList:()=>true,assignableType:(actual,expected)=>actual==='Texture'&&expected==='BaseAssetType'});
    const graph=await index.load(entry);assert.equal(graph.references!.records.length,2);for(const record of graph.references!.records){assert.equal(text.slice(record.start,record.end),record.value);assert.equal(record.targets.length,1);}index.dispose();
});

test('persistent document records restore parent links and avoid disk reads/parses while rejecting changed files and overlays',async t=>{
    const root=await temporary(t),file=path.join(root,'a.xml');await fs.writeFile(file,xml('<GameObject id="A"><Child Value="1"/></GameObject>'));
    const original=new DocumentStore();await original.read(file);const records=original.exportRecords();assert.equal(records.length,1);
    const warmed=new DocumentStore();warmed.restoreRecords(JSON.parse(JSON.stringify(records)));const doc=await warmed.read(file);assert.equal(doc.find('/AssetDeclaration[1]/GameObject[1]/Child[1]')?.parent?.localName,'GameObject');assert.equal(warmed.statistics.reads,0);assert.equal(warmed.statistics.parses,0);
    warmed.set(file,xml('<GameObject id="Unsaved"/>'),3);assert.equal(warmed.get(file),undefined);await warmed.read(file);assert.equal(warmed.exportRecords().length,0);
    const stale=new DocumentStore();stale.restoreRecords(records);await fs.writeFile(file,xml('<GameObject id="Changed"/>'));const changed=await stale.read(file);assert.equal(changed.elements.find(n=>n.attributes.id)?.attributes.id,'Changed');assert.equal(stale.statistics.parses,1);
});

test('service transactions reject an unread newer overlay and disconnected entries cannot expose stale references',async t=>{
    const root=await temporary(t),a=path.join(root,'a.xml'),b=path.join(root,'b.xml');await fs.writeFile(a,xml('<Texture id="T"/>'));await fs.writeFile(b,xml('<Texture id="U"/>'));
    const service=new AssetService({roots:[root],modRoot:root});await service.ensure(a);await service.whenComplete();assert.equal(service.findAssets().length,2);
    service.setDocument(b,xml('<Texture id="New"/>'),7);assert.equal(service.findAssets().some(asset=>asset.id==='U'),false);assert.throws(()=>service.prepareEdits([{uri:b,documentVersion:0,start:0,end:0,expected:'',text:' '}]),/内容已更新/);
    await service.ensure(a);assert.equal(service.findAssets().some(asset=>asset.id==='New'),true);service.dispose();
});

test('W3X shallow declarations retain real offsets across discarded geometry and exclude vertices from source records',async t=>{
    const root=await temporary(t),file=path.join(root,'large.w3x');const first='<W3DMesh id="First">',second='<W3DMesh id="Second"/>';const text=xml(first+'<Vertices>'+('<V X="1"/>'.repeat(180000))+'</Vertices></W3DMesh>'+second);await fs.writeFile(file,text);
    const document=await scanW3xDeclarations(file);assert.equal(document.shallow,true);assert.ok(document.text.length<1000);assert.equal(document.elements.length,3);const asset=document.elements.find(node=>node.attributes.id==='Second')!;assert.equal(asset.start,text.indexOf(second));assert.equal(text.slice(asset.attributeTokens.id.valueStart,asset.attributeTokens.id.valueEnd),'Second');
    const index=new AssetIndex({roots:[root],maxSourceBytes:1024});const graph=await index.load(file);assert.equal(graph.assets.size,2);assert.equal(graph.issues.length,0);index.dispose();
});

test('new and removed project XML update completeness and references without stale standalone records',async t=>{
    const {XsdModel}=await import('../src/ra3xml/model/xsdModel');const root=await temporary(t),a=path.join(root,'a.xml'),b=path.join(root,'b.xml');await fs.writeFile(a,xml('<FXList id="A"/>'));
    const service=new AssetService({roots:[root],modRoot:root,schema:new XsdModel()});await service.ensure(a);await service.whenComplete();assert.equal(service.completeness,'complete');
    await fs.writeFile(b,xml('<Includes><Include type="all" source="a.xml"/></Includes><FXList id="B" EventFX="a"/>'));service.invalidate(b);assert.equal(service.completeness,'partial');await service.ensure(a);await service.whenComplete();assert.equal(service.completeness,'complete');assert.equal(service.findReferences(service.findAssets({id:'A'})[0]).length,1);
    await fs.rm(b);service.invalidate(b);await service.ensure(a);assert.equal(service.findAssets({id:'B'}).length,0);assert.equal(service.getReferences().length,0);assert.equal(service.completeness,'complete');service.dispose();
});

test('project source record budget applies across independently indexed documents',async t=>{
    const root=await temporary(t),a=path.join(root,'a.xml'),b=path.join(root,'b.xml');const source=xml('<GameObject id="A"/>');await fs.writeFile(a,source);await fs.writeFile(b,source);
    const documents=new DocumentStore({maxBytes:Buffer.byteLength(source)+1});await documents.read(a);await assert.rejects(documents.read(b),/项目源码记录预算/);assert.ok(documents.statistics.bytes<=Buffer.byteLength(source)+1);
});

test('canonical file aliases migrate pending editor overlays into the shared source version',async t=>{
    const root=await temporary(t),file=path.join(root,'source.xml'),alias=path.join(root,'alias.xml');await fs.writeFile(file,xml('<Texture id="Disk"/>'));
    try{await fs.symlink(file,alias);}catch(error){if((error as NodeJS.ErrnoException).code==='EPERM'){t.skip('Symlink privilege is unavailable');return;}throw error;}
    const service=new AssetService({roots:[root],modRoot:root});service.setDocument(alias,xml('<Texture id="Overlay"/>'),9);await service.ensure(alias);assert.equal(service.getDocument(alias)?.version,9);assert.equal(service.findAssets({id:'Overlay'}).length,1);assert.equal(service.findAssets({id:'Disk'}).length,0);service.dispose();
});

test('explicit index reload rechecks text when source metadata is unchanged and reuses identical CST records',async t=>{
    const root=await temporary(t),file=path.join(root,'entry.xml');await fs.writeFile(file,xml('<Texture id="First"/>'));
    // A text source can change within one filesystem timestamp tick. Keep the
    // physical file untouched to exercise an exact stat collision deterministically.
    let source=xml('<Texture id="First"/>');const index=new AssetIndex({roots:[root],readText:async()=>({text:source,version:0})});
    await index.load(file);const first=index.getDocument(file);assert.equal(index.listAssets()[0].id,'First');
    source=xml('<Texture id="Other"/>');await index.load(file);assert.equal(index.listAssets()[0].id,'Other');assert.notEqual(index.getDocument(file),first);const parses=index.documentStore.statistics.parses;const current=index.getDocument(file);
    await index.load(file);assert.equal(index.getDocument(file),current);assert.equal(index.documentStore.statistics.parses,parses);index.dispose();
});

test('new SDK schemas/manifests and source profile changes invalidate existing semantic sessions',async t=>{
    const root=await temporary(t),mod=path.join(root,'mod'),sdk=path.join(root,'sdk');
    await fs.mkdir(path.join(mod,'Data'),{recursive:true});await fs.mkdir(path.join(sdk,'Schemas'),{recursive:true});await fs.mkdir(path.join(sdk,'builtmods'),{recursive:true});await fs.mkdir(path.join(sdk,'tools'),{recursive:true});await fs.mkdir(path.join(sdk,'Sources1'));await fs.mkdir(path.join(sdk,'Sources2'));
    const schema=(name:string)=>`<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"><xs:element name="${name}" type="xs:string"/></xs:schema>`;
    await fs.writeFile(path.join(sdk,'Schemas','Base.xsd'),schema('InitialType'));
    const empty=Buffer.alloc(48);empty.set([0,1,6,0]);await fs.writeFile(path.join(sdk,'builtmods','empty.manifest'),empty);
    await fs.writeFile(path.join(sdk,'Sources1','Static.xml'),xml('<Texture id="FirstSource"/>'));await fs.writeFile(path.join(sdk,'Sources2','Static.xml'),xml('<Texture id="OtherSource"/>'));
    const config=path.join(sdk,'tools','BinaryAssetBuilder.exe.config');const profile=(folder:string)=>`<configuration><assetbuilder defaultDataPaths="${folder}"/></configuration>`;await fs.writeFile(config,profile('Sources1'));
    const entry=path.join(mod,'Data','Mod.xml');await fs.writeFile(entry,xml('<Includes><Include type="reference" source="DATA:Static.xml"/></Includes>'));
    const service=new AssetService({roots:[mod,sdk],modRoot:mod,sdkRoot:sdk});await service.ensure(entry);await service.whenComplete();assert.equal(service.completeness,'complete');assert.ok(service.findAssets({id:'FirstSource'}).length);
    const addedSchema=path.join(sdk,'Schemas','Added.xsd');await fs.writeFile(addedSchema,schema('NewType'));service.invalidate(addedSchema);assert.equal(service.completeness,'partial');await service.ensure(entry);await service.whenComplete();assert.ok(service.schema?.getElement('NewType'));
    const names=Buffer.from('Texture:NewManifestAsset\0'),manifest=Buffer.alloc(96+names.length);manifest.set([0,1,6,0]);manifest.writeUInt32LE(1,12);manifest.writeUInt32LE(names.length,40);names.copy(manifest,96);
    const addedManifest=path.join(sdk,'builtmods','added.manifest');await fs.writeFile(addedManifest,manifest);service.invalidate(addedManifest);assert.equal(service.completeness,'partial');await service.ensure(entry);await service.whenComplete();assert.equal(service.manifests?.find('Texture','NewManifestAsset').length,1);
    await fs.writeFile(config,profile('Sources2'));service.invalidate(config);assert.equal(service.completeness,'partial');await service.ensure(entry);await service.whenComplete();assert.equal(service.findAssets({id:'FirstSource'}).length,0);assert.equal(service.findAssets({id:'OtherSource'}).length,1);service.dispose();
});
