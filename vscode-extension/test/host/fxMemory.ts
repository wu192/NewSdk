import * as fs from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const execute = promisify(execFile);
const maxProcesses = 4096;
interface ProcessRow { id: number; parent: number }
export interface FxMemorySample {
    available: boolean; timestamp: string; durationMs: number; processCount?: number;
    measuredProcessCount?: number; residentBytes?: number; reason?: string;
}

/** Only identities are used internally; reports never contain process IDs, names or paths. */
export function descendantIds(rows: readonly ProcessRow[], root: number, excluded?: number): Set<number> {
    const children = new Map<number, number[]>();
    for (const row of rows) { const list = children.get(row.parent) ?? []; list.push(row.id); children.set(row.parent, list); }
    const included = new Set<number>(), queue = [root];
    while (queue.length) {
        const id = queue.pop()!;
        if (id === excluded || included.has(id)) continue;
        included.add(id); queue.push(...(children.get(id) ?? []));
    }
    return included;
}

async function linuxResident(): Promise<Pick<FxMemorySample, 'processCount' | 'measuredProcessCount' | 'residentBytes'>> {
    const started = Date.now(), deadline = started + 5000;
    const entries = (await fs.readdir('/proc')).filter(name => /^\d+$/.test(name));
    if (entries.length > maxProcesses) throw new Error('enumeration-budget');
    const rows: ProcessRow[] = [];
    for (let offset = 0; offset < entries.length; offset += 32) {
        if (Date.now() > deadline) throw new Error('enumeration-timeout');
        const group = await Promise.all(entries.slice(offset, offset + 32).map(async name => {
            try {
                const stat = await fs.readFile(`/proc/${name}/stat`, 'utf8');
                const end = stat.lastIndexOf(')'), columns = stat.slice(end + 2).split(' ');
                return end >= 0 ? {id:Number(name), parent:Number(columns[1])} : undefined;
            } catch { return undefined; } // Processes may exit between enumeration and read.
        }));
        rows.push(...group.filter((row): row is ProcessRow => !!row));
    }
    const byId = new Map(rows.map(row => [row.id, row]));
    const executable = await fs.readlink(`/proc/${process.pid}/exe`);
    let root = process.pid; const seen = new Set<number>([root]);
    // Ascend only through the same Electron executable, never into the CI runner.
    for (let depth = 0; depth < 32; depth++) {
        const parent = byId.get(root)?.parent;
        if (!parent || seen.has(parent)) break;
        try { if (await fs.readlink(`/proc/${parent}/exe`) !== executable) break; } catch { break; }
        seen.add(parent); root = parent;
    }
    const ids = descendantIds(rows, root); let measuredProcessCount = 0, residentBytes = 0;
    for (const id of ids) {
        if (Date.now() > deadline) throw new Error('measurement-timeout');
        try {
            const status = await fs.readFile(`/proc/${id}/status`, 'utf8');
            const match = /^VmRSS:\s+(\d+)\s+kB$/m.exec(status);
            if (match) { measuredProcessCount++; residentBytes += Number(match[1]) * 1024; }
        } catch { /* A disappearing child contributes no live RSS sample. */ }
    }
    return {processCount:ids.size, measuredProcessCount, residentBytes};
}

