import { test } from 'node:test';
import * as assert from 'node:assert/strict';
import * as fs from 'node:fs/promises';
import * as os from 'node:os';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';
import { AssetIndex } from '../src/fx/assetIndex';
import { PreviewSession } from '../src/fx/previewSession';

const disposable = { dispose(): void {} };
class MockEmitter<T> {
    readonly event = (_listener: (value: T) => unknown): typeof disposable => disposable;
    fire(_value: T): void {}
    dispose(): void {}
}
class MockUri {
    constructor(readonly fsPath: string) {}
    static file(file: string): MockUri { return new MockUri(path.resolve(file)); }
    static parse(uri: string): MockUri { return new MockUri(fileURLToPath(uri)); }
}
const mockVscode = {
    Uri: MockUri, EventEmitter: MockEmitter,
    workspace: { isTrusted: true, onDidChangeTextDocument: () => disposable },
    window: {
        registerCustomEditorProvider: () => disposable, createTreeView: () => disposable,
        onDidChangeTextEditorSelection: () => disposable,
    },
    commands: { registerCommand: () => disposable },
    languages: { createDiagnosticCollection: () => ({ ...disposable, clear(): void {}, set(): void {} }) },
};
function loadProvider(): typeof import('../src/fx/editorProvider') {
    const loader = require('node:module') as { _load: (request: string, parent: unknown, isMain: boolean) => unknown };
    const original = loader._load;
    loader._load = function (request, parent, isMain) {
        return request === 'vscode' ? mockVscode : original.call(this, request, parent, isMain);
    };
    try { return require('../src/fx/editorProvider') as typeof import('../src/fx/editorProvider'); }
    finally { loader._load = original; }
}
const { registerFx } = loadProvider();
interface ProviderBoundary {
    roots: string[];
    compiledRoot?: string;
    allowedCompiled(file:string):Promise<string>;
    allowed(file: string, editable?: boolean): Promise<string>;
    dispose(): void;
}
async function fixture(run: (root: string) => Promise<void>): Promise<void> {
    const root = await fs.realpath(await fs.mkdtemp(path.join(os.tmpdir(), 'fx-readonly-')));
    try { await run(root); } finally { await fs.rm(root, { recursive: true, force: true }); }
}
async function file(root: string, relative: string): Promise<string> {
    const target = path.join(root, relative); await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.writeFile(target, '<AssetDeclaration/>'); return target;
}
async function boundary(mod: string, sdk: string, run: (provider: ProviderBoundary, index: AssetIndex) => Promise<void>): Promise<void> {
    const context = {} as Parameters<typeof registerFx>[0];
    const services = {
        getProject: () => ({root: mod}), getConfiguration: () => ({root: mod, sdkRoot: sdk}),
        saveAndBuild: async () => { throw new Error('Boundary checks must not invoke the compiler.'); },
    } as unknown as Parameters<typeof registerFx>[1];
    const provider = registerFx(context, services) as unknown as ProviderBoundary;
    provider.roots = [await fs.realpath(mod), await fs.realpath(sdk)];
    const index = new AssetIndex({roots: provider.roots, modRoot: mod, sdkRoot: sdk});
    try { await run(provider, index); } finally { provider.dispose(); index.dispose(); }
}

test('a MOD containing the SDK does not grant edits to SDK assets', async () => fixture(async root => {
    const mod = path.join(root, 'mod'); const sdk = path.join(mod, 'SDK');
    const sdkFile = await file(sdk, 'SageXml/shared.xml'); const ownFile = await file(mod, 'Data/own.xml');
    await boundary(mod, sdk, async (provider, index) => {
        assert.equal(await provider.allowed(sdkFile), await fs.realpath(sdkFile), 'SDK source remains readable');
        await assert.rejects(provider.allowed(sdkFile, true), /SDK.*只读/);
        assert.equal(await provider.allowed(ownFile, true), await fs.realpath(ownFile));
        await index.checkedFile(sdkFile); await index.checkedFile(ownFile);
        assert.equal(index.isReadonly(sdkFile), true); assert.equal(index.isReadonly(ownFile), false);
    });
}));

