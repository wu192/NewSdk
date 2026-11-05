import * as vscode from 'vscode';
import type {AssetService} from '../services/assetService';
import {showAssetSearch} from './assetSearch';
export async function showUnreferenced(service:AssetService,currentType=false):Promise<void> {
    let type:string|undefined;
    if(currentType){
        const editor=vscode.window.activeTextEditor;if(editor){const offset=editor.document.offsetAt(editor.selection.active),file=service.documents.normalize(editor.document.uri.fsPath);
            type=service.findAssets().find(asset=>service.documents.normalize(asset.uri)===file&&asset.node.start<=offset&&offset<asset.node.end)?.type;}
        if(!type){await service.startIndexing();type=await vscode.window.showQuickPick([...new Set(service.findAssets().filter(asset=>!asset.readonly).map(asset=>asset.type))].sort(),{title:'选择要查询的 MOD 资产类型'});}
        if(!type)return;
    }
    await showAssetSearch(service,{unreferenced:true,type});
}
