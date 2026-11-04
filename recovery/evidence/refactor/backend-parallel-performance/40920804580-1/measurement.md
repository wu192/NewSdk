# Full Upheaval backend parallel A/B

Status: passed

| Sample | Wall ms | CPU ms | Compile calls | Max backend overlap |
| --- | ---: | ---: | ---: | ---: |
| nocache-cold-1 | 78095.0 | 73078.1 | 3354 | off |
| nocache-cold-4 | 47084.4 | 88250.0 | 3354 | off |
| nocache-cold-auto | 47192.0 | 87046.9 | 3354 | off |
| defaults-cold-4 | 49164.9 | 91078.1 | 3354 | off |
| defaults-hot-4 | 2976.1 | 2859.4 | 0 | off |
| defaults-hot-repeat-4 | 2860.3 | 2812.5 | 0 | off |
| defaults-hot-diagnostic-4 | 2773.0 | 2703.1 | 0 | 0 |
| defaults-cold-1 | 74095.7 | 73656.2 | 3354 | off |
| defaults-hot-1 | 2919.9 | 2968.8 | 0 | off |
| defaults-hot-repeat-1 | 2875.7 | 2750.0 | 0 | off |
| defaults-hot-diagnostic-1 | 2746.0 | 2656.2 | 0 | 0 |
| defaults-cold-auto | 48580.3 | 89750.0 | 3354 | off |
| defaults-hot-auto | 2933.1 | 2843.8 | 0 | off |
| defaults-hot-repeat-auto | 2883.5 | 2781.2 | 0 | off |
| defaults-hot-diagnostic-auto | 2736.0 | 2718.8 | 0 | 0 |
| diagnostic-cold-1 | 71727.4 | 71593.8 | 3354 | 1 |
| diagnostic-cold-4 | 46713.3 | 87484.4 | 3354 | 4 |
| diagnostic-cold-auto | 46757.4 | 87203.1 | 3354 | 4 |
| modified-independent-control | 40106.1 | 40250.0 | 1292 | off |
| defaults-modified-1 | 3115.8 | 3062.5 | 1 | off |
| defaults-modified-4 | 3153.2 | 3109.4 | 1 | off |
| defaults-modified-auto | 3141.3 | 3031.2 | 1 | off |

Off = throughput comparison. Detailed = diagnostic attribution; durations overlap and cannot be added.

