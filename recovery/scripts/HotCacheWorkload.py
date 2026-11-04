#!/usr/bin/env python3
"""Pinned full Upheaval plus reachable, independent L8 benchmark partitions.

Only benchmark work directories are written. No compile or artificial delay is
performed here; the controller must calibrate actual fresh-process hot builds.
"""
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tarfile
import wave
import xml.etree.ElementTree as ET
import zlib

from CompilerCachePaths import cache_root

NS = 'uri:ea.com:eala:asset'
XI = 'http://www.w3.org/2001/XInclude'
HISTORICAL = 'bacfc69fc3e71c8116d1f22b933b8b6173170ecf'
UPHEAVAL_TREE = '5551fc26a6f96b62613443e0d0fa2958c14cbce0'
ET.register_namespace('', NS)
ET.register_namespace('xi', XI)


def need(value, message):
    if not value:
        raise ValueError(message)


def element(local_name, **attributes):
    return ET.Element('{%s}%s' % (NS, local_name), attributes)


def save_xml(path, root):
    path.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(root).write(path, encoding='utf-8', xml_declaration=True)


def declaration(*nodes):
    root = element('AssetDeclaration')
    root.extend(nodes)
    return root


def includes(root):
    found = root.find('{%s}Includes' % NS)
    if found is None:
        found = element('Includes')
        root.insert(0, found)
    return found


def add_include(root, source, kind='all'):
    includes(root).append(element('Include', type=kind, source=source))


def armor(identifier, **attributes):
    return element('ArmorTemplate', id=identifier, Default='72.5%', **attributes)


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def instance_hash(identifier):
    """ASCII InstanceId hash from EALA.Hash.FastHash, with UInt32 arithmetic."""
    data = identifier.lower().encode('ascii')
    value, offset = len(data), 0
    mask = 0xFFFFFFFF
    while offset + 4 <= len(data):
        value = (value + int.from_bytes(data[offset:offset + 2], 'little')) & mask
        value ^= ((int.from_bytes(data[offset + 2:offset + 4], 'little') ^
                   ((value << 5) & mask)) << 11) & mask
        offset += 4
        value = (value + (value >> 11)) & mask
    remaining = len(data) - offset
    if remaining == 3:
        value = (value + int.from_bytes(data[offset:offset + 2], 'little')) & mask
        value ^= (((data[offset + 2] << 2) ^ value) << 16) & mask
        value = (value + (value >> 11)) & mask
    elif remaining == 2:
        value = (value + int.from_bytes(data[offset:offset + 2], 'little')) & mask
        value ^= (value << 11) & mask
        value = (value + (value >> 17)) & mask
    elif remaining == 1:
        value = (value + data[offset]) & mask
        value ^= (value << 10) & mask
        value = (value + (value >> 1)) & mask
    value ^= (value << 3) & mask
    value = (value + (value >> 5)) & mask
    value ^= (value << 2) & mask
    value = (value + (value >> 15)) & mask
    return (value ^ (value << 10)) & mask


def allocate_id(logical, occupied):
    salt = 0
    while True:
        suffix = hashlib.sha256((logical + ':' + str(salt)).encode('ascii')).hexdigest()[:16]
        candidate = logical + '_' + suffix
        value = instance_hash(candidate)
        if value not in occupied:
            occupied[value] = candidate
            return candidate
        salt += 1


def reserved_instance_hashes(repo, mod, exclude=()):
    excluded = {path.resolve() for path in exclude}
    occupied = {}
    for base in (repo / 'SDK', mod):
        for path in base.rglob('*'):
            if path.suffix.lower() not in ('.xml', '.w3x') or path.resolve() in excluded:
                continue
            try:
                root = ET.parse(path).getroot()
            except (ET.ParseError, OSError):
                continue
            if root.tag != '{%s}AssetDeclaration' % NS:
                continue
            for node in root:
                identifier = node.get('id')
                if identifier and identifier.isascii():
                    occupied[instance_hash(identifier)] = identifier
    return occupied


def protect_generated_ids(repo, mod):
    """Allocate collision-free native InstanceIds and rewrite their references.

    String uniqueness alone is insufficient for the legacy 32-bit FastHash:
    sequential scale ids can collide systematically. Reserve existing source
    declarations, then allocate deterministic salted names for this load and
    its later added/renamed assets before starting any compiler process.
    """
    generated = list((mod / 'Data/L8').glob('*.xml')) + list((mod / 'Data/L8Shadow').glob('*.xml'))
    occupied = reserved_instance_hashes(repo, mod, generated)
    roots = {path: ET.parse(path).getroot() for path in generated}
    mapping = {}
    for root in roots.values():
        for node in root:
            identifier = node.get('id')
            if identifier:
                need(identifier not in mapping, 'Duplicate logical L8 asset id: ' + identifier)
                mapping[identifier] = allocate_id(identifier, occupied)
    # Stream-specific fixture declarations are also generated and referenced
    # only from their stream entry includes, so they are in this same map.
    for path, root in roots.items():
        for node in root.iter():
            for key, value in list(node.attrib.items()):
                if value in mapping:
                    node.set(key, mapping[value])
        save_xml(path, root)
    future = {}
    for doc in range(8):
        for i in range(4):
            logical = 'L8_Added_%02d_%02d' % (doc, i)
            future[logical] = allocate_id(logical, occupied)
    for doc in range(4):
        for i in range(8):
            logical = 'L8_Spread_%02d_%02d' % (doc, i)
            future[mapping[logical]] = allocate_id(logical + '_Renamed', occupied)
    return {'algorithm': 'Native EALA.Hash.FastHash ASCII case-insensitive InstanceId',
            'generatedIds': len(mapping), 'reservedHashes': len(occupied),
            'mappingFingerprint': hashlib.sha256(json.dumps(mapping, sort_keys=True).encode()).hexdigest(),
            'futureIds': future}


