import {createHash} from 'node:crypto';
import * as path from 'node:path';
import {filePath, type AssetRecord} from '../ra3xml/index/assetIndex';
import type {ManifestAsset} from '../ra3xml/index/manifestIndex';
import type {ReferenceRecord} from '../ra3xml/index/referenceIndex';
import type {AssetService} from '../ra3xml/services/assetService';
import type {XsdField} from '../ra3xml/model/xsdModel';
import {visibleDefineDocuments, defineDeclarations} from '../ra3xml/providers/editorFeatures';

export interface QueryTool {name: string; description: string; inputSchema: Record<string, unknown>; annotations: {readOnlyHint: true; destructiveHint: false; idempotentHint: true; openWorldHint: false};}
type Arguments = Record<string, unknown>;
type Definition = AssetRecord | ManifestAsset;
const pagination = {offset: {type: 'integer', minimum: 0}, limit: {type: 'integer', minimum: 1, maximum: 100}};
const selection = {asset_key: {type: 'string', description: 'Opaque stable key from search_assets; use to distinguish duplicate definitions.'}, type: {type: 'string'}, id: {type: 'string'}, file: {type: 'string', description: 'Project-relative file, or sdk: prefixed SDK-relative file.'}};
const annotation = {readOnlyHint: true, destructiveHint: false, idempotentHint: true, openWorldHint: false} as const;
function tool(name: string, description: string, properties: Record<string, unknown>, required: string[] = []): QueryTool {
    return {name, description, inputSchema: {type: 'object', properties, required, additionalProperties: false}, annotations: annotation};
}
export const QUERY_TOOLS: readonly QueryTool[] = [
    tool('search_assets', 'Search the current shared XML/manifest index without rescanning the project. Duplicate sources stay separate.', {query: {type: 'string'}, type: {type: 'string'}, source: {type: 'string', enum: ['mod', 'sdk-xml', 'manifest']}, ...pagination}),
    tool('get_asset_definition', 'Read a bounded XML source fragment for one exact asset; manifest metadata has no XML line.', {...selection, max_chars: {type: 'integer', minimum: 1, maximum: 32768}}),
    tool('get_asset_references', 'List outgoing typed references and their inherited source for one asset.', {...selection, ...pagination}),
    tool('find_references', 'List known incoming references to one exact asset.', {...selection, ...pagination}),
    tool('get_inheritance', 'Read the selected ancestor chain and a page of effective/source fields. Unresolved parents stay unknown.', {...selection, ...pagination}),
    tool('resolve_include', 'Read indexed Include and xi:include relationships for a known project file.', {file: selection.file, ...pagination}, ['file']),
    tool('get_schema_fields', 'Read a bounded page of fields for one XSD element/type; no full field catalog.', {element: {type: 'string'}, type: {type: 'string'}, ...pagination}),
    tool('get_fx_resources', 'Find visible Texture, FXParticleSystemTemplate and FXList declarations. Binary availability is never inferred.', {file: selection.file, query: {type: 'string'}, kind: {type: 'string', enum: ['Texture', 'FXParticleSystemTemplate', 'FXList']}, ...pagination}),
    tool('get_define', 'Read macro declarations within the selected file include scope; preserve ambiguity and dynamic values.', {file: selection.file, name: {type: 'string'}, ...pagination}, ['file', 'name']),
];
function string(args: Arguments, key: string, max = 2048): string | undefined {
    const value = args[key]; if (value === undefined) return;
    if (typeof value !== 'string' || value.length > max || value.includes('\0')) throw new Error(`Invalid ${key}`);
    return value;
}
function number(args: Arguments, key: string, fallback: number, min: number, max: number): number {
    const value = args[key]; if (value === undefined) return fallback;
    if (typeof value !== 'number' || !Number.isInteger(value) || value < min || value > max) throw new Error(`Invalid ${key}`);
    return value;
}
function inside(root: string, file: string): boolean { const relative = path.relative(root, file); return relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative); }
function clip(value: string | undefined, length = 2048): string | undefined {return value?.slice(0, length);}

