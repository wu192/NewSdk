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

test('document edit then save stays ordered across a version change and queue failures recover', async () => {
    const session = new PreviewSession(1); const order: string[] = [];
    let release: (() => void) | undefined;
    const edited = session.enqueueDocumentAction(async () => {
        await new Promise<void>(resolve => { release = resolve; });
        session.renew(2); order.push('edit-v2');
    });
    const saved = session.enqueueDocumentAction(async () => {
        assert.equal(session.documentVersion, 2); order.push('save-v2');
    });
    await Promise.resolve(); assert.equal(order.length, 0); release!();
    await Promise.all([edited, saved]); assert.deepEqual(order, ['edit-v2', 'save-v2']);
    await assert.rejects(session.enqueueDocumentAction(async () => { throw new Error('disk failure'); }), /disk failure/);
    await session.enqueueDocumentAction(async () => { order.push('retry'); });
    assert.equal(order.at(-1), 'retry');
    session.dispose(); await session.enqueueDocumentAction(async () => { assert.fail('closed sessions cannot write'); });
});