async function windowsResident(): Promise<Pick<FxMemorySample, 'processCount' | 'measuredProcessCount' | 'residentBytes'>> {
    // No CommandLine/ExecutablePath query. The small child sampler and its descendants
    // are excluded, and only final aggregate counters cross stdout.
    const script = `
$ErrorActionPreference='Stop'
$targetId=${process.pid}
$samplerId=$PID
$rows=@(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,Name)
if($rows.Count -gt ${maxProcesses}){throw 'enumeration-budget'}
$map=@{}
foreach($row in $rows){$map[[int]$row.ProcessId]=$row}
$root=$targetId
if(!$map.ContainsKey($root)){throw 'target-unavailable'}
$image=$map[$root].Name
$seen=@{}
for($depth=0;$depth -lt 32;$depth++){
  $parentId=[int]$map[$root].ParentProcessId
  if(!$map.ContainsKey($parentId) -or $seen.ContainsKey($parentId) -or $map[$parentId].Name -ne $image){break}
  $seen[$root]=$true
  $root=$parentId
}
$children=@{}
foreach($row in $rows){$parentId=[int]$row.ParentProcessId;if(!$children.ContainsKey($parentId)){$children[$parentId]=New-Object System.Collections.ArrayList};[void]$children[$parentId].Add([int]$row.ProcessId)}
$included=@{}
$queue=New-Object 'System.Collections.Generic.Stack[int]'
$queue.Push($root)
while($queue.Count -gt 0){
  $nextId=$queue.Pop()
  if($nextId -eq $samplerId -or $included.ContainsKey($nextId)){continue}
  $included[$nextId]=$true
  if($children.ContainsKey($nextId)){foreach($childId in $children[$nextId]){$queue.Push($childId)}}
}
$bytes=[long]0
$measured=0
foreach($entryId in $included.Keys){$item=Get-Process -Id $entryId -ErrorAction SilentlyContinue;if($null -ne $item){$bytes+=[long]$item.WorkingSet64;$measured++}}
@{processCount=$included.Count;measuredProcessCount=$measured;residentBytes=$bytes}|ConvertTo-Json -Compress
`;
    const result = await execute('powershell.exe', ['-NoLogo','-NoProfile','-NonInteractive','-Command',script],
        {windowsHide:true, timeout:8000, maxBuffer:8192, encoding:'utf8'});
    const parsed = JSON.parse(result.stdout.trim().replace(/^\uFEFF/, '')) as Record<string, unknown>;
    for (const name of ['processCount','measuredProcessCount','residentBytes']) {
        if (!Number.isSafeInteger(parsed[name]) || (parsed[name] as number) < 0) throw new Error('invalid-counters');
    }
    if ((parsed.processCount as number) > maxProcesses || (parsed.measuredProcessCount as number) > (parsed.processCount as number)) throw new Error('invalid-counters');
    return {processCount:parsed.processCount as number, measuredProcessCount:parsed.measuredProcessCount as number, residentBytes:parsed.residentBytes as number};
}

export async function captureFxMemory(): Promise<FxMemorySample> {
    const started = Date.now();
    try {
        if (process.platform !== 'win32' && process.platform !== 'linux') return {available:false,timestamp:new Date().toISOString(),durationMs:0,reason:'platform-not-measured'};
        const sample = await (process.platform === 'win32' ? windowsResident() : linuxResident());
        if (!sample.measuredProcessCount) throw new Error('processes-unavailable');
        return {available:true,timestamp:new Date().toISOString(),durationMs:Date.now()-started,...sample};
    } catch {
        // Error objects may contain executable paths. Keep the report deliberately generic.
        return {available:false,timestamp:new Date().toISOString(),durationMs:Date.now()-started,reason:'bounded-process-sample-unavailable'};
    }
}

export function fxMemoryReport(baselineClosed: FxMemorySample, openedAfterEditing: FxMemorySample, closedAfterEditing: FxMemorySample) {
    const delta = (sample: FxMemorySample): number | undefined => sample.available && baselineClosed.available
        ? sample.residentBytes! - baselineClosed.residentBytes! : undefined;
    return {
        platform:process.platform, metric:process.platform === 'win32' ? 'WorkingSet64' : process.platform === 'linux' ? 'VmRSS' : 'not measured',
        scope:'Current test VS Code instance main process and descendant processes only; extension host, window, shared, GPU and utility processes present in that tree. Sampling child excluded; CI runner ancestors excluded.',
        enumeration:'One process-identity snapshot, capped at 4096 entries; measure only selected instance descendants. Windows uses Win32_Process identities and Get-Process WorkingSet64; Linux uses /proc ancestry, executable identity and VmRSS. No identifiers, paths or command lines are persisted.',
        enumerationLimit:maxProcesses, samplingTimeoutMs:process.platform === 'win32' ? 8000 : 5000,
        baselinePoint:'All FX editors closed and session count zero, before loading the functional fixtures; 250 ms idle.',
        activePoint:'Preview reopened after editing, undo/redo, XML recovery and shared-document operations; before compiler invocation; 250 ms idle.',
        closedPoint:'All editors closed and FX session count zero; 250 ms idle.',
        limitations:'Sum of per-process RSS/WorkingSet may count shared pages more than once; this is not unique physical allocation, peak memory, driver VRAM or an isolated FX allocation. Includes IDE overhead. Snapshots are not atomic and children that exit may be omitted. Short post-close idle does not guarantee GC. Negative deltas are observations, not precise allocation or reclamation amounts. Functional fixtures are not the 10k-particle reference GPU load.',
        samples:{baselineClosed,openedAfterEditing,closedAfterEditing},
        deltaBytesFromBaseline:{opened:delta(openedAfterEditing),closed:delta(closedAfterEditing)},
    };
}
