# New SDK MOD size timing

Status: passed

Release; default caches; detailed profiling; auto content + BIG/SKUDEF/Additional.
Cold = empty compiler caches. Hot = fresh process, compiler caches retained. OS page cache uncontrolled.
Preparation, build and verification excluded from sample wall time. Medium is a labelled real Texture subset.

| Tier | Phase | n | Process ms | CPU ms | Compile ms | Package ms | Publish ms | Assets compiled | XML parsed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| small | cold | 1 | 2459.40 | 1031.25 | 1385.00 | 65.48 | 15.16 | 47 | 11 |
| small | hot | 2 | 587.14 | 585.94 | 406.85 | 26.85 | 12.87 | 0.0 | 0.0 |
| medium | cold | 1 | 1166.31 | 1046.88 | 936.76 | 56.97 | 26.82 | 96 | 18 |
| medium | hot | 2 | 652.63 | 664.06 | 441.21 | 51.10 | 24.53 | 0.0 | 0.0 |
| large | cold | 1 | 75285.51 | 74906.25 | 74684.70 | 326.09 | 49.08 | 3354 | 925 |
| large | hot | 2 | 2863.56 | 2757.81 | 2346.97 | 332.90 | 27.11 | 0.0 | 0.0 |

Nested totalMs rows overlap; do not add core.process to its XML/asset children.
Tiny sample counts describe this VM/workload; no universal speedup claim.
