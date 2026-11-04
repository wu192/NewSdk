# Original/new SDK paired benchmark

Status: passed.

Same physical inputs, Schema and ordered search roots; separate compiler caches.
Fresh processes, warmup excluded, uncontrolled OS page cache. New CLI includes reporting and atomic publication; original BAB writes streams directly.

| Workload | Mode | Phase | n/engine | Original ms | New ms | New/original |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| mymod | none | uncached | 3 | 899.9 | 882.1 | 0.98x |
| mymod | asset | cold | 1 | 875.3 | 839.5 | 0.96x |
| mymod | session-xml | cold | 1 | 880.2 | 945.7 | 1.07x |
| mymod | session-deflate | cold | 1 | 906.4 | 959.6 | 1.06x |
| mymod | both-xml | cold | 1 | 892.2 | 968.7 | 1.09x |
| mymod | both-deflate | cold | 1 | 906.4 | 957.1 | 1.06x |
| upheaval-armor | none | uncached | 3 | 618.3 | 667.2 | 1.08x |
| upheaval-armor | asset | cold | 1 | 611.3 | 673.3 | 1.10x |
| upheaval-armor | session-xml | cold | 1 | 604.2 | 792.0 | 1.31x |
| upheaval-armor | session-deflate | cold | 1 | 596.5 | 759.5 | 1.27x |
| upheaval-armor | both-xml | cold | 1 | 601.8 | 751.3 | 1.25x |
| upheaval-armor | both-deflate | cold | 1 | 595.9 | 763.7 | 1.28x |
| texture-16 | none | uncached | 3 | 897.7 | 977.4 | 1.09x |
| texture-16 | asset | cold | 1 | 889.1 | 968.4 | 1.09x |
| texture-16 | session-xml | cold | 1 | 887.3 | 1094.8 | 1.23x |
| texture-16 | session-deflate | cold | 1 | 910.3 | 1059.4 | 1.16x |
| texture-16 | both-xml | cold | 1 | 903.1 | 1121.9 | 1.24x |
| texture-16 | both-deflate | cold | 1 | 928.3 | 1097.3 | 1.18x |
| mymod | both-deflate | hot | 2 | 593.4 | 741.9 | 1.25x |
| mymod | both-xml | hot | 2 | 581.0 | 739.0 | 1.27x |
| mymod | session-deflate | hot | 2 | 593.2 | 724.3 | 1.22x |
| mymod | session-xml | hot | 2 | 576.7 | 746.6 | 1.29x |
| mymod | asset | hot | 2 | 895.6 | 879.1 | 0.98x |
| upheaval-armor | both-deflate | hot | 2 | 571.6 | 713.6 | 1.25x |
| upheaval-armor | both-xml | hot | 2 | 569.4 | 713.5 | 1.25x |
| upheaval-armor | session-deflate | hot | 2 | 576.2 | 731.6 | 1.27x |
| upheaval-armor | session-xml | hot | 2 | 578.6 | 711.0 | 1.23x |
| upheaval-armor | asset | hot | 2 | 592.4 | 672.6 | 1.14x |
| texture-16 | both-deflate | hot | 2 | 567.9 | 731.9 | 1.29x |
| texture-16 | both-xml | hot | 2 | 554.0 | 737.0 | 1.33x |
| texture-16 | session-deflate | hot | 2 | 563.2 | 734.4 | 1.30x |
| texture-16 | session-xml | hot | 2 | 562.8 | 745.9 | 1.33x |
| texture-16 | asset | hot | 2 | 581.9 | 658.4 | 1.13x |

A ratio above 1 means the new CLI took longer. Small samples are descriptive.
All passed pairs match every stream/CDATAs member, length, SHA256 and raw bytes.
