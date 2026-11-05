import type { FxScene } from '../../../src/fx/previewProtocol';
import { parseSourceMotion, type SourceMotion } from '../runtime/sourceMotion';
import { button, el, label, option } from './dom';

export const motionPlaybackLabels: Record<SourceMotion['playback'], string> = {once:'单次后停留', loop:'闭合循环', pingPong:'往返'};

function presetMotion(preset: string): SourceMotion {
    if (preset === 'circle') {
        const points = Array.from({length:25}, (_, index) => {
            const angle = index / 24 * Math.PI * 2;
            return {frame:index * 5, offset:(index === 0 || index === 24 ? [0,0,0] : [50 * (1 - Math.cos(angle)),50 * Math.sin(angle),0]) as [number,number,number]};
        });
        return {playback:'loop', points};
    }
    return {playback:'pingPong', points:[{frame:0,offset:[0,0,0]},{frame:60,offset:[100,0,0]}]};
}

function formatMotion(motion: SourceMotion): string {
    return motion.points.map(point => [Number((point.frame / 30).toFixed(6)), ...point.offset].join(' ')).join('\n');
}

/** Text is kept local until Apply; every transmitted path uses the shared validator. */
export function sourceMotionPanel(scene: FxScene, apply: (motion: SourceMotion | undefined) => void): HTMLElement {
    const panel = el('section','source-motion');
    panel.append(el('h3','','预览发射源轨迹（合成输入）'),el('p','hint','用户显式设置的合成输入，仅改变根系统各次出生的源位置；已有粒子继续自身运动，不自动继承源速度。源朝向沿用下方设置。此输入不代表游戏普通 FXList 必然跟随对象。'));
    const enabled = el('input'); enabled.type='checkbox'; enabled.id='source-motion-enabled'; enabled.checked=!!scene.sourceMotion;
    panel.append(label('移动发射源',enabled));
    const preset = el('select'); preset.id='source-motion-preset'; preset.setAttribute('aria-label','发射源轨迹预设');
    preset.append(option('','选择预设…'),option('line','直线往返 · 2 秒 / 单程 100'),option('circle','闭合环形 · 4 秒 / 半径 50'));
    panel.append(label('快速试用',preset));
    const playback = el('select'); playback.id='source-motion-playback'; playback.setAttribute('aria-label','轨迹播放模式');
    for (const mode of ['once','loop','pingPong'] as const) playback.append(option(mode,motionPlaybackLabels[mode]));
    const points = el('textarea','source-motion-points'); points.id='source-motion-points'; points.rows=6; points.spellcheck=false; points.maxLength=16_384;
    points.setAttribute('aria-label','轨迹点（秒 X Y Z）'); points.setAttribute('aria-describedby','source-motion-help source-motion-error');
    const help=el('p','hint','每行：秒 X Y Z（相对源位置），点间线性插值。2–64 点，首行为 0 0 0 0。时间按 30 Hz 对齐到最近逻辑帧（1/30 秒），对齐后须严格递增，最长 600 秒。闭合循环末点必须回到 0 0 0。'); help.id='source-motion-help';
    const error=el('p','source-motion-feedback'); error.id='source-motion-error'; error.setAttribute('role','status');
    const initial=scene.sourceMotion??presetMotion('line'); playback.value=initial.playback; points.value=formatMotion(initial);
    const report=(message:string,invalid:boolean):void=>{error.textContent=message;error.classList.toggle('error',invalid);points.setAttribute('aria-invalid',String(invalid));};
    const parseText=():SourceMotion|undefined=>{
        if(points.value.length>16_384){report('轨迹文本最多 16384 个字符。',true);return;}
        const lines=points.value.trim().split(/\r?\n/).filter(line=>line.trim());
        if(lines.length<2||lines.length>64){report('请输入 2–64 行轨迹点。',true);return;}
        const numberPattern=/^[+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?$/i;
        const parsed:SourceMotion['points']=[];
        for(let index=0;index<lines.length;index++){
            const cells=lines[index].trim().split(/\s+/);
            if(cells.length!==4||cells.some(cell=>!numberPattern.test(cell))){report(`第 ${index+1} 行需四个有限数值：秒 X Y Z。`,true);return;}
            const values=cells.map(Number),frame=Math.round(values[0]*30);
            if(values.some(value=>!Number.isFinite(value))||values[0]<0||values[0]>600||values.slice(1).some(value=>Math.abs(value)>1e6)){report(`第 ${index+1} 行超出范围：时间 0–600 秒，坐标绝对值不超过 1000000。`,true);return;}
            if(index===0&&(values[0]!==0||values.slice(1).some(value=>value!==0))){report('首点必须为 0 0 0 0。',true);return;}
            if(index>0&&frame<=parsed[index-1].frame){report(`第 ${index+1} 行时间须在 30 Hz 对齐后仍严格递增。`,true);return;}
            parsed.push({frame,offset:values.slice(1) as [number,number,number]});
        }
        const motion=parseSourceMotion({playback:playback.value,points:parsed});
        if(!motion){report(playback.value==='loop'?'闭合循环的末点必须回到 0 0 0；可选择往返或单次后停留。':'轨迹无效，请检查播放模式与各点。',true);return;}
        return motion;
    };
    const commit=():void=>{
        const motion=parseText();if(!motion)return;
        enabled.checked=true;points.value=formatMotion(motion);report(`已应用 ${motion.points.length} 点 · ${motionPlaybackLabels[motion.playback]} · 时间已按 30 Hz 对齐。`,false);apply(motion);
    };
    enabled.addEventListener('change',()=>{if(enabled.checked){const motion=parseText();if(motion){points.value=formatMotion(motion);report('已启用合成发射源轨迹。',false);apply(motion);}else enabled.checked=false;}else{report('已切换为静止发射源。',false);apply(undefined);}});
    preset.addEventListener('change',()=>{if(!preset.value)return;const motion=presetMotion(preset.value);playback.value=motion.playback;points.value=formatMotion(motion);enabled.checked=true;report('已应用轨迹预设；可继续编辑轨迹点。',false);apply(motion);preset.value='';});
    points.addEventListener('input',()=>{report('轨迹尚未应用。',false);});
    playback.addEventListener('change',()=>report('播放模式尚未应用。',false));
    const actions=el('div','source-motion-actions');
    const applyButton=button('应用轨迹','验证并保存此合成预览输入',commit,'primary');applyButton.id='source-motion-apply';
    const resetButton=button('重置为静止','清除轨迹并恢复静止发射源',()=>{enabled.checked=false;const motion=presetMotion('line');playback.value=motion.playback;points.value=formatMotion(motion);report('已重置为静止发射源。',false);apply(undefined);});resetButton.id='source-motion-reset';
    actions.append(applyButton,resetButton);panel.append(label('播放模式',playback),label('自定义轨迹点',points),help,actions,error);
    return panel;
}
