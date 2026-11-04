#!/usr/bin/env python3
"""Summarize existing paired benchmark windows without executing either SDK."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import statistics
import zipfile


def trace_ms(text, pattern):
    matches = []
    for line in text.splitlines():
        match = re.match(r'^\[(\d+):(\d+):(\d+(?:\.\d+)?)\]\s*(.*)', line)
        if match and re.search(pattern, match[4]):
            matches.append((int(match[1]) * 3600 + int(match[2]) * 60 + float(match[3])) * 1000)
    return matches[-1] if matches else None


def analyze(path):
    with zipfile.ZipFile(path) as archive:
        state = json.loads(archive.read('comparison.json'))
        if state['status'] != 'passed':
            raise ValueError('Require a successful complete paired benchmark')
        records, grouped = [], {}
        for sample in state['samples']:
            name = '{}-{}-{}-{}'.format(sample['workload'], sample['mode'], sample['round'], sample['engine'])
            row = {k: sample[k] for k in ('workload', 'mode', 'phase', 'round', 'engine', 'wallMs')}
            if sample['engine'] == 'new':
                report = json.loads(archive.read(name + '/report.json'))
                stages = {s['id']: s['elapsedMs'] for s in report['stages']}
                row.update(reportMs=report['elapsedMs'], outsideReportMs=sample['wallMs'] - report['elapsedMs'],
                           planMs=stages.get('plan', 0), compileMs=stages.get('compile', 0),
                           publishMs=stages.get('publish', 0), otherReportMs=report['elapsedMs'] - sum(stages.values()))
            else:
                text = archive.read(name + '/stdout.log').decode('utf-8', errors='replace')
                complete = trace_ms(text, r'^BinaryAssetBuilder complete$')
                row['originalTraceCompleteMs'] = complete
                row['outsideOriginalTraceMs'] = sample['wallMs'] - complete if complete is not None else None
                row['oldManifestUpToDate'] = 'Old manifest is up to date.' in text
                row['oldLinkOutputsUpToDate'] = all(
                    'Linked ' + kind + ' data up to date' in text for kind in ('binary', 'import', 'relocation'))
                start = trace_ms(text, r'^Loading (?:compressed )?XML cache$')
                age = trace_ms(text, r'^Session cache age is')
                row['oldXmlLoadToAgeTraceMs'] = age - start if start is not None and age is not None else None
            records.append(row)
            grouped.setdefault((row['workload'], row['mode'], row['phase'], row['engine']), []).append(row)
        groups = []
        for (workload, mode, phase, engine), rows in grouped.items():
            item = dict(workload=workload, mode=mode, phase=phase, engine=engine, n=len(rows))
            for key in rows[0]:
                values = [r[key] for r in rows if r.get(key) is not None]
                if key.endswith('Ms') and values:
                    item[key + 'Median'] = statistics.median(values)
            if engine == 'original':
                item['manifestUpToDateCount'] = sum(r['oldManifestUpToDate'] for r in rows)
                item['linkOutputsUpToDateCount'] = sum(r['oldLinkOutputsUpToDate'] for r in rows)
            groups.append(item)
    with open(path, 'rb') as stream:
        source_digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    return {'schemaVersion': 1, 'sourceRunId': state['runId'],
            'sourceBenchmarkCommit': state['benchmarkSourceCommit'], 'binarySourceCommit': state['binarySourceCommit'],
            'sourceArchiveSha256': source_digest, 'records': records, 'groups': groups,
            'limitations': ['Compile stage includes initialization, cache validation, Core processing and output hashing',
                            'Outside report/old trace windows combine startup and closing work; not pure startup timings',
                            'Old trace timestamps have coarser resolution and use different boundaries from new stages',
                            'Component medians need not add to median process time; no causal percentage assigned']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = Path(args.output)
    if output.exists():
        parser.error('Refusing to replace existing analysis evidence')
    output.parent.mkdir(parents=True, exist_ok=True)
    result = analyze(args.archive)
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    for row in result['groups']:
        if row['mode'] == 'both-deflate' and row['phase'] == 'hot':
            print(json.dumps(row))


if __name__ == '__main__':
    main()
