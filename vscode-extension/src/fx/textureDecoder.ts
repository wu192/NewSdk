/** Source textures are decoded without applying output-format, gamma or compiler transforms. */
export interface TextureMip { width: number; height: number; pixels: Uint8Array }
export interface CompressedTextureMip { width: number; height: number; bytes: Uint8Array }
export interface DecodedTexture {
    width: number;
    height: number;
    /** Top-left origin, straight RGBA8; aliases mips[0].pixels. */
    pixels: Uint8Array;
    mips: TextureMip[];
    format: 'TGA' | 'DDS_RGBA' | 'DXT1' | 'DXT3' | 'DXT5';
    compressedMips?: CompressedTextureMip[];
    byteLength: number;
    source: 'source-texture';
}
export type TextureErrorCode = 'invalid-header' | 'truncated' | 'unsupported' | 'budget';
export class TextureDecodeError extends Error {
    constructor(readonly code: TextureErrorCode, message: string) { super(message); this.name = 'TextureDecodeError'; }
}
export interface TextureDecodeOptions { maxBytes?: number; maxDimension?: number; keepCompressed?: boolean }
export interface TextureMemoryEstimate { decodedBytes: number; rgbaBytes: number; compressedBytes: number; mipCount: number }

function fail(code: TextureErrorCode, text: string): never { throw new TextureDecodeError(code, text); }
function checkedSize(width: number, height: number, options: TextureDecodeOptions): number {
    if (!Number.isSafeInteger(width) || !Number.isSafeInteger(height) || width < 1 || height < 1) {
        fail('invalid-header', '贴图宽高必须是正整数。');
    }
    if (width > (options.maxDimension ?? 16384) || height > (options.maxDimension ?? 16384)) {
        fail('budget', `贴图尺寸 ${width}×${height} 超过预览尺寸预算。`);
    }
    const bytes = width * height * 4;
    if (!Number.isSafeInteger(bytes) || bytes > (options.maxBytes ?? 32 * 1024 * 1024)) {
        fail('budget', `贴图 RGBA 分配 ${bytes} 字节超过预览预算。`);
    }
    return bytes;
}
function requireBytes(bytes: Uint8Array, offset: number, length: number): void {
    if (offset < 0 || length < 0 || offset + length > bytes.byteLength) fail('truncated', '贴图数据被截断。');
}
function result(mips: TextureMip[], format: DecodedTexture['format'], compressedMips?: CompressedTextureMip[]): DecodedTexture {
    return { width: mips[0].width, height: mips[0].height, pixels: mips[0].pixels, mips, format, compressedMips,
        byteLength: mips.reduce((sum, mip) => sum + mip.pixels.byteLength, 0)
            + (compressedMips?.reduce((sum, mip) => sum + mip.bytes.byteLength, 0) ?? 0), source: 'source-texture' };
}

function validateOptions(options: TextureDecodeOptions): void {
    if (options.maxBytes !== undefined && (!Number.isSafeInteger(options.maxBytes) || options.maxBytes < 1)) {
        fail('budget', '贴图内存预算必须是正整数。');
    }
    if (options.maxDimension !== undefined && (!Number.isSafeInteger(options.maxDimension) || options.maxDimension < 1)) {
        fail('budget', '贴图尺寸预算必须是正整数。');
    }
}
function isDds(bytes: Uint8Array): boolean {
    return bytes.length >= 4 && bytes[0] === 0x44 && bytes[1] === 0x44 && bytes[2] === 0x53 && bytes[3] === 0x20;
}

/** Exact retained allocation, before any pixel or compressed-block allocation. Source bytes are charged separately. */
export function estimateTextureMemory(bytes: Uint8Array, options: TextureDecodeOptions = {}): TextureMemoryEstimate {
    validateOptions(options);
    if (isDds(bytes)) {
        const plan = ddsLayout(bytes, options);
        return { decodedBytes: plan.allocation, rgbaBytes: plan.rgbaBytes, compressedBytes: plan.allocation - plan.rgbaBytes, mipCount: plan.layouts.length };
    }
    const plan = tgaLayout(bytes, options);
    return { decodedBytes: plan.size, rgbaBytes: plan.size, compressedBytes: 0, mipCount: 1 };
}

