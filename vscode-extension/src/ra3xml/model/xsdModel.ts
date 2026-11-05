import * as fs from 'node:fs/promises';
import * as path from 'node:path';
import { createHash } from 'node:crypto';
import { parseXml, type XmlElement } from '../../fx/xmlDocument';

const XSD = 'http://www.w3.org/2001/XMLSchema';
const ASSET = 'uri:ea.com:eala:asset';
const local = (name: string): string => name.slice(name.lastIndexOf(':') + 1);
const children = (node: XmlElement, kind: string): XmlElement[] => node.children.filter(child => child.namespaceUri === XSD && child.localName === kind);
const annotation = (node: XmlElement, name: string): string | undefined => Object.entries(node.attributes).find(([key]) => local(key) === name)?.[1];
function documentation(node: XmlElement): string | undefined {
    const content = (value: XmlElement): string => [...value.textTokens.map(token => token.value), ...value.children.map(content)].join(' ');
    const docs = children(node, 'annotation').flatMap(value => children(value, 'documentation')).map(content).join(' ').replace(/\s+/g, ' ').trim();
    return docs || undefined;
}
function inside(root: string, file: string): boolean { const relative = path.relative(root, file); return !path.isAbsolute(relative) && relative !== '..' && !relative.startsWith(`..${path.sep}`); }

export interface XsdField {
    name: string; type: string; required: boolean; default?: string; fixed?: string;
    enumValues: string[]; referenceType?: string; documentation?: string;
    minOccurs?: number; maxOccurs?: number; list?: boolean; sourceUri?: string;
}
export interface XsdElement extends XsdField { namespace?: string; abstract?: boolean; }
export interface XsdType {
    name: string; namespace: string; base?: string; attributes: XsdField[]; children: XsdField[];
    enumValues: string[]; referenceType?: string; list?: boolean; documentation?: string; sourceUri?: string;
}
export interface XsdLoadOptions { maxFiles?: number; maxBytes?: number; entryFiles?: string[]; }
interface Declaration { node: XmlElement; uri: string; namespace: string; }

/** User-SDK schema metadata, generated at runtime. This is an editor model,
 * not a substitute for the compiler's complete XSD validation. */
export class XsdModel {
    readonly elements = new Map<string, XsdElement>();
    readonly types = new Map<string, XsdType>();
    readonly issues: string[] = [];
    readonly files: string[] = [];
    complete = true;
    version = '';
    private readonly rawTypes = new Map<string, Declaration>();
    private readonly rawElements = new Map<string, Declaration>();
    private readonly groups = new Map<string, Declaration>();
    private readonly attributeGroups = new Map<string, Declaration>();
    private readonly globalAttributes = new Map<string, Declaration>();
    private readonly resolving = new Set<string>();

