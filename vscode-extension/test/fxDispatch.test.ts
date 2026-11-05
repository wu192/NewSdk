import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { AssetIndex } from '../src/fx/assetIndex';
import { AssetResolver } from '../src/fx/assetResolver';
import { localRotationMatrix, transformVector } from '../webview/fx/runtime/math';
import { ParticleWorld, type WorldSystem } from '../webview/fx/runtime';
const ns='uri:ea.com:eala:asset';
const wrap=(text:string)=>`<AssetDeclaration xmlns="${ns}" xmlns:xi="http://www.w3.org/2001/XInclude" xmlns:xai="uri:ea.com:eala:asset:instance">${text}</AssetDeclaration>`;
async function fixture(t:{after:(f:()=>Promise<unknown>)=>void},extra='') {
    const root=await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(),'fx-dispatch-')));t.after(()=>fs.rm(root,{recursive:true,force:true}));
    const xml=await fs.readFile(path.resolve(__dirname,'../../test/fixtures/fx/Data/flash.xml'),'utf8');
    const particle=xml.slice(xml.indexOf('<FXParticleSystemTemplate'),xml.indexOf('</FXParticleSystemTemplate>')+'</FXParticleSystemTemplate>'.length);
    const index=new AssetIndex({roots:[root]});const file=path.join(root,'entry.xml');
    const load=async(body:string)=>{await fs.writeFile(file,wrap(particle+body));await index.load(file);return new AssetResolver(index);};
    const resolver=await load(extra);const key=(id:string)=>index.listAssets().find(a=>a.id===id)!.key;
    return {root,file,index,particle,load,resolver,key};
}
const calls=(body:string,id='Calls')=>`<FXList id="${id}"><NuggetList>${body}</NuggetList></FXList>`;
const close=(actual:readonly number[],expected:readonly number[])=>actual.forEach((n,i)=>assert.ok(Math.abs(n-expected[i])<1e-10,`${actual} != ${expected}`));

test('object predicate uses Source ModelConditions for both objects and position dispatch skips object conditions',async t=>{
    const f=await fixture(t,calls('<ParticleSystem Particle="Flash" RequiredSourceModelConditions="MOVING" RequiredSecondaryModelConditions="DAMAGED" ExcludedSourceModelConditions="RUBBLE"/>'));
    let ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',sourceModelConditions:['MOVING'],targetModelConditions:[]});
    assert.equal(ir.systems.length,0);assert.ok(ir.issues.some(i=>i.code==='condition-not-triggered'));
    ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',sourceModelConditions:['MOVING'],targetModelConditions:['MOVING']});
    assert.equal(ir.systems.length,1);assert.ok(ir.systems[0].config); // RequiredSecondary is not read by 520BA0.
    ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',sourceModelConditions:['MOVING'],targetModelConditions:['MOVING','RUBBLE']});assert.equal(ir.systems.length,0);
    ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'position'});assert.equal(ir.systems.length,1);
    ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',sourceModelConditions:['MOVING'],targetObject:false});assert.equal(ir.systems.length,1);
});

test('Weather predicate and execution-slot StopIfPlayed differ from terrain rejection, zero Count and bone no-op',async t=>{
    const f=await fixture(t);
    let r=await f.load(calls('<ParticleSystem Particle="Flash" Weather="RAINY" StopIfPlayed="true"/><ParticleSystem Particle="Flash"/>'));
    let ir=await r.resolve(f.key('Calls'),{weather:'NORMAL'});assert.equal(ir.systems.length,1);
    r=await f.load(calls('<ParticleSystem Particle="Flash" OnlyIfOnLand="true" StopIfPlayed="true"/><ParticleSystem Particle="Flash"/>'));
    ir=await r.resolve(f.key('Calls'),{terrain:'water'});assert.equal(ir.systems.length,1);assert.equal(ir.systems[0].triggered,false);
    for(const first of ['<ParticleSystem Particle="Flash" Count="0" StopIfPlayed="true"/>','<FXListAtBonePos FX="Calls" Bone="BODY" StopIfPlayed="true"/>']) {
        r=await f.load(calls(first+'<ParticleSystem Particle="Flash"/>'));ir=await r.resolve(f.key('Calls'));assert.equal(ir.systems.length,0);
    }
    assert.ok(ir.issues.some(i=>i.code==='bone-position-noop'));
});

