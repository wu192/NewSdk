import { FxRenderer } from '../webview/fx/render/renderer';
import { defaultCamera } from '../webview/fx/render/camera';
import { ParticleWorld } from '../webview/fx/runtime';
import type { PreviewIR, FxSystem } from '../src/fx/previewProtocol';

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
let world: ParticleWorld | undefined;
let descriptors: FxSystem[] = [];
const errors: string[] = [];
function close() { renderer?.dispose(); renderer = undefined; world?.dispose(); world = undefined; descriptors=[]; }
function definition(s: FxSystem) { return {id:s.key,templateKey:s.templateKey,sourceId:s.key,orientToObject:s.orientToObject,createAtGroundHeight:s.createAtGroundHeight,config:s.config!,options:{offset:s.offset,rotation:s.rotation,rotationXYZ:s.rotationXYZ,rotationMatrix:s.rotationMatrix,startFrame:s.startFrame,systemLifeFrames:s.systemLifeFrames,initialDelayOverrideMs:s.initialDelayOverrideMs,createAtGroundHeight:s.createAtGroundHeight}}; }
function layers() { return (world?.systems??[]).map(s=> { const d=descriptors.find(d=>d.key===s.sourceId)??descriptors.find(d=>d.templateKey===s.templateKey); return {id:s.id,config:s.runtime.config,births:s.runtime.particles,alphaSamples:s.runtime.alphaSamples,textureKey:d?.textureKey,visible:true,sample:s.runtime.sample.bind(s.runtime)}; }); }
function load(value: PreviewIR) {
    close(); ir=value; renderer=new FxRenderer(canvas,message=>errors.push(message));
    descriptors=[...value.systems,...(value.dynamic?.templates??[]),...(value.dynamic?.fxLists.flatMap(l=>l.systems)??[])];
    world=new ParticleWorld(value.systems.filter(s=>s.config&&s.triggered).map(definition),value.scene.seed,32768,18000,value.dynamic?{...value.dynamic,templates:value.dynamic.templates.filter(s=>s.config).map(definition),fxLists:value.dynamic.fxLists.map(l=>({...l,systems:l.systems.filter(s=>s.config&&s.triggered).map(definition)})),groundHeight:value.scene.ground}:undefined);
    renderer.setLayers(layers(),value.textures.map(t=>t.key));
    for(const texture of value.textures)renderer.uploadTexture(texture);
    return seek(0);
}
function seek(frame: number, soft=true) {
    if (!renderer) throw new Error('Renderer is closed');
    world!.seek(frame);
    renderer.setLayers(layers(),ir.textures.map(t=>t.key));
    const stats = renderer.render(frame, {...defaultCamera}, ir.scene, '#080a0c', false, false, soft);
    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    let hash = 2166136261, lit = 0;
    for (let i = 0; i < pixels.length; i += 4) {
        hash = Math.imul(hash ^ pixels[i], 16777619) >>> 0;
        hash = Math.imul(hash ^ pixels[i+1], 16777619) >>> 0;
        if (pixels[i] > 30 || pixels[i+1] > 30 || pixels[i+2] > 30) lit++;
    }
    return {stats, hash, lit, systems:world?.systems.length, snapshotBytes:world?.snapshotBytes, counts: {...counts}, errors: errors.slice(), glError: gl.getError()};
}
Object.assign(window, {fxHarness: {load, seek, close, counts}});
