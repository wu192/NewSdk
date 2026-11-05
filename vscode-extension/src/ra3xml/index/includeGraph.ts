/** Include visibility and reverse closure operate on the same source graph as FX. */
export type { IncludeMode, IncludeEdge } from './assetIndex';
import type { IncludeEdge } from './assetIndex';
export function affectedIncludeEntries(changed: string, edges: readonly IncludeEdge[]): Set<string> {
    const result=new Set([changed]);const pending=[changed];
    while(pending.length){const target=pending.pop()!;for(const edge of edges)if(edge.to===target&&!result.has(edge.from)){result.add(edge.from);pending.push(edge.from);}}
    return result;
}
