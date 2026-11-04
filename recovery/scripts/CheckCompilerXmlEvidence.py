#!/usr/bin/env python3
"""Read archived P4 XML receipts and raw matrix logs without running a compiler.

Checks receipt consistency, not the authenticity of a GitHub artifact. Archived
EXEs, objects and runtime DLLs are normally absent: their bytes are NOT rehashed.
SDK/G3 acceptance, other component evidence and XML smoke remain separate.
"""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys

MODES = ('Debug', 'Release')
PLATFORMS = [0, 1, 2, 99]
SHA = re.compile(r'^[0-9a-f]{64}$')
EXCLUDED = {'XmlNodeOracle.cs', 'XmlPriorityLinkOracle.cs', 'XmlPrioritySdkOracle.cs'}
ORIGINAL_XML_SHA = 'd4b78e2da951dd458357bee7b358e6bc9689d1987d55867254d68b3f87bbf0e6'
MATRIX_STATUS = 'passed all 63 complete group matrices in Debug and Release; SDK and G3 acceptance remain separate'


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key: ' + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'), object_pairs_hook=unique_pairs)


def indexed(rows, key, names, label):
    require(isinstance(rows, list), label + ': expected array')
    result = {}
    for row in rows:
        require(isinstance(row, dict) and isinstance(row.get(key), str), label + ': missing ' + key)
        name = row[key]
        require(name not in result, label + ': duplicate ' + name)
        result[name] = row
    require(set(result) == set(names), label + ': missing or unexpected entries')
    return result


def hashes(rows, label):
    require(isinstance(rows, list) and rows, label + ': missing input inventory')
    seen = set()
    for row in rows:
        require(isinstance(row, dict) and isinstance(row.get('path'), str), label + ': missing path')
        require(row['path'].casefold() not in seen, label + ': duplicate path')
        seen.add(row['path'].casefold())
        require(isinstance(row.get('sha256'), str) and SHA.fullmatch(row['sha256']), label + ': invalid SHA-256')


def git_bytes(repository, *args):
    return subprocess.check_output(['git', '-C', str(repository), *args], stderr=subprocess.PIPE)


def revision_hash(repository, source, path):
    return hashlib.sha256(git_bytes(repository, 'show', source + ':' + path)).hexdigest()


def revision_text_hashes(repository, source, path):
    # GitHub's Windows checkout can materialize uniform LF text as CRLF.
    # Compare exact candidate bytes for these known source files only; never
    # normalize archived logs, transcripts, recipes, resources or DLLs.
    require(path.endswith(('.cs', '.py', '.ps1')), 'Checkout conversion requires a known text source')
    data = git_bytes(repository, 'show', source + ':' + path)
    result = {hashlib.sha256(data).hexdigest(): 'git-blob'}
    if b'\r' not in data and b'\n' in data:
        result[hashlib.sha256(data.replace(b'\n', b'\r\n')).hexdigest()] = 'windows-crlf'
    return result


def repository_groups(repository, source):
    # Read the recorded revision, so later fixture edits cannot invalidate an
    # otherwise reusable receipt or bless a reduced historical group selection.
    def git(*args):
        return git_bytes(repository, *args)
    paths = git('ls-tree', '-r', '--name-only', source, '--', 'recovery/tests').decode().splitlines()
    oracles = sorted(p for p in paths if re.fullmatch(r'recovery/tests/Xml[^/]*Oracle\.cs', p)
                     and pathlib.PurePosixPath(p).name not in EXCLUDED)
    expected_count = 64 if 'recovery/tests/XmljoypadOracle.cs' in paths else 63
    require(len(oracles) == expected_count, 'Recorded revision XML group inventory is incomplete')
    result = {}
    for oracle in oracles:
        name = pathlib.PurePosixPath(oracle).stem.removesuffix('Oracle')
        inputs = [oracle] + sorted(p for p in paths if p.startswith('recovery/tests/' + name)
                                   and p.endswith('Cases.cs'))
        result[name] = [{'path': p, 'sha256': hashlib.sha256(git('show', source + ':' + p)).hexdigest()}
                        for p in inputs]
    return result


