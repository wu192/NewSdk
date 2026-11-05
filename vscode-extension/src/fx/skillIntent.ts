/** Authoring data is separate from preview playback and compiler outcomes. */
export type FxCheckStatus = 'PASS' | 'FAIL' | 'UNKNOWN' | 'SKIPPED';
export interface FxValidationLevel {
    level: 'V0' | 'V1' | 'V2' | 'V3'; status: FxCheckStatus; summary: string; rules: string[];
    diagnostics?: {message: string; line?: number; column?: number}[];
}
export interface FxValidationReport { levels: FxValidationLevel[]; schema?: string; schemaVersion?: string; checkedAt: string; }
export interface FxVisualIntent {
    input_kind: 'text' | 'image' | 'video' | 'xml' | 'mixed';
    appearance: unknown; timing: unknown; motion: unknown; resources: unknown;
    constraints: unknown; unknowns: unknown;
}
export interface FxAuthoringAttachment {
    name: string; kind: 'image' | 'video' | 'keyframe'; status: 'selected' | 'read';
    timestampSeconds?: number;
}
export interface FxAuthoringView {
    mode: 'new' | 'edit'; focus?: boolean; skill?: {version: string}; busy: boolean; message?: string;
    attachments: FxAuthoringAttachment[]; resources: {type: string; id: string}[];
    draft?: {id: string; changes: string[]; files: string[]; report: FxValidationReport; intent?: unknown; applicable: boolean; stale: boolean};
}
export type FxAuthoringAction =
    | {action: 'open'; mode?: 'new' | 'edit'}
    | {action: 'generate' | 'analyze' | 'copyPrompt'; mode: 'new' | 'edit'; description: string; recipe?: 'impact' | 'smoke'; name?: string; resourceIds?: string[]}
    | {action:'editRecipe';settings:Record<string,string|number>}
    | {action: 'import'; mode: 'new' | 'edit'; xml?: string}
    | {action: 'check' | 'apply' | 'diff' | 'attach' | 'clearAttachments' | 'preview'};
export function canApplyAuthoring(report: FxValidationReport): boolean {
    return ['V0','V1'].every(level => report.levels.some(item => item.level === level && item.status !== 'FAIL'));
}
