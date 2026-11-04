# Bounded hot-cache matrix

Status: passed

Fresh process per sample; OS page cache uncontrolled. Small samples describe this VM only.
Wall time includes startup/build/publication; fixture setup and byte comparisons are outside sample timing.

| Scenario | Phase | n | Median ms | Min–max ms |
| --- | --- | ---: | ---: | ---: |
| small-control | uncached | 1 | 631.4 | 631.4–631.4 |
| medium-control | uncached | 1 | 901.3 | 901.3–901.3 |
| large-control | uncached | 1 | 1639.3 | 1639.3–1639.3 |
| medium-big-control | uncached | 1 | 896.3 | 896.3–896.3 |
| small-default | cold | 1 | 618.4 | 618.4–618.4 |
| medium-default | cold | 1 | 898.0 | 898.0–898.0 |
| large-default | cold | 1 | 1629.5 | 1629.5–1629.5 |
| medium-asset | cold | 1 | 879.5 | 879.5–879.5 |
| medium-session-xml | cold | 1 | 898.5 | 898.5–898.5 |
| medium-session-deflate | cold | 1 | 920.8 | 920.8–920.8 |
| medium-both-xml | cold | 1 | 916.0 | 916.0–916.0 |
| medium-jobs1 | cold | 1 | 965.4 | 965.4–965.4 |
| medium-jobsauto | cold | 1 | 948.0 | 948.0–948.0 |
| medium-big | cold | 1 | 938.8 | 938.8–938.8 |
| medium-big | hot1 | 1 | 642.1 | 642.1–642.1 |
| medium-jobsauto | hot1 | 1 | 609.9 | 609.9–609.9 |
| medium-jobs1 | hot1 | 1 | 608.5 | 608.5–608.5 |
| medium-both-xml | hot1 | 1 | 596.7 | 596.7–596.7 |
| medium-session-deflate | hot1 | 1 | 595.3 | 595.3–595.3 |
| medium-session-xml | hot1 | 1 | 596.6 | 596.6–596.6 |
| medium-asset | hot1 | 1 | 824.0 | 824.0–824.0 |
| large-default | hot1 | 1 | 644.9 | 644.9–644.9 |
| medium-default | hot1 | 1 | 598.4 | 598.4–598.4 |
| small-default | hot1 | 1 | 525.0 | 525.0–525.0 |
| small-default | hot2 | 1 | 535.1 | 535.1–535.1 |
| medium-default | hot2 | 1 | 608.3 | 608.3–608.3 |
| large-default | hot2 | 1 | 660.5 | 660.5–660.5 |
| medium-asset | hot2 | 1 | 820.9 | 820.9–820.9 |
| medium-session-xml | hot2 | 1 | 581.3 | 581.3–581.3 |
| medium-session-deflate | hot2 | 1 | 585.5 | 585.5–585.5 |
| medium-both-xml | hot2 | 1 | 582.2 | 582.2–582.2 |
| medium-jobs1 | hot2 | 1 | 599.2 | 599.2–599.2 |
| medium-jobsauto | hot2 | 1 | 599.5 | 599.5–599.5 |
| medium-big | hot2 | 1 | 629.3 | 629.3–629.3 |
| control-xml | uncached | 1 | 899.1 | 899.1–899.1 |
| medium-default | modified | 6 | 707.5 | 691.1–716.4 |
| medium-default | hot-after-xml | 1 | 603.7 | 603.7–603.7 |
| control-texture | uncached | 1 | 897.5 | 897.5–897.5 |
| medium-default | hot-after-texture | 1 | 614.7 | 614.7–614.7 |
| control-audio | uncached | 1 | 891.7 | 891.7–891.7 |
| medium-default | hot-after-audio | 1 | 592.7 | 592.7–592.7 |
| control-animation | uncached | 1 | 877.8 | 877.8–877.8 |
| medium-default | hot-after-animation | 1 | 600.5 | 600.5–600.5 |
| control-inheritance | uncached | 1 | 872.7 | 872.7–872.7 |
| medium-default | hot-after-inheritance | 1 | 604.1 | 604.1–604.1 |
| control-defines | uncached | 1 | 869.8 | 869.8–869.8 |
| medium-default | hot-after-defines | 1 | 610.2 | 610.2–610.2 |
| medium-default | repair | 1 | 597.9 | 597.9–597.9 |
| medium-default | hot-after-repair | 1 | 582.9 | 582.9–582.9 |
| medium-default | repair-session | 1 | 732.1 | 732.1–732.1 |
| medium-default | hot-after-session-repair | 1 | 594.0 | 594.0–594.0 |

