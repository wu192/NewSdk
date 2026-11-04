#!/usr/bin/env python3
"""Deterministic, reversible developer edits over the complete pinned Upheaval.

Only benchmark work copies are changed. The real SDK sound-parent relocation and
lifecycle's eight extra weapon declarations are explicit setup, never described
as untouched original MOD input. No artificial delays or expanded Armor load.
"""
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import xml.etree.ElementTree as ET

from HotCacheWorkload import (HISTORICAL, UPHEAVAL_TREE, allocate_id,
                              extract_upheaval, inventory, reserved_instance_hashes)

SCENARIOS = (
    'gameobject-balance', 'weapon-modules', 'inherited-parent', 'parent-unrelated',
    'shared-defines', 'asset-lifecycle', 'external-media', 'animation-batch')
GROUPS = {'A': SCENARIOS[:4], 'B': SCENARIOS[4:]}
DESCRIPTIONS = {
    'gameobject-balance': 'Batch balance existing GameObject build/health/movement/root fields',
    'weapon-modules': 'Batch existing WeaponTemplate ranges/speeds and DamageNugget damage/radius',
    'inherited-parent': 'Change real shared BaseSoundEffect parent fields across native AudioEvent descendants',
    'parent-unrelated': 'Change real BaseQuickChatDialog sibling unused by this MOD inheritance closure',
    'shared-defines': 'Change two used real SDK sound Defines and one explicitly unused Define control',
    'asset-lifecycle': 'Rename 20 referenced native Weapons; delete/create explicitly all-included eight-asset files',
    'external-media': 'Replace sample/pixel data of 64 real PCM WAV and 16 referenced DDS inputs',
    'animation-batch': 'Change frame rates in 32 real W3X animation documents',
}
SOUND_PARENT = Path('Data/SageXml/Sounds/BaseSoundEffect.xml')
SETUP_FILE = '.upheaval-edit-setup.json'
TOKEN = re.compile(rb'<!--.*?-->|<!\[CDATA\[.*?\]\]>|<\?.*?\?>|</?[A-Za-z_][^>]*>', re.S)
ATTR = re.compile(rb'([A-Za-z_][\w:.-]*)\s*=\s*([\"\'])(.*?)\2', re.S)
NAME = re.compile(rb'</?([A-Za-z_][\w:.-]*)')
NUMBER = re.compile(rb'-?\d+(?:\.\d+)?(?:s|d|%)?\Z')
IDENTIFIER = rb'A-Za-z0-9_.-'


def need(value, message):
    if not value:
        raise ValueError(message)


def sha_bytes(data):
    return hashlib.sha256(data).hexdigest()


def xml_tags(data):
    """Byte spans plus enclosing top-level declaration, without XML reserialization."""
    stack = []
    for token in TOKEN.finditer(data):
        raw = token.group()
        if raw.startswith((b'<!--', b'<![CDATA[', b'<?')):
            continue
        match = NAME.match(raw)
        if not match:
            continue
        name = match.group(1).decode('ascii').split(':')[-1]
        if raw.startswith(b'</'):
            need(stack and stack[-1]['node'] == name, 'Unbalanced XML token stream: '+name)
            opened = stack.pop()
            if len(stack) == 1:
                opened['elementEnd'] = token.end()
                yield opened
            continue
        attributes = {a.group(1).decode('ascii'): a.group(3) for a in ATTR.finditer(raw)}
        top = (name, attributes.get('id', b'').decode('utf-8')) if len(stack) == 1 else (stack[-1]['topType'], stack[-1]['topId']) if stack else ('', '')
        item = dict(node=name, start=token.start(), end=token.end(), raw=raw,
                    topType=top[0], topId=top[1], attributes=attributes,
                    attributesSpans=[(a.group(1).decode('ascii'), token.start()+a.start(3), token.start()+a.end(3), a.group(3)) for a in ATTR.finditer(raw)])
        yield item
        if not raw.rstrip().endswith(b'/>'):
            stack.append(item)
        elif len(stack) == 1:
            # A repeated top-level tag record with its complete element boundary.
            yield dict(item, elementEnd=token.end())
    need(not stack, 'Unterminated XML token stream')


