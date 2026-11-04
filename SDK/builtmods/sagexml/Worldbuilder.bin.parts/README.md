# Official WorldBuilder 1.12 binary reference stream

These 156 independent raw Deflate blocks preserve every byte of the official
`WBData_12.big` → `data/worldbuilder.bin`; no asset selection or re-encoding is
performed. `Worldbuilder.manifest`, `.relo`, and `.imp` are the original companion
files. Source identities are recorded in `../Worldbuilder.provenance.json`.

`index.json` uses schema version 1. Blocks start at original byte offset zero
(including the four-byte stream checksum), with an 8 MiB uncompressed block size.
Each block's `sha256` hashes its **uncompressed** bytes; the top-level `sha256`
hashes the complete original `.bin`. The production resolver decompresses only
the blocks needed for an asset and caches its current block. It does not restore
the 1.3 GB file before compiling.

The complete reconstruction has been verified as 1,308,421,704 bytes with SHA-256:

```text
682b7c654c589a61e90eae565860497e5847e02bcee4ab303d6161ecc7280e36
```

Run from the repository root to restore the original binary for the SDK oracle:

```sh
python recovery/scripts/ImportWorldbuilderReferences.py unpack --parts SDK/builtmods/sagexml/Worldbuilder.bin.parts --output-bin work/Worldbuilder.bin
```

To reproduce these blocks, extract `WBData_12.big` from the official installer,
then import into a fresh destination (or remove the existing parts directory):

```sh
python recovery/scripts/ImportWorldbuilderReferences.py import --big work/WBData_12.big --output work/worldbuilder-reference --source-zip work/CCRA3WorldBuilder_ModSDKv112.zip --installer work/CCRA3WorldBuilder_ModSDKv112.exe
```

The importer verifies that an existing destination manifest matches the official
one, hashes the source artifacts, and validates full byte-exact reconstruction.
