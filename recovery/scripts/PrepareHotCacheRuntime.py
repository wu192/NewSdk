#!/usr/bin/env python3
"""Stage the pinned, accepted Release; never rebuild production for a benchmark."""
import argparse
import json
from pathlib import Path
import subprocess

from PrepareCompilerCacheBenchmark import validate_bundle, file_sha

ACCEPTANCE = 'recovery/evidence/refactor/audiofile-parallel/41068958541-1/summary.json'


def prepare(repo, output):
    repo, output = Path(repo).resolve(), Path(output).resolve()
    if output.exists():
        raise ValueError('Refusing to overwrite runtime preparation')
    accepted = json.loads((repo / ACCEPTANCE).read_text(encoding='utf-8'))
    source = accepted['binarySourceCommit']
    if accepted['status'] != 'passed' or not accepted['productionMatrix']['allPassed']:
        raise ValueError('Pinned production acceptance did not pass')
    subprocess.run(['git', '-C', str(repo), 'merge-base', '--is-ancestor', source, 'HEAD'], check=True)
    # Test/controller changes do not change the accepted production closure.
    subprocess.run(['git', '-C', str(repo), 'diff', '--exit-code', source, '--',
                    'recovery/src', 'SDK', 'recovery/build', 'recovery/probes',
                    'recovery/scripts/Build*', 'recovery/scripts/PrepareLegacyTexture.ps1'], check=True)
    output.mkdir(parents=True)
    archive = output / 'accepted.zip'
    with archive.open('wb') as stream:
        subprocess.run(['gh', 'api', 'repos/wu162/NewSdk/actions/artifacts/' +
                        str(accepted['artifact']['id']) + '/zip'], stdout=stream, check=True, timeout=90)
    files, identities = validate_bundle(archive, accepted['artifact']['sha256'], source,
                                         accepted['runId'], accepted['runAttempt'])
    runtime = output / 'runtime'
    runtime.mkdir()
    for name, data in files.items():
        (runtime / name).write_bytes(data)
    result = dict(binarySourceCommit=source, sourceRunId=accepted['runId'],
                  benchmarkSourceCommit=subprocess.check_output(
                      ['git', '-C', str(repo), 'rev-parse', 'HEAD'], text=True).strip(),
                  archiveSha256=file_sha(archive), runtimeFiles=identities,
                  runtimeDirectory=str(runtime), productionInputsUnchanged=True)
    (output / 'preparation.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    archive.unlink()
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=Path.cwd())
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.repo, args.output), indent=2))
