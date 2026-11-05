import type { FxScene, FxTexture, FxTextureTransfer } from '../../../src/fx/previewProtocol';
import { RANDOM_TABLE, integrateDamping, sampleParticle, tableSample } from '../runtime';
import { sampleCpuParticle } from '../runtime/cpu';
import type { BirthRecord, ParticleConfig, ParticleSample, Range, Vec3 } from '../runtime/types';
import { cameraMatrices, CameraState, CameraMatrices, project, CAMERA_NEAR, CAMERA_FAR, CAMERA_FOV_SCALE } from './camera';
import { geometryContract, geometryCornerPosition, materialProfile } from './contracts';
import { cpuVertexShader, fragmentShader, groundFragmentShader, groundVertexShader, lineFragmentShader, lineVertexShader, vertexShader } from './shaders';

export interface RenderLayer {
    id: string; config: ParticleConfig; births: readonly BirthRecord[]; alphaSamples: readonly number[];
    textureKey?: string; visible: boolean;
    sample?: (birth: BirthRecord, frame: number) => ParticleSample;
}
interface BufferRecord { vao:WebGLVertexArrayObject; buffer:WebGLBuffer; capacity:number; stamp:string; count:number; births?:readonly BirthRecord[]; config?:ParticleConfig; staging:Float32Array; geometry:string; order?:readonly BirthRecord[]; }
interface GeometryRecord { vertices:WebGLBuffer; indices:WebGLBuffer; count:number; bytes:number; }
interface DepthRecord { framebuffer:WebGLFramebuffer; texture:WebGLTexture; width:number; height:number; }
interface TextureRecord { texture:WebGLTexture; bytes:number; }
export interface RenderStats { particles:number; draws:number; gpuBytes:number; canvasPixels:number; instanceUploads?:number; uploadedBytes?:number; softParticles?:boolean; }
const GPU_TEXTURE_BUDGET=128*1024*1024;
const GPU_INSTANCE_BUDGET=32*1024*1024;

