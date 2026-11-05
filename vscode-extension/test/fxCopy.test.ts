import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { planAssetCopy } from '../src/fx/copyAsset';
import { applyTextPatches } from '../src/fx/editTransaction';
import { parseXml } from '../src/fx/xmlDocument';

const ns = 'uri:ea.com:eala:asset';
const visible = [{ type: 'Texture', id: 'SharedTexture', namespace: ns }];
const declaration = (text: string): string => `<AssetDeclaration xmlns="${ns}">${text}</AssetDeclaration>`;

test('copying an inherited shared template copies its local parents and changes only the chosen call', () => {
    const source = parseXml(`<a:AssetDeclaration xmlns:a="${ns}" xmlns:custom="urn:custom"><a:FXParticleSystemTemplate id='Base' ParticleTexture='SharedTexture'><!-- keep this --><a:Size Low='1.000' High='2'/><custom:Preserve value='original'/></a:FXParticleSystemTemplate><a:FXParticleSystemTemplate id='Child' inheritFrom='Base'><a:Lifetime Low='30' High='30'/></a:FXParticleSystemTemplate></a:AssetDeclaration>`, 'SDK/B.xml', 3);
    const targetText = declaration(`<Includes><Include type="reference" source="SDKTextures.xml"/></Includes><FXList id="A"><NuggetList><ParticleSystem Particle="Child"/><ParticleSystem Particle="Child"/></NuggetList></FXList>`);
    const target = parseXml(targetText, 'MOD/A.xml', 9);
    const selected = source.elements.find(node => node.attributes.id === 'Child')!;
    const caller = target.elements.find(node => node.localName === 'ParticleSystem')!;
    const plan = planAssetCopy({ source, sourcePath: selected.path, target, reservedIds: ['Base_Copy'], visibleAssets: visible,
        reference: { nodePath: caller.path, attribute: 'Particle', expectedValue: 'Child' } });
    assert.deepEqual(plan.copied, [{sourceId:'Base',id:'Base_Copy2'},{sourceId:'Child',id:'Child_Copy'}]);
    assert.equal(plan.patches.length, 2, 'one insertion and one current-call token patch fit one WorkspaceEdit');
    const resultText = applyTextPatches(targetText, plan.patches);
    const result = parseXml(resultText);
    assert.equal(result.valid, true);
    assert.equal(result.elements.find(node => node.attributes.id === 'Child_Copy')?.attributes.inheritFrom, 'Base_Copy2');
    assert.deepEqual(result.elements.filter(node => node.localName === 'ParticleSystem').map(node => node.attributes.Particle), ['Child_Copy', 'Child']);
    assert.ok(resultText.includes("<!-- keep this --><a:Size Low='1.000' High='2'/><custom:Preserve value='original'/>") );
    assert.ok(resultText.includes('xmlns:custom="urn:custom"'));
    assert.ok(resultText.includes('<Includes><Include type="reference" source="SDKTextures.xml"/></Includes>'));
    assert.equal(result.elements.some(node => node.localName === 'Texture'), false, 'existing visible Texture stays shared');
    assert.equal(source.text.includes('Child_Copy'), false, 'SDK source stays untouched');
});

test('copy rejects missing or ambiguous Texture declarations before producing a patch', () => {
    const source = parseXml(declaration('<FXParticleSystemTemplate id="Effect" ParticleTexture="SharedTexture"/>'), 'SDK/B.xml');
    const target = parseXml(declaration(''), 'MOD/A.xml');
    const request = {source, sourcePath: source.elements.find(node => node.attributes.id === 'Effect')!.path, target};
    assert.throws(() => planAssetCopy({...request,visibleAssets:[]}), /Include.*声明/);
    assert.throws(() => planAssetCopy({...request,visibleAssets:[...visible,...visible]}), /唯一可见/);
});

test('copying a Swarm requires its optional environment declaration to remain uniquely visible in the MOD', () => {
    const source = parseXml(declaration('<FXParticleSystemTemplate id="Swarm" Type="SWARM" ParticleTexture="SharedTexture"><Draw><Swarm EnvironmentTexture="Environment"/></Draw></FXParticleSystemTemplate>'),'SDK/Swarm.xml');
    const target = parseXml(declaration(''),'MOD/A.xml');
    const request = {source,sourcePath:source.elements.find(node=>node.attributes.id==='Swarm')!.path,target};
    const environment = {type:'Texture',id:'Environment',namespace:ns};
    assert.throws(()=>planAssetCopy({...request,visibleAssets:visible}),/Texture:Environment/);
    assert.throws(()=>planAssetCopy({...request,visibleAssets:[...visible,environment,environment]}),/唯一可见.*Texture:Environment/);
    const plan = planAssetCopy({...request,visibleAssets:[...visible,environment]});
    assert.deepEqual(plan.dependencies,[...visible,environment]);
    const updated = parseXml(applyTextPatches(target.text,plan.patches));
    assert.equal(updated.valid,true);
    assert.equal(updated.elements.find(node=>node.localName==='Swarm')?.attributes.EnvironmentTexture,'Environment');
    assert.equal(updated.elements.some(node=>node.localName==='Texture'),false,'shared declarations remain explicit dependencies, not copied game assets');
    assert.equal(source.text.includes('Swarm_Copy'),false);
});

test('copy refuses unresolved inheritance, cycles and unsupported dependent systems', () => {
    const target = parseXml(declaration(''), 'MOD/A.xml');
    for (const [xml, reason] of [
        ['<FXParticleSystemTemplate id="Child" inheritFrom="Elsewhere"/>', /同一来源文档/],
        ['<FXParticleSystemTemplate id="Child" inheritFrom="Parent"/><FXParticleSystemTemplate id="Parent" inheritFrom="Child"/>', /继承循环/],
        ['<FXParticleSystemTemplate id="Child" PerParticleAttachedSystem="Other"/>', /附属资源复制/],
        ['<FXParticleSystemTemplate id="Child"><Event><FXList Name="Other"/></Event></FXParticleSystemTemplate>', /Event/],
    ] as const) {
        const source = parseXml(declaration(xml), 'SDK/B.xml');
        assert.throws(() => planAssetCopy({source,sourcePath:source.elements.find(node => node.attributes.id === 'Child')!.path,target,visibleAssets:[]}), reason);
    }
});

test('copy expands an empty destination safely and blocks cross-file macro scope changes', () => {
    const source = parseXml(declaration('<FXParticleSystemTemplate id="Effect"><Size Low="=LocalSize" High="2"/></FXParticleSystemTemplate>'), 'SDK/B.xml');
    const empty = parseXml(`<AssetDeclaration xmlns="${ns}"/>`, 'MOD/A.xml');
    assert.throws(() => planAssetCopy({source,sourcePath:source.elements[1].path,target:empty,visibleAssets:[]}), /宏范围/);
    const simple = parseXml(declaration('<FXParticleSystemTemplate id="Effect"><Size Low="1" High="2"/></FXParticleSystemTemplate>'), 'SDK/B.xml');
    const plan = planAssetCopy({source:simple,sourcePath:simple.elements[1].path,target:empty,visibleAssets:[]});
    const result = parseXml(applyTextPatches(empty.text,plan.patches));
    assert.equal(result.valid,true);assert.equal(result.elements.find(node=>node.attributes.id==='Effect_Copy')?.localName,'FXParticleSystemTemplate');
});
