import * as fs from 'node:fs/promises';
import * as path from 'node:path';

export interface ManifestAsset {
    key: string; type: string; id: string; namespace: string; uri: string;
    readonly: true; source: 'manifest'; typeHash: number; idHash: number;
    /** Metadata from the stream, never a verified source location. */
    declaredSource?: string;
}
export interface ManifestLoadOptions { maxFiles?: number; maxBytes?: number; maxAssets?: number; }

/** Reads only the manifest's declaration/name tables, never .bin payloads.
 * Layout is the SDK's PC AssetStream v4-v6: 48-byte header/entries. */
export function parseManifest(buffer: Buffer, uri: string, maxAssets = 250000): ManifestAsset[] {
    if (buffer.length < 48) throw new Error('Manifest header is truncated');
    if (buffer[0] !== 0 && buffer[0] !== 1) throw new Error('Unsupported manifest byte order');
    const bigEndian = buffer[0] === 1;
    const uint = (offset: number): number => bigEndian ? buffer.readUInt32BE(offset) : buffer.readUInt32LE(offset);
    const version = bigEndian ? buffer.readUInt16BE(2) : buffer.readUInt16LE(2);
    if (version < 4 || version > 6) throw new Error(`Unsupported manifest version ${version}`);
    const count = uint(12);
    if (count > maxAssets) throw new Error('Manifest declaration budget exceeded');
    const references = uint(32), dependencies = uint(36), namesSize = uint(40), sourcesSize = uint(44);
    const entriesEnd = 48 + count * 48, namesStart = entriesEnd + references + dependencies, sourcesStart = namesStart + namesSize;
    if (!Number.isSafeInteger(sourcesStart + sourcesSize) || sourcesStart + sourcesSize !== buffer.length) throw new Error('Manifest tables are truncated or inconsistent');
    if (entriesEnd > buffer.length || (count > 0 && namesSize === 0)) throw new Error('Manifest has no usable declaration names');
    const textAt = (start: number, length: number, offset: number): string => {
        if (offset >= length) throw new Error('Manifest string offset is outside its table');
        const end = buffer.indexOf(0, start + offset);
        if (end < start + offset || end >= start + length) throw new Error('Manifest string is not terminated inside its table');
        return buffer.toString('utf8', start + offset, end);
    };
    const result: ManifestAsset[] = [];
    for (let index = 0; index < count; index++) {
        const offset = 48 + index * 48;
        const qualified = textAt(namesStart, namesSize, uint(offset + 24));
        const separator = qualified.indexOf(':');
        if (separator <= 0 || separator === qualified.length - 1) throw new Error('Manifest declaration has no qualified type and id');
        const type = qualified.slice(0, separator), id = qualified.slice(separator + 1);
        const source = sourcesSize ? textAt(sourcesStart, sourcesSize, uint(offset + 28)) : undefined;
        result.push({key: JSON.stringify(['manifest', uri, index, type, id]), type, id, namespace: 'uri:ea.com:eala:asset', uri,
            readonly: true, source: 'manifest', typeHash: uint(offset), idHash: uint(offset + 4), declaredSource: source});
    }
    return result;
}

export class ManifestIndex {
    readonly entries: ManifestAsset[] = [];
    readonly issues: string[] = [];
    readonly files: string[] = [];
    complete = true;
    private readonly byIdentity = new Map<string, ManifestAsset[]>();
    private readonly byId = new Map<string, ManifestAsset[]>();
    static async load(sdkRoot: string, options: ManifestLoadOptions = {}): Promise<ManifestIndex> {
        const index = new ManifestIndex();
        const sdk = await fs.realpath(path.resolve(sdkRoot)).catch(() => undefined);
        if (!sdk) { index.fail('SDK root is unavailable'); return index; }
        // Preserve the configured SDK's casing on case-sensitive test hosts.
        const builtName = (await fs.readdir(sdk)).find(name => name.toLowerCase() === 'builtmods');
        if (!builtName) { index.fail('SDK builtmods manifests are unavailable'); return index; }
        const root = path.join(sdk, builtName);
        const maxFiles = options.maxFiles ?? 256, maxBytes = options.maxBytes ?? 64 * 1024 * 1024, maxAssets = options.maxAssets ?? 500000;
        let bytes = 0, files = 0;
        const inside = (file: string): boolean => { const relative = path.relative(sdk, file); return !path.isAbsolute(relative) && relative !== '..' && !relative.startsWith(`..${path.sep}`); };
        const walk = async (directory: string, depth: number): Promise<void> => {
            if (depth > 5) { index.fail('SDK manifest directory depth budget exceeded'); return; }
            const physical = await fs.realpath(directory); if (!inside(physical)) { index.fail('SDK manifest link escapes SDK'); return; }
            for (const entry of await fs.readdir(physical, {withFileTypes: true})) {
                if (entry.isDirectory() && !entry.isSymbolicLink()) await walk(path.join(physical, entry.name), depth + 1);
                else if (entry.isFile() && /\.manifest$/i.test(entry.name)) {
                    const file = path.join(physical, entry.name);
                    if (++files > maxFiles) { index.fail('SDK manifest file budget exceeded'); return; }
                    try {
                        const stat = await fs.stat(file);
                        if (bytes + stat.size > maxBytes) { index.fail('SDK manifest byte budget exceeded'); continue; }
                        bytes += stat.size;
                        const buffer = await fs.readFile(file);
                        const assets = parseManifest(buffer, file, maxAssets - index.entries.length);
                        index.files.push(file);
                        for (const asset of assets) {
                            index.entries.push(asset);
                            const identity = JSON.stringify([asset.type, asset.id.toLowerCase()]);
                            const values = index.byIdentity.get(identity) ?? []; values.push(asset); index.byIdentity.set(identity, values);
                            const ids = index.byId.get(asset.id.toLowerCase()) ?? []; ids.push(asset); index.byId.set(asset.id.toLowerCase(), ids);
                        }
                    } catch (error) { index.fail(`${file}: ${error instanceof Error ? error.message : String(error)}`); }
                }
            }
        };
        try { await walk(root, 0); } catch (error) { index.fail(String(error)); }
        if (!index.files.length) index.fail('No readable SDK builtmods manifests were found');
        return index;
    }
    private fail(message: string): void { this.complete = false; if (this.issues.length < 100) this.issues.push(message); }
    find(type: string, id: string): ManifestAsset[] { return this.byIdentity.get(JSON.stringify([type, id.toLowerCase()])) ?? []; }
    findById(id: string): ManifestAsset[] { return this.byId.get(id.toLowerCase()) ?? []; }
    list(type?: string): ManifestAsset[] { return type ? this.entries.filter(asset => asset.type === type) : this.entries; }
}