## Cache invalidation and dependency propagation

Counts are actual decisions/visits, not a reconstructed dependency graph. Omitted counters are zero.

| Sample | Invalidated documents | Dependency reloads | Affected include parents | Affected include visits | Max include depth |
| --- | ---: | ---: | ---: | ---: | ---: |
| 11-medium-jobs1-cold | 0 | 0 | 0 | 0 | 3 |
| 12-medium-jobsauto-cold | 0 | 0 | 0 | 0 | 3 |
| 13-medium-big-cold | 0 | 0 | 0 | 0 | 3 |
| 14-medium-big-hot1 | 0 | 0 | 0 | 0 | 3 |
| 15-medium-jobsauto-hot1 | 0 | 0 | 0 | 0 | 3 |
| 16-medium-jobs1-hot1 | 0 | 0 | 0 | 0 | 3 |
| 31-medium-jobs1-hot2 | 0 | 0 | 0 | 0 | 3 |
| 32-medium-jobsauto-hot2 | 0 | 0 | 0 | 0 | 3 |
| 33-medium-big-hot2 | 0 | 0 | 0 | 0 | 3 |
| 35-medium-default-modified | 1 | 0 | 1 | 1 | 3 |
| 36-medium-default-hot-after-xml | 0 | 0 | 0 | 0 | 3 |
| 38-medium-default-modified | 1 | 1 | 0 | 0 | 3 |
| 39-medium-default-hot-after-texture | 0 | 0 | 0 | 0 | 3 |
| 41-medium-default-modified | 1 | 1 | 0 | 0 | 3 |
| 42-medium-default-hot-after-audio | 0 | 0 | 0 | 0 | 3 |
| 44-medium-default-modified | 1 | 0 | 1 | 1 | 3 |
| 45-medium-default-hot-after-animation | 0 | 0 | 0 | 0 | 3 |
| 47-medium-default-modified | 2 | 1 | 2 | 2 | 3 |
| 48-medium-default-hot-after-inheritance | 0 | 0 | 0 | 0 | 3 |
| 50-medium-default-modified | 3 | 2 | 3 | 6 | 3 |
| 51-medium-default-hot-after-defines | 0 | 0 | 0 | 0 | 3 |
| 54-medium-default-repair-session | 0 | 0 | 0 | 0 | 3 |
| 55-medium-default-hot-after-session-repair | 0 | 0 | 0 | 0 | 3 |

| Sample | Invalidation reason | Reloads |
| --- | --- | ---: |
| 35-medium-default-modified | cache.invalidation.contentChanged | 1 |
| 38-medium-default-modified | cache.invalidation.dependentFileChanged | 1 |
| 41-medium-default-modified | cache.invalidation.dependentFileChanged | 1 |
| 44-medium-default-modified | cache.invalidation.contentChanged | 1 |
| 47-medium-default-modified | cache.invalidation.contentChanged | 1 |
| 47-medium-default-modified | cache.invalidation.dependentFileChanged | 1 |
| 50-medium-default-modified | cache.invalidation.contentChanged | 1 |
| 50-medium-default-modified | cache.invalidation.definesChanged | 1 |
| 50-medium-default-modified | cache.invalidation.inheritanceChanged | 1 |
