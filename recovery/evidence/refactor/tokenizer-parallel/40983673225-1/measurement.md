# Full Upheaval backend parallel A/B

Status: passed

| Sample | Wall ms | CPU ms | Compile calls | Max backend overlap |
| --- | ---: | ---: | ---: | ---: |
| nocache-cold-1 | 70479.8 | 70546.9 | 3354 | off |
| nocache-cold-4 | 41609.7 | 81796.9 | 3354 | off |
| nocache-cold-auto | 41715.8 | 80750.0 | 3354 | off |
| defaults-cold-4 | 43610.7 | 84156.2 | 3354 | off |
| defaults-hot-4 | 2919.5 | 2828.1 | 0 | off |
| defaults-hot-repeat-4 | 2815.8 | 2781.2 | 0 | off |
| defaults-hot-diagnostic-4 | 2695.9 | 2671.9 | 0 | 0 |
| defaults-cold-1 | 73489.1 | 73078.1 | 3354 | off |
| defaults-hot-1 | 2833.9 | 2796.9 | 0 | off |
| defaults-hot-repeat-1 | 2812.8 | 2765.6 | 0 | off |
| defaults-hot-diagnostic-1 | 2714.5 | 2640.6 | 0 | 0 |
| defaults-cold-auto | 43632.3 | 83687.5 | 3354 | off |
| defaults-hot-auto | 2862.1 | 2765.6 | 0 | off |
| defaults-hot-repeat-auto | 2784.6 | 2750.0 | 0 | off |
| defaults-hot-diagnostic-auto | 2695.3 | 2593.8 | 0 | 0 |
| diagnostic-cold-1 | 71522.2 | 71437.5 | 3354 | 1 |
| diagnostic-cold-4 | 41947.0 | 82234.4 | 3354 | 4 |
| diagnostic-cold-auto | 42153.2 | 82375.0 | 3354 | 4 |
| modified-independent-control | 40028.9 | 40125.0 | 1292 | off |
| defaults-modified-1 | 3091.7 | 3000.0 | 1 | off |
| defaults-modified-4 | 3098.4 | 3031.2 | 1 | off |
| defaults-modified-auto | 3104.4 | 3046.9 | 1 | off |

Off = throughput comparison. Detailed = diagnostic attribution; durations overlap and cannot be added.

