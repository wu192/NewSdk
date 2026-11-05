import {test} from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import * as os from 'node:os';
import {AssetService} from '../src/ra3xml/services/assetService';
import {XsdModel} from '../src/ra3xml/model/xsdModel';
import {SemanticCache} from '../src/ra3xml/index/cache';
import {AssetSearchIndex} from '../src/ra3xml/index/assetSearchIndex';
const schemaText='<xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" xmlns:a="urn:reference" targetNamespace="uri:ea.com:eala:asset"><xs:complexType name="GameObject"><xs:attribute name="id" type="xs:string"/><xs:attribute name="Target" type="xs:string" a:refType="GameObject"/></xs:complexType><xs:element name="GameObject" type="GameObject"/><xs:complexType name="FXList"><xs:attribute name="id" type="xs:string"/></xs:complexType><xs:element name="FXList" type="FXList"/><xs:element name="AssetDeclaration"><xs:complexType><xs:choice minOccurs="0" maxOccurs="unbounded"><xs:element name="GameObject" type="GameObject"/><xs:element name="FXList" type="FXList"/></xs:choice></xs:complexType></xs:element></xs:schema>';
function manifest(name:string):Buffer {
    const names=Buffer.from(`${name}\0`);const bytes=Buffer.alloc(96+names.length);bytes.writeUInt16LE(6,2);bytes.writeUInt32LE(1,12);bytes.writeUInt32LE(names.length,40);names.copy(bytes,96);return bytes;
}
async function fixture() {
    const root=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'newsdk-asset-search-')));const mod=path.join(root,'mod'),sdk=path.join(root,'sdk'),data=path.join(mod,'Data'),storage=path.join(root,'storage');
    await fs.mkdir(data,{recursive:true});await fs.mkdir(path.join(sdk,'builtmods'),{recursive:true});await fs.writeFile(path.join(sdk,'CnC3Types.xsd'),schemaText);
    const schema=await XsdModel.load(sdk);await fs.writeFile(path.join(sdk,'builtmods','base.manifest'),manifest('GameObject:ManifestOnly'));
    const sdkXml=path.join(sdk,'base.xml');await fs.writeFile(sdkXml,'<AssetDeclaration><GameObject id="Shared"/></AssetDeclaration>');
    const entry=path.join(data,'Mod.xml');await fs.writeFile(entry,`<AssetDeclaration><Includes><Include type="reference" source="${sdkXml}"/></Includes><GameObject id="Shared"/><GameObject id="BlueSpark"/><GameObject id="User" Target="BlueSpark"/><GameObject id="Dynamic" Target="$PICK"/><FXList id="ExternalEntry"/></AssetDeclaration>`);
    const service=new AssetService({roots:[mod,sdk],modRoot:mod,sdkRoot:sdk,storagePath:storage,schema});
    return {root,mod,sdk,data,entry,sdkXml,storage,schema,service,cleanup:async()=>{service.dispose();await fs.rm(root,{recursive:true,force:true});}};
}
test('asset search shares current semantic data, preserves all sources and never rereads for typed queries',async t=>{
    const f=await fixture();t.after(f.cleanup);await f.service.startIndexing();
    const duplicate=f.service.searchAssets({text:'GameObject:Shared'});assert.equal(duplicate.total,2);assert.deepEqual(duplicate.items.map(item=>item.source).sort(),['mod','sdk-xml']);
    const metadata=f.service.searchAssets({source:'manifest'}).items[0];assert.equal(metadata.id,'ManifestOnly');assert.equal(metadata.location,undefined);
    const reads=f.service.documents.statistics.reads,parses=f.service.documents.statistics.parses,version=f.service.indexVersion;
    for(const text of ['B','Bl','Blue','Spark','gameobject:bluespark'])assert.equal(f.service.searchAssets({text}).items[0].id,'BlueSpark');
    assert.equal(f.service.searchAssets({text:'BlueSpark'}).items[0].referenceCount,1);
    assert.equal(f.service.searchAssets({text:'Shared',source:'mod',directory:'Data'}).total,1);
    assert.equal(f.service.documents.statistics.reads,reads);assert.equal(f.service.documents.statistics.parses,parses);assert.equal(f.service.indexVersion,version);
    const report=f.service.getIndexReport();assert.equal(report.completeness,'complete');assert.ok(report.files>=3);assert.equal(report.pending,0);assert.ok(report.lastDurationMs!==undefined);
});
test('unreferenced results remain qualified candidates and refresh after an incremental edit',async t=>{
    const f=await fixture();t.after(f.cleanup);await f.service.ensure(f.entry);await f.service.whenComplete();
    const page=f.service.getUnreferencedAssets();assert.ok(!page.items.some(asset=>asset.id==='BlueSpark'));assert.ok(page.items.every(asset=>asset.source==='mod'&&asset.usage==='not-statically-referenced'));
    assert.ok(page.items.find(asset=>asset.id==='ExternalEntry')?.warnings.some(message=>message.includes('外部')));
    assert.ok(page.items.every(asset=>asset.warnings.some(message=>message.includes('动态引用'))));
    const version=f.service.indexVersion;const document=f.service.getDocument(f.entry)!;f.service.setDocument(f.entry,document.text.replace('Target="BlueSpark"','Target="Shared"'),9);
    await f.service.ensure(f.entry);await f.service.whenComplete();
    assert.ok(f.service.indexVersion>version);assert.ok(f.service.getUnreferencedAssets({text:'Blue'}).items.some(asset=>asset.id==='BlueSpark'));
    f.service.completeness='partial';assert.ok(f.service.getUnreferencedAssets().items.every(asset=>asset.warnings.some(message=>message.includes('尚未完整'))));
});
test('scoped cache rebuild preserves MOD, compiler cache, other XML identity and dirty overlays',async t=>{
    const f=await fixture();t.after(f.cleanup);await f.service.startIndexing();
    const before=await fs.readFile(f.entry,'utf8');const sentinel=path.join(f.mod,'SuperCompilerCache','keep');await fs.mkdir(path.dirname(sentinel),{recursive:true});await fs.writeFile(sentinel,'compiler');
    const other=new SemanticCache<{keep:boolean}>(f.storage,{modRoot:path.join(f.root,'other-mod'),sdkRoot:f.sdk,schemaVersion:f.schema.version});await other.write({keep:true});
    f.service.setDocument(f.entry,before.replace('id="BlueSpark"','id="DirtyOverlay"'),20);await f.service.ensure(f.entry);
    await f.service.clearCacheAndRebuild();assert.equal(await fs.readFile(f.entry,'utf8'),before);assert.equal(await fs.readFile(sentinel,'utf8'),'compiler');
    assert.deepEqual(await other.read(),{keep:true});assert.equal(f.service.searchAssets({text:'DirtyOverlay'}).total,1);assert.equal(f.service.searchAssets({text:'BlueSpark'}).total,0);
});
test('bounded metadata search supports exact type, substring and pagination',()=>{
    const index=new AssetSearchIndex();index.reset(Array.from({length:12},(_,number)=>({key:`k${number}`,type:number%2?'Texture':'FXList',id:`BlueSmoke${number}`,source:'mod',uri:'/mod.xml',relativePath:'Data/fx.xml',referenceCount:0,readonly:false})));
    const page=index.query({text:'FXList:Smoke',offset:2,limit:3});assert.equal(page.total,6);assert.equal(page.items.length,3);assert.ok(page.items.every(asset=>asset.type==='FXList'));
    assert.equal(index.query({text:'Texture:',limit:99999}).limit,500);assert.equal(index.query({text:'missing'}).total,0);
});
test('optional SDK XML participation uses the same scanner and excludes unselected SDK trees',async t=>{
    const f=await fixture();t.after(f.cleanup);await fs.mkdir(path.join(f.sdk,'SageXML'),{recursive:true});await fs.mkdir(path.join(f.sdk,'tools'),{recursive:true});
    await fs.writeFile(path.join(f.sdk,'SageXML','extra.xml'),'<AssetDeclaration><GameObject id="SdkExtra"/></AssetDeclaration>');
    await fs.writeFile(path.join(f.sdk,'tools','not-source.xml'),'<AssetDeclaration><GameObject id="NotSource"/></AssetDeclaration>');
    await f.service.startIndexing();assert.equal(f.service.searchAssets({text:'SdkExtra'}).total,0);
    const enabled=new AssetService({...f.service.options,includeSdkXml:true});t.after(()=>enabled.dispose());await enabled.startIndexing();
    assert.equal(enabled.searchAssets({text:'SdkExtra',source:'sdk-xml'}).total,1);assert.equal(enabled.searchAssets({text:'NotSource'}).total,0);
    assert.equal(enabled.getIndexReport().sdkXmlIncluded,true);
});
test('usage of a shared xi:include declaration through either logical scope prevents a false unreferenced candidate',async t=>{
    const f=await fixture();t.after(f.cleanup);const fragment=path.join(f.data,'shared.xml'),a=path.join(f.data,'a.xml'),b=path.join(f.data,'b.xml');
    await fs.writeFile(fragment,'<GameObject xmlns="uri:ea.com:eala:asset" id="InlineUsed"/>');
    await fs.writeFile(a,'<AssetDeclaration xmlns="uri:ea.com:eala:asset" xmlns:xi="http://www.w3.org/2001/XInclude"><xi:include href="shared.xml"/><GameObject id="Caller" Target="InlineUsed"/></AssetDeclaration>');
    await fs.writeFile(b,'<AssetDeclaration xmlns="uri:ea.com:eala:asset" xmlns:xi="http://www.w3.org/2001/XInclude"><xi:include href="shared.xml"/></AssetDeclaration>');
    await f.service.startIndexing();assert.ok(f.service.searchAssets({text:'InlineUsed'}).total>=2);assert.equal(f.service.getUnreferencedAssets({text:'InlineUsed'}).total,0);
});
test('incremental orphan deletion removes its graph without leaving a missing-file diagnostic',async t=>{
    const f=await fixture();t.after(f.cleanup);const orphan=path.join(f.data,'orphan.xml');await fs.writeFile(orphan,'<AssetDeclaration><GameObject id="Orphan"/></AssetDeclaration>');
    await f.service.startIndexing();assert.equal(f.service.searchAssets({text:'Orphan'}).total,1);
    await fs.rm(orphan);f.service.invalidate(orphan);await f.service.startIndexing();
    assert.equal(f.service.searchAssets({text:'Orphan'}).total,0);assert.equal(f.service.getIndexReport().completeness,'complete');assert.ok(!f.service.getIndexReport().issues.some(issue=>issue.includes('orphan.xml')));
});
test('explicit reindex keeps consumer FX views usable and selects new shared graphs',async t=>{
    const f=await fixture();t.after(f.cleanup);const view=f.service.createView();await view.load(f.entry);await f.service.startIndexing();
    await f.service.reindex();const graph=await view.load(f.entry);assert.ok(graph.assets.size>0);assert.equal(f.service.searchAssets({text:'BlueSpark'}).total,1);
});
test('SDK XML created after a completed scan is added incrementally when participation is enabled',async t=>{
    const f=await fixture();t.after(f.cleanup);await fs.mkdir(path.join(f.sdk,'SageXML'),{recursive:true});
    const enabled=new AssetService({...f.service.options,includeSdkXml:true});t.after(()=>enabled.dispose());await enabled.startIndexing();
    const file=path.join(f.sdk,'SageXML','new.xml');await fs.writeFile(file,'<AssetDeclaration><GameObject id="AddedLater"/></AssetDeclaration>');
    enabled.invalidate(file);await enabled.startIndexing();assert.equal(enabled.searchAssets({text:'AddedLater',source:'sdk-xml'}).total,1);assert.equal(enabled.completeness,'complete');
});
test('getScope returns an orphan document scope rather than an unrelated MOD entry',async t=>{
    const f=await fixture();t.after(f.cleanup);const orphan=path.join(f.data,'orphan.xml');await fs.writeFile(orphan,'<AssetDeclaration><Defines><Define name="LOCAL" value="1"/></Defines><GameObject id="Orphan"/></AssetDeclaration>');
    const graph=await f.service.getScope(orphan);assert.ok(graph.documents.has(orphan));assert.equal(graph.documents.get(orphan)?.elements.find(node=>node.localName==='Define')?.attributes.name,'LOCAL');
});
