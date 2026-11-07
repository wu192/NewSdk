"""Rebuild the 8-player map from the original uploaded archive after preparing the parser dependency."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,sys,zipfile
parser=argparse.ArgumentParser();parser.add_argument('source_zip',type=Path);parser.add_argument('output_directory',type=Path);a=parser.parse_args()
tool=Path(__file__).resolve().parent;out=a.output_directory.resolve();source=out/'work'/'source';source.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(a.source_zip) as z:
 for f in z.infolist():
  target=(source/f.filename).resolve()
  if not target.is_relative_to(source.resolve()):raise ValueError('Archive entry escapes extraction directory')
 z.extractall(source)
originals=list(source.rglob('*.map'))
if len(originals)!=1:raise ValueError('Expected one original .map')
original=originals[0];name=original.stem+'_8p';dest=out/name;dest.mkdir(parents=True,exist_ok=True);reports=out/'reports'
subprocess.run(['dotnet','run','--project',str(tool/'Editor.csproj'),'--',str(original),str(dest/(name+'.map')),str(reports)],check=True)
subprocess.run([sys.executable,str(tool/'render_previews.py'),str(dest),'--name',name,'--overview',str(out/'8p-layout.png'),'--data-dir',str(reports)],check=True)
shutil.copyfile(original.parent/'map.str',dest/'map.str')
objects=json.loads((reports/'final-objects.json').read_text())
info={'mapWidth':956,'mapHeight':760,'border':80,'pos':[]}
for o in objects:
 label=o['TypeName'] if o['TypeName']=='OreNode' else o['Properties'].get('waypointName','')
 if label=='OreNode' or label.endswith('_Start'):info['pos'].append({'name':label,'x':o['Position']['X'],'y':o['Position']['Y']})
(dest/(name+'_info.json')).write_text(json.dumps(info,ensure_ascii=False,separators=(',',':')),encoding='utf8')
ini=json.loads(''.join(chr(ord(c)-3) for c in (original.parent/'map.ini').read_text()))
ini.update({'0':name,'2':['map.str',name+'.map',name+'_art.tga',name+'_info.json',name+'_pic.tga','map.ini'],'3':hashlib.md5((dest/(name+'.map')).read_bytes()).hexdigest(),'5':'8','6':'956X760','7':name})
(dest/'map.ini').write_text(''.join(chr(ord(c)+3) for c in json.dumps(ini,ensure_ascii=True)),encoding='utf8')
with zipfile.ZipFile(out/'land-air-defense-8p.zip','w',zipfile.ZIP_DEFLATED) as z:
 for file in sorted(dest.iterdir()):z.write(file,file.relative_to(out))
print('Built',out/'land-air-defense-8p.zip')