test('selecting the SDK itself as the MOD keeps SDK source read-only', async () => fixture(async root => {
    const sdk = path.join(root, 'SDK'); const sdkFile = await file(sdk, 'SageXml/shared.xml');
    await boundary(sdk, sdk, async (provider, index) => {
        assert.equal(await provider.allowed(sdkFile), await fs.realpath(sdkFile));
        await assert.rejects(provider.allowed(sdkFile, true), /SDK.*只读/);
        await index.checkedFile(sdkFile); assert.equal(index.isReadonly(sdkFile), true);
    });
}));

test('a MOD strictly inside the SDK may edit itself but not shared or sibling SDK files', async () => fixture(async root => {
    const sdk = path.join(root, 'SDK'); const mod = path.join(sdk, 'Mods', 'Mymod');
    const ownFile = await file(mod, 'Data/own.xml'); const sdkFile = await file(sdk, 'SageXml/shared.xml');
    const siblingFile = await file(sdk, 'Mods/Other/Data/other.xml');
    await boundary(mod, sdk, async (provider, index) => {
        assert.equal(await provider.allowed(ownFile, true), await fs.realpath(ownFile));
        await index.checkedFile(ownFile); assert.equal(index.isReadonly(ownFile), false);
        for (const readonly of [sdkFile, siblingFile]) {
            assert.equal(await provider.allowed(readonly), await fs.realpath(readonly));
            await assert.rejects(provider.allowed(readonly, true), /SDK.*只读/);
            await index.checkedFile(readonly); assert.equal(index.isReadonly(readonly), true);
        }
    });
}));


test('production Save dispatch retains the dirty document when disk save fails', async () => {
    const messages: {type: string; message?: string}[] = [];
    const services = {getProject:()=>undefined,getConfiguration:()=>undefined,saveAndBuild:async()=>assert.fail('Save must not start a compiler')};
    const provider = registerFx({} as Parameters<typeof registerFx>[0],services) as unknown as {dispatch(state: unknown,message: unknown): Promise<void>;dispose():void};
    const session = new PreviewSession(3);
    const document = {version:3,isDirty:true,save:async()=>false,uri:MockUri.file('/tmp/save-failure.xml')};
    const state = {document,session,disposed:false,panel:{webview:{postMessage:async(message: {type: string;message?:string})=>{messages.push(message);return true;}}}};
    try {
        await provider.dispatch(state,{sessionId:session.id,documentVersion:2,requestId:1,type:'save'});
        assert.equal(document.isDirty,true);assert.equal(messages.at(-1)?.type,'status');assert.match(messages.at(-1)?.message??'',/保存失败.*脏文档/);
        document.save=async()=>{throw new Error('disk IO unavailable');};
        await provider.dispatch(state,{sessionId:session.id,documentVersion:2,requestId:2,type:'save'});
        assert.equal(document.isDirty,true);assert.match(messages.at(-1)?.message??'',/disk IO unavailable/);
    } finally {session.dispose();provider.dispose();}
});


test('configured compiled output is readable only through the dedicated DDS boundary', async () => fixture(async root => {
    const mod=path.join(root,'Mod'),sdk=path.join(root,'SDK'),output=path.join(root,'Output');
    await file(mod,'Data/Mod.xml');await file(sdk,'source.xml');const compiled=await file(output,'particle.dds'),other=await file(root,'unrelated/particle.dds');
    await boundary(mod,sdk,async(provider,index)=>{
        provider.compiledRoot=await fs.realpath(output);
        assert.equal(await provider.allowedCompiled(compiled),await fs.realpath(compiled));
        await assert.rejects(provider.allowed(compiled),/MOD.*SDK/);
        await assert.rejects(index.checkedFile(compiled),/根|目录|outside|root/i);
        await assert.rejects(provider.allowedCompiled(other),/输出目录/);
    });
}));
