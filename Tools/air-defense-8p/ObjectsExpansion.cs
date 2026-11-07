using Dreamness.Ra3.Map.Parser.Core.Map;
using Dreamness.Ra3.Map.Parser.Asset.Impl.GameObject;
using Dreamness.Ra3.Map.Parser.Asset.Util;
using Dreamness.Ra3.Map.Parser.Util;

// Map-specific extension: preserve the six original lanes and add two copies of lane six.
public static class ObjectsExpansion
{
    private static readonly string[] NamedTemplates = {
        "CMCV6Plus", "Shield6Effect", "YaoGuang6", "Gunship6", "天灯P6", "BK6",
        "Explosiona6", "Explosionb6", "Explosionc6", "Century6", "CMCV6", "RocketAngel6",
        "Crono6", "Twin6", "Overlord6", "TK6", "Cake6", "outpost6", "K6", "L6",
        "Player_6_Start", "Carrier6_1", "Carrier6_2", "Carrier6_3", "Carrier6_4",
        "Carrier6_5", "Carrier6_6", "Carrier6_7", "Oremine6", "Oremine12"
    };

    public static Dictionary<string, string> CreateNameMap(int player)
    {
        if (player is not (7 or 8)) throw new ArgumentOutOfRangeException(nameof(player));
        return NamedTemplates.ToDictionary(name => name, name => name switch
        {
            // 7..12 are already used by the six lower ore nodes in the original map.
            "Oremine6" => "Oremine" + (player + 6),
            "Oremine12" => "Oremine" + (player + 8),
            _ when name.StartsWith("Carrier6_") => "Carrier" + player + name.Substring("Carrier6".Length),
            _ => name.Replace("6", player.ToString())
        });
    }

    public static Dictionary<string, string> CreateOwnerMap(int player) => new()
    {
        ["Player_6/teamPlayer_6"] = $"Player_{player}/teamPlayer_{player}",
        ["PlyrCreeps/FinalBoss以逸待劳" + string.Concat(Enumerable.Repeat(" 1", 5))] =
            "PlyrCreeps/FinalBoss以逸待劳" + string.Concat(Enumerable.Repeat(" 1", player - 1))
    };

    public record Change(int Player, string SourceName, string NewName, string Type,
        float X, float Y, string Owner, bool IsWaypoint);
    public record Result(List<Change> Added, List<string> Moved);

    public static Result Apply(MapContext context)
    {
        var objects = context.ObjectsListAsset;
        var originals = objects.MapObjectList.ToArray();
        string Name(ObjectAsset o) => o.Properties.GetProperty<string>(o.IsWaypoint ? "waypointName" : "objectName") ?? "";
        var indexed = originals.Where(o => Name(o).Length > 0).ToDictionary(Name);
        var added = new List<Change>();
        var moved = new List<string>();
        var idSet = originals.Select(o => o.UniqueId).ToHashSet();

        ObjectAsset Clone(ObjectAsset source, int player, string newName)
        {
            // Reparse its serialized object to retain property wire types and every unknown property.
            using var stream = new MemoryStream(source.ToBytes(context));
            using var reader = new BinaryReader(stream);
            var clone = (ObjectAsset)AssetParser.FromBinaryReader(reader, context);
            if (clone.Errored) throw new InvalidDataException("Could not clone " + Name(source));
            var owners = CreateOwnerMap(player);
            var oldOwner = clone.Properties.GetProperty<string>("originalOwner") ?? "";
            var newOwner = owners.GetValueOrDefault(oldOwner, oldOwner);
            clone.Properties.PutProperty("originalOwner", newOwner);
            if (newName.Length > 0)
                clone.Properties.PutProperty(clone.IsWaypoint ? "waypointName" : "objectName", newName);
            var uniqueId = clone.IsWaypoint ? newName : source.UniqueId + " [8P lane " + player + "]";
            if (!idSet.Add(uniqueId)) throw new InvalidDataException("Duplicate expanded unique ID " + uniqueId);
            clone.Properties.PutProperty("uniqueID", uniqueId);
            var p = source.Position;
            float dx;
            if (source.IsWaypoint || (p.Y >= 0 && p.Y <= 6000))
            {
                dx = (player - 6) * 980f;
            }
            else if (p.Y < 0)
            {
                // Cake is a hidden ownership detector in the southern border, not a player base.
                dx = (player - 6) * 100f;
            }
            else
            {
                // Keep sleeping enemy/bonus templates in the northern hidden border. Moving
                // the whole lane-six cluster together also preserves garrison loading distances.
                dx = 4000f + (player - 7) * 800f;
            }
            clone.Position = new Vec3D(p.X + dx, p.Y, p.Z);
            objects.Add(clone, autoId: true);
            added.Add(new Change(player, Name(source), newName, clone.TypeName,
                clone.Position.X, clone.Position.Y, newOwner, clone.IsWaypoint));
            return clone;
        }

        foreach (int player in new[] { 7, 8 })
        {
            var names = CreateNameMap(player);
            foreach (var pair in names)
            {
                if (!indexed.TryGetValue(pair.Key, out var source))
                    throw new InvalidDataException("Missing original object " + pair.Key);
                if (indexed.ContainsKey(pair.Value))
                    throw new InvalidDataException("Expanded object name already exists: " + pair.Value);
                Clone(source, player, pair.Value);
            }
            foreach (var source in originals.Where(o =>
                         o.Properties.GetProperty<string>("originalOwner") == "Player_6/teamPlayer_6" &&
                         Name(o).Length == 0))
                Clone(source, player, "");

            // Three Tanya and five advanced infantry are physically parked beside the
            // lane-six transports and loaded by the existing scripts. Keep those cargo units.
            var commandos = originals.Where(o => o.TypeName == "SovietCommandoTech1" &&
                Name(o).Length == 0 && o.Position.Y > 6630f).ToArray();
            var infantry = originals.Where(o => o.TypeName == "CelestialAntiInfantryInfantryAdvanced" &&
                Name(o).Length == 0 && o.Position.Y > 6630f).ToArray();
            if (commandos.Length != 3 || infantry.Length != 5)
                throw new InvalidDataException("Unexpected lane-six transport cargo layout.");
            foreach (var source in commandos.Concat(infantry)) Clone(source, player, "");
        }

        // Both are endpoints of geometry spanning the full map width. The central road
        // and every other original position remain fixed.
        foreach (var source in originals.Where(o =>
                     (o.IsRoad && o.Position.X > 6700f) || Name(o) == "Waypoint 50"))
        {
            var p = source.Position;
            source.Position = new Vec3D(p.X + 1960f, p.Y, p.Z);
            moved.Add(Name(source).Length > 0 ? Name(source) : source.UniqueId);
        }
        objects.MarkModified();
        return new Result(added, moved);
    }
}
