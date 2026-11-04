#include <vcclr.h>
#include "../Native/Texture/PublicS3/S3_quant.h"
#using <BinaryAssetBuilder.Core.dll>

// The public S3 source remains a separate unmanaged translation unit. This
// bridge ports the bounded TexturePublicS3.cpp ABI into the unified EXE, so the
// managed texture pipeline never loads a codec or the original plugin DLL.
void CodeRGBBlockPC(RGBBlock* block);

namespace Recovered { namespace Compiler { namespace Texture {
public ref class TextureNativeCodec abstract sealed {
public:
    static int Colour(array<System::Byte>^ state, int bytes, int mode) {
        if (state == nullptr || bytes != 0x3850 || state->Length != bytes || mode != 0) return 1;
        pin_ptr<unsigned char> pinned = &state[0];
        unsigned char* buffer = pinned;
        const int* header = reinterpret_cast<const int*>(buffer);
        if ((header[0] != 2 && header[0] != 3) || header[1] != 1 ||
            header[2] < 1 || header[2] > 16 || *reinterpret_cast<int*>(buffer + 0x3030) != 71) return 2;
        RGBBlock block = {};
        block.n = header[2]; block.inLevel = 4;
        const double* values = reinterpret_cast<const double*>(buffer + 0x10);
        const double* weights = reinterpret_cast<const double*>(buffer + 0x3018);
        for (int channel = 0; channel < 3; channel++) {
            if (weights[channel] != weights[channel] || weights[channel] <= 0 || weights[channel] > 1) return 3;
            block.weight[channel] = weights[channel];
        }
        for (int i = 0; i < block.n; i++) for (int channel = 0; channel < 3; channel++) {
            double value = values[i * 3 + channel];
            if (value != value || value < 0 || value > 1) return 4;
            block.colorChannel[i][channel] = value;
        }
        CodeRGBBlockPC(&block);
        *reinterpret_cast<int*>(buffer + 0x3034) = block.outLevel;
        for (int i = 0; i < 6; i++) reinterpret_cast<int*>(buffer + 0x3038)[i] = block.endPoint[i / 3][i % 3];
        for (int i = 0; i < block.n; i++) reinterpret_cast<int*>(buffer + 0x3050)[i] = block.index[i];
        return 0;
    }

    static int Alpha(array<float>^ rgba, int count, array<int>^ endpoints, array<int>^ indices,
        [System::Runtime::InteropServices::Out] int% levels) {
        levels = 0;
        if (rgba == nullptr || endpoints == nullptr || indices == nullptr || count < 1 || count > 16 ||
            rgba->Length < count * 4 || endpoints->Length < 2 || indices->Length < count) return 1;
        AlphaBlock block = {};
        block.n = count;
        for (int i = 0; i < count; i++) {
            float value = rgba[i * 4 + 3];
            if (value != value || value < 0 || value > 1) return 2;
            block.alpha[i] = value;
        }
        CodeAlphaBlock(&block);
        levels = block.outLevel;
        endpoints[0] = block.endPoint[0]; endpoints[1] = block.endPoint[1];
        for (int i = 0; i < count; i++) indices[i] = block.index[i];
        return 0;
    }
};
}}}
