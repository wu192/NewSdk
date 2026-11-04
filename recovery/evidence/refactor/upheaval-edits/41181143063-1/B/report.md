# Complete Upheaval large-edit paired comparison

Status: passed

Group B: each scenario uses the same Windows VM, full MOD input and jobs=4 for both implementations. Three off edit pairs; detailed is separate attribution. New process per sample; OS page cache uncontrolled. Groups A/B may use different machines and their absolute times must not be compared.

Warm preparation, same-path output/cache snapshot restoration and byte comparisons are outside edit timing. After each restore, an unmeasured verification must parse/compile zero; measured hot-before and hot-after must also be zero. The compiler still normally packages BIG/SKUDEF on every build.

| Scenario | Level | Phase | Baseline n / median ms | Candidate n / median ms | Reduction | Parse b/c | Compile b/c |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| shared-defines | off | hot_before | 3 / 1456.099 | 3 / 1483.733 | -1.90% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| shared-defines | off | modified | 3 / 1793.449 | 3 / 1832.010 | -2.15% | [16, 16, 16] / [12, 12, 12] | [36, 36, 36] / [36, 36, 36] |
| shared-defines | off | hot_after | 3 / 1388.134 | 3 / 1397.988 | -0.71% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| shared-defines | detailed | hot_before | 1 / 1457.866 | 1 / 1685.416 | -15.61% | [0] / [0] | [0] / [0] |
| shared-defines | detailed | modified | 1 / 1795.093 | 1 / 1807.204 | -0.67% | [16] / [12] | [36] / [36] |
| shared-defines | detailed | hot_after | 1 / 1406.708 | 1 / 1432.027 | -1.80% | [0] / [0] | [0] / [0] |
| asset-lifecycle | off | hot_before | 3 / 1518.330 | 3 / 1523.743 | -0.36% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| asset-lifecycle | off | modified | 3 / 1936.611 | 3 / 1929.743 | 0.35% | [24, 24, 24] / [24, 24, 24] | [42, 42, 42] / [42, 42, 42] |
| asset-lifecycle | off | hot_after | 3 / 1440.246 | 3 / 1399.395 | 2.84% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| asset-lifecycle | detailed | hot_before | 1 / 1462.947 | 1 / 1455.386 | 0.52% | [0] / [0] | [0] / [0] |
| asset-lifecycle | detailed | modified | 1 / 1872.219 | 1 / 1943.410 | -3.80% | [24] / [24] | [42] / [42] |
| asset-lifecycle | detailed | hot_after | 1 / 1444.384 | 1 / 1499.529 | -3.82% | [0] / [0] | [0] / [0] |
| external-media | off | hot_before | 3 / 1443.789 | 3 / 1450.313 | -0.45% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| external-media | off | modified | 3 / 2893.223 | 3 / 2953.482 | -2.08% | [3, 3, 3] / [3, 3, 3] | [80, 80, 80] / [80, 80, 80] |
| external-media | off | hot_after | 3 / 1431.248 | 3 / 1435.286 | -0.28% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| external-media | detailed | hot_before | 1 / 1476.722 | 1 / 1523.586 | -3.17% | [0] / [0] | [0] / [0] |
| external-media | detailed | modified | 1 / 3489.155 | 1 / 2913.128 | 16.51% | [3] / [3] | [80] / [80] |
| external-media | detailed | hot_after | 1 / 1953.647 | 1 / 1423.940 | 27.11% | [0] / [0] | [0] / [0] |
| animation-batch | off | hot_before | 3 / 1451.682 | 3 / 1481.081 | -2.03% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| animation-batch | off | modified | 3 / 2174.938 | 3 / 2216.949 | -1.93% | [32, 32, 32] / [32, 32, 32] | [32, 32, 32] / [32, 32, 32] |
| animation-batch | off | hot_after | 3 / 1369.249 | 3 / 1370.552 | -0.10% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| animation-batch | detailed | hot_before | 1 / 1517.613 | 1 / 1579.928 | -4.11% | [0] / [0] | [0] / [0] |
| animation-batch | detailed | modified | 1 / 2376.994 | 1 / 2259.831 | 4.93% | [32] / [32] | [32] / [32] |
| animation-batch | detailed | hot_after | 1 / 1421.268 | 1 / 1450.609 | -2.06% | [0] / [0] | [0] / [0] |

All publication comparisons read actual bytes, including BIG/SKUDEF/Additional. Each trial restores the scenario setup, so edits do not accumulate. Failed or unmeasured scenarios remain explicit. Full production/SDK acceptance is reused only through the recorded immutable binary/input identity.

- shared-defines: passed

- asset-lifecycle: passed

- external-media: passed

- animation-batch: passed
