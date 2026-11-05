export function el<K extends keyof HTMLElementTagNameMap>(tag:K,className='',text?:string):HTMLElementTagNameMap[K] {
    const element=document.createElement(tag);element.className=className;if(text!==undefined)element.textContent=text;return element;
}
export function button(text:string,title:string,action:()=>void,className=''):HTMLButtonElement {
    const element=el('button',className,text);element.type='button';element.title=title;element.addEventListener('click',action);return element;
}
export function option(value:string,text:string):HTMLOptionElement {const o=el('option','',text);o.value=value;return o;}
export function label(text:string,control:HTMLElement):HTMLLabelElement {const l=el('label','control');l.append(el('span','',text),control);return l;}
export const localName=(name:string):string=>name.slice(name.lastIndexOf(':')+1);
