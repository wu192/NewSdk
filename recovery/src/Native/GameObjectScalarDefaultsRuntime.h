// Included by GameObject.cpp after schema tables. The decoded metadata owns no
// pointers or containers; its destructor is trivial under the mixed-mode CRT.
unsigned ModuleScalarSize(ModuleValue kind,const AIEnum* values) {
    switch(kind) {
    case ModuleValue::Boolean:case ModuleValue::Byte:return 1;
    case ModuleValue::Short:return 2;
    case ModuleValue::Float:case ModuleValue::Percent:case ModuleValue::Time:case ModuleValue::Enum:
    case ModuleValue::Unsigned:case ModuleValue::AssetId:case ModuleValue::Duration:case ModuleValue::Angle:
    case ModuleValue::Signed:case ModuleValue::FrameSpeed:case ModuleValue::StringHash:return 4;
    default:return 0;
    }
}
void DecodeModuleScalar(const ModuleField& field,const std::string& value,unsigned char* at) {
    float number=0;U32 word=0;
    switch(field.kind) {
    case ModuleValue::Float:case ModuleValue::Percent:
        number=ParseFloat(value);if(field.kind==ModuleValue::Percent)number*=0.01f;std::memcpy(at,&number,4);return;
    case ModuleValue::FrameSpeed:number=static_cast<float>(static_cast<double>(weaponSecondsPerLogicFrame)*static_cast<double>(ParseFloat(value)));std::memcpy(at,&number,4);return;
    case ModuleValue::Angle:number=InitializeAngle(value.c_str());std::memcpy(at,&number,4);return;
    case ModuleValue::Time:number=InitializeTime(value.c_str());std::memcpy(at,&number,4);return;
    case ModuleValue::Duration: {
        const float scaled=static_cast<float>(static_cast<double>(0.005f)*static_cast<double>(ParseFloat(value)));
        const double wide=static_cast<double>(static_cast<float>(std::ceil(static_cast<double>(scaled))));
        word=(wide>=-9223372036854775808.0 && wide<9223372036854775808.0)?static_cast<U32>(static_cast<std::int64_t>(wide)):0u;break;
    }
    case ModuleValue::StringHash:word=ExactStringHash(value);break;
    case ModuleValue::AssetId:word=HashAssetId(value);break;
    case ModuleValue::Signed:word=static_cast<U32>(ParseSigned(value));break;
    case ModuleValue::Unsigned:word=ParseUnsigned(value);break;
    case ModuleValue::Short:{auto shortValue=static_cast<std::uint16_t>(ParseUnsigned(value));std::memcpy(at,&shortValue,2);return;}
    case ModuleValue::Byte:*at=static_cast<unsigned char>(ParseUnsigned(value));return;
    case ModuleValue::Boolean:*at=ParseBoolean(value)?1:0;return;
    case ModuleValue::Enum:
        word=static_cast<U32>(field.values->unknown);
        for(unsigned i=0;i<field.values->count;++i) if(value==field.values->names[i].name){word=static_cast<U32>(field.values->names[i].value);break;}
        break;
    default:throw std::logic_error("Expected scalar module field");
    }
    std::memcpy(at,&word,4);
}
ModuleField::ModuleField(const char* fieldName,unsigned fieldOffset,ModuleValue fieldKind,const char* defaultText,const AIEnum* enumeration)
    :name(fieldName),offset(fieldOffset),kind(fieldKind),fallback(defaultText),values(enumeration),scalarSize(ModuleScalarSize(fieldKind,enumeration)),defaultWord(0) {
    if(fallback && scalarSize)DecodeModuleScalar(*this,std::string(fallback),reinterpret_cast<unsigned char*>(&defaultWord));
}
