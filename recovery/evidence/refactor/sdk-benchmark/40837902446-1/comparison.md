# Original/new SDK paired benchmark

Status: passed.

Same physical inputs, Schema and ordered search roots; separate compiler caches.
Fresh processes, warmup excluded, uncontrolled OS page cache. New CLI includes reporting and atomic publication; original BAB writes streams directly.

| Workload | Mode | Phase | n/engine | Original ms | New ms | New/original |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| mymod | none | uncached | 3 | 876.7 | 848.5 | 0.97x |
| mymod | asset | cold | 1 | 892.6 | 889.2 | 1.00x |
| mymod | session-xml | cold | 1 | 880.7 | 905.3 | 1.03x |
| mymod | session-deflate | cold | 1 | 886.5 | 880.0 | 0.99x |
| mymod | both-xml | cold | 1 | 879.2 | 885.0 | 1.01x |
| mymod | both-deflate | cold | 1 | 899.2 | 884.8 | 0.98x |
| upheaval-armor | none | uncached | 3 | 594.7 | 630.7 | 1.06x |
| upheaval-armor | asset | cold | 1 | 596.8 | 663.3 | 1.11x |
| upheaval-armor | session-xml | cold | 1 | 591.7 | 642.9 | 1.09x |
| upheaval-armor | session-deflate | cold | 1 | 581.9 | 658.3 | 1.13x |
| upheaval-armor | both-xml | cold | 1 | 600.9 | 668.0 | 1.11x |
| upheaval-armor | both-deflate | cold | 1 | 582.9 | 654.6 | 1.12x |
| texture-16 | none | uncached | 3 | 882.2 | 919.1 | 1.04x |
| texture-16 | asset | cold | 1 | 871.9 | 934.4 | 1.07x |
| texture-16 | session-xml | cold | 1 | 873.7 | 958.7 | 1.10x |
| texture-16 | session-deflate | cold | 1 | 892.7 | 961.1 | 1.08x |
| texture-16 | both-xml | cold | 1 | 873.4 | 984.4 | 1.13x |
| texture-16 | both-deflate | cold | 1 | 901.5 | 950.0 | 1.05x |
| mymod | both-deflate | hot | 2 | 581.2 | 620.1 | 1.07x |
| mymod | both-xml | hot | 2 | 560.1 | 622.3 | 1.11x |
| mymod | session-deflate | hot | 2 | 551.1 | 619.5 | 1.12x |
| mymod | session-xml | hot | 2 | 561.8 | 624.4 | 1.11x |
| mymod | asset | hot | 2 | 877.9 | 838.1 | 0.95x |
| upheaval-armor | both-deflate | hot | 2 | 539.5 | 596.1 | 1.10x |
| upheaval-armor | both-xml | hot | 2 | 589.4 | 589.4 | 1.00x |
| upheaval-armor | session-deflate | hot | 2 | 567.0 | 609.6 | 1.08x |
| upheaval-armor | session-xml | hot | 2 | 575.8 | 609.3 | 1.06x |
| upheaval-armor | asset | hot | 2 | 576.5 | 628.4 | 1.09x |
| texture-16 | both-deflate | hot | 2 | 547.5 | 593.9 | 1.08x |
| texture-16 | both-xml | hot | 2 | 539.4 | 596.3 | 1.11x |
| texture-16 | session-deflate | hot | 2 | 547.5 | 589.3 | 1.08x |
| texture-16 | session-xml | hot | 2 | 547.9 | 592.3 | 1.08x |
| texture-16 | asset | hot | 2 | 555.3 | 590.9 | 1.06x |

A ratio above 1 means the new CLI took longer. Small samples are descriptive.
All passed pairs match every stream/CDATAs member, length, SHA256 and raw bytes.