def inventory(mod):
    rows = []
    assets = documents = 0
    for path in sorted(mod.rglob('*')):
        if not path.is_file():
            continue
        rows.append({'path': path.relative_to(mod).as_posix(), 'bytes': path.stat().st_size,
                     'sha256': sha(path)})
        if path.suffix.lower() in ('.xml', '.w3x'):
            try:
                root = ET.parse(path).getroot()
            except ET.ParseError:
                continue
            if root.tag == '{%s}AssetDeclaration' % NS:
                documents += 1
                assets += sum(node.get('id') is not None for node in root)
    return {'fingerprint': hashlib.sha256(json.dumps(rows, sort_keys=True).encode()).hexdigest(),
            'documents': documents, 'assets': assets,
            'inputBytes': sum(row['bytes'] for row in rows), 'inputFiles': len(rows)}


def extract_upheaval(repo, work):
    tree = subprocess.check_output(['git', '-C', str(repo), 'rev-parse',
                                    HISTORICAL + ':SDK/Mods/Upheaval'], text=True).strip()
    need(tree == UPHEAVAL_TREE, 'Pinned historical Upheaval tree differs')
    archive = work / 'upheaval.tar'
    with archive.open('wb') as stream:
        subprocess.run(['git', '-C', str(repo), 'archive', HISTORICAL,
                        'SDK/Mods/Upheaval'], stdout=stream, check=True, timeout=120)
    extracted = work / 'historical'
    extracted.mkdir()
    with tarfile.open(archive) as stream:
        stream.extractall(extracted, filter='data')
    archive.unlink()
    return extracted / 'SDK/Mods/Upheaval'


def dds():
    # Uncompressed 32-bit BGRA DDS, no mipmaps. Header is DDS_HEADER (124 bytes).
    header = [124, 0x100F, 64, 64, 256, 0, 0] + [0] * 11
    header += [32, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000]
    header += [0x1000, 0, 0, 0, 0]
    return b'DDS ' + struct.pack('<31I', *header) + bytes((30, 60, 90, 255)) * 4096


def write_wave(path):
    with wave.open(str(path), 'wb') as output:
        output.setparams((2, 2, 22050, 0, 'NONE', 'not compressed'))
        output.writeframes(struct.pack('<hhhh', 0, 1234, -1234, 32767) * 8192)


def stream_entries(mod):
    values = [('base/Mod', mod / 'Data/Mod.xml')]
    values += [('apt/' + p.stem, p) for p in sorted((mod / 'Data/AptUI').glob('*.xml'))]
    values += [('maps/' + p.stem, p) for p in sorted((mod / 'Data/AdditionalMaps').glob('MapMetaData_*.xml'))]
    return values


