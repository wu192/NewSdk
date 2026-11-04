# Legacy PC compatibility modifications

The source bases remain pinned by upstreamBlobSha/upstreamSha256 in PROVENANCE.json. The current modified source is recorded by blobSha/sha256, not misrepresented as unmodified upstream. This experiment does not read or call the original DLL, and never consults sample-specific output tables.

1. squish/colourblock.cpp: in the four-colour writer, canonicalize equal endpoints using nvDDS's increment/wrap/constant-index rule. The decoded original endpoint is preserved, including black and white boundaries. It changes the actual copied writer.
2. squish/alpha.cpp: canonicalize all16 equal alpha values to(a,min(a+1,255)) with zero indices, as required by immutable original output. Valid-pixel masks other than full16 retain upstream behavior.
3. nvtt/icbc.h: apply the same degenerate four-colour canonical form in output_block4. It preserves decoded pixels and their error metric.
4. amd/compressonatorxcodec.cpp/.h: add CompBlockLegacyPC over the upstream quantizer, considering8 levels,6 levels,6 excluding zero,6 excluding one,6 excluding both. Preserve normalized-float input error, low-endpoint-first ties and integer-palette/index packing. The adapter also tries the upstream normalized-float RGB API rather than discarding original source precision through packed-byte input. Original search APIs remain available for baseline comparison.

The five-candidate logic and canonical forms follow already established, verified runtime contracts in the repository; this is source adaptation, not another DLL reverse-engineering pass. Full original10240-block Debug/Release equality remains mandatory. Matching more cases is progress, not permission to enable a partially matching backend.

## Second compatibility iteration

The AMD scalar quantizer now uses a constrained Lloyd fit with local bin-extreme
moves, adapted from the repository's readable `TextureDxt5AlphaEncoder` reference.
This replaces AMD's grid search for the compatibility entry only; the original
AMD entry is retained. It does not call the recovered native quantizer or original
DLL. The reference has known scalar/x87 tie limitations, so this remains an
experiment until the complete comparison proves equality. RGB still uses AMD's
public encoder. All source hash changes are explicit in PROVENANCE.json.

## S3 four-colour contract

The initial S3 run40631487946 compiled but stopped at the first grayscale block
that chose a three-colour midpoint. DXT3/5 require four-colour RGB regardless of
endpoint ordering. The copied allSame search now excludes that BC1-only candidate
when the caller requests four colours. This changes a general format contract,
not expected bytes for a fixture. Both original upstream and patched hashes are
retained. The failed partial run cannot establish any matrix acceptance.

## S3 grayscale compatibility data

LegacyGray4.inl contains the existing PC codec's256 fixed grayscale optimization
records, already preserved as owned source in TextureColour.asm at
D_000B9608+8192 / original RVA0xBB608. These are codec algorithm constants, not
saved DXT differential payloads. Their original source, source-data SHA256 and
new file hash are explicit in PROVENANCE.json. The copied S3 single-colour entry
uses them only for one common quantized grayscale value; non-gray search remains
S3. This is a hybrid compatibility adaptation and is not described as unmodified
public S3 code or as independently generated optimization tables. No original
DLL or existing recovered encoder function is called.

## Explicit source search-budget comparison

The original complete payloads differ on22/4096 random blocks after the first
S3 adaptation;21 of those S3 blocks have lower decoded weighted error than the
legacy output. This suggests preserving the legacy stopping rule matters more
than maximizing quality. Add an explicit source budget selector: default16/8
unclipped/clipped iterations, then0/0,1/1,2/2. Each budget runs all four input/weight
precision variants and all10240 original blocks in both configurations. This is
an experiment; lower error or selecting best results per fixture is not accepted.
No original-output data is used to choose a budget at runtime.

## Interpolation rounding comparison

40632315333 completed the full stopping-policy matrix. Gray adaptation improved
complete DXT3/5 equality to5093/5120. Lower0/0 budgets regress4 blocks and fix none;
1/1 and2/2 do not change the remaining27. Restore upstream16/8. The remaining
single-color cases suggest the decoder's one-third interpolation rounding may
matter (e.g. input green3 between endpoints4 and0: rounded gives3, floor gives2,
changing strict endpoint ties). Compare single-colour-table floor, general-ramp
floor and both, independently from baseline, with separate cached lookup tables.
Variant /4 now identifies rounding policy0..3; variant bits0..1 still preserve
source/weight precision. Keep every complete prior matrix; never compare variant
numbers across receipts without checking sourceCommit. This is a general codec
arithmetic hypothesis and not fixture-ID dispatch.

## Stage diagnostic checkpoint

S3_STAGE_TRACE adds observation-only hooks to single-colour errors and final rounding inputs/outputs. A separate diagnostic reference is built from the already accepted colour-state source (40500150864); instrumented outputs must still match immutable original block gold. It is never invoked by the candidate encoder, never supplies candidate output, and does not establish integration eligibility. The 27 outlier identities occur only in the diagnostic host. No rejected interpolation/precision/budget matrix is repeated unchanged.

PC forced-four-colour MapAndRoundMult now sets the input working level count to four, as CodeRGBBlock@0x7D470 does before MapAndRoundMult@0x7BB80. Previously only the default output was forced, leaving extra mappings from shorter fits active. The S3 complete matrix now contains only four input/weight precisions; rejected floor policies are not rerun. The first stage run40634411717 compiled but its host compared lower-case reference hex to upper-case gold and failed on263; its failure is retained and the comparison now normalizes case.

