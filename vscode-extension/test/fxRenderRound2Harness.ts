import { FxRenderer, type RenderLayer } from '../webview/fx/render/renderer';
import { cameraMatrices, project, type CameraState } from '../webview/fx/render/camera';
import type { BirthRecord, ParticleConfig, ParticleSample, Range, Vec3 } from '../webview/fx/runtime/types';
import type { FxScene } from '../src/fx/previewProtocol';
const canvas=document.querySelector('canvas')!,gl=canvas.getContext('webgl2')!;
const counts={buffers:0,textures:0,programs:0,vaos:0,framebuffers:0};
const resetHandles:(()=>void)[]=[];
for(const [create,drop,key] of [['createBuffer','deleteBuffer','buffers'],['createTexture','deleteTexture','textures'],['createProgram','deleteProgram','programs'],['createVertexArray','deleteVertexArray','vaos'],['createFramebuffer','deleteFramebuffer','framebuffers']] as const){
    const handles=new Set<unknown>(),make=gl[create].bind(gl) as (...args:unknown[])=>unknown,remove=gl[drop].bind(gl) as (handle:unknown)=>void;
    resetHandles.push(()=>{handles.clear();counts[key]=0;});
    (gl as unknown as Record<string,unknown>)[create]=(...args:unknown[])=>{const h=make(...args);if(h){handles.add(h);counts[key]++;}return h;};
    (gl as unknown as Record<string,unknown>)[drop]=(h:unknown)=>{if(handles.delete(h))counts[key]--;remove(h);};
}
canvas.addEventListener('webglcontextlost',()=>resetHandles.forEach(reset=>reset()));
const range=(low:number):Range=>({low,high:low,type:'CONSTANT'});
function config():ParticleConfig{return {
    id:'contract',texture:'solid',shader:'ADDITIVE',backend:'GPU',shaderProfile:'GPU_STANDARD',geometryType:'SIMPLE_QUAD',
    lifetime:range(100),initialDelay:range(0),burstDelay:range(1),burstCount:range(1),size:range(12),startSizeRate:range(0),systemLifetime:1,isOneShot:false,
    velocityDamping:range(1),gravity:0,drift:[0,0,0],sizeRate:range(0),sizeRateDamping:range(1),
    angleZ:range(0),angularRateZ:range(0),angularDamping:range(1),angleXY:range(0),angularRateXY:range(0),angularDampingXY:range(1),
    colors:[0,.33,.66,1].map(time=>({time,value:[.4,.6,.8]})),alphas:[0,.33,.66,1].map(time=>({time,range:range(.5)})),colorScale:range(1),
    atlas:{framesPerRow:1,totalFrames:1,singleRow:0,randTex:0,speedMultiplier:1},
    volume:{type:'Point',hollow:false,start:[0,0,0],end:[0,0,0],halfSize:[0,0,0],radius:0,radiusRate:0,length:0,offset:[0,0,0]},
    velocity:{type:'Ortho',x:range(0),y:range(0),z:range(0),radial:range(0),normal:range(0),speed:range(0),otherSpeed:range(0)},
    groundAligned:false,sortParticles:false,sortLevel:0,
};}
const camera:CameraState={yaw:-Math.PI/4,pitch:.55,distance:60,target:[0,0,3]};
const scene:FxScene={source:[100000,100000,100000],target:[100001,100001,100001],ground:0,seed:1,terrain:'land'};
const messages:string[]=[];
let renderer:FxRenderer|undefined,layer:RenderLayer;
function upload(){renderer!.uploadTexture({key:'solid',status:'ready',width:4,height:4,pixels:Array.from({length:16},()=>[128,192,64,128]).flat()} as never);}
function load(patch:Partial<ParticleConfig>={},position:Vec3=[0,0,3],cpuColor:[number,number,number,number]=[.4,.6,.8,.5]){
    renderer?.dispose();renderer=new FxRenderer(canvas,message=>messages.push(message));
    const c={...config(),...patch};
    const birth:BirthRecord={id:0,position,velocity:[0,0,0],life:100,creationFrame:0,seed:2};
    const sample:ParticleSample={alive:true,position,size:12,angleZ:0,color:cpuColor,atlas:{current:[0,0],next:[0,0],blend:0,scale:1}};
    layer={id:'one',config:c,births:[birth],alphaSamples:c.alphas?.map(a=>a.range.low)??[.5,.5,.5,.5],textureKey:'solid',visible:true,...(c.backend==='CPU'?{sample:()=>sample}:{})};
    renderer.setLayers([layer]);upload();return draw();
}
function draw(frame=0,ground=false,soft=true){
    const stats=renderer!.render(frame,camera,scene,'#203040',ground,false,soft);
    const pixels=new Uint8Array(canvas.width*canvas.height*4);gl.readPixels(0,0,canvas.width,canvas.height,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
    const at=(point:Vec3)=>{const p=project(point,cameraMatrices(camera,canvas.width/canvas.height).matrix,canvas.width,canvas.height);const x=Math.max(0,Math.min(canvas.width-1,Math.floor(p[0]))),y=Math.max(0,Math.min(canvas.height-1,Math.floor(canvas.height-p[1])));return [...pixels.slice((y*canvas.width+x)*4,(y*canvas.width+x)*4+4)];};
    let hash=2166136261;for(const p of pixels)hash=Math.imul(hash^p,16777619)>>>0;
    return {stats,hash,center:at(layer.births[0].position),above:at([0,0,1]),upper:at([0,0,4]),below:at([0,0,-1]),error:gl.getError()};
}
async function contextCycle(){
    const ext=gl.getExtension('WEBGL_lose_context')!;
    const lost=new Promise<void>(resolve=>canvas.addEventListener('webglcontextlost',()=>resolve(),{once:true}));ext.loseContext();await lost;
    const restored=new Promise<void>(resolve=>canvas.addEventListener('webglcontextrestored',()=>resolve(),{once:true}));setTimeout(()=>ext.restoreContext(),50);await restored;
    upload();return {...draw(0,true),messages:messages.slice()};
}
function budget(){
    layer.births=Array.from({length:1000000},()=>layer.births[0]);renderer!.setLayers([layer]);
    try{draw();return '';}catch(error){return String(error);}
}
function workload(frames=5){
    load({shader:'ADDITIVE',softParticles:false,size:range(.2)});
    canvas.style.width='1280px';canvas.style.height='720px';
    layer.births=Array.from({length:10000},(_,i)=>({id:i,position:[((i%100)-49.5)*.4,(Math.floor(i/100)-49.5)*.4,3] as Vec3,velocity:[0,0,0] as Vec3,life:100000,creationFrame:0,seed:i%16}));
    renderer!.setLayers([layer]);const times:number[]=[];let stats=renderer!.stats;
    for(let i=0;i<frames+2;i++){const start=performance.now();stats=renderer!.render(i,camera,scene,'#203040',false,false,false);gl.finish();if(i>=2)times.push(performance.now()-start);}
    const sorted=times.slice().sort((a,b)=>a-b);
    const result={particles:stats.particles,draws:stats.draws,canvasPixels:stats.canvasPixels,gpuBytes:stats.gpuBytes,p95Ms:sorted[Math.min(sorted.length-1,Math.floor(sorted.length*.95))],samples:times.length,renderer:'Chromium SwiftShader',referenceHardware:false,error:gl.getError()};
    renderer!.dispose();canvas.style.width='640px';canvas.style.height='360px';return result;
}
Object.assign(window,{fxRound2:{load,draw,contextCycle,budget,workload,close:()=>renderer?.dispose(),counts}});
