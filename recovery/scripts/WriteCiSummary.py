#!/usr/bin/env python3
"""Summarize selected CI reports and their upload size without staging or hashing."""
import argparse
import fnmatch
import glob
import json
import os
from pathlib import Path


def matches(parts, pattern):
    if not pattern:
        return not parts
    if pattern[0] == '**':
        return any(matches(parts[index:], pattern[1:]) for index in range(len(parts) + 1))
    return bool(parts) and fnmatch.fnmatchcase(parts[0], pattern[0]) and matches(parts[1:], pattern[1:])


def selected_files(patterns):
    included, exclusions = set(), []
    for pattern in patterns:
        pattern = pattern.strip()
        if not pattern:
            continue
        if pattern.startswith('!'):
            exclusions.append(pattern[1:].replace('\\', '/').split('/'))
        else:
            included.update(Path(name) for name in glob.iglob(pattern, recursive=True) if Path(name).is_file())
    # Apply exclusions only to selected files; never walk unrelated SDK/MOD trees.
    return sorted(path for path in included
                  if not any(matches(path.as_posix().split('/'), pattern) for pattern in exclusions))


def summarize(title, patterns):
    files = selected_files(patterns)
    sizes = {path: path.stat().st_size for path in files}
    total = sum(sizes.values())
    lines = [f'### {title}', '',
             f'Selected evidence: **{len(files)} files, {total:,} bytes '
             f'({total / 1024 / 1024:.2f} MiB before ZIP compression)**.', '',
             'Selected paths follow this workflow\'s report/transfer policy. Failed-step console logs are also available in the job log.', '']
    if total > 5 * 1024 * 1024:
        lines += ['Selected evidence exceeds 5 MiB; inspect the largest files below.', '']
    if files:
        lines += ['| Largest selected files | Bytes |', '| --- | ---: |']
        for path in sorted(files, key=lambda p: sizes[p], reverse=True)[:15]:
            lines.append(f'| `{str(path).replace(chr(124), "&#124;")}` | {sizes[path]:,} |')
        lines.append('')
    budget = 128 * 1024
    for path in files:
        # Bound summary reads; the artifact itself is never truncated or rewritten.
        if budget <= 0:
            continue
        if path.suffix.lower() == '.md' and sizes[path] <= 48 * 1024:
            body = path.read_text(encoding='utf-8-sig', errors='replace')[:min(budget, 16000)]
            lines += [f'<details><summary>{path.name}</summary>', '', body, '', '</details>', '']
            budget -= len(body)
        elif (path.name in ('validation.json', 'summary.json', 'measurement.json', 'benchmark.json', 'report.json', 'results.json')
              and sizes[path] <= 8 * 1024 * 1024):
            try:
                result = json.loads(path.read_text(encoding='utf-8-sig'))
            except (ValueError, UnicodeError):
                continue
            if isinstance(result, dict):
                status = {key: result[key] for key in ('succeeded', 'success', 'passed', 'status', 'errors', 'failures', 'durationSeconds', 'elapsedSeconds') if key in result}
                body = json.dumps(status or result, ensure_ascii=False, indent=2)[:min(budget, 4000)]
                lines += [f'<details><summary>{path}</summary>', '', '```json', body, '```', '', '</details>', '']
                budget -= len(body)
    return '\n'.join(lines) + '\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--title', default='CI evidence')
    parser.add_argument('--paths', action='append', default=[])
    args = parser.parse_args()
    patterns = os.environ.get('ARTIFACT_PATHS', '').splitlines() + args.paths
    report = summarize(args.title, patterns)
    destination = os.environ.get('GITHUB_STEP_SUMMARY')
    if destination:
        with open(destination, 'a', encoding='utf-8') as stream:
            stream.write(report)
    else:
        print(report)


if __name__ == '__main__':
    main()
