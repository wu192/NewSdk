import type { AlphaKey, ColorKey, FxNode, LightningRandomSet, ParticleConfig, Range, RuntimeIssue, Vec3 } from './types';

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
    const module = (name: string, supported: string[], allowEmpty = false): FxNode | undefined => {
        const parent = child(root, name);
        if (allowEmpty && parent && parent.children.length === 0) { check(parent, [], [], name); return undefined; }
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
    const family = (root.attributes.Type ?? 'PARTICLE') as NonNullable<ParticleConfig['family']>;
    const special = ['TRAIL', 'STREAK', 'SWARM'].includes(family);
    const backend = family === 'GPU_PARTICLE' ? 'GPU' : 'CPU';
    if (!['PARTICLE', 'GPU_PARTICLE', 'TRAIL', 'STREAK', 'SWARM'].includes(family)) issue('@Type', `Type=${root.attributes.Type} 尚无预览实现`);
    const rawShader = root.attributes.Shader ?? 'ADDITIVE';
    const alphaTest = special && ['ADDITIVE_ALPHA_TEST', 'ALPHA_TEST'].includes(rawShader);
    const shader = alphaTest ? rawShader === 'ALPHA_TEST' ? 'ALPHA' : 'ADDITIVE' : rawShader;
    if (!['ADDITIVE', 'ALPHA', 'MULTIPLY'].includes(shader)) issue('@Shader', `尚未支持材质 profile ${shader}`);
    for (const a of ['Drawable', 'SlaveSystem', 'EmitterSound']) {
        if (root.attributes[a]) issue(`@${a}`, `${a} 需要本轮以外的运行分支`);
    }
    for (const a of ['IsEmitAboveGroundOnly', 'IsParticleUpTowardsEmitter', 'UseMaximumHeight']) {
        if (bool(root, a)) issue(`@${a}`, `${a} 需要额外地形/朝向上下文`);
    }
    const physics = module('Physics', special ? ['Default', 'Swarm'] : ['Default'], special);
    const draw = module('Draw', special ? ['Default', 'Trail', 'Lightning', 'Swarm'] : backend === 'CPU' ? ['Default'] : ['Gpu']);
    const drawModule = local(draw?.name ?? (backend === 'CPU' ? 'Default' : 'Gpu')) as NonNullable<ParticleConfig['drawModule']>;
    const swarmPhysics = local(physics?.name ?? '') === 'Swarm';
    if (backend !== 'CPU' && root.attributes.PerParticleAttachedSystem) issue('@PerParticleAttachedSystem', 'GPU 的逐粒子附属生命周期尚无已证合同');
    const volume = module('Volume', special ? ['Point', 'Line', 'Box', 'Cylinder', 'Sphere', 'Lightning'] : ['Point', 'Line', 'Box', 'Cylinder', 'Sphere']);
    if (local(volume?.name ?? '') === 'Lightning' && drawModule !== 'Lightning') issue('Volume/Lightning', 'Lightning 出生区域需要 Draw/Lightning 的起终点');
    const velocity = module('Velocity', ['Ortho', 'Cylindrical', 'Spherical', 'Outward'], special);
    const updateParent = child(root, 'Update');
    const update = updateParent ? module('Update', backend === 'GPU' ? ['Default', 'RenderObject'] : ['Default'], special) : undefined;
    if (backend === 'CPU' && !special && !update) issue('Update', 'CPU Default 路径需要 Update/Default');
    check(physics, swarmPhysics ? ['AttractStrength'] : ['Gravity', 'Swirly', 'ParticlesAttachToBone'], swarmPhysics ? [] : ['VelocityDamping', 'DriftVelocity'], swarmPhysics ? 'Physics/Swarm' : 'Physics/Default');
    if (!swarmPhysics) for (const a of ['Swirly', 'ParticlesAttachToBone']) if (bool(physics, a)) issue(`Physics/Default/@${a}`, `${a} 尚未播放`);
    const drawPath = `Draw/${drawModule}`;
    const geometryType = draw?.attributes.GeometryType ?? 'SIMPLE_QUAD';
    const shaderFile = (draw?.attributes.Shader ?? 'GPUParticle.fx').toLowerCase();
    let trail: ParticleConfig['trail'], lightning: ParticleConfig['lightning'], swarm: ParticleConfig['swarm'];
    const bounded = (value: number, path: string, maximum = 1e6): number => {
        if (Math.abs(value) > maximum) issue(path, `参数超过特殊粒子预览预算 ${maximum}`, 'error', 'value');
        return value;
    };
    if (drawModule === 'Trail') {
        check(draw, ['TrailLife', 'UTile', 'VTile', 'UScrollRate', 'VScrollRate'], [], drawPath);
        const life = scalar(draw, 'TrailLife', 30, `${drawPath}/@TrailLife`);
        if (!Number.isInteger(life) || life < 1 || life > 600) issue(`${drawPath}/@TrailLife`, '拖尾寿命须为 1～600 帧整数，历史点超过预算时暂停预览', 'error', 'value');
        trail = { life, uTile: bounded(scalar(draw, 'UTile', 1, `${drawPath}/@UTile`), `${drawPath}/@UTile`), vTile: bounded(scalar(draw, 'VTile', 1, `${drawPath}/@VTile`), `${drawPath}/@VTile`),
            uScrollRate: bounded(scalar(draw, 'UScrollRate', 0, `${drawPath}/@UScrollRate`), `${drawPath}/@UScrollRate`), vScrollRate: bounded(scalar(draw, 'VScrollRate', 0, `${drawPath}/@VScrollRate`), `${drawPath}/@VScrollRate`) };
    } else if (drawModule === 'Lightning') {
        const names = ['StartAmplitude', 'EndAmplitudeChange', 'StartFrequency', 'EndFrequencyChange', 'StartPhase', 'EndPhaseChange'] as const;
        check(draw, [], ['StartPoint', 'EndPoint', 'RandomSet'], drawPath);
        for (const name of ['StartPoint', 'EndPoint']) {
            if (!child(draw, name)) issue(`${drawPath}/${name}`, `闪电缺少必需 ${name}`, 'error', 'value');
            if (draw?.children.filter(node => local(node.name) === name).length !== 1) issue(`${drawPath}/${name}`, `闪电需要且只能有一个 ${name}`, 'error', 'value');
        }
        const randomSets = draw?.children.filter(node => local(node.name) === 'RandomSet') ?? [];
        if (randomSets.length !== 2) issue(`${drawPath}/RandomSet`, '闪电需要两组 RandomSet 波形参数', 'error', 'value');
        lightning = { start: vector(draw, 'StartPoint', `${drawPath}/StartPoint`), end: vector(draw, 'EndPoint', `${drawPath}/EndPoint`),
            randomSets: randomSets.slice(0, 2).map((node, index) => {
                const path = `${drawPath}/RandomSet[${index + 1}]`; check(node, [], [...names], path);
                const result = {} as LightningRandomSet;
                for (const name of names) {
                    if (node.children.filter(c => local(c.name) === name).length > 1) issue(`${path}/${name}`, '波形参数不能重复', 'error', 'value');
                    const value = range(node, name, 0, `${path}/${name}`);
                    bounded(value.low, `${path}/${name}/@Low`); bounded(value.high, `${path}/${name}/@High`);
                    result[`${name[0].toLowerCase()}${name.slice(1)}` as keyof LightningRandomSet] = value;
                }
                return result;
            }) };
        for (const [name, point] of [['StartPoint', lightning.start], ['EndPoint', lightning.end]] as const)
            point.forEach((value, axis) => bounded(value, `${drawPath}/${name}/@${['x', 'y', 'z'][axis]}`));
    } else if (drawModule === 'Swarm') {
        check(draw, ['OpaqueSpeed', 'TransparentSpeed', 'SpeedStretchAmount', 'AttractStrength', 'EnvironmentTexture'], [], drawPath);
        swarm = { opaqueSpeed: bounded(scalar(draw, 'OpaqueSpeed', 0, `${drawPath}/@OpaqueSpeed`), `${drawPath}/@OpaqueSpeed`),
            transparentSpeed: bounded(scalar(draw, 'TransparentSpeed', 100, `${drawPath}/@TransparentSpeed`), `${drawPath}/@TransparentSpeed`),
            speedStretchAmount: bounded(scalar(draw, 'SpeedStretchAmount', 1, `${drawPath}/@SpeedStretchAmount`), `${drawPath}/@SpeedStretchAmount`),
            attractStrength: bounded(scalar(draw, 'AttractStrength', 0, `${drawPath}/@AttractStrength`), `${drawPath}/@AttractStrength`), environmentTexture: draw?.attributes.EnvironmentTexture ?? '' };
        if (swarm.opaqueSpeed < 0 || swarm.transparentSpeed < 0 || swarm.speedStretchAmount < 0) issue(drawPath, '群粒子速度阈值和拉伸量须为非负数', 'error', 'value');
    } else if (backend === 'CPU') check(draw, [], [], drawPath);
    else {
        check(draw, ['Shader', 'GeometryType', 'FramesPerRow', 'TotalFrames', 'SingleRow', 'RandTex', 'SpeedMultiplier',
            'SortParticles', 'EnableSoftParticles', 'MiscValue1', 'MiscValue2', 'MiscValue3', 'MiscValue4', 'DetailTexture'], [], drawPath);
        if (!['gpuparticle.fx', 'gpuparticleperpendicularbottom.fx'].includes(shaderFile)) issue(`${drawPath}/@Shader`, `尚未实现 shader ${draw?.attributes.Shader}`);
        if (!['SIMPLE_QUAD', 'CENTERED_QUAD', 'TWO_CONCENTRIC_QUADS', 'TWO_QUADS'].includes(geometryType)) issue(`${drawPath}/@GeometryType`, `几何 ${geometryType} 尚无普通合同`);
        if ((geometryType === 'TWO_QUADS') !== (shaderFile === 'gpuparticleperpendicularbottom.fx')) issue(drawPath, 'TWO_QUADS 需要 GpuParticlePerpendicularBottom.fx 和 GeometryUpdate 的对应组合');
        if (draw?.attributes.DetailTexture) issue(`${drawPath}/@DetailTexture`, 'DetailTexture 需要额外 shader 合同');
        for (const a of ['MiscValue1', 'MiscValue2', 'MiscValue3', 'MiscValue4']) if (scalar(draw, a) !== 0) issue(`${drawPath}/@${a}`, '非零 MiscValue 需要对应 shader 合同');
    }
    const geometry = local(update?.name ?? '') === 'RenderObject';
    let geometryUpdate: ParticleConfig['geometryUpdate'];
    if (geometry) {
        check(update, ['Rotation'], ['AngleX', 'AngleY', 'AngleZ', 'AngularRateX', 'AngularRateY', 'AngularRateZ', 'AngularDamping', 'StartSize', 'SizeRate', 'SizeDamping'], 'Update/RenderObject');
        const xyz = (name: string, fallback: number): [Range, Range, Range] => {
            const node = child(update, name); check(node, [], ['x', 'y', 'z'], `Update/RenderObject/${name}`);
            if (!node) issue(`Update/RenderObject/${name}`, 'GeometryUpdate 缺少必需三轴尺寸字段');
            return ['x', 'y', 'z'].map(axis => range(node, axis, fallback, `Update/RenderObject/${name}/${axis}`)) as [Range, Range, Range];
        };
        const rotation = update?.attributes.Rotation ?? 'ROTATION_OFF';
        if (!['ROTATION_OFF', 'ROTATE_AROUND_VELOCITY', 'ROTATE_IN_WORLD_SPACE'].includes(rotation)) issue('Update/RenderObject/@Rotation', `尚无旋转合同 ${rotation}`);
        geometryUpdate = { sizeStart: xyz('StartSize', 0), sizeRate: xyz('SizeRate', 0), sizeDamping: xyz('SizeDamping', 1),
            angle: ['X', 'Y', 'Z'].map(axis => range(update, `Angle${axis}`, 0, `Update/RenderObject/Angle${axis}`)) as [Range, Range, Range],
            angleRate: ['X', 'Y', 'Z'].map(axis => range(update, `AngularRate${axis}`, 0, `Update/RenderObject/AngularRate${axis}`)) as [Range, Range, Range],
            angleDamping: range(update, 'AngularDamping', 1, 'Update/RenderObject/AngularDamping'), rotation: rotation as NonNullable<ParticleConfig['geometryUpdate']>['rotation'] };
        if (shaderFile !== 'gpuparticleperpendicularbottom.fx') issue('Update/RenderObject', '普通 GPUParticle.fx 不消费 RenderObject 三轴 GeometryUpdate');
        if ([...geometryUpdate.sizeDamping, geometryUpdate.angleDamping].some(r => Math.min(r.low, r.high) <= 0)) issue('Update/RenderObject', 'GPU 解析三轴阻尼需要正数', 'error', 'value');
    } else {
        check(update, ['Rotation'], ['SizeRate', 'SizeRateDamping', 'AngleZ', 'AngularRateZ', 'AngularDamping', 'AngleXY', 'AngularRateXY', 'AngularDampingXY'], 'Update/Default');
        if (update?.attributes.Rotation && update.attributes.Rotation !== 'ROTATION_OFF') issue('Update/Default/@Rotation', '普通 Default 的方向覆盖分支尚未闭合');
        if (shaderFile === 'gpuparticleperpendicularbottom.fx' && backend === 'GPU') issue('Update', 'GpuParticlePerpendicularBottom.fx 需要 Update/RenderObject 三轴 GeometryUpdate');
    }
    const event = child(root, 'Event');
    check(event, [], ['Life', 'Collision'], 'Event');
    const events: NonNullable<ParticleConfig['events']> = [];
    const eventIndices = new Map<string, number>();
    for (const node of event?.children ?? []) {
        const type = local(node.name);
        const index = (eventIndices.get(type) ?? 0) + 1; eventIndices.set(type, index);
        const path = `Event/${type}[${index}]`;
        check(node, ['EventFX', 'PerParticle', 'KillAfterEvent', ...(type === 'Collision' ? ['OrientFXToTerrain'] : [])],
            [type === 'Life' ? 'EventTime' : 'HeightOffset'], path);
        const perParticle = bool(node, 'PerParticle', false, `${path}/@PerParticle`);
        if (backend === 'GPU' && perParticle) issue(`${path}/@PerParticle`, 'GPU 逐粒子事件缺少已证 Storage 合同');
        const base = { fx: node.attributes.EventFX ?? '', perParticle, kill: bool(node, 'KillAfterEvent', false, `${path}/@KillAfterEvent`) };
        if (type === 'Life') events.push({ ...base, type, eventTime: range(node, 'EventTime', 0, `${path}/EventTime`) });
        else if (type === 'Collision') events.push({ ...base, type, heightOffset: range(node, 'HeightOffset', 0, `${path}/HeightOffset`), orientToTerrain: bool(node, 'OrientFXToTerrain', false, `${path}/@OrientFXToTerrain`) });
    }
    const wind = child(root, 'Wind');
    check(wind, ['Motion', 'Strength', 'FullStrengthDist', 'ZeroStrengthDist', 'AngleChangeMin', 'AngleChangeMax',
        'PingPongStartAngleMin', 'PingPongStartAngleMax', 'PingPongEndAngleMin', 'PingPongEndAngleMax', 'TurbulenceAmplitude', 'TurbulenceFrequency'], [], 'Wind');
    if (wind && (wind.attributes.Motion ?? 'NOT_USED') !== 'NOT_USED') issue('Wind/@Motion', '风场尚未播放');
    if (local(volume?.name ?? '') === 'Lightning') check(volume, ['IsHollow'], [], 'Volume/Lightning');
    else check(volume, ['IsHollow', 'Radius', 'RadiusRate', 'Length'], ['StartPoint', 'EndPoint', 'HalfSize', 'Offset'], 'Volume');
    check(velocity, [], ['Position', 'Radial', 'Normal', 'Speed', 'OtherSpeed'], 'Velocity');
    const position = child(velocity, 'Position'); check(position, [], ['x', 'y', 'z'], 'Velocity/Ortho/Position');
    const age = (node: FxNode, path: string): number => {
        const raw = node.attributes.RelativeAge;
        return Math.fround(Math.fround(number(raw?.replace(/%$/, ''), 0, `${path}/@RelativeAge`)) * Math.fround(0.01));
    };
    let colors: ColorKey[] | undefined, alphas: AlphaKey[] | undefined;
    const alphaOrigins: (number | undefined)[] = Array(8).fill(undefined);
    const colorsNode = child(root, 'Colors'), alphasNode = child(root, 'Alphas');
    check(colorsNode, ['UseHouseColor', 'HouseColorSaturation'], ['Color', 'ColorScale'], 'Colors');
    check(alphasNode, [], ['Alpha'], 'Alphas');
    if (bool(colorsNode, 'UseHouseColor')) issue('Colors/@UseHouseColor', '阵营颜色需要显式对象颜色上下文');
    if (colorsNode) {
        colors = Array.from({ length: 8 }, () => ({ time: 0, value: [0, 0, 0] as Vec3 }));
        for (const [index, node] of colorsNode.children.filter(n => local(n.name) === 'Color').entries()) {
            const path = `Colors/Color[${index + 1}]`;
            check(node, ['Frame', 'RelativeAge'], ['Color'], path);
            const frame = scalar(node, 'Frame', 1000, `${path}/@Frame`);
            if (frame < 1 || frame > 8 || !Number.isInteger(frame)) { issue(`${path}/@Frame`, `槽 ${frame} 不参与合法八槽输入`, 'warning', 'slot'); continue; }
            const color = child(node, 'Color'); check(color, ['r', 'g', 'b'], [], `${path}/Color`);
            if (!color) issue(`${path}/Color`, '颜色槽缺少必需 Color 子节点', 'error', 'value');
            colors[frame - 1] = { time: age(node, path), value: ['r', 'g', 'b'].map(a => scalar(color, a, 0, `${path}/Color/@${a}`)) as Vec3 };
            if (backend === 'GPU' && frame > 4) issue(`${path}/@Frame`, `GPU 仅消费槽 1～4；保留槽 ${frame}`, 'warning', 'slot');
        }
    }
    if (alphasNode) {
        alphas = Array.from({ length: 8 }, () => ({ time: 0, range: constant(0) }));
        for (const [index, node] of alphasNode.children.entries()) {
            const path = `Alphas/Alpha[${index + 1}]`;
            const frame = scalar(node, 'Frame', 1000, `${path}/@Frame`);
            if (frame < 1 || frame > 8 || !Number.isInteger(frame)) { issue(`${path}/@Frame`, `槽 ${frame} 不参与合法八槽输入`, 'warning', 'slot'); continue; }
            alphas[frame - 1] = { time: age(node, path), range: range({ name: 'Alphas', attributes: {}, children: [node] }, local(node.name), 0, path) };
            alphaOrigins[frame - 1] = index + 1;
            if (backend === 'GPU' && frame > 4) issue(`${path}/@Frame`, `GPU 仅消费槽 1～4；保留槽 ${frame}`, 'warning', 'slot');
        }
        if (backend === 'GPU') issue('Alphas/Alpha/@RelativeAge', '此 GPU 路径由颜色槽控制 RGBA 时间；Alpha 时间保留但不消费', 'warning', 'alpha-time');
    }
    const config: ParticleConfig = {
        backend, family, drawModule, trail, lightning, swarm, alphaTest,
        swarmPhysics: swarmPhysics ? { attractStrength: bounded(scalar(physics, 'AttractStrength', 0, 'Physics/Swarm/@AttractStrength'), 'Physics/Swarm/@AttractStrength') } : undefined,
        geometryUpdate, geometryType: geometryType as ParticleConfig['geometryType'],
        shaderProfile: backend === 'CPU' ? 'CPU_DEFAULT' : shaderFile === 'gpuparticleperpendicularbottom.fx' ? 'GPU_PERPENDICULAR_BOTTOM' : 'GPU_STANDARD',
        softParticles: backend === 'GPU' && bool(draw, 'EnableSoftParticles', true),
        attachedSystem: backend === 'CPU' ? root.attributes.PerParticleAttachedSystem : undefined, events,
        id: root.attributes.id ?? '', texture: root.attributes.ParticleTexture ?? '', shader: shader as ParticleConfig['shader'],
        lifetime: range(root, 'Lifetime'), initialDelay: range(root, 'InitialDelay'), burstDelay: range(root, 'BurstDelay'), burstCount: range(root, 'BurstCount'),
        size: range(root, 'Size'), startSizeRate: range(root, 'StartSizeRate'), systemLifetime: scalar(root, 'SystemLifetime'), isOneShot: bool(root, 'IsOneShot'),
        velocityDamping: range(physics, 'VelocityDamping', 1, 'Physics/Default/VelocityDamping'), gravity: scalar(physics, 'Gravity'), drift: vector(physics, 'DriftVelocity'),
        sizeRate: geometry ? constant(0) : range(update, 'SizeRate'), sizeRateDamping: range(update, 'SizeRateDamping', 1),
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
        if (backend === 'GPU' && Math.min(config[name].low, config[name].high) <= 0) issue(name, '解析阻尼需为正；保留原文且暂停受影响系统', 'error', 'value');
    }
    if (Math.min(...config.volume.halfSize, config.volume.radius, config.volume.length) < 0) issue('Volume', '本轮体积合同仅支持非负有限尺寸', 'error', 'value');
    if (backend === 'GPU' && (config.startSizeRate.low !== 0 || config.startSizeRate.high !== 0)) issue('StartSizeRate', '此普通 GPU 路径不消费出生尺寸累加；保留随机采样与系统累加，不将其误作 SizeRate', 'warning', 'gpu-unused');
    if (!Number.isInteger(config.atlas.singleRow) || !Number.isInteger(config.atlas.randTex) || config.atlas.speedMultiplier < 0) issue('Draw/Gpu', 'Atlas 行/模式须为整数，播放速度需非负', 'error', 'value');
    if (special) {
        issue('Draw', '特殊粒子使用重建预览模型，尚未与游戏画面对照；运动、波形和材质可能有差异', 'warning', 'special-preview');
    }
    if (backend === 'CPU' && !special) {
        if (alphas && !alphas[1].time) issue(alphaOrigins[1] ? `Alphas/Alpha[${alphaOrigins[1]}]/@RelativeAge` : 'Alphas', 'CPU Alpha 第二槽时间为零时原生当前初值依赖分配器复用，未证分支暂停播放', 'error', 'cpu-alpha-initial');
        for (const [name, keys] of [['Colors', colors], ['Alphas', alphas]] as const) {
            if (keys?.some(key => key.time < 0)) issue(name, 'CPU 负时间的无符号帧间隔分支尚未闭合', 'error', 'cpu-key-time');
        }
        if (alphas) {
            const life = Math.trunc(Math.min(config.lifetime.low, config.lifetime.high));
            for (let i = 1; i < 8 && alphas[i].time !== 0; i++) {
                if (Math.trunc(life * alphas[i].time) === Math.trunc(life * Math.max(0, alphas[i - 1].time)))
                    issue(`Alphas/Alpha[${alphaOrigins[i]}]/@RelativeAge`, 'CPU Alpha 帧间隔为零会产生非有限斜率，暂停受影响系统', 'error', 'cpu-alpha-interval');
            }
        }
    }
    return { config: issues.some(i => i.severity === 'error') ? undefined : config, issues };
}