## PC wrapper compatibility (in validation)

Mapping-contract source98455891 /40634767431 completed Debug/Release four-precision complete matrices: best5109/5120 full DXT3 and DXT5, alpha5120/5120;16 of27 differences fixed with no baseline regressions. Stage diagnostics28 inputs matched immutable original gold using the separate reference. Remaining:263,281,337,356,462,1445,2878,3787,3910,4001,4838.

Copied S3 source now additionally implements a readable PC wrapper: float covariance-squaring quick candidate; line clipping, endpoint pair quantization; final integer-floor palette reindex and squared-weight error comparison. These are adaptations of the general original PC wrapper contracts at0x8C1F0/0x8B420/0x8B710/0x8B930/0x7BE60/0x83CD0, not a call to existing recovered functions. Upstream S3 continuous/grid search retains rounded interpolation. This is distinct from the rejected broad-floor experiment: floor is used only for final wrapper scoring/reindex. All source changes remain experimental until full byte/float/consumer/SDK gates. No corpus IDs enter the encoder.

## Isolated PC integration candidate

Only the accepted S3 family and its explicit legacy gray constants are copied here. The colour and alpha ABIs reject invalid domain inputs and call only these copied source functions. Managed block/partial/float wrappers now consume this DLL; the prior colour/alpha assembly components remain independent historical implementations and are not called for candidate encoding. Production SDK DLL/config unchanged. Component and SDK gates pending.

The first clean integration run40647513311 stopped before business comparison because Windows auto-CRLF changed source file hashes. This directory now disables text conversion explicitly. Float-domain audit also replaces upstream's near-equality ALL_SAME_THRESHOLD with the original exact input equality (CodeRGBBlock@0x7C420); byte-domain inputs were unaffected. The sample matrix keeps all4608 existing cases and adds144 near-equal RGB float/partial cases, with complete NoOriginal replay.

Normalized float/partial integration a32b077a /40648108578 passed both configurations' full10240 blocks and4752 sample cases plus NoOriginal, but6 downstream groups found RGB/index differences on filtered/partial inputs. Final floor-palette scoring now retains x87 values through channel sums, nearest-index comparisons and the full-vs-stored-quick error comparison, matching RmsError@0x7BE60 and NoMode@0x83CD0 instruction ordering. It is new small arithmetic code within S3, not recovered encoder invocation. Earlier architecture-only recompilation did not preserve these intermediate lifetimes and is not repeated. Full affected consumer rerun pending.

7b690181 /40649209933 retained complete-block/sample equality, but the same6 consumers still failed on the same first cases: x87 final scoring alone did not fix them. Inspection of NoMode@0x83DE5..0x83E0E and restore@0x83D8A proves a separate snapshot rule: quick is copied BEFORE RmsError mutates its indices. The quick candidate now computes the original quantized-endpoint projection indices (mapping0,2,3,1 and float bias rounding) and retains a pre-scoring snapshot; only a separate copy is reindexed for scoring. A quick win restores those projection indices. Saved16x1x8 and8x8-mip counterexamples have identical endpoints with differing indices, which this rule directly addresses. Full rerun pending.

Experimental initialized-three-colour extension (not production accepted): public
S3 level3 search, fixed codec LegacyGray3 data from the already recovered source,
three-point PC floor scoring and general quick-candidate comparison. Original
source/gray data fingerprints are preserved in PROVENANCE.json. Default DXT1
state is stack-dependent and is not assigned an invented flag. No production
consumer calls the experimental export. Complete initialized-state comparisons
are required before any further claim; DDS/plugin/SDK remain separate gates.

The opaque DXT1 format0 three-colour ramp includes its fourth opaque-black entry in final error/reindex; DXT1A format1 excludes its transparent entry. This general format rule applies to both continuous and quick candidates; it is not fixture selection.

## Monolithic worker concurrency

The monolithic codec now gives each native worker its own persistent scratch and
lazy lookup state. `STATIC`/`STATIC_ARR` remain static storage and become
`thread_local`; their original zero/constant initialization and addressable double
stores are preserved. The interpolation policy, both `allSame` grids and their
initialization flags are also thread-local. Read-only size, coefficient and
mapping tables become shared `const`; fixed LegacyGray tables remain shared.
No endpoint search, rounding, x87 instruction, block mapping or DDS layout changes.
Managed Colour/Alpha calls no longer acquire a process-wide `ColourLock`.

Native TLS template payload is 24,956 bytes per OS thread: 11,120 bytes of
scratch doubles, 13,824 bytes of single-colour grids, 8 bytes of initialization
flags and 4 bytes of interpolation policy (about 24,960 with alignment). This is
per native thread, separate from per-call managed image/block buffers; the
Windows EXE TLS template also applies to threads that never call the codec. The
unchanged pre-thread-local fingerprint and current source fingerprint are both
recorded in `PROVENANCE.json`. Production/test source `8fe12ca9b` passed
[Windows 41185772818](https://github.com/wu162/NewSdk/actions/runs/41185772818):
Debug/Release cold native worker tables, four overlapping texture tasks, all three
routes' complete buffers and private CDATA versus owner execution, and owner-only
diagnostic replay/failure drain. All 31 common-case result bytes, including DDS
hashes, also match the previous accepted `4c74bf64b` build. Full CLI/cache/SDK,
independent three-file ZIPs and 22 Upheaval samples passed in the same run.
