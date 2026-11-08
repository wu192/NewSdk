using System;
using BinaryAssetBuilder.Core;
partial class GameObjectOracle {
    // Explicit extension projection for the historical original-DLL oracle.
    // Reject any extra change: only a default capacity word and its exact
    // token may be removed; all token/data pointer adjustments are derived
    // from those removed byte ranges and the existing relocation tables.
#if QUEUEDOCK_LEGACY_ONLY
    static uint Word(byte[] b,int at,bool big) {return big?((uint)b[at]<<24)|((uint)b[at+1]<<16)|((uint)b[at+2]<<8)|b[at+3]:BitConverter.ToUInt32(b,at);}
#endif
    internal static AssetBuffer QueueDockLegacyView(AssetBuffer value,bool big) {
        var b=value.InstanceData;int root=(int)Word(b,8,big)-4;
        var removed=new System.Collections.Generic.List<int[]>();
        for(int at=0;at<root;at+=12) {
            if(Word(b,at,big)!=2 || Word(b,at+4,big)!=0xAF4C1F21u)continue;
            int table=(int)Word(b,at+8,big),extra=table+84;
            // Original-only transcript generation has no extension.
            if(Word(b,extra,big)==0)continue;
            int payload=(int)Word(b,extra+8,big);
            if(Word(b,extra,big)!=983236950u || Word(b,extra+4,big)!=503583685u || Word(b,payload,big)!=1 || Word(b,payload-52,big)!=0xAF4C1F21u || Word(b,extra+12,big)!=0)
                throw new Exception("Unexpected QueueDock extension in legacy comparison");
            removed.Add(new[]{extra,12});removed.Add(new[]{payload,4});
        }
        if(removed.Count==0)return value;
        removed.Sort((a,c)=>a[0].CompareTo(c[0]));
        Func<int,bool> erased=at=>{foreach(var range in removed)if(at>=range[0] && at<range[0]+range[1])return true;return false;};
        Func<int,int> position=at=>{int shift=0;foreach(var range in removed)if(range[0]<at)shift+=range[1];return at-shift;};
        int length=b.Length;foreach(var range in removed)length-=range[1];
        var projected=new byte[length];for(int at=0,destination=0;at<b.Length;++at)if(!erased(at))projected[destination++]=b[at];
        Action<byte[],int,uint> write=(bytes,at,word)=>{for(int i=0;i<4;++i)bytes[at+i]=(byte)(word>>((big?3-i:i)*8));};
        for(int at=0;at<root;at+=12)if(!erased(at))write(projected,position(at+8),(uint)position((int)Word(b,at+8,big)));
        var relocations=(byte[])value.RelocationData.Clone();
        for(int at=0;at+4<=relocations.Length;at+=4) {
            uint source=Word(value.RelocationData,at,big);if(source==uint.MaxValue)continue;
            int sourceAt=(int)source;
            if(erased(sourceAt))throw new Exception("QueueDock capacity gained a pointer relocation");
            write(relocations,at,(uint)position(sourceAt));
            write(projected,position(sourceAt),(uint)position((int)Word(b,sourceAt,big)));
        }
        var imports=(byte[])value.ImportsData.Clone();
        for(int at=0;at+4<=imports.Length;at+=4) {uint source=Word(imports,at,big);if(source!=uint.MaxValue)write(imports,at,(uint)position((int)source));}
        return new AssetBuffer{InstanceData=projected,RelocationData=relocations,ImportsData=imports};
    }
#if !QUEUEDOCK_LEGACY_ONLY
    // Exact original methods and dependency closure: queue-parking-source/manifest.json.
    static string[] QueueParkingTypes={"2941001505","3921409083"};
    static string[] QueueParkingStrings={"DockStartBoneName","DockActionBoneName","DockEndBoneName","QueueBonePrefix","TurnTableBoneName"};
    static Field[] QueueParkingFields={new Field("NumRows","Signed"),new Field("NumCols","Signed"),new Field("ApproachHeight","Float"),new Field("ParkInHangars","Boolean"),new Field("DoorOpenTime","Time"),new Field("DoorCloseTime","Time"),new Field("LowersOntoParkingPlaceOnProduction","Boolean"),new Field("CanSetRallyPoint","Boolean"),new Field("DoorsUnpackOpen","Boolean"),new Field("QueueDistance","Float"),new Field("LimitedProductionAircraftSpots","Signed"),new Field("HelipadBoneName","String")};
    static string QueueParkingXML(int group,string attrs="",string children="") {return "<Behaviors><ObjectSMCHelper TypeId='"+QueueParkingTypes[group]+"' id='QueueParking' "+attrs+">"+children+"</ObjectSMCHelper></Behaviors>";}
    static string QueueParkingFilter="<CanParkHereFilter Include='ALL' Exclude='AIRCRAFT' StatusBitFlags='ALL'><IncludeThing>A</IncludeThing><ExcludeThing>B</ExcludeThing></CanParkHereFilter>";
    static void QueueParkingFamily(IAssetBuilderPlugin original,TargetPlatform p) {
        bool big=p!=TargetPlatform.Win32;int[] tokens={7,15};
        for(int group=0;group<2;++group) {
            byte[] b=Check(original,p,"",QueueParkingXML(group)).InstanceData;int at=LifecycleAt(b,big),table=MotionTable(b,big);bool extended=group==0 && Word(b,table+84,big)==983236950u;int nativeSize=extended?56:52;if(Word(b,at,big)!=uint.Parse(QueueParkingTypes[group]))throw new Exception("Independent Queue/Parking type");AIUpdateSentinel(b,table,extended?8:tokens[group]);
            uint[] parents=group==0?new[]{816016269u,2769121946u,3613234082u}:new[]{4043318533u,687874333u,2769121946u,3613234082u};
            foreach(uint parent in parents){if(Word(b,table,big)!=1 || Word(b,table+4,big)!=parent)throw new Exception("Independent Queue/Parking Token inheritance");table=(int)Word(b,table+8,big);}
            if(group==0){if(b.Length!=at+nativeSize+20)throw new Exception("Independent Queue body plus five padded empty strings");for(int i=0;i<5;++i)if(Word(b,at+8+8*i,big)!=0 || Word(b,at+12+8*i,big)!=at+nativeSize+4*i)throw new Exception("Independent Queue empty String lengths");if(Word(b,at+48,big)!=0 || (extended && Word(b,at+52,big)!=1))throw new Exception("Independent Queue animation/capacity defaults");}
            else if(Word(b,at+8,big)!=0 || Word(b,at+12,big)!=0 || Word(b,at+16,big)!=0 || Word(b,at+20,big)!=0 || Word(b,at+24,big)!=0 || Word(b,at+28,big)!=0x42c80000u || Word(b,at+32,big)!=0 || Word(b,at+36,big)!=10 || Word(b,at+44,big)!=0 || Word(b,at+48,big)!=0 || b[at+52]!=0 || b[at+53]!=0 || b[at+54]!=1 || b[at+55]!=1)throw new Exception("Independent Parking defaults/string/nullable Filter and vector/packed bools");
            foreach(var container in ModuleContainers)Check(original,p,"",ModuleXML(container[0],container[1],"TypeId='"+QueueParkingTypes[group]+"' id='QP'"));
            foreach(string bad in new[]{"Unknown='1'","xmlns='foreign'","xmlns:f='foreign' f:TypeId='1'"})Reject(p,"",QueueParkingXML(group,bad));foreach(string bad in new[]{"text","<Unknown/>","<Die/>","<CustomAnimAndDuration/>","<ObjectFilter/>"})Reject(p,"",QueueParkingXML(group,"",bad));
            foreach(int count in new[]{0,1,2,27,28,29,65}) {string modules="";for(int i=0;i<count;++i)modules+="<ObjectSMCHelper TypeId='"+QueueParkingTypes[group]+"' id='QP"+i+"'>"+(group==1?QueueParkingFilter+"<DefaultRallyPointOffset x='1' y='2' z='3'/>":"")+"</ObjectSMCHelper>";Check(original,p,"Browser='a bb'","<Behaviors>"+modules+"<FireWeaponUpdate TypeId='2995535186'><FireWeaponNugget WeaponName='W\\23'><Offset/></FireWeaponNugget></FireWeaponUpdate></Behaviors>");}
        }
        foreach(string f in QueueParkingStrings)foreach(string value in AIUpdateSamples(new Field(f,"String")))Check(original,p,"",QueueParkingXML(0,f+"='"+Escape(value)+"'"));
        foreach(string value in HealingModelValues)Check(original,p,"",QueueParkingXML(0,"DockingAnimation='"+Escape(value)+"'"));
        foreach(var f in QueueParkingFields)foreach(string value in AIUpdateSamples(f))Check(original,p,"",QueueParkingXML(1,f.Name+"='"+Escape(value)+"'"));
        foreach(string child in new[]{"CanParkHereFilter","DefaultRallyPointOffset"}) {
            Check(original,p,"",QueueParkingXML(1,"","<"+child+"/>"));Reject(p,"",QueueParkingXML(0,"","<"+child+"/>"));
            foreach(string bad in new[]{"Unknown='1'","xmlns='foreign'","xmlns:f='foreign' f:TypeId='1'"})Reject(p,"",QueueParkingXML(1,"","<"+child+" "+bad+"/>"));foreach(string bad in new[]{"text","<Unknown/>"})Reject(p,"",QueueParkingXML(1,"","<"+child+">"+bad+"</"+child+">"));Reject(p,"",QueueParkingXML(1,"","<"+child+"/><"+child+"/>"));
        }
        foreach(string f in new[]{"x","y","z"})foreach(string value in AIUpdateSamples(new Field(f,"Float")))Check(original,p,"",QueueParkingXML(1,"","<DefaultRallyPointOffset "+f+"='"+Escape(value)+"'/>"));
        foreach(string attrs in new[]{"","id='MiXeD'","Rule='ANY' Relationship='ALL' Alignment='EVIL' Include='ALL' Exclude='AIRCRAFT' StatusBitFlags='ALL' StatusBitFlagsExclude='DAMAGED'","StatusBitFlags='' StatusBitFlagsExclude=''"})Check(original,p,"",QueueParkingXML(1,"","<CanParkHereFilter "+attrs+"><IncludeThing>A</IncludeThing><ExcludeThing>B</ExcludeThing></CanParkHereFilter>"));
        foreach(int count in new[]{0,1,2,27,28,29,65}) {string rows="";for(int i=0;i<count;++i)rows+="<IncludeThing>A"+i+"</IncludeThing><ExcludeThing>B"+i+"</ExcludeThing>";Check(original,p,"",QueueParkingXML(1,"","<CanParkHereFilter Include='ALL' StatusBitFlags='ALL'>"+rows+"</CanParkHereFilter>"));}
        foreach(string leaf in new[]{"IncludeThing","ExcludeThing"})foreach(string bad in new[]{"Unknown='1'","xmlns='foreign'"})Reject(p,"",QueueParkingXML(1,"","<CanParkHereFilter><"+leaf+" "+bad+"/></CanParkHereFilter>"));
        foreach(string value in new[]{"UNKNOWN","","ALL"}) {byte[] b=Check(original,p,"",QueueParkingXML(0,"DockingAnimation='"+value+"'")).InstanceData;int at=LifecycleAt(b,big);if(Word(b,at+48,big)!=(value=="ALL"?454u:233u))throw new Exception("Independent Queue single model enum/unknown");}
        {byte[] b=Check(original,p,"",QueueParkingXML(1,"NumRows='-17' NumCols='010' ApproachHeight='12.5' DoorOpenTime='2s' DoorCloseTime='100ms' QueueDistance='0.25' LimitedProductionAircraftSpots='-1' HelipadBoneName='' ParkInHangars='true' LowersOntoParkingPlaceOnProduction='true' CanSetRallyPoint='false' DoorsUnpackOpen='false'","<DefaultRallyPointOffset x='1' y='2' z='3'/><CanParkHereFilter/>")).InstanceData;int at=LifecycleAt(b,big),filter=(int)Word(b,at+44,big),vector=(int)Word(b,at+48,big);if(Word(b,at+8,big)!=unchecked((uint)-17) || Word(b,at+12,big)!=8 || Word(b,at+16,big)!=0x41480000u || Word(b,at+20,big)!=0x40000000u || Word(b,at+24,big)!=0x3dcccccdu || Word(b,at+28,big)!=0x3e800000u || Word(b,at+32,big)!=0xffffffffu || vector!=filter+112 || b.Length!=vector+12 || Word(b,vector,big)!=0x3f800000u || Word(b,vector+4,big)!=0x40000000u || Word(b,vector+8,big)!=0x40400000u || b[at+52]!=1 || b[at+53]!=1 || b[at+54]!=0 || b[at+55]!=0)throw new Exception("Independent Parking signed/time/bool/nullable Filter112 then Vector3 allocation");}
        {var a=Check(original,p,"Browser='a bb'",QueueParkingXML(1,"",QueueParkingFilter+"<DefaultRallyPointOffset x='1' y='2' z='3'/>"));var b=Check(original,p,"Browser='a bb'",QueueParkingXML(1,"","<DefaultRallyPointOffset z='3' y='2' x='1'/>"+QueueParkingFilter));Equal(a.InstanceData,b.InstanceData,"Parking schema allocation order",false);Equal(a.RelocationData,b.RelocationData,"Parking schema relocations",false);Equal(a.ImportsData,b.ImportsData,"Parking schema imports",false);}
    }
#endif
}
