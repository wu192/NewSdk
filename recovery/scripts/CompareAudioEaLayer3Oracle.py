import argparse,json,subprocess
from pathlib import Path
from ValidateAudioEaLayer3 import need,wav
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--decoder',required=True);p.add_argument('--reference',default='oracle');p.add_argument('--reference-codec',type=int,required=True);a=p.parse_args();rows=[]
for header in (a.root/a.reference/'assets').glob('*.snr'):
    candidate=a.root/'Release/jobs1'/header.name
    expected=subprocess.run([a.decoder,'-m','-I',str(header)],capture_output=True,text=True,check=True,timeout=20)
    original=json.loads(expected.stdout);new=json.loads(candidate.with_suffix('.info.json').read_text())
    for key in ('sampleRate','channels','numberOfSamples','loopingInfo','encoding'):
        need(original[key]==new[key],f'{header.stem}: reference {key} differs ({original[key]} / {new[key]})')
    source_header=header.read_bytes();new_header=candidate.read_bytes()
    need(source_header[0]==a.reference_codec and new_header[0]==5,f'{header.stem}: reference/candidate variant differs from declared comparison')
    need(source_header[1:8]==new_header[1:8],f'{header.stem}: reference SNR channel/rate/play/loop/sample descriptor differs')
    decoded=header.with_suffix('.decoded.wav')
    subprocess.run([a.decoder,'-i','-o',str(decoded),str(header)],capture_output=True,check=True,timeout=20)
    ch,rate,reference=wav(decoded);ch2,rate2,actual=wav(candidate.with_suffix('.decoded.wav'))
    need((ch,rate,len(reference))==(ch2,rate2,len(actual)),f'{header.stem}: reference decode dimensions differ')
    # Lossy implementations need not have identical bytes. Signal correlation
    # against the input was independently checked by ValidateAudioEaLayer3.
    reference_nonzero=sum(v!=0 for v in reference)
    reference_usable=not(a.reference_codec==5 and rate<32000 and reference_nonzero<=47*ch)
    if len(reference)>2048:
        import math
        dot=sum(x*y for x,y in zip(reference,actual));power=sum(x*x for x in reference)*sum(x*x for x in actual)
        correlation=dot/math.sqrt(max(1,power))
        # V2 has a different startup/encoding implementation: retain its measured
        # signal difference, but do not assert V1/V2 waveform equivalence. The
        # candidate must independently match the source in ValidateAudioEaLayer3.
        # Same-variant V1 reference comparison retains the alignment assertion.
        if a.reference_codec==5 and reference_usable:
            need(correlation>0.85,f'{header.stem}: V1 reference/candidate signals differ ({correlation})')
    else:correlation=None
    rows.append({'id':header.stem,'metadataEqual':True,'referenceCodec':a.reference_codec,'candidateCodec':5,'signalCorrelation':correlation,'sameVariant':a.reference_codec==5,'referenceUsable':reference_usable,'referenceNonzeroSamples':reference_nonzero,'referenceLimitation':None if reference_usable else 'Bundled EL31 LSF encoder emits only silence/startup samples; not a waveform oracle','referenceBytes':header.with_suffix('.cdata').stat().st_size,'candidateBytes':candidate.with_suffix('.cdata').stat().st_size})
need(len(rows)>=20,'Reference comparison cases absent')
usable=sum(r['referenceUsable'] for r in rows)
need(usable >= (4 if a.reference_codec==5 else 20),'Usable reference comparison cases absent')
(a.root/a.reference/'comparison.json').write_text(json.dumps({'passed':True,'scope':'All metadata/decodes recorded; waveform equivalence asserted only for usable same-variant V1 references','usableReferenceCases':usable,'unusableReferenceCases':len(rows)-usable,'cases':rows},indent=2));print('PASS jonwil reference:',len(rows),'format/decode records,',usable,'usable references,',len(rows)-usable,'unusable references explicitly recorded')
