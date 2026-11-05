import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import { scanW3xDeclarations } from '../core/w3xDeclarations';
import { parseXml, XmlDocument, XmlElement, XmlIssue } from '../core/xmlDocument';

export interface TextSnapshot { text: string; version: number; }
export interface DocumentStoreOptions {
    readText?: (uri: string) => Promise<TextSnapshot | string>;
    maxDocumentBytes?: number; maxBytes?:number;
}
export interface StoredDocumentRecord {uri:string;stamp:string;version:number;text:string;bytes:number;shallow?:boolean;sourceBytes?:number;issues:XmlIssue[];nodes:(Omit<XmlElement,'parent'|'children'> & {parentPath?:string})[];}
interface RecordEntry { document: XmlDocument; stamp: string; revision: number; bytes: number; }
export function documentPath(uri: string): string { return path.resolve(uri.startsWith('file:') ? fileURLToPath(uri) : uri); }
/** One parsed source record per physical file. Editor overlays always outrank disk. */
export class DocumentStore {
    private readonly aliases=new Map<string,string>();
    normalize(uri:string):string{const file=documentPath(uri);return this.aliases.get(file)??file;}
    alias(uri:string,physical:string):void{const source=documentPath(uri),target=documentPath(physical);if(source===target)return;this.aliases.set(source,target);const overlay=this.overlays.get(source);if(overlay){this.overlays.delete(source);this.overlays.set(target,overlay);this.invalidate(target);}const record=this.records.get(source);if(record)this.cachedBytes-=record.bytes;this.records.delete(source);}
    private readonly records = new Map<string, RecordEntry>();
    private readonly overlays = new Map<string, TextSnapshot>();
    private readonly revisions = new Map<string, number>();
    private reads = 0;
    private parses = 0;
    private cachedBytes=0;
    hasOverlay(uri:string):boolean{return this.overlays.has(this.normalize(uri));}
    private generation=0;
    get epoch():number{return this.generation;}
    constructor(readonly options: DocumentStoreOptions = {}) {}
    set(uri: string, text: string, version: number): boolean {
        const file = this.normalize(uri); const previous = this.overlays.get(file);
        if (previous?.text === text && previous.version === version) return false;
        this.overlays.set(file, {text,version}); this.invalidate(file); return true;
    }
    close(uri: string): void { const file = this.normalize(uri); if (this.overlays.delete(file)) this.invalidate(file); }
    invalidate(uri: string): void { const file = this.normalize(uri); this.revisions.set(file,(this.revisions.get(file) ?? 0)+1);this.generation++; }
    get(uri: string): XmlDocument | undefined { const file=this.normalize(uri),record=this.records.get(file),overlay=this.overlays.get(file);if(!record||record.revision!==(this.revisions.get(file)??0)||overlay&&(overlay.version!==record.document.version||overlay.text!==record.document.text))return;return record.document; }
    get statistics(): { reads: number; parses: number; documents: number; bytes: number } {
        return {reads:this.reads,parses:this.parses,documents:this.records.size,bytes:this.cachedBytes};
    }
    async read(uri: string, verifyContent = false): Promise<XmlDocument> {
        const file = this.normalize(uri); const revision = this.revisions.get(file) ?? 0; const overlay = this.overlays.get(file);
        let stamp = overlay ? `overlay:${overlay.version}` : '';
        if (!overlay) { try { const stat=await fs.stat(file,{bigint:true}); stamp=`${stat.mtimeNs}:${stat.ctimeNs}:${stat.size}`;
            if (!/\.w3x$/i.test(file) && stat.size > BigInt(this.options.maxDocumentBytes ?? 16*1024*1024)) throw new Error('XML 超过单文档解析预算。'); } catch(error) {if(!this.options.readText || (error as NodeJS.ErrnoException).code!=='ENOENT')throw error;stamp=`virtual:${revision}`;} }
        const previous=this.records.get(file);
        if(previous && previous.revision===revision && previous.stamp===stamp && (!verifyContent || !!overlay)) return previous.document;
        if(!overlay&&/\.w3x$/i.test(file)){this.reads++;this.parses++;const document=await scanW3xDeclarations(file,revision);const bytes=Buffer.byteLength(document.text);if(this.cachedBytes-(previous?.bytes??0)+bytes>(this.options.maxBytes??48*1024*1024))throw new Error('XML 项目源码记录预算已达上限。');this.cachedBytes+=bytes-(previous?.bytes??0);this.records.set(file,{document,stamp,revision,bytes});return document;}
        let snapshot:TextSnapshot;
        if(overlay) snapshot=overlay;
        else { this.reads++; const read=this.options.readText ? await this.options.readText(file) : await fs.readFile(file,'utf8'); snapshot=typeof read==='string'?{text:read,version:revision}:read; }
        const bytes=Buffer.byteLength(snapshot.text,'utf8');
        if(bytes>(this.options.maxDocumentBytes??16*1024*1024)) throw new Error('XML 超过单文档解析预算。');
        if(this.cachedBytes-(previous?.bytes??0)+bytes>(this.options.maxBytes??48*1024*1024))throw new Error('XML 项目源码记录预算已达上限。');
        const document=previous && previous.document.text===snapshot.text && previous.document.version===snapshot.version
            ? previous.document : parseXml(snapshot.text,file,snapshot.version);
        if(document!==previous?.document)this.parses++;
        this.cachedBytes+=bytes-(previous?.bytes??0);this.records.set(file,{document,stamp,revision,bytes}); return document;
    }
    exportRecords(maxBytes=4*1024*1024):StoredDocumentRecord[]{
        const result:StoredDocumentRecord[]=[];let bytes=0;
        for(const [uri,record]of [...this.records].reverse()){
            if(this.overlays.has(uri)||record.revision!==(this.revisions.get(uri)??0))continue;
            const document=record.document;
            const nodes=document.elements.map(node=>{const {parent,children,...data}=node;return {...data,parentPath:parent?.path};});
            const value:StoredDocumentRecord={uri,stamp:record.stamp,version:document.version,text:document.text,bytes:record.bytes,shallow:document.shallow,sourceBytes:document.sourceBytes,issues:document.issues,nodes};
            const size=Buffer.byteLength(JSON.stringify(value));if(size>maxBytes||bytes+size>maxBytes)continue;bytes+=size;result.push(value);if(result.length>=512)break;
        }
        return result;
    }
    restoreRecords(records:StoredDocumentRecord[]):void{
        for(const record of records){
            if(!record||typeof record.uri!=='string'||typeof record.text!=='string'||!Array.isArray(record.nodes)||record.nodes.length>100000||this.overlays.has(documentPath(record.uri)))continue;
            const file=documentPath(record.uri);const elements:XmlElement[]=record.nodes.map(({parentPath,...node})=>({...node,children:[]}));const byPath=new Map(elements.map(node=>[node.path,node]));const roots:XmlElement[]=[];
            for(let index=0;index<elements.length;index++){const node=elements[index],parentPath=record.nodes[index].parentPath;const parent=parentPath?byPath.get(parentPath):undefined;node.parent=parent;if(parent)parent.children.push(node);else roots.push(node);}
            const document:XmlDocument={uri:file,text:record.text,version:record.version,shallow:record.shallow,sourceBytes:record.sourceBytes,roots,elements,issues:record.issues,valid:record.issues.length===0,find:nodePath=>byPath.get(nodePath),elementAt:offset=>{let found:XmlElement|undefined;for(const node of elements)if(node.start<=offset&&offset<node.end&&(!found||node.start>=found.start))found=node;return found;}};
            if(this.cachedBytes+record.bytes>(this.options.maxBytes??48*1024*1024))continue;this.cachedBytes+=record.bytes-(this.records.get(file)?.bytes??0);
            this.records.set(file,{document,stamp:record.stamp,revision:this.revisions.get(file)??0,bytes:record.bytes});
        }
    }
    dispose(): void {this.records.clear();this.cachedBytes=0;this.overlays.clear();this.revisions.clear();this.aliases.clear();}
}
