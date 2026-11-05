import { parentPort } from 'node:worker_threads';
import { decodeTexture, DecodedTexture, TextureDecodeOptions, TextureDecodeError, TextureErrorCode } from './textureDecoder';

export interface TextureDecodeJob { bytes: Uint8Array; options: TextureDecodeOptions }
export type TextureDecodeReply = { texture: DecodedTexture } | { error: { code: TextureErrorCode; message: string } };

// Fixed local worker module; no dynamic code, external paths, network access or source XML.
parentPort?.on('message', (job: TextureDecodeJob) => {
    try {
        const texture = decodeTexture(job.bytes, job.options);
        // Do not keep the transferred source buffer reachable while the idle worker awaits its next job.
        job.bytes = new Uint8Array(0);
        const buffers = new Set<ArrayBuffer>();
        for (const mip of texture.mips) buffers.add(mip.pixels.buffer as ArrayBuffer);
        for (const mip of texture.compressedMips ?? []) buffers.add(mip.bytes.buffer as ArrayBuffer);
        parentPort!.postMessage({ texture } satisfies TextureDecodeReply, [...buffers]);
    } catch (error) {
        parentPort!.postMessage({ error: { code: error instanceof TextureDecodeError ? error.code : 'invalid-header',
            message: error instanceof Error ? error.message : String(error) } } satisfies TextureDecodeReply);
    }
});