export class FxRenderer {
    private gl:WebGL2RenderingContext;
    private program:WebGLProgram|null=null;
    private lineProgram:WebGLProgram|null=null;
    private cpuProgram:WebGLProgram|null=null;
    private groundProgram:WebGLProgram|null=null;
    private activeProgram:WebGLProgram|null=null;
    private geometries=new Map<string,GeometryRecord>();
    private depth:DepthRecord|null=null;
    private depthStamp='';
    private sceneStamp='';
    private sceneLineCount=0;
    private uploads=0;
    private uploadedBytes=0;
    private lineBuffer:WebGLBuffer|null=null;
    private lineVao:WebGLVertexArrayObject|null=null;
    private buffers=new Map<string,BufferRecord>();
    private textures=new Map<string,TextureRecord>();
    private uniforms=new Map<WebGLProgram,Map<string,WebGLUniformLocation|null>>();
    private parameterStates=new Map<WebGLProgram,{config:ParticleConfig;alpha:string}>();
    private curveCache=new WeakMap<ParticleConfig,{alpha:string;times:Float32Array;slopes:Float32Array;intercepts:Float32Array}>();
    private matrix:Float32Array=new Float32Array(16);
    private lost=false;
    private contextResourcesLost=false;
    private disposed=false;
    private restoreAttempts=0;
    private layers:RenderLayer[]=[];
    private statsValue:RenderStats={particles:0,draws:0,gpuBytes:0,canvasPixels:0};
    readonly status:(message:string)=>void;
    constructor(readonly canvas:HTMLCanvasElement,status:(message:string)=>void=()=>{}) {
        this.status=status;
        const gl=canvas.getContext('webgl2',{alpha:false,premultipliedAlpha:false,antialias:false,preserveDrawingBuffer:false,depth:true});
        if(!gl)throw new Error('此环境无 WebGL2；XML、结构与参数编辑仍可使用。');
        this.gl=gl;
        canvas.addEventListener('webglcontextlost',this.contextLost);
        canvas.addEventListener('webglcontextrestored',this.contextRestored);
        try {this.initialize();} catch(error){this.dispose();throw error;}
    }
    private contextLost=(event:Event)=>{event.preventDefault();this.lost=true;this.contextResourcesLost=true;this.status('GPU 上下文丢失，等待恢复；文档和编辑状态已保留。');};
    private contextRestored=()=>{
        if(this.disposed||this.restoreAttempts>=2){this.status('GPU 自动恢复已达两次，请重载预览。');return;}
        this.restoreAttempts++;
        try {this.releaseGraphicsResources();this.contextResourcesLost=false;this.uniforms.clear();this.initialize();this.lost=false;this.status('GPU 上下文已重建，请重载资源恢复贴图。');}
        catch(error){this.releaseGraphicsResources();this.status(`GPU 重建失败：${String(error)}。请重载预览。`);}
    };
    private shader(type:number,source:string):WebGLShader {
        const gl=this.gl,shader=gl.createShader(type);
        if(!shader)throw new Error('无法分配 shader。');
        gl.shaderSource(shader,source);gl.compileShader(shader);
        if(!gl.getShaderParameter(shader,gl.COMPILE_STATUS)){const error=gl.getShaderInfoLog(shader);gl.deleteShader(shader);throw new Error(`Shader 编译失败：${error}`);}
        return shader;
    }
    private link(v:string,f:string):WebGLProgram {
        const gl=this.gl,p=gl.createProgram();if(!p)throw new Error('无法创建 GPU 程序。');
        let vs:WebGLShader|undefined,fs:WebGLShader|undefined;
        try {
            vs=this.shader(gl.VERTEX_SHADER,v);fs=this.shader(gl.FRAGMENT_SHADER,f);
            gl.attachShader(p,vs);gl.attachShader(p,fs);gl.linkProgram(p);
            if(!gl.getProgramParameter(p,gl.LINK_STATUS))throw new Error(`Shader 链接失败：${gl.getProgramInfoLog(p)}`);
            return p;
        }catch(error){gl.deleteProgram(p);throw error;}
        finally{if(vs)gl.deleteShader(vs);if(fs)gl.deleteShader(fs);}
    }
    private initialize():void {
        const gl=this.gl;
        this.program=this.link(vertexShader,fragmentShader);this.cpuProgram=this.link(cpuVertexShader,fragmentShader);
        this.lineProgram=this.link(lineVertexShader,lineFragmentShader);this.groundProgram=this.link(groundVertexShader,groundFragmentShader);
        this.lineBuffer=gl.createBuffer()!;this.lineVao=gl.createVertexArray()!;
        gl.bindVertexArray(this.lineVao);gl.bindBuffer(gl.ARRAY_BUFFER,this.lineBuffer);
        gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,3,gl.FLOAT,false,28,0);
        gl.enableVertexAttribArray(1);gl.vertexAttribPointer(1,4,gl.FLOAT,false,28,12);
        gl.bindVertexArray(null);
        for(const program of [this.program,this.cpuProgram]) {
            this.use(program);gl.uniform1i(this.uniform('particleTexture'),0);gl.uniform1i(this.uniform('sceneDepth'),1);
            gl.uniform2f(this.uniform('clipPlanes'),CAMERA_NEAR,CAMERA_FAR);
        }
        this.use(this.program);gl.uniform4fv(this.uniform('randomTable[0]'),RANDOM_TABLE.flat());
        gl.disable(gl.CULL_FACE);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.depthMask(false);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL,false);gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL,gl.NONE);
    }
    private use(program:WebGLProgram):void {if(this.activeProgram!==program){this.activeProgram=program;this.gl.useProgram(program);}}
    private uniform(name:string):WebGLUniformLocation|null {
        const program=this.activeProgram!;
        let uniforms=this.uniforms.get(program);if(!uniforms){uniforms=new Map();this.uniforms.set(program,uniforms);}
        if(!uniforms.has(name))uniforms.set(name,this.gl.getUniformLocation(program,name));
        return uniforms.get(name)!;
    }
    private geometry(config:ParticleConfig):[string,GeometryRecord] {
        const type=config.geometryType??'SIMPLE_QUAD';
        const profile=config.shaderProfile??(config.backend==='CPU'?'CPU_DEFAULT':'GPU_STANDARD');
        const key=`${type}:${profile}`;
        let geometry=this.geometries.get(key);
        if(!geometry){
            const source=geometryContract(type,profile),gl=this.gl,vertices=gl.createBuffer()!,indices=gl.createBuffer()!;
            gl.bindVertexArray(null);gl.bindBuffer(gl.ARRAY_BUFFER,vertices);gl.bufferData(gl.ARRAY_BUFFER,source.vertices,gl.STATIC_DRAW);
            gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,indices);gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,source.indices,gl.STATIC_DRAW);
            geometry={vertices,indices,count:source.indices.length,bytes:source.vertices.byteLength+source.indices.byteLength};this.geometries.set(key,geometry);
        }
        return [key,geometry];
    }
    setLayers(layers:RenderLayer[],resourceKeys?:readonly string[]):void {
        this.layers=layers;
        const ids=new Set(layers.map(l=>l.id));
        for(const [id,b] of this.buffers)if(!ids.has(id)){if(!this.contextResourcesLost){this.gl.deleteVertexArray(b.vao);this.gl.deleteBuffer(b.buffer);}this.buffers.delete(id);}
        const textureKeys=new Set(resourceKeys??layers.map(l=>l.textureKey));
        for(const [key,t] of this.textures)if(!textureKeys.has(key)){if(!this.contextResourcesLost)this.gl.deleteTexture(t.texture);this.textures.delete(key);}
    }
    get supportsS3tc():boolean{return !!this.gl.getExtension('WEBGL_compressed_texture_s3tc');}
    uploadTexture(texture:FxTexture):void {
        if(!texture.width||!texture.height||!texture.pixels&&!texture.pixelsBase64)return;
        const expected=texture.width*texture.height*4;
        if(!Number.isSafeInteger(expected)||expected<4||expected>32*1024*1024||texture.pixelsBase64&&texture.pixelsBase64.length>Math.ceil(expected/3)*4)throw new Error('内联贴图超过 32 MiB 传输预算或尺寸不匹配。');
        const pixels=texture.pixelsBase64?decodeBase64(texture.pixelsBase64):new Uint8Array(texture.pixels!);
        this.uploadTransfer({key:texture.key,transferId:'inline',width:texture.width,height:texture.height,format:'RGBA8',levels:[{width:texture.width,height:texture.height,byteLength:pixels.byteLength}]},[pixels]);
    }
    uploadTransfer(descriptor:FxTextureTransfer,levels:Uint8Array[]):void {
        if(this.disposed||this.lost)return;
        if(this.textures.has(descriptor.key))return;
        const gl=this.gl,bytes=levels.reduce((sum,l)=>sum+l.byteLength,0);
        if(!Number.isSafeInteger(bytes)||bytes<1||bytes>GPU_TEXTURE_BUDGET||!levels.length||levels.length!==descriptor.levels.length)throw new Error('贴图层数、像素长度或 GPU 预算无效。');
        const used=[...this.textures.entries()].reduce((sum,[key,t])=>sum+(key===descriptor.key?0:t.bytes),0);
        if(used+bytes>GPU_TEXTURE_BUDGET)throw new Error('预览 GPU 贴图预算已达 128 MiB，请减少活动素材或重载。');
        if(descriptor.width>gl.getParameter(gl.MAX_TEXTURE_SIZE)||descriptor.height>gl.getParameter(gl.MAX_TEXTURE_SIZE))throw new Error('贴图超过本设备支持的尺寸。');
        if(descriptor.levels.length>Math.floor(Math.log2(Math.max(descriptor.width,descriptor.height)))+1)throw new Error('贴图 mip 层数超过完整链长度。');
        const ext=descriptor.format==='RGBA8'?null:gl.getExtension('WEBGL_compressed_texture_s3tc');
        if(descriptor.format!=='RGBA8'&&!ext)throw new Error('本设备不支持 S3TC 压缩纹理，请重载为 RGBA 预览。');
        const handle=gl.createTexture();if(!handle)throw new Error('无法分配 GPU 贴图。');
        try {
            gl.bindTexture(gl.TEXTURE_2D,handle);
            gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,levels.length>1?gl.LINEAR_MIPMAP_LINEAR:gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAX_LEVEL,levels.length-1);
            for(let i=0;i<levels.length;i++){
                const level=descriptor.levels[i],data=levels[i];
                if(level.width!==Math.max(1,descriptor.width>>i)||level.height!==Math.max(1,descriptor.height>>i)||data.byteLength!==level.byteLength)throw new Error('无效 mip 层尺寸。');
                if(descriptor.format==='RGBA8'){
                    if(data.byteLength!==level.width*level.height*4)throw new Error('RGBA 像素长度不匹配。');
                    gl.texImage2D(gl.TEXTURE_2D,i,gl.RGBA8,level.width,level.height,0,gl.RGBA,gl.UNSIGNED_BYTE,data);
                }else{
                    const format=descriptor.format==='DXT1'?ext!.COMPRESSED_RGBA_S3TC_DXT1_EXT:descriptor.format==='DXT3'?ext!.COMPRESSED_RGBA_S3TC_DXT3_EXT:ext!.COMPRESSED_RGBA_S3TC_DXT5_EXT;
                    const expected=Math.ceil(level.width/4)*Math.ceil(level.height/4)*(descriptor.format==='DXT1'?8:16);
                    if(data.byteLength!==expected)throw new Error('S3TC 块长度不匹配。');
                    gl.compressedTexImage2D(gl.TEXTURE_2D,i,format,level.width,level.height,0,data);
                }
            }
            if(gl.getError()!==gl.NO_ERROR)throw new Error('GPU 拒绝上传贴图。');
            const old=this.textures.get(descriptor.key);if(old)gl.deleteTexture(old.texture);
            this.textures.set(descriptor.key,{texture:handle,bytes});
        }catch(error){gl.deleteTexture(handle);throw error;}
    }
    private position(layer:RenderLayer,birth:BirthRecord,frame:number):Vec3 {
        if(layer.config.backend==='CPU')return birth.position;
        const c=layer.config,age=frame-birth.creationFrame,distance=integrateDamping(tableSample(c.velocityDamping,birth.seed,13),age);
        const x=birth.velocity[0]*distance+c.drift[0]*age,y=birth.velocity[1]*distance+c.drift[1]*age;
        const z=birth.position[2]+birth.velocity[2]*distance+c.drift[2]*age+0.5*c.gravity*age*age;
        if(c.shaderProfile==='GPU_PERPENDICULAR_BOTTOM')return [birth.position[0]+x,birth.position[1]+y,z];
        const angle=tableSample(c.angleXY,birth.seed,9,0)+tableSample(c.angularRateXY,birth.seed,9,1)*integrateDamping(tableSample(c.angularDampingXY,birth.seed,9,2),age);
        return [birth.position[0]+x*Math.cos(angle)+y*Math.sin(angle),birth.position[1]-x*Math.sin(angle)+y*Math.cos(angle),z];
    }
    private sample(layer:RenderLayer,birth:BirthRecord,frame:number):ParticleSample {
        const age=frame-birth.creationFrame,c=layer.config;
        const sample=layer.sample?layer.sample(birth,frame):c.backend==='CPU'?sampleCpuParticle(birth):sampleParticle(birth,c,age,layer.alphaSamples);
        if(c.shaderProfile==='GPU_PERPENDICULAR_BOTTOM') {
            const distance=integrateDamping(tableSample(c.velocityDamping,birth.seed,13),age);
            sample.position=birth.position.map((p,i)=>p+birth.velocity[i]*distance+c.drift[i]*age+(i===2?0.5*c.gravity*age*age:0)) as Vec3;
        }
        return sample;
    }
    private buffer(layer:RenderLayer,frame:number,camera:CameraState):BufferRecord {
        const gl=this.gl,[geometryKey,geometry]=this.geometry(layer.config);
        let record=this.buffers.get(layer.id);
        if(!record){
            record={vao:gl.createVertexArray()!,buffer:gl.createBuffer()!,capacity:0,stamp:'',count:0,staging:new Float32Array(0),geometry:''};
            this.buffers.set(layer.id,record);
        }
        if(record.geometry!==geometryKey){
            gl.bindVertexArray(record.vao);gl.bindBuffer(gl.ARRAY_BUFFER,geometry.vertices);
            gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,3,gl.FLOAT,false,20,0);
            gl.enableVertexAttribArray(4);gl.vertexAttribPointer(4,2,gl.FLOAT,false,20,12);
            gl.bindBuffer(gl.ARRAY_BUFFER,record.buffer);
            for(const [index,size,offset] of [[1,4,0],[2,4,16],[3,1,32]]){gl.enableVertexAttribArray(index);gl.vertexAttribPointer(index,size,gl.FLOAT,false,36,offset);gl.vertexAttribDivisor(index,1);}
            gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,geometry.indices);record.geometry=geometryKey;record.stamp='';
        }
        const cpu=layer.config.backend==='CPU';
        const sorted=materialProfile(cpu?'CPU':'GPU',layer.config.shader,layer.config.sortParticles,layer.config.shaderProfile).sortParticles;
        const births=layer.births;
        // IDs delimit immutable GPU birth records. CPU state and transparent ordering depend on time.
        const stamp=`${births.length}:${births[0]?.id}:${births.at(-1)?.id}${cpu||sorted?`:${cpu?Math.floor(frame):frame}`:''}${sorted?`:${camera.yaw}:${camera.pitch}:${camera.distance}:${camera.target.join(',')}`:''}`;
        if(record.stamp!==stamp||record.births!==births||record.config!==layer.config){
            let ordered=births;
            const samples=new Map<BirthRecord,ParticleSample>();
            if(cpu)for(const b of births)samples.set(b,this.sample(layer,b,frame));
            if(sorted){
                const depths=new Map<BirthRecord,number>();
                for(const b of births)depths.set(b,project(cpu?samples.get(b)!.position:this.position(layer,b,frame),this.matrix,1,1)[2]);
                ordered=births.slice().sort((a,b)=>(depths.get(b)!-depths.get(a)!)||a.creationFrame-b.creationFrame||a.id-b.id);
            }
            if(!cpu&&record.order?.length===ordered.length&&ordered.every((birth,i)=>birth===record.order![i])){
                record.stamp=stamp;record.births=births;record.config=layer.config;return record;
            }
            const needed=births.length*36;
            if(needed>record.capacity){
                const capacity=Math.max(36*128,Math.ceil(needed/4096)*4096);
                const total=[...this.buffers.values()].reduce((n,b)=>n+b.capacity,0)-record.capacity+capacity;
                if(total>GPU_INSTANCE_BUDGET)throw new Error('预览 GPU 实例缓冲预算已达 32 MiB。');
                gl.bindBuffer(gl.ARRAY_BUFFER,record.buffer);gl.bufferData(gl.ARRAY_BUFFER,capacity,gl.DYNAMIC_DRAW);record.capacity=capacity;record.staging=new Float32Array(Math.ceil(capacity/4));
            }
            const data=record.staging;
            for(let i=0;i<ordered.length;i++){
                const b=ordered[i],o=i*9;
                if(cpu){
                    const p=samples.get(b)!;data.set(p.position,o);data[o+3]=p.alive?p.size:0;data.set(p.color,o+4);data[o+8]=p.angleZ;
                }else{
                    data.set(b.position,o);data[o+3]=b.life;data.set(b.velocity,o+4);data[o+7]=b.creationFrame;data[o+8]=b.seed;
                }
            }
            gl.bindBuffer(gl.ARRAY_BUFFER,record.buffer);gl.bufferSubData(gl.ARRAY_BUFFER,0,data.subarray(0,births.length*9));
            this.uploads++;this.uploadedBytes+=needed;record.stamp=stamp;record.births=births;record.config=layer.config;record.count=births.length;record.order=cpu?undefined:ordered.slice();
        }
        return record;
    }
    private range(name:string,r:Range):void {this.gl.uniform2f(this.uniform(name),r.low,r.high-r.low);}
    private ranges(name:string,ranges:readonly Range[],spread=false):void {this.gl.uniform3fv(this.uniform(name),ranges.map(r=>spread?r.high-r.low:r.low));}
    private curves(config:ParticleConfig,alphas:readonly number[]):void {
        const alpha=alphas.join(',');let cached=this.curveCache.get(config);
        if(!cached||cached.alpha!==alpha){
            const times=config.colors?.slice(0,4).map(c=>c.time)??[0,0,0,0];
            const keys=Array.from({length:4},(_,i)=>{
                const rgb=config.colors?.[i]?.value??[1,1,1];
                return [...rgb.map(c=>config.colors&&config.shader==='MULTIPLY'?1-c:c),alphas[i]??(config.alphas?config.alphas[i].range.low:config.colors&&config.shader==='MULTIPLY'?0:1)];
            });
            const slopes:number[]=[],intercepts:number[]=[];
            for(let segment=0;segment<3;segment++){
                const dt=times[segment+1]-times[segment];
                for(let c=0;c<4;c++){
                    const slope=Math.abs(dt)<=1e-6?0:(keys[segment+1][c]-keys[segment][c])/dt;
                    slopes.push(slope);intercepts.push(keys[segment][c]-slope*times[segment]);
                }
            }
            cached={alpha,times:new Float32Array(times),slopes:new Float32Array(slopes),intercepts:new Float32Array(intercepts)};this.curveCache.set(config,cached);
        }
        const gl=this.gl;gl.uniform4fv(this.uniform('timeKeys'),cached.times);gl.uniform4fv(this.uniform('colorSlope[0]'),cached.slopes);gl.uniform4fv(this.uniform('colorIntercept[0]'),cached.intercepts);
    }
    private syntheticGround(matrices:CameraMatrices,width:number,height:number,ground:number):void {
        const gl=this.gl;
        if(!this.depth||this.depth.width!==width||this.depth.height!==height){
            if(this.depth){gl.deleteFramebuffer(this.depth.framebuffer);gl.deleteTexture(this.depth.texture);}
            const texture=gl.createTexture()!,framebuffer=gl.createFramebuffer()!;
            gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,texture);
            gl.texImage2D(gl.TEXTURE_2D,0,gl.DEPTH_COMPONENT24,width,height,0,gl.DEPTH_COMPONENT,gl.UNSIGNED_INT,null);
            gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);
            gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
            gl.bindFramebuffer(gl.FRAMEBUFFER,framebuffer);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.DEPTH_ATTACHMENT,gl.TEXTURE_2D,texture,0);
            gl.drawBuffers([gl.NONE]);gl.readBuffer(gl.NONE);
            if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE){
                gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.deleteFramebuffer(framebuffer);gl.deleteTexture(texture);this.depth=null;
                throw new Error('此设备不支持合成平地深度目标。');
            }
            this.depth={framebuffer,texture,width,height};this.depthStamp='';
        }
        this.use(this.groundProgram!);gl.bindVertexArray(null);gl.disable(gl.BLEND);gl.disable(gl.CULL_FACE);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.depthMask(true);gl.colorMask(false,false,false,false);
        gl.uniformMatrix4fv(this.uniform('viewProjection'),false,matrices.matrix);gl.uniform3fv(this.uniform('eye'),matrices.eye);
        gl.uniform3fv(this.uniform('cameraRight'),matrices.right);gl.uniform3fv(this.uniform('cameraUp'),matrices.up);gl.uniform3fv(this.uniform('cameraBackward'),matrices.backward);
        gl.uniform2f(this.uniform('viewport'),width,height);gl.uniform2f(this.uniform('projectionScale'),width/height/CAMERA_FOV_SCALE,1/CAMERA_FOV_SCALE);gl.uniform1f(this.uniform('groundHeight'),ground);
        const stamp=`${width}:${height}:${ground}:${Array.from(matrices.matrix).join(',')}`;
        if(stamp!==this.depthStamp){
            gl.bindFramebuffer(gl.FRAMEBUFFER,this.depth.framebuffer);gl.clearDepth(1);gl.clear(gl.DEPTH_BUFFER_BIT);gl.drawArrays(gl.TRIANGLES,0,3);this.depthStamp=stamp;
        }
        // A separate draw to the default depth target avoids incompatible-depth blits on some drivers.
        gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.drawArrays(gl.TRIANGLES,0,3);gl.colorMask(true,true,true,true);gl.depthMask(false);
    }
    private particleUniforms(config:ParticleConfig,alphas:readonly number[]):void {
        const gl=this.gl,c=config,alpha=alphas.join(','),previous=this.parameterStates.get(this.activeProgram!);
        if(previous?.config===c){
            if(c.backend!=='CPU'&&previous.alpha!==alpha){this.curves(c,alphas);previous.alpha=alpha;}
            return;
        }
        gl.uniform1i(this.uniform('groundAligned'),c.groundAligned?1:0);
        if(c.backend==='CPU'){this.parameterStates.set(this.activeProgram!,{config:c,alpha});return;}
        this.range('velocityDamping',c.velocityDamping);this.range('colorScale',c.colorScale);
        this.ranges('sizeMin',[c.size,c.sizeRate,c.sizeRateDamping]);this.ranges('sizeSpread',[c.size,c.sizeRate,c.sizeRateDamping],true);
        this.ranges('xyMin',[c.angleXY,c.angularRateXY,c.angularDampingXY]);this.ranges('xySpread',[c.angleXY,c.angularRateXY,c.angularDampingXY],true);
        this.ranges('zMin',[c.angleZ,c.angularRateZ,c.angularDamping]);this.ranges('zSpread',[c.angleZ,c.angularRateZ,c.angularDamping],true);
        gl.uniform3f(this.uniform('gravity'),0,0,c.gravity);gl.uniform3fv(this.uniform('drift'),c.drift);
        gl.uniform1i(this.uniform('perpendicularBottom'),c.shaderProfile==='GPU_PERPENDICULAR_BOTTOM'?1:0);
        if(c.shaderProfile==='GPU_PERPENDICULAR_BOTTOM'){
            const g=c.geometryUpdate;if(!g)throw new Error('垂直底部几何缺少明确 GeometryUpdate 配置。');
            for(const [name,value] of [['geometrySize',g.sizeStart],['geometryRate',g.sizeRate],['geometryDamping',g.sizeDamping],['geometryAngle',g.angle],['geometryAngleRate',g.angleRate]] as const){
                this.ranges(`${name}Min`,value);this.ranges(`${name}Spread`,value,true);
            }
            this.range('geometryAngleDamping',g.angleDamping);
            gl.uniform1i(this.uniform('geometryRotation'),g.rotation==='ROTATION_OFF'?0:g.rotation==='ROTATE_AROUND_VELOCITY'?1:2);
        }
        gl.uniform4f(this.uniform('atlas'),c.atlas.framesPerRow,c.atlas.totalFrames,c.atlas.singleRow,c.atlas.randTex);
        gl.uniform1f(this.uniform('atlasSpeed'),c.atlas.speedMultiplier);this.curves(c,alphas);this.parameterStates.set(this.activeProgram!,{config:c,alpha});
    }
    render(frame:number,camera:CameraState,scene:FxScene,background:string,showGround=true,lowResolution=false,softParticles=true):RenderStats {
        if(this.disposed||this.lost)return this.statsValue;
        const gl=this.gl;this.uploads=0;this.uploadedBytes=0;
        const maxWidth=lowResolution?960:1280,maxHeight=lowResolution?540:720;
        const size=this.canvas.getBoundingClientRect();
        const ratio=Math.min(1,maxWidth/Math.max(1,size.width),maxHeight/Math.max(1,size.height));
        const width=Math.max(1,Math.floor(size.width*ratio)),height=Math.max(1,Math.floor(size.height*ratio));
        if(this.canvas.width!==width||this.canvas.height!==height){this.canvas.width=width;this.canvas.height=height;}
        gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,width,height);
        const rgb=/^#[0-9a-f]{6}$/i.test(background)?[1,3,5].map(i=>parseInt(background.slice(i,i+2),16)/255):[0.025,0.03,0.04];
        gl.clearColor(rgb[0],rgb[1],rgb[2],1);gl.colorMask(true,true,true,true);gl.depthMask(true);gl.clearDepth(1);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.depthMask(false);
        const matrices=cameraMatrices(camera,width/height);this.matrix=matrices.matrix;
        if(showGround)this.syntheticGround(matrices,width,height,scene.ground);
        this.drawScene(scene,showGround);
        let particles=0,draws=0;
        const initializedPrograms=new Set<WebGLProgram>();
        const factors={ONE:gl.ONE,ZERO:gl.ZERO,SRC_ALPHA:gl.SRC_ALPHA,ONE_MINUS_SRC_ALPHA:gl.ONE_MINUS_SRC_ALPHA,ONE_MINUS_SRC_COLOR:gl.ONE_MINUS_SRC_COLOR,DST_COLOR:gl.DST_COLOR};
        const layers=this.layers.slice().sort((a,b)=>a.config.sortLevel-b.config.sortLevel);
        for(const layer of layers){
            if(!layer.visible||!layer.births.length)continue;
            const c=layer.config,cpu=c.backend==='CPU',buffer=this.buffer(layer,frame,camera);
            const profile=materialProfile(cpu?'CPU':'GPU',c.shader,c.sortParticles,c.shaderProfile);
            this.use(cpu?this.cpuProgram!:this.program!);
            gl.enable(gl.BLEND);gl.disable(gl.CULL_FACE);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.depthMask(false);gl.blendEquation(gl.FUNC_ADD);
            gl.blendFunc(factors[profile.source],factors[profile.destination]);
            if(!initializedPrograms.has(this.activeProgram!)){
                gl.uniformMatrix4fv(this.uniform('viewProjection'),false,matrices.matrix);
                gl.uniform3fv(this.uniform('cameraRight'),matrices.right);gl.uniform3fv(this.uniform('cameraUp'),matrices.up);
                gl.uniform1f(this.uniform('currentFrame'),frame);gl.uniform2f(this.uniform('viewport'),width,height);initializedPrograms.add(this.activeProgram!);
            }
            gl.uniform1f(this.uniform('alphaTestReference'),profile.alphaTest.enabled?profile.alphaTest.reference:-1);
            gl.uniform1i(this.uniform('colorOrder'),profile.colorOrder==='TEXTURE_THEN_COLOR_THEN_LINEAR'?0:profile.colorOrder==='TEXTURE_THEN_LINEAR_THEN_COLOR'?1:2);
            const soft=showGround&&softParticles&&c.softParticles!==false&&!cpu&&(!c.groundAligned||c.shaderProfile==='GPU_PERPENDICULAR_BOTTOM');
            gl.uniform1i(this.uniform('softTarget'),soft?(profile.softTarget==='ALPHA'?1:profile.softTarget==='RGB'?2:0):0);
            gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,showGround?this.depth!.texture:null);
            this.particleUniforms(c,layer.alphaSamples);
            const texture=this.textures.get(layer.textureKey??'');
            gl.uniform1i(this.uniform('missingTexture'),texture?0:1);gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,texture?.texture??null);
            gl.bindVertexArray(buffer.vao);gl.drawElementsInstanced(gl.TRIANGLES,this.geometries.get(buffer.geometry)!.count,gl.UNSIGNED_SHORT,0,buffer.count);
            particles+=buffer.count;draws++;
        }
        gl.bindVertexArray(null);
        this.statsValue={particles,draws,gpuBytes:[...this.buffers.values()].reduce((n,b)=>n+b.capacity,0)+[...this.textures.values()].reduce((n,t)=>n+t.bytes,0)+[...this.geometries.values()].reduce((n,g)=>n+g.bytes,0)+this.sceneLineCount*7*4+width*height*8+(this.depth?this.depth.width*this.depth.height*4:0),canvasPixels:width*height,instanceUploads:this.uploads,uploadedBytes:this.uploadedBytes,softParticles:showGround&&softParticles};
        return this.statsValue;
    }
    private drawScene(scene:FxScene,ground:boolean):void {
        const gl=this.gl,stamp=`${ground}:${scene.ground}:${scene.source.join(',')}:${scene.target.join(',')}`;
        this.use(this.lineProgram!);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.disable(gl.CULL_FACE);gl.enable(gl.BLEND);gl.blendEquation(gl.FUNC_ADD);gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);gl.depthMask(false);
        gl.uniformMatrix4fv(this.uniform('viewProjection'),false,this.matrix);gl.bindVertexArray(this.lineVao);
        if(stamp!==this.sceneStamp){
            const vertices:number[]=[],line=(a:Vec3,b:Vec3,color:number[])=>vertices.push(...a,...color,...b,...color);
            if(ground)for(let i=-10;i<=10;i++){
                const c=[0.17,0.19,0.22,i===0?0.8:0.5];
                line([i*10,-100,scene.ground],[i*10,100,scene.ground],c);line([-100,i*10,scene.ground],[100,i*10,scene.ground],c);
            }
            for(const [point,color] of [[scene.source,[0.2,0.85,0.7,1]],[scene.target,[1,0.6,0.2,1]]] as [Vec3,number[]][]){
                for(let axis=0;axis<3;axis++){const a=[...point] as Vec3,b=[...point] as Vec3;a[axis]-=3;b[axis]+=3;line(a,b,color);}
            }
            line(scene.source,scene.target,[0.45,0.45,0.5,0.4]);
            gl.bindBuffer(gl.ARRAY_BUFFER,this.lineBuffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(vertices),gl.DYNAMIC_DRAW);this.sceneStamp=stamp;this.sceneLineCount=vertices.length/7;
        }
        gl.drawArrays(gl.LINES,0,this.sceneLineCount);
    }
    pick(clientX:number,clientY:number,frame:number):string|undefined {
        const rect=this.canvas.getBoundingClientRect(),x=clientX-rect.left,y=clientY-rect.top;
        let chosen:string|undefined,best=Infinity;
        for(const layer of this.layers)if(layer.visible)for(const birth of layer.births){
            const p=this.sample(layer,birth,frame),g=layer.config.geometryUpdate;
            if(g&&layer.config.shaderProfile==='GPU_PERPENDICULAR_BOTTOM'){
                const age=frame-birth.creationFrame;
                const sizes=g.sizeStart.map((r,i)=>tableSample(r,birth.seed,3,i)+tableSample(g.sizeRate[i],birth.seed,3,i)*integrateDamping(tableSample(g.sizeDamping[i],birth.seed,3,i),age)) as Vec3;
                const damp=integrateDamping(tableSample(g.angleDamping,birth.seed,3),age);
                const angles=g.angle.map((r,i)=>tableSample(r,birth.seed,3,i)+tableSample(g.angleRate[i],birth.seed,3,i)*damp) as Vec3;
                const displacement=p.position.map((v,i)=>v-birth.position[i]) as Vec3;
                const center=geometryCornerPosition([0.5,0,0],sizes,angles,g.rotation,displacement,birth.velocity);
                p.position=p.position.map((v,i)=>v+center[i]) as Vec3;p.size=Math.max(...sizes.map(Math.abs));
            }
            const screen=project(p.position,this.matrix,rect.width,rect.height),distance=Math.hypot(screen[0]-x,screen[1]-y);
            if(p.alive&&screen[2]>-1&&screen[2]<1&&distance<Math.max(7,Math.abs(p.size)*2)&&distance<best){best=distance;chosen=layer.id;}
        }
        return chosen;
    }
    get stats():RenderStats{return {...this.statsValue};}
    private releaseGraphicsResources():void {
        const gl=this.gl;
        // Context loss already destroys its objects. Deleting those objects after restore is invalid.
        if(!this.contextResourcesLost){
            for(const b of this.buffers.values()){gl.deleteVertexArray(b.vao);gl.deleteBuffer(b.buffer);}
            for(const t of this.textures.values())gl.deleteTexture(t.texture);
            for(const geometry of this.geometries.values()){gl.deleteBuffer(geometry.vertices);gl.deleteBuffer(geometry.indices);}
            if(this.depth){gl.deleteFramebuffer(this.depth.framebuffer);gl.deleteTexture(this.depth.texture);}
            gl.deleteBuffer(this.lineBuffer);gl.deleteVertexArray(this.lineVao);
            gl.deleteProgram(this.program);gl.deleteProgram(this.cpuProgram);gl.deleteProgram(this.lineProgram);gl.deleteProgram(this.groundProgram);
        }
        this.buffers.clear();this.textures.clear();this.geometries.clear();this.uniforms.clear();this.parameterStates.clear();this.depth=null;this.depthStamp='';this.sceneStamp='';
        this.lineBuffer=null;this.lineVao=null;this.program=null;this.cpuProgram=null;this.lineProgram=null;this.groundProgram=null;this.activeProgram=null;
    }
    dispose():void {
        if(this.disposed)return;this.disposed=true;this.releaseGraphicsResources();
        this.canvas.removeEventListener('webglcontextlost',this.contextLost);this.canvas.removeEventListener('webglcontextrestored',this.contextRestored);
    }
}

export function decodeBase64(value:string):Uint8Array {
    const binary=atob(value),bytes=new Uint8Array(binary.length);for(let i=0;i<binary.length;i++)bytes[i]=binary.charCodeAt(i);return bytes;
}
