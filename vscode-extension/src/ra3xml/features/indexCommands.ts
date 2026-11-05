import * as vscode from 'vscode';
import {AssetCommandServices,showAssetSearch} from './assetSearch';
import {showUnreferenced} from './unreferenced';
export function registerAssetCommands(context:vscode.ExtensionContext,services:AssetCommandServices):vscode.Disposable {
    const subscriptions:vscode.Disposable[]=[];
    const command=(name:string,run:(service:NonNullable<Awaited<ReturnType<AssetCommandServices['getService']>>>)=>PromiseLike<unknown>)=>{
        subscriptions.push(vscode.commands.registerCommand(name,async()=>{try{
            if(!vscode.workspace.isTrusted)return;
            const service=await services.getService();if(!service){await vscode.window.showInformationMessage('请先选择 MOD 项目。');return;}await run(service);
        }catch(error){await vscode.window.showErrorMessage(`NewSdk XML：${error instanceof Error?error.message:String(error)}`);}}));
    };
    command('newsdk.xml.searchAssets',service=>showAssetSearch(service));
    command('newsdk.xml.unreferenced',service=>showUnreferenced(service));
    command('newsdk.xml.unreferencedType',service=>showUnreferenced(service,true));
    command('newsdk.xml.reindex',service=>vscode.window.withProgress({location:vscode.ProgressLocation.Notification,title:'NewSdk XML：重新索引工作区'},()=>service.reindex()));
    command('newsdk.xml.clearIndex',async service=>{
        await vscode.window.withProgress({location:vscode.ProgressLocation.Notification,title:'NewSdk XML：清除当前 MOD/SDK/XSD 的索引缓存并重建'},()=>service.clearCacheAndRebuild());
        await vscode.window.showInformationMessage('当前 XML 索引缓存已重建。编译缓存和 MOD 文件保持原样。');
    });
    command('newsdk.xml.indexReport',async service=>{
        await service.startIndexing();const document=await vscode.workspace.openTextDocument({language:'json',content:JSON.stringify(service.getIndexReport(),null,2)});
        await vscode.window.showTextDocument(document,{preview:true});
    });
    const api={dispose(){subscriptions.forEach(subscription=>subscription.dispose());}};context.subscriptions.push(api);return api;
}
