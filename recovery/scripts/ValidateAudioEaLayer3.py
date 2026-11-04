"""Decode actual AudioCompiler assets with vgmstream; verify signal, timing and containers."""
import argparse, array, json, math, os, struct, subprocess, sys
from pathlib import Path

def need(ok,message):
    if not ok: raise AssertionError(message)
def wav(path):
    b=Path(path).read_bytes();fmt=None;data=None
    for_start=12
    while for_start+8<=len(b):
        tag=b[for_start:for_start+4];size=struct.unpack_from('<I',b,for_start+4)[0];payload=b[for_start+8:for_start+8+size]
        if tag==b'fmt ':fmt=payload
        if tag==b'data':data=payload
        for_start+=8+size+(size&1)
    need(fmt is not None and data is not None,'Decoded WAV chunks absent')
    kind,ch,rate=struct.unpack_from('<HHI',fmt);bits=struct.unpack_from('<H',fmt,14)[0]
    need(kind in (1,65534) and bits==16,'Expected PCM16 decode')
    values=array.array('h',data)
    if sys.byteorder!='little':values.byteswap()
    return ch,rate,values

def run(root,decoder):
    cases=json.loads((root/'fixtures/cases.json').read_text());rows=[]
    receipt={'sourceCommit':os.environ.get('GITHUB_SHA'),'runId':os.environ.get('GITHUB_RUN_ID'),'passed':False,'cases':rows,'gamePlaybackTested':False}
    try:
        for mode in ('Debug','Release'):
            for case in cases:
                name=case['id'];stem=root/mode/'jobs1'/name
                for ext in ('.runtime','.relo','.imp','.cdata','.snr')+ (('.sns',) if case['stream'] else ()):
                    need(stem.with_suffix(ext).read_bytes()==(root/mode/'jobs4'/name).with_suffix(ext).read_bytes(),f'{mode}/{name}: jobs1/4 differs ({ext})')
                header=stem.with_suffix('.snr').read_bytes();runtime=stem.with_suffix('.runtime').read_bytes()
                head0,head1=struct.unpack_from('>II',header)
                need(head0>>24==5,f'{name}: codec is not EALayer3 V1')
                need(((head0>>18)&63)+1==case['channels'] and head0&0x3ffff==case['rate'],f'{name}: SNR rate/channel mismatch')
                need((head1>>30)==int(case['stream']) and bool(head1&0x20000000)==case['loop'],f'{name}: SNR play/loop mode differs')
                ratio=case['rate']/case['sourceRate'];frames=int(case['frames']*ratio)
                loop_start=int(case['loopStart']*ratio+0.5);loop_end=int(case['loopEnd']*ratio+0.5)
                count=loop_end+1 if case['loop'] else frames
                need(head1&0x1fffffff==count,f'{name}: SNR sample count differs')
                need(struct.unpack_from('<II',runtime,12)==(frames,case['rate']) and runtime[28]==case['channels'],f'{name}: runtime metadata differs')
                custom=stem.with_suffix('.cdata').read_bytes();offset=0 if case['stream'] else (12 if case['loop'] else 8)
                loop_offset=struct.unpack_from('>I',header,12)[0] if case['stream'] and case['loop'] else None
                block_frames=0;segments=0;ends=[]
                while offset<len(custom):
                    size,n=struct.unpack_from('>II',custom,offset);last=bool(size&0x80000000);size&=0xffffff
                    need(size>=8 and offset+size<=len(custom),f'{name}: bad SNS block boundary')
                    block_frames+=n;offset+=size
                    if last:segments+=1;ends.append(offset)
                need(block_frames==count and offset==len(custom),f'{name}: block sample counts differ')
                if case['stream']:
                    need(segments==(2 if case['loop'] and loop_start else 1),f'{name}: segment end markers differ')
                    need(all(end%64==0 for end in ends),f'{name}: missing segment padding')
                    if loop_offset is not None:need(loop_offset==(ends[0] if loop_start else 0),f'{name}: stream loop byte offset differs')
                result=subprocess.run([decoder,'-m','-I',str(stem.with_suffix('.snr'))],capture_output=True,text=True,timeout=20)
                need(result.returncode==0,f'{name}: decoder cannot read: {result.stderr}')
                info=json.loads(result.stdout);stem.with_suffix('.info.json').write_text(json.dumps(info,indent=2))
                need(info['sampleRate']==case['rate'] and info['channels']==case['channels'] and info['numberOfSamples']==count,f'{name}: independent decoder metadata differs: {info}')
                if case['loop']:need(info['loopingInfo']=={'start':loop_start,'end':loop_end+1},f'{name}: decoder loop differs')
                decoded=stem.with_suffix('.decoded.wav')
                result=subprocess.run([decoder,'-i','-o',str(decoded),str(stem.with_suffix('.snr'))],capture_output=True,text=True,timeout=20)
                need(result.returncode==0,f'{name}: decode failed: {result.stderr}')
                ch,rate,values=wav(decoded)
                need(ch==case['channels'] and rate==case['rate'] and len(values)==count*ch,f'{name}: decoded PCM length/rate/channel differs')
                correlations=[]
                if not name.startswith('real') and count>=2048:
                    order=list(range(ch)) if case['aiff'] or ch not in (4,6) else ([0,1,3,2] if ch==4 else [0,2,1,4,5,3])
                    for c,source_c in enumerate(order):
                        dot=power=reference=0.0
                        for i in range(160,count-160):
                            v=values[i*ch+c];t=i/case['rate']
                            expected=11000*math.sin(2*math.pi*(337+173*source_c)*t)+1700*math.sin(2*math.pi*97*t)
                            dot+=v*expected;power+=v*v;reference+=expected*expected
                        corr=dot/math.sqrt(max(1,power*reference));correlations.append(corr)
                        need(corr>0.85,f'{name} channel {c}: waveform/alignment differs, correlation={corr}')
                rows.append({'mode':mode,'id':name,'bytes':len(custom),'decodedFrames':count,'correlations':correlations,'jobsEqual':True})
        # Meaningful quality behavior, not just accepted attributes.
        low=root/'Release/jobs1/quality0.cdata';high=root/'Release/jobs1/quality100.cdata'
        need(low.read_bytes()!=high.read_bytes() and high.stat().st_size>low.stat().st_size,'PCQuality did not change real compressed payload/bitrate')
        need((root/'Release/jobs1/quality075.cdata').read_bytes()==(root/'Release/jobs1/qualityNone.cdata').read_bytes(), 'RA3 decimal quality/default semantics differ')
        receipt['passed']=True
    finally:
        (root/'validation.json').write_text(json.dumps(receipt,indent=2))
    print('PASS EALayer3: %d encoded/decoded assets, runtime/SNR/SNS/loops/signal/channel order/jobs checked'%len(rows))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--decoder',required=True);a=p.parse_args();run(a.root,a.decoder)
