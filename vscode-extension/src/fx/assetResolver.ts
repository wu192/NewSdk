import type { ParticleConfig } from '../../webview/fx/runtime';
import { compileParticle } from '../../webview/fx/runtime';
import { AssetIndex, AssetRecord, sourceOf } from './assetIndex';
import { evaluateExpression, parseScalar, XmlElement } from './xmlDocument';
import { defaultScene, FxField, FxIssue, FxNode, FxScene, FxSystem, FxTexture, PreviewIR, SourceLocation } from './previewProtocol';

interface Origin { asset: AssetRecord; uri: string; node: XmlElement; }
interface EffectiveNode { name: string; attributes: Record<string, string>; children: EffectiveNode[]; origin: Origin; attributeOrigins: Record<string, Origin>; }
const metadata = new Set(['id', 'inheritFrom', 'buildRule', 'typeHashCode']);
const labels: Record<string, string> = {
    Type: '粒子类型', Shader: '混合模式', ParticleTexture: '贴图资源', IsOneShot: '单次系统', SystemLifetime: '系统发射窗口（逻辑帧，0 无限）',
    Low: '下界', High: '上界', Lifetime: '粒子寿命（逻辑帧）', Size: '尺寸', BurstCount: '每批粒子数', BurstDelay: '批次间隔（逻辑帧）',
    InitialDelay: '初始延迟', StartSizeRate: '出生尺寸增量', VelocityDamping: '速度阻尼', Gravity: '重力', DriftVelocity: '漂移速度',
    SizeRate: '尺寸增长', SizeRateDamping: '尺寸增长阻尼', AngleZ: 'Z 角度（弧度）', AngularRateZ: 'Z 角速度（弧度/帧）',
    AngleXY: 'XY 角度（弧度）', AngularRateXY: 'XY 角速度（弧度/帧）', AngularDamping: '角阻尼', AngularDampingXY: 'XY 角阻尼',
    ColorScale: 'RGBA 缩放', RelativeAge: '相对寿命百分比', Frame: '槽号', Colors: 'GPU RGBA 共用时间（槽1～4）', Alphas: '透明度（时间字段此 GPU 路径不使用）',
    r: '红', g: '绿', b: '蓝', Count: '系统实例数', Particle: '共享模板', SystemLife: '本次调用系统寿命（Time）',
    X: 'X', Y: 'Y', Z: 'Z', x: 'X', y: 'Y', z: 'Z', FramesPerRow: 'Atlas 每行格数', TotalFrames: 'Atlas 总帧',
    SingleRow: 'Atlas 指定行', RandTex: 'Atlas 随机模式', SpeedMultiplier: 'Atlas 播放倍率', GeometryType: '几何类型',
    File: '源贴图文件', OnlyIfOnLand: '仅陆地触发', OnlyIfOnWater: '仅水面触发', StopIfPlayed: '执行槽调用后停止后续项',
};
const editableAttributes = new Set(['Low','High','Distribution','RelativeAge','r','g','b','X','Y','Z','x','y','z','Radius','Length','RadiusRate',
    'HalfSize','IsHollow','Gravity','IsOneShot','SystemLifetime','Shader','ParticleTexture','IsGroundAligned','SortLevel','SortParticles',
    'FramesPerRow','TotalFrames','SingleRow','RandTex','SpeedMultiplier','Count','Particle','SystemLife','OnlyIfOnLand','OnlyIfOnWater','StopIfPlayed',
    'UseTarget','CreateAtGroundHeight','OrientToObject','TargetCoeff','File','GeometryType','ShaderName','Motion']);
