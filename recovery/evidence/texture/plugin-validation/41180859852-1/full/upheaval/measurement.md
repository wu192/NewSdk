# Full Upheaval backend parallel A/B

Status: passed

| Sample | Wall ms | CPU ms | Compile calls | Max backend overlap |
| --- | ---: | ---: | ---: | ---: |
| nocache-cold-1 | 31523.1 | 31218.8 | 3354 | off |
| nocache-cold-4 | 21580.9 | 36515.6 | 3354 | off |
| nocache-cold-auto | 21202.7 | 36531.2 | 3354 | off |
| defaults-cold-4 | 22523.0 | 37765.6 | 3354 | off |
| defaults-hot-4 | 1565.8 | 1375.0 | 0 | off |
| defaults-hot-repeat-4 | 1559.8 | 1312.5 | 0 | off |
| defaults-hot-diagnostic-4 | 1479.1 | 1390.6 | 0 | 0 |
| defaults-cold-1 | 32866.5 | 32250.0 | 3354 | off |
| defaults-hot-1 | 1482.1 | 1375.0 | 0 | off |
| defaults-hot-repeat-1 | 1501.8 | 1343.8 | 0 | off |
| defaults-hot-diagnostic-1 | 1489.3 | 1328.1 | 0 | 0 |
| defaults-cold-auto | 21866.6 | 37625.0 | 3354 | off |
| defaults-hot-auto | 1441.9 | 1265.6 | 0 | off |
| defaults-hot-repeat-auto | 1528.0 | 1359.4 | 0 | off |
| defaults-hot-diagnostic-auto | 1502.9 | 1390.6 | 0 | 0 |
| diagnostic-cold-1 | 31248.0 | 30953.1 | 3354 | 1 |
| diagnostic-cold-4 | 20237.2 | 34734.4 | 3354 | 4 |
| diagnostic-cold-auto | 19718.2 | 34046.9 | 3354 | 4 |
| modified-independent-control | 11456.5 | 11156.2 | 1 | off |
| defaults-modified-1 | 1529.5 | 1359.4 | 1 | off |
| defaults-modified-4 | 1489.8 | 1328.1 | 1 | off |
| defaults-modified-auto | 1424.8 | 1375.0 | 1 | off |

Off = throughput comparison. Detailed = diagnostic attribution; durations overlap and cannot be added.

