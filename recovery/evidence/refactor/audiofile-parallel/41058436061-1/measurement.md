# Full Upheaval backend parallel A/B

Status: passed

| Sample | Wall ms | CPU ms | Compile calls | Max backend overlap |
| --- | ---: | ---: | ---: | ---: |
| nocache-cold-1 | 40846.3 | 40562.5 | 3354 | off |
| nocache-cold-4 | 26716.1 | 46671.9 | 3354 | off |
| nocache-cold-auto | 26495.9 | 46500.0 | 3354 | off |
| defaults-cold-4 | 28035.7 | 48593.8 | 3354 | off |
| defaults-hot-4 | 1795.6 | 1718.8 | 0 | off |
| defaults-hot-repeat-4 | 1860.3 | 1765.6 | 0 | off |
| defaults-hot-diagnostic-4 | 1819.1 | 1750.0 | 0 | 0 |
| defaults-cold-1 | 43846.5 | 43015.6 | 3354 | off |
| defaults-hot-1 | 1734.6 | 1671.9 | 0 | off |
| defaults-hot-repeat-1 | 1760.5 | 1718.8 | 0 | off |
| defaults-hot-diagnostic-1 | 1773.2 | 1703.1 | 0 | 0 |
| defaults-cold-auto | 27876.9 | 47859.4 | 3354 | off |
| defaults-hot-auto | 1885.8 | 1718.8 | 0 | off |
| defaults-hot-repeat-auto | 1752.3 | 1703.1 | 0 | off |
| defaults-hot-diagnostic-auto | 1785.6 | 1687.5 | 0 | 0 |
| diagnostic-cold-1 | 41434.9 | 41218.8 | 3354 | 1 |
| diagnostic-cold-4 | 26682.5 | 46578.1 | 3354 | 4 |
| diagnostic-cold-auto | 26667.8 | 46859.4 | 3354 | 4 |
| modified-independent-control | 15625.1 | 15515.6 | 1 | off |
| defaults-modified-1 | 2074.4 | 1937.5 | 1 | off |
| defaults-modified-4 | 2127.3 | 1968.8 | 1 | off |
| defaults-modified-auto | 2028.1 | 2000.0 | 1 | off |

Off = throughput comparison. Detailed = diagnostic attribution; durations overlap and cannot be added.

