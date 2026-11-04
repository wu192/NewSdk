// Included by GameObject.cpp; definitions remain in its namespace and original order.
// Process-lifetime metadata avoids mixed-mode CRT destructor callbacks after
// CLR shutdown. The native InitOnce barrier publishes only a complete index.
using ModuleIndex=std::unordered_map<U32,const ModuleChoice*>;
INIT_ONCE ModuleIndexOnce=INIT_ONCE_STATIC_INIT;
const ModuleIndex* moduleIndex=nullptr;
BOOL CALLBACK InitializeModuleIndex(PINIT_ONCE,PVOID,PVOID*) {
    try {
        std::unique_ptr<ModuleIndex> index(new ModuleIndex);
        index->reserve(320);
        // emplace keeps the original first-match precedence across families.
        for(const auto& choice:drawModelChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:drawBeamChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:containRestChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:containChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:slowChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:controlChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:supportChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:areaChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:utilityChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:attachChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:abilityFinishChoices) index->emplace(choice.type,&choice);
        index->emplace(abilityToggleChoice.type,&abilityToggleChoice);
        for(const auto& choice:abilityResourceChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:abilityRoutingChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:abilityStateChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:abilityMovementChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:powerCreationChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:abilityWeaponChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:abilityChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:powerRoutingChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:specialDerivedChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:specialPowerChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:motionCleanupChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:modelConditionChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:stateEventChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:resourceAlliesChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:wallEncloseChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:tiberiumChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:stealthVisionChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:statusEffectsChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:combatResponseChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:structureWallChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:economyPowerChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:queueParkingChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:buildingStructureChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:crateWatcherChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:spawnChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:assistChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:economyChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:aiUpdateChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:crateChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:healingChoices) index->emplace(choice.type,&choice);
        index->emplace(weaponSetChoice.type,&weaponSetChoice);
        for(const auto& choice:reactiveChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:flightChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:motionChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:beamChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:weaponChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:visionChoices) index->emplace(choice.type,&choice);
        index->emplace(receiverChoice.type,&receiverChoice);
        index->emplace(slavedChoice.type,&slavedChoice);
        for(const auto& choice:createChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:productionChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:remainingChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:upgradeChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:dieChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:lifecycleChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:selectorChoices) index->emplace(choice.type,&choice);
        for(const auto& choice:moduleChoices) index->emplace(choice.type,&choice);
        moduleIndex=index.release();
        return TRUE;
    } catch(...) {
        SetLastError(ERROR_NOT_ENOUGH_MEMORY);
        return FALSE;
    }
}
const ModuleChoice* FindModule(U32 type) {
    if(!InitOnceExecuteOnce(&ModuleIndexOnce,InitializeModuleIndex,nullptr,nullptr))
        throw std::runtime_error("GameObject module index initialization failed.");
    auto found=moduleIndex->find(type);
    return found==moduleIndex->end()?nullptr:found->second;
}

void MarshalModuleFieldSet(const GameObjectChild& input,unsigned char* output,const ModuleFieldSet& fields,Relo::Tracker& data) {
    for(unsigned i=0;i<fields.count;++i) {
        const auto& f=fields.fields[i];
        auto found=input.attributes.find(f.name);if(found==input.attributes.end() && !f.fallback) continue;
        auto at=output+f.offset;
        if(f.scalarSize) {
            if(found==input.attributes.end() || (f.fallback && found->second==f.fallback))std::memcpy(at,&f.defaultWord,f.scalarSize);
            else DecodeModuleScalar(f,found->second,at);
            continue;
        }
        std::string fallback;if(found==input.attributes.end())fallback=f.fallback;
        const std::string& value=found==input.attributes.end()?fallback:found->second;
        switch(f.kind) {
        case ModuleValue::DeathBits:MarshalEnumBitset(value,lifecycleDeathNames,34,33,34,reinterpret_cast<U32*>(at));break;
        case ModuleValue::NullableAssetId:
            {auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=HashAssetId(value);data.Pop();}break;
        case ModuleValue::NullableSigned:
            {auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=static_cast<U32>(ParseSigned(value));data.Pop();}break;
        case ModuleValue::ObjectStatus:MarshalObjectStatus(value,reinterpret_cast<U32*>(at));break;
        case ModuleValue::NullableObjectStatus:
            {auto words=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),28,1));MarshalObjectStatus(value,words);data.Pop();}break;
        case ModuleValue::NullableStringHash:{auto target=static_cast<U32*>(data.Push(reinterpret_cast<void**>(at),4,1));*target=ExactStringHash(value);data.Pop();}break;
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
        case ModuleValue::String:MarshalString(data,at,value);break;
        case ModuleValue::Reference:MarshalReference(data,at,value);break;
        case ModuleValue::NullableReference:
            {auto reference=data.Push(reinterpret_cast<void**>(at),4,1);MarshalReference(data,reference,value);data.Pop();}break;

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





