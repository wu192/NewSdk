import * as vscode from 'vscode';
import * as path from 'node:path';
import type { AssetRecord } from './assetIndex';
import type { PreviewIR } from './previewProtocol';

type FxTreeNode = { kind: 'asset'; asset: AssetRecord } | { kind: 'system'; key: string; name: string; asset: AssetRecord; issues: number };

/** The tree distinguishes shared definitions from each call in the current preview. */
export class FxTreeProvider implements vscode.TreeDataProvider<FxTreeNode>, vscode.Disposable {
    private readonly changed = new vscode.EventEmitter<FxTreeNode | undefined>();
    readonly onDidChangeTreeData = this.changed.event;
    private assets: AssetRecord[] = [];
    private ir?: PreviewIR;
    update(assets: AssetRecord[], ir?: PreviewIR): void { this.assets = assets.filter(a => a.type === 'FXList' || a.type === 'FXParticleSystemTemplate'); this.ir = ir; this.changed.fire(undefined); }
    getTreeItem(node: FxTreeNode): vscode.TreeItem {
        if (node.kind === 'system') {
            const item = new vscode.TreeItem(node.name, vscode.TreeItemCollapsibleState.None);
            item.description = node.issues ? `${node.issues} 项差异` : '调用实例';
            item.tooltip = `${node.asset.id}\n${node.asset.uri}`;
            item.iconPath = new vscode.ThemeIcon(node.issues ? 'warning' : 'layers');
            item.command = { command: 'newsdk.previewFx', title: '预览调用', arguments: [vscode.Uri.file(node.asset.uri), node.asset.key] };
            return item;
        }
        const { asset } = node;
        const children = this.ir?.assetKey === asset.key && this.ir.systems.length > 0;
        const item = new vscode.TreeItem(asset.id, children ? vscode.TreeItemCollapsibleState.Expanded : vscode.TreeItemCollapsibleState.None);
        item.description = `${asset.type === 'FXList' ? '组合' : '模板'}${asset.readonly ? ' · SDK 只读' : ''}`;
        item.tooltip = `${asset.type} · ${asset.namespace}\n${path.basename(asset.uri)}\n${asset.uri}`;
        item.resourceUri = vscode.Uri.file(asset.uri);
        item.iconPath = new vscode.ThemeIcon(asset.type === 'FXList' ? 'list-tree' : 'sparkle');
        item.contextValue = 'newsdk.fxAsset';
        item.command = { command: 'newsdk.previewFx', title: '预览特效', arguments: [vscode.Uri.file(asset.uri), asset.key] };
        return item;
    }
    getChildren(node?: FxTreeNode): FxTreeNode[] {
        if (!node) return this.assets.map(asset => ({ kind: 'asset', asset }));
        if (node.kind !== 'asset' || this.ir?.assetKey !== node.asset.key) return [];
        return this.ir.systems.map(system => ({ kind: 'system', key: system.key, name: system.name, asset: node.asset, issues: system.issues.length }));
    }
    dispose(): void { this.assets = []; this.ir = undefined; this.changed.dispose(); }
}
