import * as vscode from 'vscode';
import {FxSkillManager} from './skillManager';

/** Every destination is picked explicitly; imported scripts run only from authoring actions. */
export function registerSkillCommands(context: vscode.ExtensionContext, manager: FxSkillManager, changed: () => void): void {
    let busy = false;
    const command = (name: string, action: () => Promise<unknown>) => context.subscriptions.push(vscode.commands.registerCommand(name, async () => {
        if (!vscode.workspace.isTrusted) throw new Error('请先信任工作区。');
        if (busy) { void vscode.window.showInformationMessage('请等待当前 Skill 操作完成。'); return; }
        busy = true;
        try { return await action(); }
        catch (error) { void vscode.window.showErrorMessage(`NewSdk FX：${error instanceof Error ? error.message : String(error)}`); }
        finally { busy = false; }
    }));
    command('newsdk.fx.importSkill', async () => {
        const kind = await vscode.window.showQuickPick(['本地 Skill 目录', 'Skill ZIP 包'], {title: '导入/更新 ra3-fx-authoring'});
        if (!kind) return;
        const directory = kind === '本地 Skill 目录';
        const selected = await vscode.window.showOpenDialog({title: '选择你提供的 ra3-fx-authoring', canSelectFolders: directory,
            canSelectFiles: !directory, canSelectMany: false, filters: directory ? undefined : {ZIP: ['zip']}});
        if (!selected?.[0] || selected[0].scheme !== 'file') return;
        const snapshot = await vscode.window.withProgress({location: vscode.ProgressLocation.Notification, title: '检查并保存 Skill 版本'},
            () => manager.importSkill(selected[0].fsPath));
        changed();
        void vscode.window.showInformationMessage(`已导入 ${snapshot.version.slice(0, 19)} · ${snapshot.files.length} 个文件。${snapshot.validation.warnings.join('；')}`);
        return snapshot.version;
    });
    command('newsdk.fx.installSkill', async () => {
        if (!await manager.current()) throw new Error('请先导入 Skill。');
        const selected = await vscode.window.showOpenDialog({title: '选择 AI 助手的 skills 父目录（将安装 ra3-fx-authoring 子目录）',
            canSelectFolders: true, canSelectFiles: false, canSelectMany: false});
        if (!selected?.[0] || selected[0].scheme !== 'file') return;
        const installation = await manager.install(selected[0].fsPath);
        void vscode.window.showInformationMessage(`Skill 已安装到 ${installation.destination}。请在对应助手中重新加载 Skills。`);
        return installation.id;
    });
    command('newsdk.fx.openSkill', async () => {
        const file = await manager.documentPath(); if (!file) throw new Error('请先导入 Skill。');
        await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(vscode.Uri.file(file)), {preview: false});
    });
    command('newsdk.fx.copySkillPrompt', async () => {
        await vscode.env.clipboard.writeText(await manager.promptText());
        void vscode.window.showInformationMessage('已复制 Skill 聊天提示词；在 FX 的 AI 创作页可加入当前资产上下文。');
    });
    command('newsdk.fx.restoreSkill', async () => {
        const current = await manager.current();
        const selected = await vscode.window.showQuickPick((await manager.listSnapshots()).map(snapshot => ({
            label: snapshot.version.slice(0, 23), description: `${snapshot.importedAt}${snapshot.version === current?.version ? ' · 当前' : ''}`,
            detail: snapshot.source, version: snapshot.version})), {title: '恢复已导入的 Skill 版本'});
        if (!selected) return;
        await manager.restore(selected.version); changed();
        void vscode.window.showInformationMessage(`已恢复 Skill ${selected.label}。`);
    });
    command('newsdk.fx.uninstallSkill', async () => {
        const installations = await manager.listInstallations();
        if (!installations.length) { void vscode.window.showInformationMessage('没有由本扩展安装的 Skill。'); return; }
        const selected = await vscode.window.showQuickPick(installations.map(installation => ({label: installation.destination,
            description: installation.version.slice(0, 23), id: installation.id})), {title: '卸载本扩展安装的 Skill'});
        if (!selected) return;
        const result = await manager.uninstall(selected.id);
        if (result.removed) void vscode.window.showInformationMessage('已卸载本次 Skill 安装；导入版本快照仍保留。');
        else void vscode.window.showWarningMessage(`安装目录包含已修改或新增的文件，已保留：${result.preserved.slice(0, 8).join('、')}`);
    });
}
