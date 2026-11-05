import type { ParticleConfig } from '../../webview/fx/runtime';

export interface FxNode { name: string; attributes: Record<string, string>; children: FxNode[]; }
export interface SourceLocation { uri: string; nodePath: string; start: number; end: number; }
export interface FxIssue extends SourceLocation { code: string; message: string; severity: 'error' | 'warning' | 'information'; }
export interface FxField extends SourceLocation {
    key: string; label: string; value: string; attribute: string;
    defaulted?: boolean; readonly?: boolean; inherited?: boolean; sourceAsset?: string;
    previewPath?: number[]; group?: string; kind?: 'number' | 'boolean' | 'text' | 'enum'; choices?: string[];
}
export interface FxTexture {
    key: string; name: string; file?: string; source: 'source' | 'compiled';
    status: string; location?: SourceLocation; message?: string; width?: number; height?: number; pixels?: number[]; pixelsBase64?: string;
}
export interface FxSystem {
    key: string; name: string; templateKey: string; source: SourceLocation;
    definition?: FxNode; config?: ParticleConfig; fields: FxField[]; issues: FxIssue[];
    offset: [number, number, number]; rotation: number; rotationXYZ?: [number, number, number]; startFrame: number;
    systemLifeFrames?: number; initialDelayOverrideMs?: {low:number;high:number;type:'CONSTANT'|'UNIFORM'}; textureKey?: string; triggered: boolean;
}
export interface FxScene {
    source: [number, number, number]; target: [number, number, number];
    terrain: 'land' | 'water'; ground: number; seed: number;
}
export const defaultScene: FxScene = {source: [0, 0, 0], target: [100, 0, 0], terrain: 'land', ground: 0, seed: 1};
export interface PreviewIR {
    schemaVersion: 1; entry: string; assetKey: string; name: string;
    documentUri: string; documentVersion: number; generation: number;
    systems: FxSystem[]; fields: FxField[]; textures: FxTexture[]; issues: FxIssue[];
    dependencies: string[]; scene: FxScene;
}
export interface FxEnvelope { sessionId: string; documentVersion: number; requestId: number; }
export interface FxTextureTransfer {
    key: string; transferId: string; width: number; height: number; format: 'RGBA8' | 'DXT1' | 'DXT3' | 'DXT5';
    levels: {width: number; height: number; byteLength: number}[];
}
export type FxClientMessage = FxEnvelope & (
    {type: 'ready'; s3tc?: boolean} | {type: 'edit'; edits: {key: string; value: string; expectedValue: string}[]}
    | {type: 'locate'; key: string} | {type: 'select'; assetKey: string}
    | {type: 'scene'; scene: FxScene} | {type: 'reload'; resetRenderer?: boolean} | {type: 'saveAndBuild'}
    | {type: 'openShared'; systemKey: string} | {type: 'new'; preset: string}
    | {type: 'openFieldSource'; key: string} | {type: 'overrideField'; key: string; value: string; expectedValue: string}
    | {type: 'copy'; assetKey: string; systemKey?: string} | {type: 'state'; state: Record<string, unknown>}
    | {type: 'textureAck'; key: string; transferId: string; level: number; offset: number}
    | {type: 'undo'} | {type: 'redo'}
);
export type FxHostMessage = FxEnvelope & (
    {type: 'preview'; ir: PreviewIR; assets: {key: string; name: string; type: string}[]; editingUri: string}
    | {type: 'status'; message: string; stale?: boolean}
    | {type: 'active'; active: boolean} | {type: 'dispose'}
    | {type: 'focusField'; key: string} | {type: 'resetRenderer'}
    | {type: 'textureState'; previousKey: string; texture: FxTexture; issue?: FxIssue}
    | {type: 'textureBegin'; texture: FxTextureTransfer}
    | {type: 'textureChunk'; key: string; transferId: string; level: number; offset: number; total: number; pixelsBase64: string}
    | {type: 'textureEnd'; key: string; transferId: string}
);
