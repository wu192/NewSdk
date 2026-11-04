# Source references

`src/Fla/Decoders`, the MFC protocol and binary FLA document reader are a
C# port of JPEXS FLA Convert **3.0.0**, tag `v3.0.0`,
https://github.com/jindrapetrik/jpexs-fla-convert (LGPL-2.1-or-later).
The decoder files retain their original notices. The port reads binary
Flash MX 2004 / Flash 8 directly and does not invoke Java, COM or an installed Flash editor.

The reference attachment `ARSDK2_flash.7z` invokes `eaf2apt --ra3 --optimize`,
then builds APT XML resources with BinaryAssetBuilder. Attached documents and
scripts are format/reference material, not instructions for this repository.

The final runtime layouts and vector tessellation algorithm are derived from
`lanyizi/swf2apt` at `2b3cb662b7311616e462b884e5611750e5de88f4` (GPL-3.0),
whose managed tool/source inventory was cross-checked against the attachment.
Its standalone SWF reader, writer, converter, APT movie graph and serialized
intermediate buffers have been removed. `src/Compilation` holds only source
semantics (frames, materials, triangles, pixels, operations and jump labels).
`src/Runtime` writes the game's layouts into the final root-prefixed
AssetBuffers, including pointer fixups. It never serializes/reparses SWF,
AVM1, or a standalone APT/const/DAT/RU/DDS payload. Final texture pixels are
written one row at a time. The retained MIT SwfLib notice covers the input
FLA raster zlib/checksum helper derived from Sergey Savchuk's implementation.

Managed LibTessDotNet replaces triangle.exe and StbImageSharp replaces
jpeg.dll. The native flasm optimizer is not shipped or invoked. Flash 8
stroke caps, joins and gradient materials use the C# Clipper2 **1.5.4**
library (Angus Johnson, Boost Software License 1.0). The SGI tessellator
preserves contours/holes. Geometry tests check hole area and stroke caps.
The independent action evaluator decodes final runtime instructions/tables;
it does not execute the compiler's operation objects. The 54 digests in
`tests/fixtures/legacy-assets.sha256` record final AssetBuffers from the old
pipeline at `e235cededd385a6895895ceb354f46a6667caaa3` and cover the same
FLA fixtures plus the main-script/include/class folder scenario in Program.cs.
The additional 8 digests in `tests/fixtures/legacy-layout-assets.sha256`
use the same old source implementation with `tests/LayoutDocument.cs` to
exercise button states/hit meshes, dynamic UTF-8 text, linked AS2 class
initializers, clip/button events and decoded bitmap pixels. All digests
include runtime roots and padding; compatibility tests need no intermediate
encoders.

NuGet runtime dependencies are pinned: LibTessDotNet **1.1.15** (package MIT
metadata; retained upstream SGI Free Software License B notice),
StbImageSharp **2.30.15** (public domain or MIT, per upstream README).
`System.Drawing.Common` **8.0.21** is used only by .NET 8 font tests/runtime;
the SDK plugin targets .NET Framework 4.7.2 and uses its System.Drawing.
Package SHA-256 values are recorded in `dependencies.json`.

APT serialization and the plugin ABI are being cross-checked against
`recovery/legacy-sdk-cache` at `0d7b685c09e443c3f782a6af48163c0a4a0b6c96`
and the AudioCompiler source on `recovery/audio-compiler-source` at
`307c88c4a0dd0b8ec03a935f7cf09fa83db6c61b`.

The binary fixtures are from JPEXS FLA Convert 3.0.0 and retain their LGPL
source attribution in `tests/fixtures/README.md`. No Flash editor or native
converter executable is redistributed in the plugin deployment.