def patch_attributes(data, predicate):
    """predicate(tag, name, rawValue) returns replacement bytes or None."""
    patches, records = [], []
    for tag in xml_tags(data):
        if 'elementEnd' in tag:
            continue
        for name, start, end, old in tag['attributesSpans']:
            new = predicate(tag, name, old)
            if new is None or new == old:
                continue
            need(isinstance(new, bytes) and b'<' not in new and b'"' not in new and b"'" not in new,
                 'Attribute patch must be safe literal bytes')
            patches.append((start, end, new))
            records.append(dict(topType=tag['topType'], topId=tag['topId'], node=tag['node'],
                                attribute=name, before=old.decode('utf-8'), after=new.decode('utf-8')))
    for start, end, new in reversed(patches):
        data = data[:start]+new+data[end:]
    return data, records


def bump_numeric(value):
    """Small same-length positive numeric edit, retaining its unit and spelling."""
    if not NUMBER.fullmatch(value) or float(value.rstrip(b'sd%')) <= 0:
        return value
    result = bytearray(value)
    for i in range(len(result)-1, -1, -1):
        if 48 <= result[i] <= 57:
            result[i] = result[i]+1 if result[i] != 57 else 56
            return bytes(result)
    return value


def rename_tokens(data, mapping):
    """Replace complete ASCII identifiers, including explicit Type:Id handles."""
    if not mapping:
        return data, {}
    canonical = {key.lower():key for key in mapping}
    need(len(canonical) == len(mapping), 'Case-insensitive rename sources overlap')
    pattern = re.compile(rb'(?<!['+IDENTIFIER+rb'])(' + b'|'.join(re.escape(x.encode('ascii')) for x in sorted(mapping, key=lambda x: -len(x))) + rb')(?!['+IDENTIFIER+rb'])', re.I)
    counts = Counter()
    def change(match):
        key = canonical[match.group().decode('ascii').lower()]; counts[key] += 1
        return mapping[key].encode('ascii')
    return pattern.sub(change, data), dict(counts)


def riff_chunks(data):
    need(data[:4] == b'RIFF' and data[8:12] == b'WAVE', 'Expected RIFF WAVE')
    offset = 12
    while offset+8 <= len(data):
        size = struct.unpack_from('<I', data, offset+4)[0]
        need(offset+8+size <= len(data), 'Truncated WAV chunk')
        yield data[offset:offset+4], offset+8, size
        offset += 8+size+(size & 1)


