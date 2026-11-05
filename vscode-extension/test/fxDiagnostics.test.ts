import { test } from 'node:test';
import * as assert from 'node:assert/strict';

const pending: (() => void)[] = [];
const published = new Map<string, MockDiagnostic[]>();
class MockUri {constructor(readonly value: string) {} static file(value: string) {return new MockUri(value);} static parse(value: string) {return new MockUri(value);} toString() {return this.value;}}
class MockRange {constructor(..._args: unknown[]) {} isEqual(_other: unknown) {return true;}}
class MockDiagnostic {source?: string; code?: string; constructor(readonly range: MockRange,readonly message: string,readonly severity: number) {}}
const mockVscode = {
    Uri: MockUri, Range: MockRange, Diagnostic: MockDiagnostic, DiagnosticSeverity:{Error:0,Warning:1,Information:2},
    workspace:{openTextDocument:async()=>{await new Promise<void>(resolve=>pending.push(resolve));return {positionAt:(offset:number)=>offset};}},
    languages:{createDiagnosticCollection:()=>({clear:()=>published.clear(),set:(uri:MockUri,value:MockDiagnostic[])=>published.set(uri.toString(),value),dispose:()=>published.clear()})},
};
const loader=require('node:module') as {_load:(name:string,parent:unknown,main:boolean)=>unknown};
const original=loader._load;
loader._load=function(name,parent,main){return name==='vscode'?mockVscode:original.call(this,name,parent,main);};
let Diagnostics: typeof import('../src/fx/diagnostics').FxDiagnostics;
try {Diagnostics=(require('../src/fx/diagnostics') as typeof import('../src/fx/diagnostics')).FxDiagnostics;} finally {loader._load=original;}

test('a diagnostic update cannot return after owner release and replace a newer generation', async()=>{
    const diagnostics=new Diagnostics();
    const old=diagnostics.update('owner',[{uri:'/test.xml',message:'old',severity:'error'}]);
    diagnostics.remove('owner');
    const fresh=diagnostics.update('owner',[{uri:'/test.xml',message:'new',severity:'information'}]);
    pending[1]();await fresh;
    assert.equal(published.get('/test.xml')?.[0].message,'new');assert.equal(published.get('/test.xml')?.[0].severity,2);
    pending[0]();await old;
    assert.equal(published.get('/test.xml')?.[0].message,'new');
    diagnostics.clear();assert.equal(published.size,0);diagnostics.dispose();
});
