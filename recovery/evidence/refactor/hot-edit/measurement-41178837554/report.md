# Warm-cache edit paired measurement

Status: passed

Same Windows VM, input and jobs=4; new process per sample, OS page cache uncontrolled. Three independent off pairs per core edit; detailed samples are separate attribution and excluded from off medians. Cold setup and raw published byte comparisons are outside edit process timing.

| Mode | Scenario | Phase | Baseline n / median ms | Candidate n / median ms | Reduction | Parse baseline / candidate | Compile baseline / candidate |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| incremental-off | inheritance-source | hot_before | 3 / 424.53 | 3 / 439.16 | -3.45% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| incremental-off | inheritance-source | modified | 3 / 518.89 | 3 / 511.19 | 1.48% | [2, 2, 2] / [2, 2, 2] | [2, 2, 2] / [2, 2, 2] |
| incremental-off | inheritance-source | hot_after | 3 / 428.97 | 3 / 434.79 | -1.36% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| incremental-off | unrelated-parent-sibling | hot_before | 3 / 433.78 | 3 / 447.13 | -3.08% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| incremental-off | unrelated-parent-sibling | modified | 3 / 490.76 | 3 / 493.78 | -0.61% | [2, 2, 2] / [1, 1, 1] | [1, 1, 1] / [1, 1, 1] |
| incremental-off | unrelated-parent-sibling | hot_after | 3 / 427.30 | 3 / 438.37 | -2.59% | [0, 0, 0] / [0, 0, 0] | [0, 0, 0] / [0, 0, 0] |
| incremental-detailed | single-in-large-document | hot_before | 1 / 439.23 | 1 / 441.23 | -0.46% | [0] / [0] | [0] / [0] |
| incremental-detailed | single-in-large-document | modified | 1 / 513.09 | 1 / 516.69 | -0.70% | [1] / [1] | [1] / [1] |
| incremental-detailed | single-in-large-document | hot_after | 1 / 436.97 | 1 / 430.24 | 1.54% | [0] / [0] | [0] / [0] |
| incremental-detailed | inheritance-source | hot_before | 1 / 428.56 | 1 / 433.81 | -1.23% | [0] / [0] | [0] / [0] |
| incremental-detailed | inheritance-source | modified | 1 / 505.01 | 1 / 512.27 | -1.44% | [2] / [2] | [2] / [2] |
| incremental-detailed | inheritance-source | hot_after | 1 / 426.25 | 1 / 425.28 | 0.23% | [0] / [0] | [0] / [0] |
| incremental-detailed | unrelated-parent-sibling | hot_before | 1 / 428.04 | 1 / 446.78 | -4.38% | [0] / [0] | [0] / [0] |
| incremental-detailed | unrelated-parent-sibling | modified | 1 / 509.76 | 1 / 512.85 | -0.61% | [2] / [1] | [1] / [1] |
| incremental-detailed | unrelated-parent-sibling | hot_after | 1 / 460.99 | 1 / 438.78 | 4.82% | [0] / [0] | [0] / [0] |
| incremental-detailed | define-value | hot_before | 1 / 444.87 | 1 / 439.64 | 1.18% | [0] / [0] | [0] / [0] |
| incremental-detailed | define-value | modified | 1 / 523.00 | 1 / 525.49 | -0.48% | [3] / [3] | [2] / [2] |
| incremental-detailed | define-value | hot_after | 1 / 455.98 | 1 / 441.87 | 3.10% | [0] / [0] | [0] / [0] |
| incremental-detailed | unused-define-value | hot_before | 1 / 440.04 | 1 / 436.94 | 0.71% | [0] / [0] | [0] / [0] |
| incremental-detailed | unused-define-value | modified | 1 / 469.29 | 1 / 471.53 | -0.48% | [1] / [1] | [0] / [0] |
| incremental-detailed | unused-define-value | hot_after | 1 / 421.18 | 1 / 436.21 | -3.57% | [0] / [0] | [0] / [0] |
| incremental-detailed | session-metadata-missing | hot_before | 1 / 433.92 | 1 / 436.24 | -0.53% | [0] / [0] | [0] / [0] |
| incremental-detailed | session-metadata-missing | modified | 1 / 527.98 | 1 / 533.66 | -1.08% | [37] / [37] | [0] / [0] |
| incremental-detailed | session-metadata-missing | hot_after | 1 / 435.92 | 1 / 438.26 | -0.54% | [0] / [0] | [0] / [0] |

Every generation compares actual publication bytes across implementations. Hot phases must parse/compile zero. Unrelated parent sibling and unused Define edits preserve publication bytes. The off samples measure only the two selected parent edits; detailed samples retain the other dependency diagnostics. This bounded run does not replace complete Core/CLI/SDK acceptance.
