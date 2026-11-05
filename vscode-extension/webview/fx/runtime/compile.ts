import type { AlphaKey, ColorKey, FxNode, ParticleConfig, Range, RuntimeIssue, Vec3 } from './types';

const local = (name: string): string => name.split(':').pop()!;
const child = (node: FxNode | undefined, name: string): FxNode | undefined => node?.children.find(c => local(c.name) === name);
const constant = (n: number): Range => ({ low: n, high: n, type: 'CONSTANT' });

/** Compile once per document generation. No XML lookup or validation occurs in the render loop. */
export function compileParticle(root: FxNode): { config?: ParticleConfig; issues: RuntimeIssue[] } {
    const issues: RuntimeIssue[] = [];
    const issue = (path: string, message: string, severity: RuntimeIssue['severity'] = 'error', code = 'unsupported') =>
        issues.push({ path, message, severity, code });
    const number = (value: string | undefined, fallback: number, path: string): number => {
        if (value === undefined) return fallback;
        if (!/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(value.trim())) {
            issue(path, `数值/表达式尚未求值：${value}`, 'error', 'value'); return fallback;
        }
        const n = Number(value);
        if (!Number.isFinite(n) || Math.abs(n) > 1e12) { issue(path, '数值超出有限预览范围', 'error', 'value'); return fallback; }
        return n;
    };
    const scalar = (node: FxNode | undefined, attr: string, fallback = 0, path = attr): number => number(node?.attributes[attr], fallback, path);
    const bool = (node: FxNode | undefined, attr: string, fallback = false, path = attr): boolean => {
        const raw = node?.attributes[attr];
        if (raw === undefined) return fallback;
        if (/^(true|1)$/i.test(raw)) return true;
        if (/^(false|0)$/i.test(raw)) return false;
        issue(path, `无效布尔值：${raw}`, 'error', 'value'); return fallback;
    };
    const check = (node: FxNode | undefined, attrs: string[], children: string[], path: string): void => {
        if (!node) return;
        for (const name of Object.keys(node.attributes)) {
            if (name === 'xmlns' || name.startsWith('xmlns:') || name.startsWith('xai:') || name.startsWith('xsi:')) continue;
            if (!attrs.includes(name)) issue(`${path}/@${name}`, `尚未支持字段 ${name}`);
        }
        for (const c of node.children) if (!children.includes(local(c.name))) issue(`${path}/${local(c.name)}`, `尚未支持节点 ${local(c.name)}`);
    };
    const range = (parent: FxNode | undefined, name: string, fallback = 0, path = name): Range => {
        const node = child(parent, name);
        if (!node) return constant(fallback);
        check(node, ['Low', 'High', 'Type', 'Frame', 'RelativeAge'], [], path);
        const type = node.attributes.Type ?? 'CONSTANT';
        if (type !== 'CONSTANT' && type !== 'UNIFORM') issue(`${path}/@Type`, `随机类型 ${type} 不在本轮数值合同内`);
        const low = scalar(node, 'Low', 0, `${path}/@Low`);
        const high = scalar(node, 'High', 0, `${path}/@High`);
        return { low, high, type: type === 'UNIFORM' ? 'UNIFORM' : 'CONSTANT' };
    };
    const vector = (parent: FxNode | undefined, name: string, path = name): Vec3 => {
        const node = child(parent, name); check(node, ['x', 'y', 'z'], [], path);
        return ['x', 'y', 'z'].map(a => scalar(node, a, 0, `${path}/@${a}`)) as Vec3;
    };
    const module = (name: string, supported: string[]): FxNode | undefined => {
        const parent = child(root, name);
        if (!parent || parent.children.length !== 1) { issue(name, `需要一个 ${name} 模块`); return undefined; }
        check(parent, [], supported, name);
        const node = parent.children[0];
        if (!supported.includes(local(node.name))) return undefined;
        return node;
    };
    check(root, ['id', 'inheritFrom', 'Type', 'Shader', 'ParticleTexture', 'SystemLifetime', 'IsOneShot', 'Priority',
        'SortLevel', 'IsGroundAligned', 'IsEmitAboveGroundOnly', 'IsParticleUpTowardsEmitter', 'UseMaximumHeight',
        'Drawable', 'SlaveSystem', 'PerParticleAttachedSystem', 'EmitterSound'],
    ['Lifetime', 'InitialDelay', 'BurstDelay', 'BurstCount', 'Size', 'StartSizeRate', 'Physics', 'Draw', 'Volume', 'Velocity', 'Update', 'Colors', 'Alphas', 'Wind', 'Event'], '');
    if ((root.attributes.Type ?? 'PARTICLE') !== 'GPU_PARTICLE') issue('@Type', `Type=${root.attributes.Type ?? 'PARTICLE'} 暂未播放；第一轮仅普通 GPU`);
    const shader = root.attributes.Shader ?? 'ADDITIVE';
    if (!['ADDITIVE', 'ALPHA', 'MULTIPLY'].includes(shader)) issue('@Shader', `尚未支持材质 profile ${shader}`);
    for (const a of ['Drawable', 'SlaveSystem', 'PerParticleAttachedSystem', 'EmitterSound']) {
        if (root.attributes[a]) issue(`@${a}`, `${a} 需要本轮以外的运行分支`);
    }
    for (const a of ['IsEmitAboveGroundOnly', 'IsParticleUpTowardsEmitter', 'UseMaximumHeight']) {
        if (bool(root, a)) issue(`@${a}`, `${a} 需要额外地形/朝向上下文`);
    }
    const physics = module('Physics', ['Default']);
    const draw = module('Draw', ['Gpu']);
    const volume = module('Volume', ['Point', 'Line', 'Box', 'Cylinder', 'Sphere']);
    const velocity = module('Velocity', ['Ortho', 'Cylindrical', 'Spherical', 'Outward']);
    const updateParent = child(root, 'Update');
    const update = updateParent ? module('Update', ['Default']) : undefined;
    check(physics, ['Gravity', 'Swirly', 'ParticlesAttachToBone'], ['VelocityDamping', 'DriftVelocity'], 'Physics/Default');
    for (const a of ['Swirly', 'ParticlesAttachToBone']) if (bool(physics, a)) issue(`Physics/Default/@${a}`, `${a} 尚未播放`);
    check(draw, ['Shader', 'GeometryType', 'FramesPerRow', 'TotalFrames', 'SingleRow', 'RandTex', 'SpeedMultiplier',
        'SortParticles', 'EnableSoftParticles', 'MiscValue1', 'MiscValue2', 'MiscValue3', 'MiscValue4', 'DetailTexture'], [], 'Draw/Gpu');
    if ((draw?.attributes.Shader ?? 'GPUParticle.fx').toLowerCase() !== 'gpuparticle.fx') issue('Draw/Gpu/@Shader', `尚未实现 shader ${draw?.attributes.Shader}`);
    if ((draw?.attributes.GeometryType ?? 'SIMPLE_QUAD') !== 'SIMPLE_QUAD') issue('Draw/Gpu/@GeometryType', `几何 ${draw?.attributes.GeometryType} 留待第二轮`);
    if (draw?.attributes.DetailTexture) issue('Draw/Gpu/@DetailTexture', 'DetailTexture 需要额外 shader 合同');
    for (const a of ['MiscValue1', 'MiscValue2', 'MiscValue3', 'MiscValue4']) if (scalar(draw, a) !== 0) issue(`Draw/Gpu/@${a}`, '非零 MiscValue 需要对应 shader 合同');
    if (bool(draw, 'EnableSoftParticles', true)) issue('Draw/Gpu/@EnableSoftParticles', '当前为源贴图/无场景深度预览；软粒子深度在第二轮实现', 'warning', 'context');
    check(update, ['Rotation'], ['SizeRate', 'SizeRateDamping', 'AngleZ', 'AngularRateZ', 'AngularDamping', 'AngleXY', 'AngularRateXY', 'AngularDampingXY'], 'Update/Default');
    if (update?.attributes.Rotation && update.attributes.Rotation !== 'ROTATION_OFF') issue('Update/Default/@Rotation', 'Rotation GeometryUpdate 变体暂未实现');
    const event = child(root, 'Event');
    if (event && (event.children.length || Object.keys(event.attributes).length)) issue('Event', 'Event 调度尚未播放');
    const wind = child(root, 'Wind');
    check(wind, ['Motion', 'Strength', 'FullStrengthDist', 'ZeroStrengthDist', 'AngleChangeMin', 'AngleChangeMax',
        'PingPongStartAngleMin', 'PingPongStartAngleMax', 'PingPongEndAngleMin', 'PingPongEndAngleMax', 'TurbulenceAmplitude', 'TurbulenceFrequency'], [], 'Wind');
    if (wind && (wind.attributes.Motion ?? 'NOT_USED') !== 'NOT_USED') issue('Wind/@Motion', '风场尚未播放');
    check(volume, ['IsHollow', 'Radius', 'RadiusRate', 'Length'], ['StartPoint', 'EndPoint', 'HalfSize', 'Offset'], 'Volume');
    check(velocity, [], ['Position', 'Radial', 'Normal', 'Speed', 'OtherSpeed'], 'Velocity');
    const position = child(velocity, 'Position'); check(position, [], ['x', 'y', 'z'], 'Velocity/Ortho/Position');
    const age = (node: FxNode, path: string): number => {
        const raw = node.attributes.RelativeAge;
        return number(raw?.replace(/%$/, ''), 0, `${path}/@RelativeAge`) / 100;
    };
    let colors: ColorKey[] | undefined, alphas: AlphaKey[] | undefined;
    const colorsNode = child(root, 'Colors'), alphasNode = child(root, 'Alphas');
    check(colorsNode, ['UseHouseColor', 'HouseColorSaturation'], ['Color', 'ColorScale'], 'Colors');
    check(alphasNode, [], ['Alpha'], 'Alphas');
    if (bool(colorsNode, 'UseHouseColor')) issue('Colors/@UseHouseColor', '阵营颜色需要显式对象颜色上下文');
    if (colorsNode) {
        colors = Array.from({ length: 8 }, () => ({ time: 0, value: [0, 0, 0] as Vec3 }));
        for (const node of colorsNode.children.filter(n => local(n.name) === 'Color')) {
            check(node, ['Frame', 'RelativeAge'], ['Color'], 'Colors/Color');
            const frame = scalar(node, 'Frame', 1000, 'Colors/Color/@Frame');
            if (frame < 1 || frame > 8 || !Number.isInteger(frame)) { issue('Colors/Color/@Frame', `槽 ${frame} 不参与合法八槽输入`, 'warning', 'slot'); continue; }
            const color = child(node, 'Color'); check(color, ['r', 'g', 'b'], [], 'Colors/Color/Color');
            if (!color) issue('Colors/Color/Color', '颜色槽缺少必需 Color 子节点', 'error', 'value');
            colors[frame - 1] = { time: age(node, 'Colors/Color'), value: ['r', 'g', 'b'].map(a => scalar(color, a)) as Vec3 };
            if (frame > 4) issue('Colors/Color/@Frame', `GPU 仅消费槽 1～4；保留槽 ${frame}`, 'warning', 'slot');
        }
    }
    if (alphasNode) {
        alphas = Array.from({ length: 8 }, () => ({ time: 0, range: constant(0) }));
        for (const node of alphasNode.children) {
            const frame = scalar(node, 'Frame', 1000, 'Alphas/Alpha/@Frame');
            if (frame < 1 || frame > 8 || !Number.isInteger(frame)) { issue('Alphas/Alpha/@Frame', `槽 ${frame} 不参与合法八槽输入`, 'warning', 'slot'); continue; }
            alphas[frame - 1] = { time: age(node, 'Alphas/Alpha'), range: range({ name: 'Alphas', attributes: {}, children: [node] }, local(node.name), 0, 'Alphas/Alpha') };
            if (frame > 4) issue('Alphas/Alpha/@Frame', `GPU 仅消费槽 1～4；保留槽 ${frame}`, 'warning', 'slot');
        }
        issue('Alphas/Alpha/@RelativeAge', '此 GPU 路径由颜色槽控制 RGBA 时间；Alpha 时间保留但不消费', 'warning', 'alpha-time');
    }
    const config: ParticleConfig = {
        id: root.attributes.id ?? '', texture: root.attributes.ParticleTexture ?? '', shader: shader as ParticleConfig['shader'],
        lifetime: range(root, 'Lifetime'), initialDelay: range(root, 'InitialDelay'), burstDelay: range(root, 'BurstDelay'), burstCount: range(root, 'BurstCount'),
        size: range(root, 'Size'), startSizeRate: range(root, 'StartSizeRate'), systemLifetime: scalar(root, 'SystemLifetime'), isOneShot: bool(root, 'IsOneShot'),
        velocityDamping: range(physics, 'VelocityDamping', 1, 'Physics/Default/VelocityDamping'), gravity: scalar(physics, 'Gravity'), drift: vector(physics, 'DriftVelocity'),
        sizeRate: range(update, 'SizeRate'), sizeRateDamping: range(update, 'SizeRateDamping', 1),
        angleZ: range(update, 'AngleZ'), angularRateZ: range(update, 'AngularRateZ'), angularDamping: range(update, 'AngularDamping', 1),
        angleXY: range(update, 'AngleXY'), angularRateXY: range(update, 'AngularRateXY'), angularDampingXY: range(update, 'AngularDampingXY', 1),
        colors, alphas, colorScale: range(colorsNode, 'ColorScale', 1),
        atlas: { framesPerRow: Math.max(1, Math.trunc(scalar(draw, 'FramesPerRow', 1))), totalFrames: Math.max(1, Math.trunc(scalar(draw, 'TotalFrames', 1))),
            singleRow: scalar(draw, 'SingleRow'), randTex: scalar(draw, 'RandTex'), speedMultiplier: scalar(draw, 'SpeedMultiplier', 1) },
        volume: { type: local(volume?.name ?? 'Point') as ParticleConfig['volume']['type'], hollow: bool(volume, 'IsHollow'),
            start: vector(volume, 'StartPoint'), end: vector(volume, 'EndPoint'), halfSize: vector(volume, 'HalfSize'),
            radius: scalar(volume, 'Radius'), radiusRate: scalar(volume, 'RadiusRate'), length: scalar(volume, 'Length'), offset: vector(volume, 'Offset') },
        velocity: { type: local(velocity?.name ?? 'Ortho') as ParticleConfig['velocity']['type'],
            x: range(position, 'x'), y: range(position, 'y'), z: range(position, 'z'), radial: range(velocity, 'Radial'), normal: range(velocity, 'Normal'),
            speed: range(velocity, 'Speed'), otherSpeed: range(velocity, 'OtherSpeed') },
        groundAligned: bool(root, 'IsGroundAligned'), sortParticles: bool(draw, 'SortParticles'), sortLevel: scalar(root, 'SortLevel'),
    };
    for (const name of ['initialDelay', 'burstDelay', 'burstCount'] as const) {
        if (Math.min(config[name].low, config[name].high) < 0 || Math.max(config[name].low, config[name].high) > 0x7fffffff) issue(name, '仅支持非负 signed32 安全计数', 'error', 'value');
    }
    if (Math.min(config.lifetime.low, config.lifetime.high) < 1 || Math.max(config.lifetime.low, config.lifetime.high) > 0x7fffffff) issue('Lifetime', '本预览仅支持截断后为正的粒子寿命', 'error', 'value');
    if (!Number.isInteger(config.systemLifetime) || config.systemLifetime < 0 || config.systemLifetime > 0x7fffffff) issue('@SystemLifetime', '系统寿命应为非负 signed32 安全整数；0 为无限窗口', 'error', 'value');
    for (const name of ['velocityDamping', 'sizeRateDamping', 'angularDamping', 'angularDampingXY'] as const) {
        if (Math.min(config[name].low, config[name].high) <= 0) issue(name, '解析阻尼需为正；保留原文且暂停受影响系统', 'error', 'value');
    }
    if (Math.min(...config.volume.halfSize, config.volume.radius, config.volume.length) < 0) issue('Volume', '本轮体积合同仅支持非负有限尺寸', 'error', 'value');
    if (config.startSizeRate.low !== 0 || config.startSizeRate.high !== 0) issue('StartSizeRate', '此普通 GPU 路径不消费出生尺寸累加；保留随机采样与系统累加，不将其误作 SizeRate', 'warning', 'gpu-unused');
    if (!Number.isInteger(config.atlas.singleRow) || !Number.isInteger(config.atlas.randTex) || config.atlas.speedMultiplier < 0) issue('Draw/Gpu', 'Atlas 行/模式须为整数，播放速度需非负', 'error', 'value');
    return { config: issues.some(i => i.severity === 'error') ? undefined : config, issues };
}
