// Included by GameObject.cpp; definitions remain in its namespace and original order.
const ModuleChoice* FindModule(U32 type) {
    for(const auto& choice:drawModelChoices) if(choice.type==type) return &choice;
    for(const auto& choice:drawBeamChoices) if(choice.type==type) return &choice;
    for(const auto& choice:containRestChoices) if(choice.type==type) return &choice;
    for(const auto& choice:containChoices) if(choice.type==type) return &choice;
    for(const auto& choice:slowChoices) if(choice.type==type) return &choice;
    for(const auto& choice:controlChoices) if(choice.type==type) return &choice;
    for(const auto& choice:supportChoices) if(choice.type==type) return &choice;
    for(const auto& choice:areaChoices) if(choice.type==type) return &choice;
    for(const auto& choice:utilityChoices) if(choice.type==type) return &choice;
    for(const auto& choice:attachChoices) if(choice.type==type) return &choice;
    for(const auto& choice:abilityFinishChoices) if(choice.type==type) return &choice;
    if(type==abilityToggleChoice.type) return &abilityToggleChoice;
    for(const auto& choice:abilityResourceChoices) if(choice.type==type) return &choice;
    for(const auto& choice:abilityRoutingChoices) if(choice.type==type) return &choice;
    for(const auto& choice:abilityStateChoices) if(choice.type==type) return &choice;
    for(const auto& choice:abilityMovementChoices) if(choice.type==type) return &choice;
    for(const auto& choice:powerCreationChoices) if(choice.type==type) return &choice;
    for(const auto& choice:abilityWeaponChoices) if(choice.type==type) return &choice;
    for(const auto& choice:abilityChoices) if(choice.type==type) return &choice;
    for(const auto& choice:powerRoutingChoices) if(choice.type==type) return &choice;
    for(const auto& choice:specialDerivedChoices) if(choice.type==type) return &choice;
    for(const auto& choice:specialPowerChoices) if(choice.type==type) return &choice;
    for(const auto& choice:motionCleanupChoices) if(choice.type==type) return &choice;
    for(const auto& choice:modelConditionChoices) if(choice.type==type) return &choice;
    for(const auto& choice:stateEventChoices) if(choice.type==type) return &choice;
    for(const auto& choice:resourceAlliesChoices) if(choice.type==type) return &choice;
    for(const auto& choice:wallEncloseChoices) if(choice.type==type) return &choice;
    for(const auto& choice:tiberiumChoices) if(choice.type==type) return &choice;
    for(const auto& choice:stealthVisionChoices) if(choice.type==type) return &choice;
    for(const auto& choice:statusEffectsChoices) if(choice.type==type) return &choice;
    for(const auto& choice:combatResponseChoices) if(choice.type==type) return &choice;
    for(const auto& choice:structureWallChoices) if(choice.type==type) return &choice;
    for(const auto& choice:economyPowerChoices) if(choice.type==type) return &choice;
    for(const auto& choice:queueParkingChoices) if(choice.type==type) return &choice;
    for(const auto& choice:buildingStructureChoices) if(choice.type==type) return &choice;
    for(const auto& choice:crateWatcherChoices) if(choice.type==type) return &choice;
    for(const auto& choice:spawnChoices) if(choice.type==type) return &choice;
    for(const auto& choice:assistChoices) if(choice.type==type) return &choice;
    for(const auto& choice:economyChoices) if(choice.type==type) return &choice;
    for(const auto& choice:aiUpdateChoices) if(choice.type==type) return &choice;
    if(type==dozerAIUpdateChoice.type) return &dozerAIUpdateChoice;
    for(const auto& choice:crateChoices) if(choice.type==type) return &choice;
    for(const auto& choice:healingChoices) if(choice.type==type) return &choice;
    if(type==weaponSetChoice.type) return &weaponSetChoice;
    for(const auto& choice:reactiveChoices) if(choice.type==type) return &choice;
    for(const auto& choice:flightChoices) if(choice.type==type) return &choice;
    for(const auto& choice:motionChoices) if(choice.type==type) return &choice;
    for(const auto& choice:beamChoices) if(choice.type==type) return &choice;
    for(const auto& choice:weaponChoices) if(choice.type==type) return &choice;
    for(const auto& choice:visionChoices) if(choice.type==type) return &choice;
    if(type==receiverChoice.type) return &receiverChoice;
    if(type==slavedChoice.type) return &slavedChoice;
    for(const auto& choice:createChoices) if(choice.type==type) return &choice;
    for(const auto& choice:productionChoices) if(choice.type==type) return &choice;
    for(const auto& choice:remainingChoices) if(choice.type==type) return &choice;
    for(const auto& choice:upgradeChoices) if(choice.type==type) return &choice;
    for(const auto& choice:dieChoices) if(choice.type==type) return &choice;
    for(const auto& choice:lifecycleChoices) if(choice.type==type) return &choice;
    for(const auto& choice:selectorChoices) if(choice.type==type) return &choice;
    for(const auto& choice:moduleChoices) if(choice.type==type) return &choice;
    return nullptr;
}
void MarshalModuleFieldSet(const GameObjectChild& input,unsigned char* output,const ModuleFieldSet& fields,Relo::Tracker& data) {
    for(unsigned i=0;i<fields.count;++i) {
        const auto& f=fields.fields[i];
        auto found=input.attributes.find(f.name);if(found==input.attributes.end() && !f.fallback) continue;
        std::string value=found==input.attributes.end()?f.fallback:found->second;auto at=output+f.offset;float number=0;U32 word=0;
        switch(f.kind) {
        case ModuleValue::Float:case ModuleValue::Percent:
            number=ParseFloat(value);if(f.kind==ModuleValue::Percent) number*=0.01f;std::memcpy(at,&number,4);break;
        case ModuleValue::FrameSpeed:number=static_cast<float>(static_cast<double>(weaponSecondsPerLogicFrame)*static_cast<double>(ParseFloat(value)));std::memcpy(at,&number,4);break;
        case ModuleValue::Angle:number=InitializeAngle(value.c_str());std::memcpy(at,&number,4);break;
        case ModuleValue::DeathBits:MarshalEnumBitset(value,lifecycleDeathNames,34,33,34,reinterpret_cast<U32*>(at));break;
        case ModuleValue::Duration: {
            const float scaled=static_cast<float>(static_cast<double>(0.005f)*static_cast<double>(ParseFloat(value)));
            const double wide=static_cast<double>(static_cast<float>(std::ceil(static_cast<double>(scaled))));
            word=(wide>=-9223372036854775808.0 && wide<9223372036854775808.0)?static_cast<U32>(static_cast<std::int64_t>(wide)):0u;
            std::memcpy(at,&word,4);break;
        }
        case ModuleValue::NullableAssetId:
            {auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=HashAssetId(value);data.Pop();}break;
        case ModuleValue::NullableSigned:
            {auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=static_cast<U32>(ParseSigned(value));data.Pop();}break;
        case ModuleValue::ObjectStatus:MarshalObjectStatus(value,reinterpret_cast<U32*>(at));break;
        case ModuleValue::NullableObjectStatus:
            {auto words=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),28,1));MarshalObjectStatus(value,words);data.Pop();}break;
        case ModuleValue::StringHash:word=ExactStringHash(value);std::memcpy(at,&word,4);break;
        case ModuleValue::NullableStringHash:{auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=ExactStringHash(value);data.Pop();}break;
        case ModuleValue::AssetId:word=HashAssetId(value);std::memcpy(at,&word,4);break;
        case ModuleValue::ModelCondition:MarshalModelCondition(value,reinterpret_cast<U32*>(at));break;
        case ModuleValue::NullableModelCondition:
            {auto words=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),60,1));MarshalModelCondition(value,words);data.Pop();}break;
        case ModuleValue::NullableTime:
            {auto target=static_cast<float*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=InitializeTime(value.c_str());data.Pop();}break;
        case ModuleValue::NullableWideBits:
            {auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),8,1));MarshalEnumBitset(value,f.values->names,f.values->count,f.values->unknown,f.values->bits,target);data.Pop();}break;
        case ModuleValue::NullableBits:
            {auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));MarshalEnumBitset(value,f.values->names,f.values->count,f.values->unknown,f.values->bits,target);data.Pop();}break;
        case ModuleValue::NullableFloat:
            {auto target=static_cast<float*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=ParseFloat(value);data.Pop();}break;
        case ModuleValue::Bits:
        case ModuleValue::ReplaceOptions:MarshalEnumBitset(value,f.values->names,f.values->count,f.values->unknown,f.values->bits,reinterpret_cast<U32*>(at));break;
        case ModuleValue::NullablePositiveFloat:
            {auto target=static_cast<float*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=ParseFloat(value);if(*target<0.0f) throw GameObjectFormatError("Value should be >= 0");data.Pop();}break;
        case ModuleValue::StringList:case ModuleValue::SignedList:case ModuleValue::FloatList: {
            std::vector<std::string> tokens;std::size_t atText=0;
            auto space=[](unsigned char c) {return c==' ' || (c>=9 && c<=13);};
            while(atText<value.size() && value[atText]) {
                while(atText<value.size() && space(static_cast<unsigned char>(value[atText]))) ++atText;
                auto first=atText;while(atText<value.size() && value[atText] && !space(static_cast<unsigned char>(value[atText]))) ++atText;
                if(atText>first) tokens.push_back(value.substr(first,atText-first));
            }
            auto list=reinterpret_cast<NativeList*>(at);list->count=static_cast<U32>(tokens.size());
            if(list->count) {if(f.kind==ModuleValue::StringList) {auto values=static_cast<ReloString*>(data.Push(&list->data,8,list->count));for(U32 j=0;j<list->count;++j) MarshalString(data,values+j,tokens[j]);}else {auto words=static_cast<U32*>(data.Push(&list->data,4,list->count));for(U32 j=0;j<list->count;++j) if(f.kind==ModuleValue::FloatList) {float n=ParseFloat(tokens[j]);std::memcpy(words+j,&n,4);}else words[j]=static_cast<U32>(ParseSigned(tokens[j]));}data.Pop();}
            break;
        }
        case ModuleValue::Signed:word=static_cast<U32>(ParseSigned(value));std::memcpy(at,&word,4);break;
        case ModuleValue::Unsigned:word=ParseUnsigned(value);std::memcpy(at,&word,4);break;
        case ModuleValue::Short:{auto narrow=static_cast<std::uint16_t>(ParseUnsigned(value));std::memcpy(at,&narrow,2);}break;
        case ModuleValue::Time:number=InitializeTime(value.c_str());std::memcpy(at,&number,4);break;
        case ModuleValue::Byte:*at=static_cast<unsigned char>(ParseUnsigned(value));break;
        case ModuleValue::Boolean:*at=ParseBoolean(value)?1:0;break;
        case ModuleValue::String:MarshalString(data,at,value);break;
        case ModuleValue::Reference:MarshalReference(data,at,value);break;
        case ModuleValue::NullableReference:
            {auto reference=data.Push(reinterpret_cast<void**>(at),4,1);MarshalReference(data,reference,value);data.Pop();}break;
        case ModuleValue::Enum:
            word=static_cast<U32>(f.values->unknown);
            for(unsigned j=0;j<f.values->count;++j) if(value==f.values->names[j].name) {word=static_cast<U32>(f.values->names[j].value);break;}
            std::memcpy(at,&word,4);break;
        }
    }
}
template<std::size_t N>
void MarshalModuleFields(const GameObjectChild& input,unsigned char* output,const ModuleField (&fields)[N],Relo::Tracker& data) {
    ModuleFieldSet set={fields,static_cast<unsigned>(N)};MarshalModuleFieldSet(input,output,set,data);
}
U32 ExactStringHash(const std::string& text);
void MarshalClientUpdate(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,moduleFieldSets[kind],data);
    if(kind==9 || kind==10) {
        auto list=reinterpret_cast<NativeList*>(output+(kind==9?12:8));list->count=static_cast<U32>(input.children.size());
        if(list->count) {
            unsigned stride=kind==9?24:124,fields=kind==9?11:12;
            auto entries=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));
            for(U32 i=0;i<list->count;++i) MarshalModuleFieldSet(input.children[i],entries+stride*i,moduleFieldSets[fields],data);
            data.Pop();
        }
    }
    if(kind==8) {
        auto list=reinterpret_cast<NativeList*>(output+16);list->count=static_cast<U32>(input.children.size());
        if(list->count) {auto keys=static_cast<U32*>(data.Push(&list->data,4,list->count));for(U32 i=0;i<list->count;++i) keys[i]=ExactStringHash(input.children[i].text);data.Pop();}
    }
}
void MarshalBody(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    if(kind==2) MarshalModuleFields(input,output,ShieldBodyModuleDataModuleFields,data);
    MarshalModuleFields(input,output,ActiveBodyModuleDataModuleFields,data);
    auto list=reinterpret_cast<NativeList*>(output+64);list->count=static_cast<U32>(input.children.size());
    if(list->count) {
        auto entries=static_cast<unsigned char*>(data.Push(&list->data,12,list->count));
        for(U32 i=0;i<list->count;++i) MarshalModuleFields(input.children[i],entries+12*i,ActiveBodyDamageRangeModifierModuleFields,data);
        data.Pop();
    }
}
void EndianModuleFieldSet(unsigned char* output,const ModuleFieldSet& fields) {
    for(unsigned i=0;i<fields.count;++i) {
        const auto& f=fields.fields[i];auto at=output+f.offset;
        if(f.kind==ModuleValue::StringList) {auto list=reinterpret_cast<NativeList*>(at);for(U32 j=0;j<list->count;++j) Swap(&static_cast<ReloString*>(list->data)[j].length);Swap(&list->count);}
        else if(f.kind==ModuleValue::Bits) {for(unsigned j=0;j<(f.values->bits+31)/32;++j) Swap(at+4*j);}
        else if(f.kind==ModuleValue::NullableWideBits) {auto words=*reinterpret_cast<U32**>(at);if(words) {Swap(words);Swap(words+1);}}
        else if(f.kind==ModuleValue::DeathBits) {Swap(at);Swap(at+4);}
        else if(f.kind==ModuleValue::NullableObjectStatus) {auto words=*reinterpret_cast<U32**>(at);if(words) for(unsigned j=0;j<7;++j) Swap(words+j);}
        else if(f.kind==ModuleValue::ObjectStatus) {for(unsigned j=0;j<7;++j) Swap(at+4*j);}
        else if(f.kind==ModuleValue::NullableSigned || f.kind==ModuleValue::NullableAssetId || f.kind==ModuleValue::NullableStringHash) {auto word=*reinterpret_cast<U32**>(at);if(word) Swap(word);}
        else if(f.kind==ModuleValue::NullableModelCondition) {auto words=*reinterpret_cast<U32**>(at);if(words) for(unsigned j=0;j<15;++j) Swap(words+j);}
        else if(f.kind==ModuleValue::ModelCondition) {for(unsigned j=0;j<15;++j) Swap(at+4*j);}
        else if((f.kind==ModuleValue::NullablePositiveFloat || f.kind==ModuleValue::NullableFloat || f.kind==ModuleValue::NullableTime || f.kind==ModuleValue::NullableBits)) {auto value=*reinterpret_cast<float**>(at);if(value) Swap(value);}
        else if((f.kind==ModuleValue::SignedList || f.kind==ModuleValue::FloatList)) {auto list=reinterpret_cast<NativeList*>(at);for(U32 j=0;j<list->count;++j) Swap(static_cast<U32*>(list->data)+j);Swap(&list->count);}
        else if(f.kind==ModuleValue::Short) std::swap(at[0],at[1]);
        else if(f.kind!=ModuleValue::Byte && f.kind!=ModuleValue::Boolean && f.kind!=ModuleValue::Reference && f.kind!=ModuleValue::NullableReference) Swap(at);
    }
}
template<std::size_t N>
void EndianModuleFields(unsigned char* output,const ModuleField (&fields)[N]) {
    ModuleFieldSet set={fields,static_cast<unsigned>(N)};EndianModuleFieldSet(output,set);
}