def patch_wave(data):
    chunks = {kind:(offset,size) for kind,offset,size in riff_chunks(data)}
    fmt, _ = chunks[b'fmt ']
    encoding, channels, rate = struct.unpack_from('<HHI', data, fmt)
    bits = struct.unpack_from('<H', data, fmt+14)[0]
    need(encoding == 1 and channels == 1 and bits in (8, 16) and rate > 0, 'Expected native MOD mono PCM8/16 WAV')
    offset, size = chunks[b'data']; count = min(size//(bits//8), 4096)
    need(count > 0, 'Empty WAV payload')
    changed = bytearray(data)
    for index in range(count):
        if bits == 16:
            old = struct.unpack_from('<h', data, offset+index*2)[0]
            new = min(32767, old+4096) if old >= 0 else max(-32768, old-4096)
            struct.pack_into('<h', changed, offset+index*2, new)
        else:
            old = data[offset+index]
            changed[offset+index] = min(255, old+32) if old >= 128 else max(0, old-32)
    need(changed != data, 'WAV edit changed no samples')
    return bytes(changed), count


def patch_dds(data):
    need(data[:4] == b'DDS ' and len(data) > 128 and struct.unpack_from('<I', data, 4)[0] == 124, 'Expected legacy DDS header')
    need(data[84:88] != b'DX10', 'DX10 DDS is outside the native input fixture')
    count = min(128, len(data)-128); changed = bytearray(data)
    for index in range(128, 128+count):
        changed[index] ^= 0x7f
    return bytes(changed), count


def _xml_files(mod):
    return sorted((mod/'Data').rglob('*.xml'))


def _local_include_closure(mod):
    """Real MOD-local include closure; reference/instance documents are not published."""
    paths = {path.relative_to(mod).as_posix().lower():path
             for root in ('Data', 'Art') for path in (mod/root).rglob('*')
             if path.is_file() and path.suffix.lower() in ('.xml', '.w3x')}
    root = paths.get('data/mod.xml')
    if root is None:
        return {path:True for path in _xml_files(mod)}  # small format unit fixtures
    pending, seen = [(root, True)], {}
    while pending:
        path, published = pending.pop()
        if path in seen and (seen[path] or not published):
            continue
        seen[path] = published
        data = path.read_bytes()
        if b'<Include' not in data:
            continue
        for token in TOKEN.finditer(data):
            raw = token.group()
            if not re.match(rb'<Include\s', raw):
                continue
            attributes = {a.group(1).decode('ascii'):a.group(3) for a in ATTR.finditer(raw)}
            source = attributes.get('source', b'').decode().replace('\\', '/')
            if ':' in source:
                prefix, relative = source.split(':', 1)
                prefix = {'DATA':'Data', 'ART':'Art'}.get(prefix.upper())
                if prefix is None:
                    continue
                key = prefix+'/'+relative
            else:
                key = (path.parent/source).relative_to(mod).as_posix()
            target = paths.get(key.lower())
            if target:
                pending.append((target, published and attributes.get('type', b'all') == b'all'))
    return seen


def _published_xml_files(mod):
    return sorted(path for path,published in _local_include_closure(mod).items()
                  if published and path.suffix.lower() == '.xml')


def _referenced_media(mod):
    paths = {path.relative_to(mod).as_posix().lower():path for root in ('Audio','Art')
             for path in (mod/root).rglob('*') if path.is_file()}
    result = {}
    for document in _published_xml_files(mod):
        for tag in xml_tags(document.read_bytes()):
            if 'elementEnd' in tag or tag['node'] != tag['topType'] or tag['node'] not in ('AudioFile','Texture'):
                continue
            source = tag['attributes'].get('File', b'').decode().replace('\\', '/')
            if ':' not in source:
                continue
            prefix, relative = source.split(':', 1)
            root = {'AUDIO':'Audio', 'ART':'Art'}.get(prefix.upper())
            path = paths.get((root+'/'+relative).lower()) if root else None
            if path:
                result.setdefault(path, []).append(dict(type=tag['topType'], id=tag['topId'],
                    declaration=document.relative_to(mod).as_posix(), file=source))
    return result


def _sound_impact(mod):
    """Static inherited-field evidence, separate from measured compiler invalidation."""
    native = []
    for path in _published_xml_files(mod):
        for tag in xml_tags(path.read_bytes()):
            if 'elementEnd' in tag and tag['topType'] == 'AudioEvent':
                native.append((path, tag))
    parents = {}
    # Include local instance-only bases as well as the exact relocated SDK source.
    for path in sorted(_local_include_closure(mod)):
        if path.suffix.lower() != '.xml':
            continue
        for tag in xml_tags(path.read_bytes()):
            if 'elementEnd' in tag and tag['topType'] == 'AudioEvent':
                parents[tag['topId']] = tag['attributes']
    def parent_id(attributes):
        return attributes.get('inheritFrom', b'').decode().split(':')[-1]
    def reaches(identifier, target):
        visited = set()
        while identifier and identifier not in visited:
            if identifier == target:
                return True
            visited.add(identifier); identifier = parent_id(parents.get(identifier, {}))
        return False
    descendants = [(path,tag) for path,tag in native if reaches(parent_id(tag['attributes']), 'BaseSoundEffect')]
    fields = {}
    for field in ('ReverbEffectLevel', 'ZoomedInOffscreenOcclusionPercent', 'MinRange'):
        effective = []
        for path,tag in descendants:
            identifier = tag['topId']; visited = set()
            while identifier and identifier != 'BaseSoundEffect' and identifier not in visited:
                visited.add(identifier); attributes = parents.get(identifier, {})
                if field in attributes:
                    break
                identifier = parent_id(attributes)
            if identifier == 'BaseSoundEffect':
                effective.append(tag['topId'])
        fields[field] = dict(effectiveNativeDescendants=len(effective),
                             overriddenNativeDescendants=len(descendants)-len(effective),
                             effectiveIds=effective)
    usages = {}
    for identifier in ('WEAPON_FIRE', 'WEAPON_IMPACT', 'VEHICLE_DIE_EXPLOSION'):
        pattern = re.compile(rb'\$'+identifier.encode()+rb'(?!['+IDENTIFIER+rb'])')
        matches = []
        for path in _published_xml_files(mod):
            count = len(pattern.findall(path.read_bytes()))
            if count:matches.append(dict(path=path.relative_to(mod).as_posix(), occurrences=count))
        usages[identifier] = dict(nativeExpressionOccurrences=sum(row['occurrences'] for row in matches), documents=matches)
    return dict(kind='static native MOD all-Include closure; measured invalidation is recorded separately',
                directBaseSoundEffect=sum(parent_id(tag['attributes'])=='BaseSoundEffect' for _,tag in native),
                transitiveBaseSoundEffect=len(descendants),fields=fields,defineUsages=usages,
                baseQuickChatNativeDescendants=sum(reaches(parent_id(tag['attributes']), 'BaseQuickChatDialog') for _,tag in native))


def _definitions(mod, kind):
    result = []
    for path in _published_xml_files(mod):
        data = path.read_bytes()
        for tag in xml_tags(data):
            if 'elementEnd' in tag and tag['topType'] == kind:
                result.append((path, tag, data[tag['start']:tag['elementEnd']]))
    return result


def _setup_path(mod):
    return mod.parent/SETUP_FILE


def _weapon_references(mod, identifiers):
    """Count semantic attribute/text references, excluding declarations and comments."""
    canonical={identifier.lower():identifier for identifier in identifiers}
    pattern=re.compile(r'(?<![A-Za-z0-9_.-])('+'|'.join(re.escape(x) for x in sorted(identifiers,key=lambda x:-len(x)))+r')(?![A-Za-z0-9_.-])',re.I)
    references={identifier:[] for identifier in identifiers}
    for path in _published_xml_files(mod):
        for node in ET.parse(path).getroot().iter():
            node_type=node.tag.rsplit('}',1)[-1]
            values=[(name,value) for name,value in node.attrib.items()
                    if not (node_type=='WeaponTemplate' and name=='id')]
            if node.text:values.append(('text',node.text))
            for name,value in values:
                for match in pattern.finditer(value):
                    identifier=canonical[match.group().lower()]
                    references[identifier].append(dict(path=path.relative_to(mod).as_posix(),node=node_type,field=name,value=value))
    return references


def prepare(repo, work, scenario):
    repo, work = Path(repo).resolve(), Path(work).resolve()
    need(scenario in SCENARIOS, 'Unknown edit scenario '+scenario)
    work.mkdir(parents=True, exist_ok=True)
    mod = extract_upheaval(repo, work)
    metadata = dict(scenario=scenario,description=DESCRIPTIONS[scenario],sourceCommit=HISTORICAL,
                    sourceTree=UPHEAVAL_TREE,scope='Complete pinned original Upheaval; no trimmed inputs or Armor expansion',
                    setup=[],expectedPublicationChanged=scenario != 'parent-unrelated')
    setup = dict(scenario=scenario)
    if scenario in ('inherited-parent','parent-unrelated','shared-defines'):
        source=repo/'SDK/SageXml/Sounds/BaseSoundEffect.xml';target=mod/SOUND_PARENT
        target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
        metadata['setup'].append(dict(kind='real-sdk-parent-relocation',source='SDK/SageXml/Sounds/BaseSoundEffect.xml',
            path=SOUND_PARENT.as_posix(),sha256=sha_bytes(source.read_bytes()),
            reason='Same real SDK source copied into first MOD DATA search root; repo SDK unchanged. Not an original MOD-local file.'))
        setup['staticImpact'] = metadata['staticImpact'] = _sound_impact(mod)
    if scenario == 'asset-lifecycle':
        declarations = _definitions(mod,'WeaponTemplate');need(len(declarations)>=28,'Too few native weapons')
        references=_weapon_references(mod,[tag['topId'] for _,tag,_ in declarations])
        selected=[tag['topId'] for _,tag,_ in declarations if references[tag['topId']]][:20]
        need(len(selected)==20,'Twenty referenced native weapon IDs are required')
        occupied=reserved_instance_hashes(repo,mod)
        mapping={identifier:allocate_id(identifier+'_HotEditRenamed',occupied) for identifier in selected}
        removed=[allocate_id('HotEditRemove_%02d'%i,occupied) for i in range(8)]
        added=[allocate_id('HotEditAdded_%02d'%i,occupied) for i in range(8)]
        _,template,body=declarations[0];blocks=[]
        for identifier in removed:
            block,_=rename_tokens(body,{template['topId']:identifier});blocks.append(block)
        target=mod/'Data/HotEditLifecycleBefore.xml';after=mod/'Data/HotEditLifecycleAfter.xml'
        document=b'<?xml version="1.0" encoding="utf-8"?>\r\n<AssetDeclaration xmlns="uri:ea.com:eala:asset">\r\n'+b'\r\n'.join(blocks)+b'\r\n</AssetDeclaration>\r\n'
        target.write_bytes(document)
        root=mod/'Data/Mod.xml';data=root.read_bytes();close=data.find(b'</Includes>');need(close>=0,'Missing root Includes close')
        root.write_bytes(data[:close]+b'<Include type="all" source="HotEditLifecycleBefore.xml" />\r\n\t'+data[close:])
        setup.update(lifecycleFile=target.relative_to(mod).as_posix(),lifecycleAfter=after.relative_to(mod).as_posix(),templateId=template['topId'],
                     templateHex=body.hex(),removeIds=removed,addIds=added,rename=mapping,
                     semanticReferences={identifier:references[identifier] for identifier in selected})
        metadata['setup'].append(dict(kind='supplementary-real-weapon-clones',path=setup['lifecycleFile'],
            nativeTemplate=template['topId'],declarations=8,includeRoot='Data/Mod.xml',includeType='all',
            reason='Eight explicitly supplementary declarations in a separate publicly all-included file permit real file/asset delete/add; no native inputs are replaced or trimmed.'))
    metadata['input']=inventory(mod)
    _setup_path(mod).write_text(json.dumps(setup,indent=2)+'\n',encoding='utf-8')
    return mod, metadata


def mutate(mod, scenario):
    mod=Path(mod).resolve();need(scenario in SCENARIOS,'Unknown edit scenario')
    setup=json.loads(_setup_path(mod).read_text());need(setup['scenario']==scenario,'Scenario setup mismatch')
    backup=mod.parent/('.edit-original-'+scenario)
    if backup.exists():shutil.rmtree(backup)
    backup.mkdir()
    mutation=dict(scenario=scenario,description=DESCRIPTIONS[scenario],backupPath=str(backup),changes=[],
                  expectedPublicationChanged=scenario!='parent-unrelated',details={})
    def write(path,new,records=None,detail=None):
        old=path.read_bytes() if path.exists() else None
        need(old!=new,'No-op edit: '+str(path))
        relative=path.relative_to(mod).as_posix();stamp=path.stat() if old is not None else None
        if old is not None:
            target=backup/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(old)
        if new is None:path.unlink()
        else:
            path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(new)
            if stamp:os.utime(path,ns=(stamp.st_atime_ns,stamp.st_mtime_ns))
        row=dict(path=relative,operation='created' if old is None else 'deleted' if new is None else 'modified',
                 beforeSha256=None if old is None else sha_bytes(old),afterSha256=None if new is None else sha_bytes(new),
                 beforeBytes=None if old is None else len(old),afterBytes=None if new is None else len(new),
                 sameLength=old is not None and new is not None and len(old)==len(new),
                 mtimeNs=None if stamp is None else stamp.st_mtime_ns,
                 atimeNs=None if stamp is None else stamp.st_atime_ns,
                 sameMtime=stamp is not None and new is not None and path.stat().st_mtime_ns==stamp.st_mtime_ns)
        if records:row['fieldChanges']=records
        if detail:row.update(detail)
        mutation['changes'].append(row)
    if scenario in ('gameobject-balance','weapon-modules'):
        for path in _published_xml_files(mod):
            def select(tag,name,value):
                if scenario=='gameobject-balance' and tag['topType']=='GameObject':
                    allowed=(tag['node']=='GameObject' and name in ('BuildTime','ThreatLevel','HealthBoxHeightOffset') or
                             tag['node'] in ('ActiveBody','HighlanderBody') and name in ('MaxHealth','InitialHealth') or
                             tag['node']=='LocomotorSet' and name=='Speed')
                elif scenario=='weapon-modules' and tag['topType']=='WeaponTemplate':
                    allowed=(tag['node']=='WeaponTemplate' and name in ('AttackRange','WeaponSpeed') or
                             tag['node'] in ('DamageNugget','DOTNugget') and name in ('Damage','Radius'))
                else:allowed=False
                return bump_numeric(value) if allowed else None
            data,records=patch_attributes(path.read_bytes(),select)
            if records:write(path,data,records)
    elif scenario in ('inherited-parent','parent-unrelated','shared-defines'):
        path=mod/SOUND_PARENT
        parent_fields={'ReverbEffectLevel':b'090','ZoomedInOffscreenOcclusionPercent':b'25','MinRange':b'350'}
        define_fields={'WEAPON_FIRE':b'55','WEAPON_IMPACT':b'65','VEHICLE_DIE_EXPLOSION':b'115'}
        def select(tag,name,value):
            if scenario=='inherited-parent' and tag['topId']=='BaseSoundEffect':return parent_fields.get(name)
            if scenario=='parent-unrelated' and tag['topId']=='BaseQuickChatDialog' and name in ('Volume','MinVolume'):return b'115'
            if scenario=='shared-defines' and tag['node']=='Define' and name=='value':return define_fields.get(tag['attributes'].get('name',b'').decode())
            return None
        data,records=patch_attributes(path.read_bytes(),select);need(records,'Selected real SDK sound fields not found');write(path,data,records)
        mutation['details']['parentSource']='Real SDK BaseSoundEffect relocated to MOD DATA root during scenario setup'
        if 'staticImpact' in setup:mutation['details']['staticImpact']=setup['staticImpact']
        if scenario=='parent-unrelated':mutation['details']['unrelatedScope']='BaseQuickChatDialog has no native Upheaval reference or inheritFrom; not claimed unrelated to all SDK content'
    elif scenario=='asset-lifecycle':
        target=mod/setup['lifecycleFile'];data=target.read_bytes()
        removed=[tag['topId'] for tag in xml_tags(data) if 'elementEnd' in tag and tag['topType']=='WeaponTemplate']
        need(removed==setup['removeIds'],'Delete file differs from eight prepared declarations')
        write(target,None,detail=dict(deletedDeclarations=8))
        template=bytes.fromhex(setup['templateHex']);blocks=[]
        for identifier in setup['addIds']:
            block,_=rename_tokens(template,{setup['templateId']:identifier});blocks.append(block)
        data=b'<?xml version="1.0" encoding="utf-8"?>\r\n<AssetDeclaration xmlns="uri:ea.com:eala:asset">\r\n'+b'\r\n'.join(blocks)+b'\r\n</AssetDeclaration>\r\n'
        write(mod/setup['lifecycleAfter'],data,detail=dict(addedDeclarations=8))
        root=mod/'Data/Mod.xml';data=root.read_bytes()
        need(data.count(b'HotEditLifecycleBefore.xml')==1,'Root lifecycle Include is not unique')
        write(root,data.replace(b'HotEditLifecycleBefore.xml',b'HotEditLifecycleAfter.xml'))
        all_counts=Counter()
        for path in _xml_files(mod):
            data,counts=rename_tokens(path.read_bytes(),setup['rename'])
            if counts:write(path,data,detail=dict(identifierOccurrences=counts));all_counts.update(counts)
        need(all(all_counts[key]>=2 for key in setup['rename']),'A renamed native Weapon lacks a declaration/reference pair')
        residual={}
        for path in _xml_files(mod):
            _,counts=rename_tokens(path.read_bytes(),{key:key for key in setup['rename']})
            if counts:residual[path.relative_to(mod).as_posix()]=counts
        need(not residual,'Unreplaced case-insensitive native Weapon references: '+str(residual))
        mutation['details'].update(renamedAssets=setup['rename'],identifierOccurrences=dict(all_counts),addedAssets=setup['addIds'],deletedAssets=setup['removeIds'],
                                  deletedFile=setup['lifecycleFile'],createdFile=setup['lifecycleAfter'],allIncluded=True,unreplacedCaseInsensitiveTokens=residual,
                                  semanticReferences=setup['semanticReferences'])
    elif scenario=='external-media':
        referenced=_referenced_media(mod)
        waves=sorted(path for path in referenced if path.suffix.lower()=='.wav')[:64]
        textures=sorted(path for path in referenced if path.suffix.lower()=='.dds')[:16]
        need(len(waves)==64 and len(textures)==16,'Native media set too small')
        for path in waves:
            data,count=patch_wave(path.read_bytes());write(path,data,detail=dict(sampleEdits=count,format='RIFF native PCM8/16 mono; header/chunk sizes preserved',publishedDeclarations=referenced[path]))
        for path in textures:
            data,count=patch_dds(path.read_bytes());write(path,data,detail=dict(pixelBytesEdited=count,format='Legacy DDS; 128-byte header preserved',publishedDeclarations=referenced[path]))
        mutation['details'].update(wavFiles=64,ddsFiles=16)
    elif scenario=='animation-batch':
        edited=0
        for path in sorted(path for path,published in _local_include_closure(mod).items() if published and path.suffix.lower()=='.w3x'):
            data,records=patch_attributes(path.read_bytes(),lambda tag,name,value:bump_numeric(value) if tag['node']=='W3DAnimation' and name=='FrameRate' else None)
            if records:write(path,data,records);edited+=1
            if edited==32:break
        need(edited==32,'Expected 32 real W3X animation documents')
    need(mutation['changes'],'Scenario changed no files')
    assets={(r['topType'],r['topId']) for change in mutation['changes'] for r in change.get('fieldChanges',[]) if r['topId']}
    mutation['totals']=dict(files=len(mutation['changes']),xmlFiles=sum(x['path'].lower().endswith(('.xml','.w3x')) for x in mutation['changes']),
                            assetDeclarationsWithFieldEdits=len(assets),fieldEdits=sum(len(x.get('fieldChanges',[])) for x in mutation['changes']),
                            sameLengthFiles=sum(x['sameLength'] for x in mutation['changes']),sameMtimeFiles=sum(x['sameMtime'] for x in mutation['changes']))
    if scenario=='asset-lifecycle':mutation['totals'].update(renamedAssets=20,addedAssets=8,deletedAssets=8,createdFiles=1,deletedFiles=1)
    if scenario=='external-media':mutation['totals'].update(referencedInputFiles=80,
        publishedAssetDeclarations=sum(len(row.get('publishedDeclarations',[])) for row in mutation['changes']))
    (backup/'mutation.json').write_text(json.dumps(mutation,indent=2)+'\n',encoding='utf-8')
    return mutation


def restore(mod, mutation):
    mod=Path(mod).resolve();backup=Path(mutation['backupPath'])
    for row in reversed(mutation['changes']):
        path=mod/row['path']
        if row['operation']=='created':path.unlink()
        else:
            data=(backup/row['path']).read_bytes();need(sha_bytes(data)==row['beforeSha256'],'Original backup differs')
            path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
            os.utime(path,ns=(row['atimeNs'],row['mtimeNs']))
    return dict(restoredFiles=len(mutation['changes']),scenario=mutation['scenario'])
