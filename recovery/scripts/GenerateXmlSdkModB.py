#!/usr/bin/env python3
"""Generate deterministic schema-conforming real SDK fixtures for component groups B.

The local validator uses an in-memory Windows-path/QName whitespace adapter; SDK
schemas on disk remain unmodified. The real SDK/BAB validator is authoritative.
"""
from pathlib import Path
import argparse, copy, json, re, urllib.parse
from lxml import etree as E
ROOT=Path(__file__).resolve().parents[2]; X=ROOT/'SDK/Schemas/xsd';NS='uri:ea.com:eala:asset';XS='{http://www.w3.org/2001/XMLSchema}';XAS='{uri:ea.com:eala:asset:schema}'
INDEX={str(p.resolve()).lower():p.resolve() for p in X.rglob('*.xsd')}
class WindowsResolver(E.Resolver):
 def resolve(self,url,pub,context):
  key=str(Path(urllib.parse.unquote(url.removeprefix('file://'))).resolve()).lower()
  if key not in INDEX:return
  path=INDEX[key];text=path.read_text(encoding='utf-8-sig')
  def include(m):
   p=(path.parent/m[1].replace('\\','/')).resolve();return 'schemaLocation="'+str(INDEX.get(str(p).lower(),p))+'"'
  text=re.sub(r'schemaLocation="([^"]+)"',include,text)
  text=re.sub(r'\b(type|base|ref|itemType)="([^"]+)"',lambda m:m[1]+'="'+m[2].strip()+'"',text)
  return self.resolve_string(text,context,base_url=str(path))
PARSER=E.XMLParser();PARSER.resolvers.add(WindowsResolver())
SCHEMA=E.XMLSchema(E.parse(str(X/'CnC3Types.xsd'),PARSER))
TYPES={};ATTRGROUPS={};GROUPS={};ELEMENTS={};VISITED=set();TYPEPATHS={}
def gather(p):
 p=INDEX[str(p.resolve()).lower()]
 if p in VISITED:return
 VISITED.add(p)
 for node in E.parse(str(p)).getroot():
  if node.tag in (XS+'include',XS+'redefine'):gather(p.parent/node.get('schemaLocation').replace('\\','/'))
  elif node.get('name'):
   name=node.get('name');tag=E.QName(node).localname
   if tag in ('complexType','simpleType'):TYPES[name]=node;TYPEPATHS[name]=str(p.relative_to(ROOT))
   elif tag=='attributeGroup':ATTRGROUPS[name]=node
   elif tag=='group':GROUPS[name]=node
   elif tag=='element':ELEMENTS[name]=node

gather(X/'CnC3Types.xsd')
ROOTCHOICE=ELEMENTS['AssetDeclaration'].find('.//'+XS+'choice')
GLOBALS={node.get('name'):node.get('type') for node in ROOTCHOICE if node.tag==XS+'element'}
CAT=json.loads((ROOT/'recovery/catalog/XmlComponentOnlyTypes.json').read_text());GROUP_NAMES=sorted(CAT['groups'])[24:];ROOT_NAMES=[name for group in GROUP_NAMES for name in CAT['groups'][group]]
# Keep the historical component snapshot intact; later Joypad roots have their
# own catalog and join this profile only when a future full SDK matrix is run.
JOYPAD=json.loads((ROOT/'recovery/catalog/XmljoypadTypes.json').read_text())
GROUP_NAMES.append('joypad');ROOT_NAMES.extend(row['name'] for row in JOYPAD['roots'])
SOURCE_IDS={name:'RecoveryXmlB_'+name+'_Defaults' for name in ROOT_NAMES if name in GLOBALS and TYPES.get(GLOBALS[name]) is not None and TYPES[GLOBALS[name]].get('abstract')!='true'}
OMITTED_REFS=[];REACHED_TYPES=set();REACHED_ELEMENTS=set();WROTE_REFS=[];BOUNDARY=False
def local(name):return (name or '').split(':')[-1].strip()
def derives(name,target,seen=None):
 if name==target:return True
 if seen is None:seen=set()
 if name in seen:return False
 seen.add(name);node=TYPES.get(name)
 if node is None:return False
 for sub in node:
  if sub.tag in (XS+'restriction',XS+'extension') and derives(local(sub.get('base')),target,seen):return True
  if sub.tag in (XS+'complexContent',XS+'simpleContent'):
   for ext in sub:
    if ext.tag in (XS+'restriction',XS+'extension') and derives(local(ext.get('base')),target,seen):return True
 return False