/** Header detection is authoritative; the filename extension is intentionally absent. */
export function decodeTexture(bytes: Uint8Array, options: TextureDecodeOptions = {}): DecodedTexture {
    validateOptions(options);
    if (isDds(bytes)) {
        return decodeDds(bytes, options);
    }
    return decodeTga(bytes, options);
}

function tgaLayout(bytes: Uint8Array, options: TextureDecodeOptions) {
    requireBytes(bytes, 0, 18);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const type = bytes[2];
    if (![2, 3, 10, 11].includes(type)) fail('unsupported', `不支持的 TGA 图像类型 ${type}；支持 RGB(A)/灰度及 RLE。`);
    if (bytes[1] !== 0) fail('unsupported', '暂不支持 TGA 调色板图像。');
    if ((bytes[17] & 0xc0) !== 0) fail('unsupported', '暂不支持 TGA 交错存储。');
    const width = view.getUint16(12, true), height = view.getUint16(14, true), bits = bytes[16];
    const gray = type === 3 || type === 11, rle = type === 10 || type === 11;
    if (gray ? ![8, 16].includes(bits) : ![15, 16, 24, 32].includes(bits)) {
        fail('unsupported', `不支持的 TGA ${gray ? '灰度' : 'RGB'} ${bits} 位像素。`);
    }
    const size = checkedSize(width, height, options), stride = Math.ceil(bits / 8), count = width * height;
    const offset = 18 + bytes[0];
    requireBytes(bytes, 18, bytes[0]);
    if (!rle) requireBytes(bytes, offset, count * stride);
    else requireBytes(bytes, offset, stride + 1);
    return { width, height, bits, gray, rle, size, stride, count, offset };
}
function decodeTga(bytes: Uint8Array, options: TextureDecodeOptions): DecodedTexture {
    const { width, height, bits, gray, rle, size, stride, count, offset: initialOffset } = tgaLayout(bytes, options);
    let offset = initialOffset;
    const pixels = new Uint8Array(size);
    const top = (bytes[17] & 0x20) !== 0, right = (bytes[17] & 0x10) !== 0;
    function write(index: number, source: number): void {
        const x = right ? width - 1 - index % width : index % width;
        const row = Math.floor(index / width), y = top ? row : height - 1 - row;
        const target = (y * width + x) * 4;
        if (gray) {
            pixels[target] = pixels[target + 1] = pixels[target + 2] = bytes[source];
            pixels[target + 3] = stride === 2 ? bytes[source + 1] : 255;
        } else if (stride === 2) {
            const value = bytes[source] | bytes[source + 1] << 8;
            pixels[target] = Math.round(((value >>> 10) & 31) * 255 / 31);
            pixels[target + 1] = Math.round(((value >>> 5) & 31) * 255 / 31);
            pixels[target + 2] = Math.round((value & 31) * 255 / 31);
            pixels[target + 3] = bits === 16 && (bytes[17] & 15) !== 0 ? (value & 0x8000 ? 255 : 0) : 255;
        } else {
            pixels[target] = bytes[source + 2]; pixels[target + 1] = bytes[source + 1]; pixels[target + 2] = bytes[source];
            pixels[target + 3] = stride === 4 ? bytes[source + 3] : 255;
        }
    }
    let written = 0;
    while (written < count) {
        let length = 1, repeated = false;
        if (rle) {
            requireBytes(bytes, offset, 1);
            const packet = bytes[offset++]; length = (packet & 127) + 1; repeated = (packet & 128) !== 0;
        }
        if (length > count - written) fail('invalid-header', 'TGA RLE 包超出图像像素数。');
        requireBytes(bytes, offset, repeated ? stride : stride * length);
        for (let j = 0; j < length; j++) write(written++, offset + (repeated ? 0 : j * stride));
        offset += repeated ? stride : stride * length;
    }
    return result([{ width, height, pixels }], 'TGA');
}

