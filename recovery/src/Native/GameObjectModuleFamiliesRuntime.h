// Included by GameObject.cpp; definitions remain in its namespace and original order.
void MarshalLifecycle(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,lifecycleFieldSets[kind-17],data);
    if(kind==21) for(const auto& child:input.children) {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+20),56,1));
        MarshalModuleFieldSet(child,at,lifecycleFieldSets[6],data);data.Pop();
    }
}
void EndianLifecycle(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,lifecycleFieldSets[kind-17]);
    if(kind==21) {auto at=*reinterpret_cast<unsigned char**>(output+20);if(at) EndianModuleFieldSet(at,lifecycleFieldSets[6]);}
}
void MarshalDie(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,dieFieldSets[kind-23],data);
    if(kind==27) for(unsigned group=0;group<2;++group) {
        auto list=reinterpret_cast<NativeList*>(output+56+8*group);std::vector<const GameObjectChild*> values;
        for(const auto& child:input.children) if(child.name==(group==0?"UpgradeRequired":"UpgradeForbidden")) values.push_back(&child);
        list->count=static_cast<U32>(values.size());
        if(list->count) {auto refs=static_cast<U32*>(data.Push(&list->data,4,list->count));for(U32 j=0;j<list->count;++j) MarshalReference(data,refs+j,values[j]->text);data.Pop();}
    }
    if(kind==29) for(const auto& child:input.children) if(child.name=="ObjectFilter") {
        ObjectFilterInput in;in.attributes=child.attributes;
        for(const auto& entry:child.children) if(entry.name=="IncludeThing") in.includeThings.push_back(entry.text);else in.excludeThings.push_back(entry.text);
        auto filter=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+68),112,1));MarshalObjectFilter(in,filter,data);data.Pop();
    }
    for(const auto& child:input.children) if(child.name=="DieMuxData") MarshalModuleFieldSet(child,output+8,dieFieldSets[7],data);
}
void EndianDie(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,dieFieldSets[kind-23]);
    if(kind==27) {Swap(output+56);Swap(output+64);}
    if(kind==29) {auto filter=*reinterpret_cast<ObjectFilter**>(output+68);if(filter) EndianObjectFilter(filter);}
    EndianModuleFieldSet(output+8,dieFieldSets[7]);
}
void MarshalWeakChildren(const GameObjectChild& input,const char* name,unsigned char* output,unsigned offset,Relo::Tracker& data) {
    auto list=reinterpret_cast<NativeList*>(output+offset);std::vector<const GameObjectChild*> entries;
    for(const auto& child:input.children) if(child.name==name) entries.push_back(&child);
    list->count=static_cast<U32>(entries.size());if(!list->count) return;
    auto ids=static_cast<U32*>(data.Push(&list->data,4,list->count));for(U32 i=0;i<list->count;++i) ids[i]=HashAssetId(entries[i]->text);data.Pop();
}
void EndianWeakChildren(unsigned char* output,unsigned offset) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(U32 i=0;i<list->count;++i) Swap(static_cast<U32*>(list->data)+i);Swap(&list->count);
}
void MarshalUpgradeFilter(const GameObjectChild& child,ObjectFilter* target,Relo::Tracker& data) {
    ObjectFilterInput input;input.attributes=child.attributes;for(const auto& entry:child.children) if(entry.name=="IncludeThing") input.includeThings.push_back(entry.text);else input.excludeThings.push_back(entry.text);MarshalObjectFilter(input,target,data);
}
void MarshalUpgradeAnim(const GameObjectChild& input,unsigned char* output,unsigned offset,Relo::Tracker& data) {
    auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+offset),12,1));MarshalModuleFieldSet(input,at,upgradeFieldSets[11],data);data.Pop();
}
void MarshalUpgradeStruct(const GameObjectChild& input,unsigned char* output,unsigned fields,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,remainingFieldSets[fields],data);
    if(fields==14) for(const auto& child:input.children) {U32 id=HashAssetId(child.text);std::memcpy(output+(child.name=="UpgradeTrigger"?0:4),&id,4);}
    if(fields==13) for(const auto& child:input.children) MarshalModuleFieldSet(child,output+60,remainingFieldSets[15],data);
    if(fields==11) {
        auto list=reinterpret_cast<NativeList*>(output+28);for(const auto& child:input.children) if(child.name=="UpgradeTexture") ++list->count;
        if(list->count) {auto at=static_cast<unsigned char*>(data.Push(&list->data,8,list->count));unsigned i=0;for(const auto& child:input.children) if(child.name=="UpgradeTexture") MarshalModuleFieldSet(child,at+8*i++,remainingFieldSets[12],data);data.Pop();}
        for(const auto& child:input.children) if(child.name=="CustomAnimAndDuration") MarshalUpgradeAnim(child,output,36,data);
    }
}
void EndianUpgradeStruct(unsigned char* output,unsigned fields) {
    EndianModuleFieldSet(output,remainingFieldSets[fields]);
    if(fields==14) {Swap(output);Swap(output+4);}
    if(fields==13) EndianModuleFieldSet(output+60,remainingFieldSets[15]);
    if(fields==11) {Swap(output+28);auto anim=*reinterpret_cast<unsigned char**>(output+36);if(anim) EndianModuleFieldSet(anim,upgradeFieldSets[11]);}
}
void MarshalUpgradeStructList(const GameObjectChild& input,const char* name,unsigned char* output,unsigned offset,unsigned stride,unsigned fields,Relo::Tracker& data) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(const auto& child:input.children) if(child.name==name) ++list->count;
    if(list->count) {auto at=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));unsigned i=0;for(const auto& child:input.children) if(child.name==name) MarshalUpgradeStruct(child,at+stride*i++,fields,data);data.Pop();}
}
void EndianUpgradeStructList(unsigned char* output,unsigned offset,unsigned stride,unsigned fields) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(U32 i=0;i<list->count;++i) EndianUpgradeStruct(static_cast<unsigned char*>(list->data)+stride*i,fields);Swap(&list->count);
}
void MarshalRemainingUpgrade(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,remainingFieldSets[kind-41],data);
    if(kind==41) {MarshalUpgradeStructList(input,"AvailableSubStructure",output,36,8,14,data);MarshalUpgradeStructList(input,"StructureOffset",output,44,72,13,data);}
    if(kind==43) MarshalWeakChildren(input,"ReplacementTemplate",output,44,data);
    if(kind==44) MarshalUpgradeStructList(input,"Nugget",output,36,44,11,data);
    if(kind==45) for(const auto& child:input.children) if(child.name=="Offset") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+64),12,1));MarshalModuleFieldSet(child,at,remainingFieldSets[15],data);data.Pop();}
    if(kind==46) MarshalWeakChildren(input,"UpgradeToRemove",output,44,data);
    if(kind==49) {for(const auto& child:input.children) if(child.name=="ObjectFilter") {auto at=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+52),112,1));MarshalUpgradeFilter(child,at,data);data.Pop();}MarshalWeakChildren(input,"ApplyToUpgrade",output,56,data);}
    if(kind==50) for(const auto& child:input.children) if(child.name=="ObjectFilter") MarshalUpgradeFilter(child,reinterpret_cast<ObjectFilter*>(output+56),data);
}
void EndianRemainingUpgrade(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,remainingFieldSets[kind-41]);
    if(kind==41) {EndianUpgradeStructList(output,36,8,14);EndianUpgradeStructList(output,44,72,13);}
    if(kind==43) EndianWeakChildren(output,44);
    if(kind==44) EndianUpgradeStructList(output,36,44,11);
    if(kind==45) {auto at=*reinterpret_cast<unsigned char**>(output+64);if(at) EndianModuleFieldSet(at,remainingFieldSets[15]);}
    if(kind==46) EndianWeakChildren(output,44);
    if(kind==49) {auto filter=*reinterpret_cast<ObjectFilter**>(output+52);if(filter) EndianObjectFilter(filter);EndianWeakChildren(output,56);}
    if(kind==50) EndianObjectFilter(reinterpret_cast<ObjectFilter*>(output+56));
}
void MarshalUpgrade(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    if(kind==60) {
        MarshalModuleFieldSet(input,output,receiverFieldSet,data);
        for(const auto& child:input.children) if(child.name=="ObjectFilter") MarshalUpgradeFilter(child,reinterpret_cast<ObjectFilter*>(output+68),data);
    } else if(kind>=41) MarshalRemainingUpgrade(input,output,kind,data);
    if(kind>30 && kind<=40) MarshalModuleFieldSet(input,output,upgradeFieldSets[kind-30],data);
    MarshalModuleFieldSet(input,output,upgradeFieldSets[0],data);
    for(const auto& child:input.children) if(child.name=="CustomAnimAndDuration") {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+8),12,1));
        MarshalModuleFieldSet(child,at,upgradeFieldSets[11],data);data.Pop();
    }
    for(unsigned group=0;group<2;++group) {
        auto list=reinterpret_cast<NativeList*>(output+12+8*group);std::vector<const GameObjectChild*> values;
        for(const auto& child:input.children) if(child.name==(group==0?"TriggeredBy":"ConflictsWith")) values.push_back(&child);
        list->count=static_cast<U32>(values.size());
        if(list->count) {auto ids=static_cast<U32*>(data.Push(&list->data,4,list->count));for(U32 j=0;j<list->count;++j) ids[j]=HashAssetId(values[j]->text);data.Pop();}
    }
}
void EndianUpgrade(unsigned char* output,unsigned kind) {
    if(kind==60) {EndianModuleFieldSet(output,receiverFieldSet);EndianObjectFilter(reinterpret_cast<ObjectFilter*>(output+68));}
    else if(kind>=41) EndianRemainingUpgrade(output,kind);
    if(kind>30 && kind<=40) EndianModuleFieldSet(output,upgradeFieldSets[kind-30]);
    auto anim=*reinterpret_cast<unsigned char**>(output+8);if(anim) EndianModuleFieldSet(anim,upgradeFieldSets[11]);
    for(unsigned group=0;group<2;++group) {auto list=reinterpret_cast<NativeList*>(output+12+8*group);for(U32 j=0;j<list->count;++j) Swap(static_cast<U32*>(list->data)+j);Swap(&list->count);}
}
void MarshalProductionList(const GameObjectChild& input,const char* name,unsigned char* output,unsigned offset,unsigned stride,unsigned fields,Relo::Tracker& data) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(const auto& child:input.children) if(child.name==name) ++list->count;
    if(!list->count) return;
    auto at=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));unsigned i=0;
    for(const auto& child:input.children) if(child.name==name) {
        auto entry=at+stride*i++;MarshalModuleFieldSet(child,entry,productionFieldSets[fields],data);
        if(fields==4) for(const auto& filter:child.children) MarshalUpgradeFilter(filter,reinterpret_cast<ObjectFilter*>(entry+12),data);
    }
    data.Pop();
}
void EndianProductionList(unsigned char* output,unsigned offset,unsigned stride,unsigned fields) {
    auto list=reinterpret_cast<NativeList*>(output+offset);
    for(U32 i=0;i<list->count;++i) {auto at=static_cast<unsigned char*>(list->data)+stride*i;EndianModuleFieldSet(at,productionFieldSets[fields]);if(fields==4) EndianObjectFilter(reinterpret_cast<ObjectFilter*>(at+12));}
    Swap(&list->count);
}
void MarshalProduction(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,productionFieldSets[kind-52],data);
    if(kind==52) {
        MarshalProductionList(input,"QuantityModifier",output,60,8,3,data);MarshalProductionList(input,"ProductionModifier",output,68,124,4,data);
        for(const auto& child:input.children) if(child.name=="GiveUpgradeToProduced") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+76),8,1));MarshalProductionList(child,"UpgradeTranslate",at,0,8,5,data);data.Pop();}
    } else for(unsigned i=0;i<2;++i) for(const auto& child:input.children) if(child.name==(i==0?"UnitCreatePoint":"NaturalRallyPoint")) {
        unsigned char* at=output+20+12*i;
        if(kind==53) at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+8+4*i),12,1));
        MarshalModuleFieldSet(child,at,remainingFieldSets[15],data);if(kind==53) data.Pop();
    }
}
void EndianProduction(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,productionFieldSets[kind-52]);
    if(kind==52) {EndianProductionList(output,60,8,3);EndianProductionList(output,68,124,4);auto given=*reinterpret_cast<unsigned char**>(output+76);if(given) EndianProductionList(given,0,8,5);}
    else for(unsigned i=0;i<2;++i) {auto at=kind==53?*reinterpret_cast<unsigned char**>(output+8+4*i):output+20+12*i;if(at) EndianModuleFieldSet(at,remainingFieldSets[15]);}
}
void MarshalCreate(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,createFieldSets[kind-55],data);
    if(kind==56) {for(const auto& child:input.children) if(child.name=="ObjectFilter") MarshalUpgradeFilter(child,reinterpret_cast<ObjectFilter*>(output+12),data);MarshalWeakChildren(input,"Upgrade",output,124,data);}
    if(kind==58) {
        auto list=reinterpret_cast<NativeList*>(output+8);for(const auto& child:input.children) ++list->count;
        if(list->count) {auto refs=static_cast<U32*>(data.Push(&list->data,4,list->count));unsigned i=0;for(const auto& child:input.children) MarshalReference(data,refs+i++,child.text);data.Pop();}
    }
}
void EndianCreate(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,createFieldSets[kind-55]);
    if(kind==56) {EndianObjectFilter(reinterpret_cast<ObjectFilter*>(output+12));EndianWeakChildren(output,124);}
    if(kind==58) Swap(output+8);
}
void MarshalSlaved(const GameObjectChild& input,unsigned char* output,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,slavedFieldSet,data);
    for(const auto& child:input.children) {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+104),12,1));MarshalModuleFieldSet(child,at,remainingFieldSets[15],data);data.Pop();}
}
void EndianSlaved(unsigned char* output) {
    EndianModuleFieldSet(output,slavedFieldSet);auto at=*reinterpret_cast<unsigned char**>(output+104);if(at) EndianModuleFieldSet(at,remainingFieldSets[15]);
}
void MarshalVision(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,visionFieldSets[kind-61],data);
    if(kind==67) {
        for(const auto& child:input.children) if(child.name=="BroadcastObjectFilter") {auto at=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+32),112,1));MarshalUpgradeFilter(child,at,data);data.Pop();}
        for(const auto& child:input.children) if(child.name=="ObjectStatusValidation") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+36),56,1));MarshalModuleFieldSet(child,at,lifecycleFieldSets[6],data);data.Pop();}
    }
}
void EndianVision(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,visionFieldSets[kind-61]);
    if(kind==67) {auto filter=*reinterpret_cast<ObjectFilter**>(output+32);if(filter) EndianObjectFilter(filter);auto validation=*reinterpret_cast<unsigned char**>(output+36);if(validation) EndianModuleFieldSet(validation,lifecycleFieldSets[6]);}
}
void MarshalWeaponBehaviorList(const GameObjectChild& input,const char* name,unsigned char* output,unsigned offset,unsigned fields,Relo::Tracker& data) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(const auto& child:input.children) if(child.name==name) ++list->count;
    if(!list->count) return;
    auto at=static_cast<unsigned char*>(data.Push(&list->data,16,list->count));unsigned i=0;
    for(const auto& child:input.children) if(child.name==name) {
        auto entry=at+16*i++;MarshalModuleFieldSet(child,entry,weaponFieldSets[fields],data);
        if(fields==10) for(const auto& offsetNode:child.children) {auto vector=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(entry+8),12,1));MarshalModuleFieldSet(offsetNode,vector,weaponFieldSets[11],data);data.Pop();}
    }
    data.Pop();
}
void EndianWeaponBehaviorList(unsigned char* output,unsigned offset,unsigned fields) {
    auto list=reinterpret_cast<NativeList*>(output+offset);
    for(U32 i=0;i<list->count;++i) {auto entry=static_cast<unsigned char*>(list->data)+16*i;EndianModuleFieldSet(entry,weaponFieldSets[fields]);if(fields==10) {auto vector=*reinterpret_cast<unsigned char**>(entry+8);if(vector) EndianModuleFieldSet(vector,weaponFieldSets[11]);}}
    Swap(&list->count);
}
unsigned WeaponValidationOffset(unsigned kind) {return kind==69?20:kind==70?24:(kind==74 || kind==75)?32:0;}
void MarshalWeaponBehavior(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,weaponFieldSets[kind-69],data);
    if(kind==70) MarshalWeaponBehaviorList(input,"FireWeaponNugget",output,16,10,data);
    if(kind==75) {
        for(const auto& child:input.children) if(child.name=="Color") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+20),12,1));MarshalModuleFieldSet(child,at,weaponFieldSets[8],data);data.Pop();}
        MarshalWeaponBehaviorList(input,"EffectInfo",output,24,9,data);
    }
    auto offset=WeaponValidationOffset(kind);
    if(offset) for(const auto& child:input.children) if(child.name=="ObjectStatusValidation") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+offset),56,1));MarshalModuleFieldSet(child,at,lifecycleFieldSets[6],data);data.Pop();}
}
void EndianWeaponBehavior(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,weaponFieldSets[kind-69]);
    if(kind==70) EndianWeaponBehaviorList(output,16,10);
    if(kind==75) {auto color=*reinterpret_cast<unsigned char**>(output+20);if(color) EndianModuleFieldSet(color,weaponFieldSets[8]);EndianWeaponBehaviorList(output,24,9);}
    auto offset=WeaponValidationOffset(kind);if(offset) {auto at=*reinterpret_cast<unsigned char**>(output+offset);if(at) EndianModuleFieldSet(at,lifecycleFieldSets[6]);}
}
void MarshalBeamState(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,beamFieldSets[kind-77],data);
    if(kind==79) return;
    if(kind==78) MarshalModuleFieldSet(input,output,beamFieldSets[0],data);
    for(unsigned group=0;group<2;++group) {
        auto list=reinterpret_cast<NativeList*>(output+20+8*group);const char* name=group==0?"LaserEndParticleSystem":"LaserStartParticleSystem";
        for(const auto& child:input.children) if(child.name==name) ++list->count;
        if(list->count) {auto refs=static_cast<U32*>(data.Push(&list->data,4,list->count));unsigned i=0;for(const auto& child:input.children) if(child.name==name) MarshalReference(data,refs+i++,child.text);data.Pop();}
    }
    for(const auto& child:input.children) if(child.name=="EndOffset") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+36),12,1));MarshalModuleFieldSet(child,at,remainingFieldSets[15],data);data.Pop();}
    for(const auto& child:input.children) if(child.name=="ObjectStatusValidation") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+40),56,1));MarshalModuleFieldSet(child,at,lifecycleFieldSets[6],data);data.Pop();}
}
void EndianBeamState(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,beamFieldSets[kind-77]);if(kind==79) return;
    if(kind==78) EndianModuleFieldSet(output,beamFieldSets[0]);
    Swap(output+20);Swap(output+28);
    auto vector=*reinterpret_cast<unsigned char**>(output+36);if(vector) EndianModuleFieldSet(vector,remainingFieldSets[15]);
    auto validation=*reinterpret_cast<unsigned char**>(output+40);if(validation) EndianModuleFieldSet(validation,lifecycleFieldSets[6]);
}
int MotionMoveIndex(U32 type) {for(unsigned i=0;i<4;++i) if(motionMoveChoices[i].type==type) return static_cast<int>(i);return -1;}
const char* motionShapeNames[]={"Sphere","Capsule","Box","Cylinder","Triangle"};
const unsigned motionShapeSizes[]={12,16,24,20,48};
void MarshalMotion(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,motionFieldSets[kind-80],data);
    if(kind==80) for(const auto& wrapper:input.children) if(wrapper.name=="Movement" && wrapper.children.size()==1) {
        const auto& move=wrapper.children[0];auto id=move.attributes.find("TypeId");U32 type=id==move.attributes.end()?0:ParseUnsigned(id->second);int index=MotionMoveIndex(type);
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+24),index<0?4:motionMoveChoices[index].size,1));
        if(index>=0 && index<3) MarshalModuleFieldSet(move,at,motionFieldSets[4+index],data);
        if(index==2) {auto list=reinterpret_cast<NativeList*>(at+16);list->count=static_cast<U32>(move.children.size());if(list->count) {auto refs=static_cast<U32*>(data.Push(&list->data,4,list->count));for(U32 i=0;i<list->count;++i) MarshalReference(data,refs+i,move.children[i].text);data.Pop();}}
        *reinterpret_cast<U32*>(at)=index<0?0:type;data.Pop();
    }
    if(kind==81) for(const auto& volume:input.children) if(volume.name=="Volume") {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+28),76,1));MarshalModuleFieldSet(volume,at,motionFieldSets[7],data);
        for(unsigned group=0;group<5;++group) {
            auto list=reinterpret_cast<NativeList*>(at+32+8*group);for(const auto& shape:volume.children) if(shape.name==motionShapeNames[group]) ++list->count;
            if(!list->count) continue;
            auto shapes=static_cast<unsigned char*>(data.Push(&list->data,motionShapeSizes[group],list->count));unsigned i=0;
            for(const auto& shape:volume.children) if(shape.name==motionShapeNames[group]) {
                auto entry=shapes+motionShapeSizes[group]*i++;MarshalModuleFieldSet(shape,entry,motionFieldSets[8+group],data);
                for(unsigned transform=0;transform<2;++transform) for(const auto& child:shape.children) if(child.name==(transform==0?"Translation":"Rotation")) {auto vector=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(entry+4*transform),transform==0?12:16,1));MarshalModuleFieldSet(child,vector,motionFieldSets[13+transform],data);data.Pop();}
            }
            data.Pop();
        }
        data.Pop();
    }
}
void EndianMotion(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,motionFieldSets[kind-80]);
    if(kind==80) {auto at=*reinterpret_cast<unsigned char**>(output+24);if(at) {int index=MotionMoveIndex(*reinterpret_cast<U32*>(at));if(index>=0 && index<3) EndianModuleFieldSet(at,motionFieldSets[4+index]);if(index==2) Swap(at+16);Swap(at);}}
    if(kind==81) {auto at=*reinterpret_cast<unsigned char**>(output+28);if(at) {
        EndianModuleFieldSet(at,motionFieldSets[7]);
        for(unsigned group=0;group<5;++group) {auto list=reinterpret_cast<NativeList*>(at+32+8*group);
            for(U32 i=0;i<list->count;++i) {auto entry=static_cast<unsigned char*>(list->data)+motionShapeSizes[group]*i;EndianModuleFieldSet(entry,motionFieldSets[8+group]);for(unsigned transform=0;transform<2;++transform) {auto vector=*reinterpret_cast<unsigned char**>(entry+4*transform);if(vector) EndianModuleFieldSet(vector,motionFieldSets[13+transform]);}}
            Swap(&list->count);
        }
    }}
}
void TokenizeMotionMove(U32* input,void** slot,Relo::Tracker& procedures);
void MarshalFlight(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,flightFieldSets[kind-84],data);
    if(kind==86) MarshalModuleFieldSet(input,output,flightFieldSets[1],data);
}
void EndianFlight(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,flightFieldSets[kind-84]);if(kind==86) EndianModuleFieldSet(output,flightFieldSets[1]);
}
void MarshalReactiveList(const GameObjectChild& input,const char* name,unsigned char* output,unsigned offset,unsigned fields,unsigned stride,Relo::Tracker& data) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(const auto& child:input.children) if(child.name==name) ++list->count;
    if(!list->count) return;auto at=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));unsigned i=0;for(const auto& child:input.children) if(child.name==name) MarshalModuleFieldSet(child,at+stride*i++,reactiveFieldSets[fields],data);data.Pop();
}
void EndianReactiveList(unsigned char* output,unsigned offset,unsigned fields,unsigned stride) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(U32 i=0;i<list->count;++i) EndianModuleFieldSet(static_cast<unsigned char*>(list->data)+stride*i,reactiveFieldSets[fields]);Swap(&list->count);
}
void MarshalReactive(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,reactiveFieldSets[kind-87],data);
    if(kind==87) MarshalReactiveList(input,"Data",output,8,4,96,data);
    if(kind==90) {
        for(const auto& child:input.children) if(child.name=="WeaponOffset") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+80),12,1));MarshalModuleFieldSet(child,at,remainingFieldSets[15],data);data.Pop();}
        for(const auto& child:input.children) if(child.name=="DieMuxData") MarshalModuleFieldSet(child,output+84,dieFieldSets[7],data);
        MarshalReactiveList(input,"WeaponFireProbability",output,128,5,12,data);
    }
    if(kind==88 || kind==90) MarshalUpgrade(input,output,30,data);
}
void EndianReactive(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,reactiveFieldSets[kind-87]);
    if(kind==87) EndianReactiveList(output,8,4,96);
    if(kind==90) {auto at=*reinterpret_cast<unsigned char**>(output+80);if(at) EndianModuleFieldSet(at,remainingFieldSets[15]);EndianModuleFieldSet(output+84,dieFieldSets[7]);EndianReactiveList(output,128,5,12);}
    if(kind==88 || kind==90) EndianUpgrade(output,30);
}
// SortedListHelper.Sort builds an EASTL pointer permutation, then exchanges
// bookmarks before each 72-byte record swap. XML and import creation order stay
// unchanged. Keep the inverse map as the original ptr3 array does.
void SortWeaponData(unsigned char* output,U32 count,Relo::Tracker& data) {
    std::vector<int> keys(count);for(U32 i=0;i<count;++i) std::memcpy(&keys[i],output+72*i,4);
    auto order=WeaponSlotOrder(keys);std::vector<U32> current(count),position(count);
    for(U32 i=0;i<count;++i) current[i]=position[i]=i;
    for(U32 i=0;i<count;++i) {
        U32 j=position[order[i]];if(i==j) continue;
        auto a=output+72*j,b=output+72*i;
        if(!data.ExchangePointers(a,b)) throw std::logic_error("WeaponData outside current Tracker block");
        unsigned char temporary[72];std::memcpy(temporary,b,72);std::memcpy(b,a,72);std::memcpy(a,temporary,72);
        std::swap(current[i],current[j]);position[current[i]]=i;position[current[j]]=j;
    }
}
void MarshalWeaponSetNode(const GameObjectChild& input,unsigned char* output,unsigned fields,Relo::Tracker& data);
void MarshalWeaponSetList(const GameObjectChild& input,const char* name,unsigned char* output,unsigned offset,unsigned fields,unsigned stride,Relo::Tracker& data) {
    auto list=reinterpret_cast<NativeList*>(output+offset);for(const auto& child:input.children) if(child.name==name) ++list->count;
    if(!list->count) return;
    auto at=static_cast<unsigned char*>(data.Push(&list->data,stride,list->count));unsigned i=0;
    for(const auto& child:input.children) if(child.name==name) MarshalWeaponSetNode(child,at+stride*i++,fields,data);
    if(fields==5) SortWeaponData(at,list->count,data);
    data.Pop();
}
void MarshalWeaponSetNode(const GameObjectChild& input,unsigned char* output,unsigned fields,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,weaponSetFieldSets[fields],data);
    if(fields==0) {
        const char* names[]={"WeaponSlotHardpoint","WeaponSlotTurret","WeaponSlotHierarchicalTurret","WeaponSlotContained"};const unsigned strides[]={28,124,128,28};
        for(unsigned i=0;i<4;++i) MarshalWeaponSetList(input,names[i],output,8+8*i,1+i,strides[i],data);
    }
    if(fields==1) MarshalWeaponSetList(input,"Weapon",output,16,5,72,data);
    if(fields==2) {
        for(const auto& child:input.children) if(child.name=="TurretSettings") MarshalWeaponSetNode(child,output+28,6,data);
        MarshalWeaponSetNode(input,output,1,data);
    }
    if(fields==3) MarshalWeaponSetNode(input,output,2,data);
    if(fields==4) MarshalWeaponSetNode(input,output,1,data);
    if(fields==6) for(const auto& child:input.children) if(child.name=="TurretAITargetChooserData") {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+88),44,1));MarshalWeaponSetNode(child,at,7,data);data.Pop();
    }
    if(fields==7) MarshalWeaponSetNode(input,output,8,data);
    if(fields==8) MarshalWeaponSetList(input,"ObjectVisionRange",output,20,9,120,data);
    if(fields==9) for(const auto& child:input.children) if(child.name=="Filter") {
        ObjectFilterInput filter;filter.attributes=child.attributes;
        for(const auto& thing:child.children) if(thing.name=="IncludeThing") filter.includeThings.push_back(thing.text);else filter.excludeThings.push_back(thing.text);
        MarshalObjectFilter(filter,reinterpret_cast<ObjectFilter*>(output+8),data);
    }
}
void EndianWeaponSetNode(unsigned char* output,unsigned fields) {
    EndianModuleFieldSet(output,weaponSetFieldSets[fields]);
    auto list=[&](unsigned offset,unsigned child,unsigned stride) {
        auto values=reinterpret_cast<NativeList*>(output+offset);for(U32 i=0;i<values->count;++i) EndianWeaponSetNode(static_cast<unsigned char*>(values->data)+stride*i,child);Swap(&values->count);
    };
    if(fields==0) {const unsigned strides[]={28,124,128,28};for(unsigned i=0;i<4;++i) list(8+8*i,1+i,strides[i]);}
    if(fields==1) {Swap(output);list(16,5,72);}
    if(fields==2) {EndianWeaponSetNode(output+28,6);EndianWeaponSetNode(output,1);}
    if(fields==3) EndianWeaponSetNode(output,2);
    if(fields==4) EndianWeaponSetNode(output,1);
    if(fields==6) {auto chooser=*reinterpret_cast<unsigned char**>(output+88);if(chooser) EndianWeaponSetNode(chooser,7);}
    if(fields==7) EndianWeaponSetNode(output,8);
    if(fields==8) list(20,9,120);
    if(fields==9) EndianObjectFilter(reinterpret_cast<ObjectFilter*>(output+8));
}
void MarshalHealing(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,healingFieldSets[kind-92],data);
    if(kind==92) {
        for(const auto& child:input.children) if(child.name=="ObjectStatusValidation") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+100),56,1));MarshalModuleFieldSet(child,at,lifecycleFieldSets[6],data);data.Pop();}
        MarshalUpgrade(input,output,30,data);
    }
    if(kind==93) for(const auto& child:input.children) {if(child.name=="PercentOfBuildCostToRebuildInfo") MarshalModuleFieldSet(child,output+16,healingFieldSets[5],data);if(child.name=="SelfAudio") MarshalModuleFieldSet(child,output+32,healingFieldSets[6],data);}
    if(kind==94) for(const auto& child:input.children) if(child.name=="Die") {auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+28),44,1));MarshalModuleFieldSet(child,at,dieFieldSets[7],data);data.Pop();}
    if(kind==95) {
        for(const auto& child:input.children) if(child.name=="ObjectFilter") {auto at=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+112),112,1));MarshalUpgradeFilter(child,at,data);data.Pop();}
        MarshalModuleFieldSet(input,output,healingFieldSets[4],data);
    }
}
void EndianHealing(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,healingFieldSets[kind-92]);
    if(kind==92) {auto at=*reinterpret_cast<unsigned char**>(output+100);if(at) EndianModuleFieldSet(at,lifecycleFieldSets[6]);EndianUpgrade(output,30);}
    if(kind==93) {EndianModuleFieldSet(output+16,healingFieldSets[5]);EndianModuleFieldSet(output+32,healingFieldSets[6]);}
    if(kind==94) {auto at=*reinterpret_cast<unsigned char**>(output+28);if(at) EndianModuleFieldSet(at,dieFieldSets[7]);}
    if(kind==95) {auto at=*reinterpret_cast<ObjectFilter**>(output+112);if(at) EndianObjectFilter(at);EndianModuleFieldSet(output,healingFieldSets[4]);}
}
void MarshalCrate(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    unsigned group=kind-96;MarshalModuleFieldSet(input,output,crateFieldSets[group],data);
    if(group>0 && group<7) MarshalModuleFieldSet(input,output,crateFieldSets[0],data);
    if(group==7) {
        auto list=reinterpret_cast<NativeList*>(output+36);for(const auto& child:input.children) if(child.name=="CrateList") ++list->count;
        if(list->count) {auto at=static_cast<U32*>(data.Push(&list->data,4,list->count));unsigned i=0;for(const auto& child:input.children) if(child.name=="CrateList") MarshalReference(data,at+i++,child.text);data.Pop();}
        for(const auto& child:input.children) if(child.name=="BlockingUnits") {auto at=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+44),112,1));MarshalUpgradeFilter(child,at,data);data.Pop();}
    }
}
void EndianCrate(unsigned char* output,unsigned kind) {
    unsigned group=kind-96;EndianModuleFieldSet(output,crateFieldSets[group]);
    if(group>0 && group<7) EndianModuleFieldSet(output,crateFieldSets[0]);
    if(group==7) {Swap(output+36);auto at=*reinterpret_cast<ObjectFilter**>(output+44);if(at) EndianObjectFilter(at);}
}
// Concrete fields precede the AI base, matching original reference/allocation order.
void MarshalAIUpdate(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    unsigned group=kind==291?0:kind-104;
    if(kind==291) MarshalModuleFieldSet(input,output,dozerAIUpdateFieldSet,data);
    else if(group) MarshalModuleFieldSet(input,output,aiUpdateFieldSets[group],data);
    MarshalModuleFieldSet(input,output,aiUpdateFieldSets[0],data);
    for(const auto& child:input.children) if(child.name=="UnitAITargetChooserData") {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+68),40,1));
        MarshalModuleFieldSet(child,at,aiUpdateFieldSets[8],data);
        MarshalWeaponSetNode(child,at,8,data);data.Pop();
    }
}
void EndianAIUpdate(unsigned char* output,unsigned kind) {
    unsigned group=kind==291?0:kind-104;
    if(kind==291) EndianModuleFieldSet(output,dozerAIUpdateFieldSet);
    else if(group) EndianModuleFieldSet(output,aiUpdateFieldSets[group]);
    EndianModuleFieldSet(output,aiUpdateFieldSets[0]);
    auto chooser=*reinterpret_cast<unsigned char**>(output+68);
    if(chooser) EndianWeaponSetNode(chooser,8);
}
void MarshalEconomy(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    unsigned group=kind==repairDockKind?0:kind-112;MarshalModuleFieldSet(input,output,economyFieldSets[group],data);
    if(group==0 || group==3 || group==4) {
        if(group) MarshalModuleFieldSet(input,output,economyFieldSets[0],data);
        for(const auto& child:input.children) if(child.name=="ForVoiceRetreatThisIsASafeHarborToObjectFilter") {
            auto at=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+20),112,1));MarshalUpgradeFilter(child,at,data);data.Pop();
        }
    }
    if(group==2) for(const auto& child:input.children) if(child.name=="ObjectStatusValidation") {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+16),56,1));MarshalModuleFieldSet(child,at,lifecycleFieldSets[6],data);data.Pop();
    }
    if(group==6) {
        // The original visits vectors before its inline Filter, independent of XML order.
        for(const auto& child:input.children) if(child.name=="RotationSpeed") MarshalModuleFieldSet(child,output+36,economyFieldSets[7],data);
        for(const auto& child:input.children) if(child.name=="UnitSpacing") MarshalModuleFieldSet(child,output+48,economyFieldSets[7],data);
        for(const auto& child:input.children) if(child.name=="Filter") MarshalUpgradeFilter(child,reinterpret_cast<ObjectFilter*>(output+60),data);
    }
    if(kind==repairDockKind) {
        MarshalModuleFieldSet(input,output,repairDockFieldSet,data);
        auto repair=reinterpret_cast<RepairDockModuleData*>(output);
        if(!std::isfinite(repair->framesForFullHeal) || repair->framesForFullHeal<=0 || repair->numberApproachPositions<1)
            throw std::invalid_argument("RepairDockUpdate requires finite positive FramesForFullHeal and NumberApproachPositions >= 1 after expression evaluation.");
    }
}
void EndianEconomy(unsigned char* output,unsigned kind) {
    unsigned group=kind==repairDockKind?0:kind-112;EndianModuleFieldSet(output,economyFieldSets[group]);
    if(group==0 || group==3 || group==4) {
        if(group) EndianModuleFieldSet(output,economyFieldSets[0]);
        auto filter=*reinterpret_cast<ObjectFilter**>(output+20);if(filter) EndianObjectFilter(filter);
    }
    if(group==2) {auto status=*reinterpret_cast<unsigned char**>(output+16);if(status) EndianModuleFieldSet(status,lifecycleFieldSets[6]);}
    if(group==6) {EndianModuleFieldSet(output+36,economyFieldSets[7]);EndianModuleFieldSet(output+48,economyFieldSets[7]);EndianObjectFilter(reinterpret_cast<ObjectFilter*>(output+60));}
    if(kind==repairDockKind) EndianModuleFieldSet(output,repairDockFieldSet);
}
void MarshalAssistFilter(const GameObjectChild& child,unsigned char* output,unsigned offset,Relo::Tracker& data) {
    auto at=static_cast<ObjectFilter*>(data.Push(reinterpret_cast<void**>(output+offset),112,1));MarshalUpgradeFilter(child,at,data);data.Pop();
}
void EndianAssistFilter(unsigned char* output,unsigned offset) {auto at=*reinterpret_cast<ObjectFilter**>(output+offset);if(at) EndianObjectFilter(at);}
void MarshalAssist(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    unsigned group=kind-119;MarshalModuleFieldSet(input,output,assistFieldSets[group],data);
    if(group==0) {
        auto list=reinterpret_cast<NativeList*>(output+8);for(const auto& child:input.children) if(child.name=="HintObject") ++list->count;
        if(list->count) {
            auto at=static_cast<unsigned char*>(data.Push(&list->data,20,list->count));unsigned i=0;
            for(const auto& child:input.children) if(child.name=="HintObject") {
                auto row=at+20*i++;MarshalModuleFieldSet(child,row,assistFieldSets[4],data);
                for(const auto& filter:child.children) if(filter.name=="ObjectFilter") MarshalAssistFilter(filter,row,12,data);
            }data.Pop();
        }
        for(const auto& child:input.children) if(child.name=="PlacementRestriction") {
            auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+16),8,1));MarshalModuleFieldSet(child,at,assistFieldSets[5],data);
            for(const auto& filter:child.children) if(filter.name=="RestrictedObjectFilter") MarshalAssistFilter(filter,at,4,data);data.Pop();
        }
        for(const auto& child:input.children) if(child.name=="SuggestedOffset") MarshalModuleFieldSet(child,output+20,assistFieldSets[6],data);
    }
    if(group==1) for(const auto& child:input.children) if(child.name=="RequirementObjectFilter") MarshalAssistFilter(child,output,16,data);
}
void EndianAssist(unsigned char* output,unsigned kind) {
    unsigned group=kind-119;EndianModuleFieldSet(output,assistFieldSets[group]);
    if(group==0) {
        auto list=reinterpret_cast<NativeList*>(output+8);
        for(U32 i=0;i<list->count;++i) {auto row=static_cast<unsigned char*>(list->data)+20*i;EndianModuleFieldSet(row,assistFieldSets[4]);EndianAssistFilter(row,12);}Swap(&list->count);
        auto restriction=*reinterpret_cast<unsigned char**>(output+16);if(restriction) {EndianModuleFieldSet(restriction,assistFieldSets[5]);EndianAssistFilter(restriction,4);}
        EndianModuleFieldSet(output+20,assistFieldSets[6]);
    }
    if(group==1) EndianAssistFilter(output,16);
}
void MarshalSpawnUpdate(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,spawnFieldSets[kind-123],data);
    if(kind!=123) return;
    // Original schema order allocates Die, then the SpawnTemplate list, then Upgrade dependencies.
    for(const auto& child:input.children) if(child.name=="Die") {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+64),44,1));
        MarshalModuleFieldSet(child,at,dieFieldSets[7],data);data.Pop();
    }
    auto list=reinterpret_cast<NativeList*>(output+68);
    for(const auto& child:input.children) if(child.name=="SpawnTemplate") ++list->count;
    if(list->count) {
        auto at=static_cast<U32*>(data.Push(&list->data,4,list->count));unsigned i=0;
        for(const auto& child:input.children) if(child.name=="SpawnTemplate") at[i++]=HashAssetId(child.text);
        data.Pop();
    }
    MarshalUpgrade(input,output,30,data);
}
void EndianSpawnUpdate(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,spawnFieldSets[kind-123]);
    if(kind!=123) return;
    auto die=*reinterpret_cast<unsigned char**>(output+64);if(die) EndianModuleFieldSet(die,dieFieldSets[7]);
    auto list=reinterpret_cast<NativeList*>(output+68);
    for(U32 i=0;i<list->count;++i) Swap(static_cast<U32*>(list->data)+i);Swap(&list->count);
    EndianUpgrade(output,30);
}

