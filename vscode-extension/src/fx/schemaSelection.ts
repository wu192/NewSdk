import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import {parseXml} from '../ra3xml/core/xmlDocument';

export interface FxSchemaSelection {
    path?: string;
    source: 'override' | 'sdk-config' | 'sdk-default' | 'none';
    issue?: string;
}
const defaultSchema = 'Schemas/xsd/CnC3Types.xsd';
const configurationFile = 'tools/BinaryAssetBuilder.exe.config';
function inside(root: string, file: string): boolean {
    const relative = path.relative(root, file);
    return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}
function missing(error: unknown): boolean {
    return error instanceof Error && 'code' in error && error.code === 'ENOENT';
}
/** Resolve Windows SDK spelling on case-sensitive test hosts without granting access outside the SDK. */
async function sdkFile(root: string, requested: string): Promise<string | undefined> {
    if (!inside(root, requested)) throw new Error('编译器 schema 路径位于所选 SDK 之外。');
    let current = root;
    for (const component of path.relative(root, requested).split(path.sep).filter(Boolean)) {
        let names: string[];
        try { names = await fs.readdir(current); } catch (error) { if (missing(error)) return; throw error; }
        const matches = names.filter(name => name.toLowerCase() === component.toLowerCase());
        if (!matches.length) return;
        if (matches.length !== 1) throw new Error('SDK 中存在仅大小写不同的歧义路径。');
        current = path.join(current, matches[0]);
    }
    const physical = await fs.realpath(current);
    if (!inside(root, physical)) throw new Error('编译器 schema 链接指向所选 SDK 之外。');
    return physical;
}

/** Explicit override wins; otherwise use the same entry selected by the SDK compiler. */
export async function selectFxSchema(sdkRoot?: string, override?: string): Promise<FxSchemaSelection> {
    const explicit = override?.trim();
    if (explicit) {
        const selected = path.resolve(explicit);
        try {
            const physical = await fs.realpath(selected);
            if (!(await fs.stat(physical)).isFile()) throw new Error('XSD 入口必须是文件。');
            return {path: physical, source: 'override'};
        } catch (error) {
            return {path: selected, source: 'override', issue: missing(error) ? '指定的 XSD 入口不存在。' : error instanceof Error ? error.message : '指定的 XSD 入口无法读取。'};
        }
    }
    if (!sdkRoot?.trim()) return {source: 'none'};
    const expected = path.join(path.resolve(sdkRoot), defaultSchema);
    let selected: FxSchemaSelection = {path: expected, source: 'sdk-default'};
    try {
        const root = await fs.realpath(path.resolve(sdkRoot));
        if (!(await fs.stat(root)).isDirectory()) throw new Error('所选 SDK 根路径必须是目录。');
        const config = await sdkFile(root, path.join(root, configurationFile));
        let entry = path.join(root, defaultSchema);
        if (config) {
            const info = await fs.stat(config);
            if (!info.isFile() || info.size > 1024 * 1024) throw new Error('SDK 编译器配置不是有效的小型配置文件。');
            const document = parseXml(await fs.readFile(config, 'utf8'), config);
            if (!document.valid || document.roots[0]?.localName !== 'configuration') throw new Error('SDK 编译器配置不是有效 XML configuration。');
            const schema = document.roots[0].children.find(node => node.localName === 'assetbuilder')?.attributes.schema?.trim();
            if (schema) {
                if (/^[A-Za-z][A-Za-z\d+.-]*:/.test(schema) && !/^[A-Za-z]:[\\/]/.test(schema)) throw new Error('SDK schema 必须指向本地文件。');
                if (/^[A-Za-z]:[\\/]/.test(schema) && process.platform !== 'win32') throw new Error('SDK schema 使用了当前平台不可解析的 Windows 绝对路径。');
                entry = path.resolve(path.dirname(config), schema.replace(/\\/g, '/'));
                selected = {path: entry, source: 'sdk-config'};
            }
        }
        const physical = await sdkFile(root, entry);
        if (!physical || !(await fs.stat(physical)).isFile()) return {...selected, issue: '所选 SDK 的 XSD 入口不存在。请检查编译器 schema 配置或 SDK 安装。'};
        return {path: physical, source: selected.source};
    } catch (error) {
        return {...selected, issue: missing(error) ? '所选 SDK 目录不存在。' : error instanceof Error ? error.message : '所选 SDK 的 XSD 入口无法读取。'};
    }
}
