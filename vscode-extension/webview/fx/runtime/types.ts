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
export interface VolumeConfig { type: 'Point' | 'Line' | 'Box' | 'Cylinder' | 'Sphere' | 'Lightning'; hollow: boolean; start: Vec3; end: Vec3; halfSize: Vec3; radius: number; radiusRate: number; length: number; offset: Vec3; }
export interface VelocityConfig { type: 'Ortho' | 'Cylindrical' | 'Spherical' | 'Outward'; x: Range; y: Range; z: Range; radial: Range; normal: Range; speed: Range; otherSpeed: Range; }
export interface TrailConfig { life: number; uTile: number; vTile: number; uScrollRate: number; vScrollRate: number; }
export interface LightningRandomSet {
    startAmplitude: Range; endAmplitudeChange: Range; startFrequency: Range; endFrequencyChange: Range; startPhase: Range; endPhaseChange: Range;
}
export interface LightningConfig { start: Vec3; end: Vec3; randomSets: LightningRandomSet[]; }
export interface LightningRandomState {
    startAmplitude: number; endAmplitudeChange: number; startFrequency: number; endFrequencyChange: number; startPhase: number; endPhaseChange: number;
}
export interface LightningState { start: Vec3; end: Vec3; randomSets: LightningRandomState[]; }
export interface SwarmConfig { opaqueSpeed: number; transparentSpeed: number; speedStretchAmount: number; attractStrength: number; environmentTexture: string; }
export interface TrailPoint { frame: number; position: Vec3; size: number; color: Vec4; }
export interface ParticleConfig {
    backend?: 'GPU' | 'CPU';
    family?: 'PARTICLE' | 'GPU_PARTICLE' | 'TRAIL' | 'STREAK' | 'SWARM';
    drawModule?: 'Default' | 'Gpu' | 'Trail' | 'Lightning' | 'Swarm';
    trail?: TrailConfig; lightning?: LightningConfig; swarm?: SwarmConfig;
    swarmPhysics?: { attractStrength: number };
    alphaTest?: boolean;
    geometryType?: 'SIMPLE_QUAD' | 'CENTERED_QUAD' | 'TWO_CONCENTRIC_QUADS' | 'TWO_QUADS';
    shaderProfile?: 'GPU_STANDARD' | 'GPU_PERPENDICULAR_BOTTOM' | 'CPU_DEFAULT';
    geometryUpdate?: GeometryUpdate;
    softParticles?: boolean;
    attachedSystem?: string;
    events?: ParticleEventConfig[];
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
    cpu?: CpuParticleState;
    trail?: TrailPoint[];
    lightning?: LightningState;
    events?: ParticleEventState[];
}
export interface GeometryUpdate {
    sizeStart: [Range, Range, Range]; sizeRate: [Range, Range, Range]; sizeDamping: [Range, Range, Range];
    angle: [Range, Range, Range]; angleRate: [Range, Range, Range]; angleDamping: Range;
    rotation: 'ROTATION_OFF' | 'ROTATE_AROUND_VELOCITY' | 'ROTATE_IN_WORLD_SPACE';
}
export type ParticleEventConfig = { type: 'Life'; fx: string; perParticle: boolean; kill: boolean; eventTime: Range }
    | { type: 'Collision'; fx: string; perParticle: boolean; kill: boolean; heightOffset: Range; orientToTerrain: boolean };
export interface ParticleEventState { index: number; armed: boolean; value: number; }
export interface CpuCurveState { index: number; values: number[][]; times: number[]; current: number[]; delta: number[]; }
export interface CpuParticleState {
    remaining: number; size: number; sizeRate: number; sizeDamping: number;
    angleZ: number; angleRateZ: number; angleDampingZ: number;
    angleXY: number; angleRateXY: number; angleDampingXY: number;
    velocityDamping: number; color?: CpuCurveState; alpha?: CpuCurveState;
}
export interface RuntimeEventTrigger { fx: string; position: Vec3; rotationXYZ: Vec3; particleId?: number; }
export interface ParticleSample {
    alive: boolean; position: Vec3; size: number; angleZ: number; color: Vec4;
    atlas: { current: Vec2; next: Vec2; blend: number; scale: number };
}
export interface RuntimeOptions {
    maxParticles?: number; maxFrames?: number; snapshotBytes?: number; maxParticleBytes?: number;
    offset?: Vec3; rotation?: number; rotationXYZ?: Vec3; random?: FxRandom; initialDelayOverrideMs?: Range; startFrame?: number; creationFrame?: number; systemLifeFrames?: number;
    /** Complete local-to-world rotation for composed event/attached transforms. */
    rotationMatrix?: readonly number[];
    groundHeight?: number; createAtGroundHeight?: boolean;
}
