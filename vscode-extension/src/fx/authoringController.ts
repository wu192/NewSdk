import * as vscode from 'vscode';
import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { randomBytes } from 'node:crypto';
import { canonicalPath, containsPath } from '../project';
import { AssetService } from '../ra3xml/services/assetService';
import { FxSkillManager } from './skillManager';
import { SkillRunner, SkillRunResult } from './skillRunner';
import { selectFxSchema } from './schemaSelection';
import { AuthoringPlan, AuthoringTarget, localAuthoringChecks, parseAssistantResult, prepareAuthoringPlan } from './authoring';
import { canApplyAuthoring, FxAuthoringAction, FxAuthoringAttachment, FxAuthoringView, FxValidationReport } from './skillIntent';

export interface AuthoringHost {
    disposed?:boolean; document: vscode.TextDocument; target?: AuthoringTarget; modRoot: string; sdkRoot?: string; semantic?: AssetService;
    editable(): Promise<void>; publish(view: FxAuthoringView): Promise<void>; preview(target?:AuthoringTarget): Promise<void>;
}
interface Draft {id:string;version:number;mode:'new'|'edit';plan:AuthoringPlan;report:FxValidationReport;intent?:unknown;generated?:SkillRunResult;}
interface Session {mode:'new'|'edit';busy:boolean;attachments:FxAuthoringAttachment[];draft?:Draft;message?:string;}
export class FxAuthoringController implements vscode.Disposable {
    private readonly sessions = new Map<string,Session>();
    private readonly previews = new Map<string,string>();
    private readonly subscription: vscode.Disposable;
    constructor(private readonly manager?:FxSkillManager, private readonly runnerProvider?:()=>SkillRunner|undefined) {
        this.subscription = vscode.workspace.registerTextDocumentContentProvider('newsdk-fx-proposal',{provideTextDocumentContent:uri=>this.previews.get(uri.toString())??'建议已释放。'});
    }
    private get runner():SkillRunner|undefined {return this.runnerProvider?.();}
    private session(host:AuthoringHost):Session {const key=host.document.uri.toString();let s=this.sessions.get(key);if(!s){s={mode:'edit',busy:false,attachments:[]};this.sessions.set(key,s);}return s;}
    async publish(host:AuthoringHost,focus=false):Promise<void> {
        if(host.disposed)return;const s=this.session(host);const snapshot=await this.manager?.current();const draft=s.draft;const targetMatches=!draft||draft.mode===s.mode&&(draft.mode==='new'||!!host.target&&draft.plan.target?.id===host.target.id&&draft.plan.target?.type===host.target.type);
        await host.publish({mode:s.mode,focus,busy:s.busy,skill:snapshot&&{version:snapshot.version},message:s.message,attachments:s.attachments,
            resources:(host.semantic?.findAssets()??[]).filter(asset=>['Texture','FXList','FXParticleSystemTemplate'].includes(asset.type)).slice(0,100).map(asset=>({type:asset.type,id:asset.id})),
            draft:draft&&{id:draft.id,changes:draft.plan.changes,files:draft.generated?.files.map(file=>file.relativePath)??[],report:draft.report,intent:draft.intent,
                applicable:!!draft.plan.patches.length&&canApplyAuthoring(draft.report)&&draft.version===host.document.version&&targetMatches,stale:draft.version!==host.document.version||!targetMatches}});
    }
    async action(host:AuthoringHost,action:FxAuthoringAction):Promise<void> {
        if(host.disposed)return;const s=this.session(host);if(s.busy)return;
        if('mode' in action&&(action.mode==='new'||action.mode==='edit'))s.mode=action.mode;
        if(action.action==='open'){await this.publish(host,true);return;}
        if(action.action==='preview'){await host.preview();return;}
        if(action.action==='clearAttachments'){s.attachments=[];await this.publish(host);return;}
        s.busy=true;await this.publish(host);
        try {
            if(action.action==='attach')await this.attach(s);
            else if(action.action==='copyPrompt') {await vscode.env.clipboard.writeText(await this.prompt(host,s,action.description,action.resourceIds));s.message='提示词已复制；附件未被文本助手读取，仍列为未知输入。';}
            else if(action.action==='generate') {
                if(s.mode!=='new')throw new Error('受控 impact/smoke 配方用于新建；修改当前资产请导入最小修改建议或交给 AI。');
                const skill=await this.manager?.current();if(!skill||!this.runner)throw new Error('请先导入 ra3-fx-authoring Skill 并配置 Python 3。');
                const generated=await this.runner.generate(skill.rootPath,{description:this.description(action.description),recipe:action.recipe??'impact',name:action.name??'Custom'});
                try {await this.stage(host,s,generated.xml,generated.intent,generated);}catch(error){await this.runner.disposeResult(generated);throw error;}
                s.message='已执行普通 GPU 配方。颜色、数量、大小、寿命、速度和角度来自受控映射；未观察图片或视频。';
            } else if(action.action==='editRecipe') {
                const target=host.target;if(!target||target.type!=='FXParticleSystemTemplate')throw new Error('受控属性编辑需要唯一选中 FXParticleSystemTemplate；请先打开该模板定义。');
                const skill=await this.manager?.current();const runner=this.runner;if(!skill||!runner)throw new Error('请先导入 Skill 并配置 Python 3。');
                s.mode='edit';const result=await runner.edit(skill.rootPath,{xml:host.document.getText(),templateId:target.id,settings:action.settings});
                try{await this.stage(host,s,result.xml,result.intent,result);}catch(error){await runner.disposeResult(result);throw error;}s.message='已执行受控属性编辑脚本；未改动其他资产，待核对最小差异。';
            } else if(action.action==='analyze') {
                if(!vscode.lm?.selectChatModels)throw new Error('此 VS Code 未提供 AI 接口；请复制提示词并导入聊天结果。');const models=await vscode.lm.selectChatModels({});if(!models.length)throw new Error('没有可用的 VS Code AI 助手；可复制提示词并导入聊天结果。');
                const choice=await vscode.window.showQuickPick(models.map(model=>({label:model.name,description:`${model.vendor} / ${model.family}`,model})),{title:'选择已连接的文本 AI 助手'});if(!choice)return;
                const prompt=await this.prompt(host,s,action.description,action.resourceIds);const confirmed=await vscode.window.showInformationMessage(`将所选资产 XML 和创作要求发送给 ${choice.label}？`,{modal:true,detail:'只发送当前目标、限量资源名称和 Skill 提示词。所选图片和视频不会发送给文本接口，也不会显示为已识别。'},'发送');if(confirmed!=='发送')return;
                const cancellation=new vscode.CancellationTokenSource();try {
                    const response=await choice.model.sendRequest([vscode.LanguageModelChatMessage.User(prompt)],{},cancellation.token);let text='';for await(const fragment of response.text){text+=fragment;if(text.length>4*1024*1024){cancellation.cancel();throw new Error('AI 响应超过 4 MiB 预算。');}}
                    const result=parseAssistantResult(text);await this.stage(host,s,result.xml,result.intent);s.message='已收到文本 AI 建议；静态检查、预览和编译状态分别记录。';
                }finally{cancellation.dispose();}
            } else if(action.action==='import') {
                let text=action.xml;
                if(text===undefined){const file=(await vscode.window.showOpenDialog({title:'导入 AI XML 或 JSON 结果',canSelectMany:false,filters:{'XML / JSON / 文本':['xml','json','txt']}}))?.[0];if(!file)return;const stat=await fs.stat(file.fsPath);if(stat.size>4*1024*1024)throw new Error('结果超过 4 MiB 预算。');text=await fs.readFile(file.fsPath,'utf8');}
                const result=parseAssistantResult(text);await this.stage(host,s,result.xml,result.intent);s.message='结果已暂存；确认前不会修改 MOD。';
            } else if(action.action==='check') {
                if(s.draft&&s.draft.version!==host.document.version)throw new Error('内容已更新，请重新导入或生成建议；旧差异不能应用。');
                const version=host.document.version;const xml=s.draft?.plan.text??host.document.getText();const report=await this.validate(host,xml,!s.draft&&!host.document.isDirty);if(host.document.version!==version)throw new Error('检查期间 XML 已变化，请重新检查。');
                if(s.draft)s.draft.report=report;
                else s.draft={id:randomBytes(10).toString('hex'),version,mode:s.mode,plan:{text:xml,patches:[],changes:['检查当前 XML；无拟应用修改。']},report};
                s.message='静态检查已执行；尚未表示预览可播放或编译成功。';
            } else if(action.action==='diff')await this.showDiff(host,s);
            else if(action.action==='apply')await this.apply(host,s);
        } catch(error) {s.message=error instanceof Error?error.message:String(error);throw error;}
        finally{s.busy=false;await this.publish(host);}
    }
    private description(value:string):string {if(typeof value!=='string'||!value.trim()||value.length>32000)throw new Error('请填写 1～32000 字符的特效描述。');return value;}
    private async prompt(host:AuthoringHost,s:Session,description:string,resourceIds:string[]=[]):Promise<string> {
        this.description(description);const skill=await this.manager?.promptText()??'按 RA3 XML 字段和资源约束输出建议；无法确定的信息写入 unknowns。';
        const doc=host.document.getText();let selected=doc;
        if(host.target){const model=host.semantic?.getDocument(host.document.uri.fsPath);const node=model?.elements.find(node=>node.localName===host.target?.type&&node.attributes.id===host.target.id);if(node)selected=doc.slice(node.start,node.end);}
        if(selected.length>64000)throw new Error('选中 XML 超过 AI 上下文 64000 字符预算；请先选择更小的模板。');
        const known=new Set((host.semantic?.findAssets()??[]).map(asset=>`${asset.type}:${asset.id}`));const resources=Array.isArray(resourceIds)?resourceIds.filter(id=>typeof id==='string'&&known.has(id)).slice(0,32).join('\n'):'';
        const fields=host.target?[...(host.semantic?.schema?.getAttributes(host.target.type)??[]),...(host.semantic?.schema?.getChildren(host.target.type)??[])].slice(0,40).map(field=>({name:field.name,type:field.type,required:field.required,default:field.default,enumValues:field.enumValues.slice(0,30),documentation:field.documentation?.slice(0,300)})):[];
        let structure='';const snapshot=await this.manager?.current();if(snapshot){try{structure=(await fs.readFile(path.join(snapshot.rootPath,'references','xml-structure.md'),'utf8')).slice(0,8000);}catch{/* Optional task-specific reference stays unknown when absent. */}}
        return `${skill.slice(0,32000)}\n\n当前任务：${s.mode==='new'?'新建特效':'仅修改选定资产，保留其他资产、注释、命名空间、Include 和未知节点'}\n描述：${description}\n目标：${host.target?`${host.target.type}:${host.target.id}`:'未选中'}\n附件（仅选择，未读取或观察）：${JSON.stringify(s.attachments)}\n已选资源名称（存在名称不代表文件和 shader 可用）：\n${resources}\n目标 XSD 字段（有限范围）：${JSON.stringify(fields)}\nXML 结构参考（有限片段）：${structure}\n当前 XML：\n${selected}\n\n先形成视觉意图，返回 JSON {"intent":{"input_kind":"text","appearance":[],"timing":[],"motion":[],"resources":[],"constraints":[],"unknowns":[]},"xml":"完整 AssetDeclaration XML"}。图片和视频尚未读取，必须将其视觉内容和时间关系保留为 unknowns。只支持普通 GPU 配方；特殊家族需已核对模板、端点和运动上下文。`;
    }
    private async attach(s:Session):Promise<void> {
        const files=await vscode.window.showOpenDialog({title:'选择图片、视频或真实关键帧（文本助手不会读取视觉内容）',canSelectMany:true,filters:{媒体:['png','jpg','jpeg','webp','gif','mp4','webm','mov']}});
        for(const file of files??[]){if(s.attachments.length>=12)throw new Error('最多选择 12 个附件。');const video=/\.(mp4|webm|mov)$/i.test(file.fsPath);let timestampSeconds:number|undefined;
            if(!video){const input=await vscode.window.showInputBox({title:'关键帧时间戳（秒，可留空表示普通图片）',validateInput:value=>value!==''&&(!Number.isFinite(Number(value))||Number(value)<0)?'请输入非负秒数。':undefined});if(input?.trim())timestampSeconds=Number(input);}
            s.attachments.push({name:path.basename(file.fsPath),kind:video?'video':timestampSeconds===undefined?'image':'keyframe',status:'selected',timestampSeconds});
        }
        s.message='已选择附件；尚未观察视觉内容。可用时间描述补充视频信息。';
    }
    private async stage(host:AuthoringHost,s:Session,xml:string,intent?:unknown,generated?:SkillRunResult):Promise<void> {
        const version=host.document.version;const plan=prepareAuthoringPlan(host.document.getText(),xml,version,s.mode,host.target);this.checkNewIdentities(host,plan);const report=await this.validate(host,plan.text,false);
        if(host.disposed||host.document.version!==version)throw new Error('检查期间 XML 已变化或编辑器已关闭，请重新生成建议。');
        if(s.mode==='edit'&&(!host.target||plan.target?.id!==host.target.id||plan.target?.type!==host.target.type))throw new Error('检查期间选中资产已变化，请重新生成建议。');
        if(s.draft){this.previews.delete(vscode.Uri.parse(`newsdk-fx-proposal:/${s.draft.id}.xml`).toString());if(s.draft.generated)await this.runner?.disposeResult(s.draft.generated);}
        s.draft={id:randomBytes(10).toString('hex'),version,mode:s.mode,plan,report,intent,generated};
    }
    private async validate(host:AuthoringHost,xml:string,realFile:boolean):Promise<FxValidationReport> {
        const schema=await selectFxSchema(host.sdkRoot,vscode.workspace.getConfiguration('newsdk').get<string>('fxSchemaPath',''));
        const skill=await this.manager?.current();const runner=this.runner;const fallback=localAuthoringChecks(xml);
        const input={...(realFile?{xmlPath:host.document.uri.fsPath}:{xml,sourceRelativePath:path.relative(host.modRoot,host.document.uri.fsPath).replace(/\\/g,'/')}),projectRoot:realFile?host.modRoot:undefined};
        if(!skill||!runner){
            if(runner&&schema.path&&!schema.issue){
                const report=await runner.validateSchema({...input,schemaPath:schema.path});
                const v1=fallback.levels.find(level=>level.level==='V1')!;if(v1.status==='FAIL')report.levels=report.levels.map(level=>level.level==='V1'?v1:level);
                return report;
            }
            if(schema.path||schema.issue){fallback.schema=schema.path;fallback.levels=fallback.levels.map(level=>level.level==='V2'?{level:'V2',status:'UNKNOWN',summary:schema.issue??'已选择 XSD 入口；校验器尚未初始化，请配置 Python 3 和 lxml。',rules:['XSD 入口选择'],diagnostics:[{message:schema.issue??'XSD 校验器尚未执行。'}]}:level);}
            return fallback;
        }
        const report=await runner.validate(skill.rootPath,{...input,schemaPath:schema.issue?undefined:schema.path});
        if(schema.issue){
            report.schema=schema.path;
            report.levels=report.levels.map(level=>level.level==='V2'?{level:'V2',status:'UNKNOWN',summary:schema.issue!,rules:['XSD 入口选择'],diagnostics:[{message:schema.issue!}]}:level);
            if(report.raw){report.raw.levels.V2='UNKNOWN';report.raw.diagnostics.push({level:'V2',severity:'unknown',code:'XSD_SELECTION_UNAVAILABLE',file:schema.path??'',path:'/',message:schema.issue});}
        }
        // Shared parser remains authoritative for the actual proposed document.
        const v1=fallback.levels.find(level=>level.level==='V1')!;if(v1.status==='FAIL')report.levels=report.levels.map(level=>level.level==='V1'?v1:level);
        if(!realFile){const resources=report.levels.find(level=>level.level==='V3');if(resources&&resources.status==='PASS'){resources.status='UNKNOWN';resources.summary+=' 建议尚在临时区；应用位置的相对资源需要再次核对。';}}
        return report;
    }
    private async showDiff(host:AuthoringHost,s:Session):Promise<void> {
        if(!s.draft)throw new Error('先生成或导入建议。');if(s.draft.version!==host.document.version)throw new Error('文档已变化，请重新生成差异。');
        const uri=vscode.Uri.parse(`newsdk-fx-proposal:/${s.draft.id}.xml`);if(this.previews.size>=8&&!this.previews.has(uri.toString()))this.previews.delete(this.previews.keys().next().value!);this.previews.set(uri.toString(),s.draft.plan.text);await vscode.commands.executeCommand('vscode.diff',host.document.uri,uri,'NewSdk FX · 拟修改的 XML（只读建议）');
    }
    private checkNewIdentities(host:AuthoringHost,plan:AuthoringPlan):void {for(const identity of plan.additions??[]){const matches=host.semantic?.findAssets(identity)??[];if(matches.some(asset=>!asset.readonly))throw new Error(`当前 MOD 已存在 ${identity.type}:${identity.id}；请使用新的 ID 前缀。`);}}
    private async apply(host:AuthoringHost,s:Session):Promise<void> {
        const draft=s.draft;if(!draft||!draft.plan.patches.length)throw new Error('没有待应用修改。');if(draft.version!==host.document.version)throw new Error('文档已变化，请重新生成差异。');if(!canApplyAuthoring(draft.report))throw new Error('V0 或 V1 检查失败，不能直接应用。');
        if(draft.mode==='edit'&&(!draft.plan.target||!host.target||draft.plan.target.id!==host.target.id||draft.plan.target.type!==host.target.type))throw new Error('选中资产已变化，请重新生成建议。');
        await host.editable();if(host.semantic)await host.semantic.getScope(host.document.uri.fsPath);this.checkNewIdentities(host,draft.plan);await this.showDiff(host,s);
        const files:{uri:vscode.Uri;contents:Uint8Array;relative:string}[]=[];
        for(const file of draft.generated?.files??[]){
            const relative=file.relativePath.replace(/\\/g,'/');if(!relative||relative.startsWith('/')||relative.split('/').includes('..')||!/^assets\//.test(relative))throw new Error('生成素材路径不安全。');
            const destination=await canonicalPath(path.resolve(path.dirname(host.document.uri.fsPath),relative));if(!containsPath(await canonicalPath(host.modRoot),destination))throw new Error('素材必须位于当前 MOD。');
            let existing=false;try{await fs.lstat(destination);existing=true;}catch(error){if((error as NodeJS.ErrnoException).code!=='ENOENT')throw error;}
            const contents=await fs.readFile(file.path);if(contents.length>16*1024*1024)throw new Error('单个素材超过 16 MiB 预算。');
            if(existing){const old=await fs.readFile(destination);if(!old.equals(contents))throw new Error(`素材 ${relative} 已存在且内容不同；请选择其他命名。`);continue;}
            files.push({uri:vscode.Uri.file(destination),contents,relative});
        }
        const statuses=draft.report.levels.map(level=>`${level.level} ${level.status}：${level.summary}`).join('\n');
        const confirmed=await vscode.window.showWarningMessage(`应用 ${draft.plan.changes.length} 项 XML 修改及 ${files.length} 个新增素材？`,{modal:true,detail:`${statuses}\n\n${draft.plan.changes.join('\n')}\n\n新增素材（来自导入 Skill 的生成输出；许可尚未核验，请确认）：\n${files.map(file=>file.relative).join('\n')||'无'}\n\n可撤销。检查未覆盖的项目仍需人工确认；预览与编译尚未执行。`},'确认应用');if(confirmed!=='确认应用')return;
        await host.editable();if(draft.mode==='edit'&&(!host.target||draft.plan.target?.id!==host.target.id||draft.plan.target?.type!==host.target.type))throw new Error('确认期间选中资产已变化，请重新生成建议。');if(host.document.version!==draft.version)throw new Error('确认期间文档已变化，请重新生成差异。');
        if(host.semantic)await host.semantic.getScope(host.document.uri.fsPath);this.checkNewIdentities(host,draft.plan);
        const edit=new vscode.WorkspaceEdit();for(const patch of draft.plan.patches)edit.replace(host.document.uri,new vscode.Range(host.document.positionAt(patch.start),host.document.positionAt(patch.end)),patch.text);
        for(const file of files){if(!containsPath(await canonicalPath(host.modRoot),await canonicalPath(file.uri.fsPath)))throw new Error('素材目录已变化，请重新检查。');edit.createFile(file.uri,{overwrite:false,contents:file.contents});}
        if(!await vscode.workspace.applyEdit(edit))throw new Error('修改未应用。');
        s.message='XML 和新增素材已应用，可撤销。预览与编译尚未执行。';if(draft.generated)await this.runner?.disposeResult(draft.generated);this.previews.delete(vscode.Uri.parse(`newsdk-fx-proposal:/${draft.id}.xml`).toString());s.draft=undefined;await host.preview(draft.plan.target);
    }
    release(uri:string):void {const s=this.sessions.get(uri);if(s?.draft)this.previews.delete(vscode.Uri.parse(`newsdk-fx-proposal:/${s.draft.id}.xml`).toString());if(s?.draft?.generated)void this.runner?.disposeResult(s.draft.generated);this.sessions.delete(uri);}
    dispose():void {for(const key of this.sessions.keys())this.release(key);this.previews.clear();this.subscription.dispose();}
}
