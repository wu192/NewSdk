#pragma once
#include "GameObject.h"
#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <iostream>
using namespace Recovered::Native;
using U32=std::uint32_t;
inline void Require(bool b,const char* label) {if(!b) throw std::runtime_error(label);}
inline U32 Word(const std::vector<unsigned char>& b,unsigned at,bool big) {
    Require(at+4<=b.size(),"token offset outside instance");
    return big?(U32(b[at])<<24)|(U32(b[at+1])<<16)|(U32(b[at+2])<<8)|b[at+3]
              :U32(b[at])|(U32(b[at+1])<<8)|(U32(b[at+2])<<16)|(U32(b[at+3])<<24);
}
inline unsigned Table(const std::vector<unsigned char>& b,U32 type,bool big) {
    for(unsigned at=0;at+12<=b.size();at+=4)
        if(Word(b,at,big)==2 && Word(b,at+4,big)==type)return Word(b,at+8,big);
    throw std::runtime_error("polymorphic module wrapper missing");
}
inline unsigned Field(const std::vector<unsigned char>& b,unsigned table,U32 name,bool big) {
    for(unsigned at=table;at+12<=b.size();at+=12) {
        U32 key=Word(b,at,big),type=Word(b,at+4,big);
        if(!key && !type)break;
        if(key==name)return Word(b,at+8,big);
    }
    throw std::runtime_error("native field token missing");
}
inline unsigned InheritedField(const std::vector<unsigned char>& b,unsigned table,U32 name,bool big) {
    try{return Field(b,table,name,big);}catch(const std::runtime_error&){return InheritedField(b,Field(b,table,1,big),name,big);}
}
inline float Number(const std::vector<unsigned char>& b,unsigned at,bool big) {
    U32 word=Word(b,at,big);float value;std::memcpy(&value,&word,4);return value;
}
inline Recovered::Relo::Chunk Compile(GameObjectChild module,bool big) {
    GameObjectChild behaviors;behaviors.name="Behaviors";behaviors.children.push_back(module);
    return CompileGameObject({}, {behaviors},big);
}
