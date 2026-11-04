# Compiler cache benchmark

Status: passed. Binary source: `39039961847cd1ca9cb93743fd51741a4e5c9f49`.

Release, fresh process per sample, loose base output; setup and hash checks excluded from wall time.
OS page cache is uncontrolled. Cold refers only to compiler cache. Small sample counts are descriptive.

| Workload | Mode | Phase | n | Wall median ms | Min–max ms | CPU ms | Peak MiB | Cache MiB | vs no-cache |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| mymod | none | uncached | 3 | 694.0 | 691.9–1117.0 | 671.9 | 52.9 | 0.00 | 1.00x |
| mymod | asset (type cache disabled) | cold | 1 | 701.3 | 701.3–701.3 | 703.1 | 52.8 | 0.00 | 0.99x |
| mymod | session-xml | cold | 1 | 766.8 | 766.8–766.8 | 812.5 | 50.8 | 0.27 | 0.90x |
| mymod | session-deflate | cold | 1 | 778.5 | 778.5–778.5 | 750.0 | 50.9 | 0.23 | 0.89x |
| mymod | both-xml | cold | 1 | 748.6 | 748.6–748.6 | 765.6 | 50.9 | 0.27 | 0.93x |
| mymod | both-deflate | cold | 1 | 742.6 | 742.6–742.6 | 750.0 | 50.8 | 0.23 | 0.93x |
| upheaval-armor | none | uncached | 3 | 492.5 | 487.6–494.8 | 500.0 | 46.5 | 0.00 | 1.00x |
| upheaval-armor | asset (type cache disabled) | cold | 1 | 492.3 | 492.3–492.3 | 531.2 | 46.5 | 0.00 | 1.00x |
| upheaval-armor | session-xml | cold | 1 | 566.8 | 566.8–566.8 | 578.1 | 47.5 | 0.02 | 0.87x |
| upheaval-armor | session-deflate | cold | 1 | 565.1 | 565.1–565.1 | 578.1 | 47.5 | 0.01 | 0.87x |
| upheaval-armor | both-xml | cold | 1 | 566.4 | 566.4–566.4 | 625.0 | 47.5 | 0.02 | 0.87x |
| upheaval-armor | both-deflate | cold | 1 | 569.3 | 569.3–569.3 | 562.5 | 47.4 | 0.01 | 0.86x |
| texture-16 | none | uncached | 3 | 767.0 | 762.1–770.6 | 765.6 | 48.1 | 0.00 | 1.00x |
| texture-16 | asset | cold | 1 | 774.7 | 774.7–774.7 | 781.2 | 48.2 | 4.00 | 0.99x |
| texture-16 | session-xml | cold | 1 | 857.7 | 857.7–857.7 | 843.8 | 50.0 | 4.01 | 0.89x |
| texture-16 | session-deflate | cold | 1 | 870.5 | 870.5–870.5 | 937.5 | 50.0 | 4.00 | 0.88x |
| texture-16 | both-xml | cold | 1 | 878.0 | 878.0–878.0 | 890.6 | 49.9 | 8.01 | 0.87x |
| texture-16 | both-deflate | cold | 1 | 863.9 | 863.9–863.9 | 875.0 | 49.9 | 8.01 | 0.89x |
| mymod | both-deflate | hot | 2 | 559.1 | 550.1–568.2 | 562.5 | 46.9 | 0.23 | 1.24x |
| mymod | both-xml | hot | 2 | 553.2 | 551.1–555.3 | 539.1 | 46.8 | 0.33 | 1.25x |
| mymod | session-deflate | hot | 2 | 561.4 | 555.7–567.2 | 554.7 | 46.8 | 0.23 | 1.24x |
| mymod | session-xml | hot | 2 | 556.9 | 553.3–560.6 | 578.1 | 46.8 | 0.33 | 1.25x |
| mymod | asset (type cache disabled) | hot | 2 | 699.9 | 694.0–705.8 | 679.7 | 52.7 | 0.00 | 0.99x |
| upheaval-armor | both-deflate | hot | 2 | 525.0 | 522.5–527.5 | 539.1 | 45.0 | 0.01 | 0.94x |
| upheaval-armor | both-xml | hot | 2 | 527.2 | 525.5–528.9 | 539.1 | 44.9 | 0.02 | 0.93x |
| upheaval-armor | session-deflate | hot | 2 | 527.3 | 523.0–531.6 | 554.7 | 45.1 | 0.01 | 0.93x |
| upheaval-armor | session-xml | hot | 2 | 529.7 | 524.6–534.8 | 523.4 | 44.9 | 0.02 | 0.93x |
| upheaval-armor | asset (type cache disabled) | hot | 2 | 490.5 | 486.7–494.3 | 500.0 | 46.4 | 0.00 | 1.00x |
| texture-16 | both-deflate | hot | 2 | 560.3 | 557.2–563.4 | 546.9 | 44.9 | 8.01 | 1.37x |
| texture-16 | both-xml | hot | 2 | 560.1 | 556.4–563.7 | 554.7 | 44.7 | 8.01 | 1.37x |
| texture-16 | session-deflate | hot | 2 | 559.0 | 555.1–562.9 | 546.9 | 44.9 | 4.00 | 1.37x |
| texture-16 | session-xml | hot | 2 | 558.0 | 556.1–560.0 | 562.5 | 44.8 | 4.01 | 1.37x |
| texture-16 | asset | hot | 2 | 497.1 | 495.1–499.1 | 484.4 | 45.6 | 4.00 | 1.54x |

All passed samples have identical complete output inventories within their workload.

- mymod: Complete current SDK/Mods/mymod input copy; benchmark selects base only, without packaging.
- upheaval-armor: Subset only: eleven unchanged historical Upheaval Armor.xml documents, twelve ArmorTemplate assets, generated include entry. No full Upheaval, Texture, Audio, GameObject, APT or packaging claim.
- texture-16: Synthetic only: sixteen independent Texture identities using a generated 256x256 top-left BGRA TGA. Not Upheaval or a complete real MOD; no Audio, GameObject, APT or packaging claim.
