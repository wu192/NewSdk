# Full Upheaval backend parallel A/B

Status: passed

| Sample | Wall ms | CPU ms | Compile calls | Max backend overlap |
| --- | ---: | ---: | ---: | ---: |
| nocache-cold-1 | 51266.8 | 51203.1 | 3354 | off |
| nocache-cold-4 | 33814.7 | 58437.5 | 3354 | off |
| nocache-cold-auto | 34027.5 | 57968.8 | 3354 | off |
| defaults-cold-4 | 35691.6 | 60531.2 | 3354 | off |
| defaults-hot-4 | 2197.5 | 2093.8 | 0 | off |
| defaults-hot-repeat-4 | 2157.7 | 2125.0 | 0 | off |
| defaults-hot-diagnostic-4 | 2167.1 | 2046.9 | 0 | 0 |
| defaults-cold-1 | 54012.3 | 53578.1 | 3354 | off |
| defaults-hot-1 | 2217.9 | 2078.1 | 0 | off |
| defaults-hot-repeat-1 | 2205.9 | 2125.0 | 0 | off |
| defaults-hot-diagnostic-1 | 2202.3 | 2140.6 | 0 | 0 |
| defaults-cold-auto | 35800.4 | 60140.6 | 3354 | off |
| defaults-hot-auto | 2277.7 | 2203.1 | 0 | off |
| defaults-hot-repeat-auto | 2225.8 | 2156.2 | 0 | off |
| defaults-hot-diagnostic-auto | 2222.2 | 2156.2 | 0 | 0 |
| diagnostic-cold-1 | 51985.6 | 51843.8 | 3354 | 1 |
| diagnostic-cold-4 | 34202.8 | 58343.8 | 3354 | 4 |
| diagnostic-cold-auto | 34179.3 | 58593.8 | 3354 | 4 |
| modified-independent-control | 20710.6 | 20500.0 | 1 | off |
| defaults-modified-1 | 2597.4 | 2437.5 | 1 | off |
| defaults-modified-4 | 2545.2 | 2421.9 | 1 | off |
| defaults-modified-auto | 2577.7 | 2484.4 | 1 | off |

Off = throughput comparison. Detailed = diagnostic attribution; durations overlap and cannot be added.

