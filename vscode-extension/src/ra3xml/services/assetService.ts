import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { AssetIndex, AssetIndexOptions, AssetRecord, AssetGraph, IncludeEdge, filePath } from '../index/assetIndex';
import { DocumentStore, StoredDocumentRecord } from '../index/documentStore';
import { ReferenceRecord } from '../index/referenceIndex';
import { XmlDocument, XmlElement, parseXml } from '../core/xmlDocument';
import { TextPatch, EditConflictError, applyTextPatches } from '../core/editTransaction';
import { XsdModel } from '../model/xsdModel';
import { ManifestIndex } from '../index/manifestIndex';
import { SemanticCache } from '../index/cache';
export interface AssetServiceOptions extends AssetIndexOptions { storagePath?:string; schema?:XsdModel; }
export interface SemanticEditIntent {uri:string;documentVersion:number;start:number;end:number;expected:string;text:string;}
export interface EffectiveField {name:string;value:string;source:AssetRecord;nodePath:string;attribute?:string;start:number;end:number;inherited:boolean;certainty?:'effective'|'source';}
export interface AssetQuery {type?:string;id?:string;scopeUri?:string;}
/** Shared semantic session. FX owns only lightweight entry views and render resources. */
export class AssetService {
    readonly index:AssetIndex;
    readonly documents:DocumentStore;
    schema?:XsdModel;
    manifests?:ManifestIndex;
    completeness:'partial'|'complete'='partial';
    private readonly entries=new Map<string,AssetIndex>();
    private readonly listeners=new Set<()=>void>();
    private initialization?:Promise<void>;
    private scanning?:Promise<void>;
    private closed=false;
    private scanIncomplete=false;
    private cache?:SemanticCache<{files:string[];documents?:StoredDocumentRecord[]}>;
    private persistTimer?:ReturnType<typeof setTimeout>;
    private cacheFiles:string[]=[];
    private scanFinished=false;
    private scanGeneration=0;
    private viewId=0;
    private readonly dirtyEntries=new Set<string>();
    constructor(readonly options:AssetServiceOptions){
        this.schema=options.schema;this.scanIncomplete=!options.schema&&!options.sdkRoot;
        this.documents=options.documentStore??new DocumentStore({readText:options.readText,maxBytes:options.maxSourceBytes});
        this.index=new AssetIndex({...options,trustInvalidation:true,documentStore:this.documents,referenceType:(node,attribute)=>this.schema?.getReferenceType(node,attribute),
            referenceIsList:(node,attribute)=>this.schema?.isReferenceList(node,attribute)??false,assignableType:(actual,expected)=>this.schema?.isAssignable(actual,expected)??false});
    }
    onDidChange(listener:()=>void):{dispose():void}{this.listeners.add(listener);return{dispose:()=>this.listeners.delete(listener)};}
    private changed():void{for(const listener of this.listeners)listener();}
    private initialize():Promise<void>{
        return this.initialization??=(async()=>{
            if(this.options.sdkRoot){
                try{this.schema??=await XsdModel.load(this.options.sdkRoot);}catch{this.scanIncomplete=true;}
                try{this.manifests=await ManifestIndex.load(this.options.sdkRoot);}catch{this.scanIncomplete=true;}
                if(!this.schema?.complete||!this.manifests?.complete)this.scanIncomplete=true;
                for(const graph of this.index.shared.graphs.values())graph.references=undefined;
            }
            if(this.options.storagePath&&this.options.modRoot){
                const schemaVersion=this.schema?.version??'unconfigured';
                this.cache=new SemanticCache(this.options.storagePath,{modRoot:filePath(this.options.modRoot),sdkRoot:this.options.sdkRoot&&filePath(this.options.sdkRoot),schemaVersion});
                const cached=await this.cache.read();this.cacheFiles=cached?.files??[];if(cached?.documents)this.documents.restoreRecords(cached.documents);
            }
        })();
    }
    createView():AssetIndex{const view=this.index.createView();const key=`view:${++this.viewId}`;this.entries.set(key,view);const dispose=view.dispose.bind(view);view.dispose=()=>{this.entries.delete(key);dispose();};return view;}
    getDocument(uri:string):XmlDocument|undefined{return this.documents.get(uri);}
    setDocument(uri:string,text:string,version:number):void{if(this.documents.set(uri,text,version))this.invalidate(uri);}
    closeDocument(uri:string):void{this.documents.close(uri);this.invalidate(uri);}
    private sdkRelative(file:string):string|undefined {
        if(!this.options.sdkRoot)return;
        const selected=filePath(this.options.sdkRoot),physical=this.index.shared.physicalPaths.get(selected)??selected;
        for(const root of [selected,physical]){
            const relative=path.relative(root,file);
            if(relative!==".."&&!relative.startsWith(`..${path.sep}`)&&!path.isAbsolute(relative))return relative.replace(/\\/g,'/').toLowerCase();
        }
        return;
    }
    invalidate(uri:string):void {
        const file=this.documents.normalize(uri);
        const known=[...this.index.shared.graphs.values()].some(graph=>graph.dependencies.includes(file));
        const relative=this.sdkRelative(file);
        const metadata=this.schema?.files.includes(file)||this.manifests?.files.includes(file)
            ||relative!==undefined&&(/^(?:schemas\/.*\.xsd|builtmods\/.*\.manifest)$/.test(relative));
        const profile=relative==='tools/binaryassetbuilder.exe.config';
        if(!known&&/\.xml$/i.test(file)&&relative===undefined){
            this.scanning=undefined;this.scanFinished=false;this.completeness='partial';this.scanGeneration++;this.dirtyEntries.add(file);
        }
        for(const [entry,graph]of this.index.shared.graphs)if(graph.dependencies.includes(file)||graph.includes.some(edge=>edge.to===file))this.dirtyEntries.add(entry);
        if(metadata||profile){
            this.scanning=undefined;this.scanFinished=false;this.completeness='partial';this.scanGeneration++;
            if(metadata){this.initialization=undefined;this.schema=this.options.schema;this.manifests=undefined;this.scanIncomplete=false;}
            for(const entry of this.index.shared.graphs.keys())this.dirtyEntries.add(entry);
            this.index.shared.graphs.clear();
        }
        this.index.invalidate(file);this.changed();
    }
    async getScope(uri:string,_documentVersion?:number):Promise<AssetGraph>{return this.ensure(uri);}
    async ensure(uri:string):Promise<AssetGraph>{
        await this.initialize();if(this.closed)throw new Error('AssetService disposed');
        const file=await this.index.checkedFile(uri);
        for(const dirty of [...this.dirtyEntries]){try{await fs.access(dirty);}catch(error){if((error as NodeJS.ErrnoException).code==='ENOENT'&&!this.documents.hasOverlay(dirty)){this.index.shared.graphs.delete(dirty);this.entries.get(dirty)?.dispose();this.entries.delete(dirty);this.dirtyEntries.delete(dirty);continue;}}const view=this.entries.get(dirty)??this.index.createView();try{await view.load(dirty);this.entries.set(dirty,view);}catch{/* Failed inputs stay absent from current query results. */}this.dirtyEntries.delete(dirty);}
        let entry=file;
        if(this.options.modRoot){const candidate=path.join(filePath(this.options.modRoot),'Data','Mod.xml');try{await fs.access(candidate);entry=candidate;}catch{/* A standalone document still has local semantics. */}}
        const primary=this.entries.get(entry)??this.index.createView();this.entries.set(entry,primary);
        const graph=await primary.load(entry);
        if(!graph.documents.has(file)){const local=this.entries.get(file)??this.index.createView();this.entries.set(file,local);await local.load(file);}
        if(!this.scanning)this.scanning=this.scanProject().catch(()=>{this.scanIncomplete=true;this.completeness='partial';this.changed();});
        this.updateCompleteness();this.schedulePersist();return graph;
    }
    private schedulePersist():void{if(this.persistTimer)clearTimeout(this.persistTimer);this.persistTimer=setTimeout(()=>{this.persistTimer=undefined;void this.persistCache().catch(()=>undefined);},500);}
    async persistCache():Promise<void>{await this.cache?.write({files:this.cacheFiles,documents:this.documents.exportRecords()});}
    async whenComplete():Promise<void>{if(this.scanning)await this.scanning;}
    private updateCompleteness():void{if(!this.scanFinished)return;const incomplete=[...this.index.shared.graphs.values()].some(graph=>graph.issues.some(issue=>['source-budget','include-read','xml-syntax','include-path','include-contract','xinclude-contract'].includes(issue.code)));this.completeness=this.scanIncomplete||incomplete?'partial':'complete';}
    private async scanProject():Promise<void>{
        const generation=this.scanGeneration;const files=new Set<string>();const root=this.options.modRoot&&filePath(this.options.modRoot);
        const walk=async(directory:string):Promise<void>=>{
            if(this.closed)return;for(const entry of await fs.readdir(directory,{withFileTypes:true})){
                if(files.size>=(this.options.maxDocuments??5000)){this.scanIncomplete=true;return;}
                if(entry.isSymbolicLink())continue;
                const file=path.join(directory,entry.name);
                if(entry.isDirectory()){if(!/^(?:\.git|node_modules|SuperCompilerCache|out|test-results|\.vscode-test)$/i.test(entry.name))await walk(file);}
                else if(entry.isFile()&&/\.xml$/i.test(entry.name))files.add(file);
            }
        };
        if(root)await walk(root);
        for(const file of files){
            if(this.closed)return;
            if(root){const relative=path.relative(root,file);if(relative.startsWith('..')||path.isAbsolute(relative))continue;}
            if([...this.entries.values()].some(index=>index.graph.documents.has(file)))continue;
            try{const index=this.index.createView();await index.load(file);this.entries.set(file,index);}catch{this.scanIncomplete=true;}
        }
        if(generation!==this.scanGeneration)return;
        this.scanFinished=true;this.updateCompleteness();
        if(!this.closed){this.cacheFiles=[...files];await this.persistCache().catch(()=>undefined);this.changed();}
    }
    findAssets(query:AssetQuery={}):AssetRecord[]{
        const records=new Map<string,AssetRecord>();
        for(const graph of this.index.shared.graphs.values())for(const asset of graph.assets.values())records.set(asset.key,asset);
        if(!this.entries.size)for(const asset of this.index.graph.assets.values())records.set(asset.key,asset);
        const result=[...records.values()].filter(asset=>(!query.type||asset.type===query.type||this.schema?.isAssignable(asset.type,query.type))&&(!query.id||asset.id.toLowerCase()===query.id.toLowerCase()));
        if(!query.scopeUri)return result;
        const index=this.indexFor(query.scopeUri);if(!index)return [];
        return result.filter(asset=>index.candidates(asset.type,asset.id,asset.namespace,query.scopeUri).some(candidate=>candidate.key===asset.key));
    }
    private indexFor(uri:string):AssetIndex|undefined{const file=this.documents.normalize(uri);const graph=[...this.index.shared.graphs.values()].find(graph=>graph.documents.has(file));if(!graph)return;let view=this.entries.get(graph.entry);if(!view){view=this.index.createView();this.entries.set(graph.entry,view);}view.selectGraph(graph);return view;}
    getReferences(uri?:string):ReferenceRecord[]{
        const records=new Map<string,ReferenceRecord>();
        for(const graph of this.index.shared.graphs.values())for(const reference of graph.references?.records??[]){if(!uri||this.documents.normalize(reference.uri)===this.documents.normalize(uri))records.set(reference.key,reference);}
        for(const reference of records.values())if(!reference.targets.length&&!reference.dynamic){const id=reference.value.includes(':')?reference.value.slice(reference.value.indexOf(':')+1):reference.value;const prefix=reference.value.includes(':')?reference.value.slice(0,reference.value.indexOf(':')):undefined;reference.manifestTargets=(this.manifests?.findById(id)??[]).filter(asset=>(asset.type===reference.type||this.schema?.isAssignable(asset.type,reference.type))&&(!prefix||asset.type===prefix||prefix==='FXParticleSystem'&&asset.type==='FXParticleSystemTemplate'));}
        return [...records.values()];
    }
    resolveReference(uri:string,offset:number):ReferenceRecord|undefined{return this.getReferences(uri).find(reference=>reference.start<=offset&&offset<=reference.end);}
    findReferences(identity:string|AssetRecord):ReferenceRecord[]{const key=typeof identity==='string'?identity:identity.key;return this.getReferences().filter(reference=>reference.targets.includes(key));}
    private asset(identity:string|AssetRecord):AssetRecord|undefined{return typeof identity==='string'?this.findAssets().find(asset=>asset.key===identity):identity;}
    getInheritance(identity:string|AssetRecord):AssetRecord[]{
        const first=this.asset(identity);if(!first)return[];const chain:AssetRecord[]=[];const seen=new Set<string>();let current:AssetRecord|undefined=first;
        while(current&&!seen.has(current.key)){chain.push(current);seen.add(current.key);const parent=current.node.attributes.inheritFrom;if(!parent)break;const matches:AssetRecord[]=this.indexFor(current.uri)?.candidates(current.type,parent,current.namespace,current.scopeUri??current.uri)??[];current=matches.length===1?matches[0]:undefined;}
        return chain;
    }
    getEffectiveFields(identity:string|AssetRecord):EffectiveField[]{
        const chain=this.getInheritance(identity);const result=new Map<string,EffectiveField>();const selected=chain[0];
        for(const source of [...chain].reverse()){
            const walk=(node:XmlElement,route:string):void=>{
                for(const [attribute,token]of Object.entries(node.attributeTokens)){if(attribute==='id'||attribute==='inheritFrom'||attribute.startsWith('xmlns'))continue;const name=`${route}/@${attribute}`;result.set(route?`${name}\0${source.key}`:name,{name,value:token.value,source,nodePath:node.path,attribute,start:token.valueStart,end:token.valueEnd,inherited:source.key!==selected.key,certainty:route===''?'effective':'source'});}
                for(const token of node.textTokens)if(token.value.trim()){const name=`${route}/text()`;result.set(`${name}\0${source.key}`,{name,value:token.value.trim(),source,nodePath:node.path,start:token.start,end:token.end,inherited:source.key!==selected.key,certainty:'source'});}
                for(const child of node.children){const siblings=node.children.filter(candidate=>candidate.localName===child.localName);const ordinal=siblings.indexOf(child)+1;walk(child,`${route}/${child.localName}${child.attributes.id?`[@id=${child.attributes.id}]`:`[${ordinal}]`}`);}
            };walk(source.node,'');
        }
        return [...result.values()];
    }
    getIncludeDependencies(uri:string):IncludeEdge[]{const file=this.documents.normalize(uri);return this.indexFor(file)?.graph.includes.filter(edge=>edge.from===file)??[];}
    getPotentialDependents(identity:string|AssetRecord):AssetRecord[]{const asset=this.asset(identity);if(!asset)return[];const keys=new Set([asset.key]);const pending=[asset.key];while(pending.length)for(const reference of this.findReferences(pending.pop()!))if(reference.ownerKey&&!keys.has(reference.ownerKey)){keys.add(reference.ownerKey);pending.push(reference.ownerKey);}return this.findAssets().filter(record=>keys.has(record.key)&&record.key!==asset.key);}
    prepareEdits(intents:SemanticEditIntent[]):Map<string,TextPatch[]>{
        if(!intents.length||intents.length>4096)throw new Error('编辑事务为空或超过预算。');
        const result=new Map<string,TextPatch[]>();
        for(const intent of intents){const uri=this.documents.normalize(intent.uri);const document=this.documents.get(uri);if(!document||document.shallow||!document.valid||document.version!==intent.documentVersion)throw new EditConflictError();if(this.index.isReadonly(uri))throw new Error('SDK 来源只读，请先复制到 MOD。');const patches=result.get(uri)??[];patches.push({start:intent.start,end:intent.end,expected:intent.expected,text:intent.text});result.set(uri,patches);}
        for(const [uri,patches]of result){const document=this.documents.get(uri)!;const text=applyTextPatches(document.text,patches);if(!parseXml(text,uri,document.version).valid)throw new EditConflictError('修改会产生无效 XML。');}
        return result;
    }
    dispose():void{if(this.closed)return;this.closed=true;if(this.persistTimer)clearTimeout(this.persistTimer);for(const entry of [...this.entries.values()])entry.dispose();this.entries.clear();this.index.dispose();this.index.shared.graphs.clear();this.index.shared.pending.clear();this.index.shared.physicalPaths.clear();this.documents.dispose();this.listeners.clear();}
}
const services=new Map<string,AssetService>();
function sessionKey(options:AssetServiceOptions):string{return JSON.stringify([filePath(options.modRoot??options.roots[0]),options.sdkRoot?filePath(options.sdkRoot):''].map(value=>process.platform==='win32'?value.toLowerCase():value));}
export function getAssetService(options:AssetServiceOptions):AssetService{const key=sessionKey(options);let service=services.get(key);if(!service){service=new AssetService(options);services.set(key,service);}return service;}
export const acquireAssetService=getAssetService;
export function releaseAssetService(service:AssetService):void{for(const [key,current]of services)if(current===service)services.delete(key);service.dispose();}
export function disposeAssetServices():void{for(const service of services.values())service.dispose();services.clear();}
