# Compiler cache paired measurement

Status: passed

Same Windows VM and input, new process per sample; OS page cache is uncontrolled. Cold means empty compiler caches. Detailed attribution is excluded from off medians.

| Mode | Phase | Baseline n / median ms | Candidate n / median ms | Reduction |
| --- | --- | ---: | ---: | ---: |
| default | cold | 3 / 26869.63 | 3 / 23931.47 | 10.93% |
| default | hot | 6 / 1368.18 | 6 / 1380.74 | -0.92% |
| none | cold | 1 / 24764.82 | 1 / 22850.94 | 7.73% |

Two hot samples share each cold namespace; cold pairs are independent. Small sample counts are descriptive. Raw published files are compared after process timing. This performance run does not replace production/SDK acceptance.

Workload: Complete untouched historical Upheaval, all auto-discovered streams/BIG/SKUDEF/Additional
