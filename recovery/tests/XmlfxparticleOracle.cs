using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Xml;
using BinaryAssetBuilder.Core;
using Recovered;
partial class XmlfxparticleOracle {
    const string NS="uri:ea.com:eala:asset";
    static int fixtures,comparisons,rejections,layouts;
    static readonly string[] Numbers={"0","-0","1","-1","12.25","1e-7","1e30","16777217","2d1","2D-1","0x10","garbage",""," 25.75junk","25,5","100%","-12.5%","2147483648","-2147483649","077","0xff"};
    static readonly string[] Refs={"","Name","Name\\0","Name\\27","Name\\-1","Name\\2147483648","Name\\ +17junk","资源\\7"};
    static readonly string[] Bools={"true","false","TRUE","1","0","t","T"," true","yes","","10"};
    static int ModuleSize(uint id) { switch(id) {
        case 3408220251u: return 20;
        case 3036920908u: return 36;
        case 2248017887u: return 60;
        case 234581578u: return 200;
        case 2933826200u: return 104;
        case 3465979181u: return 24;
        case 1075723135u: return 28;
        case 2693838876u: return 12;
        case 877888681u: return 4;
        case 3201797789u: return 4;
        case 3809157760u: return 4;
        case 2268785311u: return 4;
        case 1903056374u: return 4;
        case 612354849u: return 8;
        case 3543124331u: return 8;
        case 1423381183u: return 8;
        case 1373300932u: return 8;
        case 1865622437u: return 24;
        case 2381754661u: return 24;
        case 3110487039u: return 36;
        case 1976375475u: return 8;
        case 3992468329u: return 12;
        case 1242273524u: return 20;
        case 1552576278u: return 32;
        case 795809322u: return 32;
        case 228810652u: return 32;
        case 3772792373u: return 48;
        case 2343798068u: return 16;
        case 3082112962u: return 16;
        case 3476018911u: return 28;
        case 4058790702u: return 28;
        case 3880943874u: return 40;
        case 3975650247u: return 68;
        default:return 4; } }
    static uint StoredType(uint id) { switch(id) {
        case 3408220251u: return id;
        case 3036920908u: return id;
        case 2248017887u: return id;
        case 234581578u: return id;
        case 2933826200u: return id;
        case 3465979181u: return id;
        case 1075723135u: return id;
        case 2693838876u: return id;
        case 877888681u: return id;
        case 3201797789u: return id;
        case 3809157760u: return id;
        case 2268785311u: return id;
        case 1903056374u: return id;
        case 612354849u: return id;
        case 3543124331u: return id;
        case 1423381183u: return id;
        case 1373300932u: return id;
        case 1865622437u: return id;
        case 2381754661u: return id;
        case 3110487039u: return id;
        case 1976375475u: return id;
        case 3992468329u: return id;
        case 1242273524u: return id;
        case 1552576278u: return id;
        case 795809322u: return id;
        case 228810652u: return id;
        case 3772792373u: return id;
        case 2343798068u: return id;
        case 3082112962u: return id;
        case 3476018911u: return id;
        case 4058790702u: return id;
        case 3880943874u: return id;
        case 3975650247u: return id;
        default:return 0; } }
    static string Escape(string value) { return value.Replace("&", "&amp;").Replace("'", "&apos;").Replace("<", "&lt;").Replace("\"", "&quot;"); }
    static string Attr(string name, string value) { return name+"='"+Escape(value)+"' "; }
    static string Xml(string attrs, string children) { return "<FXParticleSystemTemplate xmlns='"+NS+"' id='Oracle' "+attrs+">"+children+"</FXParticleSystemTemplate>"; }
    static XmlDocument Parse(string text) { XmlDocument d = new XmlDocument(); d.LoadXml(text); return d; }
    static XmlNamespaceManager Namespaces(XmlDocument d) { XmlNamespaceManager n = new XmlNamespaceManager(d.NameTable); n.AddNamespace("ea", NS); return n; }
    static InstanceDeclaration Declaration(XmlDocument xml, XmlNamespaceManager namespaces) {
        AssetDeclarationDocument document = new AssetDeclarationDocument();
        Type stateType = typeof(AssetDeclarationDocument).GetNestedType("CurrentState", BindingFlags.NonPublic);
        object state = FormatterServices.GetUninitializedObject(stateType);
        stateType.GetField("NamespaceManager").SetValue(state, namespaces);
        typeof(AssetDeclarationDocument).GetField("_Current", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(document, state);
        InstanceDeclaration d = new InstanceDeclaration(document); d.XmlNode=xml.DocumentElement; return d;
    }
    static uint Word(byte[] data, int at, bool big) {
        if (!big) return BitConverter.ToUInt32(data, at);
        return ((uint)data[at]<<24)|((uint)data[at+1]<<16)|((uint)data[at+2]<<8)|data[at+3];
    }
    static void Equal(string name, byte[] a, byte[] b) {
        if (a==null || b==null || a.Length!=b.Length) throw new Exception(name+" length original="+(a==null?-1:a.Length)+" recovered="+(b==null?-1:b.Length));
        for (int i=0;i<a.Length;++i) if(a[i]!=b[i]) throw new Exception(name+" byte "+i+" original="+a[i]+" recovered="+b[i]);
        ++comparisons;
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static XmlNode Child(XmlNode root, string name) {
        foreach(XmlNode n in root.ChildNodes) if(n.NodeType==XmlNodeType.Element && n.LocalName==name && n.NamespaceURI==NS) return n;
        return null;
    }
    static bool Imported(string text) { return text!=null && text.IndexOf('\\')>=0; }
    static void Layout(string label,XmlDocument doc,AssetBuffer b,bool big) {
        XmlElement root=doc.DocumentElement; int end=176;
        List<int> locations=new List<int>(),targets=new List<int>(),imports=new List<int>();
        if(Imported(root.HasAttribute("ParticleTexture")?root.GetAttribute("ParticleTexture"):null))imports.Add(16);
        if(Imported(root.HasAttribute("Drawable")?root.GetAttribute("Drawable"):null))imports.Add(20);
        if(root.HasAttribute("EmitterSound")) {
            locations.Add(40);targets.Add(end);
            if(Imported(root.GetAttribute("EmitterSound")))imports.Add(end);
            end+=4;
        } else Check(Word(b.InstanceData,40,big)==0,label+" absent emitter pointer");
        XmlNode alpha=Child(root,"Alphas");
        if(alpha!=null) {
            locations.Add(128);targets.Add(end); int start=end; end+=8;int n=0;
            foreach(XmlNode c in alpha.ChildNodes)if(c.NodeType==XmlNodeType.Element&&c.LocalName=="Alpha")++n;
            Check(Word(b.InstanceData,start,big)==n,label+" alpha count");
            if(n>0) {locations.Add(start+4);targets.Add(end);end+=20*n;}
        } else Check(Word(b.InstanceData,128,big)==0,label+" absent alpha");
        XmlNode colors=Child(root,"Colors");
        if(colors!=null) {
            locations.Add(132);targets.Add(end);int start=end;end+=28;int n=0;
            foreach(XmlNode c in colors.ChildNodes)if(c.NodeType==XmlNodeType.Element&&c.LocalName=="Color")++n;
            Check(Word(b.InstanceData,start+4,big)==n,label+" color count");
            if(n>0){locations.Add(start+8);targets.Add(end);end+=20*n;}
        } else Check(Word(b.InstanceData,132,big)==0,label+" absent colors");
        if(Child(root,"Wind")!=null){locations.Add(136);targets.Add(end);end+=48;}
        else Check(Word(b.InstanceData,136,big)==0,label+" absent wind");
        string[] names={"Physics","Draw","Volume","Velocity","Event","Update"};int[] offsets={140,144,148,152,160,164};
        for(int i=0;i<names.Length;++i) {
            XmlNode wrapper=Child(root,names[i]);
            if(i==4) {
                int n=0;
                if(wrapper!=null)foreach(XmlNode c in wrapper.ChildNodes)if(c.NodeType==XmlNodeType.Element)++n;
                Check(Word(b.InstanceData,156,big)==n,label+" event count");
                if(n==0) { Check(Word(b.InstanceData,160,big)==0,label+" empty event pointer"); continue; }
                locations.Add(160);targets.Add(end);int array=end;end+=4*n;int j=0;
                foreach(XmlNode module in wrapper.ChildNodes)if(module.NodeType==XmlNodeType.Element) {
                    locations.Add(array+4*j++);targets.Add(end);
                    end=ModuleLayout(label,module,end,b,big,locations,targets,imports);
                }
                continue;
            }
            XmlNode single=null;
            if(wrapper!=null)foreach(XmlNode c in wrapper.ChildNodes)if(c.NodeType==XmlNodeType.Element){single=c;break;}
            if(single==null){Check(Word(b.InstanceData,offsets[i],big)==0,label+" absent module");continue;}
            locations.Add(offsets[i]);targets.Add(end);
            end=ModuleLayout(label,single,end,b,big,locations,targets,imports);
        }
        Check(b.InstanceData.Length==end,label+" independent root/block size");
        Check(Word(b.InstanceData,0,big)==0,label+" reserved");
        for(int i=173;i<176;++i)Check(b.InstanceData[i]==0,label+" padding");
        for(int i=0;i<locations.Count;++i)Check(Word(b.InstanceData,locations[i],big)==targets[i],label+" pointer target");
        locations.Sort();imports.Sort();
        Check(b.RelocationData.Length==(locations.Count==0?0:4*(locations.Count+1)),label+" reloc size");
        for(int i=0;i<locations.Count;++i)Check(Word(b.RelocationData,4*i,big)==locations[i],label+" reloc offset");
        if(locations.Count>0)Check(Word(b.RelocationData,b.RelocationData.Length-4,big)==UInt32.MaxValue,label+" reloc sentinel");
        Check(b.ImportsData.Length==(imports.Count==0?0:4*(imports.Count+1)),label+" imports size");
        for(int i=0;i<imports.Count;++i)Check(Word(b.ImportsData,4*i,big)==imports[i],label+" import offset");
        if(imports.Count>0)Check(Word(b.ImportsData,b.ImportsData.Length-4,big)==UInt32.MaxValue,label+" import sentinel");
        ++layouts;
    }
    static int ModuleLayout(string label,XmlNode module,int start,AssetBuffer b,bool big,List<int> locations,List<int> targets,List<int> imports) {
        uint id=0; string text=module.Attributes["TypeId"]==null?"0":module.Attributes["TypeId"].Value;
        if(text.StartsWith("0x"))id=UInt32.Parse(text.Substring(2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);else UInt32.TryParse(text,out id);
        Check(Word(b.InstanceData,start,big)==StoredType(id),label+" module TypeId");
        if(id==2381754661u && module.Attributes["EnvironmentTexture"]!=null && Imported(module.Attributes["EnvironmentTexture"].Value))imports.Add(start+20);
        if(id==2693838876u||id==1075723135u||id==3465979181u) {
            if(module.Attributes["EventFX"]!=null&&Imported(module.Attributes["EventFX"].Value))imports.Add(start+4);
            Check(b.InstanceData[start+10]==0&&b.InstanceData[start+11]==0,label+" event base padding");
            if(id==1075723135u)for(int i=25;i<28;++i)Check(b.InstanceData[start+i]==0,label+" event collision padding");
        }
        return ModuleCompletionLayout(label,module,id,start,b,big,locations,targets,imports);
    }
    static void Test(IAssetBuilderPlugin original, TargetPlatform platform, string label, string text) {
        XmlDocument d=Parse(text); XmlNamespaceManager ns=Namespaces(d);
        InstanceDeclaration declaration=Declaration(d,ns);
        AssetBuffer a;
        try { a=original.ProcessInstance(declaration); }
        catch(Exception e) { throw new Exception(label+" original platform="+(int)platform,e); }
        AssetBuffer b;
        try { b=FXParticleSystemTemplateCompiler.ProcessInstance(declaration,platform); }
        catch(Exception e) { throw new Exception(label+" recovered platform="+(int)platform,e); }
        string prefix=label+"/platform="+(int)platform;
        Equal(prefix+"/instance",a.InstanceData,b.InstanceData);
        Equal(prefix+"/relocations",a.RelocationData,b.RelocationData);
        Equal(prefix+"/imports",a.ImportsData,b.ImportsData);
        Layout(prefix,d,b,platform!=TargetPlatform.Win32); ++fixtures;
    }
    static void Reject(string text, TargetPlatform platform) {
        XmlDocument d=Parse(text); XmlNamespaceManager n=Namespaces(d);
        try { PluginSchemaBoundary.Validate(d);  FXParticleSystemTemplateCompiler.ProcessInstance(Declaration(d,n),platform); }
        catch(NotSupportedException) { ++rejections; return; }
        throw new Exception("Unsupported XML accepted: "+text);
    }
    static string Wrap(string wrapper,uint id,string attrs,string children) { return "<"+wrapper+"><Anything "+Attr("TypeId",id.ToString(CultureInfo.InvariantCulture))+attrs+">"+children+"</Anything></"+wrapper+">"; }
    static string Range(string name,string value) { return "<"+name+" Low='"+Escape(value)+"' High='"+Escape(value)+"'/>"; }
    static void Matrix(IAssetBuilderPlugin original,TargetPlatform p) {
        original.Initialize(null,p);
        Test(original,p,"root-TypeId-decimal",Xml("TypeId='686292351'",""));
        Test(original,p,"root-TypeId-hex",Xml("TypeId='0x28e7fd7f'",""));
        Test(original,p,"structural-TypeId-metadata",Xml("TypeId='686292351'",
            "<SlavePosOffset TypeId='1' x='2.5' y='-3' z='4'/><Lifetime TypeId='2' Low='1' High='3'/>"+
            "<Alphas TypeId='3'><Alpha TypeId='4' Low='0.5' High='0.75'/></Alphas>"+
            "<Colors TypeId='5'><Color TypeId='6'><Color TypeId='7' r='1' g='0.5' b='0.25'/></Color><ColorScale TypeId='8' Low='1' High='2'/></Colors>"+
            "<Wind TypeId='9' Strength='3'/><Event TypeId='10'><Anything TypeId='3465979181'><EventTime TypeId='11' Low='1' High='2'/></Anything></Event>"));
        Reject(Xml("xmlns:q='urn:foreign' q:TypeId='686292351'",""),p);
        Reject(Xml("","<Lifetime xmlns:q='urn:foreign' q:TypeId='2'/>"),p);
        foreach(string typeId in new string[]{"0","99","2693838876","","garbage"})
            Reject(Xml(Attr("TypeId",typeId),""),p);
        Test(original,p,"defaults",Xml("",""));
        foreach(string name in new string[]{"ParticleTexture","Drawable","EmitterSound","SlaveSystem","PerParticleAttachedSystem"})
            foreach(string v in Refs)Test(original,p,name+"-"+v,Xml(Attr(name,v),""));
        foreach(string name in new string[]{"SystemLifetime","SortLevel"})
            foreach(string v in Numbers)Test(original,p,name+"-"+v,Xml(Attr(name,v),""));
        foreach(string name in new string[]{"IsOneShot","IsGroundAligned","IsEmitAboveGroundOnly","IsParticleUpTowardsEmitter","UseMaximumHeight"})
            foreach(string v in Bools)Test(original,p,name+"-"+v,Xml(Attr(name,v),""));
        foreach(string v in new string[]{"ALWAYS_RENDER","HIGH_OR_ABOVE","HIGH_OR_BELOW","INVALID_PRIORITY","LOW_OR_ABOVE","LOW_OR_BELOW","MEDIUM_OR_ABOVE","MEDIUM_OR_BELOW","ULTRA_HIGH_ONLY","ULTRA_HIGH_OR_BELOW","VERY_LOW_ONLY","VERY_LOW_OR_ABOVE","","UNKNOWN","particle"," PARTICLE"})Test(original,p,"enum-Priority-"+v,Xml(Attr("Priority",v),""));
        foreach(string v in new string[]{"ADDITIVE","ADDITIVE_ALPHA_TEST","ADDITIVE_NO_DEPTH_TEST","ALPHA","ALPHA_NO_DEPTH_TEST","ALPHA_TEST","INVALID_SHADER","MULTIPLY","W3D_ALPHA","","UNKNOWN","particle"," PARTICLE"})Test(original,p,"enum-Shader-"+v,Xml(Attr("Shader",v),""));
        foreach(string v in new string[]{"DRAWABLE","GPU_PARTICLE","GPU_TERRAINFIRE","INVALID_TYPE","PARTICLE","STREAK","SWARM","TRAIL","VOLUME_PARTICLE","WAVE_PARTICLE","","UNKNOWN","particle"," PARTICLE"})Test(original,p,"enum-Type-"+v,Xml(Attr("Type",v),""));
        foreach(string name in new string[]{"Lifetime","Size","StartSizeRate","BurstDelay","BurstCount","InitialDelay"})
            foreach(string v in Numbers)Test(original,p,"random-"+name+"-"+v,Xml("",Range(name,v)));
        foreach(string v in new string[]{"CONSTANT","GAUSSIAN","HIGH_BIAS","LOW_BIAS","TRIANGULAR","UNIFORM","UNKNOWN",""})
            Test(original,p,"distribution-"+v,Xml("","<Lifetime "+Attr("Type",v)+" Low='1' High='3'/>"));
        foreach(string axis in new string[]{"x","y","z"})foreach(string v in Numbers)
            Test(original,p,"offset-"+axis+"-"+v,Xml("","<SlavePosOffset "+Attr(axis,v)+"/>"));
        foreach(string v in new string[]{"CIRCULAR","INVALID","NOT_USED","PING_PONG","UNKNOWN",""})
            Test(original,p,"wind-motion-"+v,Xml("","<Wind "+Attr("Motion",v)+"/>"));
        foreach(string name in new string[]{"Strength","FullStrengthDist","ZeroStrengthDist","AngleChangeMin","AngleChangeMax","PingPongStartAngleMin","PingPongStartAngleMax","PingPongEndAngleMin","PingPongEndAngleMax","TurbulenceAmplitude","TurbulenceFrequency"})
            foreach(string v in Numbers)Test(original,p,"wind-"+name+"-"+v,Xml("","<Wind "+Attr(name,v)+"/>"));
        foreach(int n in new int[]{0,1,2,27,28,29,64,257}) {
            string alpha="",color="";
            for(int i=0;i<n;++i){alpha+="<Alpha RelativeAge='25%' Low='0.125' High='0.875'/>";color+="<Color RelativeAge='50%'><Color r='1.25' g='2.5' b='3.75'/></Color>";}
            Test(original,p,"keyframes-"+n,Xml("EmitterSound='S\\3' ParticleTexture='T\\4'","<Alphas>"+alpha+"</Alphas><Colors UseHouseColor='true'>"+color+"<ColorScale Low='0.5' High='2'/></Colors><Wind/>"));
        }
        foreach(string v in Numbers) {
            Test(original,p,"alpha-values-"+v,Xml("","<Alphas><Alpha "+Attr("RelativeAge",v)+Attr("Frame",v)+Attr("Low",v)+Attr("High",v)+"/></Alphas>"));
            Test(original,p,"color-values-"+v,Xml("","<Colors "+Attr("HouseColorSaturation",v)+"><Color "+Attr("RelativeAge",v)+Attr("Frame",v)+"><Color "+Attr("r",v)+Attr("g",v)+Attr("b",v)+"/></Color></Colors>"));
        }
        foreach(string wrapper in new string[]{"Physics","Draw","Volume","Velocity","Update"}) {
            Test(original,p,"empty-"+wrapper,Xml("","<"+wrapper+"/>"));
            foreach(uint id in new uint[]{877888681u,3201797789u,3809157760u,2268785311u,1903056374u,612354849u,3543124331u,1423381183u,1373300932u,1865622437u,2381754661u,3110487039u,1976375475u,3992468329u,1242273524u,1552576278u,795809322u,228810652u,3772792373u,2343798068u,3082112962u,3476018911u,4058790702u,3880943874u,3975650247u,0u,99u,4294967295u})Test(original,p,"dispatch-"+wrapper+"-"+id,Xml("",Wrap(wrapper,id,"","")));
            Test(original,p,"missing-TypeId-"+wrapper,Xml("","<"+wrapper+"><Anything/></"+wrapper+">"));
        }
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeBase-IsHollow-"+v,Xml("",Wrap("Volume",612354849u,Attr("IsHollow",v),"")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumePoint-IsHollow-"+v,Xml("",Wrap("Volume",3543124331u,Attr("IsHollow",v),"")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeLightning-IsHollow-"+v,Xml("",Wrap("Volume",1423381183u,Attr("IsHollow",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawLightSource-MaximumIntensityRadiusRatio-"+v,Xml("",Wrap("Volume",1373300932u,Attr("MaximumIntensityRadiusRatio",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawTrail-TrailLife-"+v,Xml("",Wrap("Volume",1865622437u,Attr("TrailLife",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawTrail-UTile-"+v,Xml("",Wrap("Volume",1865622437u,Attr("UTile",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawTrail-VTile-"+v,Xml("",Wrap("Volume",1865622437u,Attr("VTile",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawTrail-UScrollRate-"+v,Xml("",Wrap("Volume",1865622437u,Attr("UScrollRate",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawTrail-VScrollRate-"+v,Xml("",Wrap("Volume",1865622437u,Attr("VScrollRate",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawSwarm-OpaqueSpeed-"+v,Xml("",Wrap("Volume",2381754661u,Attr("OpaqueSpeed",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawSwarm-TransparentSpeed-"+v,Xml("",Wrap("Volume",2381754661u,Attr("TransparentSpeed",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawSwarm-SpeedStretchAmount-"+v,Xml("",Wrap("Volume",2381754661u,Attr("SpeedStretchAmount",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDrawSwarm-AttractStrength-"+v,Xml("",Wrap("Volume",2381754661u,Attr("AttractStrength",v),"")));
        foreach(string v in Refs)Test(original,p,"FXParticleDrawSwarm-EnvironmentTexture-"+v,Xml("",Wrap("Volume",2381754661u,Attr("EnvironmentTexture",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDefaultPhysics-Gravity-"+v,Xml("",Wrap("Volume",3110487039u,Attr("Gravity",v),"")));
        foreach(string v in Bools)Test(original,p,"FXParticleDefaultPhysics-Swirly-"+v,Xml("",Wrap("Volume",3110487039u,Attr("Swirly",v),"")));
        foreach(string v in Bools)Test(original,p,"FXParticleDefaultPhysics-ParticlesAttachToBone-"+v,Xml("",Wrap("Volume",3110487039u,Attr("ParticlesAttachToBone",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleDefaultPhysics-VelocityDamping-"+v,Xml("",Wrap("Volume",3110487039u,"",Range("VelocityDamping",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleDefaultPhysics-DriftVelocity-"+v,Xml("",Wrap("Volume",3110487039u,"","<DriftVelocity "+Attr("x",v)+Attr("y",v)+Attr("z",v)+"/>")));
        foreach(string v in Numbers)Test(original,p,"FXParticleSwarmPhysics-AttractStrength-"+v,Xml("",Wrap("Volume",1976375475u,Attr("AttractStrength",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeSphere-Radius-"+v,Xml("",Wrap("Volume",3992468329u,Attr("Radius",v),"")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeSphere-IsHollow-"+v,Xml("",Wrap("Volume",3992468329u,Attr("IsHollow",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeBox-HalfSize-"+v,Xml("",Wrap("Volume",1242273524u,"","<HalfSize "+Attr("x",v)+Attr("y",v)+Attr("z",v)+"/>")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeBox-IsHollow-"+v,Xml("",Wrap("Volume",1242273524u,Attr("IsHollow",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeCylinder-Radius-"+v,Xml("",Wrap("Volume",1552576278u,Attr("Radius",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeCylinder-RadiusRate-"+v,Xml("",Wrap("Volume",1552576278u,Attr("RadiusRate",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeCylinder-Length-"+v,Xml("",Wrap("Volume",1552576278u,Attr("Length",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeCylinder-Offset-"+v,Xml("",Wrap("Volume",1552576278u,"","<Offset "+Attr("x",v)+Attr("y",v)+Attr("z",v)+"/>")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeCylinder-IsHollow-"+v,Xml("",Wrap("Volume",1552576278u,Attr("IsHollow",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeSpline-StartPoint-"+v,Xml("",Wrap("Volume",795809322u,"","<StartPoint "+Attr("x",v)+Attr("y",v)+Attr("z",v)+"/>")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeSpline-EndPoint-"+v,Xml("",Wrap("Volume",795809322u,"","<EndPoint "+Attr("x",v)+Attr("y",v)+Attr("z",v)+"/>")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeSpline-IsHollow-"+v,Xml("",Wrap("Volume",795809322u,Attr("IsHollow",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeLine-StartPoint-"+v,Xml("",Wrap("Volume",228810652u,"","<StartPoint "+Attr("x",v)+Attr("y",v)+Attr("z",v)+"/>")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeLine-EndPoint-"+v,Xml("",Wrap("Volume",228810652u,"","<EndPoint "+Attr("x",v)+Attr("y",v)+Attr("z",v)+"/>")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeLine-IsHollow-"+v,Xml("",Wrap("Volume",228810652u,Attr("IsHollow",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeTerrainFire-CellEmissionChance-"+v,Xml("",Wrap("Volume",3772792373u,Attr("CellEmissionChance",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVolumeTerrainFire-Offset-"+v,Xml("",Wrap("Volume",3772792373u,"","<Offset>"+Range("x",v)+Range("y",v)+Range("z",v)+"</Offset>")));
        foreach(string v in Bools)Test(original,p,"FXParticleEmissionVolumeTerrainFire-IsHollow-"+v,Xml("",Wrap("Volume",3772792373u,Attr("IsHollow",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocitySphere-Speed-"+v,Xml("",Wrap("Volume",2343798068u,"",Range("Speed",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityHemisphere-Speed-"+v,Xml("",Wrap("Volume",3082112962u,"",Range("Speed",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityOutward-Speed-"+v,Xml("",Wrap("Volume",3476018911u,"",Range("Speed",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityOutward-OtherSpeed-"+v,Xml("",Wrap("Volume",3476018911u,"",Range("OtherSpeed",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityCylinder-Radial-"+v,Xml("",Wrap("Volume",4058790702u,"",Range("Radial",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityCylinder-Normal-"+v,Xml("",Wrap("Volume",4058790702u,"",Range("Normal",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityOrtho-Position-"+v,Xml("",Wrap("Volume",3880943874u,"","<Position>"+Range("x",v)+Range("y",v)+Range("z",v)+"</Position>")));
        foreach(string v in new string[]{"CIRCLE","INVALID","LINE","UNKNOWN",""})Test(original,p,"FXParticleEmissionVelocityWavefront-Shape-"+v,Xml("",Wrap("Volume",3975650247u,Attr("Shape",v),"")));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityWavefront-Velocity-"+v,Xml("",Wrap("Volume",3975650247u,"",Range("Velocity",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityWavefront-Amplitude-"+v,Xml("",Wrap("Volume",3975650247u,"",Range("Amplitude",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityWavefront-InitialRadius-"+v,Xml("",Wrap("Volume",3975650247u,"",Range("InitialRadius",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityWavefront-LineWidth-"+v,Xml("",Wrap("Volume",3975650247u,"",Range("LineWidth",v))));
        foreach(string v in Numbers)Test(original,p,"FXParticleEmissionVelocityWavefront-LineAngle-"+v,Xml("",Wrap("Volume",3975650247u,"",Range("LineAngle",v))));
        EventCompletionMatrix(original,p);
        ModuleCompletionMatrix(original,p);
        Reject(Xml("Unknown='1'",""),p);
        foreach(string c in new string[]{"<Unknown/>","<Event><Anything Unknown='1'/></Event>","<Wind Unknown='1'/>","<Physics><A/><B/></Physics>","<Physics><A TypeId='2933826200' Unknown='1'/></Physics>","<Draw><A TypeId='2248017887' Unknown='1'/></Draw>","<Colors><ColorScale/><ColorScale/></Colors>","<Wind xmlns=''/>","text"})Reject(Xml("",c),p);
        foreach(uint id in new uint[]{234581578u,2933826200u,2248017887u,3036920908u,3408220251u,2693838876u,1075723135u,3465979181u})Reject(Xml("",Wrap("Physics",id,"Unknown='1'","")),p);
        Reject("<Other xmlns='"+NS+"' id='Oracle'/>",p);
        Reject(Xml("xmlns:q='urn:other' q:Priority='HIGH_OR_ABOVE'",""),p);
        try{FXParticleSystemTemplateCompiler.ProcessInstance(null,p);throw new Exception("null declaration accepted");}catch(ArgumentNullException){++rejections;}
    }
    static int Main(string[] args) {
        Dictionary<string,object> result=new Dictionary<string,object>();
        result["succeeded"]=false;
        try {
            CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");
            Assembly assembly=Assembly.LoadFrom(args[0]);
            IAssetBuilderPlugin original=(IAssetBuilderPlugin)Activator.CreateInstance(assembly.GetType("BinaryAssetBuilder.XmlCompiler.Plugin"));
            Type rootType=assembly.GetType("SageBinaryData.FXParticleSystemTemplate");
            Check(rootType!=null && Marshal.SizeOf(rootType)==176,"original root size");
            using(SHA256 sha=SHA256.Create()) {
                string originalHash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(args[0]))).Replace("-","");
                Check(originalHash=="D4B78E2DA951DD458357BEE7B358E6BC9689D1987D55867254D68B3F87BBF0E6","original DLL fingerprint differs from fixed PE evidence");
                result["originalDllSha256"]=originalHash; Console.WriteLine("originalDllSha256="+originalHash);
            }
            List<object> platformResults=new List<object>();
            foreach(TargetPlatform p in new TargetPlatform[]{(TargetPlatform)0,(TargetPlatform)1,(TargetPlatform)2,(TargetPlatform)99}) {
                int f=fixtures,c=comparisons,r=rejections,l=layouts;
                Matrix(original,p);
                Dictionary<string,object> row=new Dictionary<string,object>();
                row["platform"]=(int)p; row["fixtures"]=fixtures-f; row["arrayComparisons"]=comparisons-c; row["rejections"]=rejections-r; row["layoutAssertions"]=layouts-l;
                platformResults.Add(row);
                Console.WriteLine("PASS platform={0} fixtures={1} arrayComparisons={2} rejections={3} layoutAssertions={4}",(int)p,fixtures-f,comparisons-c,rejections-r,layouts-l);
            }
            result["platforms"]=platformResults; result["fixtures"]=fixtures; result["arrayComparisons"]=comparisons; result["rejections"]=rejections; result["layoutAssertions"]=layouts;
            result["succeeded"]=true;
            Console.WriteLine("PASS FXParticleSystemTemplate fixtures={0} arrayComparisons={1} rejections={2} layoutAssertions={3}",fixtures,comparisons,rejections,layouts);
            return 0;
        } catch(Exception e) {
            result["error"]=e.ToString(); Console.Error.WriteLine(e); return 1;
        } finally {
            if(args.Length>1) File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result));
        }
    }
}


