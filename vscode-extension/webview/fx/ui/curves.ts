import type { FxField, FxNode, FxSystem } from '../../../src/fx/previewProtocol';
import type { ParticleConfig } from '../runtime/types';
import { curveColor, integrateDamping } from '../runtime';
import { button,el,localName,option } from './dom';
import type { EditValue, FieldActions } from './fields';

function childPath(node:FxNode,path:number[],names:string[]):number[]|undefined {
    if(!names.length)return path;
    const index=node.children.findIndex(c=>localName(c.name)===names[0]);
    return index<0?undefined:childPath(node.children[index],[...path,index],names.slice(1));
}
function at(node:FxNode,path:number[]):FxNode|undefined {let n:FxNode|undefined=node;for(const i of path)n=n?.children[i];return n;}
const same=(a:number[]|undefined,b:number[])=>!!a&&a.length===b.length&&a.every((v,i)=>v===b[i]);
function fieldAt(fields:FxField[],path:number[],attribute:string):FxField|undefined {return fields.find(f=>same(f.previewPath,path)&&f.attribute===attribute);}

export function curvesPanel(system:FxSystem,actions:FieldActions):HTMLElement {
    const panel=el('section','curves-panel');
    const config=system.config,cpu=config?.backend==='CPU',count=cpu?8:4;
    panel.append(el('h3','',cpu?'CPU · 八槽独立颜色 / Alpha':'GPU RGBA · 共用颜色时间轴'));
    panel.append(el('p','hint',cpu?'颜色与 Alpha 各有独立时间；RGB / Alpha 分别逐逻辑帧推进。节点线仅示关键帧，不代表实际取样轨迹。':'槽 1～4；横轴为相对寿命百分比。Alpha 原时间保留，普通 GPU 不使用。槽 5～8 可在参数 / XML 查看。'));
    if(!config||!system.definition){panel.append(el('p','hint','此系统尚无可播放配置。'));return panel;}
    if(config.shader==='ADDITIVE'&&!cpu)panel.append(el('p','hint material-note','当前 GPU 加色为 ONE / ONE：Alpha 不缩放可见 RGB。淡出请调 RGB / RGBA 缩放或源贴图 RGB。'));
    else if(config.shader==='MULTIPLY'&&!cpu)panel.append(el('p','hint material-note','当前 GPU 乘色为 ZERO / INV_SRC_COLOR：衰减由颜色通道决定。'));
    type Slot={slot:number;field?:FxField;time:number};
    const slotFields=(kind:'Colors'|'Alphas'):Slot[]=>{
        const path=childPath(system.definition!,[],[kind]),nodes=path?at(system.definition!,path)!.children:[];
        const keys=kind==='Colors'?config.colors:config.alphas;
        const slots=Array.from({length:count},(_,i)=>({slot:i+1,time:keys?.[i]?.time??0})) as Slot[];
        nodes.forEach((node,index)=>{const slot=Number(node.attributes.Frame);if(slot>=1&&slot<=count&&localName(node.name)===(kind==='Colors'?'Color':'Alpha'))slots[slot-1].field=fieldAt(system.fields,[...path!,index],'RelativeAge');});return slots;
    };
    const colorTimes=slotFields('Colors'),alphaTimes=slotFields('Alphas');
    const timeline=(kind:'color'|'alpha',times:Slot[]):void=>{
        const box=el('div',`curve-timeline ${kind}-timeline`);box.append(el('h4','',kind==='color'?'颜色时间 · RGB':'Alpha 时间 · 独立推进'));
        const canvas=el('canvas','curve-canvas');canvas.width=600;canvas.height=170;canvas.tabIndex=0;canvas.setAttribute('aria-label',`${kind==='color'?'颜色':'Alpha'} 生命周期时间节点`);box.append(canvas);
        let selected=0,draftTime:number|undefined,dragging=false;
        const draw=()=>{
            const ctx=canvas.getContext('2d');if(!ctx)return;
            const width=canvas.width,height=canvas.height,left=32,right=width-20,top=12,bottom=height-28;
            ctx.clearRect(0,0,width,height);ctx.fillStyle='#11151c';ctx.fillRect(0,0,width,height);ctx.strokeStyle='#303742';ctx.lineWidth=1;
            for(let i=0;i<=4;i++){const x=left+(right-left)*i/4,y=top+(bottom-top)*i/4;ctx.beginPath();ctx.moveTo(x,top);ctx.lineTo(x,bottom);ctx.moveTo(left,y);ctx.lineTo(right,y);ctx.stroke();}
            const display={...config,colors:config.colors?.map((c,i)=>kind==='color'&&i===selected&&draftTime!==undefined?{...c,time:draftTime}:c),alphas:config.alphas?.map((c,i)=>kind==='alpha'&&i===selected&&draftTime!==undefined?{...c,time:draftTime}:c)};
            let max=1;for(const key of display.colors??[])max=Math.max(max,...key.value.map(Math.abs));for(const key of display.alphas??[])max=Math.max(max,Math.abs(key.range.low),Math.abs(key.range.high));
            const channels=['#ff6f79','#7bd89c','#76aefb','#f4f4f5'];
            for(let c=kind==='alpha'?3:0;c<(cpu&&kind==='color'?3:4);c++){
                ctx.strokeStyle=channels[c];ctx.lineWidth=c===3?2:1.5;ctx.beginPath();
                if(cpu){const keys=c===3?display.alphas:display.colors;(keys??[]).slice(0,count).forEach((key,i)=>{const value=c===3?(display.alphas![i].range.low+display.alphas![i].range.high)/2:display.colors![i].value[c],x=left+key.time*(right-left),y=bottom-value/max*(bottom-top);if(i===0)ctx.moveTo(x,y);else ctx.lineTo(x,y);});}
                else for(let i=0;i<=120;i++){const t=i/120,value=curveColor(display,t,0,display.alphas?.map(a=>(a.range.low+a.range.high)/2))[c],x=left+t*(right-left),y=bottom-value/max*(bottom-top);if(i===0)ctx.moveTo(x,y);else ctx.lineTo(x,y);}ctx.stroke();
            }
            ctx.font='11px sans-serif';ctx.textAlign='center';ctx.fillStyle='#b9c0cc';for(let i=0;i<=4;i++)ctx.fillText(`${i*25}%`,left+(right-left)*i/4,height-8);
            times.forEach((t,i)=>{const time=i===selected&&draftTime!==undefined?draftTime:t.time,x=left+time*(right-left);ctx.fillStyle=i===selected?'#e7b767':'#9aa6b7';ctx.beginPath();ctx.arc(x,top+7+(cpu?i%2*15:0),5,0,Math.PI*2);ctx.fill();ctx.fillText(String(i+1),x,top+23+(cpu?i%2*15:0));});
        };
        const timeFrom=(event:PointerEvent)=>{const r=canvas.getBoundingClientRect();return Math.max(0,Math.min(1,((event.clientX-r.left)*canvas.width/r.width-32)/(canvas.width-52)));};
        const edit=(time:number)=>{const field=times[selected].field!;return [{key:field.key,expectedValue:field.value,value:percentLexical(field.value,time*100)}];};
        canvas.addEventListener('pointerdown',event=>{const time=timeFrom(event);let distance=Infinity;times.forEach((t,i)=>{const d=Math.abs(time-t.time);if(d<distance){selected=i;distance=d;}});const field=times[selected].field;if(!field||field.readonly){if(field)actions.locate(field.key);draw();return;}dragging=true;canvas.setPointerCapture(event.pointerId);canvas.focus();draftTime=time;actions.draft(edit(time));draw();});
        canvas.addEventListener('pointermove',event=>{if(!dragging)return;draftTime=timeFrom(event);actions.draft(edit(draftTime));draw();});
        const cancel=()=>{dragging=false;draftTime=undefined;actions.cancel();draw();};
        canvas.addEventListener('pointerup',event=>{if(!dragging)return;dragging=false;const time=draftTime??times[selected].time;if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);actions.commit(edit(time));draftTime=undefined;draw();});
        canvas.addEventListener('pointercancel',cancel);canvas.addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();cancel();}});
        const slots=el('div','curve-slots');
        for(const t of times){const row=el('div','curve-slot');const input=el('input');input.type='number';input.min='0';input.max='100';input.step='0.1';input.value=String(t.time*100);input.disabled=!t.field||!!t.field.readonly;input.setAttribute('aria-label',`${kind==='color'?'颜色':'Alpha'}槽 ${t.slot} 相对寿命百分比`);
            let committed=false;const values=()=>t.field?[{key:t.field.key,value:percentLexical(t.field.value,Number(input.value)),expectedValue:t.field.value}]:[];
            input.addEventListener('input',()=>{committed=false;if(Number.isFinite(Number(input.value)))actions.draft(values());});const commit=()=>{if(!committed&&t.field&&Number.isFinite(Number(input.value))){committed=true;actions.commit(values());}};input.addEventListener('change',commit);input.addEventListener('blur',commit);input.addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();committed=true;input.value=String(t.time*100);actions.cancel();input.blur();}else if(event.key==='Enter'){event.preventDefault();commit();input.blur();}});
            row.append(el('span','',`槽 ${t.slot}`),input,el('span','','%'));if(t.field)row.append(button('↗','定位时间节点',()=>actions.locate(t.field!.key),'code-button'));slots.append(row);}
        box.append(slots);panel.append(box);draw();
    };
    timeline('color',colorTimes);if(cpu)timeline('alpha',alphaTimes);
    const random=config.alphas?.some(a=>a.range.low!==a.range.high);panel.append(el('p','hint',random?'Alpha 含随机上下界；白线仅示区间中值，实际取样由局部回放决定。':'Alpha 常量：下方操作将同时写入 Low 与 High。'));
    const alphaPath=childPath(system.definition,[],['Alphas']),nodes=alphaPath?at(system.definition,alphaPath)!.children:[];
    const alphaBox=el('div','alpha-tools');nodes.forEach((node,index)=>{const slot=Number(node.attributes.Frame);if(localName(node.name)!=='Alpha'||slot<1||slot>count)return;const p=[...alphaPath!,index],low=fieldAt(system.fields,p,'Low'),high=fieldAt(system.fields,p,'High');if(!low||!high)return;const input=el('input');input.type='number';input.step='0.05';input.value=low.value;input.disabled=!!low.readonly||!!high.readonly;input.setAttribute('aria-label',`Alpha 槽 ${slot} 常量`);const apply=button(`槽 ${slot} 设常量`,'同时写入 Alpha Low / High',()=>{if(Number.isFinite(Number(input.value)))actions.commit([{key:low.key,value:input.value,expectedValue:low.value},{key:high.key,value:input.value,expectedValue:high.value}]);});apply.disabled=input.disabled;const row=el('div','alpha-row');row.append(input,apply);alphaBox.append(row);});panel.append(alphaBox);
    if(!cpu){const sync=button('同步 Alpha 时间…','提交前显示前四槽 Alpha 时间差异',()=>{const changes=alphaTimes.flatMap((t,i)=>t.field&&!t.field.readonly?[{key:t.field.key,value:percentLexical(t.field.value,colorTimes[i].time*100),expectedValue:t.field.value}]:[]).filter(change=>change.value!==change.expectedValue);if(!changes.length)return;panel.querySelector('.sync-review')?.remove();const review=el('div','sync-review');review.append(el('strong','','确认同步 Alpha 时间'));changes.forEach(change=>review.append(el('p','hint',`${change.expectedValue} → ${change.value}`)));review.append(button('应用一次编辑','可通过一次撤销恢复',()=>{actions.commit(changes);review.remove();}),button('取消','保留原 Alpha 时间',()=>review.remove()));panel.append(review);});sync.disabled=!alphaTimes.some(t=>t.field&&!t.field.readonly);panel.append(sync);}
    panel.append(sizeTrend(config));return panel;
}
function percentLexical(original:string,value:number):string{return `${Number(value.toFixed(4))}${original.trim().endsWith('%')?'%':''}`;}
function sizeTrend(config:ParticleConfig):HTMLElement {
    const block=el('div','size-trend');block.append(el('h4','',config.backend==='CPU'?'尺寸参数 · CPU 递推':'尺寸趋势 · GPU 解析参数'));
    if(config.backend==='CPU'){block.append(el('p','hint','CPU 尺寸按 Size、StartSizeRate、SizeRate、SizeRateDamping 逐逻辑帧递推；实际轨迹取决于每粒子出生取样。'));return block;}
    const canvas=el('canvas','size-canvas');canvas.width=600;canvas.height=90;const ctx=canvas.getContext('2d');
    if(ctx){ctx.fillStyle='#11151c';ctx.fillRect(0,0,600,90);const life=(config.lifetime.low+config.lifetime.high)/2,initial=(config.size.low+config.size.high)/2,rate=(config.sizeRate.low+config.sizeRate.high)/2,d=(config.sizeRateDamping.low+config.sizeRateDamping.high)/2;const values=Array.from({length:101},(_,i)=>initial+rate*integrateDamping(d,i/100*life));const max=Math.max(1,...values.map(Math.abs));ctx.strokeStyle='#e7b767';ctx.beginPath();values.forEach((v,i)=>{const x=10+i*5.8,y=76-v/max*60;if(i)ctx.lineTo(x,y);else ctx.moveTo(x,y);});ctx.stroke();}
    block.append(canvas,el('p','hint','按 Size、SizeRate、SizeRateDamping 的均值参数计算；此 GPU 路径不消费 StartSizeRate。'));return block;
}
export function atlasPanel(system:FxSystem,actions:FieldActions):HTMLElement {
    const box=el('section','atlas-panel');box.append(el('h3','','Atlas 动画'));
    if(!system.config)return box;
    const c=system.config.atlas,mode=el('select');mode.id='atlas-mode';
    for(const [value,text] of [['loop','循环'],['row','指定行末停止'],['random','随机固定格'],['randomRow','随机行播放']])mode.append(option(value,text));
    mode.value=c.singleRow>0&&c.randTex===0?'row':c.randTex!==0?'random':c.singleRow===-1?'randomRow':'loop';
    const single=system.fields.find(f=>f.attribute==='SingleRow'&&f.previewPath),rand=system.fields.find(f=>f.attribute==='RandTex'&&f.previewPath);
    mode.disabled=!single||!rand||!!single.readonly||!!rand.readonly;
    mode.addEventListener('change',()=>{if(!single||!rand)return;actions.commit([{key:single.key,expectedValue:single.value,value:mode.value==='row'?String(Math.max(1,c.singleRow)):mode.value==='randomRow'?'-1':'0'},{key:rand.key,expectedValue:rand.value,value:mode.value==='random'?'1':'0'}]);});box.append(mode);
    box.append(el('p','hint',`${c.framesPerRow} × ${c.framesPerRow} 格 · TotalFrames ${c.totalFrames} · 速度 ${c.speedMultiplier} /逻辑帧`));
    const grid=el('div','atlas-grid');grid.style.gridTemplateColumns=`repeat(${Math.min(16,c.framesPerRow)},1fr)`;
    for(let i=0;i<Math.min(c.framesPerRow*c.framesPerRow,256);i++)grid.append(el('span','',String(i)));
    box.append(grid);if(mode.disabled)box.append(el('p','hint','编辑模式需要当前定义的 SingleRow / RandTex 字段；可在 XML 中补齐默认属性。'));return box;
}
