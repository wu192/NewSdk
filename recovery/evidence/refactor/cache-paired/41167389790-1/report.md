# Compiler cache paired measurement

Status: passed

Same Windows VM and input, new process per sample; OS page cache is uncontrolled. Cold means empty compiler caches. Detailed attribution is excluded from off medians.

| Mode | Phase | Baseline n / median ms | Candidate n / median ms | Reduction |
| --- | --- | ---: | ---: | ---: |
| default | cold | 3 / 52162.59 | 3 / 48445.15 | 7.13% |
| default | hot | 6 / 7648.77 | 6 / 5049.97 | 33.98% |
| none | cold | 1 / 48377.33 | 1 / 43720.63 | 9.63% |

Two hot samples share each cold namespace; cold pairs are independent. Small sample counts are descriptive. Raw published files are compared after process timing. This performance run does not replace production/SDK acceptance.

Workload: Complete historical Upheaval plus 6 reachable Armor partitions (1536 additional documents / 49152 assets)
