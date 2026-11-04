# EALayer3 source and distribution notice

The selected sagetools files are Copyright 2026 Jonathan Wilson, licensed
under GPL-3.0-or-later (COPYING.GPL-3.0). Source:
https://github.com/jonwil/sagetools/tree/49373be5e922e8d7ae0b2caa2c74fa6c64d6cf4c/audiocore

`lame396/` is the LAME 3.96 encoder as carried by that revision. Its files
retain their individual authors and LGPL-2.0-or-later notices; the license is
COPYING.LGPL-2.0. Original source was also checked against
https://github.com/jonwil/lame-3.96/tree/ed9d6f623109775b89159ed506c20afb1e8f20b4

NewSdk's adapter and V1 orchestration (`../EaLayer3Encoder.cpp`) are distributed
under GPL-3.0-or-later. The encoding, delay, PCM startup crossfade and granule
interleaving follow `encealayer31.cpp`. Only the MP3-to-EA converter, its bit
reservoir, LAME encoder and the V1 startup decoder dependency closure are
included. No V2, Xbox, XMA, CLI, SIMEX runtime, importers or toolchain binaries
are imported. RA3 plugin and snrtool were used as format/behavior references,
not copied into production.

Local changes (2026-10-31): explicit local includes; allocator adapter instead
of the audio registry; extracted Bit_Reserve methods; per-instance decoder
Huffman count descriptors; automatic scalar/SSE synthesis scratch; scalar startup decoder;
once-only initialization of shared fixed LAME tables and constant masking
limits; initialized allocation pointer for exception cleanup. All per-stream
state stays in task-owned objects. The Windows x86 build remains the target.

A binary linked with this GPL code must be distributed under GPL-3.0-or-later
with copyright/license notices and its complete corresponding source, including
NewSdk integration and build scripts, available alongside the binary. Do not
distribute a binary-only compiler release. This notice does not grant rights
to game data or separately supplied third-party SDK binaries. Their existing
rights/notices remain applicable. The repository contains the selected source
and build instructions; any redistribution must include the matching source
revision and licenses. Binary distributions must identify the matching public source revision.
License text is not embedded in the EXE or added to the runtime deployment.
