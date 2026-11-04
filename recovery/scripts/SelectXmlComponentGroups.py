#!/usr/bin/env python3
"""Reuse successful component evidence only when its actual inputs are unchanged.
This is test selection, not component acceptance. Selected groups keep full matrices.
"""
import argparse
import fnmatch
import json
from pathlib import Path
import re
import subprocess
from functools import lru_cache
import posixpath

ROOT = Path(__file__).resolve().parents[2]
FIRST_GROUPS = {
    'attributes': ['AttributeModifier', 'UpgradeTemplate', 'LogicCommandSet'],
    'effects': ['FXList'], 'powers': ['SpecialPowerTemplate', 'ObjectCreationList'],
    'locomotor': ['LocomotorTemplate'], 'fxparticle': ['FXParticleSystemTemplate'],
    'ai': ['AIPersonalityDefinition', 'AIStrategicStateDefinition', 'AIMicroManagerData',
           'AIBudgetStateDefinition', 'AIStateDefinition', 'AITargetingHeuristic'],
}

def git(root, *args, check=True):
    return subprocess.run(['git', '-C', str(root), *args], capture_output=True,
                          text=True, check=check)

def discover_groups(root):
    return sorted(p.stem[7:] for p in (root/'recovery/scripts').glob('TestXml*.ps1')
                  if re.fullmatch(r'TestXml[a-z][a-z0-9]*', p.stem)
                  and (root/f'recovery/tests/Xml{p.stem[7:]}Oracle.cs').is_file())

def group_roots(root, group):
    result = set(FIRST_GROUPS.get(group, []))
    for name in ['XmlRecoveredTypes.json', 'XmlComponentOnlyTypes.json', f'Xml{group}Types.json']:
        path = root/'recovery/catalog'/name
        if path.is_file():
            doc = json.loads(path.read_text(encoding='utf-8-sig'))
            result.update(doc.get('groups', {}).get(group, []))
            if name == f'Xml{group}Types.json':
                result.update(row['name'] for row in doc.get('roots', []))
    return sorted(result)

@lru_cache(maxsize=None)
def version_files(root, ref):
    if ref:
        return frozenset(git(root, 'ls-tree', '-r', '--name-only', ref, '--').stdout.splitlines())
    return frozenset(p.relative_to(root).as_posix() for directory in
                     ['recovery/src', 'recovery/tests', 'recovery/scripts', 'recovery/catalog']
                     for p in (root/directory).rglob('*') if p.is_file())

@lru_cache(maxsize=None)
def version_text(root, rel, ref):
    if ref:
        return git(root, 'show', ref+':'+rel).stdout
    return (root/rel).read_text(encoding='utf-8-sig')

def include_closure(root, paths, ref=None):
    available = version_files(root, ref)
    pending = list(paths)
    result = set(paths)
    while pending:
        rel = pending.pop()
        if rel not in available or Path(rel).suffix.lower() not in ('.cpp', '.h', '.inc'):
            continue
        text = version_text(root, rel, ref)
        for name in re.findall(r'^\s*#include\s*"([^"\n]+)"', text, re.M):
            choices = [posixpath.normpath(str(Path(rel).parent/name)),
                       'recovery/src/Native/'+name, 'recovery/src/Bridge/'+name]
            target = next((p for p in choices if p in available), choices[0])
            added = [target]
            cpp = str(Path(target).with_suffix('.cpp'))
            if Path(target).suffix == '.h' and cpp in available:
                added.append(cpp)
            for item in added:
                if item not in result:
                    result.add(item)
                    pending.append(item)
    return result

def dependencies(root, group, ref=None):
    available = version_files(root, ref)
    paths = {f'recovery/scripts/TestXml{group}.ps1'}
    if group == 'joypad':
        paths.add('recovery/scripts/PrepareJoypadFixtures.py')
    patterns = [f'recovery/src/Native/Xml{group}*', f'recovery/src/Bridge/Xml{group}*',
                f'recovery/tests/Xml{group}*.cs']
    paths.update(p for p in available if any(fnmatch.fnmatchcase(p, pattern) for pattern in patterns))
    script = f'recovery/scripts/TestXml{group}.ps1'
    if script in available:
        for spec in re.findall(r'recovery/src/[A-Za-z0-9_./*?\-]+', version_text(root, script, ref)):
            if '*' not in spec and '?' not in spec:
                paths.add(spec)
            else:
                paths.update(p for p in available if fnmatch.fnmatchcase(p, spec))
    catalog = f'recovery/catalog/Xml{group}Types.json'
    if catalog in available:
        paths.add(catalog)
    paths.add('recovery/src/Bridge/XmlNodeAdapter.h')
    return include_closure(root, paths, ref)

def native_inputs(paths):
    return {p for p in paths if p.startswith('recovery/src/')}

