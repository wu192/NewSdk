import * as vscode from 'vscode';
import type { BuildPlan, BuildProgress, BuildResult } from './buildProtocol';

export interface UiModel {
  project?: { name: string; root: string; contents?: { base: boolean; apt: boolean; globalMetadata: boolean; additional: boolean } };
  config?: { compilerPath?: string; sdkRoot?: string; outputDirectory?: string; modVersion?: string };
  active: boolean;
  status: string;
  operation?: 'doctor' | 'plan' | 'build';
  elapsedMs?: number;
  progress?: BuildProgress;
  result?: BuildResult;
  plan?: BuildPlan;
  message?: string;
}

const statusNames: Record<string, string> = {
  idle: '就绪', preparing: '准备中', running: '编译中', building: '编译中',
  checking: '检查环境', planning: '读取计划', succeeded: '编译成功', failed: '失败',
  cancelled: '已取消', interrupted: '已中断 / 结果不完整', stopping: '正在停止',
};

function contentLabel(value: string): string {
  return ({ base: 'Base', apt: 'APT', maps: 'Global', 'global-metadata': 'Global' } as Record<string, string>)[value.toLowerCase()] ?? value;
}

export function statusLabel(status: string, operation?: UiModel['operation']): string {
  if (operation === 'doctor') {
    if (status === 'succeeded') return '环境检查通过';
    if (status === 'running') return '检查环境';
    if (status === 'failed') return '环境检查失败';
  }
  if (operation === 'plan') {
    if (status === 'succeeded') return '计划解析完成';
    if (status === 'running') return '读取计划';
    if (status === 'failed') return '计划解析失败';
  }
  return statusNames[status] ?? status;
}

class SidebarItem extends vscode.TreeItem {
  constructor(label: string, readonly children: SidebarItem[] = [], command?: string,
    description?: string, icon?: string, args?: unknown[]) {
    super(label, children.length ? vscode.TreeItemCollapsibleState.Expanded : vscode.TreeItemCollapsibleState.None);
    this.description = description;
    this.tooltip = description ? `${label}\n${description}` : label;
    if (command) this.command = { command, title: label, arguments: args };
    if (icon) this.iconPath = new vscode.ThemeIcon(icon);
  }
}

export class NewSdkSidebar implements vscode.TreeDataProvider<SidebarItem>, vscode.Disposable {
  private readonly changed = new vscode.EventEmitter<SidebarItem | undefined>();
  readonly onDidChangeTreeData = this.changed.event;
  private model: UiModel = { active: false, status: 'idle' };

  update(model: UiModel): void { this.model = model; this.changed.fire(undefined); }
  getTreeItem(item: SidebarItem): vscode.TreeItem { return item; }
  getChildren(item?: SidebarItem): SidebarItem[] {
    if (item) return item.children;
    const m = this.model;
    const config = m.config;
    const plan = m.plan ?? m.progress?.plan;
    const content: SidebarItem[] = plan ? [
      ...plan.tasks.map(task => new SidebarItem(contentLabel(task.content), [], 'newsdk.openWorkbench', task.inputPath, 'file-code')),
      ...plan.skipped.map(task => new SidebarItem(contentLabel(task.content), [], 'newsdk.openWorkbench', `跳过：${task.reason}`, 'circle-slash')),
    ] : [
      ...(['base', 'apt', 'globalMetadata', 'additional'] as const).map(key => new SidebarItem(
        { base: '基础数据', apt: 'APT UI', globalMetadata: '全局元数据', additional: 'Additional' }[key], [],
        'newsdk.plan', m.project?.contents?.[key] ? '检测到输入，待计划确认' : '未检测到输入', 'file-code')),
      new SidebarItem('查看编译计划以确认实际输入', [], 'newsdk.plan', undefined, 'list-tree'),
    ];
    const diagnostics = m.progress?.diagnostics ?? m.result?.diagnostics ?? [];
    const issues = diagnostics.map(d => new SidebarItem(`${d.code}: ${d.message}`, [], 'newsdk.openWorkbench', d.level,
      d.level.toLowerCase() === 'error' ? 'error' : 'warning'));
    const artifacts = (m.result?.artifacts ?? []).map((artifact, index) =>
      new SidebarItem(artifact.kind, [], 'newsdk.openArtifact', artifact.path, 'package', [index]));
    const effective = Object.entries(m.progress?.effectiveOptions ?? m.result?.effectiveOptions ?? {}).map(([key, option]) =>
      new SidebarItem(key, [], 'newsdk.openWorkbench', `${option.value}（${option.source}）`));
    return [
      new SidebarItem(m.project?.name ?? '选择 MOD 文件夹', [], 'newsdk.selectProject', m.project?.root, 'root-folder'),
      new SidebarItem(`MOD 版本：${config?.modVersion || '未设置'}`, [], 'newsdk.setVersion', undefined, 'versions'),
      new SidebarItem(m.active ? '停止编译' : '编译 MOD', [], m.active ? 'newsdk.stop' : 'newsdk.build', undefined,
        m.active ? 'debug-stop' : 'play'),
      new SidebarItem(`状态：${statusLabel(m.status, m.operation)}`, [], 'newsdk.openWorkbench', m.message, 'pulse'),
      new SidebarItem('配置', [
        new SidebarItem('编译器', [], 'newsdk.configure', config?.compilerPath ?? '未配置', 'tools'),
        new SidebarItem('SDK 目录', [], 'newsdk.configure', config?.sdkRoot ?? '未配置', 'folder-library'),
        new SidebarItem('输出目录', [], 'newsdk.configure', config?.outputDirectory ?? '未配置', 'folder-opened'),
      ]),
      new SidebarItem('项目内容', content),
      ...(effective.length ? [new SidebarItem('实际生效配置', effective)] : []),
      new SidebarItem('快捷入口', [
        new SidebarItem('打开编译工作台', [], 'newsdk.openWorkbench', undefined, 'layout'),
        new SidebarItem('查看编译计划', [], 'newsdk.plan', undefined, 'list-tree'),
        new SidebarItem('环境检查', [], 'newsdk.doctor', undefined, 'checklist'),
        new SidebarItem('完整编译日志', [], 'newsdk.openLog', undefined, 'output'),
        new SidebarItem('事件 JSONL', [], 'newsdk.openEvents', undefined, 'file-code'),
        new SidebarItem('构建报告', [], 'newsdk.openReport', undefined, 'report'),
        new SidebarItem('打开输出目录', [], 'newsdk.openOutput', undefined, 'folder-opened'),
      ]),
      ...(issues.length ? [new SidebarItem(`本轮问题 (${issues.length})`, issues)] : []),
      ...(artifacts.length ? [new SidebarItem('本轮产物', artifacts)] : []),
    ];
  }
  dispose(): void { this.changed.dispose(); }
}
