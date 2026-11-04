# Compiler cache paired measurement

Status: failed

Same Windows VM and input, new process per sample; OS page cache is uncontrolled. Cold means empty compiler caches. Detailed attribution is excluded from off medians.

| Mode | Phase | Baseline n / median ms | Candidate n / median ms | Reduction |
| --- | --- | ---: | ---: | ---: |
| default | cold | 3 / 70805.70 | 3 / 61258.65 | 13.48% |
| default | hot | 6 / 10099.48 | 6 / 6593.85 | 34.71% |
| none | cold | 1 / 63917.70 | 1 / 58605.55 | 8.31% |

Two hot samples share each cold namespace; cold pairs are independent. Small sample counts are descriptive. Raw published files are compared after process timing. This performance run does not replace production/SDK acceptance.

Workload: Complete historical Upheaval plus 6 reachable Armor partitions (1536 additional documents / 49152 assets)

Error: Command '['D:\\a\\NewSdk\\NewSdk\\work\\cache-paired-baseline\\ui-product\\Release\\bin\\NewModCompiler.exe', 'build', '--sdk-root', 'D:\\a\\NewSdk\\NewSdk\\SDK', '--mod', 'D:\\a\\_temp\\cache-paired\\historical\\SDK\\Mods\\Upheaval', '--out', 'D:\\a\\_temp\\cache-paired\\outputs\\detailed-baseline', '--jobs', '4', '--performance', 'detailed']' timed out after 43.12358399999994 seconds