test('ordinary object placement rotates Offset in the input matrix and composes system orientation with Rotate',async t=>{
    const f=await fixture(t,calls('<ParticleSystem Particle="Flash" OrientToObject="true" UseTarget="true" TargetCoeff="0.5"><Offset x="2" y="0" z="3"/><TargetOffset x="1" y="2" z="3"/><Rotate X="0" Y="0" Z="90d"/></ParticleSystem>'));
    const ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',source:[5,6,7],target:[20,20,20],sourceRotation:[0,0,Math.PI/2]});
    const system=ir.systems[0];assert.ok(system.config);close(system.offset,[5,8,10]);close(system.target!,[6,10,13]);
    close(transformVector([1,0,0],system.rotationMatrix!),[-1,0,0]);
    assert.ok(ir.issues.some(i=>i.code==='nugget-unused'&&i.message.includes('TargetCoeff')));
});

test('Ricochet derives input yaw from source minus secondary; SetTargetMatrix requires OrientToObject',async t=>{
    const f=await fixture(t,calls('<ParticleSystem Particle="Flash" Ricochet="true" OrientToObject="true"><Offset x="2" y="0" z="0"/></ParticleSystem><ParticleSystem Particle="Flash" SetTargetMatrix="true"/>'));
    const ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',source:[5,6,0],target:[7,6,0]});
    close(ir.systems[0].offset,[3,6,0]);close(transformVector([1,0,0],ir.systems[0].rotationMatrix!),[-1,0,0]);
    close(ir.systems[1].rotationMatrix!,localRotationMatrix([0,0,0]));
});

test('unsupported object filters and bone overrides retain specific source diagnostics',async t=>{
    const f=await fixture(t,calls('<ParticleSystem Particle="Flash"><SourceObjectFilter Rule="ALL"/></ParticleSystem><ParticleSystem Particle="Flash" CreateOverrideBone="BONE"/>'));
    let ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object'});assert.equal(ir.systems.length,1);assert.equal(ir.systems[0].config,undefined);
    assert.ok(ir.issues.some(i=>i.code==='object-filter-context'&&i.nodePath.endsWith('/SourceObjectFilter[1]')));
    assert.ok(ir.issues.some(i=>i.code==='bone-context'&&i.nodePath.endsWith('/ParticleSystem[2]')));
    ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'position'});assert.ok(ir.systems[0].config); // Native position predicate receives null objects.
});

test('stealth state and disabled-type checks are distinct native object predicate gates',async t=>{
    const f=await fixture(t,calls('<ParticleSystem Particle="Flash"><SourceMustNotHaveBeenDisabledThisFrameByType>EMP</SourceMustNotHaveBeenDisabledThisFrameByType></ParticleSystem>'));
    let ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',sourceStealthState:5});assert.equal(ir.systems.length,0);
    ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'object',sourceDisabledTypesThisFrame:['EMP']});assert.equal(ir.systems.length,0);
    ir=await f.resolver.resolve(f.key('Calls'),{dispatch:'position',sourceStealthState:5,sourceDisabledTypesThisFrame:['EMP']});assert.equal(ir.systems.length,1);
});

test('xi:include preserves physical fields while its single-asset fragment joins logical document inheritance and Defines',async t=>{
    const f=await fixture(t);
    const fragment=path.join(f.root,'parent.xml');
    await fs.writeFile(fragment,f.particle.replace('<FXParticleSystemTemplate','<FXParticleSystemTemplate xmlns="'+ns+'"').replace('id="Flash"','id="Parent"').replace('Low="24"','Low="= $SIZE"'));
    await fs.writeFile(f.file,wrap('<xi:include href="parent.xml"/><Defines><Define name="SIZE" value="3*7"/></Defines><Texture id="FxFixtureTexture" File="test.tga"/><FXParticleSystemTemplate id="Child" inheritFrom="Parent"><Size High="22"/></FXParticleSystemTemplate>'));
    await f.index.load(f.file);const ir=await new AssetResolver(f.index).resolve(f.key('Child'));
    assert.equal(ir.systems[0].config?.size.low,21);assert.equal(ir.systems[0].config?.size.high,22);assert.equal(ir.textures[0].status,'pending');
    const field=ir.fields.find(field=>field.group==='尺寸'&&field.attribute==='Low')!;
    assert.equal(field.uri,fragment);assert.equal(field.value,'= $SIZE');assert.equal(field.inherited,true);
    assert.equal(f.index.getAsset(field.sourceAsset!)?.scopeUri,f.file);assert.ok(!ir.issues.some(i=>i.code==='inherit-cross-document'));
});