def ref_type(type_name,declaration=None,seen=None):
 if declaration is not None and declaration.get(XAS+'refType'):return declaration.get(XAS+'refType')
 node=TYPES.get(local(type_name))
 if node is None:return None
 if node.get(XAS+'refType'):return node.get(XAS+'refType')
 for n in node:
  if n.get('base'):return ref_type(n.get('base'),n)
 return None
def value(type_name,rich=False,declaration=None,seen=None):
 name=local(type_name);seen=set() if seen is None else set(seen)
 if name in seen:return '0'
 seen.add(name);node=TYPES.get(name)
 target=ref_type(name,declaration)
 if target:
  candidates=[n for n in SOURCE_IDS if derives(GLOBALS[n],target)]
  if candidates:
   v=SOURCE_IDS[candidates[0]];WROTE_REFS.append({'type':name,'targetType':target,'asset':v});return v
  if derives(name,'WeakReference') or derives(name,'Poid'):return 'RecoveryXmlB_Weak_'+target
  OMITTED_REFS.append({'type':name,'targetType':target});return None
 if name in ('FileReference','FileName') or 'FileReference' in name:return None
 if name=='SageBool':return 'false' if BOUNDARY else ('true' if rich else 'false')
 if name in ('SageInt','SageUnsignedInt','SageUnsignedShort','SageDurationUnsignedInt'):
  if BOUNDARY:return {'SageInt':'-2147483648','SageUnsignedInt':'4294967295','SageUnsignedShort':'65535','SageDurationUnsignedInt':'1'}[name]
  return '3' if rich else '1'
 if name in ('SageReal','Velocity','PositiveNonZeroReal'):return '1e30' if BOUNDARY else ('0.5' if rich else '1.0')
 if name in ('Time','DurationUnsignedInt'):return '1ms' if BOUNDARY else '250ms'
 if name=='Angle':return '360d' if BOUNDARY else '30d'
 if name=='Percentage':return '100%' if BOUNDARY else '50%'
 if node is not None:
  restriction=node.find(XS+'restriction')
  if restriction is not None:
   enums=restriction.findall(XS+'enumeration')
   if enums:return enums[-1 if rich and len(enums)>1 else 0].get('value')
   v=value(restriction.get('base'),rich,restriction,seen)
   # Basic numeric facets constrain the representative value.
   if v is not None:
    for facet in restriction:
     kind=E.QName(facet).localname if isinstance(facet.tag,str) else ''
     if kind=='length' and v is not None:v=('x'*int(facet.get('value')))
     elif kind=='maxLength' and v is not None:v=v[:int(facet.get('value'))]
     elif kind=='minInclusive':v=facet.get('value')
     elif kind=='minExclusive':
      try:v=str(float(facet.get('value'))+1)
      except ValueError:pass
   return v
  list_node=node.find(XS+'list')
  if list_node is not None:
   v=value(list_node.get('itemType'),rich,list_node,seen)
   return None if v is None else (v+' '+v if rich else v)
  union=node.find(XS+'union')
  if union is not None:return value(union.get('memberTypes','xs:string').split()[0],rich,union,seen)
 if name in ('boolean','SageBool'):return 'false' if BOUNDARY else ('true' if rich else 'false')
 if name in ('float','double','decimal','SageReal','NonNegativeReal','PositiveReal'):return '1e30' if BOUNDARY else ('0.5' if rich else '1.0')
 if name in ('byte','unsignedByte','short','unsignedShort','int','unsignedInt','long','unsignedLong','integer','nonNegativeInteger','positiveInteger','SageInt','SageUnsignedInt'):
  if BOUNDARY:return {'byte':'-128','unsignedByte':'255','short':'-32768','unsignedShort':'65535','int':'-2147483648','unsignedInt':'4294967295','long':'2147483647','unsignedLong':'4294967295','integer':'2147483647','nonNegativeInteger':'0','positiveInteger':'1','SageInt':'-2147483648','SageUnsignedInt':'4294967295'}[name]
  return '3' if rich else '1'
 if name in ('SageTime','Time'):return '250ms'
 if 'Percentage' in name or 'Percent' in name:return '50%'
 if name in ('SageAngle','Angle'):return '30d'
 if name in ('hexBinary',):return '01020304'
 if name=='base64Binary':return 'AQIDBA=='
 return 'RecoveryXmlB_Value' if rich else 'RecoveryXmlB_Default'

