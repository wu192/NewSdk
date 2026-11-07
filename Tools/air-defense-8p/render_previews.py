from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json, argparse
p=argparse.ArgumentParser();p.add_argument('map_folder');p.add_argument('--name',required=True);p.add_argument('--overview',required=True);p.add_argument('--data-dir',default='work');a=p.parse_args();data=Path(a.data_dir)
meta=json.loads((data/'final-preview/terrain.json').read_text());w,h,b=meta['width'],meta['height'],meta['border']
colors=[(142,125,39),(151,150,128),(137,120,80),(149,113,54),(220,226,228),(62,67,65),(169,161,137)]
tex=(data/'final-preview/textures.u8').read_bytes();pixels=bytearray()
for y in range(h-1,-1,-1):
 for x in range(w):pixels.extend(colors[tex[y*w+x]])
terrain=Image.frombytes('RGB',(w,h),bytes(pixels));d=ImageDraw.Draw(terrain)
objs=json.loads((data/'final-objects.json').read_text())
def pos(o):return (o['Position']['X']/10+b,h-1-(o['Position']['Y']/10+b))
roads=[o for o in objs if o['RoadOption']]
for o1,o2 in zip(roads[::2],roads[1::2]):d.line([pos(o1),pos(o2)],fill=(51,48,34),width=2)
folder=Path(a.map_folder);folder.mkdir(parents=True,exist_ok=True)
# Preserve the original archive's PNG-encoded .tga preview convention.
terrain.save(folder/(a.name+'_art.tga'),format='PNG')
preview=terrain.copy();d=ImageDraw.Draw(preview);font=ImageFont.truetype('/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc',20)
for o in objs:
 if o['TypeName']=='OreNode':
  x,y=pos(o);d.ellipse((x-3,y-3,x+3,y+3),fill=(255,214,83),outline=(31,39,38))
for o in objs:
 n=o['Properties'].get('waypointName','')
 if n.endswith('_Start'):
  player=int(n.split('_')[1]);x,y=pos(o);c=(80,220,175) if player>=7 else (110,185,255)
  d.ellipse((x-11,y-11,x+11,y+11),fill=c,outline=(25,30,38),width=2)
  d.text((x,y-30),str(player),font=font,fill=(255,255,255),anchor='mm',stroke_width=2,stroke_fill=(20,25,30))
thumb=Image.new('RGB',(500,500),(31,38,46));small=preview.resize((500,round(h/w*500)),Image.Resampling.LANCZOS);thumb.paste(small,(0,(500-small.height)//2));thumb.save(folder/(a.name+'_pic.tga'),format='PNG')
canvas=Image.new('RGB',(w+80,h+148),(23,32,43));canvas.paste(preview,(40,95));d=ImageDraw.Draw(canvas)
ft=ImageFont.truetype('/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc',27);fs=ImageFont.truetype('/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc',18)
d.text((40,25),'陆地空战塔防 · 8 人右扩版',font=ft,fill=(235,241,247))
d.text((40,64),'总尺寸 956 × 760  |  可玩区域 796 × 600  |  右侧增加 196 格',font=fs,fill=(171,190,207))
d.rectangle((40+680,95+80,40+876,95+680),outline=(80,220,175),width=3)
d.text((40,h+110),'蓝色 1–6：原位置    绿色 7–8：新增位置    黄色：矿点',font=fs,fill=(203,217,229))
canvas.save(a.overview)
