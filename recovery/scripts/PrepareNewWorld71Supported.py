#!/usr/bin/env python3
"""Prepare the user's supported-input benchmark variant without reserializing XML."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import shutil

from UpheavalEditWorkload import xml_tags

UNSUPPORTED_INSTANCES = {
    ('AudioFile', 'FX_kanade_spell'): 'Source WAVE extensible input has 8 channels; the accepted reader supports 1/2/4/6.',
    ('AudioEvent', 'FX_kanade_spell'): 'Unused event depends exclusively on the removed 8-channel AudioFile.',
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def prepare(mod, catalog, backup, output, unsupported_textures=None, preserve_media=False):
    if preserve_media and unsupported_textures is not None:
        raise ValueError('Preserved-media input cannot remove unsupported textures')
    excluded = {row['name'] for row in json.loads(catalog.read_text(encoding='utf-8'))['excluded']}
    previous = json.loads(output.read_text(encoding='utf-8')) if output.exists() else {}
    if previous and previous['modRoot'] != str(mod):
        raise ValueError('Adjustment receipt belongs to a different MOD directory')
    if previous and bool(previous.get('preserveMedia', False)) != preserve_media:
        raise ValueError('Adjustment receipt belongs to a different media preparation mode')
    removed = previous.get('removedAssets', [])
    xml_changes = previous.get('xmlChanges', [])
    tga_changes = previous.get('tgaHeaderNormalizations', [])
    bad_textures = {str((mod / row['path']).resolve()).lower(): row['reason']
                    for row in (json.loads(unsupported_textures.read_text(encoding='utf-8'))['unsupported']
                                if unsupported_textures else [])}
    lookup = {str(path.resolve()).lower(): path for path in mod.rglob('*') if path.is_file()} if bad_textures else {}
    def texture_reason(path, tag):
        if tag['topType'] not in ('Texture', 'OnDemandTexture') or not bad_textures:
            return None
        raw = tag['attributes'].get('File', b'').decode('utf-8').replace('\\', '/')
        if not raw:
            return None
        prefix, separator, target = raw.partition(':')
        if separator and prefix.lower() == 'art':
            candidate = mod / 'Art' / target[:2] / target if '/' not in target else mod / 'Art' / target
        elif separator and prefix.lower() == 'data':
            candidate = mod / 'Data' / target
        elif not separator:
            candidate = path.parent / raw
        else:
            return None
        key = str(candidate.resolve()).lower()
        return bad_textures.get(key) if key in lookup else None
    unsupported_instances = {} if preserve_media else UNSUPPORTED_INSTANCES
    candidate_types = excluded | {kind for kind, name in unsupported_instances}
    candidate_tags = re.compile(rb'<(?:[A-Za-z_][\w.-]*:)?(?:' +
                                b'|'.join(re.escape(name.encode('ascii')) for name in candidate_types) + rb')(?=[\s/>])')
    bad_names = [Path(path).name.encode('utf-8').lower() for path in bad_textures]
    documents = sorted(path for folder in ('Data', 'Art') for path in (mod / folder).rglob('*')
                       if path.suffix.lower() == '.xml')
    for path in documents:
        relative = path.relative_to(mod)
        if relative.parts[1].lower() in ('aptui', 'additionalmaps'):
            continue
        data = path.read_bytes()
        visible = re.sub(rb'<!--.*?-->|<!\[CDATA\[.*?\]\]>|<\?.*?\?>', b'', data, flags=re.S)
        if not candidate_tags.search(visible) and not any(name in visible.lower() for name in bad_names):
            continue
        spans = [tag for tag in xml_tags(data)
                 if 'elementEnd' in tag and tag['topId'] and
                 (tag['topType'] in excluded or (tag['topType'], tag['topId']) in unsupported_instances or texture_reason(path, tag))]
        if not spans:
            continue
        original = backup / relative
        original.parent.mkdir(parents=True, exist_ok=True)
        if not original.exists():
            shutil.copy2(path, original)
        changed = data
        for tag in reversed(spans):
            changed = changed[:tag['start']] + changed[tag['elementEnd']:]
        path.write_bytes(changed)
        xml_changes.append(dict(path=relative.as_posix(), originalSha256=sha(data),
                                modifiedSha256=sha(changed), backup=str(original)))
        removed.extend(dict(path=relative.as_posix(), type=tag['topType'], id=tag['topId'],
            reason=unsupported_instances.get((tag['topType'], tag['topId'])) or texture_reason(path, tag) or 'Excluded built-in XML compiler type') for tag in spans)
    tga_paths = () if preserve_media else sorted((mod / 'Data/AptUI').rglob('*'))
    for path in tga_paths:
        if path.suffix.lower() != '.tga':
            continue
        with path.open('rb') as stream:
            header = stream.read(18)
        if len(header) < 18 or header[1] != 0 or header[2] not in (2, 10) or not any(header[3:8]):
            continue
        data = path.read_bytes()
        changed = data[:3] + bytes(5) + data[8:]
        if data[8:] != changed[8:] or data[:3] != changed[:3]:
            raise ValueError('TGA normalization touched bytes outside the unused color-map specification')
        path.write_bytes(changed)
        tga_changes.append(dict(path=path.relative_to(mod).as_posix(),
            originalHeader=header.hex(), modifiedHeader=changed[:18].hex(),
            originalSha256=sha(data), modifiedSha256=sha(changed),
            unchangedPixelPayloadSha256=sha(data[18 + header[0]:])))
    result = dict(schemaVersion=1, modRoot=str(mod),
        scope=('Only excluded XML types removed; all base/APT/global entries retained. AudioFile, AudioEvent and Texture declarations and all raw media bytes preserved; no TGA header normalization.'
               if preserve_media else
               'User-authorized base unsupported declarations removed. All base/APT/global entries remain selected. Unused TGA color-map specification bytes normalized for both engines; pixel/encoded payload bytes unchanged.'),
        excludedTypes=sorted(excluded), removedAssets=removed, xmlChanges=xml_changes,
        tgaHeaderNormalizations=tga_changes, removedAssetCount=len(removed),
        normalizedTgaCount=len(tga_changes), originalArchivePreserved=True,
        preserveMedia=preserve_media, rawMediaPreserved=preserve_media)
    if unsupported_textures:
        result['baseTextureInputProbe'] = json.loads(unsupported_textures.read_text(encoding='utf-8'))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: result[key] for key in ('removedAssetCount', 'normalizedTgaCount', 'scope')}, ensure_ascii=False))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mod', type=Path, required=True)
    parser.add_argument('--catalog', type=Path, default=Path(__file__).resolve().parents[1] / 'catalog/XmlSdkTypes.json')
    parser.add_argument('--backup', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--unsupported-textures', type=Path)
    parser.add_argument('--preserve-media', action='store_true',
                        help='Remove only excluded XML types; preserve all audio/texture declarations and raw media')
    args = parser.parse_args()
    if args.preserve_media and args.unsupported_textures is not None:
        parser.error('--preserve-media cannot be combined with --unsupported-textures')
    prepare(args.mod.resolve(), args.catalog.resolve(), args.backup.resolve(), args.output.resolve(), args.unsupported_textures, args.preserve_media)


if __name__ == '__main__':
    main()
