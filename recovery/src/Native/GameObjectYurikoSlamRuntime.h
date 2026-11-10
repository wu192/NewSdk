void MarshalYurikoSlam(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    if(kind==293) {
        MarshalModuleFieldSet(input,output,addLiftFieldSet,data);
        MarshalSpecialPowerDerived(input,output,183,data);
    } else if(kind==294) {
        MarshalModuleFieldSet(input,output,flingFieldSet,data);
        auto list=reinterpret_cast<NativeList*>(output+468);
        for(const auto& child:input.children) if(child.name=="ObjectReplacement") ++list->count;
        if(list->count) {
            auto entries=static_cast<unsigned char*>(data.Push(&list->data,8,list->count));unsigned i=0;
            for(const auto& child:input.children) if(child.name=="ObjectReplacement")
                MarshalModuleFieldSet(child,entries+8*i++,flingReplacementFieldSet,data);
            data.Pop();
        }
        MarshalSpecialPowerBase(input,output,162,data);
    } else {
        MarshalModuleFieldSet(input,output,yurikoLiftFieldSet,data);
        MarshalModuleFieldSet(input,output,utilityFieldSets[236-225],data);
    }
}
void EndianYurikoSlam(unsigned char* output,unsigned kind) {
    if(kind==293) {EndianModuleFieldSet(output,addLiftFieldSet);EndianSpecialPowerDerived(output,183);}
    else if(kind==294) {
        EndianModuleFieldSet(output,flingFieldSet);
        auto list=reinterpret_cast<NativeList*>(output+468);
        for(U32 i=0;i<list->count;++i) EndianModuleFieldSet(static_cast<unsigned char*>(list->data)+8*i,flingReplacementFieldSet);
        Swap(&list->count);
        EndianSpecialPowerBase(output,162);
    }
    else {EndianModuleFieldSet(output,yurikoLiftFieldSet);EndianModuleFieldSet(output,utilityFieldSets[236-225]);}
}

WeaponNuggetInput ReadInlineDamageNugget(const GameObjectChild& node) {
    WeaponNuggetInput input;input.typeId=0xc0f53e99u;input.attributes=node.attributes;
    auto filter=[](const GameObjectChild& child) {
        ObjectFilterInput result;result.attributes=child.attributes;
        for(const auto& item:child.children) {
            if(item.name=="IncludeThing") result.includeThings.push_back(item.text);
            else if(item.name=="ExcludeThing") result.excludeThings.push_back(item.text);
        }
        return result;
    };
    for(const auto& child:node.children) {
        if(child.name=="SpecialObjectFilter") {input.hasSpecialFilter=true;input.specialFilter=filter(child);}
        else if(child.name=="RequiredUpgrade") input.requiredUpgrades.push_back(child.text);
        else if(child.name=="ForbiddenUpgrade") input.forbiddenUpgrades.push_back(child.text);
        else if(child.name=="DamageScalarDetails") {
            NuggetScalarInput scalar;auto found=child.attributes.find("Scalar");
            if(found!=child.attributes.end()) scalar.scalar=found->second;
            for(const auto& detail:child.children) {
                if(detail.name=="Filter") {scalar.hasFilter=true;scalar.filter=filter(detail);}
                else if(detail.name=="RequiredUpgrade") scalar.requiredUpgrades.push_back(detail.text);
            }
            input.damageScalars.push_back(std::move(scalar));
        }
    }
    return input;
}
void MarshalDamageDynamics(const GameObjectChild& input,unsigned char* output,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,damageDynamicsFieldSet,data);
    auto list=reinterpret_cast<NativeList*>(output+12);
    for(const auto& child:input.children) if(child.name=="DamageNugget") ++list->count;
    if(list->count) {
        auto entries=static_cast<unsigned char*>(data.Push(&list->data,DamageNuggetDataSize,list->count));unsigned i=0;
        for(const auto& child:input.children) if(child.name=="DamageNugget")
            MarshalInlineDamageNugget(ReadInlineDamageNugget(child),entries+DamageNuggetDataSize*i++,data);
        data.Pop();
    }
}
void EndianDamageDynamics(unsigned char* output) {
    EndianModuleFieldSet(output,damageDynamicsFieldSet);
    auto list=reinterpret_cast<NativeList*>(output+12);
    for(U32 i=0;i<list->count;++i) EndianInlineDamageNugget(static_cast<unsigned char*>(list->data)+DamageNuggetDataSize*i);
    Swap(&list->count);
}