def check_raw(folder, group):
    rows, calls = [], []
    for line in (folder / 'host.log').read_text(encoding='utf-8-sig').splitlines():
        m = re.fullmatch(r'PASS platform=(\d+) fixtures=(\d+) (?:arrayComparisons|comparisons)=(\d+) rejections=(\d+) (?:layoutAssertions|independent)=(\d+)', line)
        if m:
            p, f, c, r, l = map(int, m.groups())
            if group in ('Xmlpowers', 'Xmllocomotor', 'Xmlfxparticle'):
                require(c == 3 * f, group + ': single-path three-buffer coverage')
            elif group == 'Xmlplayers':
                require(c >= 3 * f, group + ': three-buffer coverage')
            elif group == 'Xmlai':
                require(f >= 1000 and c >= 6000 and r >= 30 and l >= 300, group + ': full AI coverage')
            else:
                require(c == 6 * f, group + ': direct/ProcessInstance coverage')
        else:
            m = re.fullmatch(r'PASS platform=(\d+) fixtures=(\d+) threeArrayComparisons=(\d+) directArrayComparisons=(\d+) rejections=(\d+) independentLayouts=(\d+)', line)
            if m:
                p, f, a, b, r, l = map(int, m.groups())
                require(a == 3 * f and b == 3 * f, group + ': separate direct/ProcessInstance coverage')
                c = a + b
            else:
                m = re.fullmatch(r'SUMMARY platform=(\d+) fixtures=(\d+) threeArrayComparisons=(\d+) rejected=(\d+) independentLayouts=(\d+)', line)
                if m:
                    p, f, c, r, l = map(int, m.groups())
                    require(c >= 3 * f, group + ': attributes three-buffer coverage')
                else:
                    m = re.fullmatch(r'ORACLE-END calls=(\d+)', line)
                    if m:
                        calls.append(int(m[1]))
                    continue
        require(f > 0 and r > 0 and l > 0, group + ': missing fixtures/rejections/layouts')
        if group not in ('Xmlplayers', 'Xmlai', 'Xmlpowers', 'Xmlattributes'):
            require(l >= f, group + ': incomplete layout coverage')
        if group == 'Xmlplayers':
            require(f >= 1000 and c >= 3000 and r >= 30 and l >= 300, group + ': full Players coverage')
        rows.append(dict(platform=p, fixtures=f, comparisons=c, rejections=r, layouts=l))
    require([r['platform'] for r in rows] == PLATFORMS, group + ': complete ordered four-platform matrix missing')
    require(len(calls) == 1 and calls[0] >= sum(r['fixtures'] for r in rows), group + ': oracle terminal/call coverage')
    transcript = folder / 'original.tsv'
    wire = transcript.read_text(encoding='utf-8-sig').splitlines()
    require(len(wire) == calls[0] + 1 and wire[-1] == 'END\t' + str(calls[0]), group + ': incomplete oracle transcript')
    seen = set()
    for i, line in enumerate(wire[:-1], 1):
        parts = line.split('\t')
        require(len(parts) == 6 and parts[0] == str(i) and parts[1] in ('0', '1', '2', '99')
                and all(SHA.fullmatch(v) for v in parts[2:]), group + ': malformed oracle row')
        seen.add(int(parts[1]))
    require(seen == set(PLATFORMS), group + ': oracle platform missing')
    return rows, calls[0], digest(transcript)