    static async load(sdkRoot: string, options: XsdLoadOptions = {}): Promise<XsdModel> {
        const model = new XsdModel();
        const selected = path.resolve(sdkRoot);
        let root: string;
        try { root = await fs.realpath(selected); } catch { model.fail(`SDK schema root is unavailable: ${selected}`); return model; }
        const maxFiles = options.maxFiles ?? 4096, maxBytes = options.maxBytes ?? 32 * 1024 * 1024;
        const loaded = new Set<string>(); let bytes = 0;
        const digest = createHash('sha256');
        // SDK include spellings use Windows case semantics even on a test host.
        const names = new Map<string, string[]>();
        const canonical = async (input: string): Promise<string> => {
            const absolute = path.resolve(input);
            if (!inside(root, absolute)) throw new Error(`Schema include escapes SDK: ${absolute}`);
            let current = root;
            for (const part of path.relative(root, absolute).split(path.sep).filter(Boolean)) {
                let entries = names.get(current); if (!entries) { entries = await fs.readdir(current); names.set(current, entries); }
                const matches = entries.filter(name => name.toLowerCase() === part.toLowerCase());
                if (matches.length !== 1) throw new Error(`Missing or ambiguous schema path: ${absolute}`);
                current = path.join(current, matches[0]);
            }
            const physical = await fs.realpath(current);
            if (!inside(root, physical)) throw new Error(`Schema link escapes SDK: ${absolute}`);
            return physical;
        };
        const visit = async (input: string): Promise<void> => {
            let file: string;
            try { file = await canonical(input); } catch (error) { model.fail(String(error)); return; }
            if (loaded.has(file)) return;
            if (loaded.size >= maxFiles) { model.fail('SDK schema file budget exceeded'); return; }
            loaded.add(file);
            try {
                const stat = await fs.stat(file);
                if (stat.size + bytes > maxBytes) throw new Error('SDK schema byte budget exceeded');
                const text = await fs.readFile(file, 'utf8'); bytes += Buffer.byteLength(text);
                if (bytes > maxBytes) throw new Error('SDK schema byte budget exceeded');
                digest.update(path.relative(root, file)).update('\0').update(text).update('\0');
                model.files.push(file);
                const document = parseXml(text, file);
                const schema = document.roots.find(node => node.namespaceUri === XSD && node.localName === 'schema');
                if (!schema || !document.valid) throw new Error(`Invalid SDK XSD: ${file}`);
                const namespace = schema.attributes.targetNamespace ?? '';
                for (const node of schema.children) {
                    if (node.namespaceUri !== XSD) continue;
                    const name = node.attributes.name; const declaration = {node, uri: file, namespace};
                    if (name) {
                        const target = node.localName === 'element' ? model.rawElements : node.localName === 'attribute' ? model.globalAttributes
                            : node.localName === 'group' ? model.groups : node.localName === 'attributeGroup' ? model.attributeGroups
                            : ['simpleType', 'complexType'].includes(node.localName) ? model.rawTypes : undefined;
                        if (target) { target.set(`{${namespace}}${name}`, declaration); if (!target.has(name) || namespace === ASSET) target.set(name, declaration); }
                    }
                }
                for (const node of schema.children.filter(node => node.namespaceUri === XSD && ['include', 'import', 'redefine'].includes(node.localName))) {
                    const location = node.attributes.schemaLocation;
                    if (!location) continue;
                    if (/^[a-z][a-z\d+.-]*:/i.test(location) || location.startsWith('//')) { model.fail(`Nonlocal SDK schema is not loaded: ${location}`); continue; }
                    await visit(path.resolve(path.dirname(file), location.replace(/\\/g, '/')));
                }
            } catch (error) { model.fail(error instanceof Error ? error.message : String(error)); }
        };
        if (options.entryFiles?.length) { for (const file of options.entryFiles) await visit(path.resolve(root, file)); }
        else {
            const candidates = ['Schemas/xsd/CnC3Types.xsd', 'Schemas/CnC3Types.xsd', 'xsd/CnC3Types.xsd', 'CnC3Types.xsd'];
            let entry: string | undefined;
            for (const candidate of candidates) { try { entry = await canonical(path.join(root, candidate)); break; } catch { /* Try next SDK layout. */ } }
            if (entry) await visit(entry);
            else {
                // Small custom/test SDKs may expose several standalone schema files.
                const walk = async (directory: string, depth: number): Promise<void> => {
                    if (depth > 6 || loaded.size >= maxFiles) return;
                    for (const item of await fs.readdir(directory, {withFileTypes: true})) {
                        if (item.isDirectory() && !item.isSymbolicLink()) await walk(path.join(directory, item.name), depth + 1);
                        else if (item.isFile() && /\.xsd$/i.test(item.name)) await visit(path.join(directory, item.name));
                    }
                };
                try { await walk(root, 0); } catch (error) { model.fail(String(error)); }
                if (!loaded.size) model.fail('No SDK XSD files were found');
            }
        }
        // Finalize only after includes are collected, so declaration order cannot
        // change reference types, inheritance, or enum completion.
        for (const name of model.rawTypes.keys()) model.resolveType(name);
        for (const [name, declaration] of model.rawElements) {
            const field = model.field(declaration.node, declaration, false) as XsdElement;
            field.namespace = declaration.namespace; field.abstract = declaration.node.attributes.abstract === 'true';
            model.elements.set(name, field);
        }
        // Assets are often declared locally in AssetDeclaration's choice rather
        // than as global XSD elements. Expose those concrete declarations too.
        const assetRoot = model.elements.get('AssetDeclaration');
        for (const field of assetRoot ? model.types.get(assetRoot.type)?.children ?? [] : []) {
            if (!model.elements.has(field.name)) model.elements.set(field.name, {...field, namespace: ASSET});
        }
        model.version = digest.digest('hex');
        return model;
    }
    private fail(message: string): void { this.complete = false; if (this.issues.length < 100 && !this.issues.includes(message)) this.issues.push(message); }
    private typeKey(node: XmlElement, declaration: Declaration): string { return `${declaration.uri}#${node.path}`; }
    private resolveType(name: string): XsdType | undefined {
        name = this.rawTypes.has(name) ? name : local(name);
        const cached = this.types.get(name); if (cached) return cached;
        const declaration = this.rawTypes.get(name);
        if (!declaration) {
            if (name === 'boolean') return {name, namespace: XSD, attributes: [], children: [], enumValues: ['true', 'false', '0', '1']};
            return undefined;
        }
        const {node} = declaration;
        const type: XsdType = {name, namespace: declaration.namespace, attributes: [], children: [], enumValues: [], documentation: documentation(node), sourceUri: declaration.uri};
        this.types.set(name, type);
        if (this.resolving.has(name)) return type;
        this.resolving.add(name);
        const extension = node.children.flatMap(child => ['complexContent', 'simpleContent'].includes(child.localName) ? child.children : [child])
            .find(child => child.namespaceUri === XSD && ['extension', 'restriction'].includes(child.localName));
        const body = extension ?? node;
        type.base = body.attributes.base ? local(body.attributes.base) : undefined;
        const base = type.base ? this.resolveType(type.base) : undefined;
        type.attributes = [...base?.attributes ?? []]; type.children = [...base?.children ?? []];
        type.enumValues = children(body, 'enumeration').map(value => value.attributes.value).filter(value => value !== undefined);
        if (!type.enumValues.length) type.enumValues = [...base?.enumValues ?? []];
        type.referenceType = annotation(node, 'refType') ?? annotation(body, 'refType') ?? base?.referenceType;
        const list = children(body, 'list')[0];
        type.list = !!list || base?.list;
        if (list) {
            const item = list.attributes.itemType ? this.resolveType(local(list.attributes.itemType)) : undefined;
            type.referenceType ??= item?.referenceType;
            if (!type.enumValues.length) type.enumValues = [...item?.enumValues ?? []];
        }
        const collect = (container: XmlElement, optional: boolean, groupStack: Set<string>): void => {
            for (const child of container.children) {
                if (child.namespaceUri !== XSD) continue;
                if (child.localName === 'element') {
                    const value = this.field(child, declaration, false); if (optional) { value.required = false; value.minOccurs = 0; }
                    const previous = type.children.findIndex(field => field.name === value.name); if (previous < 0) type.children.push(value); else type.children[previous] = value;
                } else if (child.localName === 'attribute') {
                    const value = this.field(child, declaration, true);
                    const previous = type.attributes.findIndex(field => field.name === value.name);
                    if (child.attributes.use === 'prohibited') { if (previous >= 0) type.attributes.splice(previous, 1); }
                    else if (previous < 0) type.attributes.push(value); else type.attributes[previous] = value;
                } else if (['sequence', 'all', 'choice'].includes(child.localName)) {
                    collect(child, optional || child.attributes.minOccurs === '0' || child.localName === 'choice', groupStack);
                } else if (['group', 'attributeGroup'].includes(child.localName)) {
                    const ref = local(child.attributes.ref ?? ''); const source = (child.localName === 'group' ? this.groups : this.attributeGroups).get(ref);
                    if (source && !groupStack.has(ref)) collect(source.node, optional || child.attributes.minOccurs === '0', new Set([...groupStack, ref]));
                }
            }
        };
        collect(body, false, new Set());
        this.resolving.delete(name);
        return type;
    }
    private field(node: XmlElement, declaration: Declaration, attribute: boolean): XsdField {
        const ref = node.attributes.ref ? local(node.attributes.ref) : undefined;
        const referred = ref ? (attribute ? this.globalAttributes : this.rawElements).get(ref) : undefined;
        const effective = referred?.node ?? node; const context = referred ?? declaration;
        let typeName = local(effective.attributes.type ?? 'string');
        const inline = effective.children.find(child => child.namespaceUri === XSD && ['simpleType', 'complexType'].includes(child.localName));
        if (inline) { typeName = this.typeKey(inline, context); if (!this.rawTypes.has(typeName)) this.rawTypes.set(typeName, {...context, node: inline}); }
        const type = this.resolveType(typeName);
        const minOccurs = attribute ? undefined : Number(node.attributes.minOccurs ?? '1');
        const maxOccurs = attribute ? undefined : node.attributes.maxOccurs === 'unbounded' ? Infinity : Number(node.attributes.maxOccurs ?? '1');
        return {name: node.attributes.name ?? ref ?? '', type: typeName, required: attribute ? node.attributes.use === 'required' : (minOccurs ?? 0) > 0,
            default: node.attributes.default ?? effective.attributes.default, fixed: node.attributes.fixed ?? effective.attributes.fixed,
            enumValues: [...type?.enumValues ?? []], referenceType: annotation(node, 'refType') ?? annotation(effective, 'refType') ?? type?.referenceType,
            documentation: documentation(node) ?? documentation(effective) ?? type?.documentation, minOccurs, maxOccurs, list: type?.list, sourceUri: context.uri};
    }
    getElement(element: string | XmlElement): XsdElement | undefined {
        if (typeof element === 'string') return this.elements.get(local(element));
        const chain: XmlElement[] = []; for (let current: XmlElement | undefined = element; current; current = current.parent) chain.unshift(current);
        let result = this.elements.get(chain[0]?.localName ?? '');
        for (const child of chain.slice(1)) result = this.types.get(result?.type ?? '')?.children.find(field => field.name === child.localName);
        return result;
    }
    getType(name: string): XsdType | undefined { return this.types.get(name) ?? this.types.get(local(name)) ?? this.resolveType(name); }
    getAttributes(element: string | XmlElement): XsdField[] { return this.getType(this.getElement(element)?.type ?? '')?.attributes ?? []; }
    getChildren(element: string | XmlElement): XsdField[] { return this.getType(this.getElement(element)?.type ?? '')?.children ?? []; }
    /** XSD reference targets may name a base asset type rather than a concrete
     * declaration (for example BaseRenderAssetType accepts Texture). */
    isAssignable(actualType: string, expectedType: string): boolean {
        const normalized = (name: string): string => local(name) === 'FXParticleSystem' ? 'FXParticleSystemTemplate' : local(name);
        const expected = normalized(expectedType), actual = normalized(actualType);
        if (actual === expected) return true;
        const target = this.getElement(expected)?.type ?? expected;
        let current: string | undefined = this.getElement(actual)?.type ?? actual;
        const seen = new Set<string>();
        while (current && !seen.has(current)) {
            if (current === target || normalized(current) === expected) return true;
            seen.add(current); current = this.getType(current)?.base;
        }
        return false;
    }
    isReferenceList(element: string | XmlElement, attributeName?: string): boolean {
        const field = attributeName ? this.getAttributes(element).find(value => value.name === local(attributeName)) : this.getElement(element);
        return !!(field?.list || this.getType(field?.type ?? '')?.list);
    }
    getReferenceType(element: string | XmlElement, attributeName?: string): string | undefined {
        if (attributeName) return this.getAttributes(element).find(field => field.name === local(attributeName))?.referenceType;
        const field = this.getElement(element); return field?.referenceType ?? this.getType(field?.type ?? '')?.referenceType;
    }
}
