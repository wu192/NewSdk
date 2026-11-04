# Full Upheaval backend parallel A/B

Status: passed

| Sample | Wall ms | CPU ms | Compile calls | Max backend overlap |
| --- | ---: | ---: | ---: | ---: |
| nocache-cold-1 | 36789.4 | 36609.4 | 3354 | off |
| nocache-cold-4 | 27366.1 | 43984.4 | 3354 | off |
| nocache-cold-auto | 27504.8 | 43968.8 | 3354 | off |
| defaults-cold-4 | 29150.9 | 45687.5 | 3354 | off |
| defaults-hot-4 | 2023.5 | 1890.6 | 0 | off |
| defaults-hot-repeat-4 | 2012.9 | 1859.4 | 0 | off |
| defaults-hot-diagnostic-4 | 2013.9 | 1875.0 | 0 | 0 |
| defaults-cold-1 | 39224.5 | 38328.1 | 3354 | off |
| defaults-hot-1 | 2023.6 | 1906.2 | 0 | off |
| defaults-hot-repeat-1 | 2022.6 | 1906.2 | 0 | off |
| defaults-hot-diagnostic-1 | 2013.6 | 1921.9 | 0 | 0 |
| defaults-cold-auto | 29184.9 | 45687.5 | 3354 | off |
| defaults-hot-auto | 2010.1 | 1890.6 | 0 | off |
| defaults-hot-repeat-auto | 1996.7 | 1906.2 | 0 | off |
| defaults-hot-diagnostic-auto | 2006.7 | 1953.1 | 0 | 0 |
| diagnostic-cold-1 | 36805.6 | 36453.1 | 3354 | 1 |
| diagnostic-cold-4 | 27264.0 | 44171.9 | 3354 | 4 |
| diagnostic-cold-auto | 27522.2 | 44218.8 | 3354 | 4 |
| modified-independent-control | 17410.9 | 17203.1 | 1 | off |
| defaults-modified-1 | 2376.1 | 2156.2 | 1 | off |
| defaults-modified-4 | 2304.5 | 2281.2 | 1 | off |
| defaults-modified-auto | 2494.3 | 2093.8 | 1 | off |

Off = throughput comparison. Detailed = diagnostic attribution; durations overlap and cannot be added.

