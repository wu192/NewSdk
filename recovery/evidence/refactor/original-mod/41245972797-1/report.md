# Original SDK versus current compiler

Status: passed

Complete MOD input copies; base stream only, loose output. No BIG/SKUDEF, APT or global-metadata timing.
Same Windows VM, SDK, physical MOD input, Schema, cwd and ordered search roots. Separate caches.
Fresh process per sample, process launch through exit includes CLI/report overhead. Hash checks excluded.
Cold = initially absent compiler cache/output namespace; OS page cache is uncontrolled.
New Release jobs4, performance off; original BAB has its original serial implementation.

| MOD | Mode | Phase | n/engine | Original seconds | New seconds | New time change |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| mymod | both-deflate | cold | 3 | 0.556325 | 0.458682 | -17.55% |
| mymod | both-deflate | hot | 6 | 0.354725 | 0.362145 | +2.09% |
| mymod | none | uncached | 1 | 0.549737 | 0.462649 | -15.84% |
| upheaval | both-deflate | cold | 3 | 50.839589 | 18.607911 | -63.40% |
| upheaval | both-deflate | hot | 6 | 1.487778 | 0.866732 | -41.74% |
| upheaval | none | uncached | 1 | 48.779128 | 16.690810 | -65.78% |

All raw samples, CPU/peak working set and output identities are in measurement.json.
All cache-state repeats must match their own engine baseline byte for byte.
Cross-engine differences, if present, are retained below; such results do not establish equivalent output.
Small sample counts describe this VM and workload; no statistical significance or game-runtime claim.

