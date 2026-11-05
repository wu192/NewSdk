import type { Vec3 } from '../runtime/types';
export interface CameraState { yaw: number; pitch: number; distance: number; target: Vec3; }
export const defaultCamera: CameraState = {yaw: -Math.PI/4, pitch: Math.PI/5, distance: 160, target: [0,0,10]};
export interface CameraMatrices { matrix: Float32Array; right: Vec3; up: Vec3; eye: Vec3; backward: Vec3; }
export const CAMERA_NEAR=0.1;
export const CAMERA_FAR=100000;
export const CAMERA_FOV_SCALE=1/Math.tan(Math.PI/8);
const dot=(a:Vec3,b:Vec3)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
const cross=(a:Vec3,b:Vec3):Vec3=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const normal=(v:Vec3):Vec3=>{const l=Math.hypot(...v)||1;return [v[0]/l,v[1]/l,v[2]/l];};
export function cameraMatrices(c:CameraState,aspect:number):CameraMatrices {
    const offset:Vec3=[Math.cos(c.yaw)*Math.cos(c.pitch)*c.distance,Math.sin(c.yaw)*Math.cos(c.pitch)*c.distance,Math.sin(c.pitch)*c.distance];
    const eye=c.target.map((v,i)=>v+offset[i]) as Vec3;
    const forward=normal(offset),right=normal(cross([0,0,1],forward)),up=cross(forward,right);
    const view=new Float32Array([right[0],up[0],forward[0],0,right[1],up[1],forward[1],0,right[2],up[2],forward[2],0,-dot(right,eye),-dot(up,eye),-dot(forward,eye),1]);
    const f=CAMERA_FOV_SCALE,near=CAMERA_NEAR,far=CAMERA_FAR;
    const p=new Float32Array([f/aspect,0,0,0,0,f,0,0,0,0,(far+near)/(near-far),-1,0,0,2*far*near/(near-far),0]);
    const matrix=new Float32Array(16);
    for(let col=0;col<4;col++)for(let row=0;row<4;row++)for(let k=0;k<4;k++)matrix[col*4+row]+=p[k*4+row]*view[col*4+k];
    return {matrix,right,up,eye,backward:forward};
}
export function project(position:Vec3,matrix:Float32Array,width:number,height:number):[number,number,number] {
    const p=[...position,1],out=[0,0,0,0];
    for(let r=0;r<4;r++)for(let k=0;k<4;k++)out[r]+=matrix[k*4+r]*p[k];
    return [(out[0]/out[3]+1)*width/2,(1-out[1]/out[3])*height/2,out[2]/out[3]];
}
