import { FxRenderer } from '../webview/fx/render/renderer';
import { defaultCamera } from '../webview/fx/render/camera';
import { ParticleRuntime } from '../webview/fx/runtime';
import type { PreviewIR } from '../src/fx/previewProtocol';

const canvas = document.querySelector('canvas')!;
const gl = canvas.getContext('webgl2')!;
const counts = {buffers: 0, textures: 0, programs: 0, vaos: 0};
for (const [create, remove, key] of [
    ['createBuffer', 'deleteBuffer', 'buffers'], ['createTexture', 'deleteTexture', 'textures'],
    ['createProgram', 'deleteProgram', 'programs'], ['createVertexArray', 'deleteVertexArray', 'vaos'],
] as const) {
    const make = gl[create].bind(gl), drop = gl[remove].bind(gl);
    (gl as unknown as Record<string, unknown>)[create] = (...args: unknown[]) => {
        const result = (make as (...args: unknown[]) => unknown)(...args); if (result) counts[key]++; return result;
    };
    (gl as unknown as Record<string, unknown>)[remove] = (handle: unknown) => {
        if (handle) counts[key]--; (drop as (v: unknown) => void)(handle);
    };
}
let renderer: FxRenderer | undefined;
let ir: PreviewIR;
let runtimes: {key: string; runtime: ParticleRuntime; textureKey?: string}[] = [];
const errors: string[] = [];
function close() { renderer?.dispose(); renderer = undefined; runtimes.forEach(r => r.runtime.dispose()); runtimes = []; }
function load(value: PreviewIR) {
    close(); ir = value; renderer = new FxRenderer(canvas, message => errors.push(message));
    runtimes = value.systems.filter(s => s.config && s.triggered).map((s, i) => ({key: s.key,
        textureKey: s.textureKey, runtime: new ParticleRuntime(s.config!, value.scene.seed + i,
            {offset: s.offset, rotation: s.rotation, rotationXYZ: s.rotationXYZ, startFrame: s.startFrame, systemLifeFrames: s.systemLifeFrames})}));
    renderer.setLayers(runtimes.map(r => ({id: r.key, config: r.runtime.config, births: r.runtime.particles, alphaSamples: r.runtime.alphaSamples, textureKey: r.textureKey, visible: true})));
    for (const texture of value.textures) renderer.uploadTexture(texture);
    return seek(0);
}
function seek(frame: number) {
    if (!renderer) throw new Error('Renderer is closed');
    for (const r of runtimes) r.runtime.seek(frame);
    renderer.setLayers(runtimes.map(r => ({id: r.key, config: r.runtime.config, births: r.runtime.particles, alphaSamples: r.runtime.alphaSamples, textureKey: r.textureKey, visible: true})));
    const stats = renderer.render(frame, {...defaultCamera}, ir.scene, '#080a0c', false);
    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let hash = 2166136261, lit = 0;
    for (let i = 0; i < pixels.length; i += 4) {
        hash = Math.imul(hash ^ pixels[i], 16777619) >>> 0;
        hash = Math.imul(hash ^ pixels[i+1], 16777619) >>> 0;
        if (pixels[i] > 30 || pixels[i+1] > 30 || pixels[i+2] > 30) lit++;
    }
    return {stats, hash, lit, counts: {...counts}, errors: errors.slice(), glError: gl.getError()};
}
Object.assign(window, {fxHarness: {load, seek, close, counts}});
