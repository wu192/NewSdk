import { defaultScene } from '../../src/fx/previewProtocol';
import type { FxClientMessage, FxEnvelope, FxHostMessage, FxNode, FxScene, FxSystem, FxTextureTransfer, PreviewIR } from '../../src/fx/previewProtocol';
import { compileParticle, ParticleWorld } from './runtime';
import type { WorldSystem } from './runtime';
import { decodeBase64, FxRenderer } from './render/renderer';
import type { RenderLayer } from './render/renderer';
import { defaultCamera } from './render/camera';
import type { CameraState } from './render/camera';
import { button,el,label,option } from './ui/dom';
import { fieldsPanel } from './ui/fields';
import type { EditValue,FieldActions } from './ui/fields';
import { atlasPanel,curvesPanel } from './ui/curves';

declare function acquireVsCodeApi(): {postMessage(message:unknown):void;getState():unknown;setState(value:unknown):void;};
declare global {interface Window {__FX_BOOTSTRAP__?:FxEnvelope;__FX_TEST__?:boolean;__fxTest?:Record<string,unknown>;}}
const vscode=acquireVsCodeApi();
const bootstrap=window.__FX_BOOTSTRAP__;
if(!bootstrap||typeof bootstrap.sessionId!=='string')throw new Error('缺少预览会话标识。');
const sessionId=bootstrap.sessionId;
let version=bootstrap.documentVersion,requestId=bootstrap.requestId,lastPreviewRequest=-1;
let ir:PreviewIR|undefined,draftIr:PreviewIR|undefined,world:ParticleWorld|undefined,renderer:FxRenderer|undefined;
let editingUri='',disposed=false,active=true,documentVisible=!document.hidden,rendererFailed=false;
let raf:number|undefined,draftRaf:number|undefined,lastTime:number|undefined,accumulator=0,seekAbort:AbortController|undefined;
let pendingDraft:EditValue[]|undefined;
interface TextureAssembly {descriptor:FxTextureTransfer;levels:Uint8Array[];received:number[];version:number;discard:boolean;}
const transfers=new Map<string,TextureAssembly>();
const uploadedTextureKeys=new Set<string>();
const textureGpuErrors=new Map<string,string>();
const MAX_TRANSFER_BYTES=32*1024*1024;
interface SessionState {frame:number;playing:boolean;speed:number;endFrame:number;selected:string;hidden:string[];solo?:string;background:string;ground:boolean;lowResolution:boolean;followCursor:boolean;camera:CameraState;}
const saved=vscode.getState();
const validSaved=saved&&typeof saved==='object'?saved as Partial<SessionState>:{};
const state:SessionState={frame:validNumber(validSaved.frame,0,0,18000),playing:validSaved.playing!==false,speed:validNumber(validSaved.speed,1,0.1,4),endFrame:validNumber(validSaved.endFrame,300,1,18000),selected:typeof validSaved.selected==='string'?validSaved.selected:'',hidden:Array.isArray(validSaved.hidden)?validSaved.hidden.filter(s=>typeof s==='string'):[],solo:typeof validSaved.solo==='string'?validSaved.solo:undefined,background:/^#[0-9a-f]{6}$/i.test(validSaved.background??'')?validSaved.background!:'#10141b',ground:validSaved.ground!==false,lowResolution:!!validSaved.lowResolution,followCursor:!!validSaved.followCursor,camera:validCamera(validSaved.camera)};
function validNumber(value:unknown,fallback:number,min:number,max:number):number{return typeof value==='number'&&Number.isFinite(value)?Math.max(min,Math.min(max,value)):fallback;}
function validCamera(camera:unknown):CameraState {
    if(!camera||typeof camera!=='object')return structuredClone(defaultCamera);
    const c=camera as Partial<CameraState>,target=Array.isArray(c.target)&&c.target.length===3&&c.target.every(Number.isFinite)?c.target:defaultCamera.target;
    return {yaw:validNumber(c.yaw,defaultCamera.yaw,-1000,1000),pitch:validNumber(c.pitch,defaultCamera.pitch,0.03,Math.PI/2-0.03),distance:validNumber(c.distance,defaultCamera.distance,5,10000),target:[...target]};
}
type ClientPayload=FxClientMessage extends infer T?T extends FxClientMessage?Omit<T,keyof FxEnvelope>:never:never;
function send(payload:ClientPayload):void {if(!disposed)vscode.postMessage({...payload,sessionId,documentVersion:version,requestId:++requestId});}
function persist(host=false):void {vscode.setState({...state,frame:Math.max(0,world?.frame??state.frame)});if(host)send({type:'state',state:{...state}});}
function status(message:string,kind=''):void {statusBar.textContent=message;statusBar.className=`status ${kind}`;}
function showError(error:unknown):void {status(error instanceof Error?error.message:String(error),'error');}

const app=el('main','fx-app');app.id='fx-app';
const header=el('header','app-header'),brand=el('strong','brand','NewSdk · 特效');
const assetSelect=el('select','asset-select');assetSelect.id='asset-select';assetSelect.setAttribute('aria-label','当前特效');assetSelect.addEventListener('change',()=>{cancelDraft();send({type:'select',assetKey:assetSelect.value});});
const newSelect=el('select','new-select');newSelect.setAttribute('aria-label','新建特效');newSelect.append(option('','新建特效…'));for(const name of ['闪光','烟雾','喷射火花','持续光环'])newSelect.append(option(name,name));newSelect.addEventListener('change',()=>{if(newSelect.value)send({type:'new',preset:newSelect.value});newSelect.value='';});
const copyButton=button('复制为独立资源','预览要插入的资产和引用，确认后作为一个可撤销编辑',()=>{if(ir)send({type:'copy',assetKey:ir.assetKey});});
const buildButton=button('保存并编译','保存当前构建涉及的修改并调用 NewSdk 编译器',()=>{commitPendingField();send({type:'saveAndBuild'});},'primary');buildButton.id='save-build';
const reloadButton=button('重载资源','取消旧加载并按新 generation 重读文件',()=>send({type:'reload',resetRenderer:true}));
header.append(brand,assetSelect,newSelect,copyButton,reloadButton,buildButton);
const identity=el('div','identity');identity.id='editing-identity';
const toolbar=el('nav','playback');toolbar.setAttribute('aria-label','播放与时间轴');
const playButton=button('暂停','播放 / 暂停；逻辑步频 30 Hz',()=>setPlaying(!state.playing));playButton.id='play-button';
const replayButton=button('重播','使用固定预览初态重新播放',()=>{void seek(0).then(()=>setPlaying(true));});replayButton.id='replay-button';
const stepButton=button('单步','推进一个逻辑帧（1/30 秒）',()=>{setPlaying(false);if(world){world.step();state.frame=world.frame;draw();persist();}});stepButton.id='step-button';
const speed=el('select');speed.id='speed';for(const s of [0.25,0.5,1,2,4])speed.append(option(String(s),`${s}×`));speed.value=String(state.speed);speed.addEventListener('change',()=>{state.speed=Number(speed.value);lastTime=undefined;persist();});
const seekInput=el('input','seek');seekInput.id='seek';seekInput.type='range';seekInput.min='0';seekInput.max=String(state.endFrame);seekInput.step='1';seekInput.value=String(state.frame);seekInput.setAttribute('aria-label','时间轴逻辑帧');seekInput.addEventListener('input',()=>{setPlaying(false);void seek(Number(seekInput.value));});
const timeLabel=el('output','time-label','0.000 秒 · 帧 0');timeLabel.id='time-label';
const endInput=el('input','end-frame');endInput.type='number';endInput.min='1';endInput.max='18000';endInput.value=String(state.endFrame);endInput.title='播放范围终止逻辑帧（最多 600 秒）';endInput.addEventListener('change',()=>{state.endFrame=Math.round(validNumber(Number(endInput.value),300,1,18000));seekInput.max=String(state.endFrame);endInput.value=String(state.endFrame);persist();});
toolbar.append(playButton,replayButton,stepButton,label('速度',speed),seekInput,timeLabel,label('范围帧',endInput));
const workspace=el('div','workspace'),layers=el('aside','layers'),layerTitle=el('h2','','图层 / 调用实例'),layerList=el('div','layer-list');layerList.id='layer-list';
layers.append(layerTitle,el('p','hint','隐藏 / 独显只影响显示，各层仍按原顺序运行。'),layerList);
const stage=el('section','stage'),canvas=el('canvas','preview-canvas');canvas.id='fx-canvas';canvas.tabIndex=0;canvas.setAttribute('aria-label','特效三维预览；拖动旋转、滚轮缩放、点击选择系统');
const stageHint=el('div','stage-hint','静止源 · 平地 · 位置触发 · 标准高质量数学 · 源贴图预览');
const noGpu=el('div','no-gpu');noGpu.hidden=true;
const stats=el('output','render-stats');stats.id='render-stats';
stage.append(canvas,stageHint,noGpu,stats);
const inspector=el('aside','inspector'),tabs=el('nav','tabs'),inspectorBody=el('div','inspector-body');inspectorBody.id='inspector-body';
let activeTab='parameters';
for(const [id,title] of [['parameters','参数'],['scene','预览场景'],['capabilities','能力 / 问题']]){const b=button(title,title,()=>{activeTab=id;for(const child of Array.from(tabs.children))child.classList.toggle('selected',(child as HTMLElement).dataset.tab===id);renderInspector();});b.dataset.tab=id;b.classList.toggle('selected',id===activeTab);tabs.append(b);}
inspector.append(tabs,inspectorBody);workspace.append(layers,stage,inspector);
const statusBar=el('div','status','正在读取特效…');statusBar.id='status';statusBar.setAttribute('role','status');
app.append(header,identity,toolbar,workspace,statusBar);document.body.replaceChildren(app);

function selectedSystem():FxSystem|undefined {const current=draftIr??ir;return current?.systems.find(s=>s.key===state.selected)??current?.systems[0];}
function renderLayers():void {
    layerList.replaceChildren();
    for(const system of ir?.systems??[]){
        const row=el('div','layer-row');row.classList.toggle('selected',system.key===state.selected);row.dataset.systemKey=system.key;
        const choose=button(system.name,`调用实例；共享模板 ${system.templateKey}`,()=>{cancelDraft();state.selected=system.key;renderLayers();renderInspector();persist();},'layer-name');
        const visible=el('input');visible.type='checkbox';visible.checked=!state.hidden.includes(system.key);visible.title='显示此图层';visible.setAttribute('aria-label',`显示 ${system.name}`);visible.addEventListener('change',()=>{state.hidden=state.hidden.filter(k=>k!==system.key);if(!visible.checked)state.hidden.push(system.key);draw();persist();});
        const solo=button('独',state.solo===system.key?'取消独显':'只绘制该图层；其他图层继续逻辑运行',()=>{state.solo=state.solo===system.key?undefined:system.key;renderLayers();draw();persist();},'solo-button');solo.classList.toggle('selected',state.solo===system.key);
        row.append(visible,choose,solo);const detail=el('small','layer-detail',!system.triggered?'未触发':system.config?'GPU · 普通贴片':'暂未播放');detail.title=system.issues.map(i=>i.message).join('\n');row.append(detail);layerList.append(row);
    }
    if(!ir?.systems.length)layerList.append(el('p','hint','此资产没有可播放的普通 GPU 子系统；可继续查看 XML。'));
}
const actions:FieldActions={draft:queueDraft,commit:commitEdits,cancel:cancelDraft,locate:key=>{if(key)send({type:'locate',key});},openSource:key=>send({type:'openFieldSource',key}),override:edit=>send({type:'overrideField',...edit})};
function renderInspector():void {
    inspectorBody.replaceChildren();
    if(activeTab==='scene'){renderScene();return;}
    if(activeTab==='capabilities'){renderCapabilities();return;}
    if(!ir){inspectorBody.append(el('p','hint','请选择 XML 中的 FXList 或 FXParticleSystemTemplate。'));return;}
    const system=selectedSystem();
    if(system){
        inspectorBody.append(el('h2','system-title',system.name));
        const shared=el('div','shared-tools');shared.append(button('复制为独立模板','复制选中模板及其继承父定义，仅替换这次调用；不复制贴图文件',()=>send({type:'copy',assetKey:system.templateKey,systemKey:system.key})),button('编辑共享源','切换可视编辑器与原生 XML 到该模板定义；组合预览根保持不变',()=>send({type:'openShared',systemKey:system.key})),button('XML ↗','在原生 XML 中定位此系统',()=>{const f=system.fields[0];if(f)send({type:'locate',key:f.key});}));inspectorBody.append(shared);
        const fields=[...system.fields];const keys=new Set(fields.map(f=>f.key));
        const rootFields=ir.fields.filter(f=>!keys.has(f.key));
        if(rootFields.length){const root=el('details','call-fields');root.open=true;root.append(el('summary','','这次调用 / 组合'),fieldsPanel(rootFields,actions));inspectorBody.append(root);}
        const quick=fields.filter(f=>/\/(?:Lifetime|BurstCount|Size)\[\d+\]$/.test(f.nodePath)||f.attribute==='Shader'&&f.previewPath?.length===0).map(f=>f.attribute==='Shader'?{...f,group:'材质'}:f);
        if(quick.length){const quickPanel=fieldsPanel(quick,actions);for(const group of Array.from(quickPanel.querySelectorAll('details')))group.open=true;inspectorBody.append(el('h3','','常用参数'),quickPanel);}
        const quickKeys=new Set(quick.map(f=>f.key));
        inspectorBody.append(curvesPanel(system,actions),atlasPanel(system,actions),fieldsPanel(fields.filter(f=>!quickKeys.has(f.key)),actions));
    }else inspectorBody.append(fieldsPanel(ir.fields,actions));
}
function renderScene():void {
    const scene=ir?.scene??defaultScene;
    inspectorBody.append(el('h2','','预览场景'),el('p','hint','此处只保存到 VS Code 会话，不修改游戏 XML。内部采用 Z 向上坐标。'));
    const background=el('input');background.type='color';background.value=state.background;background.addEventListener('input',()=>{state.background=background.value;draw();persist();});inspectorBody.append(label('背景',background));
    const ground=el('input');ground.type='checkbox';ground.checked=state.ground;ground.addEventListener('change',()=>{state.ground=ground.checked;draw();persist();});inspectorBody.append(label('显示平面网格',ground));
    const low=el('input');low.type='checkbox';low.checked=state.lowResolution;low.addEventListener('change',()=>{state.lowResolution=low.checked;draw();persist();});inspectorBody.append(label('低性能档（最高 960×540）',low));
    const follow=el('input');follow.type='checkbox';follow.checked=state.followCursor;follow.addEventListener('change',()=>{state.followCursor=follow.checked;persist(true);});inspectorBody.append(label('跟随原生 XML 光标',follow));
    for(const [key,name] of [['source','源位置'],['target','目标位置']] as const){
        const vector=el('fieldset','vector');vector.append(el('legend','',name));
        for(let axis=0;axis<3;axis++){const input=el('input');input.type='number';input.step='1';input.value=String(scene[key][axis]);input.setAttribute('aria-label',`${name} ${'XYZ'[axis]}`);input.addEventListener('change',()=>{const next=structuredClone(scene);next[key][axis]=Number(input.value);if(Number.isFinite(next[key][axis]))send({type:'scene',scene:next});});vector.append(label('XYZ'[axis],input));}inspectorBody.append(vector);
    }
    const terrain=el('select');terrain.append(option('land','陆地'),option('water','水面'));terrain.value=scene.terrain;terrain.addEventListener('change',()=>send({type:'scene',scene:{...scene,terrain:terrain.value as FxScene['terrain']}}));inspectorBody.append(label('表面条件',terrain));
    for(const [key,name,step] of [['ground','地面高度','1'],['seed','固定预览初态','1']] as const){const input=el('input');input.type='number';input.step=step;input.value=String(scene[key]);input.addEventListener('change',()=>{const value=Number(input.value);if(Number.isFinite(value))send({type:'scene',scene:{...scene,[key]:value}});});inspectorBody.append(label(name,input));}
    inspectorBody.append(button('重置相机','固定预览相机，不影响粒子逻辑',()=>{state.camera=structuredClone(defaultCamera);draw();persist();}),el('p','hint','当前固定为位置触发、静止源、标准高质量数学；未实现的对象、骨骼和环境分支在能力列表中列明。合成平面未提供 soft particle 深度。'));
}
function renderCapabilities():void {
    inspectorBody.append(el('h2','','能力与当前差异'),el('p','hint','普通 GPU / SIMPLE_QUAD / GPUParticle.fx；材质采用公开标准高质量 profile。尚无游戏实机截图对照。'));
    const issues=ir?.issues??[];
    if(!issues.length)inspectorBody.append(el('p','success','当前资产未发现阻止播放的问题。'));
    issues.forEach((issue,index)=>{const row=el('div',`issue ${issue.severity}`);row.append(el('strong','',issue.code),el('p','',issue.message),button('XML ↗','定位问题源码',()=>send({type:'locate',key:`issue:${index}`})));inspectorBody.append(row);});
    for(const texture of ir?.textures??[]){const row=el('div',`texture-state ${texture.status==='ready'?'':'warning'}`);row.append(el('strong','',texture.name),el('p','',`${texture.source==='compiled'?'编译结果':'源贴图预览'} · ${texture.status}${texture.width?` · ${texture.width}×${texture.height}`:''}`));if(texture.message)row.append(el('p','hint',texture.message));if(texture.file)row.append(el('small','source-path',texture.file));inspectorBody.append(row);}
    inspectorBody.append(el('p','hint',`资源预算：粒子 / 出生记录 16 MiB；GPU 贴图 128 MiB，实例缓冲 32 MiB；画布 DPR 1，最高 ${state.lowResolution?'960×540':'1280×720'}。超限暂停，源 XML 保持原值。`));
}
function frameLabel(frame:number):void {state.frame=frame;seekInput.value=String(Math.min(frame,state.endFrame));timeLabel.textContent=`${(frame/30).toFixed(3)} 秒 · 帧 ${frame}`;}
function renderLayerData():RenderLayer[] {
    const current=draftIr??ir;
    return (world?.systems??[]).map(s=>{const system=current!.systems.find(x=>x.key===s.id)!;return {id:s.id,config:s.runtime.config,births:s.runtime.particles,alphaSamples:s.runtime.alphaSamples,textureKey:system?.textureKey,visible:!state.hidden.includes(s.id)&&(!state.solo||state.solo===s.id)};});
}
function ensureRenderer():void {
    if(renderer||rendererFailed||disposed||!active||!documentVisible)return;
    try {uploadedTextureKeys.clear();textureGpuErrors.clear();renderer=new FxRenderer(canvas,message=>{if(message.startsWith('GPU 上下文丢失'))setPlaying(false);status(message,'warning');if(message.startsWith('GPU 上下文已重建')){uploadedTextureKeys.clear();send({type:'reload'});}});noGpu.hidden=true;}
    catch(error){rendererFailed=true;state.playing=false;playButton.textContent='播放';playButton.disabled=true;replayButton.disabled=true;stepButton.disabled=true;stopFrame();noGpu.textContent=error instanceof Error?error.message:String(error);noGpu.hidden=false;status(noGpu.textContent,'warning');}
}
function resetRenderer():void {
    stopFrame();transfers.clear();uploadedTextureKeys.clear();textureGpuErrors.clear();
    renderer?.dispose();renderer=undefined;rendererFailed=false;
    playButton.disabled=false;replayButton.disabled=false;stepButton.disabled=false;
    noGpu.hidden=true;ensureRenderer();showPreviewStatus();draw();
}
function renderNow(renderFrame?:number):void {
    if(disposed||!active||!documentVisible)return;
    ensureRenderer();
    const current=draftIr??ir;
    if(!current||!world)return;
    try {
        if(renderer){renderer.setLayers(renderLayerData(),current.textures.map(texture=>texture.key));const result=renderer.render(renderFrame??world.frame,state.camera,current.scene,state.background,state.ground,state.lowResolution);stats.textContent=`${result.particles.toLocaleString()} 粒子 · ${result.draws} 批 · GPU ${(result.gpuBytes/1048576).toFixed(1)} MiB`;}
        frameLabel(Math.max(0,world.frame));
        if(world.budgetExceeded){setPlaying(false);status(`预览预算已达上限：${world.budgetExceeded}。时间轴可退回；不会修改 XML。`,'warning');}
    }catch(error){setPlaying(false);showError(error);}
}
function draw():void {if(raf===undefined&&active&&documentVisible&&!disposed)raf=requestAnimationFrame(t=>{raf=undefined;renderNow();if(state.playing)tick(t);});}
function tick(time:number):void {
    if(!state.playing||!active||!documentVisible||disposed)return;
    if(lastTime!==undefined)accumulator+=(time-lastTime)/1000*30*state.speed;
    lastTime=time;
    if(accumulator>120){setPlaying(false);status('追帧超过预算，已暂停；未跳过粒子或随机事件。','warning');return;}
    if(world){
        const deadline=performance.now()+4;
        while(accumulator>=1&&world.frame<state.endFrame&&!world.budgetExceeded){world.step();accumulator--;if(performance.now()>deadline)break;}
        renderNow(world.frame+Math.min(1,accumulator));
        if(world.frame>=state.endFrame){setPlaying(false);persist();return;}
    }
    if(state.playing&&raf===undefined)raf=requestAnimationFrame(t=>{raf=undefined;tick(t);});
}
function stopFrame():void {if(raf!==undefined){cancelAnimationFrame(raf);raf=undefined;}lastTime=undefined;accumulator=0;}
function setPlaying(playing:boolean):void {
    state.playing=playing;playButton.textContent=playing?'暂停':'播放';stopFrame();if(playing&&world&&active&&documentVisible)draw();persist();
}
async function seek(frame:number):Promise<void> {
    if(!world)return;
    seekAbort?.abort();const abort=new AbortController();seekAbort=abort;
    status('正在重播到指定时间…');
    try {await world.seekAsync(Math.max(0,Math.min(18000,Math.trunc(frame))),abort.signal);if(abort.signal.aborted)return;renderNow();persist();if(!world.budgetExceeded)showPreviewStatus();}
    catch(error){if(!abort.signal.aborted)showError(error);}
}
function definitions(current:PreviewIR):WorldSystem[] {
    const playable=current.systems.filter(s=>s.triggered&&s.config);
    if(playable.length>128)throw new Error('预览系统预算已达 128 层；已暂停加载，请选择较小组合。');
    return playable.map(s=>({id:s.key,config:s.config!,options:{offset:s.offset,rotation:s.rotation,rotationXYZ:s.rotationXYZ,startFrame:s.startFrame,systemLifeFrames:s.systemLifeFrames,initialDelayOverrideMs:s.initialDelayOverrideMs}}));
}
async function rebuild(current:PreviewIR,frame:number):Promise<void> {
    const selected=definitions(current);seekAbort?.abort();world?.dispose();world=new ParticleWorld(selected,current.scene.seed,32768,18000);
    stopFrame();await seek(frame);if(state.playing)draw();
}
function showPreviewStatus():void {
    if(rendererFailed){status(noGpu.textContent??'无可用 WebGL2，结构编辑仍可用。','warning');return;}
    if(draftIr){status('调节中 · 松手 / 回车提交一次文档编辑，Esc 取消','draft');return;}
    const gpuFailure=(ir?.textures??[]).map(texture=>textureGpuErrors.get(texture.key)).find(Boolean);
    if(gpuFailure){status(`部分显示 · ${gpuFailure}；可重载资源后重试。`,'warning');return;}
    const issues=ir?.issues??[];
    const blocked=issues.filter(i=>i.severity==='error').length;
    const incompleteTextures=(ir?.textures??[]).filter(texture=>texture.status!=='ready'||!uploadedTextureKeys.has(texture.key)).length;
    status(blocked||incompleteTextures?`部分显示 · ${blocked} 个问题，${incompleteTextures} 个素材尚不可用；详情可在“能力 / 问题”或 Problems 查看。`:issues.length?`预览可用 · ${issues.length} 条能力 / 资源提示。`:'预览可用 · 固定局部初态 · 源贴图预览',blocked||incompleteTextures?'warning':'');
}
function queueDraft(edits:EditValue[]):void {
    pendingDraft=edits;
    if(draftRaf!==undefined)return;
    draftRaf=requestAnimationFrame(()=>{draftRaf=undefined;const value=pendingDraft;pendingDraft=undefined;if(value)void applyDraft(value).catch(showError);});
}
async function applyDraft(edits:EditValue[]):Promise<void> {
    if(!ir)return;
    const current=structuredClone(ir);let changed=false;
    for(const system of current.systems){
        if(!system.definition)continue;
        for(const edit of edits){const field=system.fields.find(f=>f.key===edit.key);if(!field?.previewPath||field.readonly)continue;
            let node:FxNode|undefined=system.definition;for(const index of field.previewPath)node=node?.children[index];
            if(node){node.attributes[field.attribute]=edit.value;changed=true;}
        }
        if(changed){const compiled=compileParticle(system.definition);if(!compiled.config){status(`草稿尚未应用：${compiled.issues.map(i=>i.message).join('；')}`,'warning');return;}system.config=compiled.config;}
    }
    if(!changed){status('调节中 · 此调用参数将在提交后重建预览','draft');return;}
    draftIr=current;await rebuild(current,Math.max(0,state.frame));showPreviewStatus();
}
function commitEdits(edits:EditValue[]):void {
    if(draftRaf!==undefined){cancelAnimationFrame(draftRaf);draftRaf=undefined;}pendingDraft=undefined;
    const changed=edits.filter(e=>e.value!==e.expectedValue);if(!changed.length){cancelDraft();return;}
    status('正在将单次调节写入文档…');send({type:'edit',edits:changed});
}
function commitPendingField():void {const focus=document.activeElement;if(focus instanceof HTMLInputElement||focus instanceof HTMLSelectElement)focus.blur();}
function cancelDraft():void {
    if(draftRaf!==undefined){cancelAnimationFrame(draftRaf);draftRaf=undefined;}pendingDraft=undefined;
    if(draftIr&&ir){draftIr=undefined;void rebuild(ir,Math.max(0,state.frame));}showPreviewStatus();
}
async function receive(message:FxHostMessage):Promise<void> {
    if(!message||typeof message!=='object'||message.sessionId!==sessionId||disposed)return;
    if(!Number.isSafeInteger(message.documentVersion)||!Number.isSafeInteger(message.requestId))return;
    if(message.type==='dispose'){dispose();return;}
    if(message.type==='active'){setActivity(message.active);return;}
    if(message.documentVersion<version)return;
    if(message.type==='focusField'){focusField(message.key);return;}
    if(message.type==='resetRenderer'){resetRenderer();return;}
    if(message.type==='status'){status(message.message,message.stale?'warning':'');return;}
    if(message.type==='textureState'){receiveTextureState(message);return;}
    if(message.type==='textureBegin'||message.type==='textureChunk'||message.type==='textureEnd'){receiveTexture(message);return;}
    if(message.type!=='preview'||message.requestId<lastPreviewRequest||message.ir?.schemaVersion!==1)return;
    lastPreviewRequest=message.requestId;version=message.documentVersion;requestId=Math.max(requestId,message.requestId);
    const hadDraft=!!draftIr;draftIr=undefined;pendingDraft=undefined;if(draftRaf!==undefined){cancelAnimationFrame(draftRaf);draftRaf=undefined;}
    transfers.clear();ir=message.ir;editingUri=message.editingUri;
    const currentTextureKeys=new Set(ir.textures.map(texture=>texture.key));for(const key of uploadedTextureKeys)if(!currentTextureKeys.has(key))uploadedTextureKeys.delete(key);for(const key of textureGpuErrors.keys())if(!currentTextureKeys.has(key))textureGpuErrors.delete(key);
    for(const texture of ir.textures){const failure=textureGpuErrors.get(texture.key);if(failure){texture.status='gpu-error';texture.message=failure;}}
    assetSelect.replaceChildren();for(const asset of message.assets)assetSelect.append(option(asset.key,`${asset.name} · ${asset.type==='FXList'?'组合':'模板'}`));assetSelect.value=ir.assetKey;
    if(!ir.systems.some(s=>s.key===state.selected))state.selected=ir.systems[0]?.key??'';
    identity.textContent=`正在编辑 ${fileName(editingUri)} / 正在预览 ${ir.name}`;identity.title=`编辑：${editingUri}\n预览入口：${ir.entry}`;
    stageHint.textContent=`静止源 · ${ir.scene.terrain==='land'?'陆地':'水面'} · 位置触发 · 标准高质量数学 · 源贴图预览`;
    renderLayers();renderInspector();ensureRenderer();
    renderer?.setLayers(ir.systems.filter(s=>s.config).map(s=>({id:s.key,config:s.config!,births:[],alphaSamples:[],textureKey:s.textureKey,visible:true})),ir.textures.map(texture=>texture.key));
    for(const texture of ir.textures){try{if(renderer&&(texture.pixels||texture.pixelsBase64)){renderer.uploadTexture(texture);uploadedTextureKeys.add(texture.key);textureGpuErrors.delete(texture.key);}}catch(error){textureFailure(texture.key,error);showError(error);}delete texture.pixels;delete texture.pixelsBase64;}
    await rebuild(ir,Math.max(0,state.frame));
    if(hadDraft)status('内容已更新，预览已与当前文档同步；如有冲突请重新应用该项。');else showPreviewStatus();
}
function focusField(key:string):void {
    const system=ir?.systems.find(s=>s.fields.some(f=>f.key===key));if(system)state.selected=system.key;
    activeTab='parameters';for(const child of Array.from(tabs.children))child.classList.toggle('selected',(child as HTMLElement).dataset.tab==='parameters');
    renderLayers();renderInspector();
    const row=Array.from(inspectorBody.querySelectorAll<HTMLElement>('[data-field-key]')).find(r=>r.dataset.fieldKey===key);
    if(row){let parent=row.parentElement;while(parent&&parent!==inspectorBody){if(parent instanceof HTMLDetailsElement)parent.open=true;parent=parent.parentElement;}row.classList.add('follow-highlight');row.scrollIntoView({block:'nearest'});setTimeout(()=>row.classList.remove('follow-highlight'),1400);}
}
function receiveTextureState(message:Extract<FxHostMessage,{type:'textureState'}>):void {
    if(!active||!documentVisible||!ir||message.documentVersion!==version||message.requestId<lastPreviewRequest)return;
    const current=ir.textures.find(texture=>texture.key===message.previousKey);
    if(!current||!message.texture||typeof message.texture.key!=='string'||!message.texture.key)return;
    const metadata={...message.texture};delete metadata.pixels;delete metadata.pixelsBase64;
    const failure=textureGpuErrors.get(metadata.key);if(failure){metadata.status='gpu-error';metadata.message=failure;}
    const update=(preview:PreviewIR)=>{
        const index=preview.textures.findIndex(texture=>texture.key===message.previousKey);
        if(index<0)return;
        const location=metadata.location??preview.textures[index].location;
        preview.textures[index]={...metadata};
        if(message.texture.status==='ready'&&location)preview.issues=preview.issues.filter(issue=>issue.code!=='texture-resource'||issue.uri!==location.uri||issue.start!==location.start);
        for(const system of preview.systems)if(system.textureKey===message.previousKey)system.textureKey=metadata.key;
        if(message.issue){const issueIndex=preview.issues.findIndex(issue=>issue.code===message.issue!.code&&issue.uri===message.issue!.uri&&issue.start===message.issue!.start);if(issueIndex<0)preview.issues.push(message.issue);else preview.issues[issueIndex]=message.issue;}
    };
    update(ir);if(draftIr)update(draftIr);
    if(message.previousKey!==metadata.key){transfers.delete(message.previousKey);uploadedTextureKeys.delete(message.previousKey);textureGpuErrors.delete(message.previousKey);}
    // Keep the existing simulation/RNG/time. Only resource identity and render bindings change.
    if(world)renderer?.setLayers(renderLayerData(),(draftIr??ir).textures.map(texture=>texture.key));
    if(activeTab==='capabilities')renderInspector();showPreviewStatus();draw();
}
type TextureMessage=Extract<FxHostMessage,{type:'textureBegin'|'textureChunk'|'textureEnd'}>;
function receiveTexture(message:TextureMessage):void {
    if(!active||!documentVisible||message.documentVersion!==version||message.requestId<lastPreviewRequest)return;
    if(message.type==='textureBegin'){
        const descriptor=message.texture;
        if(!descriptor||typeof descriptor.transferId!=='string'||!Array.isArray(descriptor.levels)||descriptor.levels.length<1||descriptor.levels.length>16)throw new Error('未知或无效的贴图传输。');
        if(!ir?.textures.some(texture=>texture.key===descriptor.key))return;
        if(!['RGBA8','DXT1','DXT3','DXT5'].includes(descriptor.format)||!Number.isInteger(descriptor.width)||!Number.isInteger(descriptor.height)||descriptor.width<1||descriptor.height<1||descriptor.width>16384||descriptor.height>16384)throw new Error('贴图传输的格式或尺寸无效。');
        if(descriptor.levels.length>Math.floor(Math.log2(Math.max(descriptor.width,descriptor.height)))+1)throw new Error('贴图传输 mip 层数过多。');
        transfers.delete(descriptor.key);
        const size=descriptor.levels.reduce((n,l)=>n+l.byteLength,0);
        const used=[...transfers.values()].reduce((n,t)=>n+t.descriptor.levels.reduce((sum,l)=>sum+l.byteLength,0),0);
        if(!Number.isSafeInteger(size)||size<1||used+size>MAX_TRANSFER_BYTES)throw new Error('贴图传输超过 32 MiB 暂存预算。');
        descriptor.levels.forEach((l,i)=>{const width=Math.max(1,descriptor.width>>i),height=Math.max(1,descriptor.height>>i),bytes=descriptor.format==='RGBA8'?width*height*4:Math.ceil(width/4)*Math.ceil(height/4)*(descriptor.format==='DXT1'?8:16);if(l.width!==width||l.height!==height||l.byteLength!==bytes)throw new Error('贴图传输 mip 描述无效。');});
        const discard=!renderer;
        transfers.set(descriptor.key,{descriptor,levels:descriptor.levels.map(l=>new Uint8Array(discard?0:l.byteLength)),received:descriptor.levels.map(()=>0),version:message.documentVersion,discard});return;
    }
    const transfer=transfers.get(message.key);
    if(!transfer||transfer.descriptor.transferId!==message.transferId||transfer.version!==message.documentVersion)return;
    if(message.type==='textureChunk'){
        const level=transfer.descriptor.levels[message.level];
        if(!level||message.offset!==transfer.received[message.level]||message.total!==level.byteLength||typeof message.pixelsBase64!=='string'||message.pixelsBase64.length>350000)throw new Error('贴图分块顺序或大小无效。');
        const bytes=decodeBase64(message.pixelsBase64);
        if(bytes.byteLength<1||bytes.byteLength>262144||message.offset+bytes.byteLength>level.byteLength)throw new Error('贴图分块超出目标。');
        if(!transfer.discard)transfer.levels[message.level].set(bytes,message.offset);
        transfer.received[message.level]+=bytes.byteLength;
        send({type:'textureAck',key:message.key,transferId:message.transferId,level:message.level,offset:transfer.received[message.level]});return;
    }
    try {
        if(transfer.received.some((n,i)=>n!==transfer.descriptor.levels[i].byteLength))throw new Error('贴图传输未完整，保留缺素材状态。');
        if(!transfer.discard&&renderer){renderer.uploadTransfer(transfer.descriptor,transfer.levels);uploadedTextureKeys.add(message.key);textureGpuErrors.delete(message.key);for(const preview of [ir,draftIr]){const texture=preview?.textures.find(t=>t.key===message.key);if(texture?.status==='gpu-error'){texture.status='ready';delete texture.message;}}}
    }catch(error){textureFailure(message.key,error);throw error;}finally{transfers.delete(message.key);}
    if(activeTab==='capabilities')renderInspector();showPreviewStatus();draw();
}
function textureFailure(key:string,error:unknown):void {
    const message=error instanceof Error?error.message:String(error);textureGpuErrors.set(key,message);uploadedTextureKeys.delete(key);
    for(const preview of [ir,draftIr]){const texture=preview?.textures.find(t=>t.key===key);if(texture){texture.status='gpu-error';texture.message=message;}}
    if(activeTab==='capabilities')renderInspector();
}
function fileName(value:string):string {return value.replace(/\\/g,'/').split('/').at(-1)??value;}
window.addEventListener('message',event=>{void receive(event.data as FxHostMessage).catch(showError);});
function setActivity(value:boolean):void {
    active=value;
    if(!value){stopFrame();seekAbort?.abort();persist(true);transfers.clear();uploadedTextureKeys.clear();textureGpuErrors.clear();renderer?.dispose();renderer=undefined;world?.dispose();world=undefined;}
    else if(ir){ensureRenderer();send({type:'reload'});}
}
document.addEventListener('visibilitychange',()=>{documentVisible=!document.hidden;if(!documentVisible){stopFrame();seekAbort?.abort();persist(true);transfers.clear();uploadedTextureKeys.clear();textureGpuErrors.clear();renderer?.dispose();renderer=undefined;world?.dispose();world=undefined;}else if(active&&ir){ensureRenderer();send({type:'reload'});}});
const resize=new ResizeObserver(()=>draw());resize.observe(stage);
let orbit:{x:number;y:number;yaw:number;pitch:number;moved:boolean}|undefined;
canvas.addEventListener('pointerdown',event=>{orbit={x:event.clientX,y:event.clientY,yaw:state.camera.yaw,pitch:state.camera.pitch,moved:false};canvas.setPointerCapture(event.pointerId);});
canvas.addEventListener('pointermove',event=>{if(!orbit)return;const dx=event.clientX-orbit.x,dy=event.clientY-orbit.y;orbit.moved ||=Math.hypot(dx,dy)>3;if(!orbit.moved)return;state.camera.yaw=orbit.yaw-dx*0.008;state.camera.pitch=Math.max(0.03,Math.min(Math.PI/2-0.03,orbit.pitch+dy*0.008));draw();});
canvas.addEventListener('pointerup',event=>{if(!orbit)return;if(!orbit.moved){const selected=renderer?.pick(event.clientX,event.clientY,world?.frame??state.frame);if(selected){state.selected=selected;renderLayers();renderInspector();}}orbit=undefined;if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);persist();});
canvas.addEventListener('pointercancel',()=>{orbit=undefined;});
canvas.addEventListener('wheel',event=>{event.preventDefault();state.camera.distance=Math.max(5,Math.min(10000,state.camera.distance*Math.exp(event.deltaY*0.001)));draw();persist();},{passive:false});
function dispose():void {
    if(disposed)return;persist();disposed=true;stopFrame();if(draftRaf!==undefined)cancelAnimationFrame(draftRaf);seekAbort?.abort();world?.dispose();world=undefined;renderer?.dispose();renderer=undefined;transfers.clear();uploadedTextureKeys.clear();textureGpuErrors.clear();resize.disconnect();ir=undefined;draftIr=undefined;
}
window.addEventListener('keydown',event=>{
    if(event.isComposing||!event.ctrlKey&&!event.metaKey)return;
    const key=event.key.toLowerCase();
    if(key==='z'||key==='y'){
        event.preventDefault();event.stopPropagation();cancelDraft();
        const fields=[...(ir?.fields??[]),...(ir?.systems.flatMap(s=>s.fields)??[])];
        for(const row of Array.from(inspectorBody.querySelectorAll<HTMLElement>('.field-row'))){const field=fields.find(f=>f.key===row.dataset.fieldKey),input=row.querySelector('input,select');if(field&&(input instanceof HTMLInputElement||input instanceof HTMLSelectElement)){input.value=field.value;input.classList.remove('draft');}}
        send({type:key==='y'||event.shiftKey?'redo':'undo'});
    }
});
window.addEventListener('pagehide',dispose);
if(window.__FX_TEST__)window.__fxTest={
    receive:(message:FxHostMessage)=>receive(message),seek:(frame:number)=>seek(frame),pause:()=>setPlaying(false),play:()=>setPlaying(true),dispose,
    stats:()=>({frame:world?.frame,playing:state.playing,raf:raf!==undefined,systems:world?.systems.length??0,active,disposed,gpu:renderer?.stats??null,issues:ir?.issues.length??0}),
    renderer:()=>renderer,world:()=>world,
};
playButton.textContent=state.playing?'暂停':'播放';ensureRenderer();send({type:'ready',s3tc:renderer?.supportsS3tc??false});
