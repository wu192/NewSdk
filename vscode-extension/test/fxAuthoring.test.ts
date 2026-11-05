import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { prepareAuthoringPlan, localAuthoringChecks, parseAssistantResult } from '../src/fx/authoring';
import { canApplyAuthoring } from '../src/fx/skillIntent';
import { parseXml } from '../src/fx/xmlDocument';
const namespace='uri:ea.com:eala:asset';
const xml=(contents:string)=>`<AssetDeclaration xmlns="${namespace}">${contents}</AssetDeclaration>`;

test('AI proposal edits only selected identity and preserves comments, includes, unknown nodes and lexical quotes',()=>{
    const original=xml(`<!-- keep -->\r\n<Include source='DATA:other.xml'/>\r\n<FXParticleSystemTemplate id='A' Shader='GPUParticle.fx'><Size Low='1' High='2'/><!-- inside --><Unknown Value='keep'/></FXParticleSystemTemplate><FXList id="B" CullTracking="keep"/>`);
    const proposed=xml(`<FXParticleSystemTemplate id="A" Shader="GPUParticle.fx"><Size Low="3" High="2"/></FXParticleSystemTemplate><FXList id="B" CullTracking="unwanted"/>`);
    const plan=prepareAuthoringPlan(original,proposed,7,'edit',{type:'FXParticleSystemTemplate',id:'A'});
    assert.equal(plan.text,original.replace("Low='1'","Low='3'"));assert.equal(plan.patches.length,1);assert.match(plan.changes[0],/Low/);
});
test('AI structure and target identity changes require manual XML editing',()=>{
    const original=xml(`<FXParticleSystemTemplate id="A"><Size Low="1"/></FXParticleSystemTemplate>`);
    assert.throws(()=>prepareAuthoringPlan(original,xml(`<FXParticleSystemTemplate id="B"/>`),1,'edit',{type:'FXParticleSystemTemplate',id:'A'}),/相同类型和 ID/);
    assert.throws(()=>prepareAuthoringPlan(original,xml(`<FXParticleSystemTemplate id="A"><Size Low="2"/><LightningParticleSystem/></FXParticleSystemTemplate>`),1,'edit',{type:'FXParticleSystemTemplate',id:'A'}),/结构或家族/);
});
test('new declarations inherit proposal namespaces without replacing original multi asset XML',()=>{
    const original=`<a:AssetDeclaration xmlns:a="${namespace}"><!-- keep --><a:FXList id="Existing"/></a:AssetDeclaration>`;
    const proposal=xml(`<FXParticleSystemTemplate id="New"/><FXList id="NewList"/>`);
    const plan=prepareAuthoringPlan(original,proposal,1,'new');assert.ok(plan.text.includes('<!-- keep --><a:FXList id="Existing"/>'));assert.ok(parseXml(plan.text).valid);
    const parsed=parseXml(plan.text);assert.equal(parsed.elements.find(node=>node.attributes.id==='New')?.namespaceUri,namespace);assert.deepEqual(plan.target,{type:'FXList',id:'NewList'});
});
test('new proposals cannot collide with existing identity or duplicate their own identity',()=>{
    assert.throws(()=>prepareAuthoringPlan(xml(`<FXList id="A"/>`),xml(`<FXList id="A"/>`),1,'new'),/身份重复/);
    assert.throws(()=>prepareAuthoringPlan(xml(''),xml(`<FXList id="A"/><FXList id="A"/>`),1,'new'),/重复资产/);
    assert.throws(()=>prepareAuthoringPlan(xml(''),xml(`<GameObject id="A"/>`),1,'new'),/只能包含/);
});
test('local V1 check never invents field, XSD or resource checks and blockers prevent apply',()=>{
    const report=localAuthoringChecks(xml(`<FXList id="A"/>`));assert.deepEqual(report.levels.map(level=>level.status),['UNKNOWN','PASS','SKIPPED','UNKNOWN']);assert.equal(canApplyAuthoring(report),true);
    assert.equal(canApplyAuthoring(localAuthoringChecks('<broken>')),false);assert.equal(localAuthoringChecks(`<NotAssetDeclaration xmlns="${namespace}"/>`).levels[1].status,'FAIL');const v0=report.levels[0];v0.status='FAIL';assert.equal(canApplyAuthoring(report),false);
});
test('AI text parsing supports structured intent and fenced XML without executing data',()=>{
    const source=xml(`<FXList id="A"/>`);assert.deepEqual(parseAssistantResult(JSON.stringify({intent:{unknowns:['shader']},xml:source})),{intent:{unknowns:['shader']},xml:source});assert.equal(parseAssistantResult('```xml\n'+source+'\n```').xml.trim(),source);
});
test('typed text edits escape markup and preserve untouched definitions',()=>{
    const original=xml(`<FXList id="A"><Name>Old</Name></FXList><FXList id="B"/>`);const proposal=xml(`<FXList id="A"><Name>A &amp; B</Name></FXList>`);
    const plan=prepareAuthoringPlan(original,proposal,4,'edit',{type:'FXList',id:'A'});assert.equal(plan.text,original.replace('>Old<','>A &amp; B<'));
});
test('reordered keyed children never apply their values to the wrong child identity',()=>{
    const original=xml(`<FXList id="A"><N id="One" Value="1"/><N id="Two" Value="2"/></FXList>`);const proposal=xml(`<FXList id="A"><N id="Two" Value="9"/><N id="One" Value="1"/></FXList>`);
    assert.throws(()=>prepareAuthoringPlan(original,proposal,1,'edit',{type:'FXList',id:'A'}),/子节点身份/);
});
test('namespaced attributes cannot silently move across rebound prefixes',()=>{
    const original=xml(`<FXList xmlns:x="urn:a" id="A" x:flag="old"/>`);const proposal=xml(`<FXList xmlns:x="urn:b" id="A" x:flag="new"/>`);
    assert.throws(()=>prepareAuthoringPlan(original,proposal,1,'edit',{type:'FXList',id:'A'}),/属性命名空间/);
});

test('new authoring can expand an empty AssetDeclaration without replacing its namespace',()=>{
    const source=`<AssetDeclaration xmlns="${namespace}"/>`;const plan=prepareAuthoringPlan(source,xml('<FXList id="New"/>'),2,'new');const parsed=parseXml(plan.text);assert.equal(parsed.valid,true);assert.equal(parsed.roots[0].namespaceUri,namespace);assert.equal(parsed.elements.find(node=>node.attributes.id==='New')?.namespaceUri,namespace);
});
test('reordered keyframes require manual review so unknown frame data cannot move identities',()=>{
    const original=xml('<FXList id="A"><Curve Frame="0" Value="1" Unknown="keep0"/><Curve Frame="30" Value="2" Unknown="keep30"/></FXList>');const proposed=xml('<FXList id="A"><Curve Frame="30" Value="9"/><Curve Frame="0" Value="1"/></FXList>');assert.throws(()=>prepareAuthoringPlan(original,proposed,1,'edit',{type:'FXList',id:'A'}),/关键帧身份/);
});