def prepare(repo, work, partitions):
    """Return serializable workload metadata; ``work`` must be new/empty.

    Partitions is a scale parameter, never evidence that the 8-second gate has
    been met. The unchanged original inventory is captured before augmentation.
    """
    repo, work = Path(repo).resolve(), Path(work).resolve()
    need(type(partitions) is int and partitions > 0, 'partitions must be positive')
    need(not work.exists() or not any(work.iterdir()), 'Workload work directory is not empty')
    work.mkdir(parents=True, exist_ok=True)
    mod = extract_upheaval(repo, work)
    original = inventory(mod)
    original.update(sourceCommit=HISTORICAL, sourceTree=UPHEAVAL_TREE,
                    scope='Unmodified complete original Upheaval before augmentation')
    main = ET.parse(mod / 'Data/Mod.xml').getroot()
    need(not any(node.get('id', '').startswith('L8_') for node in main), 'L8 prefix collision')
    index = declaration()
    generated = mod / 'Data/L8'
    generated.mkdir()
    for partition in range(partitions):
        # Every filler document is Include-all reachable; ids do not collide
        # across partitions. No unused files are counted as expanded load.
        for doc in range(256):
            name = 'scale-%03d-%03d.xml' % (partition, doc)
            save_xml(generated / name, declaration(*(armor('L8_Scale_%03d_%03d_%02d' %
                     (partition, doc, i)) for i in range(32))))
            add_include(index, name)
    for doc in range(16):
        name = 'spread-%02d.xml' % doc
        save_xml(generated / name, declaration(*(armor('L8_Spread_%02d_%02d' % (doc, i))
                                               for i in range(8))))
        add_include(index, name)
    save_xml(generated / 'dense.xml', declaration(*(armor('L8_Dense_%03d' % i) for i in range(256))))
    add_include(index, 'dense.xml')

    image = struct.pack('<BBBHHBHHHHBB', 0, 0, 2, 0, 0, 0, 0, 0, 64, 64, 32, 0x28)
    materials = generated / 'materials'
    materials.mkdir()
    for kind, count, extension in [('texture', 96, 'dds'), ('audio', 64, 'wav')]:
        names = []
        for i in range(count):
            name = '%s-%03d.%s' % (kind, i, extension)
            target = materials / name
            if kind == 'texture':
                target.write_bytes(dds())
            else:
                write_wave(target)
            names.append(name)
        shutil.copy2(materials / names[0], materials / ('shared.' + extension))
    # Common Texture edits use independently referenced TGA; Full adds DDS
    # independent/shared references, avoiding a mislabeled TGA-only DDS test.
    for i in range(32):
        (materials / ('common-%03d.tga' % i)).write_bytes(image + bytes((30, 60, 90, 255)) * 4096)
    for doc in range(8):
        root = declaration()
        for i in range(doc * 4, doc * 4 + 4):
            root.append(element('Texture', id='L8_CommonTexture_%03d' % i,
                                File='materials/common-%03d.tga' % i,
                                Type='StandardTexture', GenerateMipMaps='false',
                                OutputFormat='A8R8G8B8', AllowAutomaticResize='false'))
        save_xml(generated / ('textures-%02d.xml' % doc), root)
        add_include(index, 'textures-%02d.xml' % doc)
    for shared in (False, True):
        root = declaration()
        for i in range(96):
            root.append(element('Texture', id='L8_Dds_%s_%03d' % (shared, i),
                                File='materials/' + ('shared.dds' if shared else 'texture-%03d.dds' % i),
                                Type='StandardTexture', GenerateMipMaps='false', OutputFormat='A8R8G8B8',
                                AllowAutomaticResize='false'))
        name = 'dds-%s.xml' % ('shared' if shared else 'independent')
        save_xml(generated / name, root)
        add_include(index, name)
    for shared in (False, True):
        root = declaration()
        for i in range(64):
            root.append(element('AudioFile', id='L8_Audio_%s_%03d' % (shared, i),
                                File='materials/' + ('shared.wav' if shared else 'audio-%03d.wav' % i),
                                PCCompression='XAS' if i % 4 == 0 or shared else 'NONE',
                                IsStreamedOnPC='false'))
        name = 'audio-%s.xml' % ('shared' if shared else 'independent')
        save_xml(generated / name, root)
        add_include(index, name)

    hierarchy = ET.parse(repo / 'SDK/Mods/W3xRecovery/Data/Dependencies.xml').getroot().find('{%s}W3DHierarchy' % NS)
    animation = ET.parse(repo / 'SDK/Mods/W3xRecovery/Data/Animations.xml').getroot().find('{%s}W3DAnimation' % NS)
    need(hierarchy is not None and animation is not None and
         hierarchy.get('id') == animation.get('Hierarchy'), 'Existing W3X closure differs')
    hierarchy = copy.deepcopy(hierarchy)
    hierarchy.set('id', 'L8_Hierarchy')
    save_xml(generated / 'hierarchy.xml', declaration(hierarchy))
    add_include(index, 'hierarchy.xml')
    for doc in range(8):
        root = declaration()
        add_include(root, 'hierarchy.xml')
        for i in range(doc * 4, doc * 4 + 4):
            node = copy.deepcopy(animation)
            node.set('id', 'L8_Animation_%03d' % i)
            node.set('Hierarchy', 'L8_Hierarchy')
            root.append(node)
        name = 'animations-%02d.xml' % doc
        save_xml(generated / name, root)
        add_include(index, name)

    for doc in range(16):
        base_id = 'L8_Parent_%02d' % doc
        parent = declaration(armor(base_id), *(armor('L8_Sibling_%02d_%02d' % (doc, i)) for i in range(4)))
        parent_name, child_name = 'parent-%02d.xml' % doc, 'children-%02d.xml' % doc
        save_xml(generated / parent_name, parent)
        child = declaration()
        add_include(child, parent_name, 'instance')
        for i in range(8):
            # Do not override Default: changing the parent really changes each
            # descendant's expanded input rather than only its source file.
            child.append(element('ArmorTemplate', id='L8_Child_%02d_%02d' % (doc, i), inheritFrom=base_id))
        save_xml(generated / child_name, child)
        add_include(index, parent_name)
        add_include(index, child_name)

    # A separate four-edge inheritance chain keeps Full depth comparisons
    # independent from Common's one-edge parent/sibling partition. Every level
    # is a real included asset and no descendant overrides the changed field.
    for branch in range(8):
        previous_id = 'L8_Depth_Root_%02d' % branch
        previous_name = 'depth-root-%02d.xml' % branch
        save_xml(generated / previous_name, declaration(armor(previous_id)))
        add_include(index, previous_name)
        for level in range(1, 4):
            current_id = 'L8_Depth_Level_%02d_%d' % (branch, level)
            current_name = 'depth-level-%02d-%d.xml' % (branch, level)
            root = declaration()
            add_include(root, previous_name, 'instance')
            root.append(element('ArmorTemplate', id=current_id, inheritFrom=previous_id))
            save_xml(generated / current_name, root)
            add_include(index, current_name)
            previous_id, previous_name = current_id, current_name
        root = declaration()
        add_include(root, previous_name, 'instance')
        for leaf in range(16):
            root.append(element('ArmorTemplate', id='L8_Depth_Leaf_%02d_%02d' % (branch, leaf),
                                inheritFrom=previous_id))
        name = 'depth-leaves-%02d.xml' % branch
        save_xml(generated / name, root)
        add_include(index, name)

    define = declaration(element('Defines'))
    define[0].append(element('Define', name='L8_SCALE', value='83.25'))
    save_xml(generated / 'defines.xml', define)
    # XInclude contributes Include directives, not Define declarations: directly
    # injecting a Define into every host makes its global source identity differ
    # per host. Ordinary Includes preserve one shared defining document.
    xi_defines = declaration(element('Defines'))
    xi_defines[0].append(element('Define', name='L8_XI_SCALE', value='74.25'))
    save_xml(generated / 'xi-defines-a.xml', xi_defines)
    fragment = element('Includes')
    fragment.append(element('Include', type='all', source='defines.xml'))
    fragment.append(element('Include', type='all', source='xi-defines-a.xml'))
    save_xml(generated / 'fragment.xml', fragment)
    for doc in range(32):
        root = declaration()
        root.append(ET.Element('{%s}include' % XI, {'href': 'fragment.xml'}))
        for i in range(4):
            root.append(armor('L8_Define_%02d_%02d' % (doc, i), DamageScalar='=$L8_SCALE + $L8_XI_SCALE'))
        name = 'defines-host-%02d.xml' % doc
        save_xml(generated / name, root)
        add_include(index, name)

    # A wider instance-include graph provides a separate frontend path test.
    width_index = declaration()
    for doc in range(32):
        name = 'include-parent-%02d.xml' % doc
        save_xml(generated / name, declaration(armor('L8_IncludeParent_%02d' % doc)))
        add_include(width_index, name)
    for doc in range(64):
        root = declaration()
        add_include(root, 'include-parent-%02d.xml' % (doc % 32), 'instance')
        for i in range(2):
            root.append(element('ArmorTemplate', id='L8_Include_%02d_%02d' % (doc, i),
                                inheritFrom='L8_IncludeParent_%02d' % (doc % 32)))
        name = 'include-host-%02d.xml' % doc
        save_xml(generated / name, root)
        add_include(width_index, name)
    save_xml(generated / 'include-wide.xml', width_index)
    add_include(index, 'include-wide.xml')
    # DATA:-root resolution is deliberate; the shadow case introduces another
    # search root ahead of Data only after the hot-before sample.
    for doc in range(16):
        save_xml(mod / 'Data/L8Shadow' / ('leaf-%02d.xml' % doc), declaration(*(
            armor('L8_Shadow_%02d_%02d' % (doc, i)) for i in range(4))))
        add_include(index, 'DATA:L8Shadow/leaf-%02d.xml' % doc)
    save_xml(generated / 'index.xml', index)
    add_include(main, 'L8/index.xml')
    save_xml(mod / 'Data/Mod.xml', main)

    entries = stream_entries(mod)
    stream_fixtures = []
    for stream_index, (stream_id, entry) in enumerate(entries):
        # Keep the original stream entries and data, append legal independent
        # Armor declarations through a relative include into every real stream.
        target = generated / ('stream-%02d.xml' % stream_index)
        save_xml(target, declaration(*(armor('L8_Stream_%02d_%02d' % (stream_index, i)) for i in range(32))))
        root = ET.parse(entry).getroot()
        add_include(root, os.path.relpath(target, entry.parent).replace(os.sep, '/'))
        save_xml(entry, root)
        stream_fixtures.append({'streamId': stream_id, 'path': target.relative_to(mod).as_posix(), 'assets': 32})
    identifiers = protect_generated_ids(repo, mod)
    dimensions = inventory(mod)
    result = dict(modRoot=str(mod), sdkRoot=str(repo / 'SDK'), partitions=partitions, originalUpheaval=original,
                  sourceCommit=HISTORICAL, sourceTree=UPHEAVAL_TREE,
                  scope='Complete real Upheaval + reachable independent expanded mixed benchmark load; not unmodified original Upheaval',
                  streams=[row[0] for row in entries], streamCount=len(entries),
                  streamBasis='Entry discovery matching Planner; actual build plan/report must confirm',
                  streamFixtures=stream_fixtures, instanceIds=identifiers, **dimensions)
    result['cases'] = cases(result)
    return result


