import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { compileParticle, FxRandom, sampleVolume, type FxNode } from '../webview/fx/runtime';
import { parseXml, type XmlElement } from '../src/fx/xmlDocument';

const node = (name: string, attributes: Record<string, string> = {}, children: FxNode[] = []): FxNode => ({ name, attributes, children });
const plain = (element: XmlElement): FxNode => node(element.localName, element.attributes, element.children.map(plain));
function sdk(name: string): FxNode {
    const filename = path.resolve(__dirname, '../../../SDK/SageXml/FXParticleSystems', name);
    const document = parseXml(fs.readFileSync(filename, 'utf8'), filename);
    assert.ok(document.valid);
    return plain(document.elements.find(element => element.localName === 'FXParticleSystemTemplate')!);
}
const child = (root: FxNode, name: string): FxNode => root.children.find(node => node.name === name)!;
function playable(root: FxNode) {
    const result = compileParticle(root);
    assert.ok(result.config, JSON.stringify(result.issues));
    assert.equal(result.issues.filter(issue => issue.code === 'special-preview').length, 1);
    assert.equal(result.issues.filter(issue => issue.severity === 'error').length, 0);
    return result.config;
}

test('SDK Lightning compiles STREAK with empty modules, two complete waveforms and alpha test', () => {
    const config = playable(sdk('TeslaFireLightning01.xml'));
    assert.equal(config.family, 'STREAK'); assert.equal(config.backend, 'CPU');
    assert.equal(config.drawModule, 'Lightning'); assert.equal(config.volume.type, 'Lightning');
    assert.equal(config.shader, 'ADDITIVE'); assert.equal(config.alphaTest, true);
    assert.deepEqual(config.lightning!.start, [100, 300, 220]);
    assert.deepEqual(config.lightning!.end, [300, 300, 220]);
    assert.equal(config.lightning!.randomSets.length, 2);
    assert.deepEqual(config.lightning!.randomSets[0].startAmplitude, { low: 5, high: 50, type: 'CONSTANT' });
    assert.deepEqual(config.lightning!.randomSets[1].endAmplitudeChange, { low: 0, high: 0, type: 'CONSTANT' });
    const random = new FxRandom(7), calls = random.calls;
    assert.deepEqual(sampleVolume(config.volume, random), [0, 0, 0]);
    assert.equal(random.calls, calls, 'Lightning origin does not consume RNG or double endpoint offsets');
});

test('SDK Trail retains independent Swarm physics and uses a bounded history length', () => {
    const config = playable(sdk('Vacuum_Sparks.xml'));
    assert.equal(config.family, 'TRAIL'); assert.equal(config.drawModule, 'Trail');
    assert.deepEqual(config.swarmPhysics, { attractStrength: 0.01 });
    assert.deepEqual(config.trail, { life: 2, uTile: 1, vTile: 1, uScrollRate: 0, vScrollRate: 0 });
    assert.equal(config.volume.type, 'Cylinder'); assert.equal(config.volume.radiusRate, -1);
});

test('SDK Swarm accepts empty physics/update and preserves its environment texture reference', () => {
    const config = playable(sdk('JapanTankNano.xml'));
    assert.equal(config.family, 'SWARM'); assert.equal(config.drawModule, 'Swarm');
    assert.equal(config.swarmPhysics, undefined);
    assert.deepEqual(config.swarm, { opaqueSpeed: 0, transparentSpeed: 0, speedStretchAmount: 1, attractStrength: 0.05, environmentTexture: 'FXBuzzerRef' });
    assert.equal(config.velocityDamping.low, 1); assert.equal(config.sizeRateDamping.low, 1);
});

test('special alpha-test shaders use the exact SDK enums, including ALPHA_TEST', () => {
    const root = sdk('JapanTankNano.xml'); root.attributes.Shader = 'ALPHA_TEST';
    const config = playable(root);
    assert.equal(config.shader, 'ALPHA'); assert.equal(config.alphaTest, true);
    root.attributes.Shader = 'ALPHA_ALPHA_TEST';
    const result = compileParticle(root);
    assert.equal(result.config, undefined);
    assert.ok(result.issues.some(issue => issue.severity === 'error' && issue.path === '@Shader'));
});

test('special compile rejects invalid history budgets, malformed waveforms and unexpected module fields', () => {
    for (const life of ['0', '-1', '1.5', '601', 'Infinity', '$Frames']) {
        const root = sdk('Vacuum_Sparks.xml'); child(root, 'Draw').children[0].attributes.TrailLife = life;
        const result = compileParticle(root); assert.equal(result.config, undefined, life);
        assert.ok(result.issues.some(issue => issue.severity === 'error' && issue.path.includes('TrailLife')), life);
    }
    for (const mutate of [
        (draw: FxNode) => { draw.children = draw.children.filter(node => node.name !== 'EndPoint'); },
        (draw: FxNode) => { draw.children.pop(); },
        (draw: FxNode) => { draw.children.push(node('RandomSet')); },
        (draw: FxNode) => { child(draw, 'StartPoint').attributes.x = '1000001'; },
        (draw: FxNode) => { child(child(draw, 'RandomSet'), 'StartFrequency').attributes.High = '1000001'; },
        (draw: FxNode) => { child(draw, 'RandomSet').children.push(node('UnknownWaveform')); },
    ]) {
        const root = sdk('TeslaFireLightning01.xml'); mutate(child(root, 'Draw').children[0]);
        assert.equal(compileParticle(root).config, undefined);
    }
    const empty = sdk('JapanTankNano.xml'); child(empty, 'Physics').attributes.Unknown = '1';
    assert.equal(compileParticle(empty).config, undefined);
    const excessive = sdk('JapanTankNano.xml'); child(excessive, 'Draw').children[0].attributes.AttractStrength = '1000001';
    assert.equal(compileParticle(excessive).config, undefined);
});

test('special support does not relax ordinary CPU modules, shaders or unrelated native families', () => {
    const root = sdk('JapanTankNano.xml'); root.attributes.Type = 'PARTICLE';
    assert.equal(compileParticle(root).config, undefined);
    root.attributes.Type = 'DRAWABLE'; assert.equal(compileParticle(root).config, undefined);
    root.attributes.Type = 'SWARM'; child(root, 'Draw').children[0] = node('RenderObject');
    assert.equal(compileParticle(root).config, undefined);
    const lightning = sdk('TeslaFireLightning01.xml'); child(lightning, 'Physics').children.push(node('Default', { Swirly: 'true' }));
    assert.equal(compileParticle(lightning).config, undefined);
});
