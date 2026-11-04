"""Small signal/real-source AudioFile fixtures, shared by plugin and production CLI tests."""
from pathlib import Path
import json, math, struct, wave
import xml.etree.ElementTree as ET
NS='uri:ea.com:eala:asset'
ET.register_namespace('',NS)
def chunk(tag,data,be=False):
    return tag+struct.pack('>I' if be else '<I',len(data))+data+(b'\0' if len(data)&1 else b'')
def make(out,repo):
    out=Path(out);out.mkdir(parents=True,exist_ok=True)
    root=ET.Element('{%s}AssetDeclaration'%NS);cases=[]
    def add(name,channels,rate=22050,quality=75,stream=False,loop=False,aiff=False,frames=8193,target=None,bits=16):
        samples=[int(11000*math.sin(2*math.pi*(337+173*c)*i/rate)+1700*math.sin(2*math.pi*97*i/rate)) for i in range(frames) for c in range(channels)]
        start,end=(frames//3,frames-113) if loop else (-1,frames-1)
        path=out/(name+('.aiff' if aiff else '.wav'))
        if aiff:
            # 22050Hz in AIFF's IEEE extended representation.
            exponent=rate.bit_length()-1
            extended=struct.pack('>HQ',16383+exponent,rate<<(63-exponent))
            data=chunk(b'COMM',struct.pack('>hIh',channels,frames,bits)+extended,True)
            if loop:
                markers=struct.pack('>H',2)+struct.pack('>hI',1,start)+b'\x01a'+struct.pack('>hI',2,end+1)+b'\x01b'
                data+=chunk(b'MARK',markers,True)
                data+=chunk(b'INST',bytes([60,0,0,127,0,127])+struct.pack('>hHHHHHH',0,1,1,2,0,0,0),True)
            data+=chunk(b'SSND',bytes(8)+(struct.pack('>%dh'%len(samples),*samples) if bits==16 else b''.join(((v<<8)&0xffffff).to_bytes(3,'big') for v in samples)),True)
            path.write_bytes(b'FORM'+struct.pack('>I',len(data)+4)+b'AIFF'+data)
        else:
            data=chunk(b'fmt ',struct.pack('<HHIIHH',1,channels,rate,rate*channels*(bits//8),channels*(bits//8),bits))
            data+=chunk(b'data',(struct.pack('<%dh'%len(samples),*samples) if bits==16 else b''.join(((v<<8)&0xffffff).to_bytes(3,'little') for v in samples)))
            if loop:data+=chunk(b'smpl',struct.pack('<15I',0,0,0,60,0,0,0,1,0,0,0,start,end,0,0))
            path.write_bytes(b'RIFF'+struct.pack('<I',len(data)+4)+b'WAVE'+data)
        attrs={'id':name,'File':str(path.resolve()),'PCCompression':'EALAYER3','IsStreamedOnPC':str(stream).lower(),'SubtitleStringName':'EAL3:'+name}
        if quality is not None:attrs['PCQuality']=str(quality)
        if target:attrs['PCSampleRate']=str(target)
        ET.SubElement(root,'{%s}AudioFile'%NS,attrs)
        cases.append(dict(id=name,channels=channels,rate=target or rate,sourceRate=rate,frames=frames,quality=75 if quality is None else quality,stream=stream,loop=loop,loopStart=start,loopEnd=end,aiff=aiff,file=str(path.resolve())))
    for channels in (1,2,4,6,8):
        for stream in (False,True):
            for loop in (False,True):add('c%d_s%d_l%d'%(channels,stream,loop),channels,stream=stream,loop=loop)
    for rate in (16000,24000,32000,44100,48000):add('rate%d'%rate,2,rate=rate,stream=True)
    for q in (0,100,None):add('quality%s'%q,2,quality=q)
    add('quality075',2,quality='075')
    for channels in (1,2,4,6):add('aiff%d'%channels,channels,aiff=True,loop=True,stream=channels!=1)
    add('wave24',2,stream=True,bits=24)
    add('aiff24',2,stream=True,loop=True,aiff=True,bits=24)
    add('resampleLoop',2,target=44100,loop=True,stream=True)
    add('wave24c8',8,loop=True,bits=24)
    add('resampleLoop8',8,target=44100,loop=True,stream=True)
    for frames in (1,2,46,47,48,575,576,577,1105,1152):add('short%d'%frames,1,frames=frames,stream=True)
    for index in (0,1):
        name='real%d'%index;source=Path(repo)/('recovery/fixtures/audio/main/real-%d.wav'%index)
        with wave.open(str(source)) as w: ch,rate,frames=w.getnchannels(),w.getframerate(),w.getnframes()
        ET.SubElement(root,'{%s}AudioFile'%NS,{'id':name,'File':str(source.resolve()),'PCCompression':'EALAYER3','IsStreamedOnPC':'true'})
        cases.append(dict(id=name,channels=ch,rate=rate,sourceRate=rate,frames=frames,quality=75,stream=True,loop=False,loopStart=-1,loopEnd=frames-1,aiff=False,file=str(source.resolve())))
    ET.ElementTree(root).write(out/'assets.xml',encoding='utf-8',xml_declaration=True)
    (out/'cases.json').write_text(json.dumps(cases,indent=2))
    return cases
if __name__=='__main__':
    import sys
    make(sys.argv[1],sys.argv[2])