void MarshalQueueParking(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,queueParkingFieldSets[kind-128],data);
    if(kind!=129) return;
    for(const auto& child:input.children) if(child.name=="CanParkHereFilter") MarshalAssistFilter(child,output,44,data);
    for(const auto& child:input.children) if(child.name=="DefaultRallyPointOffset") {
        auto at=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+48),12,1));
        MarshalModuleFieldSet(child,at,assistFieldSets[6],data);data.Pop();
    }
}
void EndianQueueParking(unsigned char* output,unsigned kind) {
    EndianModuleFieldSet(output,queueParkingFieldSets[kind-128]);
    if(kind!=129) return;
    EndianAssistFilter(output,44);auto vector=*reinterpret_cast<unsigned char**>(output+48);
    if(vector) EndianModuleFieldSet(vector,assistFieldSets[6]);
}

#include "GameObjectEconomyPowerRuntime.h"
#include "GameObjectStructureWallRuntime.h"

#include "GameObjectCombatResponseRuntime.h"

#include "GameObjectStatusEffectsRuntime.h"
#include "GameObjectStealthVisionRuntime.h"

#include "GameObjectTiberiumRuntime.h"

#include "GameObjectWallEncloseRuntime.h"

#include "GameObjectResourceAlliesRuntime.h"