def expand(workload, partitions):
    """Append scale partitions without touching existing sources/cache paths.

    Rewarming only the genuinely new assets avoids repeatedly paying for the
    unchanged full Upheaval cold compile during bounded calibration. Callers
    must still run a warmup and two real hot processes after every expansion.
    """
    need(type(partitions) is int and partitions > workload['partitions'],
         'Expansion must increase the integer partition count')
    mod, repo = Path(workload['modRoot']), Path(workload['sdkRoot']).parent
    data = mod / 'Data/L8'
    occupied = reserved_instance_hashes(repo, mod)
    for identifier in workload['instanceIds']['futureIds'].values():
        occupied[instance_hash(identifier)] = identifier
    root = ET.parse(data / 'index.xml').getroot()
    mapping = {}
    for partition in range(workload['partitions'], partitions):
        for doc in range(256):
            name = 'scale-%03d-%03d.xml' % (partition, doc)
            nodes = []
            for i in range(32):
                logical = 'L8_Scale_%03d_%03d_%02d' % (partition, doc, i)
                actual = allocate_id(logical, occupied)
                mapping[logical] = actual
                nodes.append(armor(actual))
            need(not (data / name).exists(), 'Expansion would overwrite existing source')
            save_xml(data / name, declaration(*nodes))
            add_include(root, name)
    save_xml(data / 'index.xml', root)
    workload['partitions'] = partitions
    workload['instanceIds']['generatedIds'] += len(mapping)
    workload['instanceIds']['reservedHashes'] = len(occupied)
    workload['instanceIds']['mappingFingerprint'] = hashlib.sha256((
        workload['instanceIds']['mappingFingerprint'] + json.dumps(mapping, sort_keys=True)).encode()).hexdigest()
    workload.update(inventory(mod))
    workload['cases'] = cases(workload)
    return workload


