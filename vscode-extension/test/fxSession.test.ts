import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { ActivePreview, PreviewSession } from '../src/fx/previewSession';

test('a document generation cancels pending work and rejects stale mutation envelopes', () => {
    const session = new PreviewSession(7);
    const first = session.renew();
    const second = session.renew(8);
    assert.equal(first.signal.aborted, true);
    assert.equal(session.current(first.requestId), false);
    assert.equal(session.current(second.requestId), true);
    assert.equal(session.accepts({ sessionId: session.id, documentVersion: 7, requestId: 2, type: 'edit' }), false);
    assert.equal(session.accepts({ sessionId: session.id, documentVersion: 8, requestId: 2, type: 'edit' }), true);
    assert.equal(session.acceptMutation(2), true);
    assert.equal(session.acceptMutation(2), false);
    assert.equal(session.acceptMutation(1), false);
    session.dispose();
    assert.equal(second.signal.aborted, true);
});

test('opening a second canvas suspends the first and releases active callbacks once', () => {
    const active = new ActivePreview(); const a = new PreviewSession(1); const b = new PreviewSession(1);
    let stoppedA = 0; let stoppedB = 0;
    active.activate(a, () => stoppedA++);
    active.activate(b, () => stoppedB++);
    assert.equal(stoppedA, 1); assert.equal(a.visible, false); assert.equal(a.signal.aborted, true);
    assert.equal(b.visible, true);
    active.release(b);
    assert.equal(stoppedB, 1); assert.equal(b.signal.aborted, true);
    active.dispose(); assert.equal(stoppedB, 1);
    b.dispose(); active.activate(b, () => stoppedB++); assert.equal(b.visible, false);
});
