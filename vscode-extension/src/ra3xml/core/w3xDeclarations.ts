import { createReadStream } from 'node:fs';
import { parseXml, XmlDocument } from './xmlDocument';
/** Scan tags with bounded buffering; geometry text/vertices never enter the CST. */
export async function scanW3xDeclarations(uri:string,version=0):Promise<XmlDocument>{
    let buffer='',base=0,depth=0,root='',rootStart=0,rootClose=0,total=0;
    const declarations:{open:string;start:number;openEnd:number;end:number;closeStart:number}[]=[];
    const stack:(number|undefined)[]=[];
    const consume=(tag:string,start:number,end:number):void=>{
        if(/^<\?|^<!--|^<!\[CDATA\[/.test(tag))return;
        if(/^<!/.test(tag))throw new Error('W3X 声明浅扫描不展开 DTD。');
        if(/^<\//.test(tag)){depth--;const selected=stack.pop();if(selected!==undefined){declarations[selected].end=end;declarations[selected].closeStart=start;}if(depth===0)rootClose=start;return;}
        const selfClosing=/\/\s*>$/.test(tag);
        if(depth===0){root=tag;rootStart=start;}
        let selected:number|undefined;
        if(depth===1){if(declarations.length>=20000)throw new Error('W3X 顶层声明超过预算。');selected=declarations.length;declarations.push({open:tag,start,openEnd:end,end,closeStart:end-2});}
        if(!selfClosing){depth++;stack.push(selected);}
    };
    for await(const chunk of createReadStream(uri,{encoding:'utf8',highWaterMark:64*1024})){
        buffer+=chunk;total+=chunk.length;let cursor=0;
        for(;;){const start=buffer.indexOf('<',cursor);if(start<0){base+=buffer.length;buffer='';break;}
            let end=-1;
            if(buffer.startsWith('<!--',start)){const at=buffer.indexOf('-->',start+4);if(at>=0)end=at+3;}
            else if(buffer.startsWith('<![CDATA[',start)){const at=buffer.indexOf(']]>',start+9);if(at>=0)end=at+3;}
            else{let quote='';for(let index=start+1;index<buffer.length;index++){const char=buffer[index];if(quote){if(char===quote)quote='';}else if(char==='"'||char==="'")quote=char;else if(char==='>'){end=index+1;break;}}}
            if(end<0){base+=start;buffer=buffer.slice(start);if(buffer.length>128*1024)throw new Error('W3X XML token 超过浅扫描预算。');break;}
            consume(buffer.slice(start,end),base+start,base+end);cursor=end;
            if(cursor===buffer.length){base+=buffer.length;buffer='';break;}
        }
    }
    if(depth!==0||!root||buffer.includes('<'))throw new Error('W3X 顶层 XML 声明不完整。');
    const rootName=/^<([^\s/>]+)/.exec(root)?.[1];if(!rootName)throw new Error('W3X 缺少根元素。');
    if(/\/\s*>$/.test(root))return{...parseXml(root,uri,version),shallow:true,sourceBytes:total};
    const opens=declarations.map(value=>value.open.replace(/\/?\s*>$/, '/>'));
    const skeleton=root+opens.join('')+`</${rootName}>`;const document=parseXml(skeleton,uri,version);
    const rootNode=document.roots[0];
    for(let index=0;index<rootNode.children.length;index++){
        const node=rootNode.children[index],source=declarations[index],shift=source.start-node.start;
        for(const token of Object.values(node.attributeTokens)){token.start+=shift;token.end+=shift;token.valueStart+=shift;token.valueEnd+=shift;}
        node.start=source.start;node.openEnd=source.openEnd;node.end=source.end;node.closeStart=source.closeStart;node.selfClosing=/\/\s*>$/.test(source.open);
    }
    rootNode.start=rootStart;rootNode.end=total;rootNode.closeStart=rootClose;
    return {...document,shallow:true,sourceBytes:total};
}