interface Mask { value: number; shift: number; maximum: number }
function mask(value: number, bits: number): Mask {
    value >>>= 0;
    if (!value) return { value: 0, shift: 0, maximum: 0 };
    if (bits < 32 && value >= 2 ** bits) fail('invalid-header', 'DDS 颜色掩码超出像素位数。');
    let shift = 0;
    while (((value >>> shift) & 1) === 0) shift++;
    const maximum = value >>> shift;
    if ((BigInt(maximum) & (BigInt(maximum) + 1n)) !== 0n) fail('unsupported', '暂不支持非连续 DDS 颜色掩码。');
    return { value, shift, maximum };
}
function component(value: number, channel: Mask, fallback: number): number {
    return channel.value ? Math.round(((value & channel.value) >>> channel.shift) / channel.maximum * 255) : fallback;
}
function color565(value: number): number[] {
    // D3D's normalized 565 endpoints, rounded once into an 8-bit target.
    return [Math.round(((value >>> 11) & 31) * 255 / 31), Math.round(((value >>> 5) & 63) * 255 / 63), Math.round((value & 31) * 255 / 31), 255];
}
function colorTable(a: number, b: number, forceFour: boolean): number[][] {
    const c0 = color565(a), c1 = color565(b);
    if (a > b || forceFour) return [c0, c1,
        [0, 1, 2].map(i => Math.floor((2 * c0[i] + c1[i]) / 3)).concat(255),
        [0, 1, 2].map(i => Math.floor((c0[i] + 2 * c1[i]) / 3)).concat(255)];
    return [c0, c1, [0, 1, 2].map(i => Math.floor((c0[i] + c1[i]) / 2)).concat(255), [0, 0, 0, 0]];
}

