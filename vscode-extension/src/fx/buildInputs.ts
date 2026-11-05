import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { AssetIndex, TextSnapshot } from './assetIndex';
import { canonicalPath, containsPath, pathIdentity } from '../project';

export interface BuildInputOptions {
    modRoot: string; sdkRoot: string;
    /** Supply open TextDocument snapshots so unsaved Include edits affect the closure. */
    readText?: (uri: string) => Promise<TextSnapshot | string>;
    signal?: AbortSignal;
    maxFiles?: number;
}
export interface BuildInputs {
    /** Actual automatic compiler stream entry candidates, kept separate by AssetIndex. */
    entries: string[];
    /** Physical, normalized paths. Includes reference XML and MOD/SDK Additional packaging files. */
    files: string[];
    /** false means the caller must offer MOD-local dirty XML selection. */
    complete: boolean;
    reasons: string[];
}
function nodeCode(error: unknown): string | undefined { return (error as NodeJS.ErrnoException | undefined)?.code; }
function cancelled(signal?: AbortSignal): void { if (signal?.aborted) throw new Error('构建输入扫描已取消。'); }

/**
 * Conservative input discovery for package-content=auto, include-additional=true.
 * A selected preview is only one dependency root and cannot establish build scope.
 * This helper never saves documents, runs the compiler, or walks unrelated roots.
 */
export async function collectBuildInputs(options: BuildInputOptions): Promise<BuildInputs> {
    const modRoot = await canonicalPath(options.modRoot); const sdkRoot = await canonicalPath(options.sdkRoot);
    const files = new Map<string, string>(); const entries: string[] = []; const reasons: string[] = [];
    const limit = options.maxFiles ?? 20000;
    const add = async (file: string): Promise<boolean> => {
        cancelled(options.signal);
        try {
            const physical = await canonicalPath(file);
            if (!containsPath(modRoot, physical) && !containsPath(sdkRoot, physical)) {
                reasons.push(`输入越出 MOD/SDK 物理根：${file}`); return false;
            }
            if (files.size >= limit && !files.has(pathIdentity(physical))) {
                if (!reasons.includes('构建输入文件数超过扫描预算。')) reasons.push('构建输入文件数超过扫描预算。'); return false;
            }
            files.set(pathIdentity(physical), physical); return true;
        } catch (error) { reasons.push(`无法确定输入路径 ${file}：${error instanceof Error ? error.message : String(error)}`); return false; }
    };
    const candidate = async (file: string): Promise<void> => {
        cancelled(options.signal);
        try { if ((await fs.stat(file)).isFile()) entries.push(file); }
        catch (error) { if (nodeCode(error) !== 'ENOENT') reasons.push(`无法读取编译入口 ${file}。`); }
    };
    const directoryEntries = async (directory: string, accept: (name: string) => boolean): Promise<void> => {
        cancelled(options.signal);
        try {
            const items = await fs.readdir(directory, { withFileTypes: true });
            for (const item of items) if (accept(item.name)) {
                if (item.isFile() || item.isSymbolicLink()) await candidate(path.join(directory, item.name));
                else if (!item.isDirectory()) reasons.push(`无法确定入口类型 ${path.join(directory, item.name)}。`);
            }
        } catch (error) { if (nodeCode(error) !== 'ENOENT') reasons.push(`无法列出编译入口目录 ${directory}。`); }
    };
    await candidate(path.join(modRoot, 'Data', 'Mod.xml'));
    await directoryEntries(path.join(modRoot, 'Data', 'AptUI'), name => /\.xml$/i.test(name));
    await directoryEntries(path.join(modRoot, 'Data', 'AdditionalMaps'), name => /^MapMetaData_.*\.xml$/i.test(name));
    const index = new AssetIndex({ roots: [modRoot, sdkRoot], modRoot, sdkRoot, readText: options.readText });
    try {
        for (const entry of entries) {
            cancelled(options.signal);
            try {
                const graph = await index.load(entry, options.signal);
                for (const file of graph.dependencies) await add(file);
                // Even warning-level unsupported include contracts make a save scope uncertain.
                for (const issue of graph.issues) reasons.push(`${path.relative(modRoot, entry)}：${issue.message}`);
                // Texture/model/compiler inputs can contain XML outside Includes. We cannot
                // prove their compiler traversal solely from the lightweight FX asset graph.
                for (const document of graph.documents.values()) for (const node of document.elements) {
                    for (const [key, value] of Object.entries(node.attributes)) {
                        if (/^(?:File|Filename|SourceFile)$/i.test(key) && /\.(?:xml|w3x)(?:$|[?#])/i.test(value)) {
                            reasons.push(`外部 XML 编译素材需确认保存范围：${value}`);
                        }
                    }
                }
            } catch (error) {
                cancelled(options.signal);
                reasons.push(`无法确定入口闭包 ${entry}：${error instanceof Error ? error.message : String(error)}`);
            }
        }
    } finally { index.dispose(); }
    // Additional is copied into the final package without an XML include graph.
    const overlay = async (directory: string, inputRoot: string): Promise<void> => {
        cancelled(options.signal);
        let items: string[];
        try {
            const physical = await canonicalPath(directory);
            if (!containsPath(inputRoot, physical)) { reasons.push(`Additional 目录越出输入物理根：${directory}`); return; }
            items = await fs.readdir(directory);
        } catch (error) { if (nodeCode(error) !== 'ENOENT') reasons.push(`无法读取 Additional 输入目录 ${directory}。`); return; }
        for (const name of items) {
            cancelled(options.signal);
            if (files.size >= limit) { reasons.push('构建输入文件数超过扫描预算。'); return; }
            const file = path.join(directory, name);
            try {
                const stat = await fs.lstat(file);
                if (stat.isSymbolicLink()) { reasons.push(`Additional 含符号链接，需确认保存范围：${file}`); continue; }
                if (stat.isDirectory()) await overlay(file, inputRoot);
                else if (stat.isFile()) await add(file);
            } catch (error) { reasons.push(`无法读取 Additional 输入 ${file}。`); }
        }
    };
    await overlay(path.join(sdkRoot, 'Additional'), sdkRoot);
    await overlay(path.join(modRoot, 'Additional'), modRoot);
    if (!entries.length) reasons.push('没有发现可确认的自动编译入口。');
    return { entries: [...new Set(entries)].sort(), files: [...files.values()].sort(), complete: reasons.length === 0, reasons: [...new Set(reasons)] };
}
