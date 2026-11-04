#!/usr/bin/env python3
"""Download the pinned ModDB source archive and extract it under SDK/Mods."""
import argparse
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import subprocess
import urllib.request

PAGE = 'https://www.moddb.com/mods/red-alert-the-new-world/downloads/the-new-world-71-source-code'
SIZE = 982990565
MD5 = '7baeedd038574044e977c6495564665a'


def checksums(path):
    hashes = [hashlib.md5(), hashlib.sha256()]
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            for digest in hashes:
                digest.update(chunk)
    return [digest.hexdigest() for digest in hashes]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk-root', type=Path, required=True)
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source-url', help='Fresh direct mirror redirect when ModDB blocks the runner IP')
    parser.add_argument('--sevenzip', default=shutil.which('7z') or '7z')
    args = parser.parse_args()
    archive = args.archive.resolve()
    archive.parent.mkdir(parents=True, exist_ok=True)
    if not archive.is_file():
        if args.source_url:
            mirrors = [args.source_url]
        else:
            request = urllib.request.Request('https://www.moddb.com/downloads/start/240820/all',
                                             headers={'User-Agent': 'curl/8.7.1'})
            with urllib.request.urlopen(request, timeout=60) as response:
                page = response.read().decode('utf-8')
            mirrors = list(dict.fromkeys('https://www.moddb.com' + html.unescape(s) for s in
                re.findall(r'[/]downloads/mirror/240820/[^"\s<>]+', page)))
        if not mirrors:
            raise ValueError('ModDB did not provide any source mirrors')
        errors = []
        for mirror in mirrors:
            try:
                request = urllib.request.Request(mirror,
                                                 headers={'User-Agent': 'curl/8.7.1'})
                with urllib.request.urlopen(request, timeout=120) as response, archive.open('wb') as stream:
                    shutil.copyfileobj(response, stream, 1024 * 1024)
                if archive.stat().st_size != SIZE or checksums(archive)[0] != MD5:
                    raise ValueError('Downloaded bytes do not match the published source archive')
                break
            except Exception as error:
                archive.unlink(missing_ok=True)
                errors.append(type(error).__name__ + ': ' + str(error))
        else:
            raise RuntimeError('All ModDB mirrors failed: ' + '; '.join(errors))
    md5, sha256 = checksums(archive)
    if archive.stat().st_size != SIZE or md5 != MD5:
        raise ValueError('Existing archive does not match the published source archive')
    mods = args.sdk_root.resolve() / 'Mods'
    mod = mods / 'The New World'
    if mod.exists():
        raise FileExistsError('Refusing to replace an existing MOD: ' + str(mod))
    mods.mkdir(parents=True, exist_ok=True)
    subprocess.run([args.sevenzip, 'x', str(archive), '-o' + str(mods), '-y', '-bsp0'], check=True)
    if not (mod / 'Data/Mod.xml').is_file():
        raise ValueError('Archive did not contain the expected MOD entry')
    files = [p for p in mod.rglob('*') if p.is_file()]
    receipt = dict(source=PAGE, filename=archive.name, bytes=archive.stat().st_size,
        md5=md5, sha256=sha256, modRoot=str(mod), files=len(files),
        unpackedBytes=sum(p.stat().st_size for p in files), entry='Data/Mod.xml',
        scope='Full original source tree, including Art, Audio, Data, Additional and supplied xsd')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(receipt, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
