import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { BuildVersionTracker } from '../src/fx/buildVersion';
test('saved build distinguishes memory-only changes from changed disk inputs', () => {
    const tracker = new BuildVersionTracker();
    tracker.memory('a.xml', 2); tracker.begin();
    assert.equal(tracker.result(), 'current');
    tracker.memory('a.xml', 3);
    assert.equal(tracker.result(), 'previous-saved');
    tracker.disk();
    assert.equal(tracker.result(), 'inputs-changed');
    tracker.begin(); assert.equal(tracker.result(), 'current');
});