const booleanAttributes = new Set(['IsOneShot','IsHollow','IsGroundAligned','SortParticles','OnlyIfOnLand','OnlyIfOnWater','StopIfPlayed','UseTarget','CreateAtGroundHeight','OrientToObject']);
function plain(node: EffectiveNode): FxNode { return { name: node.name, attributes: { ...node.attributes }, children: node.children.map(plain) }; }
function clone(node: EffectiveNode): EffectiveNode { return { ...node, attributes: {...node.attributes}, attributeOrigins: {...node.attributeOrigins}, children: node.children.map(clone) }; }
function find(node: EffectiveNode, name: string): EffectiveNode | undefined { return node.children.find(child => child.name === name); }
function source(origin: Origin, attribute?: string): SourceLocation {
    const token = attribute ? origin.node.attributeTokens[attribute] : undefined;
    return { uri: origin.uri, nodePath: origin.node.path, start: token?.valueStart ?? origin.node.start, end: token?.valueEnd ?? origin.node.openEnd };
}
function problem(code: string, message: string, origin: Origin, attribute?: string, severity: FxIssue['severity'] = 'error'): FxIssue {
    return {...source(origin, attribute), code, message, severity};
}
function bool(value: string | undefined): boolean { return value?.toLowerCase() === 'true' || value === '1'; }
function isMetadata(name: string): boolean { return name === 'xmlns' || name.startsWith('xmlns:') || name.endsWith(':joinAction') || name === 'inheritFrom' || name === 'buildRule' || name === 'typeHashCode'; }

