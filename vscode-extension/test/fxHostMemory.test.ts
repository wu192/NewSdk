import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import { descendantIds, fxMemoryReport } from './host/fxMemory';

test('host memory aggregation excludes CI ancestors, other instances and its sampler subtree', () => {
    const rows = [{id:1,parent:0},{id:2,parent:1},{id:3,parent:2},{id:4,parent:3},{id:5,parent:4},{id:6,parent:2},{id:7,parent:3},{id:8,parent:7},{id:9,parent:1}];
    assert.deepEqual([...descendantIds(rows,3,7)].sort((a,b)=>a-b),[3,4,5]);
});

test('memory report preserves negative observed deltas and unavailable measurements without private identities', () => {
    const base={available:true,timestamp:'baseline',durationMs:2,residentBytes:100,processCount:3,measuredProcessCount:3};
    const report=fxMemoryReport(base,{...base,residentBytes:80},{available:false,timestamp:'closed',durationMs:1,reason:'unavailable'});
    assert.equal(report.deltaBytesFromBaseline.opened,-20);assert.equal(report.deltaBytesFromBaseline.closed,undefined);
    assert.match(report.limitations,/shared pages/);assert.match(report.limitations,/not unique/);assert.ok(Buffer.byteLength(JSON.stringify(report))<128*1024);
    assert.equal(Object.hasOwn(report.samples.baselineClosed,'pid'),false);
});