test('xi:include does not silently flatten a nested AssetDeclaration wrapper',async t=>{
    const f=await fixture(t);await fs.writeFile(path.join(f.root,'nested.xml'),wrap(f.particle));
    await fs.writeFile(f.file,wrap('<xi:include href="nested.xml"/>'));await f.index.load(f.file);
    assert.equal(f.index.listAssets().length,0);assert.ok(f.index.graph.issues.some(i=>i.code==='xinclude-nested-declaration'));
});

test('compiler enum-list +/- override keeps inherited ModelConditions and raw edit source',async t=>{
    const f=await fixture(t,calls('<ParticleSystem id="N" Particle="Flash" RequiredSourceModelConditions="MOVING DAMAGED"/>','Base')+'<FXList id="Child" inheritFrom="Base"><NuggetList><ParticleSystem id="N" RequiredSourceModelConditions="-DAMAGED +REALLYDAMAGED"/></NuggetList></FXList>');
    const ir=await f.resolver.resolve(f.key('Child'),{dispatch:'object',sourceModelConditions:['MOVING','REALLYDAMAGED'],targetObject:false});
    assert.equal(ir.systems.length,1);assert.ok(ir.systems[0].config);
    assert.equal(ir.fields.find(field=>field.attribute==='RequiredSourceModelConditions')?.value,'-DAMAGED +REALLYDAMAGED');
    const broken=await f.load(calls('<ParticleSystem id="N" Particle="Flash" RequiredSourceModelConditions="MOVING"/>','Base')+'<FXList id="Child" inheritFrom="Base"><NuggetList><ParticleSystem id="N" RequiredSourceModelConditions="-DAMAGED"/></NuggetList></FXList>');
    const bad=await broken.resolve(f.key('Child'));assert.equal(bad.systems.length,0);assert.ok(bad.issues.some(i=>i.code==='inherit-bitflag'));
});

test('numeric arithmetic retains lexical source; unproven unit-expression grammar remains diagnosed',async t=>{
    const f=await fixture(t,'<Defines><Define name="DELAY" value="250"/><Define name="ANGLE" value="45*2"/></Defines>'+calls('<ParticleSystem Particle="Flash" SystemLife="= $DELAY / 1000"><Rotate X="0" Y="0" Z="= $ANGLE * 3.141592653589793 / 180"/></ParticleSystem>'));
    const ir=await f.resolver.resolve(f.key('Calls'));assert.equal(ir.systems[0].systemLifeFrames,8);assert.equal(ir.systems[0].rotation,Math.PI/2);
    assert.equal(ir.fields.find(field=>field.attribute==='SystemLife')?.value,'= $DELAY / 1000');
    const r=await f.load('<Defines><Define name="DELAY" value="250"/></Defines>'+calls('<ParticleSystem Particle="Flash" SystemLife="= $DELAY ms"/>'));
    const unresolved=await r.resolve(f.key('Calls'));assert.equal(unresolved.systems.length,0);assert.ok(unresolved.issues.some(issue=>issue.code==='expression-unresolved'));
});

test('FXList numeric literals never evaluate unmarked arithmetic or recover compiler numeric prefixes',async t=>{
    const f=await fixture(t);
    const cases:[string,string][]=[
        ['Count','<ParticleSystem Particle="Flash" Count="1+2"/>'],
        ['Count','<ParticleSystem Particle="Flash" Count="1e2"/>'],
        ['Count','<ParticleSystem Particle="Flash" Count="2junk"/>'],
        ['SystemLife','<ParticleSystem Particle="Flash" SystemLife="1+2s"/>'],
        ['SystemLife','<ParticleSystem Particle="Flash" SystemLife="500msjunk"/>'],
        ['Z','<ParticleSystem Particle="Flash"><Rotate Z="45+45d"/></ParticleSystem>'],
        ['x','<ParticleSystem Particle="Flash"><Offset x="1+2"/></ParticleSystem>'],
        ['Low','<ParticleSystem Particle="Flash"><InitialDelay Low="100+100" High="200"/></ParticleSystem>'],
    ];
    for(const [attribute,nugget] of cases){
        const resolver=await f.load(calls(nugget)),ir=await resolver.resolve(f.key('Calls'));
        assert.ok(!ir.systems.some(system=>system.config&&system.triggered),nugget);
        const issue=ir.issues.find(issue=>issue.code==='invalid-value');assert.ok(issue,nugget);
        assert.equal(issue.uri,f.file);assert.equal(issue.start,f.index.getDocument(f.file)!.find(issue.nodePath)!.attributeTokens[attribute].valueStart);
        assert.ok(ir.fields.some(field=>field.attribute===attribute&&/[+]|junk|1e2/.test(field.value)),nugget);
    }
    const resolver=await f.load('<FXList id="Calls" CullTracking="1+2s"><NuggetList><ParticleSystem Particle="Flash"/></NuggetList></FXList>');
    const ir=await resolver.resolve(f.key('Calls'));assert.equal(ir.systems.length,0);assert.ok(ir.issues.some(issue=>issue.code==='invalid-value'));
});

