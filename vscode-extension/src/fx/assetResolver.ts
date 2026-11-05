import type { ParticleConfig } from '../../webview/fx/runtime';
import { compileParticle } from '../../webview/fx/runtime';
import { localRotationMatrix, transformVector } from '../../webview/fx/runtime/math';
import { AssetIndex, AssetRecord, sourceOf } from './assetIndex';
import { fxModelConditionNames } from './capabilities';
import { evaluateExpression, parseScalar, XmlElement } from './xmlDocument';
import { defaultScene, FxEventList, FxField, FxIssue, FxNode, FxScene, FxSystem, FxTexture, PreviewIR, SourceLocation } from './previewProtocol';

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
    TrailLife: '拖尾保留帧数', UTile: 'U 方向平铺', VTile: 'V 方向平铺', UScrollRate: 'U 滚动速度', VScrollRate: 'V 滚动速度',
    OpaqueSpeed: '完全不透明速度', TransparentSpeed: '完全透明速度', SpeedStretchAmount: '速度拉伸倍率', AttractStrength: '吸引强度', EnvironmentTexture: '环境贴图资源',
    StartAmplitude: '初始振幅', EndAmplitudeChange: '振幅变化', StartFrequency: '初始频率', EndFrequencyChange: '频率变化', StartPhase: '初始相位', EndPhaseChange: '相位变化',
    StartPoint: '起点', EndPoint: '终点',
};
const editableAttributes = new Set(['Low','High','Distribution','RelativeAge','r','g','b','X','Y','Z','x','y','z','Radius','Length','RadiusRate',
    'HalfSize','IsHollow','Gravity','IsOneShot','SystemLifetime','Shader','ParticleTexture','IsGroundAligned','SortLevel','SortParticles',
    'FramesPerRow','TotalFrames','SingleRow','RandTex','SpeedMultiplier','Count','Particle','SystemLife','OnlyIfOnLand','OnlyIfOnWater','StopIfPlayed',
    'UseTarget','CreateAtGroundHeight','OrientToObject','TargetCoeff','File','GeometryType','ShaderName','Motion', 'Type', 'PerParticleAttachedSystem',
    'EventFX','PerParticle','KillAfterEvent','OrientFXToTerrain','EnableSoftParticles','Rotation','PlayIfSourceIsStealthed','Weather','PlayEvenIfShrouded',
    'CullTracking','CullTrackingMin','CullTrackingMax','RequiredSourceModelConditions','ExcludedSourceModelConditions','RequiredSecondaryModelConditions','ExcludedSecondaryModelConditions',
    'Ricochet','SetTargetMatrix','Swirly','ParticlesAttachToBone','SizeX','SizeY','SizeZ',
    'TrailLife','UTile','VTile','UScrollRate','VScrollRate','OpaqueSpeed','TransparentSpeed','SpeedStretchAmount','AttractStrength','EnvironmentTexture']);
const booleanAttributes = new Set(['IsOneShot','IsHollow','IsGroundAligned','SortParticles','OnlyIfOnLand','OnlyIfOnWater','StopIfPlayed','UseTarget','CreateAtGroundHeight','OrientToObject',
    'PerParticle','KillAfterEvent','OrientFXToTerrain','EnableSoftParticles','PlayIfSourceIsStealthed','PlayEvenIfShrouded','Ricochet','SetTargetMatrix','Swirly','ParticlesAttachToBone']);
const modelConditionAttributes = new Set(['RequiredSourceModelConditions','ExcludedSourceModelConditions','RequiredSecondaryModelConditions','ExcludedSecondaryModelConditions']);
const weatherNames = ['NORMAL','CLOUDY','RAINY','CLOUDYRAINY','SUNNY','SNOWY','INVALID'];
const disabledNames = ['DEFAULT','USER_PARALYZED','EMP','HELD','PARALYZED','UNMANNED','UNDERPOWERED','FREEFALL','TEMPORARILY_BUSY','SCRIPT_DISABLED','SCRIPT_UNDERPOWERED','INFILTRATED'];
const integerAttributes = new Set(['Count','CullTrackingMin','CullTrackingMax']);
const scalarLiterals = {
    number: /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/,
    time: /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?(?:ms|s)?$/,
    angle: /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?(?:r|d)?$/,
};
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
const instanceNamespace = 'uri:ea.com:eala:asset:instance';
function instanceAttribute(node: XmlElement, localName: string): [string,string] | undefined {
    for (const [name,value] of Object.entries(node.attributes)) {
        const colon = name.indexOf(':'); if (colon < 0 || name.slice(colon+1) !== localName) continue;
        const declaration = `xmlns:${name.slice(0,colon)}`;
        let ancestor: XmlElement | undefined = node;
        while (ancestor) {
            if (Object.hasOwn(ancestor.attributes,declaration)) { if (ancestor.attributes[declaration] === instanceNamespace) return [name,value]; break; }
            ancestor = ancestor.parent;
        }
    }
    return undefined;
}
function isMetadata(name: string, node: XmlElement): boolean { return name === 'xmlns' || name.startsWith('xmlns:')
    || instanceAttribute(node,'joinAction')?.[0] === name || instanceAttribute(node,'insertPosition')?.[0] === name
    || name === 'inheritFrom' || name === 'buildRule' || name === 'typeHashCode'; }

