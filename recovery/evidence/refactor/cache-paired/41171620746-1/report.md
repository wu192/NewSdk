# Compiler cache paired measurement

Status: passed

Same Windows VM and input, new process per sample; OS page cache is uncontrolled. Cold means empty compiler caches. Detailed attribution is excluded from off medians.

| Mode | Phase | Baseline n / median ms | Candidate n / median ms | Reduction |
| --- | --- | ---: | ---: | ---: |
| default | cold | 3 / 36723.48 | 3 / 33500.74 | 8.78% |
| default | hot | 6 / 2273.15 | 6 / 1690.65 | 25.63% |
| none | cold | 1 / 34411.07 | 1 / 32181.51 | 6.48% |

Two hot samples share each cold namespace; cold pairs are independent. Small sample counts are descriptive. Raw published files are compared after process timing. This performance run does not replace production/SDK acceptance.

Workload: Complete untouched historical Upheaval, all auto-discovered streams/BIG/SKUDEF/Additional