function ddsLayout(bytes: Uint8Array, options: TextureDecodeOptions) {
    requireBytes(bytes, 0, 128);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const u32 = (offset: number): number => view.getUint32(offset, true);
    if (u32(4) !== 124 || u32(76) !== 32) fail('invalid-header', 'DDS 头大小无效。');
    const height = u32(12), width = u32(16), depth = u32(24), caps2 = u32(112), flags = u32(8);
    if (depth > 1 || (caps2 & 0x200000) !== 0 || (flags & 0x800000) !== 0) fail('unsupported', '暂不支持 DDS Volume 纹理。');
    if ((caps2 & 0xfe00) !== 0) fail('unsupported', '暂不支持 DDS Cube 纹理。');
    const pfFlags = u32(80), fourCC = String.fromCharCode(...bytes.subarray(84, 88));
    if ((pfFlags & 4) !== 0 && fourCC === 'DX10') fail('unsupported', '暂不支持 DDS DX10 扩展头。');
    let format: DecodedTexture['format'] = 'DDS_RGBA';
    const compressed = (pfFlags & 4) !== 0;
    if (compressed) {
        if (!['DXT1', 'DXT3', 'DXT5'].includes(fourCC)) fail('unsupported', `暂不支持 DDS 压缩格式 ${fourCC.replace(/\0/g, '')}。`);
        format = fourCC as 'DXT1' | 'DXT3' | 'DXT5';
    } else if (!(pfFlags & 0x40)) fail('unsupported', '暂不支持此 DDS 像素类型；需要 RGB(A) 掩码。');
    checkedSize(width, height, options);
    const mipCount = u32(28) || 1;
    const maximumMips = Math.floor(Math.log2(Math.max(width, height))) + 1;
    if (mipCount > maximumMips) fail('invalid-header', 'DDS mip 数超过尺寸允许的完整链。');
    const bits = u32(88), masks = [u32(92), u32(96), u32(100), u32(104)];
    let channels: Mask[] = [];
    if (!compressed) {
        if (![16, 24, 32].includes(bits)) fail('unsupported', `暂不支持 DDS ${bits} 位 RGB(A)。`);
        if (masks.slice(0, 3).some(value => value === 0)) fail('invalid-header', 'DDS RGB 掩码缺失。');
        for (let i = 0; i < masks.length; i++) for (let j = i + 1; j < masks.length; j++) {
            if ((masks[i] & masks[j]) !== 0) fail('invalid-header', 'DDS 颜色掩码重叠。');
        }
        channels = masks.map(value => mask(value, bits));
    }
    const blockSize = format === 'DXT1' ? 8 : 16;
    const firstTightPitch = width * bits / 8;
    const headerPitch = (flags & 8) !== 0 ? u32(20) : 0;
    if (!compressed && headerPitch && headerPitch < firstTightPitch) fail('invalid-header', 'DDS 行跨度小于像素行。');
    const alignRows = headerPitch > firstTightPitch && headerPitch === Math.ceil(firstTightPitch / 4) * 4;
    const layouts: { width: number; height: number; offset: number; length: number; pitch: number }[] = [];
    let offset = 128, w = width, h = height, allocation = 0, rgbaBytes = 0;
    for (let level = 0; level < mipCount; level++) {
        const pitch = compressed ? Math.ceil(w / 4) * blockSize
            : level === 0 && headerPitch ? headerPitch : alignRows ? Math.ceil(w * bits / 8 / 4) * 4 : w * bits / 8;
        const length = pitch * (compressed ? Math.ceil(h / 4) : h);
        requireBytes(bytes, offset, length);
        rgbaBytes += checkedSize(w, h, options);
        allocation += w * h * 4 + (compressed && options.keepCompressed !== false ? length : 0);
        if (allocation > (options.maxBytes ?? 32 * 1024 * 1024)) fail('budget', 'DDS mip 链与压缩块超过预览内存预算。');
        layouts.push({ width: w, height: h, offset, length, pitch }); offset += length;
        w = Math.max(1, Math.floor(w / 2)); h = Math.max(1, Math.floor(h / 2));
    }
    return { layouts, format, channels, compressed, bits, blockSize, allocation, rgbaBytes };
}
function decodeDds(bytes: Uint8Array, options: TextureDecodeOptions): DecodedTexture {
    const { layouts, format, channels, compressed, bits, blockSize } = ddsLayout(bytes, options);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const u32 = (offset: number): number => view.getUint32(offset, true);
    const mips: TextureMip[] = [], compressedMips: CompressedTextureMip[] | undefined = compressed && options.keepCompressed !== false ? [] : undefined;
    for (const layout of layouts) {
        const pixels = new Uint8Array(layout.width * layout.height * 4);
        if (!compressed) {
            const stride = bits / 8;
            for (let y = 0; y < layout.height; y++) for (let x = 0; x < layout.width; x++) {
                const source = layout.offset + y * layout.pitch + x * stride;
                let value = 0;
                for (let b = 0; b < stride; b++) value += bytes[source + b] * 2 ** (8 * b);
                const target = (y * layout.width + x) * 4;
                for (let channel = 0; channel < 4; channel++) pixels[target + channel] = component(value >>> 0, channels[channel], channel === 3 ? 255 : 0);
            }
        } else {
            for (let by = 0; by < Math.ceil(layout.height / 4); by++) for (let bx = 0; bx < Math.ceil(layout.width / 4); bx++) {
                const block = layout.offset + by * layout.pitch + bx * blockSize;
                const colorOffset = block + (format === 'DXT1' ? 0 : 8);
                const palette = colorTable(view.getUint16(colorOffset, true), view.getUint16(colorOffset + 2, true), format !== 'DXT1');
                const indices = u32(colorOffset + 4);
                let alphaIndices = 0n;
                const alpha: number[] = [];
                if (format === 'DXT5') {
                    const a = bytes[block], b = bytes[block + 1]; alpha.push(a, b);
                    if (a > b) for (let k = 1; k <= 6; k++) alpha.push(Math.floor(((7 - k) * a + k * b) / 7));
                    else {
                        for (let k = 1; k <= 4; k++) alpha.push(Math.floor(((5 - k) * a + k * b) / 5));
                        alpha.push(0, 255);
                    }
                    for (let k = 0; k < 6; k++) alphaIndices |= BigInt(bytes[block + 2 + k]) << BigInt(k * 8);
                }
                for (let i = 0; i < 16; i++) {
                    const x = bx * 4 + i % 4, y = by * 4 + Math.floor(i / 4);
                    if (x >= layout.width || y >= layout.height) continue;
                    const color = palette[(indices >>> (2 * i)) & 3], target = (y * layout.width + x) * 4;
                    pixels[target] = color[0]; pixels[target + 1] = color[1]; pixels[target + 2] = color[2];
                    pixels[target + 3] = format === 'DXT3' ? ((bytes[block + Math.floor(i / 2)] >>> ((i % 2) * 4)) & 15) * 17
                        : format === 'DXT5' ? alpha[Number((alphaIndices >> BigInt(i * 3)) & 7n)] : color[3];
                }
            }
            compressedMips?.push({ width: layout.width, height: layout.height, bytes: bytes.slice(layout.offset, layout.offset + layout.length) });
        }
        mips.push({ width: layout.width, height: layout.height, pixels });
    }
    return result(mips, format, compressedMips);
}
