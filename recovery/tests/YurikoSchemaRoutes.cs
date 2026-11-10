using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using EALA.Hash;
class YurikoSchemaRoutes {
    static int Main(string[] args) {
        var expected=new Dictionary<string,uint>();
        string modules="";
        if(args[1]=="shield") {expected.Add("YurikoShieldSphereUpdateModuleData",0x95d855b6u);modules="<YurikoShieldSphereUpdate id='Shield'><ObjectFilter Rule='ANY' Include='INFANTRY'/></YurikoShieldSphereUpdate>";}
        if(args[1]=="domination") {expected.Add("LureObjectsUpdateModuleData",0xb65ab72au);expected.Add("AddObjectsToLureUpdateSpecialPowerModuleData",0xa7deffccu);modules="<LureObjectsUpdate id='Lure'><GuardOffset x='1' y='2' z='3'/></LureObjectsUpdate><AddObjectsToLureUpdateSpecialPower id='Dominate' SpecialPowerTemplate='TestPower' TeleportLinkID='4' LureObjectLinkID='7'/>";}
        if(args[1]=="slam") {expected.Add("AddObjectsToLiftUpdateSpecialPowerModuleData",0xd9b247dfu);expected.Add("FlingStoredObjectsSpecialPowerModuleData",0x12d1fad9u);expected.Add("YurikoLiftObjectUpdateModuleData",0xaa9e10d6u);modules="<YurikoLiftObjectUpdate id='Lift' LiftObjectLinkID='7'/><AddObjectsToLiftUpdateSpecialPower id='Pickup' SpecialPowerTemplate='TestPower'/><FlingStoredObjectsSpecialPower id='Throw' SpecialPowerTemplate='TestPower'/>";}
        if(expected.Count==0)throw new Exception("unknown fixture group");
        var schemas=new XmlSchemaSet();schemas.XmlResolver=new XmlUrlResolver();schemas.Add("uri:ea.com:eala:asset",args[0]);schemas.Compile();
        var settings=new XmlReaderSettings();settings.Schemas=schemas;settings.ValidationType=ValidationType.Schema;
        settings.ValidationEventHandler+=(sender,e)=>{throw e.Exception;};
        string xml="<AssetDeclaration xmlns='uri:ea.com:eala:asset'><GameObject id='Test'><Behaviors>"+modules+"</Behaviors><AI><AIUpdate id='AI'/></AI><Body><ActiveBody id='Body' MaxHealth='500'/></Body></GameObject></AssetDeclaration>";
        int checkedTypes=0;
        using(var reader=XmlReader.Create(new StringReader(xml),settings)) while(reader.Read()) {
            if(reader.NodeType!=XmlNodeType.Element || reader.SchemaInfo==null || reader.SchemaInfo.SchemaType==null)continue;
            string typeName=reader.SchemaInfo.SchemaType.Name;uint nativeType;
            if(typeName!=null && expected.TryGetValue(typeName,out nativeType)) {
                // Production SchemaSet.GetInstanceTypeMetadata uses the schema
                // type's Name with HashProvider.GetCaseSensitiveSymbolHash.
                uint actual=FastHash.GetHashCode(typeName);
                if(actual!=nativeType)throw new Exception(typeName+" selected wrong native TypeId");
                ++checkedTypes;
            }
        }
        if(checkedTypes!=expected.Count)throw new Exception("module schema route missing");
        Console.WriteLine("Validated XML without injected TypeId; all "+checkedTypes+" module schema names map to native IDs");return 0;
    }
}
