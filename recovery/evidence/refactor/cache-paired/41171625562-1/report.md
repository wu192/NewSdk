# Warm-cache edit paired diagnostics

Status: passed

Synthetic 512 Armor + inheritance/Define workload, one process per implementation/phase. All samples use detailed instrumentation; these are diagnostic paired observations, not off-mode throughput medians. New process each time; OS page cache uncontrolled. Setup and raw byte comparison are outside process wall.

| Scenario | Phase | Baseline ms | Candidate ms | Baseline parse/compile | Candidate parse/compile |
| --- | --- | ---: | ---: | --- | --- |
| single-in-large-document | hot_before | 535.76 | 505.65 | 0/0 | 0/0 |
| single-in-large-document | modified | 635.87 | 602.43 | 1/1 | 1/1 |
| single-in-large-document | hot_after | 503.12 | 527.55 | 0/0 | 0/0 |
| inheritance-source | hot_before | 529.50 | 516.42 | 0/0 | 0/0 |
| inheritance-source | modified | 612.67 | 604.24 | 2/2 | 2/2 |
| inheritance-source | hot_after | 509.16 | 539.79 | 0/0 | 0/0 |
| unrelated-parent-sibling | hot_before | 502.62 | 509.58 | 0/0 | 0/0 |
| unrelated-parent-sibling | modified | 596.10 | 580.85 | 2/1 | 2/1 |
| unrelated-parent-sibling | hot_after | 517.63 | 509.55 | 0/0 | 0/0 |
| define-value | hot_before | 531.91 | 504.80 | 0/0 | 0/0 |
| define-value | modified | 613.97 | 601.06 | 3/2 | 3/2 |
| define-value | hot_after | 531.58 | 508.61 | 0/0 | 0/0 |
| unused-define-value | hot_before | 510.07 | 506.24 | 0/0 | 0/0 |
| unused-define-value | modified | 545.42 | 547.13 | 1/0 | 1/0 |
| unused-define-value | hot_after | 514.43 | 500.49 | 0/0 | 0/0 |
| session-metadata-missing | hot_before | 514.87 | 506.92 | 0/0 | 0/0 |
| session-metadata-missing | modified | 639.76 | 595.40 | 37/0 | 37/0 |
| session-metadata-missing | hot_after | 515.71 | 500.37 | 0/0 | 0/0 |

Every generation compares actual published bytes across implementations; hot phases also compare their current generation. Unrelated sibling edits and lost session metadata must preserve publication bytes. Counters retain the difference between frontend work and actual backend calls. This bounded diagnostic does not replace the complete SDK/production regression.