def valid_evidence(root, group, head="HEAD"):
    path = root/f'recovery/evidence/xml-{group}/validation.json'
    try:
        row = json.loads(path.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return None, 'missing or unreadable successful evidence'
    if row.get('succeeded') is not True or not row.get('runId'):
        return None, 'previous validation did not succeed'
    anchor = row.get('sourceCommit', '')
    if not re.fullmatch(r'[0-9a-fA-F]{40}', anchor) or git(root, 'cat-file', '-e', anchor+'^{commit}', check=False).returncode:
        return None, 'validation source commit is unavailable'
    if git(root, 'merge-base', '--is-ancestor', anchor, head, check=False).returncode:
        return None, 'validation source commit is not an ancestor'
    modes = row.get('modes', [])
    labels = {m.get('mode', '').lstrip('/') for m in modes}
    if len(modes) != 2 or labels != {'Od', 'O2'}:
        return None, 'incomplete optimization mode evidence'
    for mode in modes:
        platforms = mode.get('result', {}).get('platforms', mode.get('platforms'))
        if platforms is None:
            platforms = [int(v) for line in mode.get('summaries', [])
                         for v in re.findall(r'\bplatform=(\d+)\b', line)]
        try:
            values = [int(v['platform'] if isinstance(v, dict) else v) for v in platforms]
        except (KeyError, TypeError, ValueError):
            return None, 'invalid platform evidence'
        if len(values) != 4 or set(values) != {0, 1, 2, 99}:
            return None, 'incomplete four-platform mode evidence'
        if not (mode.get('succeeded') is True or ('exitCode' in mode and mode['exitCode'] == 0) or mode.get('result', {}).get('succeeded') is True):
            return None, 'optimization mode has no success attestation'
        if mode.get('succeeded') is False or mode.get('exitCode', 0) != 0 or mode.get('result', {}).get('succeeded') is False:
            return None, 'failed optimization mode evidence'
    return row, None

def affects(changed, paths):
    # Runtime compiler/core DLL changes invalidate every component using that toolchain.
    return bool(changed & paths or any(p.startswith('SDK/tools/') and p.lower().endswith('.dll') for p in changed))

def select(root, head='HEAD', force_groups=None):
    root = Path(root).resolve()
    version_files.cache_clear()
    version_text.cache_clear()
    groups = discover_groups(root)
    forced = set(force_groups or [])
    if forced - set(groups):
        raise ValueError('Unknown requested groups: '+','.join(sorted(forced-set(groups))))
    changes = {}
    def changed_since(anchor):
        if anchor not in changes:
            changes[anchor] = set(git(root, 'diff', '--name-only', anchor, head, '--').stdout.splitlines())
        return changes[anchor]
    inputs = {g: dependencies(root, g) for g in groups}
    native = {g: native_inputs(paths) for g, paths in inputs.items()}
    rows, reasons, reused, selected = {}, {}, [], []
    for group in groups:
        row, error = valid_evidence(root, group, head)
        rows[group] = row
        if row:
            inputs[group].update(dependencies(root, group, row['sourceCommit']))
            native[group] = native_inputs(inputs[group])
        if group in forced:
            reason = 'explicitly requested'
        elif error:
            reason = error
        elif affects(changed_since(row['sourceCommit']), inputs[group]):
            reason = 'component source, dependency or test input changed'
        else:
            reused.append({'group': group, 'sourceCommit': row['sourceCommit'], 'runId': str(row['runId'])})
            continue
        selected.append(group)
        reasons[group] = reason
    link_inputs = set().union(*native.values()) if native else set()
    for directory, pattern in [('recovery/src/Native', 'Xml*.cpp'),
                               ('recovery/src/Bridge', 'Xml*Compiler.cpp')]:
        link_inputs.update(p.relative_to(root).as_posix() for p in (root/directory).glob(pattern))
    link_inputs = include_closure(root, link_inputs)
    link_inputs.update({'recovery/scripts/TestXmlPriorityLink.ps1', 'recovery/tests/XmlPriorityLinkOracle.cs'})
    link, link_error = valid_evidence(root, 'priority-link', head)
    if link_error:
        link_ready, link_reason = True, link_error
        relevant = [g for g in groups if not rows[g] or affects(changed_since(rows[g]['sourceCommit']), native[g])]
    else:
        for g in groups:
            prior = native_inputs(dependencies(root, g, link['sourceCommit']))
            native[g].update(prior)
            link_inputs.update(prior)
        changed = changed_since(link['sourceCommit'])
        link_ready = affects(changed, link_inputs)
        link_reason = 'combined source or harness inputs changed' if link_ready else 'successful combined evidence inputs unchanged'
        relevant = [g for g in groups if affects(changed, native[g])]
    if link_ready and not relevant:
        # Harness-only edits can validate selected pending roots, or metadata alone.
        relevant = selected
    roots = sorted({name for g in relevant for name in group_roots(root, g)}) if link_ready else []
    return {'groups': selected, 'reused': reused, 'reasons': reasons,
            'link_ready': link_ready, 'link_roots': roots, 'link_reason': link_reason}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=ROOT)
    parser.add_argument('--head', default='HEAD')
    parser.add_argument('--groups', default='', help='Explicit comma-separated affected groups; normal mode reuses unchanged success evidence')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result = select(args.root, args.head, [g.strip() for g in args.groups.split(',') if g.strip()])
    encoded = json.dumps(result, ensure_ascii=False)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(encoded+'\n', encoding='utf-8')
    print(encoded)

if __name__ == '__main__':
    main()