def check(root, repository, expected_source=None):
    root, repository = pathlib.Path(root), pathlib.Path(repository)
    records = []
    for path in root.rglob('*.json'):
        if path.name in ('validation.json', 'build.json', 'validation-host-build.json', 'link-inputs.json'):
            value = read_json(path)
            require(isinstance(value, dict), str(path) + ': expected JSON receipt object')
            records.append((path, value))
    matrices = [(p, r) for p, r in records if 'groups' in r and 'sdkValidated' in r and 'modes' in r]
    require(len(matrices) == 1, 'Expected one complete XML matrix validation.json')
    matrix_path, matrix = matrices[0]
    source = matrix.get('sourceCommit')
    require(isinstance(source, str) and re.fullmatch('[0-9a-f]{40}', source), 'Invalid sourceCommit')
    require(expected_source is None or source == expected_source, 'Unexpected sourceCommit')
    require(matrix.get('schemaVersion') == 1 and matrix.get('succeeded') is True
            and matrix.get('sdkValidated') is False, 'XML receipt is incomplete or claims unsupported acceptance')
    run_id, attempt = matrix.get('runId'), matrix.get('runAttempt')
    require(type(run_id) in (str, int) and str(run_id).strip()
            and type(attempt) in (str, int) and str(attempt).isdigit() and int(attempt) > 0, 'Missing or invalid run identity')
    checkout_forms = set()
    for field, path in [('adapterSha256', 'recovery/scripts/PrepareCompilerXmlMatrices.py'),
                        ('runnerSha256', 'recovery/scripts/TestCompilerXmlMatrices.ps1'),
                        ('transportSha256', 'recovery/tests/XmlProductOracleTransport.cs'),
                        ('originalDllSha256', 'SDK/tools/BinaryAssetBuilder.XmlCompiler.dll')]:
        if field == 'originalDllSha256':
            require(matrix.get(field) == revision_hash(repository, source, path), field + ': differs from recorded sourceCommit')
        else:
            candidates = revision_text_hashes(repository, source, path)
            require(matrix.get(field) in candidates, field + ': differs from recorded sourceCommit checkout bytes')
            checkout_forms.add(candidates[matrix[field]])
    require(matrix['originalDllSha256'] == ORIGINAL_XML_SHA, 'Original XML DLL differs from pinned oracle')
    expected = repository_groups(repository, source)
    expected_status = MATRIX_STATUS.replace('63', str(len(expected)))
    require(matrix.get('status') == expected_status, 'XML receipt status does not cover the recorded revision group inventory')
    inventory = indexed(matrix.get('groups'), 'group', expected, 'XML group inventory')
    for name, row in inventory.items():
        hashes(row.get('inputs'), name)
        require([i['path'] for i in row['inputs']] == [i['path'] for i in expected[name]], name + ': fixture input paths/order differ from recorded revision')
        for item in row['inputs']:
            require(set(item) == {'path', 'sha256'}, name + ': unexpected fixture input fields')
            candidates = revision_text_hashes(repository, source, item['path'])
            require(item['sha256'] in candidates, name + ': fixture input hash differs from recorded checkout bytes')
            checkout_forms.add(candidates[item['sha256']])

    def receipt(kind, mode):
        matches = []
        for path, row in records:
            selected = ((kind == 'product' and 'nativeBuild' in row and 'exeSha256' in row)
                        or (kind == 'host' and 'sameProductionObjects' in row)
                        or (kind == 'recipe' and path.name == 'link-inputs.json'))
            if selected and row.get('configuration') == mode:
                require(row.get('sourceCommit') == source, kind + ': sourceCommit drift')
                matches.append((path, row))
        require(matches, mode + ': missing ' + kind + ' receipt')
        canonical = {json.dumps(r, sort_keys=True) for _, r in matches}
        require(len(canonical) == 1, mode + ': divergent duplicate ' + kind + ' receipts')
        return matches[0], {digest(p) for p, _ in matches}

    modes = indexed(matrix.get('modes'), 'configuration', MODES, 'XML modes')
    summary = []
    for mode in MODES:
        (_, product), _ = receipt('product', mode)
        (_, host), _ = receipt('host', mode)
        (_, recipe), recipe_hashes = receipt('recipe', mode)
        require(product.get('schemaVersion') == 2 and host.get('schemaVersion') == 2
                and recipe.get('schemaVersion') == 1, mode + ': unsupported receipt schema')
        require(product.get('entryPoint') == 'NewModCompiler.Hosting.CompilerEntry.Main'
                and host.get('entryPoint') == 'NewModCompiler.Validation.ValidationEntry.Main'
                and host.get('productionEntryPoint') == product['entryPoint'], mode + ': entry identity drift')
        for label, value in [('product EXE', product.get('exeSha256')), ('host EXE', host.get('exeSha256'))]:
            require(isinstance(value, str) and SHA.fullmatch(value), mode + ': invalid ' + label + ' hash')
        require(host.get('sameProductionObjects') is True and host.get('productionExeUnchanged') is True
                and host.get('productExeSha256') == product['exeSha256'], mode + ': production/host EXE identity drift')
        require(recipe_hashes == {host.get('linkRecipeSha256')}, mode + ': link recipe bytes differ from host receipt')
        inputs = recipe.get('inputs')
        hashes(inputs, mode + ' link inputs')
        for kind, key in [('foundation', 'foundationSha256'), ('application', 'applicationSha256')]:
            values = [i['sha256'] for i in inputs if i.get('kind') == kind]
            require(values == [product.get(key)], mode + ': ' + kind + ' identity drift')
        require(len([i for i in inputs if i.get('kind') == 'entry']) == 1, mode + ': entry object inventory')
        require(host.get('inputs') == [i for i in inputs if i.get('kind') != 'entry'], mode + ': host link input inventory differs')
        require(isinstance(product.get('nativeBuild'), dict), mode + ': missing native build receipt')
        require([i['path'] for i in inputs if i.get('kind') == 'nativeOrBridge'] == product['nativeBuild'].get('objects'), mode + ': native/bridge inventory differs')
        require([i['path'] for i in inputs if i.get('kind') == 'resource'] == [r['path'] for r in product.get('resources', [])], mode + ': resource inventory differs')
        runtime = host.get('runtimeFiles')
        require(isinstance(runtime, list) and [r.get('name') for r in runtime] == ['NewModCompiler.exe.config', 'antlr.runtime.dll']
                and all(isinstance(r.get('sha256'), str) and SHA.fullmatch(r['sha256']) for r in runtime), mode + ': runtime identity inventory')
        runtime_names = ['NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll']
        if 'texturePlugin' in product:
            require(product['texturePlugin'] == {'implementation': 'built-in-source', 'externalRuntimeFiles': []}, mode + ': source texture implementation receipt differs')
        else:
            # Revalidation of historical accepted archives retains their DLL contract.
            runtime_names += ['BinaryAssetBuilder.TextureCompiler.dll', 'BinaryAssetBuilder.TextureCompiler.dll.patch.json']
        require(product.get('runtimeFiles') == runtime_names, mode + ': production runtime inventory differs')
        state = modes[mode]
        require(state.get('passed') is True and state.get('productExeSha256') == product['exeSha256']
                and state.get('validationHostSha256') == host['exeSha256'], mode + ': matrix EXE identity drift')
        groups = indexed(state.get('groups'), 'group', expected, mode + ' groups')
        for name, group in groups.items():
            require(group.get('passed') is True and isinstance(group.get('generatedHostSha256'), str)
                    and SHA.fullmatch(group['generatedHostSha256']), mode + ' ' + name + ': incomplete group')
            rows, calls, sha = check_raw(matrix_path.parent / mode / name, name)
            require(group.get('platforms') == rows and group.get('originalCalls') == calls
                    and group.get('transcriptSha256') == sha, mode + ' ' + name + ': raw evidence differs from receipt')
        summary.append(dict(configuration=mode, groups=len(groups), productExeSha256=product['exeSha256'], validationHostSha256=host['exeSha256']))
    return dict(sourceCommit=source, runId=matrix['runId'], runAttempt=matrix['runAttempt'], configurations=summary,
                status='P4 XML archived receipts and raw matrices consistent', sourceCheckoutHashForms=sorted(checkout_forms),
                limitations=['EXE/object/runtime binary bytes are not present in standard evidence and were not independently rehashed',
                             'Runtime hashes are only recorded in the validation-host receipt; archived runtime bytes are unavailable for independent comparison',
                             'This checks the recorded revision only; it does not prove evidence is reusable at HEAD',
                             'Receipt consistency cannot authenticate an artifact; verify its GitHub run and source revision separately',
                             'Other P4 components, XML joint smoke, SDK and G3 acceptance remain separate'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('evidence_root', type=pathlib.Path)
    parser.add_argument('--repository', type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[2])
    parser.add_argument('--source-commit')
    args = parser.parse_args()
    try:
        print(json.dumps(check(args.evidence_root, args.repository, args.source_commit), indent=2))
    except (ValueError, OSError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        print('XML evidence rejected: ' + str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
