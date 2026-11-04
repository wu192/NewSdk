# Original/new SDK paired benchmark

Status: passed.

Same physical inputs, Schema and ordered search roots; separate compiler caches.
Fresh processes, warmup excluded, uncontrolled OS page cache. New CLI includes reporting and atomic publication; original BAB writes streams directly.

| Workload | Mode | Phase | n/engine | Original ms | New ms | New/original |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| mymod | none | uncached | 3 | 670.2 | 667.5 | 1.00x |
| mymod | asset | cold | 1 | 687.0 | 655.6 | 0.95x |
| mymod | session-xml | cold | 1 | 680.8 | 738.2 | 1.08x |
| mymod | session-deflate | cold | 1 | 677.0 | 750.0 | 1.11x |
| mymod | both-xml | cold | 1 | 677.2 | 736.4 | 1.09x |
| mymod | both-deflate | cold | 1 | 694.1 | 731.4 | 1.05x |
| upheaval-armor | none | uncached | 3 | 452.0 | 493.4 | 1.09x |
| upheaval-armor | asset | cold | 1 | 444.0 | 507.4 | 1.14x |
| upheaval-armor | session-xml | cold | 1 | 449.1 | 587.7 | 1.31x |
| upheaval-armor | session-deflate | cold | 1 | 461.3 | 573.6 | 1.24x |
| upheaval-armor | both-xml | cold | 1 | 477.9 | 569.1 | 1.19x |
| upheaval-armor | both-deflate | cold | 1 | 445.1 | 605.5 | 1.36x |
| texture-16 | none | uncached | 3 | 693.6 | 774.0 | 1.12x |
| texture-16 | asset | cold | 1 | 703.1 | 766.9 | 1.09x |
| texture-16 | session-xml | cold | 1 | 720.3 | 851.4 | 1.18x |
| texture-16 | session-deflate | cold | 1 | 709.7 | 823.7 | 1.16x |
| texture-16 | both-xml | cold | 1 | 705.7 | 850.6 | 1.21x |
| texture-16 | both-deflate | cold | 1 | 715.8 | 846.5 | 1.18x |
| mymod | both-deflate | hot | 2 | 448.7 | 556.4 | 1.24x |
| mymod | both-xml | hot | 2 | 424.3 | 549.7 | 1.30x |
| mymod | session-deflate | hot | 2 | 428.1 | 547.6 | 1.28x |
| mymod | session-xml | hot | 2 | 424.7 | 556.1 | 1.31x |
| mymod | asset | hot | 2 | 685.1 | 655.5 | 0.96x |
| upheaval-armor | both-deflate | hot | 2 | 426.6 | 536.5 | 1.26x |
| upheaval-armor | both-xml | hot | 2 | 425.6 | 528.3 | 1.24x |
| upheaval-armor | session-deflate | hot | 2 | 409.4 | 536.4 | 1.31x |
| upheaval-armor | session-xml | hot | 2 | 423.2 | 525.1 | 1.24x |
| upheaval-armor | asset | hot | 2 | 457.3 | 497.5 | 1.09x |
| texture-16 | both-deflate | hot | 2 | 424.0 | 559.2 | 1.32x |
| texture-16 | both-xml | hot | 2 | 421.6 | 564.8 | 1.34x |
| texture-16 | session-deflate | hot | 2 | 429.0 | 552.3 | 1.29x |
| texture-16 | session-xml | hot | 2 | 428.0 | 554.2 | 1.30x |
| texture-16 | asset | hot | 2 | 440.1 | 493.9 | 1.12x |

A ratio above 1 means the new CLI took longer. Small samples are descriptive.
All passed pairs match every stream/CDATAs member, length, SHA256 and raw bytes.
