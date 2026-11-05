import type { FxRandom } from './random';
import type { FxNode } from '../../../src/fx/previewProtocol';
export type { FxNode };
export type Vec2 = [number, number];
export type Vec3 = [number, number, number];
export type Vec4 = [number, number, number, number];
export interface Range { low: number; high: number; type: 'CONSTANT' | 'UNIFORM'; }
export interface RuntimeIssue { code: string; path: string; message: string; severity: 'error' | 'warning'; }
export interface ColorKey { time: number; value: Vec3; }
export interface AlphaKey { time: number; range: Range; }
export interface AtlasConfig { framesPerRow: number; totalFrames: number; singleRow: number; randTex: number; speedMultiplier: number; }
export interface VolumeConfig { type: 'Point' | 'Line' | 'Box' | 'Cylinder' | 'Sphere'; hollow: boolean; start: Vec3; end: Vec3; halfSize: Vec3; radius: number; radiusRate: number; length: number; offset: Vec3; }
export interface VelocityConfig { type: 'Ortho' | 'Cylindrical' | 'Spherical' | 'Outward'; x: Range; y: Range; z: Range; radial: Range; normal: Range; speed: Range; otherSpeed: Range; }
export interface ParticleConfig {
    id: string; texture: string; shader: 'ADDITIVE' | 'ALPHA' | 'MULTIPLY';
    lifetime: Range; initialDelay: Range; burstDelay: Range; burstCount: Range;
    size: Range; startSizeRate: Range; systemLifetime: number; isOneShot: boolean;
    velocityDamping: Range; gravity: number; drift: Vec3;
    sizeRate: Range; sizeRateDamping: Range;
    angleZ: Range; angularRateZ: Range; angularDamping: Range;
    angleXY: Range; angularRateXY: Range; angularDampingXY: Range;
    colors?: ColorKey[]; alphas?: AlphaKey[]; colorScale: Range;
    atlas: AtlasConfig; volume: VolumeConfig; velocity: VelocityConfig;
    groundAligned: boolean; sortParticles: boolean; sortLevel: number;
}
export interface BirthRecord {
    id: number; position: Vec3; velocity: Vec3; life: number;
    creationFrame: number; seed: number;
}
export interface ParticleSample {
    alive: boolean; position: Vec3; size: number; angleZ: number; color: Vec4;
    atlas: { current: Vec2; next: Vec2; blend: number; scale: number };
}
export interface RuntimeOptions {
    maxParticles?: number; maxFrames?: number; snapshotBytes?: number;
    offset?: Vec3; rotation?: number; rotationXYZ?: Vec3; random?: FxRandom; initialDelayOverrideMs?: Range; startFrame?: number; systemLifeFrames?: number;
}