#include "GameObjectMotionCleanupRuntime.h"


#include "GameObjectSpecialPowerBaseRuntime.h"

#include "GameObjectSpecialPowerDerivedRuntime.h"

#include "GameObjectPowerRoutingRuntime.h"

#include "GameObjectSpecialAbilityRuntime.h"
#include "GameObjectAbilityWeaponRuntime.h"


#include "GameObjectPowerCreationRuntime.h"
#include "GameObjectAbilityMovementRuntime.h"
#include "GameObjectAbilityStateRuntime.h"
#include "GameObjectAbilityRoutingRuntime.h"
#include "GameObjectAbilityResourceRuntime.h"



#include "GameObjectAbilityToggleRuntime.h"

#include "GameObjectAbilityFinishRuntime.h"

#include "GameObjectAttachRuntime.h"

#include "GameObjectAreaRuntime.h"

#include "GameObjectSupportRuntime.h"

#include "GameObjectObjectControlRuntime.h"

#include "GameObjectSlowDeathRuntime.h"

#include "GameObjectContainCoreRuntime.h"
#include "GameObjectContainRemainingRuntime.h"
#include "GameObjectDrawBeamRuntime.h"


#include "GameObjectDrawModelConditionsRuntime.h"
#include "GameObjectDrawAnimationRuntime.h"
#include "GameObjectDrawDynamicsRuntime.h"
#include "GameObjectDrawModelRuntime.h"
