using System;
using System.IO;
using System.Linq;
using Dreamness.Ra3.Map.Parser.Asset.Base;
using Dreamness.Ra3.Map.Parser.Asset.Util;
using Dreamness.Ra3.Map.Parser.Core.Map;

/// <summary>
/// Extends the playable right edge by repeating the last complete player lanes.
/// Works on the raw terrain planes to preserve all flags and texture/blend tables.
/// Object, player, script, and trigger-area edits are intentionally caller-owned.
/// </summary>
public static class TerrainExpansion
{
    public sealed record Result(int OldWidth, int NewWidth, int Height, int Border,
        int AddedColumns, int SourceStartGridX, int OldRightEdgeGridX, int NewRightEdgeGridX);

    public static Result ExpandRight(Ra3Map map, int addedColumns = 196, int lanePitch = 98)
        => ExpandRight(map.Context, addedColumns, lanePitch);

    public static Result ExpandRight(MapContext context, int addedColumns = 196, int lanePitch = 98)
    {
        var oldHeightAsset = context.HeightMapDataAsset;
        var oldBlendAsset = context.BlendTileDataAsset;
        int width = oldHeightAsset.MapWidth, height = oldHeightAsset.MapHeight;
        int border = oldHeightAsset.BorderWidth;
        int rightEdge = width - border;
        int playableWidth = width - border * 2;
        if (addedColumns <= 0 || addedColumns > playableWidth || addedColumns % lanePitch != 0)
            throw new ArgumentOutOfRangeException(nameof(addedColumns), "Must add whole player lanes that fit inside the original playable width.");
        if (oldHeightAsset.Version != 6 || oldBlendAsset.Version != 27)
            throw new NotSupportedException("Only HeightMapData v6 and BlendTileData v27 are supported.");

        byte[] originalHeight = Payload(oldHeightAsset, context);
        byte[] originalBlend = Payload(oldBlendAsset, context);
        byte[] expandedHeight = ExpandHeight(originalHeight, width, height, border, addedColumns);
        byte[] expandedBlend = ExpandBlend(originalBlend, width, height, rightEdge, addedColumns);
        var heightAsset = ParseReplacement(oldHeightAsset, expandedHeight, context);
        context.OverrideAsset(heightAsset); // Blend parsing uses the new height dimensions.
        try
        {
            var blendAsset = ParseReplacement(oldBlendAsset, expandedBlend, context);
            if (blendAsset.Errored) throw blendAsset.ErrorException;
            context.OverrideAsset(blendAsset);
        }
        catch
        {
            context.OverrideAsset(oldHeightAsset);
            throw;
        }
        return new Result(width, width + addedColumns, height, border, addedColumns,
            rightEdge - addedColumns, rightEdge, rightEdge + addedColumns);
    }

    private static byte[] Payload(BaseAsset asset, MapContext context)
    {
        byte[] serialized = asset.ToBytes(context);
        if (serialized.Length < 10) throw new InvalidDataException("Asset header is incomplete.");
        return serialized.AsSpan(10).ToArray();
    }

