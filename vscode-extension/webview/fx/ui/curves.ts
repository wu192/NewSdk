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
    const panel=el('section','curves-panel');panel.append(el('h3','','GPU RGBA · 共用颜色时间轴'));
    panel.append(el('p','hint','槽 1～4；横轴为相对寿命百分比。Alpha 原时间保留，普通 GPU 不使用。槽 5～8 可在参数/XML 查看。'));
    if(!system.config){panel.append(el('p','hint','此系统尚无可播放配置。'));return panel;}
    const canvas=el('canvas','curve-canvas');canvas.width=600;canvas.height=170;canvas.setAttribute('aria-label','RGBA 生命周期曲线，拖动颜色节点可修改 RelativeAge');panel.append(canvas);
    const config=system.config;
    if(config.shader==='ADDITIVE')panel.append(el('p','hint material-note','当前加色配置为 ONE / ONE：Alpha 不缩放可见 RGB。淡出请调 RGB / RGBA 缩放或源贴图 RGB；需要按 Alpha 透明时选择 ALPHA 材质。'));
    else if(config.shader==='MULTIPLY')panel.append(el('p','hint material-note','当前乘色配置为 ZERO / INV_SRC_COLOR：Alpha 不缩放可见 RGB；衰减由颜色通道决定。'));
    const colorsPath=system.definition?childPath(system.definition,[],['Colors']):undefined;
    const colorNodes=colorsPath?at(system.definition!,colorsPath)!.children:[];
    const times:({slot:number;field?:FxField;time:number})[]=Array.from({length:4},(_,i)=>({slot:i+1,time:config.colors?.[i]?.time??0}));
    colorNodes.forEach((node,index)=>{
        const slot=Number(node.attributes.Frame);if(localName(node.name)==='Color'&&slot>=1&&slot<=4)times[slot-1].field=fieldAt(system.fields,[...colorsPath!,index],'RelativeAge');
    });
    let selected=0,draftTime:number|undefined;
    const draw=()=>{
        const ctx=canvas.getContext('2d');if(!ctx)return;
        const width=canvas.width,height=canvas.height,left=32,right=width-20,top=12,bottom=height-28;
        ctx.clearRect(0,0,width,height);ctx.fillStyle='#11151c';ctx.fillRect(0,0,width,height);
        ctx.strokeStyle='#303742';ctx.lineWidth=1;
        for(let i=0;i<=4;i++){const x=left+(right-left)*i/4,y=top+(bottom-top)*i/4;ctx.beginPath();ctx.moveTo(x,top);ctx.lineTo(x,bottom);ctx.moveTo(left,y);ctx.lineTo(right,y);ctx.stroke();}
        const display=draftTime===undefined?config:{...config,colors:config.colors?.map((c,i)=>i===selected?{...c,time:draftTime!}:c)};
        const channels=['#ff6f79','#7bd89c','#76aefb','#f4f4f5'];
        let max=1;
        for(const key of display.colors??[])max=Math.max(max,...key.value.map(Math.abs));
        for(let c=0;c<4;c++){
            ctx.strokeStyle=channels[c];ctx.lineWidth=c===3?2:1.5;ctx.beginPath();
            for(let i=0;i<=120;i++){const t=i/120,value=curveColor(display,t,0,display.alphas?.map(a=>(a.range.low+a.range.high)/2))[c];const x=left+t*(right-left),y=bottom-value/max*(bottom-top);if(i===0)ctx.moveTo(x,y);else ctx.lineTo(x,y);}ctx.stroke();
        }
        ctx.font='11px sans-serif';ctx.textAlign='center';ctx.fillStyle='#b9c0cc';for(let i=0;i<=4;i++)ctx.fillText(`${i*25}%`,left+(right-left)*i/4,height-8);
        for(let i=0;i<4;i++){const t=i===selected&&draftTime!==undefined?draftTime:times[i].time,x=left+t*(right-left);ctx.fillStyle=i===selected?'#e7b767':'#9aa6b7';ctx.beginPath();ctx.arc(x,top+7,5,0,Math.PI*2);ctx.fill();ctx.fillText(String(i+1),x,top+23);}
    };
    let dragging=false;
    const timeFrom=(event:PointerEvent)=>{const r=canvas.getBoundingClientRect();return Math.max(0,Math.min(1,((event.clientX-r.left)*canvas.width/r.width-32)/(canvas.width-52)));};
    canvas.addEventListener('pointerdown',event=>{
        const time=timeFrom(event);let distance=Infinity;for(let i=0;i<4;i++){const d=Math.abs(time-times[i].time);if(d<distance){selected=i;distance=d;}}
        if(!times[selected].field||times[selected].field!.readonly){actions.locate(times[selected].field?.key??system.fields[0]?.key??'');draw();return;}
        dragging=true;canvas.setPointerCapture(event.pointerId);canvas.focus();draftTime=time;draw();
    });
    canvas.addEventListener('pointermove',event=>{
        if(!dragging)return;draftTime=timeFrom(event);const f=times[selected].field!;
        actions.draft([{key:f.key,expectedValue:f.value,value:percentLexical(f.value,draftTime*100)}]);draw();
    });
    const end=(event:PointerEvent)=>{
        if(!dragging)return;dragging=false;const f=times[selected].field!;const time=draftTime??times[selected].time;
        if(canvas.hasPointerCapture(event.pointerId))canvas.releasePointerCapture(event.pointerId);
        actions.commit([{key:f.key,expectedValue:f.value,value:percentLexical(f.value,time*100)}]);draftTime=undefined;draw();
    };
    canvas.addEventListener('pointerup',end);canvas.addEventListener('pointercancel',()=>{dragging=false;draftTime=undefined;actions.cancel();draw();});
    canvas.tabIndex=0;canvas.addEventListener('keydown',e=>{if(e.key==='Escape'){dragging=false;draftTime=undefined;actions.cancel();draw();}});
    const slots=el('div','curve-slots');
    for(const t of times){const row=el('div','curve-slot');const input=el('input');input.type='number';input.min='0';input.max='100';input.step='0.1';input.value=String(t.time*100);input.disabled=!t.field||!!t.field.readonly;
        const apply=(commit:boolean)=>{if(!t.field)return;const edits=[{key:t.field.key,value:percentLexical(t.field.value,Number(input.value)),expectedValue:t.field.value}];(commit?actions.commit:actions.draft)(edits);};
        input.addEventListener('input',()=>apply(false));input.addEventListener('change',()=>apply(true));
        row.append(el('span','',`槽 ${t.slot}`),input,el('span','','%'));if(t.field)row.append(button('↗','定位颜色时间节点',()=>actions.locate(t.field!.key),'code-button'));slots.append(row);}
    panel.append(slots);
    const alphaRandom=config.alphas?.some(a=>a.range.low!==a.range.high);
    panel.append(el('p','hint',alphaRandom?'Alpha 含随机范围；白线为区间中值，实际取样由局部回放决定。':'Alpha 常量模式：编辑下方 Low/High，设常量将同时写入两端。'));
    if(system.definition){
        const alphaPath=childPath(system.definition,[],['Alphas']);const alphaNodes=alphaPath?at(system.definition,alphaPath)!.children:[];
        const alphaBox=el('div','alpha-tools');
        alphaNodes.forEach((node,index)=>{const slot=Number(node.attributes.Frame);if(slot<1||slot>4)return;
            const path=[...alphaPath!,index],low=fieldAt(system.fields,path,'Low'),high=fieldAt(system.fields,path,'High');if(!low||!high)return;
            const input=el('input');input.type='number';input.step='0.05';input.value=low.value;input.disabled=!!low.readonly||!!high.readonly;
            const b=button(`槽 ${slot} 设常量`,'将同一数值写入 Alpha Low 与 High',()=>actions.commit([{key:low.key,value:input.value,expectedValue:low.value},{key:high.key,value:input.value,expectedValue:high.value}]));b.disabled=input.disabled;
            const row=el('div','alpha-row');row.append(input,b);alphaBox.append(row);
        });panel.append(alphaBox);
        const sync=button('同步 Alpha 时间…','提交前确认将前四个 Alpha 时间同步为颜色时间',()=>{
            const changes:EditValue[]=[];
            alphaNodes.forEach((node,index)=>{const slot=Number(node.attributes.Frame);if(slot<1||slot>4)return;const f=fieldAt(system.fields,[...alphaPath!,index],'RelativeAge');if(f&&!f.readonly)changes.push({key:f.key,value:percentLexical(f.value,times[slot-1].time*100),expectedValue:f.value});});
            if(!changes.length)return;
            const existing=panel.querySelector('.sync-review');if(existing)existing.remove();
            const review=el('div','sync-review');review.append(el('strong','','确认同步 Alpha 时间'));
            changes.forEach((change,i)=>review.append(el('p','hint',`槽 ${i+1}：${change.expectedValue} → ${change.value}`)));
            review.append(button('应用一次编辑','保留其他字段，可通过一次撤销恢复',()=>{actions.commit(changes);review.remove();}),button('取消','保留当前 Alpha 时间',()=>review.remove()));panel.append(review);
        });sync.disabled=!alphaNodes.length;panel.append(sync);
    }
    panel.append(sizeTrend(config));draw();return panel;
}
function percentLexical(original:string,value:number):string{return `${Number(value.toFixed(4))}${original.trim().endsWith('%')?'%':''}`;}
function sizeTrend(config:ParticleConfig):HTMLElement {
    const block=el('div','size-trend');block.append(el('h4','','尺寸趋势 · 解析参数'));
    const canvas=el('canvas','size-canvas');canvas.width=600;canvas.height=90;const ctx=canvas.getContext('2d');
    if(ctx){ctx.fillStyle='#11151c';ctx.fillRect(0,0,600,90);const life=(config.lifetime.low+config.lifetime.high)/2,initial=(config.size.low+config.size.high)/2,rate=(config.sizeRate.low+config.sizeRate.high)/2,d=(config.sizeRateDamping.low+config.sizeRateDamping.high)/2;
        const values=Array.from({length:101},(_,i)=>initial+rate*integrateDamping(d,i/100*life));const max=Math.max(1,...values.map(Math.abs));ctx.strokeStyle='#e7b767';ctx.beginPath();values.forEach((v,i)=>{const x=10+i*5.8,y=76-v/max*60;if(i)ctx.lineTo(x,y);else ctx.moveTo(x,y);});ctx.stroke();}
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