/** Resolves only the chosen asset's typed references; unknown branches stay visible. */
export class AssetResolver {
    constructor(readonly index: AssetIndex) {}
    private origin(asset: AssetRecord, node = asset.node, uri = asset.uri): Origin { return {asset, node, uri}; }
    private issueAtAsset(code: string, message: string, asset: AssetRecord): FxIssue { return problem(code, message, this.origin(asset)); }
    private select(type: string, reference: string, from: AssetRecord, issues: FxIssue[], origin = this.origin(from), attribute?: string): AssetRecord | undefined {
        let candidates = this.index.candidates(type, reference, from.namespace);
        // A local instance intentionally replaces a reference-stream definition. Two
        // concrete definitions remain ambiguous; filesystem order is never precedence.
        const concrete = candidates.filter(candidate => candidate.includeType !== 'reference');
        if (concrete.length) candidates = concrete;
        if (candidates.length !== 1) {
            issues.push(problem(candidates.length ? 'asset-ambiguous' : 'asset-missing', candidates.length
                ? `${type}:${reference} 有多个候选来源：${candidates.map(a => a.uri).join('、')}`
                : `在当前入口找不到 ${type}:${reference}`, origin, attribute));
            return undefined;
        }
        return candidates[0];
    }
    private fromXml(asset: AssetRecord, node: XmlElement, issues: FxIssue[], uri = asset.uri, includeStack: string[] = []): EffectiveNode {
        const origin = this.origin(asset, node, uri); const attributes: Record<string, string> = {}; const attributeOrigins: Record<string, Origin> = {};
        for (const [name, value] of Object.entries(node.attributes)) if (!isMetadata(name)) { attributes[name] = value; attributeOrigins[name] = origin; }
        const children: EffectiveNode[] = [];
        for (const child of node.children) {
            const edge = this.index.graph.includes.find(e => e.mode === 'xml' && e.from === uri && e.source.nodePath === child.path);
            if (edge) {
                if (includeStack.includes(edge.to)) { issues.push(problem('xinclude-cycle', `xi:include 循环：${[...includeStack, edge.to].join(' → ')}`, this.origin(asset, child, uri))); continue; }
                const target = this.index.getDocument(edge.to);
                if (!target?.valid) { issues.push(problem('xinclude-missing', 'xi:include 目标尚不可用', this.origin(asset, child, uri))); continue; }
                const roots = target.roots.flatMap(root => root.localName === 'AssetDeclaration' ? root.children : [root]);
                for (const imported of roots) children.push(this.fromXml(asset, imported, issues, edge.to, [...includeStack, edge.to]));
            } else children.push(this.fromXml(asset, child, issues, uri, includeStack));
        }
        return {name: node.localName, attributes, attributeOrigins, children, origin};
    }
    private merge(parent: EffectiveNode, child: EffectiveNode, issues: FxIssue[]): EffectiveNode {
        const result = clone(parent);
        result.origin = child.origin;
        Object.assign(result.attributes, child.attributes); Object.assign(result.attributeOrigins, child.attributeOrigins);
        const key = (node: EffectiveNode): string => `${node.name}:${node.attributes.id ?? ''}`;
        for (const incoming of child.children) {
            const actionEntry = Object.entries(incoming.origin.node.attributes).find(([name]) => name.endsWith(':joinAction'));
            const action = actionEntry?.[1] ?? 'Merge';
            const matching = result.children.map((node, index) => ({node, index})).filter(value => key(value.node) === key(incoming));
            if (!['Merge','Replace','Remove','Append'].includes(action)) {
                issues.push(problem('inherit-join-action', `尚不支持 xai:joinAction=${action}`, incoming.origin, actionEntry?.[0])); continue;
            }
            if (action === 'Append') { result.children.push(clone(incoming)); continue; }
            const repeatedSequence = (parent.name === 'Colors' && incoming.name === 'Color') || (parent.name === 'Alphas' && incoming.name === 'Alpha') || parent.name === 'NuggetList';
            if (repeatedSequence && !incoming.attributes.id && matching.length) {
                issues.push(problem('inherit-sequence-contract', `继承 ${parent.name}/${incoming.name} 的无 id 序列合并合同尚未验证；请显式 Replace 整个 ${parent.name}`, incoming.origin)); continue;
            }
            if (matching.length > 1) {
                issues.push(problem('inherit-ambiguous', `继承节点 ${incoming.name} 缺少唯一 id，无法确定合并目标`, incoming.origin)); continue;
            }
            if (action === 'Remove') {
                if (matching.length) result.children.splice(matching[0].index, 1);
                else issues.push(problem('inherit-remove', `删除的父节点 ${incoming.name} 不存在`, incoming.origin));
            } else if (!matching.length) result.children.push(clone(incoming));
            else result.children[matching[0].index] = action === 'Replace' ? clone(incoming) : this.merge(matching[0].node, incoming, issues);
        }
        return result;
    }
    private effective(asset: AssetRecord, issues: FxIssue[], stack: string[] = []): EffectiveNode | undefined {
        if (stack.includes(asset.key)) {
            issues.push(this.issueAtAsset('inherit-cycle', `继承循环：${[...stack.slice(stack.indexOf(asset.key)), asset.key].map(key => this.index.getAsset(key)?.id ?? key).join(' → ')}`, asset)); return undefined;
        }
        const own = this.fromXml(asset, asset.node, issues);
        if (!asset.node.attributes.inheritFrom) return own;
        const parent = this.select(asset.type, asset.node.attributes.inheritFrom, asset, issues, this.origin(asset), 'inheritFrom');
        if (!parent) return undefined;
        const inherited = this.effective(parent, issues, [...stack, asset.key]);
        return inherited ? this.merge(inherited, own, issues) : undefined;
    }
    private symbols(issues: FxIssue[]): Record<string, number> {
        const definitions = new Map<string, {value: string; origin: Origin; override: boolean}[]>();
        const fallbackAsset = this.index.listAssets()[0];
        if (!fallbackAsset) return {};
        for (const [uri, document] of this.index.graph.documents) for (const node of document.elements) {
            if (node.localName !== 'Define' || node.parent?.localName !== 'Defines') continue;
            const name = node.attributes.name; if (!name) continue;
            const list = definitions.get(name) ?? []; list.push({value: node.attributes.value ?? '', origin: this.origin(fallbackAsset, node, uri), override: bool(node.attributes.override)}); definitions.set(name, list);
        }
        const pending = new Map<string, {value: string; origin: Origin}>(); const symbols: Record<string, number> = {};
        for (const [name, values] of definitions) {
            const overrides = values.filter(d => d.override); const chosen = overrides.length ? overrides : values;
            if (new Set(chosen.map(d => d.value)).size !== 1) { issues.push(problem('define-conflict', `宏 ${name} 有冲突来源，不能按文件遍历顺序覆盖`, chosen[0].origin)); continue; }
            pending.set(name, chosen[0]);
        }
        for (let pass = 0; pass < 32 && pending.size; pass++) {
            let changed = false;
            for (const [name, definition] of pending) {
                const value = evaluateExpression(definition.value, symbols);
                if (value.status === 'evaluated') { symbols[name] = value.value; symbols[`$${name}`] = value.value; pending.delete(name); changed = true; }
            }
            if (!changed) break;
        }
        // Unused string/object defines do not stop an unrelated particle; unresolved
        // references produce precise issues when a selected field consumes them.
        return symbols;
    }
    private expandExpressions(node: EffectiveNode, symbols: Record<string, number>, issues: FxIssue[]): void {
        for (const [name, value] of Object.entries(node.attributes)) {
            if (!/^\s*(?:=|\$)/.test(value)) continue;
            const result = evaluateExpression(value, symbols);
            if (result.status === 'evaluated') node.attributes[name] = String(result.value);
            else issues.push(problem('expression-unresolved', `${name} 尚未求值：${result.reason}`, node.attributeOrigins[name] ?? node.origin, name));
        }
        for (const child of node.children) this.expandExpressions(child, symbols, issues);
    }
    private fields(node: EffectiveNode, selectedAsset: AssetRecord, prefix: number[] = [], group = node.name): FxField[] {
        const result: FxField[] = [];
        const atlasDefaults: Record<string,string> = node.name === 'Gpu' ? {FramesPerRow:'1',TotalFrames:'1',SingleRow:'0',RandTex:'0',SpeedMultiplier:'1.0'} : {};
        for (const [attribute, value] of Object.entries({...atlasDefaults, ...node.attributes})) {
            const origin = node.attributeOrigins[attribute] ?? node.origin;
            const token = origin.node.attributeTokens[attribute]; const defaulted = !token && Object.hasOwn(atlasDefaults,attribute); if (!token && !defaulted) continue;
            let choices: string[] | undefined;
            if (attribute === 'Shader' && node.name === 'FXParticleSystemTemplate') choices = ['ADDITIVE','ALPHA','MULTIPLY'];
            if (attribute === 'RandTex') choices = ['0','1','2'];
            if (attribute === 'Distribution') choices = ['CONSTANT','UNIFORM'];
            const inherited = origin.asset.key !== selectedAsset.key || origin.uri !== selectedAsset.uri;
            const location = token ? source(origin, attribute) : {...source(origin), start:origin.node.openEnd-(origin.node.selfClosing?2:1), end:origin.node.openEnd-(origin.node.selfClosing?2:1)};
            result.push({...location, defaulted, key: JSON.stringify([origin.uri, origin.node.path, attribute]), attribute,
                label: labels[attribute] ?? attribute, value: token?.value ?? value, readonly: this.index.isReadonly(origin.uri) || !editableAttributes.has(attribute) || metadata.has(attribute) || (node.name === 'Gpu' && ['Shader','GeometryType'].includes(attribute)),
                inherited, sourceAsset: origin.asset.key, previewPath: prefix, group: labels[group] ?? group,
                kind: choices ? 'enum' : booleanAttributes.has(attribute) ? 'boolean' : /^[-+\d.=$]/.test(value.trim()) ? 'number' : 'text', choices});
        }
        node.children.forEach((child, i) => result.push(...this.fields(child, selectedAsset, [...prefix, i], prefix.length === 0 ? child.name : group)));
        return result;
    }
    private runtimeIssues(diagnostics: ReturnType<typeof compileParticle>['issues'], node: EffectiveNode): FxIssue[] {
        return diagnostics.map(diagnostic => {
            const parts = diagnostic.path.replace(/^\/?FXParticleSystemTemplate\/?/, '').split('/').filter(Boolean);
            let current = node; let attribute: string | undefined;
            for (const part of parts) {
                if (part.startsWith('@')) { attribute = part.slice(1); break; }
                const child = current.children.find(value => value.name === part.replace(/\[.*\]$/, ''));
                if (child) current = child;
                else if (current.attributes[part] !== undefined) attribute = part;
            }
            return problem(diagnostic.code, diagnostic.message, attribute ? current.attributeOrigins[attribute] ?? current.origin : current.origin, attribute, diagnostic.severity);
        });
    }
    private async texture(template: AssetRecord, node: EffectiveNode, issues: FxIssue[]): Promise<FxTexture> {
        const name = node.attributes.ParticleTexture ?? '';
        const key = `texture:${template.key}:${name}`;
        let location = source(node.attributeOrigins.ParticleTexture ?? node.origin, 'ParticleTexture');
        if (!name) return {key, name, location, source:'source', status:'empty-reference', message:'ParticleTexture 尚未选择贴图'};
        const declaration = this.select('Texture', name, template, issues, node.attributeOrigins.ParticleTexture ?? node.origin, 'ParticleTexture');
        if (!declaration) return {key, name, location, source:'source', status:'missing-declaration', message:`Texture 声明不存在或有多个候选：${name}`};
        const resolved = this.effective(declaration, issues);
        if (!resolved) return {key, name, location, source:'source', status:'invalid-declaration', message:'Texture 继承无法解析'};
        const file = resolved.attributes.File;
        location = source(resolved.attributeOrigins.File ?? resolved.origin, 'File');
        if (file === undefined) {
            const empty = Object.keys(resolved.attributes).every(attr => attr === 'id');
            return {key, name, location, source:'source', status:empty ? 'empty-declaration':'missing-file', message:empty ? 'Texture 是空声明，没有可预览的源素材':'Texture 缺少 File 属性'};
        }
        if (!file.trim()) return {key, name, location, source:'source', status:'empty-file', message:'Texture.File 为空'};
        if (resolved.attributes.Type && resolved.attributes.Type !== 'StandardTexture') return {key, name, location, source:'source', status:'unsupported-type', message:`暂不支持 Texture.Type=${resolved.attributes.Type}`};
        try { return {key, name, location, source:'source', status:'pending', file: await this.index.resolveFile(resolved.attributeOrigins.File?.uri ?? declaration.uri, file)}; }
        catch (error) { return {key, name, location, source:'source', status:'invalid-file', message:error instanceof Error ? error.message:String(error)}; }
    }
    private numeric(node: EffectiveNode, attribute: string, fallback: number, issues: FxIssue[], kind: 'number'|'time'|'angle' = 'number'): number {
        const lexical = node.attributes[attribute]; if (lexical === undefined) return fallback;
        const parsed = parseScalar(lexical, kind);
        if (parsed.status !== 'evaluated' || !Number.isFinite(parsed.value)) { issues.push(problem('invalid-value', `${attribute}：${parsed.status === 'evaluated' ? '数值必须有限' : parsed.reason}`, node.attributeOrigins[attribute] ?? node.origin, attribute)); return fallback; }
        return parsed.value;
    }
    async resolve(assetKey: string, sceneValues: Partial<FxScene> = {}, signal?: AbortSignal): Promise<PreviewIR> {
        const asset = this.index.getAsset(assetKey); if (!asset) throw new Error('所选 FX 资产不属于当前入口，请重新选择');
        const scene: FxScene = {...defaultScene, ...sceneValues, source: [...(sceneValues.source ?? defaultScene.source)], target: [...(sceneValues.target ?? defaultScene.target)]};
        if (![...scene.source, ...scene.target, scene.ground, scene.seed].every(Number.isFinite)) throw new Error('预览场景数值必须有限');
        const issues = [...this.index.graph.issues]; const systems: FxSystem[] = []; const textures: FxTexture[] = [];
        const symbols = this.symbols(issues);
        const rootIssueStart = issues.length;
        const effective = this.effective(asset, issues);
        const rootResolutionFailed = issues.slice(rootIssueStart).some(issue => issue.severity === 'error');
        if (effective) this.expandExpressions(effective, symbols, issues);
        const fields = effective ? this.fields(effective, asset) : [];
        const addSystem = async (template: AssetRecord, call: EffectiveNode | undefined, instance: number, callIndex: number): Promise<void> => {
            if (signal?.aborted) throw new Error('FX preview cancelled');
            const systemIssues: FxIssue[] = [];
            const definition = this.effective(template, systemIssues); if (definition) this.expandExpressions(definition, symbols, systemIssues);
            const compiled = definition ? compileParticle(plain(definition)) : undefined;
            if (compiled && definition) systemIssues.push(...this.runtimeIssues(compiled.issues, definition));
            const textureIssues: FxIssue[] = [];
            const texture = definition ? await this.texture(template, definition, textureIssues) : undefined;
            if (texture && texture.status !== 'pending' && !textureIssues.length) textureIssues.push({...texture.location!,code:`texture-${texture.status}`,message:texture.message ?? texture.status,severity:'warning'});
            systemIssues.push(...textureIssues.map(issue => ({...issue,severity:'warning' as const})));
            if (texture && !textures.some(t => t.key === texture.key)) textures.push(texture);
            let triggered = true; const offset: [number,number,number] = [...scene.source]; let rotation = 0; let rotationXYZ: [number,number,number] | undefined;
            let systemLifeFrames: number | undefined; let initialDelayOverrideMs: FxSystem['initialDelayOverrideMs']; let config: ParticleConfig | undefined = compiled?.config;
            if (call) {
                const supportedAttributes = new Set(['Particle','Count','SystemLife','OnlyIfOnLand','OnlyIfOnWater','StopIfPlayed','Weather','OrientToObject','CreateAtGroundHeight','UseTarget','Ricochet','SetTargetMatrix','TargetCoeff']);
                for (const [name,value] of Object.entries(call.attributes)) if (!supportedAttributes.has(name) && value && !metadata.has(name)) systemIssues.push(problem('nugget-context', `ParticleSystem.${name} 尚无已实现的调用上下文`, call.attributeOrigins[name] ?? call.origin, name));
                for (const name of ['OrientToObject','UseTarget','Ricochet','SetTargetMatrix']) if (bool(call.attributes[name])) systemIssues.push(problem('nugget-context', `ParticleSystem.${name} 需要对象/目标矩阵上下文，本轮普通位置预览未播放此分支`, call.attributeOrigins[name] ?? call.origin, name));
                if (call.attributes.TargetCoeff !== undefined && this.numeric(call,'TargetCoeff',1,systemIssues) !== 1) systemIssues.push(problem('nugget-context','TargetCoeff 需要目标分支上下文',call.origin,'TargetCoeff'));
                if (call.attributes.Weather && call.attributes.Weather !== 'INVALID') systemIssues.push(problem('weather-context', `需要 Weather=${call.attributes.Weather} 上下文`, call.origin, 'Weather'));
                const placement = find(call,'Offset'); if (placement) ['x','y','z'].forEach((axis,i) => { offset[i] += this.numeric(placement,axis,0,systemIssues); });
                if (bool(call.attributes.CreateAtGroundHeight)) offset[2] = scene.ground + (placement ? this.numeric(placement,'z',0,systemIssues) : 0);
                const rotate = find(call,'Rotate'); if (rotate) {
                    rotationXYZ = ['X','Y','Z'].map(axis => this.numeric(rotate,axis,0,systemIssues,'angle')) as [number,number,number];
                    rotation = rotationXYZ[2];
                    if (rotationXYZ[0] !== 0 || rotationXYZ[1] !== 0) systemIssues.push(problem('rotate-contract', 'Rotate XYZ 采用 EA 同系矩阵合同 Rx·Ry·Rz，尚无 RA3 实机画面对照', rotate.origin, undefined, 'warning'));
                }
                const delay = find(call,'InitialDelay'); if (delay && config) {
                    const low = this.numeric(delay,'Low',-1,systemIssues); const high = this.numeric(delay,'High',-1,systemIssues);
                    const type = delay.attributes.Type ?? 'CONSTANT';
                    if (!['CONSTANT','UNIFORM'].includes(type)) systemIssues.push(problem('nugget-delay-type', `InitialDelay.Type=${type} 尚未实现`, delay.origin, 'Type'));
                    if (low >= 0 && high >= low && ['CONSTANT','UNIFORM'].includes(type)) initialDelayOverrideMs = {low,high,type:type as 'CONSTANT'|'UNIFORM'};
                    else if (!(low === -1 && high === -1)) systemIssues.push(problem('nugget-delay', 'InitialDelay 需非负毫秒范围或默认 -1,-1', delay.origin));
                }
                const lifetime = this.numeric(call,'SystemLife',-1,systemIssues,'time');
                if (lifetime >= 0) systemLifeFrames = Math.ceil(lifetime*30);
                else if (lifetime !== -1) systemIssues.push(problem('nugget-lifetime','SystemLife 只支持非负 Time 或默认 -1s',call.origin,'SystemLife'));
                for (const child of call.children) if (!['Rotate','Offset','InitialDelay'].includes(child.name)) systemIssues.push(problem('nugget-module', `ParticleSystem/${child.name} 暂未实现`, child.origin));
                if (bool(call.attributes.OnlyIfOnLand) && scene.terrain === 'water' || bool(call.attributes.OnlyIfOnWater) && scene.terrain === 'land') {
                    triggered = false; systemIssues.push(problem('condition-not-triggered', '当前陆地/水面条件不满足，未触发', call.origin, undefined, 'information'));
                }
            }
            if (systemIssues.some(i => i.severity === 'error')) config = undefined;
            const system: FxSystem = {key:`${asset.key}/call/${callIndex}/${instance}`, name:`${template.id}${call ? ` · 调用 ${callIndex+1}.${instance+1}`:''}`,
                templateKey:template.key, source:call ? source(call.origin):source(this.origin(template)), definition:definition ? plain(definition):undefined,
                config, fields:definition ? this.fields(definition, template):[], issues:systemIssues, offset, rotation, rotationXYZ, startFrame:0, systemLifeFrames, initialDelayOverrideMs,
                textureKey:texture?.key, triggered};
            systems.push(system); issues.push(...systemIssues);
        };
        if (effective && asset.type === 'FXParticleSystemTemplate') await addSystem(asset,undefined,0,0);
        else if (effective && asset.type === 'FXList' && !rootResolutionFailed) {
            const list = find(effective,'NuggetList');
            if (!list) issues.push(this.issueAtAsset('nugget-list-missing','FXList 缺少 NuggetList',asset));
            let callIndex = 0;
            for (const nugget of list?.children ?? []) {
                if (signal?.aborted) throw new Error('FX preview cancelled');
                if (nugget.name !== 'ParticleSystem') {
                    issues.push(problem('nugget-unsupported', `${nugget.name} 节点保留在 XML 中，当前未播放`, nugget.origin, undefined, 'warning'));
                    if (bool(nugget.attributes.StopIfPlayed)) { issues.push(problem('dispatch-stop-context', `${nugget.name} 执行槽尚未实现，无法可靠继续 StopIfPlayed 调度`, nugget.origin)); break; }
                    continue;
                }
                const invalidBoolean = Object.entries(nugget.attributes).find(([name,value]) => booleanAttributes.has(name) && !/^(?:[Tt]rue|[Ff]alse|0|1)$/.test(value));
                if (invalidBoolean) {
                    issues.push(problem('invalid-boolean', `${invalidBoolean[0]} 不是合法 SageBool`, nugget.origin, invalidBoolean[0]));
                    if (invalidBoolean[0] === 'StopIfPlayed') break;
                    continue;
                }
                const countIssueStart = issues.length;
                const count = this.numeric(nugget,'Count',1,issues);
                if (issues.slice(countIssueStart).some(issue => issue.severity === 'error')) continue;
                if (!Number.isInteger(count)) { issues.push(problem('nugget-count','Count 必须为整数系统实例数',nugget.origin,'Count')); continue; }
                if (count > 128 || systems.length + Math.max(count,0) > 128) { issues.push(problem('system-budget','预览系统实例预算已达上限（128）',nugget.origin,'Count')); continue; }
                const particle = nugget.attributes.Particle ?? '';
                const template = particle ? this.select('FXParticleSystemTemplate',particle,asset,issues,nugget.origin,'Particle'):undefined;
                if (!particle) issues.push(problem('particle-reference-empty','ParticleSystem.Particle 为空，未创建系统',nugget.origin,'Particle','information'));
                if (template) for (let i=0; i<count; i++) await addSystem(template,nugget,i,callIndex);
                callIndex++;
                // Land/water are backend checks after the execution slot; they do
                // not prevent StopIfPlayed from ending the parent traversal.
                if (bool(nugget.attributes.StopIfPlayed) && (!nugget.attributes.Weather || nugget.attributes.Weather === 'INVALID')) break;
            }
        } else if (effective && asset.type !== 'FXList') issues.push(this.issueAtAsset('asset-not-previewable', `${asset.type} 只提供结构与资源查看`, asset));
        const document = this.index.getDocument(asset.uri);
        return {schemaVersion:1,entry:asset.entry,assetKey:asset.key,name:asset.id,documentUri:asset.uri,documentVersion:document?.version ?? 0,
            generation:this.index.generation,systems,fields,textures,issues,dependencies:[...this.index.graph.dependencies],scene};
    }
}
