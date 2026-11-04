# Original/new SDK paired benchmark

Status: passed.

Same physical inputs, Schema and ordered search roots; separate compiler caches.
Fresh processes, warmup excluded, uncontrolled OS page cache. New CLI includes reporting and atomic publication; original BAB writes streams directly.

| Workload | Mode | Phase | n/engine | Original ms | New ms | New/original |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| mymod | none | uncached | 6 | 681.4 | 635.0 | 0.93x |
| mymod | asset | cold | 1 | 697.2 | 640.4 | 0.92x |
| mymod | session-xml | cold | 1 | 696.3 | 668.0 | 0.96x |
| mymod | session-deflate | cold | 1 | 721.3 | 664.5 | 0.92x |
| mymod | both-xml | cold | 1 | 693.0 | 660.9 | 0.95x |
| mymod | both-deflate | cold | 1 | 700.0 | 661.8 | 0.95x |
| upheaval-armor | none | uncached | 6 | 464.3 | 460.0 | 0.99x |
| upheaval-armor | asset | cold | 1 | 458.7 | 473.7 | 1.03x |
| upheaval-armor | session-xml | cold | 1 | 476.4 | 470.9 | 0.99x |
| upheaval-armor | session-deflate | cold | 1 | 460.7 | 476.0 | 1.03x |
| upheaval-armor | both-xml | cold | 1 | 463.7 | 464.7 | 1.00x |
| upheaval-armor | both-deflate | cold | 1 | 459.5 | 477.3 | 1.04x |
| texture-16 | none | uncached | 6 | 695.3 | 743.3 | 1.07x |
| texture-16 | asset | cold | 1 | 718.6 | 766.2 | 1.07x |
| texture-16 | session-xml | cold | 1 | 704.8 | 774.6 | 1.10x |
| texture-16 | session-deflate | cold | 1 | 727.3 | 774.4 | 1.06x |
| texture-16 | both-xml | cold | 1 | 720.6 | 798.3 | 1.11x |
| texture-16 | both-deflate | cold | 1 | 728.2 | 838.8 | 1.15x |
| mymod | both-deflate | hot | 5 | 450.6 | 446.1 | 0.99x |
| mymod | both-xml | hot | 5 | 436.2 | 445.0 | 1.02x |
| mymod | session-deflate | hot | 5 | 432.5 | 443.3 | 1.02x |
| mymod | session-xml | hot | 5 | 436.6 | 449.8 | 1.03x |
| mymod | asset | hot | 5 | 688.0 | 626.6 | 0.91x |
| upheaval-armor | both-deflate | hot | 5 | 426.3 | 417.5 | 0.98x |
| upheaval-armor | both-xml | hot | 5 | 423.5 | 412.0 | 0.97x |
| upheaval-armor | session-deflate | hot | 5 | 423.1 | 422.2 | 1.00x |
| upheaval-armor | session-xml | hot | 5 | 422.4 | 415.6 | 0.98x |
| upheaval-armor | asset | hot | 5 | 452.5 | 463.3 | 1.02x |
| texture-16 | both-deflate | hot | 5 | 429.5 | 454.5 | 1.06x |
| texture-16 | both-xml | hot | 5 | 419.7 | 457.1 | 1.09x |
| texture-16 | session-deflate | hot | 5 | 440.2 | 444.7 | 1.01x |
| texture-16 | session-xml | hot | 5 | 423.6 | 451.4 | 1.07x |
| texture-16 | asset | hot | 5 | 438.8 | 465.6 | 1.06x |

A ratio above 1 means the new CLI took longer. Small samples are descriptive.
All passed pairs match every stream/CDATAs member, length, SHA256 and raw bytes.
