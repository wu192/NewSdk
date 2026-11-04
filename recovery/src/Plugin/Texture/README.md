# Built-in PC texture compiler

`Recovered.Compiler.Texture.Plugin` is compiled into `NewModCompiler.exe`. It
implements `Texture`, `OnDemandTexture` and `TerrainTextureAtlas` without loading
`BinaryAssetBuilder.TextureCompiler.dll` or any replacement codec DLL. The S3
native implementation is linked into the same EXE and called through
`recovery/src/Bridge/TextureCompiler.cpp`. All three texture routes now prepare
read-only input leases and a header-based memory budget on the owner, then decode
and encode on workers. S3 scratch, interpolation policy and lazy tables use native
thread-local storage; per-call managed buffers and GDI+ objects are not shared.
Diagnostics are collected per work item and replayed by the owner in commit order.
Unavailable leases, unrecognized image headers or insufficient budget fall back
to the owner.

Production/test source `8fe12ca9b` passed the complete
[Windows run 41185772818](https://github.com/wu162/NewSdk/actions/runs/41185772818):
Debug/Release builds, exact original metadata/transcripts, component and actual
four-worker execution, cache matrices, complete mymod original comparison,
independent three-file ZIP deployment and all 22 Upheaval samples. The
[common-format receipt](../../../evidence/texture/plugin-validation/41185772818-1/common-summary.json)
records 31 cases per configuration with byte-identical results to the prior
accepted source and 16 actual CLI cases. Packages are listed in the
[final artifact](https://github.com/wu162/NewSdk/actions/runs/41185772818/artifacts/13037167160);
the final archive has been independently downloaded and its SHA256 verified.
All six runtime files and both exact three-file ZIPs match the production build
receipts and release manifests; see the
[full validation summary](../../../evidence/texture/plugin-validation/41185772818-1/full-summary.json).

Common input/output coverage:

| Input | Handling |
| --- | --- |
| DDS, including uppercase `.DDS` | Complete bytes copied without recompression, preserving existing cube/volume DDS |
| TGA | Recovered raw/RLE true-color, grayscale and indexed decoders |
| PNG, BMP, DIB | Recovered direct-color decoders and managed BI_RGB indexed BMP/DIB (1/4/8-bit); other standard PNG/BMP variants use Windows GDI+ |
| JPEG, GIF, TIFF | Windows GDI+ decoder; GIF/TIFF use the first frame |
| PPM | Managed P3/P6 reader, including 8/16-bit big-endian P6 samples |

Raster outputs support `A8R8G8B8`, `X8R8G8B8`, `R5G6B5`, `DXT1`, `DXT1A`,
`DXT3` and `DXT5`, with optional mipmaps. The existing alpha inference is retained:
default DXT1 becomes DXT3 for constant/nonopaque or one-bit alpha, or DXT5 for
other alpha. NPOT raster images are resized to power-of-two dimensions by the
recovered resampler. `VolumeTexture` accepts a horizontal strip of square slices.
Raster `CubeTexture` reports the existing error; prebuilt cube DDS is preserved.

The reference raw DDS writers, triangle filtering, atlas packing, buffer
relocations and OnDemand cdata layout are retained. The public S3 source retains
DXT3/5 float and partial-block handling. Managed DXT1/DXT1A provide deterministic,
standard BC1 output for common opaque and alpha inputs, including blocks the
reference recovery branch did not support. These new BC1 bytes and GDI+/PPM
expanded inputs do not claim byte identity with the legacy native compiler.
The original plugin version `4202`, runtime type hashes and asset layouts are
preserved. ProcessingHash values also remain exactly those of the original DLL:
`Texture=2673862832`, `OnDemandTexture=4165508788`,
`TerrainTextureAtlas=1182597302`. Switching implementations does not create a new
cache identity or clear existing outputs. Use clean or independent session,
asset-cache and intermediate directories when comparing implementations so
existing BC1 results are not reused across different encoders.

Source origin: [`recovery/texturecompiler-source` at
`b30dffac5aeb027cf37a6dac0cd47beafe9fbea0`](https://github.com/wu162/NewSdk/tree/b30dffac5aeb027cf37a6dac0cd47beafe9fbea0/src/TextureCompiler).
The recovered decoder/raw/mip/packer/catalog source was copied from its
`Managed` directory. `Plugin.Metadata.cs`, `Plugin.Dds.cs`, `Plugin.Images.cs`,
`TextureImageLoader.cs`, `TextureDdsEncoder.cs` and the S3 call boundary were
adapted for this monolithic host. `TextureBlockCompression.cs` is the independent
managed BC1 encoder; `TextureIndexedBmpDecoder.cs` decodes standard indexed
BMP/DIB palette, packed indices, padded rows and top-down/bottom-up storage. Public S3 upstream notices, fingerprints and compatibility
patch notes are preserved in `recovery/src/Native/Texture/PublicS3`.

Build the S3 native translation unit with `/arch:SSE2 /fp:strict`. Windows x86
Actions must validate serial/parallel complete payloads, unchanged original
metadata, the linked plugin, common formats and the existing legacy
comparison/SDK checks; a successful source build alone is not a byte-comparison
receipt. JPEG output depends on the Windows GDI+ codec and may differ from the
reference IJG codec. No original-DLL fallback is present.
