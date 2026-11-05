import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { defaultScene } from '../src/fx/previewProtocol';
import { parseFxScene } from '../src/fx/previewSession';

test('object context survives host validation and does not alias persisted arrays', () => {
    const input = {...defaultScene, dispatch:'object', sourceRotation:[0,0,1], sourceModelConditions:['MOVING','DAMAGED'], targetModelConditions:['REALLYDAMAGED'], targetObject:false, weather:'SNOWY', sourceStealthState:2, sourceDisabledTypesThisFrame:['EMP'], sourceVisibility:1, positionVisibility:0};
    const parsed = parseFxScene(input); assert.ok(parsed);
    assert.deepEqual(parsed, input); parsed.sourceModelConditions!.push('ATTACKING');
    assert.equal(input.sourceModelConditions.length, 2);
    assert.equal(parseFxScene({...input, sourceRotation:[NaN,0,0]}), undefined);
    assert.equal(parseFxScene({...input, sourceModelConditions:['<xml>']}), undefined);
    assert.equal(parseFxScene({...input, sourceModelConditions:Array(129).fill('MOVING')}), undefined);
    assert.equal(parseFxScene({...input, dispatch:'execute-command'}), undefined);
    assert.equal(parseFxScene({...input, seed:-1}), undefined);
});