test('FXList leading-equals expressions and complete literal units remain valid after strict numeric validation',async t=>{
    const f=await fixture(t,'<Defines><Define name="COPIES" value="2"/></Defines>'+calls('<ParticleSystem Particle="Flash" Count="= $COPIES" SystemLife="2.5e2ms"><Offset x="= 1+2"/><Rotate Z="90d"/><InitialDelay Low="2e2" High="200"/></ParticleSystem>'));
    const ir=await f.resolver.resolve(f.key('Calls'));assert.equal(ir.systems.length,2);
    for(const system of ir.systems){assert.ok(system.config);assert.equal(system.systemLifeFrames,8);assert.equal(system.rotation,Math.PI/2);assert.equal(system.offset[0],3);assert.equal(system.initialDelayOverrideMs?.low,200);}
    assert.ok(!ir.issues.some(issue=>issue.code==='invalid-value'));
    assert.equal(ir.fields.find(field=>field.attribute==='Count')?.value,'= $COPIES');
});

function worldDefinition(system: import('../src/fx/previewProtocol').FxSystem):WorldSystem {
    return {id:system.key,templateKey:system.templateKey,sourceId:system.key,orientToObject:system.orientToObject,config:system.config!,options:{offset:system.offset,rotation:system.rotation,rotationXYZ:system.rotationXYZ,rotationMatrix:system.rotationMatrix,initialDelayOverrideMs:system.initialDelayOverrideMs,systemLifeFrames:system.systemLifeFrames,startFrame:system.startFrame,createAtGroundHeight:system.createAtGroundHeight}};
}
test('EventFX and attached cycles remain bounded references and queued runtime creation is deterministic',async t=>{
    const f=await fixture(t);
    const cpu=f.particle.replace('id="Flash"','id="Parent" PerParticleAttachedSystem="Parent"').replace('Type="GPU_PARTICLE"','Type="PARTICLE"').replace(/<Draw>.*?<\/Draw>/s,'<Draw><Default/></Draw>').replace('<Event/>','<Event><Life EventFX="Loop" PerParticle="true"><EventTime Low="1" High="1"/></Life></Event>');
    await fs.writeFile(f.file,wrap(cpu+calls('<ParticleSystem Particle="Parent"/>','Loop')));await f.index.load(f.file);
    const ir=await new AssetResolver(f.index).resolve(f.key('Loop'));assert.ok(ir.systems[0].config);assert.equal(ir.dynamic?.templates.length,1);assert.equal(ir.dynamic?.fxLists.length,1);
    assert.equal(ir.dynamic!.templates[0].config!.attachedSystem,ir.dynamic!.templates[0].templateKey);
    assert.equal(ir.dynamic!.templates[0].config!.events![0].fx,ir.dynamic!.fxLists[0].key);
    assert.deepEqual(ir.dynamic!.initialFxLists,[f.key('Loop')]);assert.ok(JSON.stringify(ir).length<512*1024);
    const graph={...ir.dynamic!,templates:ir.dynamic!.templates.map(worldDefinition),fxLists:ir.dynamic!.fxLists.map(list=>({...list,systems:list.systems.map(worldDefinition)})),maxSystems:16,maxDepth:4};
    const world=new ParticleWorld(ir.systems.map(worldDefinition),17,1000,100,graph);
    world.seek(12);const snapshot=world.systems.map(s=>({id:s.id,particles:s.runtime.particles.map(p=>[p.id,...p.position])}));
    assert.ok(world.systems.length<=16);world.seek(0);world.seek(12);assert.deepEqual(world.systems.map(s=>({id:s.id,particles:s.runtime.particles.map(p=>[p.id,...p.position])})),snapshot);
    world.dispose();
});

