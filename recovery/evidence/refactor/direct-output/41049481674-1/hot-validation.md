# Bounded hot-cache matrix

Status: passed

Fresh process per sample; OS page cache uncontrolled. Small samples describe this VM only.
Wall time includes startup/build/publication; fixture setup and byte comparisons are outside sample timing.

| Scenario | Phase | n | Median ms | Min–max ms |
| --- | --- | ---: | ---: | ---: |
| small-control | uncached | 1 | 615.8 | 615.8–615.8 |
| medium-control | uncached | 1 | 906.4 | 906.4–906.4 |
| large-control | uncached | 1 | 1595.0 | 1595.0–1595.0 |
| medium-big-control | uncached | 1 | 877.0 | 877.0–877.0 |
| small-default | cold | 1 | 595.8 | 595.8–595.8 |
| medium-default | cold | 1 | 887.6 | 887.6–887.6 |
| large-default | cold | 1 | 1659.2 | 1659.2–1659.2 |
| medium-asset | cold | 1 | 912.1 | 912.1–912.1 |
| medium-session-xml | cold | 1 | 884.2 | 884.2–884.2 |
| medium-session-deflate | cold | 1 | 897.3 | 897.3–897.3 |
| medium-both-xml | cold | 1 | 909.2 | 909.2–909.2 |
| medium-jobs1 | cold | 1 | 928.5 | 928.5–928.5 |
| medium-jobsauto | cold | 1 | 912.4 | 912.4–912.4 |
| medium-big | cold | 1 | 925.7 | 925.7–925.7 |
| medium-big | hot1 | 1 | 630.2 | 630.2–630.2 |
| medium-jobsauto | hot1 | 1 | 601.1 | 601.1–601.1 |
| medium-jobs1 | hot1 | 1 | 607.1 | 607.1–607.1 |
| medium-both-xml | hot1 | 1 | 588.7 | 588.7–588.7 |
| medium-session-deflate | hot1 | 1 | 601.0 | 601.0–601.0 |
| medium-session-xml | hot1 | 1 | 586.3 | 586.3–586.3 |
| medium-asset | hot1 | 1 | 798.7 | 798.7–798.7 |
| large-default | hot1 | 1 | 625.5 | 625.5–625.5 |
| medium-default | hot1 | 1 | 584.3 | 584.3–584.3 |
| small-default | hot1 | 1 | 525.8 | 525.8–525.8 |
| small-default | hot2 | 1 | 518.1 | 518.1–518.1 |
| medium-default | hot2 | 1 | 593.5 | 593.5–593.5 |
| large-default | hot2 | 1 | 657.4 | 657.4–657.4 |
| medium-asset | hot2 | 1 | 809.6 | 809.6–809.6 |
| medium-session-xml | hot2 | 1 | 588.5 | 588.5–588.5 |
| medium-session-deflate | hot2 | 1 | 593.8 | 593.8–593.8 |
| medium-both-xml | hot2 | 1 | 584.6 | 584.6–584.6 |
| medium-jobs1 | hot2 | 1 | 596.0 | 596.0–596.0 |
| medium-jobsauto | hot2 | 1 | 589.2 | 589.2–589.2 |
| medium-big | hot2 | 1 | 607.6 | 607.6–607.6 |
| control-xml | uncached | 1 | 868.0 | 868.0–868.0 |
| medium-default | modified | 6 | 681.8 | 669.1–701.0 |
| medium-default | hot-after-xml | 1 | 581.4 | 581.4–581.4 |
| control-texture | uncached | 1 | 867.6 | 867.6–867.6 |
| medium-default | hot-after-texture | 1 | 586.8 | 586.8–586.8 |
| control-audio | uncached | 1 | 859.4 | 859.4–859.4 |
| medium-default | hot-after-audio | 1 | 587.8 | 587.8–587.8 |
| control-animation | uncached | 1 | 873.0 | 873.0–873.0 |
| medium-default | hot-after-animation | 1 | 586.9 | 586.9–586.9 |
| control-inheritance | uncached | 1 | 868.4 | 868.4–868.4 |
| medium-default | hot-after-inheritance | 1 | 588.6 | 588.6–588.6 |
| control-defines | uncached | 1 | 885.1 | 885.1–885.1 |
| medium-default | hot-after-defines | 1 | 602.0 | 602.0–602.0 |
| medium-default | repair | 1 | 602.4 | 602.4–602.4 |
| medium-default | hot-after-repair | 1 | 592.9 | 592.9–592.9 |
| medium-default | repair-session | 1 | 736.1 | 736.1–736.1 |
| medium-default | hot-after-session-repair | 1 | 604.3 | 604.3–604.3 |

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
