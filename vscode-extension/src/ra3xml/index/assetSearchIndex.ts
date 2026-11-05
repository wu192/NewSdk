export type AssetSource = 'mod' | 'sdk-xml' | 'manifest';
export interface AssetSearchResult {
    key:string; type:string; id:string; source:AssetSource; uri:string; relativePath:string;
    referenceCount:number; readonly:boolean; location?:{start:number;end:number};
}
export interface AssetSearchQuery {text?:string;type?:string;source?:AssetSource;directory?:string;offset?:number;limit?:number;}
export interface AssetSearchPage {items:AssetSearchResult[];total:number;offset:number;limit:number;indexVersion:number;completeness:'partial'|'complete';}
/** Derived lookup keys over the shared semantic records; never reads/parses source files. */
export class AssetSearchIndex {
    private records:AssetSearchResult[]=[];
    private readonly byType=new Map<string,Set<number>>();
    private readonly byPrefix=new Map<string,Set<number>>();
    private readonly byTrigram=new Map<string,Set<number>>();
    reset(records:AssetSearchResult[]):void {
        this.records=records.sort((a,b)=>a.type.localeCompare(b.type)||a.id.localeCompare(b.id)||a.uri.localeCompare(b.uri));
        this.byType.clear();this.byPrefix.clear();this.byTrigram.clear();
        const add=(map:Map<string,Set<number>>,key:string,index:number):void=>{const bucket=map.get(key)??new Set();bucket.add(index);map.set(key,bucket);};
        this.records.forEach((record,index)=>{
            add(this.byType,record.type.toLowerCase(),index);
            const id=record.id.toLowerCase();for(let length=1;length<=Math.min(id.length,32);length++)add(this.byPrefix,id.slice(0,length),index);
            for(const trigram of new Set(Array.from({length:Math.max(0,id.length-2)},(_,offset)=>id.slice(offset,offset+3))))add(this.byTrigram,trigram,index);
        });
    }
    query(query:AssetSearchQuery={}):{items:AssetSearchResult[];total:number;offset:number;limit:number} {
        const text=(query.text??'').trim();const colon=text.indexOf(':');
        const type=(query.type??(colon>=0?text.slice(0,colon):'')).toLowerCase();
        const id=(colon>=0?text.slice(colon+1):text).toLowerCase();
        let candidates:Set<number>|undefined=type?this.byType.get(type)??new Set():undefined;
        // Substring matching uses a small indexed candidate set for >=3 characters.
        // A prefix query also benefits from the prefix map for short IDs.
        if(id.length>=3){const trigrams=Array.from({length:id.length-2},(_,offset)=>this.byTrigram.get(id.slice(offset,offset+3))??new Set<number>());const smallest=trigrams.reduce((a,b)=>a.size<=b.size?a:b);candidates=candidates?new Set([...smallest].filter(index=>candidates!.has(index))):smallest;}
        const directory=query.directory?.replace(/\\/g,'/').replace(/\/$/,'');
        const matches=(candidates?[...candidates]:this.records.map((_,index)=>index)).filter(index=>{
            const record=this.records[index];return (!id||record.id.toLowerCase().includes(id))&&(!query.source||record.source===query.source)
                &&(!directory||record.relativePath===directory||record.relativePath.startsWith(`${directory}/`));
        });
        // Prefer full IDs and prefixes while retaining all partial-ID matches.
        if(id)matches.sort((a,b)=>{const rank=(index:number)=>this.records[index].id.toLowerCase()===id?0:this.byPrefix.get(id.slice(0,32))?.has(index)?1:2;return rank(a)-rank(b)||a-b;});
        const offset=Number.isSafeInteger(query.offset)?Math.max(0,query.offset!):0;
        const limit=Number.isSafeInteger(query.limit)?Math.max(1,Math.min(500,query.limit!)):100;
        return {items:matches.slice(offset,offset+limit).map(index=>this.records[index]),total:matches.length,offset,limit};
    }
}
