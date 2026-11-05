import * as vscode from 'vscode';
import type {AssetService, AssetSearchQuery, AssetSearchResult, UnreferencedAsset} from '../services/assetService';
export interface AssetCommandServices {getService():AssetService|undefined|Promise<AssetService|undefined>;}
interface AssetItem extends vscode.QuickPickItem {asset?:AssetSearchResult;next?:boolean;}
const sourceNames={'mod':'MOD','sdk-xml':'SDK XML','manifest':'manifest'};
/** Human-friendly filters; the underlying API also accepts structured filters. */
export function parseAssetSearch(text:string):AssetSearchQuery {
    let source:AssetSearchQuery['source'];let directory:string|undefined;
    const words=text.split(/\s+/).filter(word=>{
        const match=/^@(mod|sdk|manifest)$/i.exec(word);if(match){source=match[1].toLowerCase()==='sdk'?'sdk-xml':match[1].toLowerCase() as 'mod'|'manifest';return false;}
        if(word.startsWith('path:')){directory=word.slice(5);return false;}return true;
    });return {text:words.join(' '),source,directory};
}
export async function openAssetResult(asset:AssetSearchResult):Promise<void> {
    if(asset.source==='manifest'||!asset.location){
        const document=await vscode.workspace.openTextDocument({language:'json',content:JSON.stringify({...asset,note:'此来源只有 manifest 元数据，没有已确认的 XML 源位置。'},null,2)});
        await vscode.window.showTextDocument(document,{preview:true});return;
    }
    const document=await vscode.workspace.openTextDocument(vscode.Uri.file(asset.uri));
    await vscode.window.showTextDocument(document,{preview:true,selection:new vscode.Range(document.positionAt(asset.location.start),document.positionAt(asset.location.end))});
}
export async function showAssetSearch(service:AssetService,options:{unreferenced?:boolean;type?:string}={}):Promise<void> {
    const picker=vscode.window.createQuickPick<AssetItem>();picker.matchOnDescription=false;picker.matchOnDetail=false;
    picker.title=options.unreferenced?'NewSdk XML · 未被静态引用（不能据此判断可以删除）':'NewSdk XML · 查找资产';
    picker.placeholder='类型:ID、部分 ID；@mod / @sdk / @manifest；path:Data/目录';
    picker.busy=true;let offset=0;let disposed=false;
    const references={iconPath:new vscode.ThemeIcon('references'),tooltip:'查看引用'};
    const impact={iconPath:new vscode.ThemeIcon('type-hierarchy-sub'),tooltip:'查看影响'};
    const copy={iconPath:new vscode.ThemeIcon('files'),tooltip:'复制 SDK 资产到 MOD'};
    const refresh=():void=>{
        if(disposed)return;const query={...parseAssetSearch(picker.value),type:options.type,offset,limit:200};
        const page=options.unreferenced?service.getUnreferencedAssets(query):service.searchAssets(query);
        const suffix=page.completeness==='partial'?' · 索引部分完成':'';
        picker.title=`NewSdk XML · ${options.unreferenced?'未被静态引用':'查找资产'} · ${page.total} 项${suffix}`;
        picker.items=[...page.items.map(asset=>({label:`${asset.type}:${asset.id}`,description:`${sourceNames[asset.source]} · ${asset.referenceCount} 个已知引用`,
            detail:asset.relativePath+(options.unreferenced?` · 未被静态引用；${(asset as UnreferencedAsset).warnings.join('；')}`:''),asset,
            buttons:asset.source==='manifest'?[]:asset.source==='sdk-xml'?[references,impact,copy]:[references,impact]})),
            ...(offset+page.items.length<page.total?[{label:'下一页…',description:`${offset+page.items.length} / ${page.total}`,next:true}]:[])];
    };
    const listeners:vscode.Disposable[]=[picker.onDidChangeValue(()=>{offset=0;refresh();}),picker.onDidAccept(()=>{
        const picked=picker.selectedItems[0];if(picked?.next){offset+=200;refresh();return;}
        if(picked?.asset){picker.hide();void openAssetResult(picked.asset).catch(error=>vscode.window.showErrorMessage(String(error)));}
    }),picker.onDidTriggerItemButton(event=>{
        const asset=event.item.asset;if(!asset?.location)return;
        const command=event.button===copy?'newsdk.xml.copyToMod':event.button===impact?'newsdk.xml.impact':'newsdk.xml.references';
        void vscode.commands.executeCommand(command,vscode.Uri.file(asset.uri),asset.location.start);
    }),service.onDidChange(()=>{refresh();void service.startIndexing().then(refresh).catch(()=>undefined);}),picker.onDidHide(()=>{disposed=true;listeners.forEach(listener=>listener.dispose());picker.dispose();})];
    picker.show();refresh();try{await service.startIndexing();refresh();}finally{if(!disposed)picker.busy=false;}
}