/** Thin, read-only DTO layer over the editor's AssetService; owns no source/index cache. */
export class AssetQueryAdapter {
    constructor(readonly service: AssetService, private readonly refresh?: () => Promise<void>) {}
    private relative(file: string): string {
        const resolved = filePath(file);
        const roots: {root: string; prefix: string}[] = [];
        const add = (root: string | undefined, prefix: string) => {
            if (!root) return;
            const selected = filePath(root); roots.push({root: selected, prefix});
            const physical = this.service.index.shared.physicalPaths.get(selected); if (physical && physical !== selected) roots.push({root: physical, prefix});
        };
        add(this.service.options.modRoot, ''); add(this.service.options.sdkRoot, 'sdk:');
        this.service.options.roots.forEach((root, index) => add(root, `root${index}:`));
        for (const {root, prefix} of roots) if (inside(root, resolved)) return prefix + path.relative(root, resolved).replace(/\\/g, '/');
        return '[outside-project]';
    }
    private schemaName(name: string | undefined): string | undefined {
        if (!name) return;
        const separator = name.indexOf('#');
        return separator >= 0 && path.isAbsolute(name.slice(0, separator)) ? this.relative(name.slice(0, separator)) + name.slice(separator) : name;
    }
    // Preserve the shared canonical identity, including namespace, logical scope and xi:include occurrence.
    private stableKey(key: string): string {return createHash('sha256').update(key).digest('hex');}
    private stable(asset: Definition): string {return this.stableKey(asset.key);}
    private definition(asset: Definition): Record<string, unknown> {
        const isXml = 'node' in asset, document = isXml ? this.service.getDocument(asset.uri) : undefined;
        const position = isXml ? this.location(asset.uri, asset.node.start, asset.node.end) : undefined;
        return {asset_key: this.stable(asset), type: asset.type, id: asset.id, namespace: asset.namespace,
            source: isXml ? asset.readonly ? 'sdk-xml' : 'mod' : 'manifest', file: this.relative(asset.uri), readonly: asset.readonly,
            location: position, reference_count: isXml ? this.service.findReferences(asset).length : undefined,
            state: isXml && document && !document.shallow ? 'known' : 'unknown',
            ...(isXml ? {} : {xml_source: 'unavailable', declared_source: asset.declaredSource && (path.isAbsolute(asset.declaredSource) || /^[A-Za-z]:[\\/]/.test(asset.declaredSource)) ? '[unverified-source-path]' : clip(asset.declaredSource)})};
    }
    private location(uri: string, start: number, end: number): Record<string, unknown> {
        const doc = this.service.getDocument(uri);
        let line: number | undefined, character: number | undefined;
        if (doc && !doc.shallow) {const before = doc.text.slice(0, start); line = before.split('\n').length; character = start - (before.lastIndexOf('\n') + 1) + 1;}
        return {file: this.relative(uri), start, end, ...(line === undefined ? {} : {line, character})};
    }
    private allDefinitions(): Definition[] {return [...this.service.findAssets(), ...this.service.manifests?.list() ?? []];}
    private select(args: Arguments): {asset?: Definition; state: 'known' | 'unknown' | 'ambiguous'; candidates?: Record<string, unknown>[]} {
        const key = string(args, 'asset_key', 128), type = string(args, 'type'), id = string(args, 'id'), file = string(args, 'file');
        if (!key && !id) throw new Error('asset_key or id is required');
        const matches = this.allDefinitions().filter(asset => (!key || this.stable(asset) === key) && (!type || asset.type === type) && (!id || asset.id.toLowerCase() === id.toLowerCase()) && (!file || this.relative(asset.uri) === file));
        if (matches.length === 1) return {asset: matches[0], state: 'known'};
        return {state: matches.length ? 'ambiguous' : 'unknown', candidates: matches.slice(0, 100).map(asset => this.definition(asset))};
    }
    private file(relative: string | undefined): string | undefined {
        if (!relative) throw new Error('file is required');
        // Look up only files already present in the shared index. This never resolves or reads a caller-supplied path.
        for (const graph of this.service.index.shared.graphs.values()) for (const file of graph.documents.keys()) if (this.relative(file) === relative) return file;
        return;
    }
    private page<T>(items: T[], args: Arguments, map: (item: T) => unknown = item => item): Record<string, unknown> {
        const offset = number(args, 'offset', 0, 0, 1_000_000), limit = number(args, 'limit', 50, 1, 100);
        return {items: items.slice(offset, offset + limit).map(map), total: items.length, offset, limit, next_offset: offset + limit < items.length ? offset + limit : null};
    }
    private reference(reference: ReferenceRecord): Record<string, unknown> {
        const targets = this.service.findAssets().filter(asset => reference.targets.includes(asset.key));
        return {type: reference.type, value: clip(reference.value), field: reference.attribute ?? 'text()', node_path: reference.nodePath,
            location: this.location(reference.uri, reference.start, reference.end), state: reference.dynamic ? 'unknown' : reference.ambiguous ? 'ambiguous' : targets.length ? 'known' : reference.manifestTargets?.length ? 'metadata-only' : this.service.completeness === 'complete' ? 'missing' : 'unknown',
            dynamic: reference.dynamic, targets: targets.slice(0, 20).map(asset => this.definition(asset)), manifest_targets: reference.manifestTargets?.slice(0, 20).map(asset => this.definition(asset))};
    }
    async query(name: string, args: Arguments = {}): Promise<Record<string, unknown>> {
        const descriptor = QUERY_TOOLS.find(tool => tool.name === name); if (!descriptor) throw new Error('Unknown read-only query tool');
        const allowed = new Set(Object.keys(descriptor.inputSchema.properties as object));
        for (const key of Object.keys(args)) if (!allowed.has(key)) throw new Error(`Unknown argument ${key}`);
        await this.refresh?.();
        const meta = {index_version: this.service.indexVersion, completeness: this.service.completeness, snapshot: false, observed_at: new Date().toISOString()};
        let result: Record<string, unknown>;
        if (name === 'search_assets') {
            const source = string(args, 'source'); if (source && !['mod', 'sdk-xml', 'manifest'].includes(source)) throw new Error('Invalid source');
            const page = this.service.searchAssets({text: string(args, 'query'), type: string(args, 'type'), source: source as 'mod' | 'sdk-xml' | 'manifest' | undefined,
                offset: number(args, 'offset', 0, 0, 1_000_000), limit: number(args, 'limit', 50, 1, 100)});
            result = {...page, items: page.items.map(item => ({asset_key: this.stableKey(item.key), type: item.type, id: item.id, source: item.source,
                file: this.relative(item.uri), readonly: item.readonly, reference_count: item.referenceCount,
                location: item.location ? this.location(item.uri, item.location.start, item.location.end) : undefined,
                state: item.source === 'manifest' ? 'unknown' : 'known'})), next_offset: page.offset + page.limit < page.total ? page.offset + page.limit : null};
        } else if (name === 'resolve_include' || name === 'get_define') {
            const file = this.file(string(args, 'file'));
            if (!file) result = {state: 'unknown', reason: 'File is absent from the current index.'};
            else if (name === 'resolve_include') result = {state: 'known', ...this.page(this.service.getIncludeDependencies(file), args, edge => ({from: this.relative(edge.from), to: this.relative(edge.to), mode: edge.mode,
                location: this.location(edge.source.uri, edge.source.start, edge.source.end), state: this.service.getDocument(edge.to) ? 'known' : 'unknown'}))};
            else {
                const macro = string(args, 'name', 128)?.replace(/^\$/, ''); if (!macro) throw new Error('name is required');
                const graph = [...this.service.index.shared.graphs.values()].find(graph => graph.documents.has(file));
                const docs = graph ? visibleDefineDocuments(file, graph.documents.values(), graph.includes) : [];
                const matches = defineDeclarations(docs).filter(define => define.name === macro);
                result = {state: matches.length > 1 ? 'ambiguous' : matches.length ? 'known' : this.service.completeness === 'complete' ? 'missing' : 'unknown',
                    ...this.page(matches, args, define => ({name: define.name, value: clip(define.value), location: this.location(define.uri, define.start, define.end), dynamic: /[$=]/.test(define.value ?? '')}))};
            }
        } else if (name === 'get_schema_fields') {
            const element = string(args, 'element'), typeName = string(args, 'type'); if (!element && !typeName) throw new Error('element or type is required');
            const schema = this.service.schema, type = typeName ? schema?.getType(typeName) ?? [...schema?.types.values() ?? []].find(type => this.schemaName(type.name) === typeName) : schema?.getType(schema.getElement(element!)?.type ?? '');
            const fields = (type?.attributes.map(field => this.schemaField(field, 'attribute')) ?? []).concat(type?.children.map(field => this.schemaField(field, 'element')) ?? []);
            result = {state: type ? 'known' : 'unknown', element, type: this.schemaName(type?.name ?? typeName), schema_version: schema?.version,
                schema_complete: schema?.complete ?? false, documentation: clip(type?.documentation), unit_metadata: 'Only documented units are available; no unit inference is performed.', ...this.page(fields, args)};
        } else if (name === 'get_fx_resources') {
            const fileArg = string(args, 'file'), file = fileArg ? this.file(fileArg) : undefined;
            if (fileArg && !file) result = {state: 'unknown', reason: 'File is absent from the current index.'};
            else {
                const kind = string(args, 'kind'); if (kind && !['Texture', 'FXParticleSystemTemplate', 'FXList'].includes(kind)) throw new Error('Invalid kind');
                const query = string(args, 'query')?.toLowerCase();
                const xml = this.service.findAssets(file ? {scopeUri: file} : {});
                const definitions: Definition[] = [...xml, ...this.service.manifests?.list() ?? []];
                const resources = definitions.filter(asset => (kind ? asset.type === kind : ['Texture', 'FXParticleSystemTemplate', 'FXList'].includes(asset.type)) && (!query || asset.id.toLowerCase().includes(query)));
                result = {state: this.service.completeness === 'complete' ? 'known' : 'unknown', binary_status: 'unknown', ...this.page(resources, args, asset => ({...this.definition(asset), resource_status: 'declaration-only',
                    declared_file: 'node' in asset ? clip(asset.node.attributes.File ?? asset.node.attributes.file) : undefined}))};
            }
        } else {
            const selected = this.select(args), asset = selected.asset;
            if (!asset) result = {state: selected.state, candidates: selected.candidates};
            else if (!('node' in asset)) result = {state: 'unknown', definition: this.definition(asset), reason: 'Manifest stores declaration metadata only; source/reference/inheritance data is unavailable.'};
            else if (name === 'get_asset_definition') {
                const document = this.service.getDocument(asset.uri), budget = number(args, 'max_chars', 16384, 1, 32768), text = document && !document.shallow ? document.text.slice(asset.node.start, asset.node.end) : undefined;
                result = {state: text === undefined ? 'unknown' : 'known', definition: this.definition(asset), xml: text?.slice(0, budget), truncated: text !== undefined && text.length > budget, total_chars: text?.length,
                    document_version: document?.version, reason: document?.shallow ? 'Shallow declaration metadata has no complete XML fragment.' : undefined};
            } else if (name === 'find_references') result = {state: this.service.completeness === 'complete' ? 'known' : 'unknown', definition: this.definition(asset), ...this.page(this.service.findReferences(asset), args, reference => this.reference(reference))};
            else if (name === 'get_asset_references') {
                const chain = this.service.getInheritance(asset), owners = new Map(chain.map(parent => [parent.key, parent]));
                result = {state: this.service.completeness === 'complete' ? 'known' : 'unknown', definition: this.definition(asset), ...this.page(this.service.getReferences().filter(reference => reference.ownerKey && owners.has(reference.ownerKey)), args, reference => ({...this.reference(reference),
                    inherited: reference.ownerKey !== asset.key, source: this.definition(owners.get(reference.ownerKey!)!)}))};
            } else {
                const chain = this.service.getInheritance(asset), last = chain.at(-1), unresolved = !!last?.node.attributes.inheritFrom;
                result = {state: unresolved ? 'unknown' : 'known', definition: this.definition(asset), chain: chain.slice(0, 100).map(parent => this.definition(parent)), chain_truncated: chain.length > 100,
                    unresolved_parent: unresolved ? clip(last!.node.attributes.inheritFrom) : undefined, ...this.page(this.service.getEffectiveFields(asset), args, field => ({name: field.name, value: clip(field.value), inherited: field.inherited, certainty: field.certainty,
                        source: this.definition(field.source), location: this.location(field.source.uri, field.start, field.end)}))};
            }
        }
        return {...meta, ...result};
    }
    private schemaField(field: XsdField, kind: string): Record<string, unknown> {return {name: field.name, kind, type: this.schemaName(field.type), required: field.required, default: clip(field.default), fixed: clip(field.fixed),
        enum_values: field.enumValues.slice(0, 100), enum_truncated: field.enumValues.length > 100, reference_type: field.referenceType, list: field.list,
        documentation: clip(field.documentation), source: field.sourceUri ? this.relative(field.sourceUri) : undefined, min_occurs: field.minOccurs, max_occurs: field.maxOccurs === Infinity ? 'unbounded' : field.maxOccurs};}
}
