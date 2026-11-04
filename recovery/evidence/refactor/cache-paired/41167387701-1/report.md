# Compiler cache paired measurement

Status: passed

Same Windows VM and input, new process per sample; OS page cache is uncontrolled. Cold means empty compiler caches. Detailed attribution is excluded from off medians.

| Mode | Phase | Baseline n / median ms | Candidate n / median ms | Reduction |
| --- | --- | ---: | ---: | ---: |
| default | cold | 3 / 35943.50 | 3 / 34804.40 | 3.17% |
| default | hot | 6 / 2216.58 | 6 / 1737.07 | 21.63% |
| none | cold | 1 / 34236.46 | 1 / 33392.79 | 2.46% |

Two hot samples share each cold namespace; cold pairs are independent. Small sample counts are descriptive. Raw published files are compared after process timing. This performance run does not replace production/SDK acceptance.

Workload: Complete untouched historical Upheaval, all auto-discovered streams/BIG/SKUDEF/Additional
