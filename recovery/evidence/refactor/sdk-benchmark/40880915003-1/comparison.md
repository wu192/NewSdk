# Original/new SDK paired benchmark

Status: passed.

Same physical inputs, Schema and ordered search roots; separate compiler caches.
Fresh processes, warmup excluded, uncontrolled OS page cache. New CLI includes reporting and atomic publication; original BAB writes streams directly.

| Workload | Mode | Phase | n/engine | Original ms | New ms | New/original |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| mymod | none | uncached | 3 | 486.1 | 470.7 | 0.97x |
| mymod | asset | cold | 1 | 496.3 | 456.4 | 0.92x |
| mymod | session-xml | cold | 1 | 479.1 | 496.1 | 1.04x |
| mymod | session-deflate | cold | 1 | 530.3 | 474.1 | 0.89x |
| mymod | both-xml | cold | 1 | 535.4 | 465.5 | 0.87x |
| mymod | both-deflate | cold | 1 | 502.8 | 485.1 | 0.96x |
| upheaval-armor | none | uncached | 3 | 336.6 | 319.9 | 0.95x |
| upheaval-armor | asset | cold | 1 | 337.5 | 335.3 | 0.99x |
| upheaval-armor | session-xml | cold | 1 | 336.1 | 333.3 | 0.99x |
| upheaval-armor | session-deflate | cold | 1 | 321.9 | 343.2 | 1.07x |
| upheaval-armor | both-xml | cold | 1 | 339.2 | 334.9 | 0.99x |
| upheaval-armor | both-deflate | cold | 1 | 323.4 | 343.6 | 1.06x |
| texture-16 | none | uncached | 3 | 505.2 | 523.9 | 1.04x |
| texture-16 | asset | cold | 1 | 509.2 | 545.5 | 1.07x |
| texture-16 | session-xml | cold | 1 | 518.4 | 548.0 | 1.06x |
| texture-16 | session-deflate | cold | 1 | 522.7 | 542.9 | 1.04x |
| texture-16 | both-xml | cold | 1 | 521.5 | 559.6 | 1.07x |
| texture-16 | both-deflate | cold | 1 | 519.0 | 546.7 | 1.05x |
| mymod | both-deflate | hot | 2 | 317.0 | 328.2 | 1.04x |
| mymod | both-xml | hot | 2 | 311.5 | 312.7 | 1.00x |
| mymod | session-deflate | hot | 2 | 323.6 | 299.5 | 0.93x |
| mymod | session-xml | hot | 2 | 311.5 | 357.5 | 1.15x |
| mymod | asset | hot | 2 | 480.8 | 446.6 | 0.93x |
| upheaval-armor | both-deflate | hot | 2 | 317.7 | 295.7 | 0.93x |
| upheaval-armor | both-xml | hot | 2 | 302.5 | 291.4 | 0.96x |
| upheaval-armor | session-deflate | hot | 2 | 304.3 | 298.3 | 0.98x |
| upheaval-armor | session-xml | hot | 2 | 321.8 | 292.6 | 0.91x |
| upheaval-armor | asset | hot | 2 | 335.8 | 318.1 | 0.95x |
| texture-16 | both-deflate | hot | 2 | 328.3 | 335.5 | 1.02x |
| texture-16 | both-xml | hot | 2 | 314.5 | 321.2 | 1.02x |
| texture-16 | session-deflate | hot | 2 | 320.5 | 329.5 | 1.03x |
| texture-16 | session-xml | hot | 2 | 314.2 | 334.8 | 1.07x |
| texture-16 | asset | hot | 2 | 309.3 | 321.7 | 1.04x |

A ratio above 1 means the new CLI took longer. Small samples are descriptive.
All passed pairs match every stream/CDATAs member, length, SHA256 and raw bytes.
