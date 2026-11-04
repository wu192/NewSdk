#include "Xmlsoundmaps.h"
#include "ScalarParsing.h"
#include "DataInitializers.h"
#include "ResourceMarshaling.h"
#include "EnumBitset.h"
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace SoundmapsDetail {
using Relo::U32;
struct Field {const char* name;unsigned offset;};
#include "XmlsoundmapsFields.inc"
void Check(bool ok,const char* message){if(!ok)throw std::invalid_argument(message);}

void Word(void* p,U32 v){std::memcpy(p,&v,4);}
void Real(void* p,float v){std::memcpy(p,&v,4);}
void Swap(void* p){U32 v;std::memcpy(&v,p,4);Word(p,(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24));}
void Swap16(void* p){auto b=static_cast<unsigned char*>(p);unsigned char v=b[0];b[0]=b[1];b[1]=v;}
std::string Value(const XmlsoundmapsNode& n,const char* key,const char* fallback=""){auto a=n.attributes.find(key);return a==n.attributes.end()?fallback:a->second;}
bool Has(const XmlsoundmapsNode& n,const char* key){return n.attributes.count(key)!=0;}

// StringHashTag uses the original FastHash without TypedAssetId's lowercasing.
U32 RawHash(const std::string& input){
    const auto p0=reinterpret_cast<const unsigned char*>(input.c_str());auto p=p0;U32 size=static_cast<U32>(std::strlen(input.c_str())),h=size;
    if(!size)return 0;
    auto shortWord=[](const unsigned char* b){return U32(b[0])|(U32(b[1])<<8);};
    auto signedByte=[](unsigned char c){return static_cast<U32>(static_cast<std::int8_t>(c));};
    for(U32 n=size/4;n;--n,p+=4){h+=shortWord(p);h^=(shortWord(p+2)^(h<<5))<<11;h+=h>>11;}
    switch(size&3){case 3:h+=shortWord(p);h^=((signedByte(p[2])<<2)^h)<<16;h+=h>>11;break;case 2:h+=shortWord(p);h^=h<<11;h+=h>>17;break;case 1:h+=signedByte(*p);h^=h<<10;h+=h>>1;break;}
    h^=h<<3;h+=h>>5;h^=h<<2;h+=h>>15;return(h<<10)^h;
}
void NullableFlags(Relo::Tracker& tracker,unsigned char* root,unsigned offset,const XmlsoundmapsNode& input,const char* name,bool model,bool big){
    if(!Has(input,name))return;unsigned words=model?15:7;
    auto p=static_cast<U32*>(tracker.Push(reinterpret_cast<void**>(root+offset),4,words));
    if(model)MarshalModelCondition(Value(input,name),p);else MarshalObjectStatus(Value(input,name),p);
    if(big)for(unsigned i=0;i<words;++i)Swap(p+i);tracker.Pop();
}
void Sounds(Relo::Tracker& tracker,unsigned char* root,const XmlsoundmapsNode& input,bool big){
    unsigned count=static_cast<unsigned>(input.children.size());Word(root+32,count);
    if(count){auto items=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(root+36),24,count));
        for(unsigned i=0;i<count;++i){const auto& sound=input.children[i];auto item=items+24*i;
            if(Has(sound,"Name"))Word(item,HashAssetId(Value(sound,"Name")));
            if(Has(sound,"Sound"))MarshalReference(tracker,item+4,Value(sound,"Sound"));
            unsigned keys=0,ducks=0;for(const auto& c:sound.children){if(c.name=="Key")++keys;else ++ducks;}
            Word(item+8,keys);Word(item+16,ducks);
            // Marshal's two lists are emitted in schema field order, independent of XML order.
            if(keys){auto values=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(item+12),4,keys));unsigned j=0;
                for(const auto& c:sound.children)if(c.name=="Key"){Word(values+4*j,RawHash(c.value));if(big)Swap(values+4*j);++j;}tracker.Pop();}
            if(ducks){auto values=static_cast<unsigned char*>(tracker.Push(reinterpret_cast<void**>(item+20),12,ducks));unsigned j=0;
                for(const auto& c:sound.children)if(c.name=="Duck"){auto d=values+12*j;
                    if(Has(c,"AudioMap"))MarshalReference(tracker,d,Value(c,"AudioMap"));if(Has(c,"SoundRef"))Word(d+4,HashAssetId(Value(c,"SoundRef")));if(Has(c,"VolumeMultiplier"))Real(d+8,ParsePercentage(Value(c,"VolumeMultiplier")));
                    if(big){Swap(d+4);Swap(d+8);}++j;}tracker.Pop();}
            if(big){Swap(item);Swap(item+8);Swap(item+16);}
        }tracker.Pop();}
    if(big)Swap(root+32);
}
}
unsigned int XmlsoundmapsTypeId(const std::string& name){
    using namespace SoundmapsDetail;
    if(name=="AnimationSoundSettings")return ANIMATION_SOUND_SETTINGS_ID;
    if(name=="MiscAudio")return MISC_AUDIO_ID;
    if(name=="LargeGroupAudioMap")return LARGE_GROUP_AUDIO_MAP_ID;
    return 0;
}
Relo::Chunk CompileXmlsoundmaps(const XmlsoundmapsNode& input,bool bigEndian){
    using namespace SoundmapsDetail;static_assert(sizeof(void*)==4,"soundmaps requires x86.");
    Check(XmlsoundmapsTypeId(input.name)!=0,"Unsupported soundmaps root.");
    Relo::Tracker tracker;unsigned char* root=nullptr;
    if(input.name=="AnimationSoundSettings"){
        tracker.Push(reinterpret_cast<void**>(&root),8,1);
        if(Has(input,"MinMicrophoneDistanceToDirty"))Word(root+4,ParseSigned(Value(input,"MinMicrophoneDistanceToDirty")));if(bigEndian)Swap(root+4);
    }else if(input.name=="MiscAudio"){
        std::vector<std::string> attributes;for(const auto& f:MiscFields)attributes.push_back(f.name);tracker.Push(reinterpret_cast<void**>(&root),384,1);
        for(const auto& f:MiscFields)if(Has(input,f.name))MarshalReference(tracker,root+f.offset,Value(input,f.name));
    }else{

        tracker.Push(reinterpret_cast<void**>(&root),48,1);
        NullableFlags(tracker,root,4,input,"RequiredModelConditionFlags",true,bigEndian);NullableFlags(tracker,root,8,input,"ExcludedModelConditionFlags",true,bigEndian);
        NullableFlags(tracker,root,12,input,"RequiredObjectStatusBits",false,bigEndian);NullableFlags(tracker,root,16,input,"ExcludedObjectStatusBits",false,bigEndian);
        Word(root+20,ParseUnsigned(Value(input,"Size","1000")));Real(root+24,InitializeTime(Value(input,"HandOffModeTime","1s").c_str()));
        Real(root+28,static_cast<float>(static_cast<double>(0.2f)*static_cast<double>(ParseFloat(Value(input,"MaximumAudioSpeed","100")))));
        std::uint16_t start=static_cast<std::uint16_t>(ParseUnsigned(Value(input,"StartThreshold"))),stop=static_cast<std::uint16_t>(ParseUnsigned(Value(input,"StopThreshold")));
        std::memcpy(root+40,&start,2);std::memcpy(root+42,&stop,2);root[44]=ParseBoolean(Value(input,"IgnoreStealthedUnits","true"));
        Sounds(tracker,root,input,bigEndian);if(bigEndian){Swap(root+20);Swap(root+24);Swap(root+28);Swap16(root+40);Swap16(root+42);}
    }
    return tracker.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
