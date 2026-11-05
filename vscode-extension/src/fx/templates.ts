/** Small complete templates. Texture is an explicit author choice, never a fabricated id. */
export const fxPresets = ['闪光', '烟雾', '喷射火花', '持续光环', '拖尾', '闪电', '群粒子'] as const;
export type FxPreset = typeof fxPresets[number];
export function escapeXml(value: string): string { return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;').replace(/'/g, '&apos;'); }
export function createFxTemplate(preset: FxPreset, id: string, texture?: string, prefix = ''): string {
    if (!fxPresets.includes(preset) || !/^[A-Za-z_][A-Za-z0-9_.-]*$/.test(id) || !/^(?:[A-Za-z_][\w.-]*:)?$/.test(prefix)) throw new Error('无效的特效模板参数。');
    const q = (name: string): string => `${prefix}${name}`;
    if (preset === '拖尾' || preset === '闪电' || preset === '群粒子') return createSpecialTemplate(preset, id, texture, q);
    const smoke = preset === '烟雾'; const spark = preset === '喷射火花'; const halo = preset === '持续光环';
    const life = smoke ? 90 : halo ? 60 : spark ? 24 : 12;
    const size = smoke ? 6 : halo ? 4 : spark ? 1 : 12;
    const color = smoke ? 'r="0.5" g="0.5" b="0.5"' : spark ? 'r="1" g="0.45" b="0.08"' : 'r="1" g="1" b="1"';
    const rv = (name: string, low: number, high = low): string => `<${q(name)} Low="${low}" High="${high}"/>`;
    const alphas = [0, 0.2, 0.7, 1].map((age, index) => `<${q('Alpha')} Frame="${index + 1}" RelativeAge="${age * 100}%" Low="${index === 3 ? 0 : 1}" High="${index === 3 ? 0 : 1}"/>`).join('');
    const colors = [0, 0.2, 0.7, 1].map((age, index) => `<${q('Color')} Frame="${index + 1}" RelativeAge="${age * 100}%"><${q('Color')} ${color}/></${q('Color')}>`).join('');
    const volume = halo ? `<${q('Cylinder')} IsHollow="true" Radius="16" Length="0" RadiusRate="0"><${q('Offset')} x="0" y="0" z="0"/></${q('Cylinder')}>` : `<${q('Point')}/>`;
    const velocity = spark ? `<${q('Spherical')}>${rv('Speed', 2, 5)}</${q('Spherical')}>` : `<${q('Ortho')}><${q('Position')}>${rv('x', 0)}${rv('y', 0)}${rv('z', smoke ? 0.25 : 0)}</${q('Position')}></${q('Ortho')}>`;
    return `<${q('FXParticleSystemTemplate')} id="${id}" Type="GPU_PARTICLE" Shader="${smoke ? 'ALPHA' : 'ADDITIVE'}" IsOneShot="${!smoke && !halo}" SystemLifetime="${smoke || halo ? 0 : 1}"${texture ? ` ParticleTexture="${escapeXml(texture)}"` : ''}>
  ${rv('Lifetime', life)}
  ${rv('Size', size)}
  ${rv('StartSizeRate', 0)}
  ${rv('BurstDelay', smoke || halo ? 2 : 0)}
  ${rv('BurstCount', spark ? 24 : halo ? 4 : 1)}
  ${rv('InitialDelay', 0)}
  <${q('Alphas')}>${alphas}</${q('Alphas')}>
  <${q('Colors')}>${colors}${rv('ColorScale', 1)}</${q('Colors')}>
  <${q('Wind')} Motion="NOT_USED"/>
  <${q('Physics')}><${q('Default')} Gravity="${spark ? -0.08 : 0}">${rv('VelocityDamping', 1)}<${q('DriftVelocity')} x="0" y="0" z="0"/></${q('Default')}></${q('Physics')}>
  <${q('Draw')}><${q('Gpu')} Shader="GPUParticle.fx" GeometryType="SIMPLE_QUAD" FramesPerRow="1" TotalFrames="1" SingleRow="0" RandTex="0" EnableSoftParticles="false"/></${q('Draw')}>
  <${q('Volume')}>${volume}</${q('Volume')}>
  <${q('Velocity')}>${velocity}</${q('Velocity')}>
  <${q('Update')}><${q('Default')}>${rv('SizeRate', smoke ? 0.08 : 0)}${rv('SizeRateDamping', 1)}${rv('AngleZ', 0)}${rv('AngularRateZ', halo ? 0.01 : 0)}${rv('AngularDamping', 1)}${rv('AngleXY', 0)}${rv('AngularRateXY', 0)}${rv('AngularDampingXY', 1)}</${q('Default')}></${q('Update')}>
</${q('FXParticleSystemTemplate')}>`;
}

