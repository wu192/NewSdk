# Native tokenizer cost probes

Research only. These programs include the unchanged production helper or use the
root field-name list, then compare a temporary algorithm prototype. They are not
production sources, Windows C++/CLI integration tests or full MOD benchmarks.
The captured results are Linux native x86_64 / GCC 14.2 / `-O2`; do not report their
speed ratios as GameObject, Weapon or full MOD speedups.

Build each program independently from this directory (C++17):

```
g++ -std=c++17 -O2 EnumLookup.cpp -o enum-cost
g++ -std=c++17 -O2 RelocationLookup.cpp -o relocation-cost
g++ -std=c++17 -O2 AttributeLookup.cpp -o attribute-cost
```

Run the programs to emit CSV. EnumLookup requires the two existing enum arrays to
remain strictly sorted and compares the masks before timing. RelocationLookup
uses synthetic owner blocks, verifies every query plus failed-lookup output
preservation, and reports index construction separately. AttributeLookup counts
lookup allocations in a separate pass; allocation counting is disabled during
its five alternating measurement pairs. Its committed key list is the combined
88 GameObject and 104 Weapon root names at source 9f9d1767. GameObject's real loop
skips Browser, while the model deliberately traverses the complete name list.

See `../../docs/TOKENIZER_PLUGIN_NEXT_OPTIMIZATIONS.md` for scope and constraints.