/** Resolves only the chosen asset's typed references; unknown branches stay visible. */
export class AssetResolver {
    constructor(readonly index: AssetIndex) {}
    private origin(asset: AssetRecord, node = asset.node, uri = asset.uri): Origin { return {asset, node, uri}; }
    private issueAtAsset(code: string, message: string, asset: AssetRecord): FxIssue { return problem(code, message, this.origin(asset)); }
    private select(type: string, reference: string, from: AssetRecord, issues: FxIssue[], origin = this.origin(from), attribute?: string): AssetRecord | undefined {
        const candidates = this.index.candidates(type, reference, from.namespace, from.scopeUri??from.uri);
        if (candidates.length !== 1) {
            issues.push(problem(candidates.length ? 'asset-ambiguous' : 'asset-missing', candidates.length
                ? `${type}:${reference} 有多个候选来源：${candidates.map(a => a.uri).join('、')}`
                : `在定义文档 ${from.uri} 的可见依赖中找不到 ${type}:${reference}`, origin, attribute));
            return undefined;
        }
        return candidates[0];
    }
    private fromXml(asset: AssetRecord, node: XmlElement, issues: FxIssue[], uri = asset.uri, includeStack: string[] = []): EffectiveNode {
        const origin = this.origin(asset, node, uri); const attributes: Record<string, string> = {}; const attributeOrigins: Record<string, Origin> = {};
        if(node.namespaceUri && node.namespaceUri!==asset.namespace)issues.push(problem('node-namespace',`节点 ${node.name} 的命名空间 ${node.namespaceUri} 不属于该 FX 资产`,origin));
        for (const [name, value] of Object.entries(node.attributes)) if (!isMetadata(name,node)) { attributes[name] = value; attributeOrigins[name] = origin; }
        const children: EffectiveNode[] = [];
        for (const child of node.children) {
            const edge = this.index.graph.includes.find(e => e.mode === 'xml' && e.from === uri && e.source.nodePath === child.path);
            if (edge) {
                if (includeStack.includes(edge.to)) { issues.push(problem('xinclude-cycle', `xi:include 循环：${[...includeStack, edge.to].join(' → ')}`, this.origin(asset, child, uri))); continue; }
                const target = this.index.getDocument(edge.to);
                if (!target?.valid) { issues.push(problem('xinclude-missing', 'xi:include 目标尚不可用', this.origin(asset, child, uri))); continue; }
                const roots = target.roots;
                for (const imported of roots) children.push(this.fromXml(asset, imported, issues, edge.to, [...includeStack, edge.to]));
            } else children.push(this.fromXml(asset, child, issues, uri, includeStack));
        }
        return {name: node.localName, attributes, attributeOrigins, children, origin};
    }
    private joinAction(node: EffectiveNode, issues: FxIssue[], fallback: 'Append'|'Replace'|'Remove' = 'Append'): 'Append'|'Replace'|'Remove' | undefined {
        const entry = instanceAttribute(node.origin.node,'joinAction'); if (!entry) return fallback;
        const action = entry[1].toLowerCase();
        if (action === 'append') return 'Append'; if (action === 'replace' || action === 'overwrite') return 'Replace'; if (action === 'remove') return 'Remove';
        issues.push(problem('inherit-join-action', `xai:joinAction=${entry[1]} 无效；编译器只支持 Append、Replace、Remove`, node.origin, entry[0])); return undefined;
    }
    /** a75840e recovery/src/Managed/Core/.../NodeJoiner.cs: defaults to Append,
     * singleton sequences match names, choices replace their only branch, repeated
     * sequences match id only. Frame is a payload slot, never an inheritance key.
     */
    private merge(parent: EffectiveNode | undefined, child: EffectiveNode, issues: FxIssue[], action: 'Append'|'Replace' = 'Append'): EffectiveNode {
        const result = parent && action !== 'Replace' ? clone(parent) : {...child,attributes:{},attributeOrigins:{},children:[]};
        result.origin = child.origin; result.name = child.name;
        for (const [attribute,value] of Object.entries(child.attributes)) {
            // NodeJoiner.ReplaceNodeAttributes applies +/- only to enum-list XSD
            // attributes. Preserve its sequential substring behavior verbatim.
            if (modelConditionAttributes.has(attribute) && /[+-]/.test(value)) {
                let combined = result.attributes[attribute] ?? '';
                for (const token of value.split(' ')) {
                    const modifier = token[0], flag = token.slice(1);
                    if (!flag || !fxModelConditionNames.has(flag) || !['+','-'].includes(modifier)) {
                        issues.push(problem('inherit-bitflag', `非法 ModelConditions 覆盖 ${token}；每项需要 +标志 或 -标志`, child.attributeOrigins[attribute] ?? child.origin,attribute)); continue;
                    }
                    if (modifier === '+' && !combined.includes(flag)) combined += ` ${flag}`;
                    if (modifier === '-') {
                        if (!combined.includes(flag)) issues.push(problem('inherit-bitflag', `无法删除未继承的 ModelCondition ${flag}`,child.attributeOrigins[attribute] ?? child.origin,attribute));
                        else combined = combined.split(flag).join('');
                    }
                }
                result.attributes[attribute] = combined;
            } else result.attributes[attribute] = value;
            result.attributeOrigins[attribute] = child.attributeOrigins[attribute] ?? child.origin;
        }
        for (const incoming of child.children) {
            const childAction = this.joinAction(incoming,issues,action); if (!childAction) continue;
            const multiple = (child.name === 'Colors' && incoming.name === 'Color') || (child.name === 'Alphas' && incoming.name === 'Alpha') || (child.name === 'Lightning' && incoming.name === 'RandomSet') || child.name === 'NuggetList' || child.name === 'Event' || incoming.name === 'SourceMustNotHaveBeenDisabledThisFrameByType';
            const choice = ['Physics','Draw','Volume','Velocity','Update'].includes(child.name);
            const matching = result.children.map((node,index) => ({node,index})).filter(value => multiple
                ? !!incoming.attributes.id && value.node.attributes.id === incoming.attributes.id
                : choice && result.children.length === 1 || value.node.name === incoming.name);
            if (matching.length > 1) { issues.push(problem('inherit-ambiguous', `继承节点 ${incoming.name} 无法确定唯一合并目标`, incoming.origin)); continue; }
            const existing = matching[0];
            if (childAction === 'Remove') {
                if (existing) result.children.splice(existing.index,1);
                else issues.push(problem('inherit-remove', `删除的父节点 ${incoming.name} 不存在（多值序列须使用 id 匹配）`, incoming.origin));
                continue;
            }
            const replacement = this.merge(existing?.node.name === incoming.name ? existing.node : undefined,incoming,issues,childAction);
            if (existing) result.children[existing.index] = replacement;
            else {
                const position = instanceAttribute(incoming.origin.node,'insertPosition');
                if (position && !['top','bottom'].includes(position[1].toLowerCase())) { issues.push(problem('inherit-insert-position', `insertPosition=${position[1]} 无效`, incoming.origin,position[0])); continue; }
                if (position?.[1].toLowerCase() === 'top') result.children.unshift(replacement); else result.children.push(replacement);
            }
        }
        if (['Colors','Alphas'].includes(result.name) && result.children.filter(node => node.name === (result.name === 'Colors'?'Color':'Alpha')).length > 8)
            issues.push(problem('inherit-max-occurs', `${result.name} 合并后超过 XSD 的 8 个节点上限`, child.origin));
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
        // a75840e InstanceDeclaration.cs:228-240 removes inheritFrom before XSD;
        // AssetDeclarationDocument.cs:1153 forbids non-BaseInheritableAsset sources
        // unless FindLocation.Self. FXList/particle/Texture are BaseAssetType.
        if ((parent.scopeUri??parent.uri) !== (asset.scopeUri??asset.uri)) { issues.push(problem('inherit-cross-document', `${asset.type} 非 BaseInheritableAsset，只支持同文档 inheritFrom`, this.origin(asset),'inheritFrom')); return undefined; }
        const rootAction = this.joinAction(own,issues); if (!rootAction) return undefined;
        if (rootAction === 'Remove') { issues.push(problem('inherit-remove-root','编译器不支持删除顶层资产',this.origin(asset))); return undefined; }
        const inherited = this.effective(parent, issues, [...stack, asset.key]);
        return inherited ? this.merge(inherited, own, issues,rootAction) : undefined;
    }
    private symbols(issues: FxIssue[], documentUri: string): Record<string, number> {
        type Definition = {value:string;origin:Origin;override:boolean};
        const fallbackAsset = this.index.listAssets()[0]; if (!fallbackAsset) return {};
        const cache = new Map<string,Map<string,Definition>>();
        const collect = (file: string): Map<string,Definition> => {
            const cached = cache.get(file); if (cached) return cached;
            const definitions = new Map<string,Definition>(); cache.set(file,definitions);
            for (const edge of this.index.graph.includes.filter(edge => edge.from === file)) for (const [name,definition] of collect(edge.to)) {
                const existing = definitions.get(name);
                if (existing && existing.origin.uri !== definition.origin.uri) issues.push(problem('define-conflict', `宏 ${name} 经两个不同定义文档导入，编译器禁止此冲突`, definition.origin));
                else definitions.set(name,definition);
            }
            const own = new Set<string>();
            for (const node of this.index.getDocument(file)?.elements ?? []) {
                if (node.localName !== 'Define' || node.parent?.localName !== 'Defines' || !node.attributes.name) continue;
                const name = node.attributes.name; const definition = {value:node.attributes.value ?? '',origin:this.origin(fallbackAsset,node,file),override:node.attributes.override === 'true'};
                if (own.has(name) || definitions.has(name) && !definition.override) issues.push(problem('define-conflict', `宏 ${name} 重复；覆盖导入宏必须显式 override=true`, definition.origin));
                else definitions.set(name,definition);
                own.add(name);
            }
            return definitions;
        };
        const pending = new Map(collect(documentUri)); const symbols: Record<string,number> = {};
        for (let pass=0;pass<32 && pending.size;pass++) {
            let changed = false;
            for (const [name,definition] of pending) {
                const result = evaluateExpression(definition.value,symbols);
                if (result.status === 'evaluated') { symbols[name]=result.value; symbols[`$${name}`]=result.value; pending.delete(name); changed=true; }
            }
            if (!changed) break;
        }
        return symbols;
    }
    private expandExpressions(node: EffectiveNode, symbols: Record<string, number>, issues: FxIssue[]): void {
        for (const [name, value] of Object.entries(node.attributes)) {
            // Pinned AssetDeclarationDocument.ProcessExpressionsInNode tests
            // the very first character. A bare $name is not a compiler expression.
            if (value[0] !== '=') continue;
            const result = evaluateExpression(value,symbols);
            if (result.status === 'evaluated') node.attributes[name] = String(result.value);
            else issues.push(problem('expression-unresolved', `${name} 尚未求值：${result.reason}`, node.attributeOrigins[name] ?? node.origin, name));
        }
        for (const child of node.children) this.expandExpressions(child, symbols, issues);
    }
    private fields(node: EffectiveNode, selectedAsset: AssetRecord, prefix: number[] = [], group = node.name, family = node.attributes.Type ?? 'PARTICLE'): FxField[] {
        const result: FxField[] = [];
        const defaults: Record<string,string> = node.name === 'Gpu' ? {FramesPerRow:'1',TotalFrames:'1',SingleRow:'0',RandTex:'0',SpeedMultiplier:'1.0'}
            : node.name === 'Trail' && group === 'Draw' ? {TrailLife:'30',UTile:'1.0',VTile:'1.0',UScrollRate:'0.0',VScrollRate:'0.0'}
            : node.name === 'Swarm' && group === 'Draw' ? {OpaqueSpeed:'0',TransparentSpeed:'100.0',SpeedStretchAmount:'1.0',AttractStrength:'0.0',EnvironmentTexture:''}
            : node.name === 'Swarm' && group === 'Physics' ? {AttractStrength:'0.0'} : {};
        for (const [attribute, value] of Object.entries({...defaults, ...node.attributes})) {
            const origin = node.attributeOrigins[attribute] ?? node.origin;
            const token = origin.node.attributeTokens[attribute]; const defaulted = !token && Object.hasOwn(defaults,attribute); if (!token && !defaulted) continue;
            let choices: string[] | undefined;
            if (attribute === 'Shader' && node.name === 'FXParticleSystemTemplate') choices = ['ADDITIVE','ALPHA','MULTIPLY', ...(['TRAIL','STREAK','SWARM'].includes(node.attributes.Type) ? ['ADDITIVE_ALPHA_TEST','ALPHA_TEST'] : [])];
            if (attribute === 'Shader' && node.name === 'Gpu') choices = ['GPUParticle.fx','GpuParticlePerpendicularBottom.fx'];
            if (attribute === 'Rotation') choices = ['ROTATION_OFF','ROTATE_AROUND_VELOCITY','ROTATE_IN_WORLD_SPACE'];
            if (attribute === 'RandTex') choices = ['0','1','2'];
            if (attribute === 'Distribution') choices = ['CONSTANT','UNIFORM'];
            if (attribute === 'Type' && ['InitialDelay','BurstDelay','BurstCount','Lifetime','Size','EventTime','HeightOffset','StartAmplitude','EndAmplitudeChange','StartFrequency','EndFrequencyChange','StartPhase','EndPhaseChange'].includes(node.name)) choices = ['CONSTANT','UNIFORM'];
            if (attribute === 'Type' && node.name === 'FXParticleSystemTemplate') choices = ['GPU_PARTICLE','PARTICLE','TRAIL','STREAK','SWARM'];
            if (attribute === 'GeometryType') choices = ['SIMPLE_QUAD','CENTERED_QUAD','TWO_CONCENTRIC_QUADS','TWO_QUADS'];
            if (attribute === 'Weather') choices = weatherNames;
            const inherited = origin.asset.key !== selectedAsset.key || origin.uri !== selectedAsset.uri;
            const location = token ? source(origin, attribute) : {...source(origin), start:origin.node.openEnd-(origin.node.selfClosing?2:1), end:origin.node.openEnd-(origin.node.selfClosing?2:1)};
            result.push({...location, defaulted, key: JSON.stringify([origin.uri, origin.node.path, attribute]), attribute,
                label: ['Low','High'].includes(attribute) && labels[node.name] ? `${labels[node.name]} · ${labels[attribute]}` : labels[attribute] ?? attribute, value: token?.value ?? value, readonly: this.index.isReadonly(origin.uri) || !editableAttributes.has(attribute) || metadata.has(attribute),
                inherited, sourceAsset: origin.asset.key, previewPath: prefix, group: group === 'Colors' && family !== 'GPU_PARTICLE' ? '颜色曲线（八槽）' : group === 'Alphas' && family !== 'GPU_PARTICLE' ? '透明度曲线（八槽）' : labels[group] ?? group,
                kind: choices ? 'enum' : booleanAttributes.has(attribute) ? 'boolean' : /^[-+\d.=$]/.test(value.trim()) ? 'number' : 'text', choices});
        }
        node.children.forEach((child, i) => result.push(...this.fields(child, selectedAsset, [...prefix, i],
            node.name === 'Lightning' && child.name === 'RandomSet' ? `闪电波形 ${node.children.slice(0,i+1).filter(c => c.name === 'RandomSet').length}` : prefix.length === 0 ? child.name : group, family)));
        return result;
    }
    private runtimeIssues(diagnostics: ReturnType<typeof compileParticle>['issues'], node: EffectiveNode): FxIssue[] {
        return diagnostics.map(diagnostic => {
            const parts = diagnostic.path.replace(/^\/?FXParticleSystemTemplate\/?/, '').split('/').filter(Boolean);
            let current = node; let attribute: string | undefined;
            for (const part of parts) {
                if (part.startsWith('@')) { attribute = part.slice(1); break; }
                const match = /^([^[]+)(?:\[(\d+)\])?$/.exec(part);
                const child = current.children.filter(value => value.name === match?.[1])[Math.max(0,Number(match?.[2] ?? 1)-1)];
                if (child) current = child;
                else if (current.attributes[part] !== undefined) attribute = part;
            }
            if (attribute && current.attributes[attribute] === undefined && current.children.length === 1 && current.children[0].attributes[attribute] !== undefined) current = current.children[0];
            return problem(diagnostic.code, diagnostic.message, attribute ? current.attributeOrigins[attribute] ?? current.origin : current.origin, attribute, diagnostic.severity);
        });
    }
    private async texture(template: AssetRecord, node: EffectiveNode, issues: FxIssue[], attribute = 'ParticleTexture'): Promise<FxTexture> {
        const name = node.attributes[attribute] ?? '';
        let key = `texture:${template.key}:${name}`;
        let location = source(node.attributeOrigins[attribute] ?? node.origin, attribute);
        if (!name) return {key, name, location, source:'source', status:'empty-reference', message:`${attribute} 尚未选择贴图`};
        const declaration = this.select('Texture', name, template, issues, node.attributeOrigins[attribute] ?? node.origin, attribute);
        if (!declaration) return {key, name, location, source:'source', status:'missing-declaration', message:`Texture 声明不存在或有多个候选：${name}`};
        key = `texture:${declaration.key}`;
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
        // Expressions have already passed the compiler's leading '=' gate.
        // Never ask the general expression parser to interpret an unmarked
        // literal such as Count="1+2" or SystemLife="1+2s". Prefix recovery by
        // strtol/atof is outside this finite preview profile and is diagnosed.
        const literal = integerAttributes.has(attribute) ? /^[+-]?\d+$/ : scalarLiterals[kind];
        if (!literal.test(lexical.trim())) {
            issues.push(problem('invalid-value', `${attribute}：需要完整数值${kind==='time'?'及可选 s/ms 后缀':kind==='angle'?'及可选 r/d 后缀':''}；算术表达式须以 = 开头，未实现编译器数值前缀容错`,node.attributeOrigins[attribute]??node.origin,attribute));
            return fallback;
        }
        const parsed = parseScalar(lexical, kind);
        if (parsed.status !== 'evaluated' || !Number.isFinite(parsed.value)) { issues.push(problem('invalid-value', `${attribute}：${parsed.status === 'evaluated' ? '数值必须有限' : parsed.reason}`, node.attributeOrigins[attribute] ?? node.origin, attribute)); return fallback; }
        return parsed.value;
    }
    private predicate(nugget: EffectiveNode, scene: FxScene, issues: FxIssue[]): 'play'|'skip'|'context' {
        const weather = nugget.attributes.Weather ?? 'INVALID';
        if (!weatherNames.includes(weather)) { issues.push(problem('weather-invalid', `未知 Weather=${weather}`,nugget.origin,'Weather')); return 'context'; }
        if (weather !== 'INVALID' && weather !== (scene.weather ?? 'NORMAL')) {
            issues.push(problem('condition-not-triggered', `Weather=${weather} 与当前 ${scene.weather ?? 'NORMAL'} 不符`,nugget.origin,'Weather','information')); return 'skip';
        }
        const flags = (attribute: string): string[] => (nugget.attributes[attribute] ?? '').trim().split(/\s+/).filter(Boolean);
        for (const attribute of modelConditionAttributes) for (const flag of flags(attribute)) if (!fxModelConditionNames.has(flag)) {
            issues.push(problem('model-condition-invalid', `未知 ModelCondition ${flag}`,nugget.attributeOrigins[attribute] ?? nugget.origin,attribute)); return 'context';
        }
        // 520BA0 receives NULL,NULL on position dispatch; object filters and
        // ModelConditions are not evaluated in that context. XSD enum validity
        // above still applies to the original declaration.
        if (scene.dispatch !== 'object') return 'play';
        for (const filter of nugget.children.filter(node => ['SourceObjectFilter','SecondaryObjectFilter'].includes(node.name))) {
            if (filter.name === 'SecondaryObjectFilter' && scene.targetObject === false) continue;
            issues.push(problem('object-filter-context', `${filter.name} 需要任意 ObjectFilter 解释器；当前对象分支暂停`,filter.origin)); return 'context';
        }
        const required = flags('RequiredSourceModelConditions').filter(flag => flag !== 'INVALID');
        const excluded = flags('ExcludedSourceModelConditions').filter(flag => flag !== 'INVALID');
        for (const attribute of ['RequiredSecondaryModelConditions','ExcludedSecondaryModelConditions']) if(flags(attribute).some(flag=>flag!=='INVALID')) issues.push(problem('predicate-descriptor-unused',`${attribute} 未被固定 520BA0 共用 predicate 读取；目标对象仍使用 Source 条件`,nugget.attributeOrigins[attribute]??nugget.origin,attribute,'information'));
        const matches = (actual: readonly string[]): boolean => {
            const set = new Set(actual.filter(flag => flag !== 'INVALID'));
            const has = (flag: string): boolean => set.has('ALL') || set.has(flag);
            return required.every(has) && !excluded.some(flag => flag === 'ALL' ? set.size > 0 : has(flag));
        };
        // Native 520BA0 intentionally reuses Source required/excluded for both
        // objects. Secondary descriptors are compiled but not read here.
        if (!matches(scene.sourceModelConditions ?? []) || scene.targetObject !== false && !matches(scene.targetModelConditions ?? [])) {
            issues.push(problem('condition-not-triggered','源/目标对象不满足 SourceModelConditions（原生 predicate 对双方复用此字段）',nugget.origin,undefined,'information')); return 'skip';
        }
        if (scene.sourceStealthState === 5 && !bool(nugget.attributes.PlayIfSourceIsStealthed)) {
            issues.push(problem('condition-not-triggered','源 Drawable 状态字节为 5，PlayIfSourceIsStealthed=false',nugget.origin,undefined,'information')); return 'skip';
        }
        for (const node of nugget.children.filter(child => child.name === 'SourceMustNotHaveBeenDisabledThisFrameByType')) {
            const value = node.origin.node.textTokens.map(token => token.value).join('').trim();
            if (!disabledNames.includes(value)) { issues.push(problem('disabled-type-invalid', `未知 DisabledType ${value}`,node.origin)); return 'context'; }
            if (scene.sourceDisabledTypesThisFrame?.includes(value)) { issues.push(problem('condition-not-triggered', `源本逻辑帧被 ${value} 禁用`,node.origin,undefined,'information')); return 'skip'; }
        }
        return 'play';
    }
    async resolve(assetKey: string, sceneValues: Partial<FxScene> = {}, signal?: AbortSignal): Promise<PreviewIR> {
        const asset = this.index.getAsset(assetKey); if (!asset) throw new Error('所选 FX 资产不属于当前入口，请重新选择');
        const scene: FxScene = {...defaultScene,...sceneValues,source:[...(sceneValues.source ?? defaultScene.source)],target:[...(sceneValues.target ?? defaultScene.target)]};
        if (scene.source.length!==3 || scene.target.length!==3 || scene.sourceRotation && scene.sourceRotation.length!==3 || ![...scene.source,...scene.target,...(scene.sourceRotation ?? []),scene.ground,scene.seed,scene.positionVisibility??1,scene.sourceVisibility??1,scene.sourceStealthState??0].every(Number.isFinite)) throw new Error('预览场景坐标需三个有限值，其余场景数值必须有限');
        if (scene.dispatch && !['position','object'].includes(scene.dispatch)) throw new Error('未知 FXList 分派上下文');
        if (!weatherNames.includes(scene.weather??'NORMAL')) throw new Error(`未知场景 Weather ${scene.weather}`);
        if (scene.sourceDisabledTypesThisFrame?.some(name=>!disabledNames.includes(name))) throw new Error('未知场景 DisabledType');
        for (const flag of [...(scene.sourceModelConditions ?? []),...(scene.targetModelConditions ?? [])]) if (!fxModelConditionNames.has(flag)) throw new Error(`未知场景 ModelCondition ${flag}`);
        const issues = [...this.index.graph.issues], textures: FxTexture[] = [], systems: FxSystem[] = [];
        const templates = new Map<string,FxSystem>(), fxLists = new Map<string,FxEventList>();
        const pending: AssetRecord[] = [], queued = new Set<string>(); let hasDynamic = false, dynamicDescriptors = 0;
        const enqueue = (target: AssetRecord, origin: Origin, targetIssues: FxIssue[]): boolean => {
            hasDynamic = true;
            if (queued.has(target.key)) return true;
            if (queued.size >= 128) { targetIssues.push(problem('reference-budget','事件/附属引用图已达 128 个资产上限，停止展开此引用',origin)); return false; }
            queued.add(target.key); pending.push(target); return true;
        };
        const multiply = (a: readonly number[],b: readonly number[]): number[] => Array.from({length:9},(_,index) => {
            const row = Math.floor(index/3), col = index%3; return a[row*3]*b[col]+a[row*3+1]*b[col+3]+a[row*3+2]*b[col+6];
        });
        const template = async (record: AssetRecord): Promise<FxSystem> => {
            const cached = templates.get(record.key); if (cached) return cached;
            if (signal?.aborted) throw new Error('FX preview cancelled');
            const localIssues: FxIssue[] = [];
            const definition = this.effective(record,localIssues);
            if (definition) this.expandExpressions(definition,this.symbols(localIssues,record.scopeUri??record.uri),localIssues);
            const compiled = definition ? compileParticle(plain(definition)) : undefined;
            if (compiled && definition) localIssues.push(...this.runtimeIssues(compiled.issues,definition));
            const textureIssues: FxIssue[] = [], texture = definition ? await this.texture(record,definition,textureIssues) : undefined;
            if (texture && texture.status !== 'pending' && !textureIssues.length) textureIssues.push({...texture.location!,code:`texture-${texture.status}`,message:texture.message ?? texture.status,severity:'warning'});
            localIssues.push(...textureIssues.map(issue => ({...issue,severity:'warning' as const})));
            if (texture && !textures.some(value => value.key === texture.key)) textures.push(texture);
            const swarmDraw = definition && find(definition,'Draw')?.children.find(node => node.name === 'Swarm');
            const environmentIssues: FxIssue[] = [];
            const environmentTexture = swarmDraw?.attributes.EnvironmentTexture?.trim() ? await this.texture(record,swarmDraw,environmentIssues,'EnvironmentTexture') : undefined;
            if (environmentTexture && environmentTexture.status !== 'pending' && !environmentIssues.length) environmentIssues.push({...environmentTexture.location!,code:`environment-texture-${environmentTexture.status}`,message:environmentTexture.message ?? environmentTexture.status,severity:'warning'});
            localIssues.push(...environmentIssues.map(issue => ({...issue,severity:'warning' as const,message:`环境贴图：${issue.message}；继续使用基础粒子贴图预览`})));
            if (environmentTexture && !textures.some(value => value.key === environmentTexture.key)) textures.push(environmentTexture);
            const config = compiled?.config;
            const value: FxSystem = {key:record.key,name:record.id,templateKey:record.key,source:source(this.origin(record)),definition:definition ? plain(definition):undefined,
                config,fields:definition ? this.fields(definition,record):[],issues:localIssues,offset:[0,0,0],rotation:0,startFrame:0,textureKey:texture?.key,environmentTextureKey:environmentTexture?.key,triggered:true};
            templates.set(record.key,value); // Register before following references; cycles remain graph edges.
            if (definition) {
                const attached = definition.attributes.PerParticleAttachedSystem;
                if (attached) {
                    const target = this.select('FXParticleSystemTemplate',attached,record,localIssues,definition.attributeOrigins.PerParticleAttachedSystem ?? definition.origin,'PerParticleAttachedSystem');
                    if (target && enqueue(target,definition.origin,localIssues) && config) config.attachedSystem = target.key;
                }
                const events = find(definition,'Event')?.children ?? [];
                for (let i=0;i<events.length;i++) {
                    const event = events[i], reference = event.attributes.EventFX;
                    if (!reference || !['Life','Collision'].includes(event.name)) continue;
                    const target = this.select('FXList',reference,record,localIssues,event.attributeOrigins.EventFX ?? event.origin,'EventFX');
                    if (target && enqueue(target,event.origin,localIssues) && config?.events?.[i]) config.events[i].fx = target.key;
                }
            }
            if (localIssues.some(issue => issue.severity === 'error')) value.config = undefined;
            issues.push(...localIssues); return value;
        };
        const instantiate = async (record: AssetRecord,call: EffectiveNode | undefined,instance: number,callIndex: number,context: FxScene,listKey: string,relative=false): Promise<FxSystem> => {
            const base = await template(record), localIssues: FxIssue[] = [...base.issues], inheritedIssueCount = localIssues.length;
            const value: FxSystem = {...base,key:`${listKey}/call/${callIndex}/${instance}`,name:`${record.id}${call ? ` · 调用 ${callIndex+1}.${instance+1}`:''}`,
                source:call ? source(call.origin):base.source,issues:localIssues,offset:[...context.source],triggered:true};
            const inputAngles: [number,number,number] = context.dispatch === 'object' ? [...(context.sourceRotation ?? [0,0,0])] : [0,0,0];
            if (!call) {if(context.dispatch==='object')value.rotationMatrix=[...localRotationMatrix(inputAngles)];return value;}
            const supportedAttributes = new Set(['Particle','Count','SystemLife','OnlyIfOnLand','OnlyIfOnWater','StopIfPlayed','Weather','OrientToObject','CreateAtGroundHeight','UseTarget','Ricochet','SetTargetMatrix','TargetCoeff','CreateOverrideBone','TargetOverrideBone','PlayIfSourceIsStealthed',...modelConditionAttributes]);
            for (const name of Object.keys(call.attributes)) if (!supportedAttributes.has(name) && !metadata.has(name)) localIssues.push(problem('nugget-context',`ParticleSystem.${name} 尚无已实现的调用合同`,call.attributeOrigins[name] ?? call.origin,name));
            for (const name of ['CreateOverrideBone','TargetOverrideBone']) if (call.attributes[name]) localIssues.push(problem('bone-context',`ParticleSystem.${name} 需要完整对象骨骼，当前分支暂停`,call.attributeOrigins[name] ?? call.origin,name));
            const hasTarget = context.dispatch === 'object' && context.targetObject !== false;
            value.orientToObject=bool(call.attributes.OrientToObject);
            if (bool(call.attributes.Ricochet) && hasTarget) inputAngles.splice(0,3,0,0,Math.atan2(context.source[1]-context.target[1],context.source[0]-context.target[0]));
            const inputMatrix = localRotationMatrix(inputAngles);
            const placement = find(call,'Offset');
            const localOffset = placement ? ['x','y','z'].map(axis => this.numeric(placement,axis,0,localIssues)) as [number,number,number] : [0,0,0] as [number,number,number];
            const offset = transformVector(localOffset,inputMatrix);
            value.offset = context.source.map((n,index) => n+offset[index]) as [number,number,number];
            if (bool(call.attributes.CreateAtGroundHeight)) {if(!relative)value.offset[2] = context.ground; value.createAtGroundHeight = true;}
            let matrix = bool(call.attributes.OrientToObject) ? [...inputMatrix] : [...localRotationMatrix([0,0,0])];
            if (hasTarget) value.target = [...context.target];
            const targetOffset = find(call,'TargetOffset');
            if (targetOffset) value.target = ['x','y','z'].map((axis,index) => value.offset[index]+this.numeric(targetOffset,axis,0,localIssues)) as [number,number,number];
            // 5336DC only consumes SetTargetMatrix under OrientToObject with a
            // secondary object. The +10 vertical bias is present in the native path.
            if (bool(call.attributes.OrientToObject) && bool(call.attributes.SetTargetMatrix) && hasTarget) {
                const dx=context.target[0]-value.offset[0],dy=context.target[1]-value.offset[1],dz=context.target[2]-value.offset[2];
                matrix = multiply(matrix,localRotationMatrix([0,Math.PI-Math.atan2(dz+10,Math.hypot(dx,dy)),0]));
            }
            const rotate = find(call,'Rotate');
            if (rotate) {
                value.rotationXYZ = ['X','Y','Z'].map(axis => this.numeric(rotate,axis,0,localIssues,'angle')) as [number,number,number]; value.rotation = value.rotationXYZ[2];
                matrix = multiply(matrix,localRotationMatrix(value.rotationXYZ));
                if (value.rotationXYZ[0] !== 0 || value.rotationXYZ[1] !== 0) localIssues.push(problem('rotate-contract','Rotate XYZ 采用 EA 同系矩阵合同 Rx·Ry·Rz，尚无 RA3 实机画面对照',rotate.origin,undefined,'warning'));
            }
            value.rotationMatrix = matrix;
            if (call.attributes.TargetCoeff !== undefined) {
                this.numeric(call,'TargetCoeff',1,localIssues);
                localIssues.push(problem('nugget-unused','固定 533400 普通粒子创建路径不读取 TargetCoeff；XML 值已保留',call.attributeOrigins.TargetCoeff ?? call.origin,'TargetCoeff','information'));
            }
            const delay = find(call,'InitialDelay');
            if (delay) {
                const low=this.numeric(delay,'Low',-1,localIssues),high=this.numeric(delay,'High',-1,localIssues),type=delay.attributes.Type ?? 'CONSTANT';
                if (low >= 0 && high >= low && ['CONSTANT','UNIFORM'].includes(type)) value.initialDelayOverrideMs={low,high,type:type as 'CONSTANT'|'UNIFORM'};
                else if (!(low===-1 && high===-1 && ['CONSTANT','UNIFORM'].includes(type))) localIssues.push(problem('nugget-delay','InitialDelay 需非负毫秒范围/合法 Type，或默认 -1,-1',delay.origin));
            }
            const life=this.numeric(call,'SystemLife',-1,localIssues,'time');
            if (life>=0) value.systemLifeFrames=Math.ceil(life*30); else if(life!==-1) localIssues.push(problem('nugget-lifetime','SystemLife 只支持非负 Time 或默认 -1s',call.origin,'SystemLife'));
            for(const child of call.children) if(!['Rotate','Offset','TargetOffset','InitialDelay','SourceObjectFilter','SecondaryObjectFilter','SourceMustNotHaveBeenDisabledThisFrameByType'].includes(child.name)) localIssues.push(problem('nugget-module',`ParticleSystem/${child.name} 尚无已实现的创建合同`,child.origin));
            if(bool(call.attributes.OnlyIfOnLand) && context.terrain==='water' || bool(call.attributes.OnlyIfOnWater) && context.terrain==='land') {
                value.triggered=false; localIssues.push(problem('condition-not-triggered','当前陆地/水面条件不满足，未触发',call.origin,undefined,'information'));
            }
            if(localIssues.some(issue=>issue.severity==='error')) value.config=undefined;
            issues.push(...localIssues.slice(inheritedIssueCount)); return value;
        };
        const dispatch = async(record:AssetRecord,context:FxScene,destination:FxSystem[],effective?:EffectiveNode):Promise<boolean> => {
            const localIssues:FxIssue[]=[];
            const node=effective ?? this.effective(record,localIssues); if(node && !effective) this.expandExpressions(node,this.symbols(localIssues,record.scopeUri??record.uri),localIssues);
            issues.push(...localIssues); if(!node || localIssues.some(issue=>issue.severity==='error')) return false;
            const listAttributes=new Set(['id','PlayEvenIfShrouded','Tailorable','CullTracking','CullTrackingMin','CullTrackingMax']);
            for(const name of Object.keys(node.attributes)) if(!listAttributes.has(name)) {issues.push(problem('fxlist-field',`FXList.${name} 尚无已实现合同`,node.attributeOrigins[name]??node.origin,name));return false;}
            for(const name of ['PlayEvenIfShrouded','Tailorable']) if(node.attributes[name]!==undefined && !/^(?:true|false|0|1)$/i.test(node.attributes[name])) {issues.push(problem('invalid-boolean',`${name} 不是合法 SageBool`,node.attributeOrigins[name]??node.origin,name));return false;}
            for(const child of node.children) if(child.name!=='NuggetList') {issues.push(problem('fxlist-node',`FXList/${child.name} 尚无已实现合同`,child.origin));return false;}
            const cullIssues:FxIssue[]=[],window=this.numeric(node,'CullTracking',0,cullIssues,'time'),minimum=this.numeric(node,'CullTrackingMin',0,cullIssues),maximum=this.numeric(node,'CullTrackingMax',0,cullIssues);
            if(window<0 || !Number.isInteger(minimum) || !Number.isInteger(maximum) || minimum<0 || maximum<minimum || maximum>0x7fffffff)cullIssues.push(problem('cull-tracking-value','CullTracking 需非负时间及 0≤Min≤Max≤signed32 的整数范围',node.origin));
            if(cullIssues.length){issues.push(...cullIssues);return false;}
            if(bool(node.attributes.Tailorable)) issues.push(problem('fxlist-tailorable','Tailorable 质量裁剪采用当前标准高质量预览，未模拟其他游戏质量配置',node.attributeOrigins.Tailorable??node.origin,'Tailorable','warning'));
            if(!bool(node.attributes.PlayEvenIfShrouded)) {
                const visible=context.dispatch==='object' ? context.sourceVisibility ?? 1 : 0;
                if(context.dispatch==='object' && visible>2 || (context.dispatch!=='object' || visible===0) && (context.positionVisibility??1)!==1) {
                    issues.push(problem('condition-not-triggered','当前原生可见性查询值拒绝 FXList 分派',node.origin,undefined,'information'));return false;
                }
            }
            const list=find(node,'NuggetList'); if(!list){issues.push(problem('nugget-list-missing','FXList 缺少 NuggetList',node.origin));return false;}
            let callIndex=0;
            for(const nugget of list.children) {
                if(signal?.aborted) throw new Error('FX preview cancelled');
                const invalidBoolean=Object.entries(nugget.attributes).find(([name,value])=>booleanAttributes.has(name) && !/^(?:true|false|0|1)$/i.test(value));
                if(invalidBoolean){issues.push(problem('invalid-boolean',`${invalidBoolean[0]} 不是合法 SageBool`,nugget.origin,invalidBoolean[0]));if(invalidBoolean[0]==='StopIfPlayed')break;continue;}
                const predicate=this.predicate(nugget,context,issues);
                if(predicate==='skip')continue;
                if(predicate==='context'){if(bool(nugget.attributes.StopIfPlayed))break;continue;}
                if(nugget.name==='FXListAtBonePos' && context.dispatch!=='object') {
                    issues.push(problem('bone-position-noop','FXListAtBonePos 的位置执行槽为空操作；没有 source object 骨骼',nugget.origin,undefined,'information'));
                } else if(nugget.name!=='ParticleSystem') {
                    issues.push(problem(nugget.name==='FXListAtBonePos'||nugget.name==='FXParticleSysBone'?'bone-context':'nugget-unsupported',`${nugget.name} 需要未实现的${nugget.name.includes('Bone')?'对象骨骼':'业务执行槽'}，未播放此节点`,nugget.origin,undefined,'warning'));
                    if(bool(nugget.attributes.StopIfPlayed)){issues.push(problem('dispatch-stop-context',`${nugget.name} 已进入执行槽，StopIfPlayed 终止后续项`,nugget.origin,undefined,'information'));break;}
                    continue;
                } else {
                    const countIssues:FxIssue[]=[],count=this.numeric(nugget,'Count',1,countIssues);
                    if(!Number.isInteger(count)||Math.abs(count)>0x7fffffff)countIssues.push(problem('nugget-count','Count 必须为 signed32 整数系统实例数',nugget.origin,'Count'));
                    if(count>128 || destination.length+Math.max(0,count)>128 || destination!==systems && dynamicDescriptors+Math.max(0,count)>512)countIssues.push(problem('system-budget','预览系统实例预算已达上限（单列表 128、事件图 512）',nugget.origin,'Count'));
                    issues.push(...countIssues);
                    if(!countIssues.length && count>0) {
                        const particle=nugget.attributes.Particle??'';
                        const target=particle?this.select('FXParticleSystemTemplate',particle,record,issues,nugget.origin,'Particle'):undefined;
                        if(!particle)issues.push(problem('particle-reference-empty','ParticleSystem.Particle 为空，未创建系统',nugget.origin,'Particle','information'));
                        if(target)for(let i=0;i<count;i++){destination.push(await instantiate(target,nugget,i,callIndex,context,record.key,destination!==systems));if(destination!==systems)dynamicDescriptors++;}
                    }
                    callIndex++;
                }
                // Native StopIfPlayed follows slot invocation, even when Count is
                // zero, a position bone slot is empty, or terrain rejects creation.
                if(bool(nugget.attributes.StopIfPlayed))break;
            }
            return true;
        };
        const rootIssues:FxIssue[]=[],effective=this.effective(asset,rootIssues);
        if(effective)this.expandExpressions(effective,this.symbols(rootIssues,asset.scopeUri??asset.uri),rootIssues);
        issues.push(...rootIssues);const fields=effective?this.fields(effective,asset):[];
        if(effective && (asset.type==='FXParticleSystemTemplate' || !rootIssues.some(issue=>issue.severity==='error'))) {
            if(asset.type==='FXParticleSystemTemplate')systems.push(await instantiate(asset,undefined,0,0,scene,asset.key));
            else if(asset.type==='FXList')await dispatch(asset,scene,systems,effective);
            else issues.push(this.issueAtAsset('asset-not-previewable',`${asset.type} 只提供结构与资源查看`,asset));
        }
        for(let cursor=0;cursor<pending.length;cursor++) {
            const record=pending[cursor];
            if(record.type==='FXParticleSystemTemplate'){await template(record);continue;}
            if(fxLists.has(record.key))continue;
            const list:FxEventList={key:record.key,systems:[],cullTrackingFrames:0,cullTrackingMin:0,cullTrackingMax:0,triggered:true};fxLists.set(record.key,list);
            const localIssues:FxIssue[]=[],node=this.effective(record,localIssues);
            if(node)this.expandExpressions(node,this.symbols(localIssues,record.scopeUri??record.uri),localIssues);
            if(node){
                // Original DLL Time initializer stores f32 seconds, and 533D20
                // subtracts that raw value from GameLogic+50. There is no *30.
                list.cullTrackingFrames=Math.fround(this.numeric(node,'CullTracking',0,localIssues,'time'));
                list.cullTrackingMin=this.numeric(node,'CullTrackingMin',0,localIssues);list.cullTrackingMax=this.numeric(node,'CullTrackingMax',0,localIssues);
                if(list.cullTrackingFrames<0 || !Number.isInteger(list.cullTrackingMin) || !Number.isInteger(list.cullTrackingMax) || list.cullTrackingMin<0 || list.cullTrackingMax<list.cullTrackingMin)localIssues.push(problem('cull-tracking-value','CullTracking 需非负时间及 0≤Min≤Max 的整数范围',node.origin));
            }
            issues.push(...localIssues);
            list.triggered=!!node && !localIssues.some(issue=>issue.severity==='error') && await dispatch(record,{...scene,dispatch:'position',source:[0,0,0],target:[0,0,0],sourceRotation:[0,0,0],ground:0},list.systems,node);
        }
        const document=this.index.getDocument(asset.uri);
        return {schemaVersion:1,entry:asset.entry,assetKey:asset.key,name:asset.id,documentUri:asset.uri,documentVersion:document?.version??0,generation:this.index.generation,
            systems,fields,textures,issues,dependencies:[...this.index.graph.dependencies],scene,
            dynamic:hasDynamic?{templates:[...templates.values()],fxLists:[...fxLists.values()],maxSystems:128,maxEventsPerFrame:256,maxDepth:16,initialFxLists:fxLists.has(asset.key)?[asset.key]:[]}:undefined};
    }
}
