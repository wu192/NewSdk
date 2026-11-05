import type { ManifestAsset } from './manifestIndex';
import type { XmlElement } from '../core/xmlDocument';
import type { AssetGraph, AssetRecord } from './assetIndex';
export interface ReferenceRecord {
    key:string;uri:string;scopeUri:string;nodePath:string;attribute?:string;value:string;type:string;
    start:number;end:number;documentVersion:number;ownerKey?:string;targets:string[];dynamic:boolean;ambiguous:boolean;manifestTargets?:ManifestAsset[];
}
export type ReferenceTypeResolver=(node:XmlElement,attribute?:string)=>string|undefined;
/** FX rules are semantic extensions; no renderer data enters this graph. */
export function fxReferenceType(node:XmlElement,attribute?:string):string|undefined {
    if(attribute==='ParticleTexture' || attribute==='EnvironmentTexture'&&node.localName==='Swarm')return 'Texture';
    if(attribute==='PerParticleAttachedSystem'||attribute==='Particle'&&node.localName==='ParticleSystem')return 'FXParticleSystemTemplate';
    if(attribute==='EventFX'||attribute==='FX'&&node.localName==='FXListAtBonePos')return 'FXList';
    return;
}
export class ReferenceIndex {
    readonly records:ReferenceRecord[]=[];
    private readonly incoming=new Map<string,ReferenceRecord[]>();
    rebuild(graph:AssetGraph,candidates:(type:string,value:string,namespace:string,scopeUri:string)=>AssetRecord[],resolveType?:ReferenceTypeResolver,isList?:(node:XmlElement,attribute?:string)=>boolean):void {
        this.records.length=0;this.incoming.clear();
        const seen=new Set<string>();
        for(const owner of graph.assets.values()){
            const visit=(node:XmlElement,uri:string,scopeUri:string,stack:string[]):void=>{
                const include=graph.includes.find(edge=>edge.mode==='xml'&&edge.from===uri&&edge.source.nodePath===node.path);
                if(include){if(!stack.includes(include.to))for(const root of graph.documents.get(include.to)?.roots??[])visit(root,include.to,scopeUri,[...stack,include.to]);return;}
                const document=graph.documents.get(uri);if(!document)return;
                const add=(value:string,type:string,start:number,end:number,attribute?:string,forcedDynamic=false):void=>{
                    if(!value.trim())return;
                    const key=JSON.stringify([uri,start,end,scopeUri,type,owner.key]);if(seen.has(key))return;seen.add(key);
                    const dynamic=forcedDynamic||/[$=]/.test(value);const targets=dynamic?[]:candidates(type,value,owner.namespace,scopeUri).map(target=>target.key);
                    const reference:ReferenceRecord={key,uri,scopeUri,nodePath:node.path,attribute,value,type,start,end,documentVersion:document.version,ownerKey:owner.key,targets,dynamic,ambiguous:targets.length>1};
                    this.records.push(reference);for(const target of targets){const incoming=this.incoming.get(target)??[];incoming.push(reference);this.incoming.set(target,incoming);}
                };
                for(const [attribute,token]of Object.entries(node.attributeTokens)){
                    const type=attribute==='inheritFrom'&&node===owner.node?owner.type:resolveType?.(node,attribute)??fxReferenceType(node,attribute);
                    if(type){if(isList?.(node,attribute)&&!token.rawValue.includes('&')){for(const match of token.rawValue.matchAll(/\S+/g))add(match[0],type,token.valueStart+match.index!,token.valueStart+match.index!+match[0].length,attribute);}else add(token.value,type,token.valueStart,token.valueEnd,attribute,token.rawValue!==token.value);}
                }
                const textType=resolveType?.(node);
                if(textType)for(const token of node.textTokens){const value=token.value.trim();if(!value)continue;const raw=document.text.slice(token.start,token.end);const leading=raw.length-raw.trimStart().length;if(isList?.(node)&&!raw.includes('&')){for(const match of raw.matchAll(/\S+/g))add(match[0],textType,token.start+match.index!,token.start+match.index!+match[0].length);}else add(value,textType,token.start+leading,token.end-(raw.length-raw.trimEnd().length),undefined,raw.trim()!==value);}
                for(const child of node.children)visit(child,uri,scopeUri,stack);
            };visit(owner.node,owner.uri,owner.scopeUri??owner.uri,[owner.uri]);
        }
    }
    findReferences(key:string):ReferenceRecord[]{return this.incoming.get(key)??[];}
    at(uri:string,offset:number):ReferenceRecord|undefined{return this.records.find(record=>record.uri===uri&&record.start<=offset&&offset<=record.end);}
    dependents(key:string):Set<string>{const result=new Set([key]);const pending=[key];while(pending.length)for(const ref of this.findReferences(pending.pop()!))if(ref.ownerKey&&!result.has(ref.ownerKey)){result.add(ref.ownerKey);pending.push(ref.ownerKey);}return result;}
}