    private static BaseAsset ParseReplacement(BaseAsset source, byte[] payload, MapContext context)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(source.Id);
            writer.Write(source.Version);
            writer.Write(payload.Length);
            writer.Write(payload);
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream);
        var replacement = AssetParser.FromBinaryReader(reader, context);
        if (replacement.Errored) throw replacement.ErrorException;
        return replacement;
    }

    private static byte[] ExpandHeight(byte[] payload, int width, int height, int border, int added)
    {
        using var input = new BinaryReader(new MemoryStream(payload));
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        if (input.ReadInt32() != width || input.ReadInt32() != height || input.ReadInt32() != border)
            throw new InvalidDataException("Height dimensions differ from parsed dimensions.");
        writer.Write(width + added);
        writer.Write(height);
        writer.Write(border);
        int borderCount = input.ReadInt32();
        writer.Write(borderCount);
        for (int i = 0; i < borderCount; i++)
        {
            int x1 = input.ReadInt32(), y1 = input.ReadInt32();
            int x2 = input.ReadInt32(), y2 = input.ReadInt32();
            writer.Write(x1);
            writer.Write(y1);
            writer.Write(x2 == width - border * 2 ? x2 + added : x2);
            writer.Write(y2);
        }
        if (input.ReadInt32() != width * height) throw new InvalidDataException("Height area mismatch.");
        writer.Write((width + added) * height);
        ExpandBytePlane(input, writer, width, height, width - border, added, 2);
        // Preserve unknown extensions, should a compatible producer add them.
        writer.Write(input.ReadBytes(checked((int)(input.BaseStream.Length - input.BaseStream.Position))));
        return output.ToArray();
    }

    private static byte[] ExpandBlend(byte[] payload, int width, int height, int rightEdge, int added)
    {
        using var input = new BinaryReader(new MemoryStream(payload));
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        if (input.ReadInt32() != width * height) throw new InvalidDataException("Blend area mismatch.");
        writer.Write((width + added) * height);
        // The map has no blend/cliff definitions. Definitions encode sub-tile phases,
        // so a map that has them requires rephasing those definitions as well.
        int oldTailOffset = checked(4 + 8 * width * height + 8 * ((width + 7) / 8) * height + width * height);
        if (BitConverter.ToInt32(payload, oldTailOffset + 4) != 1 || BitConverter.ToInt32(payload, oldTailOffset + 8) != 1)
            throw new NotSupportedException("Blend/cliff definitions require additional sub-tile remapping.");
        ExpandTilePlane(input, writer, width, height, rightEdge, added);
        // Blends, SingleEdgeBlends, CliffBlends.
        for (int plane = 0; plane < 3; plane++) ExpandBytePlane(input, writer, width, height, rightEdge, added, 2);
        // Impassable, ImpassableToPlayers, PassageWidths, ExtraPassable,
        // Visibilities, Buildabilities, ImpassableToAirUnits, TiberiumGrowabilities.
        for (int plane = 0; plane < 8; plane++) ExpandBitPlane(input, writer, width, height, rightEdge, added);
        // DynamicShrubberies.
        ExpandBytePlane(input, writer, width, height, rightEdge, added, 1);
        // TextureCellCount, blend counts, textures, magic values and blend infos.
        writer.Write(input.ReadBytes(checked((int)(input.BaseStream.Length - input.BaseStream.Position))));
        return output.ToArray();
    }

    private static void ExpandTilePlane(BinaryReader input, BinaryWriter output,
        int width, int height, int rightEdge, int added)
    {
        for (int y = 0; y < height; y++)
        {
            byte[] row = input.ReadBytes(width * 2);
            if (row.Length != width * 2) throw new EndOfStreamException("Terrain tiles truncated.");
            output.Write(row, 0, rightEdge * 2);
            for (int x = rightEdge; x < width + added; x++)
            {
                int sourceX = x - added;
                int texture = BitConverter.ToUInt16(row, sourceX * 2) / 64;
                int subTile = y % 8 / 2 * 16 + y % 2 * 2 + x % 8 / 2 * 4 + x % 2;
                output.Write(checked((ushort)(texture * 64 + subTile)));
            }
        }
    }

    private static void ExpandBytePlane(BinaryReader input, BinaryWriter output,
        int width, int height, int rightEdge, int added, int elementSize)
    {
        int sourceStart = rightEdge - added;
        int rowLength = checked(width * elementSize);
        for (int y = 0; y < height; y++)
        {
            byte[] row = input.ReadBytes(rowLength);
            if (row.Length != rowLength) throw new EndOfStreamException("Terrain plane truncated.");
            output.Write(row, 0, rightEdge * elementSize);
            output.Write(row, sourceStart * elementSize, added * elementSize);
            output.Write(row, rightEdge * elementSize, (width - rightEdge) * elementSize);
        }
    }

    private static void ExpandBitPlane(BinaryReader input, BinaryWriter output,
        int width, int height, int rightEdge, int added)
    {
        int oldStride = (width + 7) / 8;
        int newWidth = width + added;
        int newStride = (newWidth + 7) / 8;
        for (int y = 0; y < height; y++)
        {
            byte[] row = input.ReadBytes(oldStride);
            if (row.Length != oldStride) throw new EndOfStreamException("Terrain bit plane truncated.");
            byte[] expanded = new byte[newStride];
            for (int x = 0; x < newWidth; x++)
            {
                int sourceX = x < rightEdge ? x : x - added;
                if ((row[sourceX / 8] & (1 << (sourceX % 8))) != 0)
                    expanded[x / 8] |= (byte)(1 << (x % 8));
            }
            output.Write(expanded);
        }
    }
}
