#!/usr/bin/env python3
"""Derive the production SDK route manifest, plugin and oracle table from accepted formal routes.

This generates registration metadata only; it does not mark SDK integration accepted.
"""
from pathlib import Path
import json,re,sys
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'recovery/scripts'))
from AuditXmlComponentCatalog import audit
report=audit(); by_name={r['name']:r for r in report['entries']}
link=(ROOT/'recovery/tests/XmlPriorityLinkOracle.cs').read_text()
rows=[]
for name,tid,ai in re.findall(r'new Root\("([^"]+)", (\d+)u, (true|false)\)',link):
    row=by_name[name]
    assert int(tid)==row['typeId']
    assert not row['excludedRoot']
    rows.append(dict(name=name,typeId=int(tid),typeHash=row['typeHash'],schemaGlobal=row['schemaGlobal'],compiler='XmlaiCompiler' if ai=='true' else name+'Compiler'))
assert len(rows)==300 and len({r['name'] for r in rows})==300
excluded=[dict(name=r['name'],typeId=r['typeId']) for r in report['entries'] if r['excludedRoot']]
assert len(excluded)==1 and excluded[0]['name']=='TestGameObject'
manifest=dict(sourceBlob='00bb9e5df72d07a21204f85f9a9a2f1d9557f00b',scope='Static current SDK registration scope; runtime acceptance is recorded separately',sdkAcceptanceEvaluated=False,runtimeAcceptanceEvidence='recovery/evidence/xml-priority-sdk/acceptance.json',registeredEntries=len(rows),schemaGlobalEntries=sum(r['schemaGlobal'] for r in rows),nonGlobalEntries=sum(not r['schemaGlobal'] for r in rows),excluded=excluded,roots=rows)
(ROOT/'recovery/catalog/XmlSdkTypes.json').write_text(json.dumps(manifest,indent=2)+'\n')
p=ROOT/'recovery/src/Plugin/XmlPriorityPlugin.cs';s=p.read_text()
s=re.sub(r'// (?:Register the|Generated) \d+ recovered entries;?[^\n]*', '// Generated 300 recovered entries; see catalog/XmlSdkTypes.json and GenerateXmlSdkRoutes.py. Every payload is generated',s)
a=s.index('            switch (typeId)');b=s.index('            info.ProcessingHash',a)
s=s[:a]+'''            switch (typeId)
            {
'''+''.join(f'                case {r["typeId"]}u: info.TypeName = "{r["name"]}"; info.TypeHash = {r["typeHash"]}u; break;\n' for r in rows)+'''                default: throw new NotSupportedException("XmlPriority supports only its generated recovered scope; excluded roots remain unsupported.");
            }
'''+s[b:]
a=s.index('            switch (typeId)',s.index('public AssetBuffer ProcessInstance'));b=s.index('            if (!String.IsNullOrEmpty(trace))',a)
s=s[:a]+'''            switch (typeId)
            {
'''+''.join(f'''                case {r['typeId']}u:
                    typeName = "{r['name']}";
                    result = global::Recovered.{r['compiler']}.ProcessInstance(compilationInstance, platform);
                    break;
''' for r in rows)+'''                default: throw new NotSupportedException("XmlPriority supports only its generated recovered scope; excluded roots remain unsupported.");
            }
'''+s[b:]
p.write_text(s)
p=ROOT/'recovery/src/App/Application/BuiltinXmlRoutes.cs'
p.write_text('''#if NEWSdk_MONOLITHIC
// Explicit production scope generated from catalog/XmlSdkTypes.json.
// Non-global entries remain component routes; they are not new schema roots.
namespace NewModCompiler.Application
{
    internal static class BuiltinXmlRoutes
    {
        internal static readonly BuiltinCompilerRoute[] Entries = {
'''+''.join(f'            new BuiltinCompilerRoute({r["typeId"]}u, "{r["name"]}", {r["typeHash"]}u, "xml", {str(r["schemaGlobal"]).lower()}),\n' for r in rows)+'''        };
    }
}
#endif
''')
p=ROOT/'recovery/tests/XmlPrioritySdkOracle.cs';s=p.read_text();a=s.index('    // BEGIN GENERATED SDK ROUTES');b=s.index('    // END GENERATED SDK ROUTES',a)+len('    // END GENERATED SDK ROUTES')
s=s[:a]+'''    // BEGIN GENERATED SDK ROUTES
    static readonly Dictionary<string,uint> Supported=new Dictionary<string,uint>(StringComparer.Ordinal) {
'''+''.join(f'        {{"{r["name"]}",{r["typeId"]}u}},\n' for r in rows)+'''    };
    static readonly string[] NonGlobalRoots=new[]{'''+','.join(json.dumps(r['name']) for r in rows if not r['schemaGlobal'])+'''};
    static readonly uint[] ExcludedTypeIds=new uint[]{'''+','.join(str(r['typeId'])+'u' for r in excluded)+'''};
    // END GENERATED SDK ROUTES'''+s[b:];p.write_text(s)
print(json.dumps({k:manifest[k] for k in ['registeredEntries','schemaGlobalEntries','nonGlobalEntries','sdkAcceptanceEvaluated']}))