def cases(workload):
    def case(identifier, name, group='Common', expected='backend', assets=0, docs=0, materials=0, **other):
        return dict(id=identifier, family=identifier.split('-')[0], name=name, group=group,
                    expectedPath=expected, changedAssets=assets, changedDocuments=docs,
                    changedMaterials=materials, **other)
    rows = [
        case('C00', '完全无修改热态', expected='hot'),
        case('C01', '分散玩法 XML 批量修改', assets=128, docs=16, minInstanceCompile=128),
        case('C02', '密集 XML 等量修改', assets=128, docs=1, minInstanceCompile=128),
        case('C03', '新增资产与 Include', assets=32, docs=8, minInstanceCompile=32),
        case('C04', '32 个独立 TGA 输入更新', assets=32, docs=8, materials=32, minInstanceCompile=32),
        case('C05', '32 个独立音频输入更新（含 XAS）', assets=32, docs=1, materials=32, minInstanceCompile=32),
        case('C06', '32 个 W3DAnimation 更新', assets=32, docs=8, minInstanceCompile=32),
        case('C07', '16 个有效继承父字段影响 128 后代', assets=16, docs=16, affectedAssets=128, minInstanceCompile=128),
        case('C08', 'instance 父文件的 64 无关兄弟更新', expected='frontend', assets=64, docs=16,
             minXmlParse=16, maxInstanceCompile=64),
        case('C09', '共享 Define + XInclude 影响 32 宿主/128 资产', assets=128, docs=3, affectedDocuments=32, minInstanceCompile=128),
        case('C10', '一个实际流 SessionCache 元数据丢失', expected='session', minXmlParse=1),
        case('C11', '一个最终 BIG/SKUDEF 丢失', expected='output', maxInstanceCompile=0),
        case('C12', '完整混合负载跨 XML/纹理/音频/动画和多个流修改', assets=256, docs=22, materials=64, minInstanceCompile=256),
        case('F01-delete', '删除 32 个资产', 'Full', assets=32, docs=1, expected='frontend', minXmlParse=1),
        case('F01-rename', '32 个无引用资产 ID 改名', 'Full', assets=32, docs=4, minInstanceCompile=32),
        case('F01-move', '8 份文档移动并更新 Include', 'Full', assets=64, docs=8, expected='frontend', minXmlParse=8),
        case('F01-entry', '8 个新入口 Include / 32 个资产', 'Full', assets=32, docs=8, minInstanceCompile=32),
        case('F02', '继承与共享 Define 扇出（200 个实际资产）', 'Full', assets=8, docs=11,
             affectedAssets=192, minInstanceCompile=200),
        case('F02-depth', '8 个父实例经 4 层继承边影响 152 个后代', 'Full', assets=8, docs=8,
             affectedAssets=152, inheritanceDepth=4, minInstanceCompile=160),
        case('F03-independent', '64 个独立音频输入（含 XAS）', 'Full', assets=64, docs=1, materials=64, minInstanceCompile=64),
        case('F03-shared', '64 个共享 WAV 的 XAS AudioFile', 'Full', assets=64, docs=1, materials=1, minInstanceCompile=64),
        case('F04-independent', '96 个独立 DDS 输入', 'Full', assets=96, docs=1, materials=96, minInstanceCompile=96),
        case('F04-shared', '96 个共享 DDS Texture', 'Full', assets=96, docs=1, materials=1, minInstanceCompile=96),
        case('F05', '共享层级影响 32 个动画', 'Full', assets=1, docs=1, affectedAssets=32, minInstanceCompile=32),
        case('F06', '64 份 instance Include 宿主宽深变化及合法目标重载', 'Full', assets=128, docs=101, expected='frontend', minXmlParse=64),
        case('F07-session-partial', '部分流 SessionCache 丢失', 'Full', expected='session', minXmlParse=1),
        case('F07-session-all', '全流 SessionCache 丢失', 'Full', expected='session', minXmlParse=1),
        case('F07-assets', '32 个中间 .asset/.cdata 丢失', 'Full', expected='intermediate'),
        case('F07-linked', '一个链接流 .bin 丢失', 'Full', expected='linked'),
        case('F09-move', '16 个叶子 XML 移动并更新 Include', 'Full', assets=128, docs=16, expected='frontend', minXmlParse=16),
        case('F09-mtime', '16 个叶子 XML 只更新时间戳', 'Full', docs=16, expected='timestamp'),
        case('F09-same-stamp', '16 个叶子 XML 等长内容变化并保留 mtime', 'Full', assets=128, docs=16, minInstanceCompile=128),
        case('F09-shadow', '16 个叶子 XML 搜索根路径遮蔽', 'Full', assets=64, docs=16, minInstanceCompile=64),
        case('F09-entry', '一个真实 APT 流入口改名', 'Full', docs=1, expected='frontend', minXmlParse=1),
        case('F10', '完整可编辑声明的 25% 混合压力', 'Full', assets=math.ceil(workload['assets'] * .25),
             minInstanceCompile=math.ceil(workload['assets'] * .25)),
        case('F12', 'Schema/TypeHash/ProcessingHash 合法版本变化', 'Full', availability='not_applicable',
             reason='未提供合法可运行且确实改变身份的另一版本，不模拟身份哈希'),
    ]
    for fixture in workload['streamFixtures']:
        rows.append(case('F08-' + fixture['streamId'].replace('/', '-'), '实际流局部更新 ' + fixture['streamId'],
                         'Full', assets=32, docs=1, minInstanceCompile=32, streamId=fixture['streamId'], paths=[fixture['path']]))
    rows.append(case('F08-cross', '实际多个流组合更新', 'Full', assets=32 * workload['streamCount'],
                     docs=workload['streamCount'], minInstanceCompile=32 * workload['streamCount'],
                     paths=[fixture['path'] for fixture in workload['streamFixtures']]))
    # The controller owns per-mode cold preparation and independent baselines.
    from BenchmarkCompilerCache import MODES
    for mode in MODES:
        rows.append(case('F11-mode-' + mode, '代表负载缓存模式 ' + mode, 'Full', expected='mode', mode=mode))
    for jobs in ('1', '4', 'auto'):
        rows.append(case('F11-jobs-' + jobs, '音频更新 jobs=' + jobs, 'Full', assets=32, docs=1,
                         materials=32, minInstanceCompile=32, jobs=jobs))
    return rows