def fill(node,type_name,rich=False,depth=0,stack=()):
 name=local(type_name);REACHED_TYPES.add(name)
 if depth>10 or stack.count(name)>0:rich=False
 if depth>16:return
 decl=TYPES.get(name)
 if decl is None:
  v=value(type_name,rich);node.text='' if v is None else v;return
 if decl.tag==XS+'simpleType':
  v=value(type_name,rich);node.text='' if v is None else v;return
 process(node,decl,rich,depth,stack+(name,))

def process(node,decl,rich,depth,stack):
 for item in decl:
  if not isinstance(item.tag,str):continue
  kind=E.QName(item).localname
  if kind in ('annotation','simpleType'):continue
  if kind in ('complexContent','simpleContent'):
   for ext in item:
    if not isinstance(ext.tag,str):continue
    if ext.tag in (XS+'extension',XS+'restriction'):
     base=local(ext.get('base'));base_decl=TYPES.get(base)
     if base_decl is not None and base_decl.tag==XS+'complexType':process(node,base_decl,rich,depth,stack)
     else:
      v=value(ext.get('base'),rich);node.text='' if v is None else v
     process(node,ext,rich,depth,stack)
  elif kind=='attributeGroup':
   group=ATTRGROUPS.get(local(item.get('ref')))
   if group is not None:process(node,group,rich,depth,stack)
  elif kind=='attribute':
   attr=item.get('name');required=item.get('use')=='required'
   if not attr or attr in ('inheritFrom','TypeId','typeHashCode','isPrototype') or item.get('use')=='prohibited':continue
   if attr=='id':
    if required and node.getparent() is not None:node.set('id','RecoveryXmlB_Nested_'+str(len(REACHED_ELEMENTS)))
    continue
   if not rich and not required:continue
   v=item.get('fixed') or value(item.get('type','xs:string'),rich,item)
   if v is not None:node.set(attr,v)
   elif required:node.set(attr,'')
  elif kind in ('sequence','all','choice','group'):particle(node,item,rich,depth,stack)

def particle(node,part,rich,depth,stack):
 kind=E.QName(part).localname
 if kind=='group':
  group=GROUPS.get(local(part.get('ref')))
  if group is not None:
   for child in group:
    if isinstance(child.tag,str) and E.QName(child).localname in ('sequence','choice','all'):particle(node,child,rich,depth,stack)
  return
 if int(part.get('minOccurs','1'))==0 and not rich:return
 children=[c for c in part if isinstance(c.tag,str) and c.tag in (XS+'element',XS+'sequence',XS+'choice',XS+'all',XS+'group')]
 if kind=='choice' and part.get('maxOccurs','1')=='1':children=children[:1]
 for child in children:
  if child.tag!=XS+'element':particle(node,child,rich,depth,stack);continue
  minimum=int(child.get('minOccurs','1'))
  if minimum==0 and not rich:continue
  count=max(minimum,1)
  if rich and child.get('maxOccurs','1')!='1':count=max(count,3 if BOUNDARY else 2)
  type_name=child.get('type');tag=child.get('name');reference=child.get('ref')
  if reference:
   tag=local(reference);c=ELEMENTS.get(tag)
   if c is not None:type_name=c.get('type')
  if not tag:continue
  td=TYPES.get(local(type_name))
  if td is not None and td.get('abstract')=='true':continue
  if depth>7 and minimum==0:continue
  for index in range(count):
   c=E.SubElement(node,'{'+NS+'}'+tag);REACHED_ELEMENTS.add(tag)
   inline=child.find(XS+'complexType')
   if inline is not None:process(c,inline,rich,depth+1,stack)
   elif type_name:fill(c,type_name,rich,depth+1,stack)
   else:c.text='RecoveryXmlB_Value'

