# Original SDK versus current compiler

Status: outputs-differ

Complete MOD input copies; base stream only, loose output. No BIG/SKUDEF, APT or global-metadata timing.
Same Windows VM, SDK, physical MOD input, Schema, cwd and ordered search roots. Separate caches.
Fresh process per sample, process launch through exit includes CLI/report overhead. Hash checks excluded.
Cold = initially absent compiler cache/output namespace; OS page cache is uncontrolled.
New Release jobs4, performance off; original BAB has its original serial implementation.

| MOD | Mode | Phase | n/engine | Original seconds | New seconds | New time change |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| mymod | both-deflate | cold | 3 | 0.858703 | 0.711169 | -17.18% |
| mymod | both-deflate | hot | 6 | 0.547668 | 0.548666 | +0.18% |
| mymod | none | uncached | 1 | 0.858731 | 0.692825 | -19.32% |
| upheaval | both-deflate | cold | 3 | 92.655592 | 28.497811 | -69.24% |
| upheaval | both-deflate | hot | 6 | 2.399294 | 1.332181 | -44.48% |
| upheaval | none | uncached | 1 | 92.880339 | 27.599057 | -70.29% |

All raw samples, CPU/peak working set and output identities are in measurement.json.
All cache-state repeats must match their own engine baseline byte for byte.
Cross-engine differences, if present, are retained below; such results do not establish equivalent output.
Small sample counts describe this VM and workload; no statistical significance or game-runtime claim.

- Output mismatch: upheaval trial 0 both-deflate cold: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 0 both-deflate hot: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 0 both-deflate hot: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 1 both-deflate cold: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 1 both-deflate hot: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 1 both-deflate hot: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 2 both-deflate cold: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 2 both-deflate hot: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 2 both-deflate hot: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
- Output mismatch: upheaval trial 3 none uncached: mod.bin, mod.imp, mod.manifest, mod.relo, mod/cdata/166b084d.53c81e47.0002c491.5e3d73cb.cdata, mod/cdata/166b084d.53c81e47.0002c491.a87811ca.cdata, mod/cdata/166b084d.53c81e47.000d6515.79c39f6b.cdata, mod/cdata/166b084d.53c81e47.000d6515.ecf27dae.cdata
