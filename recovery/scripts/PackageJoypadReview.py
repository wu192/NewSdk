"""Copy only accepted Joypad runtime ZIPs and their review metadata."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import zipfile


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def need(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key in ('products', 'packages', 'evidence', 'output', 'source-commit'):
        parser.add_argument('--' + key, required=True)
    args = parser.parse_args()
    products, packages, evidence, output = (Path(value).resolve() for value in
        (args.products, args.packages, args.evidence, args.output))
    output.mkdir(parents=True, exist_ok=False)
    core = read(evidence / 'component/validation.json')
    need(core['succeeded'] and core['sourceCommit'] == args.source_commit and len(core['modes']) == 2, 'Core source/configurations differ')
    for mode in core['modes']:
        need(mode['passed'], 'A targeted Core fixture failed')
        matrix = read(evidence / 'component' / mode['configuration'] / 'CompilerXmlBackendBatch/result.json')
        need(matrix['succeeded'] and len(matrix['matrices']) == 4, 'Four-platform worker matrix incomplete')
        for platform in matrix['matrices']:
            need(platform['certifiedRoutes'] == 295 and platform['joypadComplexInputs'] == 89 and len(platform['retainedOwners']) == 5, 'Joypad worker/owner matrix differs')
    for name, packaged in (('production', False), ('packaged', True)):
        result = read(evidence / name / 'validation.json')
        need(result['succeeded'] and result['sourceCommit'] == args.source_commit and result['usePackagedRuntime'] is packaged, 'Joypad CLI acceptance source differs')
        need(result['assets'] == 45 and len(result['rootNames']) == 11 and len(result['schemaDataChoices']) == 29 and len(result['samples']) == 18, 'Small Joypad production matrix incomplete')
        need(all(item['passed'] for item in result['samples']), 'A Joypad CLI sample failed')
        overlap = [item for item in result['samples'] if item['label'] in ('uncached-4', 'cache-cold')]
        need(len(overlap) == 4 and all(item['joypadMaximumConcurrency'] > 1 for item in overlap), 'Real Joypad worker overlap missing')
    original = read(evidence / 'original-baseline/validation.json')
    need(original['succeeded'], 'Small original SDK byte differential failed')
    acceptance = read(evidence / 'cli/validation.json')
    need(acceptance['succeeded'] and len(acceptance['modes']) == 2 and all(mode['passed'] for mode in acceptance['modes']), 'Production CLI/cancellation acceptance failed')
    release = read(packages / 'release.json')
    need(release['succeeded'] and release['binarySourceCommit'] == args.source_commit and len(release['configurations']) == 2, 'Independent ZIP deployment did not pass')
    allowed = {'NewModCompiler.exe', 'NewModCompiler.exe.config', 'antlr.runtime.dll'}
    records = []
    for mode in ('Debug', 'Release'):
        stem = 'NewModCompiler-Win32-' + mode
        archive = packages / (stem + '.zip')
        manifest_path = packages / (stem + '.manifest.json')
        manifest = read(manifest_path)
        build = read(products / mode / 'build.json')
        need(manifest['binarySourceCommit'] == args.source_commit and build['sourceCommit'] == args.source_commit, 'Runtime source commit differs')
        need(digest(archive.read_bytes()) == manifest['archive']['sha256'], 'Runtime ZIP digest differs')
        need(len(manifest['files']) == 3 and {item['name'] for item in manifest['files']} == allowed, 'Manifest file inventory differs')
        with zipfile.ZipFile(archive) as runtime:
            need(len(runtime.infolist()) == 3 and set(runtime.namelist()) == allowed and runtime.testzip() is None, 'Runtime ZIP must contain exactly three valid files')
            for item in manifest['files']:
                data = runtime.read(item['name'])
                need(len(data) == item['bytes'] and digest(data) == item['sha256'], 'Runtime ZIP member digest differs')
                need(data == (products / mode / 'bin' / item['name']).read_bytes(), 'Packaged bytes differ from production')
                if item['name'] == 'NewModCompiler.exe':
                    need(item['sha256'] == build['exeSha256'] and build['containsProbeOrTestSources'] is False, 'Production EXE receipt differs')
        shutil.copyfile(archive, output / archive.name)
        shutil.copyfile(manifest_path, output / manifest_path.name)
        records.append(dict(configuration=mode, archive=manifest['archive'], files=manifest['files'], toolchain=manifest['toolchain']))
    metadata = dict(schemaVersion=1, repository='wu162/NewSdk', binarySourceCommit=args.source_commit,
                    sourceBranch=os.environ.get('GITHUB_REF_NAME'), runId=os.environ.get('GITHUB_RUN_ID'),
                    binaryBuildRunId=os.environ.get('BINARY_BUILD_RUN', os.environ.get('GITHUB_RUN_ID')),
                    componentEvidenceRunId=core['runId'],
                    cliEvidenceRunId=acceptance['runId'],
                    testSourceCommit=os.environ.get('COMPILER_TEST_SOURCE_COMMIT', args.source_commit),
                    publicationStatus='awaiting-user-confirmation; no release or tag created',
                    scope='Joypad parallelism: 11 roots, 34 runtime Data branches in the component matrix, 29 schema choices in a 45-asset MOD. Actual worker overlap, jobs1/4/auto, hot-cache output changes, single-asset edit, original SDK and extracted runtime parity.',
                    largeModCompilationPerformed=False, fullSdkCompilationPerformed=False,
                    knownLimits='No new large MOD, complete mymod, game runtime or performance benchmark acceptance; unchanged native layout/parser evidence retains its original source/run provenance.',
                    checks=dict(componentPlatforms=[0, 1, 2, 99], certifiedMetadataRoutes=295, complexInputsPerPlatform=89,
                                originalSdkSmallMod=True, productionCliCancellation=True, packagedJoypadWorkers=True,
                                rawBytesEqual=True, archiveCrc=True, archiveAndFileSha256=True, sourceFilesIncluded=False),
                    packages=records)
    (output / 'release-metadata.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (output / 'USAGE.zh-CN.md').write_text('''# NewModCompiler Joypad 并行待确认包

普通使用请选择 NewModCompiler-Win32-Release.zip；Debug 包供调试。
完整解压三个运行文件至同一目录：NewModCompiler.exe、NewModCompiler.exe.config、antlr.runtime.dll。

要求 Windows x86 兼容环境、.NET Framework 4.8 及与清单工具链匹配的 x86 VC 运行库。SDK 与 MOD 单独准备。

```powershell
./NewModCompiler.exe doctor --sdk-root "C:\\NewSdk\\SDK"
./NewModCompiler.exe build --sdk-root "C:\\NewSdk\\SDK" --mod "C:\\Mods\\Example" --out "C:\\Build\\Example" --mod-version 1.0 --jobs auto
```

11 个 Joypad 根已加入现有并行调度；资源和内存预算仍生效，提交与诊断由 owner 处理。版本和缓存身份未更换。
本轮仅使用小型 Joypad MOD 和定向测试，没有大型 MOD/全量 SDK 编译，不作为新性能或游戏内验收结论。
SHA256SUMS.txt 用于附件校验，manifest 和 release-metadata.json 记录二进制来源与范围。
该包不含源码，尚未发布，需用户明确确认后才能发布 GitHub Release。
''', encoding='utf-8')
    files = sorted(output.iterdir())
    need(len(files) == 6, 'Unexpected review files')
    (output / 'SHA256SUMS.txt').write_text(''.join(digest(path.read_bytes()) + '  ' + path.name + '\n' for path in files), encoding='utf-8')
    print(json.dumps(metadata, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
