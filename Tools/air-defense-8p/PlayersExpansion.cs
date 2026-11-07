using System.Text.Json;
using Dreamness.Ra3.Map.Parser.Core.Map;
using Dreamness.Ra3.Map.Parser.Asset.Impl.Player;
using Dreamness.Ra3.Map.Parser.Asset.Impl.Team;

public static class PlayersExpansion
{
    public sealed record Result(int PlayersBefore,int PlayersAfter,int TeamsBefore,int TeamsAfter,int AddedTeams);
    public static Result Apply(Ra3Map map)
    {
        var c=map.Context;
        var sides=c.SideListAsset.PlayerDataList;
        var originalPlayers=sides.ToArray();
        int sourceIndex=Array.FindIndex(originalPlayers,p=>p.Name=="Player_6");
        if(sourceIndex<0 || originalPlayers.Any(p=>p.Name=="Player_7"||p.Name=="Player_8"))throw new InvalidDataException("Expected six original player slots.");
        var newPlayers=new List<PlayerData>(originalPlayers.Take(sourceIndex+1));
        for(int i=7;i<=8;i++)
        {
            using var br=new BinaryReader(new MemoryStream(originalPlayers[sourceIndex].ToBytes(c)));
            var clone=PlayerData.FromBinaryReader(br,c);
            foreach(var prop in clone.Properties.PropertiesDict.Values)
                if(prop.Value is string s && s.Contains("Player_6")) prop.Value=s.Replace("Player_6",$"Player_{i}");
            newPlayers.Add(clone);
        }
        newPlayers.AddRange(originalPlayers.Skip(sourceIndex+1));
        sides.Clear();foreach(var p in newPlayers)sides.Add(p);
        var maps=new Dictionary<string,Dictionary<string,string>> { ["7"]=ObjectsExpansion.CreateNameMap(7),["8"]=ObjectsExpansion.CreateNameMap(8) };
        var teams=c.TeamsAsset.TeamList;
        var originals=teams.ToArray();
        const string suffix=" 1 1 1 1 1";
        foreach(var source in originals.Where(t=>t.FullName.Contains("Player_6") || (t.FullName.EndsWith(suffix)&&!t.FullName.EndsWith(suffix+" 1"))))
        {
            for(int i=7;i<=8;i++)
            {
                using var br=new BinaryReader(new MemoryStream(source.ToBytes(c)));
                var clone=TeamAsset.FromBinaryReader(br,c);
                foreach(var prop in clone.Properties.PropertiesDict.Values)
                {
                    if(prop.Value is not string text)continue;
                    string mapped=text;
                    if(text.Contains("Player_6"))mapped=text.Replace("Player_6",$"Player_{i}");
                    else if(text.EndsWith(suffix))mapped=text+string.Concat(Enumerable.Repeat(" 1",i-6));
                    else if(maps[i.ToString()].TryGetValue(text,out var named))mapped=named;
                    if(mapped!=text)prop.Value=mapped;
                }
                if(teams.Any(t=>t.FullName==clone.FullName))continue;
                teams.Add(clone);
            }
        }
        return new(originalPlayers.Length,sides.Count,originals.Length,teams.Count,teams.Count-originals.Length);
    }
}
