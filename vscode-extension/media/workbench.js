const NewSdkPresentation = (() => {
  const labels = { idle: '就绪', preparing: '准备中', running: '编译中', building: '编译中', checking: '检查环境', planning: '读取计划', succeeded: '编译成功', failed: '失败', cancelled: '已取消', interrupted: '已中断 / 结果不完整', stopping: '正在停止' };
  const phases = { preparing: '准备', plan: '解析编译计划', planning: '解析编译计划', compile: '编译数据流', package: '打包', publish: '发布', succeeded: '完成', failed: '失败', cancelled: '已取消', interrupted: '已中断' };
  function statusLabel(status, operation) {
    const specific = operation === 'doctor' ? { running: '检查环境', succeeded: '环境检查通过', failed: '环境检查失败' } :
      operation === 'plan' ? { running: '读取计划', succeeded: '计划解析完成', failed: '计划解析失败' } : {};
    return specific[status] ?? labels[status] ?? status;
  }
  function phaseLabel(phase, operation, status) {
    if (operation === 'doctor') return statusLabel(status, operation);
    if (operation === 'plan' && phase === 'succeeded') return '计划解析完成';
    return phases[phase] ?? phase ?? '—';
  }
  function contentStatus(task, model) {
    if (model.operation === 'plan') return '计划编译';
    if (model.operation === 'doctor') return '未编译';
    const progress = model.progress ?? model.result;
    const events = (progress?.events ?? []).filter(event => event.streamId === task.id);
    const finished = events.find(event => event.eventType === 'StreamCompleted');
    if (finished) {
      const status = finished.payload?.status ?? finished.payload?.Status;
      if (status === 'cancelled') return '已取消';
      if (status === 'failed' || finished.level === 'error') return '失败';
      if (status === 'succeeded') return '完成';
    }
    if (events.some(event => event.eventType === 'StreamStarted') || progress?.activeStream === task.id) return model.active ? '进行中' : '未完成';
    return '待开始';
  }
  function resultText(model) {
    if (model.operation === 'doctor') return model.message ?? statusLabel(model.status, model.operation);
    if (model.operation === 'plan') return model.result ? `${statusLabel(model.result.status, 'plan')} · ${model.result.reason}` : model.active ? '正在解析编译计划。' : '尚无本轮计划结果。';
    const stale = model.result?.inputState === 'inputs-changed' ? ' · 编译期间输入文件已变化，结果对应上一保存版本。'
      : model.result?.inputState === 'previous-saved' ? ' · 结果对应上一保存版本，未包含当前未保存修改。' : '';
    return model.result ? `${statusLabel(model.result.status, 'build')} · ${model.result.reason}${stale}` : model.active ? '构建进行中，等待本轮报告确认结果。' : '尚无本轮构建结果。';
  }
  function contentLabel(value) {
    return { base: 'Base', apt: 'APT', maps: 'Global', 'global-metadata': 'Global' }[String(value).toLowerCase()] ?? value;
  }
  function optionRows(model) {
    const options = model.progress?.effectiveOptions ?? model.result?.effectiveOptions ?? {};
    const origins = { project: '项目预设', commandLine: '显式覆盖', default: '编译器默认', applicationConfig: '编译器配置' };
    return Object.entries(options).sort(([a], [b]) => a.localeCompare(b)).map(([key, option]) => ({ key, value: option.value, source: origins[option.source] ?? option.source }));
  }
  function metricRows(model) {
    const report = model.result?.report;
    const result = [];
    for (const [group, label] of [['cache', '缓存'], ['performance', '性能']]) {
      const data = report?.[group];
      if (!data || typeof data !== 'object' || Array.isArray(data)) continue;
      for (const [key, value] of Object.entries(data).slice(0, 32)) {
        if (typeof value === 'string' || typeof value === 'boolean' || typeof value === 'number') result.push({ key: `${label} · ${key}`, value: String(value) });
      }
    }
    return result;
  }
  return { statusLabel, phaseLabel, contentStatus, resultText, contentLabel, optionRows, metricRows };
})();
if (typeof module !== 'undefined' && module.exports) module.exports = NewSdkPresentation;

