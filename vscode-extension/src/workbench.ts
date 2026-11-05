import * as vscode from 'vscode';
import { randomBytes } from 'node:crypto';
import type { UiModel } from './sidebar';

const commands = new Set([
  'newsdk.build', 'newsdk.stop', 'newsdk.configure', 'newsdk.plan', 'newsdk.doctor',
  'newsdk.openLog', 'newsdk.openEvents', 'newsdk.openReport', 'newsdk.openOutput',
  'newsdk.selectProject', 'newsdk.setVersion', 'newsdk.openArtifact',
]);

export class NewSdkWorkbench implements vscode.Disposable {
  private panel?: vscode.WebviewPanel;
  private model: UiModel = { active: false, status: 'idle' };
  constructor(private readonly extensionUri: vscode.Uri,
    private readonly execute: (command: string, value?: string | number) => void | Promise<void>) {}

  show(): void {
    if (this.panel) { this.panel.reveal(); this.update(this.model); return; }
    const panel = vscode.window.createWebviewPanel('newsdk.workbench', 'NewSdk MOD 工作台', vscode.ViewColumn.One, {
      enableScripts: true,
      localResourceRoots: [vscode.Uri.joinPath(this.extensionUri, 'media')],
      retainContextWhenHidden: true,
    });
    this.panel = panel;
    panel.webview.html = this.html(panel.webview);
    const messages = panel.webview.onDidReceiveMessage((message: unknown) => {
      if (!message || typeof message !== 'object') return;
      const data = message as Record<string, unknown>;
      if (data.type === 'ready') { this.update(this.model); return; }
      if (data.type !== 'command' || typeof data.command !== 'string' || !commands.has(data.command)) return;
      let value: string | number | undefined;
      if (data.command === 'newsdk.openArtifact') {
        if (!Number.isSafeInteger(data.value) || (data.value as number) < 0 ||
          (data.value as number) >= (this.model.result?.artifacts.length ?? 0)) return;
        value = data.value as number;
      } else if (data.command === 'newsdk.setVersion') {
        if (typeof data.value !== 'string' || data.value.length > 200) return;
        value = data.value;
      }
      if (this.model.active && ['newsdk.build', 'newsdk.configure', 'newsdk.plan', 'newsdk.doctor',
        'newsdk.selectProject', 'newsdk.setVersion'].includes(data.command)) return;
      void Promise.resolve(this.execute(data.command, value)).catch((error: unknown) =>
        vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error)));
    });
    panel.onDidDispose(() => { messages.dispose(); this.panel = undefined; });
  }

  update(model: UiModel): void {
    this.model = model;
    if (this.panel) void this.panel.webview.postMessage({ type: 'state', model });
  }
  dispose(): void { this.panel?.dispose(); }

  private html(webview: vscode.Webview): string {
    const nonce = randomBytes(24).toString('base64');
    const script = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'workbench.js'));
    const style = webview.asWebviewUri(vscode.Uri.joinPath(this.extensionUri, 'media', 'workbench.css'));
    return `<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width, initial-scale=1.0">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}'; img-src ${webview.cspSource} data:;">
<link rel="stylesheet" href="${style}"><title>NewSdk MOD 工作台</title></head>
<body><main>
  <header><div><div class="eyebrow">NewSdk Mod Tools · P0</div><h1 id="projectName">MOD 工作台</h1><p id="projectRoot" class="path">选择 MOD 文件夹以开始。</p></div><button data-command="newsdk.selectProject" class="secondary" data-idle>选择项目</button></header>
  <section class="toolbar" aria-label="编译操作"><label>MOD 版本 <input id="version" type="text" maxlength="200" placeholder="例如 1.0" autocomplete="off"></label><button id="saveVersion" class="secondary" data-idle>保存版本</button><button data-command="newsdk.build" data-idle>编译 MOD</button><button data-command="newsdk.stop" id="stop" class="secondary">停止编译</button><button data-command="newsdk.configure" class="secondary" data-idle>配置环境</button></section>
  <section class="card" aria-labelledby="statusHeading"><div class="section-title"><h2 id="statusHeading">当前状态</h2><strong id="status" role="status">就绪</strong></div><p id="message"></p><div class="metrics"><div><span>当前阶段</span><strong id="phase">—</strong></div><div><span>正在处理的数据流</span><strong id="stream">—</strong></div><div><span>已完成 / 计划数据流</span><strong id="streams">—</strong></div><div><span>已用时间</span><strong id="elapsed">—</strong></div></div></section>
  <div class="columns"><section class="card"><div class="section-title"><h2>编译内容</h2><button data-command="newsdk.plan" class="secondary small" data-idle>查看计划</button></div><p id="planNote" class="muted">由编译器 plan 确认实际输入。</p><div id="contents" class="rows"></div></section>
  <section class="card"><div class="section-title"><h2>环境配置</h2><button data-command="newsdk.doctor" class="secondary small" data-idle>环境检查</button></div><dl id="configuration"></dl></section></div>
  <section class="card"><div class="section-title"><h2>本轮问题 <span id="issueCount" class="muted">0</span></h2></div><p class="muted">保留诊断代码和原始消息；有真实源码位置的诊断同时显示在 Problems 面板。</p><div id="issues" aria-live="polite"></div></section>
  <section class="card"><div class="section-title"><h2>实际生效配置</h2></div><p class="muted">由本轮编译器事件或报告确认，包含每项配置的来源。</p><div id="effectiveOptions" class="rows"></div><div id="compilerMetrics" class="rows"></div></section>
  <section class="card"><div class="section-title"><h2>本轮结果与产物</h2><button data-command="newsdk.openOutput" class="secondary small">打开输出目录</button></div><p id="result">尚无本轮构建结果。</p><div id="artifacts" class="rows"></div><nav aria-label="编译日志与报告"><button data-command="newsdk.openLog" class="secondary">build.log</button><button data-command="newsdk.openEvents" class="secondary">完整 JSONL</button><button data-command="newsdk.openReport" class="secondary">构建报告</button></nav></section>
</main><script nonce="${nonce}" src="${script}"></script></body></html>`;
  }
}
