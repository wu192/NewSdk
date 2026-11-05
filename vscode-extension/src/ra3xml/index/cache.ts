import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { createHash } from 'node:crypto';
/** Derived records only. Cache roots are supplied by extension global storage. */
export class SemanticCache<T> {
    private readonly file:string;
    constructor(storagePath:string, identity:{modRoot:string;sdkRoot?:string;schemaVersion:string}) {
        const key=createHash('sha256').update(JSON.stringify(identity)).digest('hex');this.file=path.join(storagePath,'ra3xml',`${key}.json`);
    }
    async read():Promise<T|undefined>{try{const stat=await fs.stat(this.file);if(stat.size>8*1024*1024)return;const value=JSON.parse(await fs.readFile(this.file,'utf8')) as {version:number;records:T};return value.version===1?value.records:undefined;}catch{return;}}
    async write(records:T):Promise<void>{const json=JSON.stringify({version:1,records});if(Buffer.byteLength(json)>8*1024*1024)return;await fs.mkdir(path.dirname(this.file),{recursive:true});const temporary=`${this.file}.${process.pid}.tmp`;await fs.writeFile(temporary,json);await fs.rename(temporary,this.file);}
}
