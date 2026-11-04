# Warm-cache edit paired diagnostics

Status: passed

Synthetic 512 Armor + inheritance/Define workload, one process per implementation/phase. All samples use detailed instrumentation; these are diagnostic paired observations, not off-mode throughput medians. New process each time; OS page cache uncontrolled. Setup and raw byte comparison are outside process wall.

| Scenario | Phase | Baseline ms | Candidate ms | Baseline parse/compile | Candidate parse/compile |
| --- | --- | ---: | ---: | --- | --- |
| single-in-large-document | hot_before | 444.80 | 464.57 | 0/0 | 0/0 |
| single-in-large-document | modified | 526.28 | 506.67 | 1/1 | 1/1 |
| single-in-large-document | hot_after | 444.54 | 450.22 | 0/0 | 0/0 |
| inheritance-source | hot_before | 459.16 | 431.00 | 0/0 | 0/0 |
| inheritance-source | modified | 525.92 | 514.39 | 2/2 | 2/2 |
| inheritance-source | hot_after | 455.10 | 433.77 | 0/0 | 0/0 |
| unrelated-parent-sibling | hot_before | 438.03 | 443.30 | 0/0 | 0/0 |
| unrelated-parent-sibling | modified | 516.67 | 513.99 | 2/1 | 2/1 |
| unrelated-parent-sibling | hot_after | 437.96 | 440.27 | 0/0 | 0/0 |
| define-value | hot_before | 449.73 | 438.31 | 0/0 | 0/0 |
| define-value | modified | 534.82 | 523.16 | 3/2 | 3/2 |
| define-value | hot_after | 442.74 | 445.23 | 0/0 | 0/0 |
| session-metadata-missing | hot_before | 452.58 | 442.67 | 0/0 | 0/0 |
| session-metadata-missing | modified | 552.50 | 519.75 | 37/0 | 37/0 |
| session-metadata-missing | hot_after | 444.91 | 428.16 | 0/0 | 0/0 |

Every generation compares actual published bytes across implementations; hot phases also compare their current generation. Unrelated sibling edits and lost session metadata must preserve publication bytes. Counters retain the difference between frontend work and actual backend calls. This bounded diagnostic does not replace the complete SDK/production regression.
