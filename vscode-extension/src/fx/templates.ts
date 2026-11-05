/** Small complete templates. Texture is an explicit author choice, never a fabricated id. */
export const fxPresets = ['闪光', '烟雾', '喷射火花', '持续光环'] as const;
export type FxPreset = typeof fxPresets[number];
export function escapeXml(value: string): string { return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;'); }
export function createFxTemplate(preset: FxPreset, id: string, texture?: string, prefix = ''): string {
    if (!fxPresets.includes(preset) || !/^[A-Za-z_][A-Za-z0-9_.-]*$/.test(id) || !/^(?:[A-Za-z_][\w.-]*:)?$/.test(prefix)) throw new Error('无效的特效模板参数。');
    const q = (name: string): string => `${prefix}${name}`;
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
