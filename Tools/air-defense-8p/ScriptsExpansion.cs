using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dreamness.Ra3.Map.Parser.Core.Map;
using Dreamness.Ra3.Map.Parser.Asset.Impl.Script;

public static class ScriptsExpansion
{
    static readonly string[] ObjectPrefixes = { "CMCV", "Shield", "YaoGuang", "Gunship", "天灯P", "BK", "Explosiona", "Explosionb", "Explosionc", "Century", "RocketAngel", "Crono", "Twin", "Overlord", "TK", "Cake", "outpost", "K", "L", "Carrier", "JavelinIFV", "ATech", "盟基地", "帝基地", "质子炮弹", "质子之力", "质子", "Player", "ShieldP", "一键进驻P", "盟军补贴P", "盟高科热点绑定P", "盟帝额外经济P" };
    static int addedScripts, addedActions, editedLua;
    public static string MapToken(string text, int player)
    {
        var result=text.Replace("Player_6", "Player_"+player).Replace("teamPlayer_6", "teamPlayer_"+player);
        foreach(var prefix in ObjectPrefixes.OrderByDescending(x=>x.Length))
            result=Regex.Replace(result, Regex.Escape(prefix)+"6(?![0-9])", prefix+player);
        result=Regex.Replace(result, @"(?<![0-9])P6(?![0-9])", "P"+player);
        result=result.Replace("Player6Zone", "Player"+player+"Zone");
        if(result.EndsWith(" 1 1 1 1 1") && !result.EndsWith(" 1 1 1 1 1 1"))result+=string.Concat(Enumerable.Repeat(" 1",player-6));
        return result;
    }
    static JsonNode Remap(JsonNode source,int player)
    {
        var node=source.DeepClone(); TransformStrings(node,s=>MapToken(s,player)); return node;
    }
    static void TransformStrings(JsonNode node,Func<string,string> change)
    {
        if(node is JsonObject o)foreach(var k in o.Select(x=>x.Key).ToArray())
        {
            if(o[k] is JsonValue v && v.TryGetValue<string>(out var s))o[k]=change(s);
            else if(o[k]!=null)TransformStrings(o[k]!,change);
        }
        else if(node is JsonArray a)for(int i=0;i<a.Count;i++)
        {
            if(a[i] is JsonValue v && v.TryGetValue<string>(out var s))a[i]=change(s);
            else if(a[i]!=null)TransformStrings(a[i]!,change);
        }
    }
    static bool IsLaneSixScript(string name)=>Regex.IsMatch(name,@"(?:P6(?:后援)?|Player6失败|基洛夫自爆[abc]6|航母组合6|摇光6|质子爆炸6)$");
    static bool HasLaneSixArg(JsonNode action)
    {
        if(action["Arguments"] is not JsonArray args)return false;
        foreach(var arg in args)if(arg is JsonValue v && v.TryGetValue<string>(out var s) && !s.Contains("#!ra3lua") && MapToken(s,7)!=s)return true;
        return false;
    }
    static string ExpandLua(string code)
    {
        var original=code;
        code=Regex.Replace(code,@"for\s+i\s*=\s*1\s*,\s*6\s*,\s*1\s+do", "for i = 1,8,1 do");
        code=code.Replace("elseif x >= 6000 then", "elseif x >= 7960 then").Replace("tempX = 5950", "tempX = 7910");
        code=code.Replace("OremineRestore(\"Oremine\",12)", "OremineRestore(\"Oremine\",16)");
        // Duplicate complete paratroop blocks; duplicating individual statements would change their conditional scope.
        var blockPattern=@"(?m)^[ \t]*if EvaluateCondition\(""NAMED_NOT_DESTROYED"",bossName\.\.""6""\) then\r?\n.*?^[ \t]*end";
        var blocks=new List<string>();
        code=Regex.Replace(code,blockPattern,m=>{blocks.Add(m.Value);return "-- EXPAND_BLOCK_"+(blocks.Count-1)+" --";},RegexOptions.Singleline);
        var lines=Regex.Split(code,@"\r?\n");var output=new List<string>();
        foreach(var line in lines)
        {
            output.Add(line);
            bool expand=line.Contains("teamName..\" 1 1 1 1 1\"") || line.Contains("bossName..\"6\"") || line.Contains("bossName..\"6_1\"") || line.Contains("FindPercentage(\"Cake6\"") || line.Contains("CarrierInitial(\"Carrier6_\"");
            if(expand)for(int p=7;p<=8;p++)
            {
                var s=MapToken(line,p).Replace("teamName..\" 1 1 1 1 1\"", "teamName..\""+string.Concat(Enumerable.Repeat(" 1",p-1))+"\"").Replace("bossName..\"6\"", "bossName..\""+p+"\"").Replace("bossName..\"6_1\"", "bossName..\""+p+"_1\"").Replace("waypoint..\"6\"", "waypoint..\""+p+"\"");
                output.Add(s);
            }
        }
        code=string.Join("\r\n",output);
        for(int i=0;i<blocks.Count;i++)
        {
            var block=blocks[i];var parts=new List<string>{block};
            for(int p=7;p<=8;p++)parts.Add(MapToken(block,p).Replace("teamName..\" 1 1 1 1 1\"", "teamName..\""+string.Concat(Enumerable.Repeat(" 1",p-1))+"\"").Replace("bossName..\"6\"", "bossName..\""+p+"\""));
            code=code.Replace("-- EXPAND_BLOCK_"+i+" --",string.Join("\r\n\r\n",parts));
        }
        if(code!=original)editedLua++;
        return code;
    }
    static void ExpandSharedScript(JsonNode script)
    {
        foreach(var section in new[]{"Then","Else"})if(script[section] is JsonArray actions)
        {
            // Keep each bonus reinforcement's create/hunt/power references on its spawn lane.
            var teamAliases=new Dictionary<string,string>();
            foreach(var a in actions)
            {
                if(a?["Name"]?.GetValue<string>()=="CREATE_REINFORCEMENT_TEAM" && a["Arguments"] is JsonArray spawnArgs && spawnArgs.Count>=2)
                {
                    var team=spawnArgs[0]?.ToString()??"";var wp=spawnArgs[1]?.ToString()??"";
                    if(team.StartsWith("bonusK10重明") && Regex.IsMatch(wp,@"^K[1-6]$"))teamAliases[team]="bonusK10重明"+string.Concat(Enumerable.Repeat(" 1",int.Parse(wp[1..])-1));
                }
                if(a?["Arguments"] is JsonArray refs)for(int i=0;i<refs.Count;i++)
                    if(refs[i] is JsonValue val && val.TryGetValue<string>(out var token) && teamAliases.TryGetValue(token,out var replacement))refs[i]=replacement;
            }
            var original=actions.ToArray();
            foreach(var action in original)
            {
                if(action?["Name"]?.ToString()=="ENABLE_SCRIPT" && action["Arguments"]?[0]?.ToString()=="超时空磁弩__6")
                {var extra=action.DeepClone();extra["Arguments"]![0]="超时空磁弩__7";actions.Add(extra);addedActions++;}
                if(HasLaneSixArg(action!))for(int p=7;p<=8;p++){actions.Add(Remap(action!,p));addedActions++;}
                if(action?["Arguments"] is JsonArray args)for(int i=0;i<args.Count;i++)if(args[i] is JsonValue v && v.TryGetValue<string>(out var s)&&s.Contains("#!ra3lua"))args[i]=ExpandLua(s);
            }
        }
        if(script["If"] is JsonArray groups)foreach(var group in groups.OfType<JsonArray>())foreach(var cond in group.ToArray())if(HasLaneSixArg(cond!))for(int p=7;p<=8;p++)group.Add(Remap(cond!,p));
    }
    static void ExpandContent(JsonArray content,bool personal=false)
    {
        foreach(var node in content.ToArray())
        {
            if(node?["Type"]?.ToString()=="Folder")ExpandContent((JsonArray)node["Content"]!,personal);
            else if(node!=null)
            {
                var name=node["Name"]?.ToString()??"";
                if(!personal && IsLaneSixScript(name))for(int p=7;p<=8;p++){var clone=Remap(node,p);clone["Name"]=Regex.Replace(MapToken(name,p),"6$",p.ToString());content.Add(clone);addedScripts++;}
                else if(!personal && !name.StartsWith("超时空磁弩"))ExpandSharedScript(node);
                // Existing seventh summon had its power copied from lane six; keep each summon on its own team.
                if(name.StartsWith("超时空磁弩") && node["Then"] is JsonArray summonActions)
                {
                    var spawn=summonActions.FirstOrDefault(a=>a?["Name"]?.ToString()=="CREATE_REINFORCEMENT_TEAM");
                    var team=spawn?["Arguments"]?[0]?.ToString();
                    if(team!=null)foreach(var action in summonActions)
                        if(action?["Arguments"] is JsonArray args)for(int i=0;i<args.Count;i++)
                            if(args[i]?.ToString().StartsWith("FinalBoss磁弩")==true)args[i]=team;
                }
                // Skill3 already has seven timed summons; add lane eight at the end of the same countdown.
                if(name=="超时空磁弩__6")
                {
                    var eighth=node.DeepClone();eighth["Name"]="超时空磁弩__7";
                    TransformStrings(eighth,s=>s.Replace("Crono7","Crono8").Replace("FinalBoss磁弩 1 1 1 1 1 1", "FinalBoss磁弩 1 1 1 1 1 1 1"));
                    eighth["If"]![0]![0]!["Arguments"]![2]=0;
                    content.Add(eighth);addedScripts++;
                }
            }
        }
    }
    // The readable library JSON omits Script.Unknown and secondary argument channels.
    // Retain those wire fields explicitly through the JSON edits, including cloned actions.
    static void WireArgs(IEnumerable<ScriptArgument> args,JsonNode node,bool restore)
    {
        if(!restore)
        {
            var wire=new JsonArray();foreach(var a in args)wire.Add(new JsonArray(a.IntValue,a.FloatValue,a.StringValue));node["_wireArgs"]=wire;
        }
        else if(node["_wireArgs"] is JsonArray wire)
        {
            int i=0;foreach(var a in args)
            {
                var data=wire[i++]!;var real=a.ArgumentModel.RealType;
                if(real!="Int32")a.IntValue=data[0]!.GetValue<int>();
                if(real!="Double")a.FloatValue=data[1]!.GetValue<float>();
                if(real!="String")a.StringValue=data[2]!.GetValue<string>();
            }
        }
    }
    static void WireScript(Script script,JsonNode node,bool restore)
    {
        if(restore)script.Unknown=node["_wireUnknown"]?.ToString()??"X";
        else node["_wireUnknown"]=script.Unknown;
        for(int i=0;i<script.ScriptActionOnTrue.Count;i++)WireArgs(script.ScriptActionOnTrue[i].Arguments,node["Then"]![i]!,restore);
        for(int i=0;i<script.ScriptActionOnFalse.Count;i++)WireArgs(script.ScriptActionOnFalse[i].Arguments,node["Else"]![i]!,restore);
        for(int i=0;i<script.ScriptOrConditions.Count;i++)for(int j=0;j<script.ScriptOrConditions[i].Conditions.Count;j++)WireArgs(script.ScriptOrConditions[i].Conditions[j].Arguments,node["If"]![i]![j]!,restore);
    }
    static void WireContent(IEnumerable<Script> scripts,IEnumerable<ScriptGroup> groups,JsonArray nodes,bool restore)
    {
        int si=0,gi=0;var sa=scripts.ToArray();var ga=groups.ToArray();
        foreach(var node in nodes)
        {
            if(node!["Type"]!.ToString()=="Folder"){var g=ga[gi++];WireContent(g.Scripts,g.ScriptGroups,(JsonArray)node["Content"]!,restore);}
            else WireScript(sa[si++],node,restore);
        }
    }
    static void WireLists(PlayerScriptsList lists,JsonArray nodes,bool restore)
    {
        for(int i=0;i<lists.ScriptLists.Count;i++)WireContent(lists.ScriptLists[i].Scripts,lists.ScriptLists[i].ScriptGroups,(JsonArray)nodes[i]!["Content"]!,restore);
    }
    public static string Expand(Ra3Map map,string? auditPath=null)
    {
        addedScripts=addedActions=editedLua=0;
        // Sides must already contain P7/P8. Original script list player order still matches first 17 slots.
        var root=JsonNode.Parse(map.Context.ExportPlayerScriptsListToJson())!.AsArray();
        WireLists(map.Context.PlayerScriptsListAsset,root,false);
        // If sides were inserted before this export, labels for the old AI lists are shifted. Restore their original names.
        var originalNames=new[]{"","PlyrCivilian","SkirmishNull","SkirmishObserver","SkirmishJapan","SkirmishCommentator","SkirmishAllies","SkirmishNeutral","SkirmishCivilian","SkirmishRandom","SkirmishSoviet","Player_1","Player_2","Player_3","Player_4","Player_5","Player_6","PlyrCreeps","PlyrAllies","PlyrNeutral"};
        if(root.Count!=20)throw new InvalidOperationException("Expected original 20 script lists; do not pre-insert empty P7/P8 lists.");
        for(int i=0;i<20;i++)root[i]!["PlayerName"]=originalNames[i];
        foreach(var list in root)ExpandContent((JsonArray)list!["Content"]!,Regex.IsMatch(list["PlayerName"]!.ToString(),@"^Player_[1-6]$"));
        var p6=root.Single(x=>x!["PlayerName"]!.ToString()=="Player_6")!;
        for(int p=7;p<=8;p++){var copy=Remap(p6,p);copy["PlayerName"]="Player_"+p;root.Insert(16+p-6,copy);addedScripts+=((JsonArray)copy["Content"]!).Count;}
        var json=root.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default){WriteIndented=true});
        map.Context.ImportPlayerScriptsListFromJson(json);
        var orderedRoot=new JsonArray();
        foreach(var player in map.Context.SideListAsset.PlayerDataList)orderedRoot.Add(root.Single(x=>x!["PlayerName"]!.ToString()==player.Name)!.DeepClone());
        WireLists(map.Context.PlayerScriptsListAsset,orderedRoot,true);
        if(auditPath!=null)File.WriteAllText(auditPath,json);
        return $"Scripts: +{addedScripts} per-player/lane scripts, +{addedActions} shared actions, {editedLua} Lua blocks extended.";
    }
}
