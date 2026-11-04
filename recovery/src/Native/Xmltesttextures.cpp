#include "Xmltesttextures.h"
#include "ResourceMarshaling.h"
#include <cstdio>
#include <cstring>
#include <stdexcept>
#ifdef _MANAGED
#pragma managed(push,off)
#endif
namespace Recovered { namespace Native { namespace TestTexturesDetail {
using Relo::U32;using Node=XmltesttexturesNode;
void Check(bool ok,const char* s){if(!ok)throw std::invalid_argument(s);}

void Swap(void* p){U32 v;std::memcpy(&v,p,4);v=(v<<24)|((v<<8)&0xff0000u)|((v>>8)&0xff00u)|(v>>24);std::memcpy(p,&v,4);}
struct Builder {
 Relo::Tracker t;std::vector<void*> words;
 void Word(void* p,U32 v){std::memcpy(p,&v,4);words.push_back(p);}
 void Blob(unsigned char* p,const std::string& path){std::FILE* stream=std::fopen(path.c_str(),"rb");if(!stream)return;
    if(std::fseek(stream,0,SEEK_END)!=0){std::fclose(stream);throw std::invalid_argument("DataBlob requires a seekable file.");}long size=std::ftell(stream);if(size<0){std::fclose(stream);throw std::invalid_argument("DataBlob file length failed.");}
    U32 count=static_cast<U32>(size);void* data=nullptr;try{data=t.Push(reinterpret_cast<void**>(p),1,count);}catch(...){std::fclose(stream);throw;}std::fseek(stream,0,SEEK_SET);if(std::fread(data,1,count,stream)!=count)count=0;std::fclose(stream);Word(p+4,count);t.Pop();
 }
};
}
unsigned int XmltesttexturesTypeId(const std::string& name){if(name=="TestTexture")return 2826773070u;if(name=="TestTextureCollection")return 49556361u;return 0;}
Relo::Chunk CompileXmltesttextures(const XmltesttexturesNode& n,bool bigEndian){using namespace TestTexturesDetail;static_assert(sizeof(void*)==4,"Test textures require x86.");
    unsigned id=XmltesttexturesTypeId(n.name);bool texture=id==2826773070u;Builder b;unsigned char* root=nullptr;b.t.Push(reinterpret_cast<void**>(&root),texture?20:12,1);
    if(texture){auto a=n.attributes.find("CustomData");if(a!=n.attributes.end()){MarshalString(b.t,root+4,a->second);b.words.push_back(root+4);}}
    U32 count=static_cast<U32>(n.children.size());b.Word(root+(texture?12:4),count);if(count){auto items=static_cast<unsigned char*>(b.t.Push(reinterpret_cast<void**>(root+(texture?16:8)),texture?8:4,count));for(U32 i=0;i<count;++i){const auto& c=n.children[i];if(texture)b.Blob(items+8*i,c.value);else MarshalReference(b.t,items+4*i,c.value);}b.t.Pop();}
    if(bigEndian)for(void* word:b.words)Swap(word);
    return b.t.MakeRelocatable(bigEndian,0);
}
}}
#ifdef _MANAGED
#pragma managed(pop)
#endif
