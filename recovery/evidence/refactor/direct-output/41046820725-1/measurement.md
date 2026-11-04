# Direct output paired measurement

Status: passed

Synthetic 256 Armor, 8 Texture, 2 PCM Audio, 2 animation and 1 hierarchy base stream; Additional absent/16 MiB. Not a full MOD benchmark.

Windows GetProcessIoCounters logical process transfers; includes runtime loading and report/log I/O, excludes controller comparisons; OS page cache uncontrolled.

| Scenario | Phase | Engine | n | Wall ms | Package ms | Publish ms | Logical read MiB | Logical write MiB |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| compiled-only | disabled | before | 3 | 1075.73 | 25.19 | 19.88 | 2.98 | 1.17 |
| compiled-only | disabled | after | 3 | 973.54 | 23.12 | 0.00 | 2.27 | 0.91 |
| compiled-only | cold | before | 3 | 1037.40 | 23.42 | 18.11 | 3.14 | 1.35 |
| compiled-only | cold | after | 3 | 1039.67 | 23.67 | 0.00 | 2.42 | 1.09 |
| compiled-only | hot | before | 3 | 722.14 | 24.23 | 14.05 | 2.35 | 0.52 |
| compiled-only | hot | after | 3 | 668.63 | 24.74 | 0.00 | 2.07 | 0.32 |
| additional-16MiB | disabled | before | 3 | 1073.48 | 70.39 | 26.59 | 66.98 | 49.17 |
| additional-16MiB | disabled | after | 3 | 1018.88 | 37.15 | 0.00 | 18.27 | 16.91 |
| additional-16MiB | cold | before | 3 | 1098.70 | 67.44 | 25.80 | 67.14 | 49.35 |
| additional-16MiB | cold | after | 3 | 1028.40 | 36.95 | 0.00 | 18.42 | 17.09 |
| additional-16MiB | hot | before | 3 | 744.25 | 70.54 | 26.09 | 50.35 | 32.52 |
| additional-16MiB | hot | after | 3 | 694.20 | 39.58 | 0.00 | 18.07 | 16.32 |