def apply_case(workload, case, destination, cache=None):
    """Mutate a restored working baseline, returning actual changed-input counts.

    Source/cache snapshots must be restored by the caller before each repeat.
    Counts distinguish directly edited declarations from affected declarations;
    external-input edits report their dependent assets separately.
    """
    mod, destination = Path(workload['modRoot']), Path(destination)
    cache = Path(cache) if cache is not None else cache_root(workload['sdkRoot'], mod)
    data = mod / 'Data/L8'
    identifier = case['id']
    changed, assets, materials = set(), 0, 0
    affected, deleted, extra_args, touched_streams = 0, [], [], []
    metadata_documents, metadata_stream, modified_caches = 0, None, 0

    def edit_xml(path, transform):
        root = ET.parse(path).getroot()
        count = transform(root)
        need(count > 0, 'No actual XML edits: ' + str(path))
        save_xml(path, root)
        changed.add(path.relative_to(mod).as_posix())
        return count

    def edit_armor(paths, limit=None, predicate=lambda node: True, preserve=False):
        nonlocal assets
        for path in paths:
            stamp = path.stat()
            before = path.read_bytes()
            def transform(root):
                nonlocal assets
                count = 0
                for node in root:
                    if node.tag == '{%s}ArmorTemplate' % NS and predicate(node) and (limit is None or assets < limit):
                        node.set('Default', '73.5%')
                        assets += 1
                        count += 1
                return count
            edit_xml(path, transform)
            if preserve:
                need(len(before) == path.stat().st_size, 'Same-stamp mutation changed length')
                os.utime(path, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
            if limit is not None and assets >= limit:
                break

    def edit_material(paths, count_affected):
        nonlocal materials, affected
        for path in paths:
            payload = bytearray(path.read_bytes())
            offset = 128 if path.suffix == '.dds' else (18 if path.suffix == '.tga' else 44)
            need(len(payload) > offset + 1, 'Material input is empty')
            payload[offset] ^= 1
            path.write_bytes(payload)
            changed.add(path.relative_to(mod).as_posix())
            materials += 1
        affected += count_affected

    def animations(count):
        nonlocal assets
        for path in sorted(data.glob('animations-*.xml')):
            def transform(root):
                nonlocal assets
                changed_count = 0
                for node in root:
                    if node.tag == '{%s}W3DAnimation' % NS and assets < count:
                        node.set('FrameRate', str(int(node.get('FrameRate')) + 1))
                        assets += 1
                        changed_count += 1
                return changed_count
            edit_xml(path, transform)
            if assets >= count:
                break

    def shared_defines():
        nonlocal affected
        edit_xml(data / 'defines.xml', change_defines)
        root = ET.parse(data / 'xi-defines-a.xml').getroot()
        change_defines(root)
        save_xml(data / 'xi-defines-b.xml', root)
        changed.add('Data/L8/xi-defines-b.xml')
        def retarget_fragment(root):
            count = 0
            for node in root:
                if node.get('source') == 'xi-defines-a.xml':
                    node.set('source', 'xi-defines-b.xml')
                    count += 1
            return count
        edit_xml(data / 'fragment.xml', retarget_fragment)
        affected += 128

    def change_defines(root):
        values = root.findall('.//{%s}Define' % NS)
        for node in values:
            node.set('value', '84.25' if node.get('name') == 'L8_SCALE' else '75.25')
        return len(values)

    def parents(siblings=False, documents=16):
        nonlocal affected
        edit_armor(sorted(data.glob('parent-*.xml'))[:documents], predicate=lambda node:
                   node.get('id', '').startswith('L8_Sibling_' if siblings else 'L8_Parent_'))
        if not siblings:
            affected += documents * 8

    def new_assets():
        nonlocal assets
        root = ET.parse(data / 'index.xml').getroot()
        for i in range(8):
            name = 'added-%02d.xml' % i
            save_xml(data / name, declaration(*(armor(workload['instanceIds']['futureIds'][
                     'L8_Added_%02d_%02d' % (i, j)]) for j in range(4))))
            changed.add('Data/L8/' + name)
            add_include(root, name)
            assets += 4
        save_xml(data / 'index.xml', root)
        changed.add('Data/L8/index.xml')

    def move_docs(count):
        index = ET.parse(data / 'index.xml').getroot()
        nodes = {node.get('source'): node for node in includes(index)}
        for source in sorted(data.glob('spread-*.xml'))[:count]:
            relative = 'moved/' + source.name
            target = data / relative
            target.parent.mkdir(exist_ok=True)
            source.rename(target)
            nodes[source.name].set('source', relative)
            changed.add(source.relative_to(mod).as_posix())
            changed.add(target.relative_to(mod).as_posix())
        save_xml(data / 'index.xml', index)
        changed.add('Data/L8/index.xml')

    if identifier == 'C00' or identifier.startswith('F11-mode-'):
        pass
    elif identifier in ('C01', 'F09-same-stamp'):
        edit_armor(sorted(data.glob('spread-*.xml')), preserve=identifier == 'F09-same-stamp')
    elif identifier == 'C02':
        edit_armor([data / 'dense.xml'], limit=128)
    elif identifier in ('C03', 'F01-entry'):
        new_assets()
    elif identifier == 'C04':
        edit_material(sorted((data / 'materials').glob('common-*.tga')), 32)
    elif identifier in ('C05', 'F03-independent') or identifier.startswith('F11-jobs-'):
        count = 64 if identifier == 'F03-independent' else 32
        edit_material(sorted((data / 'materials').glob('audio-*.wav'))[:count], count)
    elif identifier == 'C06':
        animations(32)
    elif identifier == 'C07':
        parents()
    elif identifier == 'C08':
        parents(siblings=True)
    elif identifier == 'C09':
        shared_defines()
    elif identifier == 'F07-session-partial':
        # One SessionCache may contain all five streams. Remove a selected
        # stream's serialized document state, preserving file hashes and every
        # other stream; deleting this shared file would falsely test all-stream
        # loss again. FileItem explicitly allows its optional ad to be absent.
        fixture = next((item for item in workload['streamFixtures']
                        if item['streamId'].startswith('apt/')), None)
        need(fixture is not None, 'No actual APT stream available for partial metadata loss')
        selected = str((mod / fixture['path']).resolve()).replace('\\', '/').casefold()
        entry = next(path for identifier, path in stream_entries(mod) if identifier == fixture['streamId'])
        selected_entry = str(entry.resolve()).replace('\\', '/').casefold()
        candidates = sorted(p for p in cache.rglob('BinaryAssetBuilder.SessionCache.xml*')
                            if p.name in ('BinaryAssetBuilder.SessionCache.xml',
                                          'BinaryAssetBuilder.SessionCache.xml.deflate'))
        for path in candidates:
            payload = path.read_bytes()
            compressed = path.suffix == '.deflate'
            document = ET.fromstring(zlib.decompress(payload, -15) if compressed else payload)
            for item in document.findall('./fic/fi'):
                identity, state = item.find('hi'), item.find('ad')
                if identity is None or state is None:
                    continue
                source = identity.get('d', '').split(';')[0].replace('\\', '/').casefold()
                hints = {hint.replace('\\', '/').casefold() for hint in (state.findtext('dsf') or '').split(';') if hint}
                # Drop the selected stream's exclusive document state. Shared
                # documents retain their metadata for the other real streams.
                if source in (selected, selected_entry) or hints == {selected_entry}:
                    item.remove(state)
                    metadata_documents += 1
            if metadata_documents:
                payload = ET.tostring(document, encoding='utf-8')
                if compressed:
                    compressor = zlib.compressobj(wbits=-15)
                    payload = compressor.compress(payload) + compressor.flush()
                path.write_bytes(payload)
                modified_caches += 1
                metadata_stream = fixture['streamId']
                break
        need(metadata_documents > 0, 'Selected actual stream has no serialized document metadata')
    elif identifier in ('C10', 'F07-session-all'):
        paths = sorted(cache.rglob('BinaryAssetBuilder.SessionCache.xml*'))
        paths = [p for p in paths if p.is_file() and p.name in
                 ('BinaryAssetBuilder.SessionCache.xml', 'BinaryAssetBuilder.SessionCache.xml.deflate')]
        need(paths, 'No real SessionCache metadata found')
        if identifier != 'F07-session-all':
            parent = paths[0].parent
            paths = [p for p in paths if p.parent == parent]
        for path in paths:
            deleted.append(str(path.resolve()))
            path.unlink()
    elif identifier == 'C11':
        paths = sorted(p for p in (destination / 'published').rglob('*')
                       if p.is_file() and p.suffix.lower() in ('.big', '.skudef'))
        need(paths, 'No final BIG/SKUDEF exists; C11 requires normal BIG packaging')
        paths[0].unlink()
        deleted.append(paths[0].relative_to(destination).as_posix())
    elif identifier == 'C12':
        edit_armor(sorted(data.glob('spread-*.xml'))[:12])
        edit_material(sorted((data / 'materials').glob('common-*.tga')), 32)
        edit_material(sorted((data / 'materials').glob('audio-*.wav'))[:32], 32)
        old_count = assets
        assets = 0
        animations(32)
        assets += old_count
        streams = [fixture for fixture in workload['streamFixtures']
                   if not fixture['streamId'].startswith('base/')][:2]
        need(len(streams) == 2, 'C12 needs two real non-base streams')
        edit_armor([mod / fixture['path'] for fixture in streams])
        touched_streams = [stream for stream in workload['streams'] if stream.startswith('base/')]
        touched_streams += [fixture['streamId'] for fixture in streams]
    elif identifier == 'F01-delete':
        def remove(root):
            nodes = [node for node in root if node.tag == '{%s}ArmorTemplate' % NS][:32]
            for node in nodes:
                root.remove(node)
            return len(nodes)
        assets = edit_xml(data / 'scale-000-000.xml', remove)
    elif identifier == 'F01-rename':
        def rename(root):
            nodes = [node for node in root if node.tag == '{%s}ArmorTemplate' % NS]
            for node in nodes:
                node.set('id', workload['instanceIds']['futureIds'][node.get('id')])
            return len(nodes)
        for path in sorted(data.glob('spread-*.xml'))[:4]:
            assets += edit_xml(path, rename)
    elif identifier in ('F01-move', 'F09-move'):
        move_docs(8 if identifier == 'F01-move' else 16)
    elif identifier == 'F02':
        parents(documents=8)
        shared_defines()
    elif identifier == 'F02-depth':
        edit_armor(sorted(data.glob('depth-root-*.xml')))
        affected = 152
    elif identifier == 'F03-shared':
        edit_material([data / 'materials/shared.wav'], 64)
    elif identifier == 'F04-independent':
        edit_material(sorted((data / 'materials').glob('texture-*.dds')), 96)
    elif identifier == 'F04-shared':
        edit_material([data / 'materials/shared.dds'], 96)
    elif identifier == 'F05':
        def hierarchy_edit(root):
            root.find('.//{%s}Translation' % NS).set('Z', '2')
            return 1
        assets = edit_xml(data / 'hierarchy.xml', hierarchy_edit)
        affected = 32
    elif identifier == 'F06':
        moved = data / 'include-moved'
        moved.mkdir()
        for parent in sorted(data.glob('include-parent-*.xml')):
            parent.rename(moved / parent.name)
            changed.add(parent.relative_to(mod).as_posix())
            changed.add((moved / parent.name).relative_to(mod).as_posix())
        for host in sorted(data.glob('include-host-*.xml')):
            def retarget(root):
                nodes = list(includes(root))
                for node in nodes:
                    node.set('source', 'include-moved/' + node.get('source'))
                return len(nodes)
            edit_xml(host, retarget)
        wide = declaration()
        for parent in sorted(moved.glob('*.xml')):
            add_include(wide, 'include-moved/' + parent.name)
        for branch in range(4):
            branch_root = declaration()
            for doc in range(branch * 16, branch * 16 + 16):
                add_include(branch_root, '../include-host-%02d.xml' % doc)
            name = 'include-depth/branch-%02d.xml' % branch
            save_xml(data / name, branch_root)
            changed.add('Data/L8/' + name)
            add_include(wide, name)
        save_xml(data / 'include-wide.xml', wide)
        changed.add('Data/L8/include-wide.xml')
    elif identifier == 'F07-assets':
        # Native asset-cache entries need not be consulted by a fully linked
        # hot build. Remove actual stream CDATA, keeping the session backup so
        # the successful fallback restoration path is really required.
        paths = sorted(p for p in (cache / 'streams').rglob('*.cdata') if p.is_file())
        need(len(paths) >= 32, 'Fewer than 32 actual stream CDATAs available')
        for path in paths[:32]:
            deleted.append(str(path.resolve()))
            path.unlink()
    elif identifier == 'F07-linked':
        paths = sorted(p for p in (cache / 'streams').rglob('*.bin') if p.is_file())
        need(paths, 'No actual linked-stream .bin found')
        paths[0].unlink()
        deleted.append(str(paths[0].resolve()))
    elif identifier.startswith('F08-'):
        edit_armor([mod / path for path in case['paths']])
    elif identifier == 'F09-mtime':
        for path in sorted(data.glob('spread-*.xml')):
            stamp = path.stat()
            os.utime(path, ns=(stamp.st_atime_ns, stamp.st_mtime_ns + 2_000_000_000))
            changed.add(path.relative_to(mod).as_posix())
    elif identifier == 'F09-shadow':
        shadow = mod / 'Data/L8ShadowOverride'
        for source in sorted((mod / 'Data/L8Shadow').glob('*.xml')):
            target = shadow / 'L8Shadow' / source.name
            root = ET.parse(source).getroot()
            for node in root:
                node.set('Default', '73.5%')
                assets += 1
            save_xml(target, root)
            changed.add(target.relative_to(mod).as_posix())
        sdk = Path(workload['sdkRoot'])
        extra_args = ['--data', ';'.join(str(path) for path in
                      (shadow, mod / 'Data', sdk, sdk / 'Mods', sdk / 'SAGEXML'))]
    elif identifier == 'F09-entry':
        selected = next(((stream, path) for stream, path in stream_entries(mod)
                         if stream.startswith('apt/')), None)
        need(selected is not None, 'No actual APT entry is available to rename')
        old_stream, source = selected
        target = source.with_name(source.stem + '_L8Renamed.xml')
        need(not target.exists(), 'APT entry rename would overwrite another input')
        source.rename(target)
        changed.add(source.relative_to(mod).as_posix())
        changed.add(target.relative_to(mod).as_posix())
    elif identifier == 'F10':
        # Use 25% of the *whole* workload declaration inventory, not 25% of
        # only our generated subset; include XML + texture/audio/animation.
        target = case['changedAssets']
        edit_material(sorted((data / 'materials').glob('common-*.tga')), 32)
        edit_material(sorted((data / 'materials').glob('audio-*.wav'))[:32], 32)
        animations(32)
        edit_armor(sorted(data.glob('scale-*.xml')), limit=target - affected)
        need(assets + affected == target, 'Insufficient independent declarations for 25% stress')
    else:
        raise ValueError('No available implementation for ' + identifier)
    xml_paths = {p for p in changed if Path(p).suffix.lower() in ('.xml', '.w3x')}
    result = {'changedAssets': assets, 'affectedAssets': affected,
            'changedDocuments': len(xml_paths), 'changedMaterials': materials,
            'changedPaths': sorted(changed), 'deletedPaths': deleted,
            'extraArgs': extra_args,
            'streamIdsTouched': touched_streams,
            'metadataDocumentsRemoved': metadata_documents, 'metadataStreamId': metadata_stream,
            'changedCaches': (len(deleted) if identifier != 'C11' else 0) + modified_caches,
            'changedOutputs': len(deleted) if identifier == 'C11' else 0}
    if identifier == 'F09-entry':
        result['changedDocuments'] = 1
        result['expectedStreams'] = sorted('apt/' + target.stem if stream == old_stream else stream
                                           for stream in workload['streams'])
        result['renamedStreamFrom'] = old_stream
        result['renamedStreamTo'] = 'apt/' + target.stem
    return result
