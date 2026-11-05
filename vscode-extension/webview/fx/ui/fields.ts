import type { FxField } from '../../../src/fx/previewProtocol';
import { button,el,option } from './dom';
export interface EditValue {key:string;value:string;expectedValue:string;}
export interface FieldActions {draft:(edits:EditValue[])=>void;commit:(edits:EditValue[])=>void;cancel:()=>void;locate:(key:string)=>void;openSource?:(key:string)=>void;override?:(edit:EditValue)=>void;}
export function fieldRow(field:FxField,actions:FieldActions):HTMLElement {
    const row=el('div','field-row');row.dataset.fieldKey=field.key;
    const name=el('label','field-label',field.label);
    const input=field.kind==='enum'||field.kind==='boolean'?el('select'):el('input');
    input.id=`field-${field.key.replace(/[^\w-]/g,'_')}`;name.htmlFor=input.id;
    if(input instanceof HTMLSelectElement){for(const c of field.choices??(field.kind==='boolean'?['true','false']:[field.value]))input.append(option(c,c));if(!Array.from(input.options).some(o=>o.value===field.value))input.append(option(field.value,field.value));}
    else {input.type='text';input.inputMode=field.kind==='number'?'decimal':'text';input.autocomplete='off';input.spellcheck=false;}
    input.value=field.value;input.disabled=!!field.readonly;
    input.title=[field.value,field.uri,field.inherited?'继承字段：编辑原定义':'当前定义'].join('\n');
    const edits=()=>[{key:field.key,value:input.value,expectedValue:field.value}];
    let committed=false;
    input.addEventListener('input',()=>{committed=false;input.classList.add('draft');actions.draft(edits());});
    const commit=()=>{if(field.inherited)return;if(!committed&&input.value!==field.value){committed=true;actions.commit(edits());}input.classList.remove('draft');};
    input.addEventListener('change',commit);input.addEventListener('blur',commit);
    input.addEventListener('keydown',rawEvent=>{
        const event=rawEvent as KeyboardEvent;
        if(event.key==='Enter'){event.preventDefault();commit();input.blur();}
        else if(event.key==='Escape'){event.preventDefault();input.value=field.value;committed=true;input.classList.remove('draft');actions.cancel();input.blur();}
    });
    const code=button('↗','定位此参数在原生 XML 中的位置',()=>actions.locate(field.key),'code-button');code.setAttribute('aria-label',`定位 ${field.label}`);
    row.append(name,input,code);
    if(['ParticleTexture','EnvironmentTexture','File'].includes(field.attribute))row.append(el('small','field-source','引用提交后加载'));
    if(field.readonly||field.inherited){const badge=el('small','field-source',field.readonly?'SDK / 只读':`继承自 ${field.sourceAsset??'父定义'}`);badge.title=field.uri;row.append(badge);}
    if(field.defaulted)row.append(el('small','field-source','默认值；首次编辑会按原位置插入属性'));
    if(field.inherited){const source=el('div','inherited-actions');source.append(button('修改父定义','将可视编辑器和原生 XML 切到该共享定义',()=>actions.openSource?.(field.key)),button('在本资产覆盖','预览并确认新覆盖节点，保留父定义原文',()=>actions.override?.({key:field.key,value:input.value,expectedValue:field.value})));row.append(source);}
    return row;
}
export function fieldsPanel(fields:FxField[],actions:FieldActions):HTMLElement {
    const panel=el('div','field-groups'),groups=new Map<string,FxField[]>();
    for(const field of fields){const group=field.group??'当前资产';if(!groups.has(group))groups.set(group,[]);groups.get(group)!.push(field);}
    for(const [name,items] of groups){const details=el('details','field-group');details.open=groups.size<5||/寿命|数量|发射|基础/.test(name);details.append(el('summary','',name));for(const field of items)details.append(fieldRow(field,actions));panel.append(details);}
    if(!fields.length)panel.append(el('p','hint','此节点没有可视编辑字段；可打开 XML 查看完整结构。'));
    return panel;
}
