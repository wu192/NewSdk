#!/usr/bin/env python3
"""Prepare an untouched original SDK runtime against the same benchmark inputs."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as E

SOURCE = '39039961847cd1ca9cb93743fd51741a4e5c9f49'
RUN = '40814401237'


def need(value, message):
    if not value:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def inventory(root, key='path'):
    root = Path(root)
    need(root.is_dir() and not root.is_symlink(), 'Inventory root is missing or linked')
    rows = []
    for path in sorted(root.rglob('*')):
        need(not path.is_symlink(), 'Linked SDK/workload input rejected')
        if path.is_file():
            data = path.read_bytes()
            rows.append({key: path.relative_to(root).as_posix(), 'bytes': len(data), 'sha256': sha(data)})
    return rows


def semantic(node):
    return (node.tag, tuple(sorted(node.attrib.items())), (node.text or '').strip(),
            tuple(semantic(child) for child in node))


def startup_config(data):
    """Change only startup; preserve assetbuilder bytes and all LoD declarations."""
    before = E.fromstring(data)
    need(before.tag == 'configuration' and before.find('assetbuilder') is not None,
         'Original SDK assetbuilder configuration is missing')
    startup = (b'<startup useLegacyV2RuntimeActivationPolicy="true">'
               b'<supportedRuntime version="v4.0"/></startup>')
    pattern = rb'<startup\b[^>]*(?:/>|>.*?</startup\s*>)'
    matches = list(re.finditer(pattern, data, flags=re.S))
    need(len(matches) <= 1, 'Ambiguous original startup configuration')
    if matches:
        match = matches[0]
        effective = data[:match.start()] + startup + data[match.end():]
    else:
        match = re.search(rb'</configuration\s*>', data)
        need(match is not None, 'Original configuration closing element is absent')
        effective = data[:match.start()] + startup + b'\r\n' + data[match.start():]
    after = E.fromstring(effective)
    need(semantic(before.find('assetbuilder')) == semantic(after.find('assetbuilder')),
         'Startup adaptation changed assetbuilder settings')
    before_without = copy.deepcopy(before)
    after_without = copy.deepcopy(after)
    for tree in (before_without, after_without):
        for node in list(tree):
            if node.tag == 'startup':
                tree.remove(node)
    need(semantic(before_without) == semantic(after_without), 'Startup adaptation changed other configuration sections')
    return effective


def resolve_case_path(root, configured):
    """Match Win32 separator/case semantics when preflighting on Linux too."""
    configured = configured.replace('\\', os.sep).replace('/', os.sep)
    path = Path(configured)
    if not path.is_absolute():
        path = Path(root) / path
    normalized = Path(os.path.abspath(path))
    if normalized.exists() or os.name == 'nt':
        return normalized
    current = Path(normalized.anchor)
    for part in normalized.parts[1:]:
        exact = current / part
        if exact.exists() or not current.is_dir():
            current = exact
            continue
        choices = [p for p in current.iterdir() if p.name.casefold() == part.casefold()]
        need(len(choices) <= 1, 'Case-ambiguous Win32 input root')
        current = choices[0] if choices else exact
    return current


def effective_roots(sdk, mod, section):
    sdk, mod = Path(sdk).resolve(), Path(mod).resolve()
    need(';' not in str(mod), 'MOD path cannot contain the legacy list separator')
    roots = dict(dataRoot=str(sdk))
    missing_roots = {}
    for kind in ('Data', 'Art', 'Audio'):
        configured = section.get('default' + kind + 'Paths')
        need(configured is not None, 'SDK configuration omitted default' + kind + 'Paths')
        values = [str(mod / kind)] + [value for value in configured.split(';') if value != '']
        selected, missing = [], []
        for value in values:
            need(value.strip() and value != '*', 'Empty/wildcard root cannot be frozen for a paired benchmark')
            path = resolve_case_path(sdk, value)
            selected.append(str(path))
            if not path.is_dir():
                need(not path.exists(), 'Configured input root is not a directory')
                missing.append(str(path))
        # Preserve original order and duplicates; never sort resolver candidates.
        roots[kind.lower() + 'Paths'] = selected
        missing_roots[kind.lower() + 'Paths'] = missing
    return roots, missing_roots


def verify_original_binaries(repo, tools):
    result = subprocess.run(['git', '-C', str(repo), 'ls-tree', '-rz', SOURCE, 'SDK/tools'],
                            capture_output=True, check=True, timeout=60)
    expected = {}
    for row in result.stdout.split(b'\0'):
        if row:
            meta, path = row.split(b'\t', 1)
            mode, kind, blob = meta.decode().split()
            name = path.decode('utf-8').removeprefix('SDK/tools/')
            if name.lower().endswith(('.exe', '.dll')):
                need(mode == '100644' and kind == 'blob', 'Non-file original executable input')
                expected[name] = blob
    need('BinaryAssetBuilder.exe' in expected, 'Original executable identity is absent')
    actual = {p.relative_to(tools).as_posix() for p in tools.rglob('*') if p.is_file() and p.suffix.lower() in ('.exe', '.dll')}
    need(actual == set(expected), 'Original SDK EXE/DLL membership differs from accepted source')
    for name, blob in expected.items():
        data = (tools / name).read_bytes()
        git_blob = hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()
        need(git_blob == blob, 'Original executable/DLL differs from accepted SDK: ' + name)


def prepare(preparation, destination, repo, source=SOURCE, run=RUN):
    initial = json.loads(Path(preparation).read_text(encoding='utf-8'))
    need(initial.get('succeeded') is True and initial.get('binarySourceCommit') == source and
         str(initial.get('sourceRunId')) == str(run), 'Shared preparation is not the accepted binary/input source')
    sdk = Path(initial['sdkRoot']).resolve()
    need(sdk == (repo / 'SDK').resolve(), 'Shared SDK root differs from repository SDK')
    original_config = sdk / 'tools/BinaryAssetBuilder.exe.config'
    config = original_config.read_bytes()
    section = E.fromstring(config).find('assetbuilder')
    need(section is not None, 'Shared SDK assetbuilder section is absent')
    need(not destination.exists(), 'Refusing to replace original SDK preparation')
    need(not destination.is_relative_to(sdk), 'Original preparation cannot modify shared SDK inputs')
    tools = sdk / 'tools'
    verify_original_binaries(repo, tools)
    before = inventory(tools, 'name')
    workloads = copy.deepcopy(initial['workloads'])
    for workload in workloads:
        mod = Path(workload['modRoot'])
        need(inventory(mod) == workload['files'], 'Shared workload differs from its accepted preparation: ' + workload['name'])
        roots, missing = effective_roots(sdk, mod, section)
        workload['effectiveInputRoots'] = roots
        workload['missingInputRoots'] = missing
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='.sdk-benchmark-', dir=destination.parent) as temporary:
        stage = Path(temporary) / 'prepared'
        stage.mkdir()
        runtime = stage / 'original-runtime'
        shutil.copytree(tools, runtime)
        need(inventory(runtime, 'name') == before, 'Original runtime copy differs from source SDK/tools')
        effective = startup_config(config)
        (runtime / 'BinaryAssetBuilder.exe.config').write_bytes(effective)
        files = inventory(runtime, 'name')
        for old in before:
            current = next(row for row in files if row['name'] == old['name'])
            if old['name'] != 'BinaryAssetBuilder.exe.config':
                need(current == old, 'Original runtime copy changed an EXE/DLL/dependency')
        need(inventory(tools, 'name') == before, 'Original SDK/tools changed during preparation')
        state = dict(schemaVersion=1, succeeded=True, binarySourceCommit=source,
                     benchmarkSourceCommit=initial['benchmarkSourceCommit'], sourceRunId=run,
                     sharedPreparation=str(Path(preparation).resolve()), sharedPreparationSha256=sha(Path(preparation).read_bytes()),
                     originalExe=str(destination / 'original-runtime/BinaryAssetBuilder.exe'),
                     originalRuntimeDirectory=str(destination / 'original-runtime'), originalRuntimeFiles=files,
                     originalSourceFiles=before, sourceSdkRoot=str(sdk), originalWorkingDirectory=str(sdk),
                     originalConfigSha256=sha(config), effectiveConfigSha256=sha(effective),
                     assetbuilderConfigUnchanged=True, workloads=workloads,
                     scope='Original SDK EXE/DLL bytes copied from accepted source; only CLR4 startup adapted. Shared workload files and SDK roots; no second workload copy or modified plugin/cache/buildConfiguration settings.')
        (stage / 'original-preparation.json').write_text(json.dumps(state, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        stage.rename(destination)
    return destination / 'original-preparation.json'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--preparation', type=Path, required=True)
    parser.add_argument('--binary-source-commit', default=SOURCE)
    parser.add_argument('--source-run-id', default=RUN)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    args = parser.parse_args()
    print(prepare(args.preparation, args.output.resolve(), args.repo.resolve(), args.binary_source_commit, args.source_run_id))


if __name__ == '__main__':
    main()