def document():return E.Element('{'+NS+'}AssetDeclaration',nsmap={None:NS})
def validate_asset(asset):
 doc=document();doc.append(copy.deepcopy(asset))
 return SCHEMA.validate(doc),[str(x) for x in SCHEMA.error_log]

def main():
 global BOUNDARY
 options=argparse.ArgumentParser(description=__doc__);options.add_argument('--include-boundaries',action='store_true',help='Add third valid boundary/list-growth variants after the baseline SDK batch');args=options.parse_args()
 output=ROOT/'SDK/Mods/XmlCompilerRecoveryB/Data';output.mkdir(parents=True,exist_ok=True)
 assets=document();cases=[];blocked=[]
 for name in ROOT_NAMES:
  if name not in SOURCE_IDS:
   blocked.append({'name':name,'reason':'not a concrete global AssetDeclaration entry; SDK reachability requires real nested owner/custom source pipeline'});continue
  variants=[('Defaults',False),('Fields',True)]+([('Boundaries',True)] if args.include_boundaries else [])
  for variant,rich in variants:
   BOUNDARY=variant=='Boundaries'
   node=E.Element('{'+NS+'}'+name);fill(node,GLOBALS[name],rich);node.set('id','RecoveryXmlB_'+name+'_'+variant)
   # Valid BAB metadata remains on the SDK node; the original XML marshaler never reads it.
   if name=='MusicScriptConditionNugget_LocalPlayerIsObserver' and variant=='Fields':node.set('typeHashCode','00000000')
   good,errors=validate_asset(node)
   if not good:blocked.append({'name':name,'variant':variant,'errors':errors});continue
   assets.append(node);cases.append({'name':name,'id':node.get('id'),'variant':variant,'schemaValidated':True,'attributes':len(node.attrib),'descendants':len(list(node.iter()))-1})
  BOUNDARY=False
  # Legal inheritance exercises BAB merge/default preprocessing of the same concrete root.
  base=next((n for n in assets if E.QName(n).localname==name and n.get('id','').endswith('_Defaults')),None)
  if base is not None:
   derived=copy.deepcopy(base);derived.set('id','RecoveryXmlB_'+name+'_Inherited');derived.set('inheritFrom',base.get('id'))
   good,errors=validate_asset(derived)
   if good:assets.append(derived);cases.append({'name':name,'id':derived.get('id'),'variant':'Inherited','schemaValidated':True,'inheritFrom':base.get('id')})
 E.ElementTree(assets).write(str(output/'Probe.xml'),encoding='utf-8',xml_declaration=True,pretty_print=True)
 mod=document();E.SubElement(mod,'{'+NS+'}Tags');includes=E.SubElement(mod,'{'+NS+'}Includes')
 for src in ('DATA:static.xml','DATA:global.xml','DATA:audio.xml'):E.SubElement(includes,'{'+NS+'}Include',type='reference',source=src)
 E.SubElement(includes,'{'+NS+'}Include',type='all',source='Probe.xml');E.ElementTree(mod).write(str(output/'Mod.xml'),encoding='utf-8',xml_declaration=True,pretty_print=True)
 report={'scope':'Real SDK source fixture plan; local schema validation is not SDK success or runtime coverage','groups':GROUP_NAMES,'cases':cases,'blocked':blocked,'sourceGlobalTypes':sorted(SOURCE_IDS),'schemaReachableTypes':sorted(REACHED_TYPES),'schemaReachableElements':sorted(REACHED_ELEMENTS),'generatedReferences':WROTE_REFS,'omittedOptionalUnresolvedReferences':OMITTED_REFS,'noHandAuthoredTypeId':True,'validatorAdapter':'read-only canonicalization of Windows include casing/backslashes and QName whitespace; original XSD files unchanged'}
 (ROOT/'recovery/evidence/xml-sdk-mod-b-fixtures.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps({'cases':len(cases),'globalTypes':len({c['name'] for c in cases}),'blocked':len(blocked),'schemaErrors':sum('errors' in r for r in blocked),'assets':len(assets)}))
 if any('errors' in r for r in blocked):print(json.dumps([r for r in blocked if 'errors' in r][:8],indent=2))
if __name__=='__main__':main()
