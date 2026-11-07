using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Dreamness.Ra3.Map.Parser.Asset.Base;
using Dreamness.Ra3.Map.Parser.Asset.Impl.Default;
using Dreamness.Ra3.Map.Parser.Core.Map;

/// <summary>Extends the original six-player metadata while retaining unknown record fields.</summary>
public static class MetadataExpansion
{
    public sealed record Point(float X, float Y);
    public sealed record Area(string Name, ushort Flags, uint Id, Point[] Points, uint Tail);
    public sealed record HotSpot(string Name, string Title, string Description);
    public sealed record Result(int TriggerAreaCount, int MultiplayerPositionCount, int HotSpotCount,
        uint Player7AreaId, uint Player8AreaId, float DeathZoneMinX, float DeathZoneMaxX);

    public static Result Expand(Ra3Map map, float lanePitch = 980f) => Expand(map.Context, lanePitch);

    public static Result Expand(MapContext context, float lanePitch = 980f)
    {
        var areaAsset = context.AssetDict["TriggerAreas"];
        var areas = ReadAreas(Payload(areaAsset, context));
        if (areas.Count != 7 || areas.Any(a => a.Name is "Player7Zone" or "Player8Zone"))
            throw new InvalidDataException("Expected the original six player zones and DeathZone.");
        var lastArea = areas.Single(a => a.Name == "Player6Zone");
        uint nextId = checked(areas.Max(a => a.Id) + 1);
        var area7 = lastArea with {Name = "Player7Zone", Id = nextId,
            Points = lastArea.Points.Select(p => p with {X = p.X + lanePitch}).ToArray()};
        var area8 = lastArea with {Name = "Player8Zone", Id = checked(nextId + 1),
            Points = lastArea.Points.Select(p => p with {X = p.X + lanePitch * 2}).ToArray()};
        int deathIndex = areas.FindIndex(a => a.Name == "DeathZone");
        if (deathIndex < 0) throw new InvalidDataException("DeathZone is missing.");
        areas[deathIndex] = areas[deathIndex] with {
            Points = areas[deathIndex].Points.Select(p => p with {X = p.X + lanePitch * 2}).ToArray()};
        areas.Add(area7);
        areas.Add(area8);

        var positionAsset = context.AssetDict["MPPositionList"];
        byte[] positions = Payload(positionAsset, context);
        var positionRecords = ReadPositionRecords(positions);
        if (positionRecords.Count != 6) throw new InvalidDataException("Expected six multiplayer-position records.");
        using var expandedPositions = new MemoryStream();
        expandedPositions.Write(positions);
        expandedPositions.Write(positionRecords[^1]);
        expandedPositions.Write(positionRecords[^1]);

        var hotSpotAsset = context.AssetDict["MissionHotSpots"];
        var hotSpots = ReadHotSpots(Payload(hotSpotAsset, context));
        if (hotSpots.Count != 6) throw new InvalidDataException("Expected six mission hot spots.");
        var lastHotSpot = hotSpots.Single(h => h.Name == "JavelinIFV6");
        hotSpots.Add(lastHotSpot with {Name = "JavelinIFV7"});
        hotSpots.Add(lastHotSpot with {Name = "JavelinIFV8"});

        context.OverrideAsset(Replacement(areaAsset, WriteAreas(areas)));
        context.OverrideAsset(Replacement(positionAsset, expandedPositions.ToArray()));
        context.OverrideAsset(Replacement(hotSpotAsset, WriteHotSpots(hotSpots)));
        return Validate(context, lanePitch);
    }

    public static Result Validate(MapContext context, float lanePitch = 980f)
    {
        var areas = ReadAreas(Payload(context.AssetDict["TriggerAreas"], context));
        var positions = ReadPositionRecords(Payload(context.AssetDict["MPPositionList"], context));
        var hotSpots = ReadHotSpots(Payload(context.AssetDict["MissionHotSpots"], context));
        if (areas.Count != 9 || positions.Count != 8 || hotSpots.Count != 8)
            throw new InvalidDataException("Expanded metadata record counts differ from 9 areas, 8 positions and 8 hot spots.");
        if (areas.Select(a => a.Id).Distinct().Count() != areas.Count ||
            areas.Select(a => a.Name).Distinct().Count() != areas.Count)
            throw new InvalidDataException("Trigger-area names or IDs are duplicated.");
        for (int player = 1; player <= 8; player++)
        {
            if (areas.Count(a => a.Name == $"Player{player}Zone") != 1 ||
                hotSpots.Count(h => h.Name == $"JavelinIFV{player}") != 1)
                throw new InvalidDataException($"Player {player} is missing a zone or hot spot.");
        }
        var area6 = areas.Single(a => a.Name == "Player6Zone");
        var area7 = areas.Single(a => a.Name == "Player7Zone");
        var area8 = areas.Single(a => a.Name == "Player8Zone");
        foreach (var pair in new[] {(Area: area7, Offset: lanePitch), (Area: area8, Offset: lanePitch * 2)})
        {
            if (pair.Area.Points.Length != area6.Points.Length || pair.Area.Flags != area6.Flags || pair.Area.Tail != area6.Tail)
                throw new InvalidDataException("New trigger-area record does not retain the template fields.");
            for (int i = 0; i < area6.Points.Length; i++)
                if (pair.Area.Points[i].X != area6.Points[i].X + pair.Offset || pair.Area.Points[i].Y != area6.Points[i].Y)
                    throw new InvalidDataException("New trigger-area polygon is not an exact translated copy.");
        }
        if (!positions[5].SequenceEqual(positions[6]) || !positions[5].SequenceEqual(positions[7]))
            throw new InvalidDataException("Added multiplayer-position records differ from the original template.");
        var death = areas.Single(a => a.Name == "DeathZone");
        return new Result(areas.Count, positions.Count, hotSpots.Count, area7.Id, area8.Id,
            death.Points.Min(p => p.X), death.Points.Max(p => p.X));
    }

