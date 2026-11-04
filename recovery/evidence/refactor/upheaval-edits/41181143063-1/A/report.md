# Complete Upheaval large-edit paired comparison

Status: passed

Group A: each scenario uses the same Windows VM, full MOD input and jobs=4 for both implementations. Three off edit pairs; detailed is separate attribution. New process per sample; OS page cache uncontrolled. Groups A/B may use different machines and their absolute times must not be compared.

Warm preparation, same-path output/cache snapshot restoration and byte comparisons are outside edit timing. After each restore, an unmeasured verification must parse/compile zero; measured hot-before and hot-after must also be zero. The compiler still normally packages BIG/SKUDEF on every build.

| Scenario | Level | Phase | Baseline n / median ms | Candidate n / median ms | Reduction | Parse b/c | Compile b/c |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| gameobject-balance | off | hot_before | 3 / 1671.068 | 3 / 1695.619 | -1.47% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| gameobject-balance | off | modified | 3 / 2512.333 | 3 / 2507.480 | 0.19% | [38, 38, 38] / [38, 38, 38] | [37, 37, 37] / [37, 37, 37] |
| gameobject-balance | off | hot_after | 3 / 1654.467 | 3 / 1665.485 | -0.67% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| gameobject-balance | detailed | hot_before | 1 / 1709.254 | 1 / 1697.691 | 0.68% | [0] / [0] | [0] / [0] |
| gameobject-balance | detailed | modified | 1 / 2558.086 | 1 / 2517.314 | 1.59% | [38] / [38] | [37] / [37] |
| gameobject-balance | detailed | hot_after | 1 / 1653.786 | 1 / 1718.347 | -3.90% | [0] / [0] | [0] / [0] |
| weapon-modules | off | hot_before | 3 / 1684.395 | 3 / 1691.162 | -0.40% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| weapon-modules | off | modified | 3 / 2047.802 | 3 / 2063.040 | -0.74% | [20, 20, 20] / [20, 20, 20] | [101, 101, 101] / [101, 101, 101] |
| weapon-modules | off | hot_after | 3 / 1656.011 | 3 / 1662.641 | -0.40% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| weapon-modules | detailed | hot_before | 1 / 1703.264 | 1 / 1673.304 | 1.76% | [0] / [0] | [0] / [0] |
| weapon-modules | detailed | modified | 1 / 2084.001 | 1 / 2091.920 | -0.38% | [20] / [20] | [101] / [101] |
| weapon-modules | detailed | hot_after | 1 / 1661.648 | 1 / 1675.985 | -0.86% | [0] / [0] | [0] / [0] |
| inherited-parent | off | hot_before | 3 / 1743.836 | 3 / 1791.875 | -2.75% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| inherited-parent | off | modified | 3 / 2282.276 | 3 / 2184.384 | 4.29% | [16, 16, 16] / [16, 16, 16] | [304, 304, 304] / [304, 304, 304] |
| inherited-parent | off | hot_after | 3 / 1818.158 | 3 / 1817.083 | 0.06% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| inherited-parent | detailed | hot_before | 1 / 1698.593 | 1 / 1715.103 | -0.97% | [0] / [0] | [0] / [0] |
| inherited-parent | detailed | modified | 1 / 2161.091 | 1 / 2138.983 | 1.02% | [16] / [16] | [304] / [304] |
| inherited-parent | detailed | hot_after | 1 / 1664.118 | 1 / 1670.762 | -0.40% | [0] / [0] | [0] / [0] |
| parent-unrelated | off | hot_before | 3 / 1665.532 | 3 / 1750.696 | -5.11% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| parent-unrelated | off | modified | 3 / 1887.288 | 3 / 1840.589 | 2.47% | [16, 16, 16] / [1, 1, 1] | [1, 1, 1] / [1, 1, 1] |
| parent-unrelated | off | hot_after | 3 / 1663.448 | 3 / 1647.960 | 0.93% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| parent-unrelated | detailed | hot_before | 1 / 1723.022 | 1 / 1705.483 | 1.02% | [0] / [0] | [0] / [0] |
| parent-unrelated | detailed | modified | 1 / 1944.430 | 1 / 1866.657 | 4.00% | [16] / [1] | [1] / [1] |
| parent-unrelated | detailed | hot_after | 1 / 1719.534 | 1 / 1699.888 | 1.14% | [0] / [0] | [0] / [0] |

All publication comparisons read actual bytes, including BIG/SKUDEF/Additional. Each trial restores the scenario setup, so edits do not accumulate. Failed or unmeasured scenarios remain explicit. Full production/SDK acceptance is reused only through the recorded immutable binary/input identity.

- gameobject-balance: passed

- weapon-modules: passed

- inherited-parent: passed

- parent-unrelated: passed