if (typeof document !== 'undefined') (() => {
  const api = acquireVsCodeApi();
  const $ = id => document.getElementById(id);
  let current = { active: false, status: 'idle' };
  let receivedAt = Date.now();
  function command(name, value) { api.postMessage({ type: 'command', command: name, value }); }
  document.querySelectorAll('[data-command]').forEach(button => button.addEventListener('click', () => command(button.dataset.command)));
  $('saveVersion').addEventListener('click', () => command('newsdk.setVersion', $('version').value.trim()));
  $('version').addEventListener('keydown', event => { if (event.key === 'Enter') command('newsdk.setVersion', $('version').value.trim()); });
  function text(id, value) { $(id).textContent = value ?? '—'; }
  function node(tag, content, className) { const element = document.createElement(tag); if (content !== undefined) element.textContent = content; if (className) element.className = className; return element; }
  function row(title, status, detail) {
    const element = node('div', undefined, 'row');
    const heading = node('div', undefined, 'row-title');
    heading.append(node('strong', title), node('span', status));
    element.append(heading);
    if (detail) element.append(node('p', detail, 'path'));
    return element;
  }
  function formatElapsed(milliseconds) {
    const seconds = Math.floor(Math.max(0, milliseconds) / 1000);
    return `${Math.floor(seconds / 60)} 分 ${seconds % 60} 秒`;
  }
  function elapsed() {
    const progress = current.progress ?? current.result;
    const baseline = Number.isFinite(current.elapsedMs) ? current.elapsedMs : progress?.elapsedMs;
    text('elapsed', Number.isFinite(baseline) ? formatElapsed(baseline + (current.active ? Date.now() - receivedAt : 0)) : '—');
  }
  function render(model) {
    current = model;
    receivedAt = Date.now();
    const progress = model.progress ?? model.result;
    const plan = model.plan ?? progress?.plan;
    text('projectName', model.project?.name ?? 'MOD 工作台');
    text('projectRoot', model.project?.root ?? '选择 MOD 文件夹以开始。');
    text('status', NewSdkPresentation.statusLabel(model.status, model.operation));
    $('status').className = model.status === 'succeeded' ? 'success' : ['failed', 'interrupted'].includes(model.status) ? 'failure' : '';
    text('message', model.message ?? '');
    if (document.activeElement !== $('version')) $('version').value = model.config?.modVersion ?? '';
    $('version').disabled = model.active;
    document.querySelectorAll('[data-idle]').forEach(button => { button.disabled = model.active; });
    $('stop').disabled = !model.active;
    text('phase', NewSdkPresentation.phaseLabel(progress?.phase, model.operation, model.status));
    text('stream', progress?.activeStream);
    text('streams', model.operation === 'build' && progress ? `${progress.completedStreams} / ${progress.totalStreams}` : '—');
    elapsed();
    const config = $('configuration'); config.replaceChildren();
    for (const [label, value] of [['编译器', model.config?.compilerPath], ['SDK', model.config?.sdkRoot], ['输出目录', model.config?.outputDirectory]]) config.append(node('dt', label), node('dd', value || '未配置'));
    const contents = $('contents'); contents.replaceChildren();
    if (plan) {
      text('planNote', plan.kind ? `编译器计划：${plan.kind}` : '以下内容由编译器 plan 确认。');
      for (const task of plan.tasks) contents.append(row(NewSdkPresentation.contentLabel(task.content), NewSdkPresentation.contentStatus(task, model), task.inputPath));
      for (const task of plan.skipped) contents.append(row(NewSdkPresentation.contentLabel(task.content), '跳过', task.reason));
      if (!plan.tasks.length && !plan.skipped.length) contents.append(node('p', '计划没有可编译输入。', 'muted'));
    } else {
      text('planNote', '目录检测仅供参考，实际输入以编译器 plan 为准。');
      for (const [key, label] of [['base', '基础数据'], ['apt', 'APT UI'], ['globalMetadata', '全局元数据'], ['additional', 'Additional']]) contents.append(row(label, model.project?.contents?.[key] ? '检测到输入，待计划确认' : '未检测到输入'));
    }
    const diagnostics = progress?.diagnostics ?? [];
    const errors = diagnostics.filter(item => String(item.level).toLowerCase() === 'error').length;
    const warnings = diagnostics.filter(item => ['warning', 'warn'].includes(String(item.level).toLowerCase())).length;
    text('issueCount', `${errors} 错误 · ${warnings} 警告`);
    const issues = $('issues'); issues.replaceChildren();
    if (!diagnostics.length) issues.append(node('p', '本轮尚未收到诊断。', 'muted'));
    for (const item of diagnostics) {
      const element = node('div', undefined, `issue ${String(item.level).toLowerCase() === 'error' ? 'error' : ''}`);
      element.append(node('strong', `${item.level} · ${item.code}`), node('p', item.message, 'raw'));
      if (item.file) element.append(node('p', `${item.file}${Number.isSafeInteger(item.line) && item.line > 0 ? `:${item.line}${item.column ? `:${item.column}` : ''}` : ''}`, 'path'));
      if (item.assetId || item.assetType) element.append(node('p', `${item.assetType ?? ''}:${item.assetId ?? ''}`, 'path'));
      if (item.streamId || item.stageId) element.append(node('p', [item.streamId, item.stageId].filter(Boolean).join(' · '), 'muted'));
      issues.append(element);
    }
    const result = model.result;
    const options = $('effectiveOptions'); options.replaceChildren();
    const optionRows = NewSdkPresentation.optionRows(model);
    for (const option of optionRows) options.append(row(option.key, option.source, option.value));
    if (!optionRows.length) options.append(node('p', '等待本轮编译器确认生效配置。', 'muted'));
    const metrics = $('compilerMetrics'); metrics.replaceChildren();
    for (const metric of NewSdkPresentation.metricRows(model)) metrics.append(row(metric.key, metric.value));
    text('result', NewSdkPresentation.resultText(model));
    const artifacts = $('artifacts'); artifacts.replaceChildren();
    for (const [index, artifact] of (result?.artifacts ?? []).entries()) {
      const element = row(artifact.kind, Number.isFinite(artifact.size) ? `${artifact.size.toLocaleString()} 字节` : '', artifact.path);
      const reveal = node('button', '打开文件位置', 'secondary small');
      reveal.addEventListener('click', () => command('newsdk.openArtifact', index)); element.append(reveal); artifacts.append(element);
    }
    if (result && !result.artifacts.length) artifacts.append(node('p', model.operation === 'plan' ? '编译计划不生成构建产物。' : '本轮报告未确认可展示的产物。', 'muted'));
  }
  window.addEventListener('message', event => { if (event.data?.type === 'state' && event.data.model) render(event.data.model); });
  setInterval(elapsed, 1000);
  api.postMessage({ type: 'ready' });
})();