test('CullTracking uses raw compiler f32 Time value, and dynamic references participate in invalidation',async t=>{
    const f=await fixture(t);
    const cpu=f.particle.replace('id="Flash"','id="Parent"').replace('Type="GPU_PARTICLE"','Type="PARTICLE"').replace(/<Draw>.*?<\/Draw>/s,'<Draw><Default/></Draw>').replace('<Event/>','<Event><Life EventFX="Pulse" PerParticle="true"><EventTime Low="1" High="1"/></Life></Event>');
    await fs.writeFile(f.file,wrap(cpu+'<FXList id="Pulse" CullTracking="1000ms" CullTrackingMin="2" CullTrackingMax="4"><NuggetList><ParticleSystem Particle="Parent"/></NuggetList></FXList>'));await f.index.load(f.file);
    const ir=await new AssetResolver(f.index).resolve(f.key('Parent'));assert.equal(ir.dynamic!.fxLists[0].cullTrackingFrames,1);
    assert.ok(f.index.dependentsOf(f.key('Pulse')).has(f.key('Parent')));assert.ok(f.index.dependentsOf(f.key('Parent')).has(f.key('Pulse')));
});

test('event placement transforms full Offset before ground snap and inherits matrix only when OrientToObject is enabled',async t=>{
    const f=await fixture(t);
    const cpu=f.particle.replace('id="Flash"','id="Parent"').replace('Type="GPU_PARTICLE"','Type="PARTICLE"').replace(/<Draw>.*?<\/Draw>/s,'<Draw><Default/></Draw>').replace('<Event/>','<Event><Life EventFX="Pulse"><EventTime Low="1000" High="1000"/></Life></Event>');
    const list=calls('<ParticleSystem Particle="Flash"><Offset x="2" y="0" z="3"/></ParticleSystem><ParticleSystem Particle="Flash" OrientToObject="true"/><ParticleSystem Particle="Flash" CreateAtGroundHeight="true"><Offset x="2" y="0" z="3"/></ParticleSystem>','Pulse');
    await fs.writeFile(f.file,wrap(f.particle+cpu+list));await f.index.load(f.file);
    const ir=await new AssetResolver(f.index).resolve(f.key('Parent'),{ground:5});
    const graph={...ir.dynamic!,templates:ir.dynamic!.templates.map(worldDefinition),fxLists:ir.dynamic!.fxLists.map(list=>({...list,systems:list.systems.map(worldDefinition)})),groundHeight:5};
    const world=new ParticleWorld(ir.systems.map(worldDefinition),1,1000,100,graph);world.seek(0);
    world.systems[0].runtime.onEvent?.({fx:ir.dynamic!.fxLists[0].key,position:[10,20,30],rotationXYZ:[0,Math.PI/2,0]});
    world.seek(1);const spawned=world.systems.filter(system=>system.dynamic);assert.equal(spawned.length,3);
    close(spawned[0].runtime.options.offset!,[13,20,28]);close(spawned[0].runtime.options.rotationMatrix!,localRotationMatrix([0,0,0]));
    close(spawned[1].runtime.options.rotationMatrix!,localRotationMatrix([0,Math.PI/2,0]));
    close(spawned[2].runtime.options.offset!,[13,20,5]);world.dispose();
});

test('repeated xi:include creates duplicate inline definitions instead of asset-include deduplication',async t=>{
    const f=await fixture(t);await fs.writeFile(path.join(f.root,'inline.xml'),f.particle.replace('<FXParticleSystemTemplate','<FXParticleSystemTemplate xmlns="'+ns+'"'));
    await fs.writeFile(f.file,wrap('<xi:include href="inline.xml"/><xi:include href="inline.xml"/>'+calls('<ParticleSystem Particle="Flash"/>')));await f.index.load(f.file);
    assert.equal(f.index.listAssets().filter(asset=>asset.id==='Flash').length,2);
    const ir=await new AssetResolver(f.index).resolve(f.key('Calls'));assert.equal(ir.systems.length,0);assert.ok(ir.issues.some(issue=>issue.code==='asset-ambiguous'));
});
