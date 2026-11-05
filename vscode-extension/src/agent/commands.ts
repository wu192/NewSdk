import * as vscode from 'vscode';
import {AssetService} from '../ra3xml/services/assetService';
import {AssetQueryAdapter} from './queryAdapter';
import {ReadonlyMcpServer} from './mcpServer';

export interface McpController extends vscode.Disposable { reset(): void; }
/** Opt-in lifecycle; no listener or project context is exposed before Start. */
export function registerMcpCommands(context: vscode.ExtensionContext, services: {
    getService(): AssetService | undefined;
    refresh(service: AssetService): Promise<void>;
}): McpController {
    let server: ReadonlyMcpServer | undefined;
    let configuration: unknown;
    let generation = 0;
    let starting = false;
    const reset = () => {
        generation++; configuration = undefined;
        const previous = server; server = undefined;
        if (previous) void previous.stop();
    };
    const command = (name: string, action: () => Promise<unknown>) => context.subscriptions.push(vscode.commands.registerCommand(name, async () => {
        try {
            if (!vscode.workspace.isTrusted) throw new Error('请先信任工作区。');
            return await action();
        } catch (error) { void vscode.window.showErrorMessage(`NewSdk MCP：${error instanceof Error ? error.message : String(error)}`); }
    }));
    command('newsdk.agent.start', async () => {
        if (configuration) return configuration;
        if (starting) throw new Error('只读 MCP 正在启动。');
        const service = services.getService(); if (!service) throw new Error('请先选择 MOD 项目。');
        const selectedGeneration = generation;
        starting = true;
        let pending: ReadonlyMcpServer | undefined;
        try {
            await services.refresh(service);
            if (generation !== selectedGeneration) throw new Error('项目已切换，请重新启动 MCP。');
            pending = new ReadonlyMcpServer(new AssetQueryAdapter(service, () => services.refresh(service)));
            const started = await pending.start();
            if (generation !== selectedGeneration) throw new Error('项目已切换，请重新启动 MCP。');
            server = pending;
            configuration = {mcpServers: {newsdk: started.clientConfig}};
            void vscode.window.showInformationMessage('只读 MCP 已在本机启用；查询共享 XML 索引。', '复制连接配置').then(choice => {
                if (choice && configuration) return vscode.env.clipboard.writeText(JSON.stringify(configuration, null, 2));
            });
            return configuration;
        } catch (error) { if (pending && pending !== server) await pending.stop(); throw error; }
        finally { starting = false; }
    });
    command('newsdk.agent.stop', async () => { reset(); void vscode.window.showInformationMessage('只读 MCP 已停止。'); });
    command('newsdk.agent.copyConfiguration', async () => {
        if (!configuration) throw new Error('请先启动只读 MCP。');
        await vscode.env.clipboard.writeText(JSON.stringify(configuration, null, 2));
        void vscode.window.showInformationMessage('已复制本次会话的 MCP 连接配置。');
    });
    const controller = {reset, dispose: reset}; context.subscriptions.push(controller); return controller;
}
