using System;
using System.IO;
using System.Xml;
using BinaryAssetBuilder;
using BinaryAssetBuilder.Core;

namespace Recovered.Compiler.Texture
{
    public partial class Plugin
    {
        private static TexturePacker.Image LoadAtlasImage(string path)
        {
            try { return TextureImageLoader.Load(path); }
            catch (InvalidDataException)
            {
                throw new BinaryAssetBuilderException(null, ErrorCode.BadTextureFile,
                    "D3DX cannot load the texture from disk.", new object[0]);
            }
        }

        private AssetBuffer CompileAtlas(InstanceDeclaration declaration)
        {
            // Original marshals declaration.Node (the preprocessed/inherited
            // Core view), and only direct Tile children in the asset namespace.
            XmlNode node = declaration.Node;
            var packer = new TexturePacker(LoadAtlasImage);
            int declared = 0;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.LocalName != "Tile" || child.NamespaceURI != "uri:ea.com:eala:asset") continue;
                ++declared;
                var tile = (XmlElement)child;
                packer.AddTexture(tile.GetAttribute("BaseTexture"), tile.GetAttribute("NormalTexture"),
                    AtlasTextureId(tile.GetAttribute("TextureID")));
            }
            // Empty declared list bypasses FinalizeTracker. A nonempty list
            // whose files are all missing still produces the empty 512 canvas.
            if (declared == 0) return new AssetBuffer();
            packer.PackTextures();
            byte[] baseDds = TextureAtlasDdsEncoder.Encode((uint)packer.BaseAtlas.Width,
                (uint)packer.BaseAtlas.Height, ImageBytes(packer.BaseAtlas), false);
            byte[] normalDds = packer.HasNormals ? TextureAtlasDdsEncoder.Encode((uint)packer.NormalAtlas.Width,
                (uint)packer.NormalAtlas.Height, ImageBytes(packer.NormalAtlas), true) : null;
            TexturePacker.Tile[] tiles = packer.Tiles;
            int normalAt = checked(28 + ((baseDds.Length + 3) & ~3));
            int tilesAt = checked(normalAt + (normalDds == null ? 0 : ((normalDds.Length + 3) & ~3)));
            byte[] data = new byte[checked(tilesAt + tiles.Length * 12)];
            Write32(data, 4, (uint)tiles.Length);
            Write32(data, 8, (uint)tilesAt);
            Write32(data, 12, 28);
            Write32(data, 16, (uint)baseDds.Length);
            Buffer.BlockCopy(baseDds, 0, data, 28, baseDds.Length);
            if (normalDds != null)
            {
                Write32(data, 20, (uint)normalAt);
                Write32(data, 24, (uint)normalDds.Length);
                Buffer.BlockCopy(normalDds, 0, data, normalAt, normalDds.Length);
            }
            for (int i = 0; i < tiles.Length; i++)
            {
                int at = tilesAt + i * 12; TexturePacker.Tile tile = tiles[i];
                Write32(data, at, tile.Id);
                WriteAtlas16(data, at + 4, tile.UpperLeftX); WriteAtlas16(data, at + 6, tile.UpperLeftY);
                WriteAtlas16(data, at + 8, tile.BottomRightX); WriteAtlas16(data, at + 10, tile.BottomRightY);
            }
            byte[] relo = new byte[normalDds == null ? 12 : 16];
            Write32(relo, 0, 12);
            if (normalDds != null) Write32(relo, 4, 20);
            Write32(relo, relo.Length - 8, 8); Write32(relo, relo.Length - 4, uint.MaxValue);
            return new AssetBuffer { InstanceData = data, RelocationData = relo, ImportsData = new byte[0] };
        }
        private static void WriteAtlas16(byte[] data, int at, ushort value)
        {
            data[at] = (byte)value; data[at + 1] = (byte)(value >> 8);
        }
        // Original Tile marshaler uses Win32 strtoul(..., base=0), including
        // octal/hex, a leading sign, prefix termination and overflow saturation.
        private static uint AtlasTextureId(string text)
        {
            int i = 0; while (i < text.Length && " \t\r\n\v\f".IndexOf(text[i]) >= 0) ++i;
            bool negative = i < text.Length && text[i] == '-';
            if (i < text.Length && (text[i] == '+' || text[i] == '-')) ++i;
            int radix = 10;
            if (i < text.Length && text[i] == '0')
            {
                radix = 8;
                if (i + 2 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X') && AtlasDigit(text[i + 2]) < 16)
                { radix = 16; i += 2; }
            }
            uint value = 0; bool overflow = false;
            for (; i < text.Length; ++i)
            {
                int digit = AtlasDigit(text[i]); if (digit >= radix) break;
                if (value > (uint.MaxValue - digit) / radix) overflow = true;
                value = unchecked(value * (uint)radix + (uint)digit);
            }
            // VC8 strtoul saturates the magnitude, then applies the sign even
            // on overflow: -4294967296 therefore becomes 1, not UINT_MAX.
            if (overflow) value = uint.MaxValue;
            return negative ? unchecked(0u - value) : value;
        }
        private static int AtlasDigit(char c)
        {
            return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : 16;
        }
    }
}