    public static void ExportPreviewData(MapContext context, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var height = context.HeightMapDataAsset;
        var tile = context.BlendTileDataAsset;
        using var textureWriter = new BinaryWriter(File.Create(Path.Combine(outputDirectory, "textures.u8")));
        using var elevationWriter = new BinaryWriter(File.Create(Path.Combine(outputDirectory, "elevations.f32")));
        for (int y = 0; y < height.MapHeight; y++)
        for (int x = 0; x < height.MapWidth; x++)
        {
            textureWriter.Write(checked((byte)(tile.Tiles[x,y] / 64)));
            elevationWriter.Write(height.Elevations[x,y]);
        }
        File.WriteAllText(Path.Combine(outputDirectory, "terrain.json"), JsonSerializer.Serialize(new {
            width = height.MapWidth, height = height.MapHeight, border = height.BorderWidth,
            order = "row-major y=0 bottom edge", elevationFormat = "little-endian float32",
            textures = tile.Textures.Select(t => t.Name).ToArray()
        }, new JsonSerializerOptions {WriteIndented = true}));
    }

    public static List<Area> ReadAreas(byte[] payload)
    {
        using var reader = new BinaryReader(new MemoryStream(payload));
        uint count = reader.ReadUInt32();
        if (count > 10000) throw new InvalidDataException("Implausible trigger-area count.");
        var result = new List<Area>();
        for (int i = 0; i < count; i++)
        {
            string name = ReadString(reader);
            ushort flags = reader.ReadUInt16();
            uint id = reader.ReadUInt32();
            uint pointCount = reader.ReadUInt32();
            if (pointCount > 10000) throw new InvalidDataException("Implausible polygon size.");
            var points = new Point[pointCount];
            for (int p = 0; p < pointCount; p++) points[p] = new Point(reader.ReadSingle(), reader.ReadSingle());
            result.Add(new Area(name, flags, id, points, reader.ReadUInt32()));
        }
        End(reader);
        return result;
    }

    public static List<HotSpot> ReadHotSpots(byte[] payload)
    {
        using var reader = new BinaryReader(new MemoryStream(payload));
        uint count = reader.ReadUInt32();
        if (count > 10000) throw new InvalidDataException("Implausible hot-spot count.");
        var result = new List<HotSpot>();
        for (int i = 0; i < count; i++) result.Add(new HotSpot(ReadString(reader), ReadString(reader), ReadString(reader)));
        End(reader);
        return result;
    }

    public static List<byte[]> ReadPositionRecords(byte[] payload)
    {
        using var reader = new BinaryReader(new MemoryStream(payload));
        var records = new List<byte[]>();
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            long start = reader.BaseStream.Position;
            reader.ReadInt32(); reader.ReadInt16();
            int length = reader.ReadInt32();
            if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid multiplayer-position record length.");
            reader.BaseStream.Position += length;
            records.Add(payload.AsSpan((int)start, length + 10).ToArray());
        }
        return records;
    }

    private static byte[] WriteAreas(List<Area> areas)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(areas.Count);
        foreach (var area in areas)
        {
            WriteString(writer, area.Name); writer.Write(area.Flags); writer.Write(area.Id); writer.Write(area.Points.Length);
            foreach (var point in area.Points) {writer.Write(point.X); writer.Write(point.Y);}
            writer.Write(area.Tail);
        }
        return stream.ToArray();
    }

    private static byte[] WriteHotSpots(List<HotSpot> hotSpots)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(hotSpots.Count);
        foreach (var hotSpot in hotSpots)
        {WriteString(writer, hotSpot.Name); WriteString(writer, hotSpot.Title); WriteString(writer, hotSpot.Description);}
        return stream.ToArray();
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        byte[] value = reader.ReadBytes(length);
        if (value.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(value);
    }
    private static void WriteString(BinaryWriter writer, string value)
    {byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(checked((ushort)bytes.Length)); writer.Write(bytes);}
    private static void End(BinaryReader reader)
    {if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Metadata has an unparsed suffix.");}
    private static byte[] Payload(BaseAsset asset, MapContext context) => asset.ToBytes(context).AsSpan(10).ToArray();
    private static DefaultAsset Replacement(BaseAsset source, byte[] payload) => new DefaultAsset {
        Id = source.Id, Version = source.Version, AssetType = source.AssetType, DataSize = payload.Length,
        Data = payload, Parsed = true
    };
}
