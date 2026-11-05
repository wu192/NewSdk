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

test('synthetic source motion survives persisted scene round trips with independent points', () => {
    for (const playback of ['once', 'loop', 'pingPong'] as const) {
        const sourceMotion = {playback, points:[{frame:0, offset:[0,0,0]}, {frame:30, offset:[60,-30,6]}, {frame:60, offset:[0,0,0]}]};
        const input = {...defaultScene, sourceMotion};
        const parsed = parseFxScene(input); assert.ok(parsed?.sourceMotion);
        assert.deepEqual(parsed.sourceMotion, sourceMotion);
        const restored = parseFxScene(JSON.parse(JSON.stringify({scene:parsed})).scene);
        assert.deepEqual(restored, parsed, 'workspace state.scene must retain the complete motion');
        parsed.sourceMotion.points[1].offset[0] = 999;
        parsed.sourceMotion.points.push({frame:90, offset:[0,0,0]});
        assert.equal(input.sourceMotion.points[1].offset[0], 60);
        assert.equal(input.sourceMotion.points.length, 3);
        assert.equal(restored!.sourceMotion!.points[1].offset[0], 60);
    }
    assert.deepEqual(parseFxScene(defaultScene), defaultScene, 'stationary scenes remain valid');
    const points = Array.from({length:64}, (_, frame) => ({frame, offset:[frame,0,0]}));
    assert.ok(parseFxScene({...defaultScene, sourceMotion:{playback:'once', points}}));
    assert.ok(parseFxScene({...defaultScene, sourceMotion:{playback:'once', points:[{frame:0, offset:[0,0,0]}, {frame:18000, offset:[1e6,-1e6,1e6]}]}}));
});

test('host rejects malformed synthetic source motion instead of silently dropping it', () => {
    const start = {frame:0, offset:[0,0,0]}, end = {frame:30, offset:[10,0,0]};
    const once = {playback:'once', points:[start,end]};
    const invalid: unknown[] = [
        null, false, [], {}, {...once, playback:'repeat'}, {...once, points:[]}, {...once, points:[start]},
        {...once, points:Array.from({length:65}, (_, frame) => ({frame, offset:[0,0,0]}))},
        {...once, points:[{...start, frame:1}, end]}, {...once, points:[{...start, offset:[1,0,0]}, end]},
        {...once, points:[start, {...end, frame:0}]}, {...once, points:[start, end, {...end, frame:29}]},
        {...once, points:[start, {...end, frame:0.5}]}, {...once, points:[start, {...end, frame:18001}]},
        {...once, points:[start, {...end, frame:NaN}]}, {...once, points:[start, {...end, frame:'30'}]},
        {...once, points:[start, {...end, offset:[Infinity,0,0]}]}, {...once, points:[start, {...end, offset:[NaN,0,0]}]},
        {...once, points:[start, {...end, offset:[1000001,0,0]}]}, {...once, points:[start, {...end, offset:['10',0,0]}]},
        {...once, points:[start, {...end, offset:[0,0]}]}, {...once, points:[start, {...end, offset:[0,0,0,0]}]},
        {...once, points:[start, null]}, {...once, points:[start, []]}, {...once, playback:'loop'},
    ];
    for (const sourceMotion of invalid) assert.equal(parseFxScene({...defaultScene, sourceMotion}), undefined, JSON.stringify(sourceMotion));
});
