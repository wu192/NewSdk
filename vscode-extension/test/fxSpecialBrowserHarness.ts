import { FxRenderer, type RenderLayer } from '../webview/fx/render/renderer';
import { defaultCamera, type CameraState } from '../webview/fx/render/camera';
import { ParticleWorld } from '../webview/fx/runtime';
import type { FxSystem, PreviewIR } from '../src/fx/previewProtocol';

const canvas = document.querySelector('canvas')!, gl = canvas.getContext('webgl2')!;
const counts = { buffers: 0, textures: 0, programs: 0, vaos: 0, framebuffers: 0 };
const resetHandles: (() => void)[] = [];
for (const [create, remove, key] of [['createBuffer', 'deleteBuffer', 'buffers'], ['createTexture', 'deleteTexture', 'textures'], ['createProgram', 'deleteProgram', 'programs'], ['createVertexArray', 'deleteVertexArray', 'vaos'], ['createFramebuffer', 'deleteFramebuffer', 'framebuffers']] as const) {
    const handles = new Set<unknown>(), make = gl[create].bind(gl) as (...args: unknown[]) => unknown, drop = gl[remove].bind(gl) as (handle: unknown) => void;
    resetHandles.push(() => { handles.clear(); counts[key] = 0; });
    (gl as unknown as Record<string, unknown>)[create] = (...args: unknown[]) => { const handle = make(...args); if (handle) { handles.add(handle); counts[key]++; } return handle; };
    (gl as unknown as Record<string, unknown>)[remove] = (handle: unknown) => { if (handles.delete(handle)) counts[key]--; drop(handle); };
}
canvas.addEventListener('webglcontextlost', () => resetHandles.forEach(reset => reset()));
let renderer: FxRenderer | undefined, world: ParticleWorld | undefined, ir: PreviewIR;
let camera: CameraState = { ...defaultCamera, distance: 100, target: [0, 0, 10] };
let visible = true;
const messages: string[] = [];
function close(): void { renderer?.dispose(); world?.dispose(); renderer = undefined; world = undefined; }
function definition(system: FxSystem) {
    return { id: system.key, sourceId: system.key, config: system.config!, options: { offset: system.offset, rotationXYZ: system.rotationXYZ, rotationMatrix: system.rotationMatrix, startFrame: system.startFrame } };
}
function layers(): RenderLayer[] {
    return world!.systems.map(system => {
        const descriptor = ir.systems.find(value => value.key === system.sourceId) as FxSystem & { environmentTextureKey?: string };
        return { id: system.id, config: system.runtime.config, births: system.runtime.particles, alphaSamples: system.runtime.alphaSamples,
            textureKey: descriptor?.textureKey, environmentTextureKey: descriptor?.environmentTextureKey, visible,
            sample: (birth, frame) => system.runtime.sample(birth, frame) };
    });
}
function upload(): void { for (const texture of ir.textures) if (texture.status === 'ready') renderer!.uploadTexture(texture); }
function load(value: PreviewIR) {
    close(); ir = value; visible = true; messages.length = 0;
    camera = { ...defaultCamera, distance: 100, target: [0, 0, 10] };
    renderer = new FxRenderer(canvas, message => messages.push(message));
    world = new ParticleWorld(ir.systems.filter(system => system.config && system.triggered).map(definition), ir.scene.seed, 2048, 18000, { sourceMotion: ir.scene.sourceMotion, groundHeight: ir.scene.ground });
    renderer.setLayers(layers(), ir.textures.map(texture => texture.key)); upload(); return seek(0);
}
function draw(frame: number) {
    if (!renderer || !world) throw new Error('Preview closed');
    renderer.setLayers(layers(), ir.textures.map(texture => texture.key));
    const stats = renderer.render(frame, camera, { ...ir.scene, source: [1e5, 1e5, 1e5], target: [1e5 + 1, 1e5, 1e5], sourceMotion: undefined }, '#080a0c', false, false, false);
    const pixels = new Uint8Array(canvas.width * canvas.height * 4); gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let hash = 2166136261, lit = 0, firstLit = -1;
    for (let index = 0; index < pixels.length; index += 4) {
        for (let channel = 0; channel < 3; channel++) hash = Math.imul(hash ^ pixels[index + channel], 16777619) >>> 0;
        if (pixels[index] > 30 || pixels[index + 1] > 30 || pixels[index + 2] > 30) { lit++; if (firstLit < 0) firstLit = index / 4; }
    }
    const rect = canvas.getBoundingClientRect(), pickX = firstLit % canvas.width, pickY = Math.floor(firstLit / canvas.width);
    const picked = firstLit < 0 ? undefined : renderer.pick(rect.left + (pickX + .5) / canvas.width * rect.width, rect.top + (canvas.height - pickY - .5) / canvas.height * rect.height, frame);
    return { hash, lit, picked, stats, state: JSON.stringify(world.systems.map(system => system.runtime.captureState())), stateBytes: world.stats.stateBytes,
        snapshotBytes: world.snapshotBytes, budgetExceeded: world.budgetExceeded, counts: { ...counts }, glError: gl.getError() };
}
function seek(frame: number) { world!.seek(frame); return draw(frame); }
function view(yaw: number) { camera.yaw = yaw; return draw(world!.frame); }
function hide(hidden: boolean) { visible = !hidden; return draw(world!.frame); }
function restoreAtSameFrame() {
    const runtime = world!.systems[0].runtime, before = runtime.captureState(), moved = structuredClone(before);
    for (const particle of moved.particles) {
        particle.position[1] += 10;
        for (const point of particle.trail ?? []) point.position[1] += 10;
        if (particle.lightning) { particle.lightning.start[1] += 10; particle.lightning.end[1] += 10; }
    }
    runtime.restoreState(moved); const changed = draw(world!.frame);
    runtime.restoreState(before); return { changed, restored: draw(world!.frame) };
}
function pickBudget() {
    const layer = layers()[0], birth = layer.births[0];
    renderer!.setLayers([{ ...layer, births: Array.from({ length: 2000 }, () => birth) }]);
    const picked = renderer!.pick(0, 0, world!.frame); renderer!.setLayers(layers());
    return { picked, messages: messages.slice() };
}
async function contextCycle() {
    const frame = world!.frame, ext = gl.getExtension('WEBGL_lose_context'); if (!ext) throw new Error('WEBGL_lose_context unavailable');
    const lost = new Promise<void>(resolve => canvas.addEventListener('webglcontextlost', () => resolve(), { once: true })); ext.loseContext(); await lost;
    const restored = new Promise<void>(resolve => canvas.addEventListener('webglcontextrestored', () => resolve(), { once: true })); setTimeout(() => ext.restoreContext(), 30); await restored;
    upload(); return { ...draw(frame), messages: messages.slice() };
}
Object.assign(window, { fxSpecial: { load, seek, draw, view, hide, restoreAtSameFrame, pickBudget, contextCycle, close, counts } });
