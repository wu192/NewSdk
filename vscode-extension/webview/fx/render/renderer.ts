import type { FxScene, FxTexture, FxTextureTransfer } from '../../../src/fx/previewProtocol';
import { RANDOM_TABLE, sampleParticle } from '../runtime';
import type { BirthRecord, ParticleConfig, Range, Vec3 } from '../runtime/types';
import { cameraMatrices, CameraState, project } from './camera';
import { fragmentShader, lineFragmentShader, lineVertexShader, vertexShader } from './shaders';

export interface RenderLayer {
    id: string; config: ParticleConfig; births: readonly BirthRecord[]; alphaSamples: readonly number[];
    textureKey?: string; visible: boolean;
}
interface BufferRecord { vao:WebGLVertexArrayObject; buffer:WebGLBuffer; capacity:number; stamp:string; count:number; births?:readonly BirthRecord[]; staging:Float32Array; }
interface TextureRecord { texture:WebGLTexture; bytes:number; }
export interface RenderStats { particles:number; draws:number; gpuBytes:number; canvasPixels:number; }
const GPU_TEXTURE_BUDGET=128*1024*1024;
const GPU_INSTANCE_BUDGET=32*1024*1024;

export class FxRenderer {
    private gl:WebGL2RenderingContext;
    private program!:WebGLProgram;
    private lineProgram!:WebGLProgram;
    private corners!:WebGLBuffer;
    private indices!:WebGLBuffer;
    private lineBuffer!:WebGLBuffer;
    private lineVao!:WebGLVertexArrayObject;
    private buffers=new Map<string,BufferRecord>();
    private textures=new Map<string,TextureRecord>();
    private uniforms=new Map<string,WebGLUniformLocation|null>();
    private matrix:Float32Array=new Float32Array(16);
    private lost=false;
    private disposed=false;
    private restoreAttempted=false;
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
    private contextLost=(event:Event)=>{event.preventDefault();this.lost=true;this.status('GPU 上下文丢失，等待恢复；文档和编辑状态已保留。');};
    private contextRestored=()=>{
        if(this.disposed||this.restoreAttempted)return;
        this.restoreAttempted=true;
        try {this.buffers.clear();this.textures.clear();this.uniforms.clear();this.initialize();this.lost=false;this.status('GPU 上下文已重建，请重载资源恢复贴图。');}
        catch(error){this.status(`GPU 重建失败：${String(error)}。请重载预览。`);}
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
        this.program=this.link(vertexShader,fragmentShader);this.lineProgram=this.link(lineVertexShader,lineFragmentShader);
        this.corners=gl.createBuffer()!;this.indices=gl.createBuffer()!;this.lineBuffer=gl.createBuffer()!;this.lineVao=gl.createVertexArray()!;
        gl.bindBuffer(gl.ARRAY_BUFFER,this.corners);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-0.5,-0.5,0.5,-0.5,0.5,0.5,-0.5,0.5]),gl.STATIC_DRAW);
        gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,this.indices);gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,new Uint16Array([0,1,2,0,2,3]),gl.STATIC_DRAW);
        gl.bindVertexArray(this.lineVao);gl.bindBuffer(gl.ARRAY_BUFFER,this.lineBuffer);
        gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,3,gl.FLOAT,false,28,0);
        gl.enableVertexAttribArray(1);gl.vertexAttribPointer(1,4,gl.FLOAT,false,28,12);
        gl.bindVertexArray(null);
        gl.disable(gl.CULL_FACE);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.depthMask(false);
        gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL,false);gl.pixelStorei(gl.UNPACK_COLORSPACE_CONVERSION_WEBGL,gl.NONE);
    }
    private uniform(name:string):WebGLUniformLocation|null {
        if(!this.uniforms.has(name))this.uniforms.set(name,this.gl.getUniformLocation(this.program,name));
        return this.uniforms.get(name)!;
    }
    setLayers(layers:RenderLayer[]):void {
        this.layers=layers;
        const ids=new Set(layers.map(l=>l.id));
        for(const [id,b] of this.buffers)if(!ids.has(id)){this.gl.deleteVertexArray(b.vao);this.gl.deleteBuffer(b.buffer);this.buffers.delete(id);}
        const textureKeys=new Set(layers.map(l=>l.textureKey));
        for(const [key,t] of this.textures)if(!textureKeys.has(key)){this.gl.deleteTexture(t.texture);this.textures.delete(key);}
    }
    get supportsS3tc():boolean{return !!this.gl.getExtension('WEBGL_compressed_texture_s3tc');}
    uploadTexture(texture:FxTexture):void {
        if(!texture.width||!texture.height||!texture.pixels&&!texture.pixelsBase64)return;
        const pixels=texture.pixelsBase64?decodeBase64(texture.pixelsBase64):new Uint8Array(texture.pixels!);
        this.uploadTransfer({key:texture.key,transferId:'inline',width:texture.width,height:texture.height,format:'RGBA8',levels:[{width:texture.width,height:texture.height,byteLength:pixels.byteLength}]},[pixels]);
    }
    uploadTransfer(descriptor:FxTextureTransfer,levels:Uint8Array[]):void {
        if(this.disposed||this.lost)return;
        const gl=this.gl,bytes=levels.reduce((sum,l)=>sum+l.byteLength,0);
        if(!Number.isSafeInteger(bytes)||bytes<1||bytes>GPU_TEXTURE_BUDGET||!levels.length||levels.length!==descriptor.levels.length)throw new Error('贴图层数、像素长度或 GPU 预算无效。');
        const used=[...this.textures.entries()].reduce((sum,[key,t])=>sum+(key===descriptor.key?0:t.bytes),0);
        if(used+bytes>GPU_TEXTURE_BUDGET)throw new Error('预览 GPU 贴图预算已达 128 MiB，请减少活动素材或重载。');
        if(descriptor.width>gl.getParameter(gl.MAX_TEXTURE_SIZE)||descriptor.height>gl.getParameter(gl.MAX_TEXTURE_SIZE))throw new Error('贴图超过本设备支持的尺寸。');
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
    private buffer(layer:RenderLayer,frame:number,camera:CameraState):BufferRecord {
        const gl=this.gl;
        let record=this.buffers.get(layer.id);
        if(!record){
            record={vao:gl.createVertexArray()!,buffer:gl.createBuffer()!,capacity:0,stamp:'',count:0,staging:new Float32Array(0)};
            gl.bindVertexArray(record.vao);
            gl.bindBuffer(gl.ARRAY_BUFFER,this.corners);gl.enableVertexAttribArray(0);gl.vertexAttribPointer(0,2,gl.FLOAT,false,8,0);
            gl.bindBuffer(gl.ARRAY_BUFFER,record.buffer);
            for(const [index,size,offset] of [[1,4,0],[2,4,16],[3,1,32]]){gl.enableVertexAttribArray(index);gl.vertexAttribPointer(index,size,gl.FLOAT,false,36,offset);gl.vertexAttribDivisor(index,1);}
            gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,this.indices);this.buffers.set(layer.id,record);
        }
        const sorted=layer.config.sortParticles||layer.config.shader==='ALPHA';
        const births=layer.births;
        const stamp=`${births.length}:${births[0]?.id}:${births.at(-1)?.id}${sorted?`:${frame}:${camera.yaw}:${camera.pitch}`:''}`;
        if(record.stamp!==stamp||record.births!==births){
            let ordered=sorted?[...births]:births;
            if(sorted){
                const depths=new Map<number,number>();
                for(const b of births){const p=sampleParticle(b,layer.config,frame-b.creationFrame,layer.alphaSamples).position;depths.set(b.id,project(p,this.matrix,1,1)[2]);}
                ordered=ordered.slice().sort((a,b)=>(depths.get(b.id)!-depths.get(a.id)!)||a.id-b.id);
            }
            const needed=births.length*36;
            if(needed>record.capacity){
                const capacity=Math.max(36*128,Math.ceil(needed/4096)*4096);
                const total=[...this.buffers.values()].reduce((n,b)=>n+b.capacity,0)-record.capacity+capacity;
                if(total>GPU_INSTANCE_BUDGET)throw new Error('预览 GPU 实例缓冲预算已达 32 MiB。');
                gl.bindBuffer(gl.ARRAY_BUFFER,record.buffer);gl.bufferData(gl.ARRAY_BUFFER,capacity,gl.DYNAMIC_DRAW);record.capacity=capacity;record.staging=new Float32Array(Math.ceil(capacity/4));
            }
            const data=record.staging;
            for(let i=0;i<ordered.length;i++){const b=ordered[i],o=i*9;data[o]=b.position[0];data[o+1]=b.position[1];data[o+2]=b.position[2];data[o+3]=b.life;data[o+4]=b.velocity[0];data[o+5]=b.velocity[1];data[o+6]=b.velocity[2];data[o+7]=b.creationFrame;data[o+8]=b.seed;}
            gl.bindBuffer(gl.ARRAY_BUFFER,record.buffer);gl.bufferSubData(gl.ARRAY_BUFFER,0,data.subarray(0,births.length*9));record.stamp=stamp;record.births=births;record.count=births.length;
        }
        return record;
    }
    private range(name:string,r:Range):void {this.gl.uniform2f(this.uniform(name),r.low,r.high-r.low);}
    private ranges(name:string,ranges:Range[],spread=false):void {this.gl.uniform3fv(this.uniform(name),ranges.map(r=>spread?r.high-r.low:r.low));}
    private curves(config:ParticleConfig,alphas:readonly number[]):void {
        const gl=this.gl,times=config.colors?.slice(0,4).map(c=>c.time)??[0,0,0,0];
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
        gl.uniform4fv(this.uniform('timeKeys'),times);gl.uniform4fv(this.uniform('colorSlope[0]'),slopes);gl.uniform4fv(this.uniform('colorIntercept[0]'),intercepts);
    }
    render(frame:number,camera:CameraState,scene:FxScene,background:string,showGround=true,lowResolution=false):RenderStats {
        if(this.disposed||this.lost)return this.statsValue;
        const gl=this.gl;
        const maxWidth=lowResolution?960:1280,maxHeight=lowResolution?540:720;
        const size=this.canvas.getBoundingClientRect();
        const ratio=Math.min(1,maxWidth/Math.max(1,size.width),maxHeight/Math.max(1,size.height));
        const width=Math.max(1,Math.floor(size.width*ratio)),height=Math.max(1,Math.floor(size.height*ratio));
        if(this.canvas.width!==width||this.canvas.height!==height){this.canvas.width=width;this.canvas.height=height;}
        gl.viewport(0,0,width,height);
        const rgb=/^#[0-9a-f]{6}$/i.test(background)?[1,3,5].map(i=>parseInt(background.slice(i,i+2),16)/255):[0.025,0.03,0.04];
        gl.clearColor(rgb[0],rgb[1],rgb[2],1);gl.depthMask(true);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.depthMask(false);
        const matrices=cameraMatrices(camera,width/height);this.matrix=matrices.matrix;
        this.drawScene(scene,showGround);
        gl.useProgram(this.program);gl.enable(gl.BLEND);gl.disable(gl.CULL_FACE);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);gl.depthMask(false);
        gl.uniformMatrix4fv(this.uniform('viewProjection'),false,matrices.matrix);
        gl.uniform3fv(this.uniform('cameraRight'),matrices.right);gl.uniform3fv(this.uniform('cameraUp'),matrices.up);
        gl.uniform1f(this.uniform('currentFrame'),frame);
        gl.uniform4fv(this.uniform('randomTable[0]'),RANDOM_TABLE.flat());
        let particles=0,draws=0;
        const layers=this.layers.slice().sort((a,b)=>a.config.sortLevel-b.config.sortLevel);
        for(const layer of layers){
            if(!layer.visible||!layer.births.length)continue;
            const c=layer.config,buffer=this.buffer(layer,frame,camera);
            this.range('velocityDamping',c.velocityDamping);this.range('colorScale',c.colorScale);
            this.ranges('sizeMin',[c.size,c.sizeRate,c.sizeRateDamping]);this.ranges('sizeSpread',[c.size,c.sizeRate,c.sizeRateDamping],true);
            this.ranges('xyMin',[c.angleXY,c.angularRateXY,c.angularDampingXY]);this.ranges('xySpread',[c.angleXY,c.angularRateXY,c.angularDampingXY],true);
            this.ranges('zMin',[c.angleZ,c.angularRateZ,c.angularDamping]);this.ranges('zSpread',[c.angleZ,c.angularRateZ,c.angularDamping],true);
            gl.uniform3f(this.uniform('gravity'),0,0,c.gravity);gl.uniform3fv(this.uniform('drift'),c.drift);
            gl.uniform1i(this.uniform('groundAligned'),c.groundAligned?1:0);
            gl.uniform4f(this.uniform('atlas'),c.atlas.framesPerRow,c.atlas.totalFrames,c.atlas.singleRow,c.atlas.randTex);
            gl.uniform1f(this.uniform('atlasSpeed'),c.atlas.speedMultiplier);this.curves(c,layer.alphaSamples);
            const texture=this.textures.get(layer.textureKey??'');
            gl.uniform1i(this.uniform('missingTexture'),texture?0:1);gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,texture?.texture??null);gl.uniform1i(this.uniform('particleTexture'),0);
            gl.blendEquation(gl.FUNC_ADD);
            if(c.shader==='ADDITIVE')gl.blendFunc(gl.ONE,gl.ONE);
            else if(c.shader==='ALPHA')gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);
            else gl.blendFunc(gl.ZERO,gl.ONE_MINUS_SRC_COLOR);
            gl.bindVertexArray(buffer.vao);gl.drawElementsInstanced(gl.TRIANGLES,6,gl.UNSIGNED_SHORT,0,buffer.count);
            particles+=buffer.count;draws++;
        }
        gl.bindVertexArray(null);
        this.statsValue={particles,draws,gpuBytes:[...this.buffers.values()].reduce((n,b)=>n+b.capacity,0)+[...this.textures.values()].reduce((n,t)=>n+t.bytes,0)+width*height*8,canvasPixels:width*height};
        return this.statsValue;
    }
    private drawScene(scene:FxScene,ground:boolean):void {
        const gl=this.gl,vertices:number[]=[];
        const line=(a:Vec3,b:Vec3,color:number[])=>vertices.push(...a,...color,...b,...color);
        if(ground)for(let i=-10;i<=10;i++){
            const c=[0.17,0.19,0.22,i===0?0.8:0.5];
            line([i*10,-100,scene.ground],[i*10,100,scene.ground],c);line([-100,i*10,scene.ground],[100,i*10,scene.ground],c);
        }
        for(const [point,color] of [[scene.source,[0.2,0.85,0.7,1]],[scene.target,[1,0.6,0.2,1]]] as [Vec3,number[]][]){
            for(let axis=0;axis<3;axis++){const a=[...point] as Vec3,b=[...point] as Vec3;a[axis]-=3;b[axis]+=3;line(a,b,color);}
        }
        line(scene.source,scene.target,[0.45,0.45,0.5,0.4]);
        gl.useProgram(this.lineProgram);gl.enable(gl.BLEND);gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);gl.depthMask(false);
        gl.uniformMatrix4fv(gl.getUniformLocation(this.lineProgram,'viewProjection'),false,this.matrix);
        gl.bindVertexArray(this.lineVao);gl.bindBuffer(gl.ARRAY_BUFFER,this.lineBuffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(vertices),gl.DYNAMIC_DRAW);gl.drawArrays(gl.LINES,0,vertices.length/7);
    }
    pick(clientX:number,clientY:number,frame:number):string|undefined {
        const rect=this.canvas.getBoundingClientRect(),x=clientX-rect.left,y=clientY-rect.top;
        let chosen:string|undefined,best=Infinity;
        for(const layer of this.layers)if(layer.visible)for(const birth of layer.births){
            const p=sampleParticle(birth,layer.config,frame-birth.creationFrame,layer.alphaSamples);
            const screen=project(p.position,this.matrix,rect.width,rect.height),distance=Math.hypot(screen[0]-x,screen[1]-y);
            if(p.alive&&screen[2]>-1&&screen[2]<1&&distance<Math.max(7,Math.abs(p.size)*2)&&distance<best){best=distance;chosen=layer.id;}
        }
        return chosen;
    }
    get stats():RenderStats{return {...this.statsValue};}
    dispose():void {
        if(this.disposed)return;this.disposed=true;
        const gl=this.gl;
        for(const b of this.buffers.values()){gl.deleteVertexArray(b.vao);gl.deleteBuffer(b.buffer);}this.buffers.clear();
        for(const t of this.textures.values())gl.deleteTexture(t.texture);this.textures.clear();
        gl.deleteBuffer(this.corners);gl.deleteBuffer(this.indices);gl.deleteBuffer(this.lineBuffer);gl.deleteVertexArray(this.lineVao);
        gl.deleteProgram(this.program);gl.deleteProgram(this.lineProgram);
        this.canvas.removeEventListener('webglcontextlost',this.contextLost);this.canvas.removeEventListener('webglcontextrestored',this.contextRestored);
    }
}

export function decodeBase64(value:string):Uint8Array {
    const binary=atob(value),bytes=new Uint8Array(binary.length);for(let i=0;i<binary.length;i++)bytes[i]=binary.charCodeAt(i);return bytes;
}
