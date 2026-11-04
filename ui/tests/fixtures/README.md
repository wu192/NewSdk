The binary FLA fixtures are from JPEXS FLA Convert 3.0.0, tag `v3.0.0`,
`testdata/fla/f8`, https://github.com/jindrapetrik/jpexs-fla-convert.
They are redistributed under LGPL-2.1-or-later; see
`../../licenses/JPEXS-LGPL-2.1.txt`. `flash8-empty.fla` is the upstream
`0001_empty_doc.fla`. These are real binary Flash 8 source documents, not
renamed SWF or ZIP/XFL files.

`legacy-assets.sha256` contains 54 SHA-256 digests of the **final**
root-prefixed AssetBuffers produced at commit
`e235cededd385a6895895ceb354f46a6667caaa3`, before removing the intermediate
SWF/APT pipeline. Keys are `source-file:asset-type:asset-name`. The baseline
covers all the Flash 8 fixtures plus the source-folder/include/AS2-class
scenario in `Program.cs` after the include changes. Tests compare the direct
compiler's final buffers without retaining the old intermediate encoders.

`legacy-layout-assets.sha256` adds 8 final AssetBuffer digests captured from
the same old implementation, with the in-memory source records described in
`LayoutDocument.cs`. This covers button states/hit tests, dynamic Chinese
text, a linked AS2 class, event handlers and a premultiplied source bitmap.
Only final asset hashes are retained, not old conversion code or files.

`mx2004-empty.fla` is the upstream Flash MX 2004 empty document. It is accepted
as revision 0x38; CS3 remains an explicit unsupported-version fixture.
