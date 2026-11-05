import { strict as assert } from 'node:assert';
import { test } from 'node:test';
import * as path from 'node:path';

// Exercise the exact presentation helpers loaded by the webview, without a DOM.
const presentation = require(path.resolve(__dirname, '../../media/workbench.js')) as {
  statusLabel(status: string, operation?: string): string;
  phaseLabel(phase: string | undefined, operation?: string, status?: string): string;
  contentStatus(task: { id: string }, model: unknown): string;
  resultText(model: unknown): string;
  contentLabel(value: string): string;
  optionRows(model: unknown): Array<{key: string; value: string; source: string}>;
  metricRows(model: unknown): Array<{key: string; value: string}>;
};

test('a successful plan never presents its input streams as compiled', () => {
  const model = {
    operation: 'plan', status: 'succeeded', active: false,
    result: { status: 'succeeded', reason: 'plan confirmed', events: [], artifacts: [] },
  };
  assert.equal(presentation.statusLabel(model.status, model.operation), '计划解析完成');
  assert.equal(presentation.contentStatus({ id: 'base' }, model), '计划编译');
  assert.doesNotMatch(presentation.resultText(model), /编译成功/);
});

test('doctor success describes an environment check', () => {
  const model = { operation: 'doctor', status: 'succeeded', active: false };
  assert.equal(presentation.statusLabel(model.status, model.operation), '环境检查通过');
  assert.equal(presentation.contentStatus({ id: 'base' }, model), '未编译');
  assert.equal(presentation.resultText(model), '环境检查通过');
});

test('interrupted and cancelled streams do not display completion', () => {
  const events = [
    { eventType: 'StreamStarted', streamId: 'base' },
    { eventType: 'StreamCompleted', streamId: 'base', level: 'error', payload: { status: 'cancelled' } },
  ];
  assert.equal(presentation.contentStatus({ id: 'base' }, {
    operation: 'build', active: false, result: { status: 'cancelled', events },
  }), '已取消');
  assert.equal(presentation.contentStatus({ id: 'base' }, {
    operation: 'build', active: false, result: { status: 'interrupted', events: events.slice(0, 1) },
  }), '未完成');
});

test('compiler stage names are translated with unknown phases preserved', () => {
  assert.equal(presentation.phaseLabel('compile', 'build', 'running'), '编译数据流');
  assert.equal(presentation.phaseLabel('package', 'build', 'running'), '打包');
  assert.equal(presentation.phaseLabel('publish', 'build', 'running'), '发布');
  assert.equal(presentation.phaseLabel('future-stage', 'build', 'running'), 'future-stage');
});

test('workbench presents compiler configuration origins and observed stale results', () => {
  const model = { operation: 'build', result: { status: 'succeeded', reason: 'confirmed', inputState: 'inputs-changed',
    effectiveOptions: { 'package-format': { value: 'loose', source: 'project' }, jobs: { value: 'auto', source: 'default' } },
    report: { cache: { status: 'Used', hits: 5, nested: { ignored: true } } } } };
  assert.match(presentation.resultText(model), /上一保存版本/);
  assert.deepEqual(presentation.optionRows(model), [
    { key: 'jobs', value: 'auto', source: '编译器默认' }, { key: 'package-format', value: 'loose', source: '项目预设' },
  ]);
  assert.deepEqual(presentation.metricRows(model), [{ key: '缓存 · status', value: 'Used' }, { key: '缓存 · hits', value: '5' }]);
  assert.equal(presentation.contentLabel('maps'), 'Global');
  assert.deepEqual(presentation.optionRows({}), []);
});