function createSpecialTemplate(preset: '拖尾' | '闪电' | '群粒子', id: string, texture: string | undefined, q: (name: string) => string): string {
    const trail = preset === '拖尾', lightning = preset === '闪电';
    const rv = (name: string, low: number, high = low): string => `<${q(name)} Low="${low}" High="${high}" Type="${low === high ? 'CONSTANT' : 'UNIFORM'}"/>`;
    const point = (name: string, x: number, y: number, z: number): string => `<${q(name)} x="${x}" y="${y}" z="${z}"/>`;
    const color = lightning ? 'r="0.3" g="0.6" b="1"' : trail ? 'r="1" g="0.5" b="0.1"' : 'r="0.25" g="1" b="0.6"';
    const alphas = [0, 30, 75, 100].map((age,index) => `<${q('Alpha')} Frame="${index+1}" RelativeAge="${age}%" Low="${index===3?0:1}" High="${index===3?0:1}"/>`).join('');
    const colors = [0, 30, 75, 100].map((age,index) => `<${q('Color')} Frame="${index+1}" RelativeAge="${age}%"><${q('Color')} ${color}/></${q('Color')}>`).join('');
    const randomSet = (amplitude: number, frequency: number): string => `<${q('RandomSet')}>${rv('StartAmplitude',amplitude*0.6,amplitude)}${rv('EndAmplitudeChange',-0.01,0.01)}${rv('StartFrequency',frequency)}${rv('EndFrequencyChange',-0.005,0.005)}${rv('StartPhase',0,6.283185)}${rv('EndPhaseChange',0.06,0.12)}</${q('RandomSet')}>`;
    const draw = trail ? `<${q('Trail')} TrailLife="24" UTile="1" VTile="1" UScrollRate="0.01" VScrollRate="0"/>`
        : lightning ? `<${q('Lightning')}>${point('StartPoint',0,0,8)}${point('EndPoint',80,0,24)}${randomSet(7,2)}${randomSet(2,9)}</${q('Lightning')}>`
        : `<${q('Swarm')} OpaqueSpeed="4" TransparentSpeed="0" SpeedStretchAmount="0.8" AttractStrength="0.02"/>`;
    const physics = trail ? `<${q('Default')} Gravity="-0.003">${rv('VelocityDamping',0.995)}${point('DriftVelocity',0,0,0)}</${q('Default')}>`
        : lightning ? '' : `<${q('Swarm')} AttractStrength="0.02"/>`;
    const volume = lightning ? `<${q('Lightning')}/>` : trail ? `<${q('Point')}/>` : `<${q('Sphere')} IsHollow="true" Radius="18"/>`;
    const velocity = lightning ? '' : trail ? `<${q('Ortho')}><${q('Position')}>${rv('x',1.4)}${rv('y',0.05)}${rv('z',0.3)}</${q('Position')}></${q('Ortho')}>`
        : `<${q('Spherical')}>${rv('Speed',1.2,2)}</${q('Spherical')}>`;
    return `<${q('FXParticleSystemTemplate')} id="${id}" Type="${trail?'TRAIL':lightning?'STREAK':'SWARM'}" Shader="${lightning?'ADDITIVE_ALPHA_TEST':'ADDITIVE'}" IsOneShot="false" SystemLifetime="0"${texture ? ` ParticleTexture="${escapeXml(texture)}"` : ''}>
  ${rv('Lifetime',lightning?15:trail?60:90)}
  ${rv('Size',lightning?2:trail?1.5:2.5)}
  ${rv('StartSizeRate',0)}
  ${rv('BurstDelay',lightning?5:trail?2:3)}
  ${rv('BurstCount',lightning||trail?1:3)}
  ${rv('InitialDelay',0)}
  <${q('Alphas')}>${alphas}</${q('Alphas')}>
  <${q('Colors')}>${colors}${rv('ColorScale',1)}</${q('Colors')}>
  <${q('Wind')} Motion="NOT_USED"/>
  <${q('Physics')}>${physics}</${q('Physics')}>
  <${q('Draw')}>${draw}</${q('Draw')}>
  <${q('Volume')}>${volume}</${q('Volume')}>
  <${q('Velocity')}>${velocity}</${q('Velocity')}>
  <${q('Update')}><${q('Default')}>${rv('SizeRate',0)}${rv('SizeRateDamping',1)}${rv('AngleZ',0)}${rv('AngularRateZ',0)}${rv('AngularDamping',1)}${rv('AngleXY',0)}${rv('AngularRateXY',0)}${rv('AngularDampingXY',1)}</${q('Default')}></${q('Update')}>
</${q('FXParticleSystemTemplate')}>`;
}
