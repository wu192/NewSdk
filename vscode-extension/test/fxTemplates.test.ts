import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { createFxTemplate, fxPresets } from '../src/fx/templates';
import { parseXml, toFxNode } from '../src/fx/xmlDocument';
import { compileParticle } from '../webview/fx/runtime';

test('all new-effect presets have schema-ordered required modules and compile as ordinary GPU', () => {
    for (const preset of fxPresets) {
        const text = `<AssetDeclaration xmlns="uri:ea.com:eala:asset">${createFxTemplate(preset, 'AuthorEffect', 'ChosenTexture')}</AssetDeclaration>`;
        const document = parseXml(text);
        assert.equal(document.valid, true, `${preset}: valid XML`);
        const asset = document.roots[0].children[0];
        assert.equal(asset.attributes.ParticleTexture, 'ChosenTexture');
        assert.deepEqual(asset.children.slice(-5).map(node => node.localName), ['Physics', 'Draw', 'Volume', 'Velocity', 'Update']);
        const result = compileParticle(toFxNode(asset));
        assert.ok(result.config, `${preset}: ${JSON.stringify(result.issues)}`);
    }
});

test('new effects preserve qualified namespaces and leave absent textures explicit', () => {
    const text = `<a:AssetDeclaration xmlns:a="uri:ea.com:eala:asset">${createFxTemplate('闪光', 'NoTextureYet', undefined, 'a:')}</a:AssetDeclaration>`;
    const document = parseXml(text);
    assert.equal(document.valid, true);
    assert.equal(document.roots[0].children[0].attributes.ParticleTexture, undefined);
    assert.ok(document.elements.every(node => node.namespaceUri === 'uri:ea.com:eala:asset'));
});
