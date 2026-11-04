// Included by GameObject.cpp; definitions remain in its namespace and original order.
void EndianModule(U32* input) {
    auto choice=FindModule(input[0]);
    if(choice && choice->kind>=286 && choice->kind<=290) EndianDrawModel(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=198 && choice->kind<=199) EndianPowerCreation(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=213 && choice->kind<=214) EndianAbilityResource(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=283 && choice->kind<=285) EndianDrawBeam(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=278 && choice->kind<=282) EndianContainRemaining(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=270 && choice->kind<=277) EndianContainCore(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=265 && choice->kind<=269) EndianSlowDeath(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=252 && choice->kind<=264) EndianObjectControl(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=244 && choice->kind<=251) EndianSupport(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=238 && choice->kind<=243) EndianArea(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=225 && choice->kind<=237) EndianModuleFieldSet(reinterpret_cast<unsigned char*>(input),utilityFieldSets[choice->kind-225]);
    if(choice && choice->kind>=220 && choice->kind<=224) EndianAttach(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=216 && choice->kind<=219) EndianAbilityFinish(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind==215) EndianAbilityToggle(reinterpret_cast<unsigned char*>(input));
    if(choice && choice->kind>=211 && choice->kind<=212) EndianAbilityRouting(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=204 && choice->kind<=210) EndianAbilityState(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=200 && choice->kind<=203) EndianAbilityMovement(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=194 && choice->kind<=197) EndianAbilityWeapon(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=191 && choice->kind<=193) EndianSpecialAbility(reinterpret_cast<unsigned char*>(input));
    if(choice && choice->kind>=188 && choice->kind<=190) EndianPowerRouting(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=165 && choice->kind<=187) EndianSpecialPowerDerived(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=162 && choice->kind<=164) EndianSpecialPowerBase(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=159 && choice->kind<=161) EndianMotionCleanup(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=156 && choice->kind<=158) EndianModuleFieldSet(reinterpret_cast<unsigned char*>(input),modelConditionFieldSets[choice->kind-156]);
    if(choice && choice->kind>=154 && choice->kind<=155) EndianModuleFieldSet(reinterpret_cast<unsigned char*>(input),stateEventFieldSets[choice->kind-154]);
    if(choice && choice->kind>=152 && choice->kind<=153) EndianResourceAllies(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=150 && choice->kind<=151) EndianWallEnclose(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=147 && choice->kind<=149) EndianTiberium(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=145 && choice->kind<=146) EndianStealthVision(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=143 && choice->kind<=144) EndianStatusEffects(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=140 && choice->kind<=142) EndianCombatResponse(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=137 && choice->kind<=139) EndianStructureWall(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=133 && choice->kind<=136) EndianEconomyPower(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=128 && choice->kind<=129) EndianQueueParking(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=130 && choice->kind<=132) EndianModuleFieldSet(reinterpret_cast<unsigned char*>(input),buildingStructureFieldSets[choice->kind-130]);
    if(choice && choice->kind>=123 && choice->kind<=125) EndianSpawnUpdate(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=119 && choice->kind<=122) EndianAssist(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=112 && choice->kind<=118) EndianEconomy(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=104 && choice->kind<=111) EndianAIUpdate(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=96 && choice->kind<=103) EndianCrate(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=92 && choice->kind<=95) EndianHealing(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind==91) EndianWeaponSetNode(reinterpret_cast<unsigned char*>(input),0);
    if(choice && choice->kind && choice->kind<=2) {
        auto bytes=reinterpret_cast<unsigned char*>(input);
        if(choice->kind==2) EndianModuleFields(bytes,ShieldBodyModuleDataModuleFields);
        EndianModuleFields(bytes,ActiveBodyModuleDataModuleFields);
        auto list=reinterpret_cast<NativeList*>(bytes+64);
        for(U32 i=0;i<list->count;++i) {auto at=static_cast<unsigned char*>(list->data)+12*i;Swap(at);Swap(at+4);}
        Swap(&list->count);
    }
    if(choice && choice->kind>=3 && choice->kind<13) {
        auto bytes=reinterpret_cast<unsigned char*>(input);EndianModuleFieldSet(bytes,moduleFieldSets[choice->kind]);
        if(choice->kind==9 || choice->kind==10) {
            auto list=reinterpret_cast<NativeList*>(bytes+(choice->kind==9?12:8));unsigned stride=choice->kind==9?24:124,fields=choice->kind==9?11:12;
            for(U32 j=0;j<list->count;++j) EndianModuleFieldSet(static_cast<unsigned char*>(list->data)+stride*j,moduleFieldSets[fields]);
            Swap(&list->count);
        }
        if(choice->kind==8) {auto list=reinterpret_cast<NativeList*>(bytes+16);for(U32 i=0;i<list->count;++i) Swap(static_cast<U32*>(list->data)+i);Swap(&list->count);}
    }
    if(choice && choice->kind>=13 && choice->kind<=16) EndianSoundSelector(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=17 && choice->kind<=22) EndianLifecycle(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=23 && choice->kind<=29) EndianDie(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=87 && choice->kind<=90) EndianReactive(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=84 && choice->kind<=86) EndianFlight(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=80 && choice->kind<=83) EndianMotion(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=77 && choice->kind<=79) EndianBeamState(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=69 && choice->kind<=76) EndianWeaponBehavior(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=61 && choice->kind<=68) EndianVision(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind==59) EndianSlaved(reinterpret_cast<unsigned char*>(input));
    if(choice && choice->kind>=55 && choice->kind<=58) EndianCreate(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && choice->kind>=52 && choice->kind<=54) EndianProduction(reinterpret_cast<unsigned char*>(input),choice->kind);
    if(choice && ((choice->kind>=30 && choice->kind<=51) || choice->kind==60)) EndianUpgrade(reinterpret_cast<unsigned char*>(input),choice->kind);
    Swap(input);Swap(input+1);
}

void MarshalModule(const GameObjectChild& input,void** slot,Relo::Tracker& data) {
    auto found=input.attributes.find("TypeId");U32 type=found==input.attributes.end()?0:ParseUnsigned(found->second);
    auto choice=FindModule(type);
    if(!choice) for(auto known:knownModuleTypes) if(known==type) throw std::invalid_argument("Unsupported polymorphic GameObject module TypeId");
    // New queue/building groups dispatch after the common allocation below.
    auto target=static_cast<U32*>(data.Push(slot,choice?choice->size:8,1));
    if(choice && choice->kind>=198 && choice->kind<=199) MarshalPowerCreation(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=213 && choice->kind<=214) MarshalAbilityResource(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=286 && choice->kind<=290) MarshalDrawModel(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=283 && choice->kind<=285) MarshalDrawBeam(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=278 && choice->kind<=282) MarshalContainRemaining(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=270 && choice->kind<=277) MarshalContainCore(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=265 && choice->kind<=269) MarshalSlowDeath(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=252 && choice->kind<=264) MarshalObjectControl(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=244 && choice->kind<=251) MarshalSupport(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=238 && choice->kind<=243) MarshalArea(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=225 && choice->kind<=237) MarshalModuleFieldSet(input,reinterpret_cast<unsigned char*>(target),utilityFieldSets[choice->kind-225],data);
    if(choice && choice->kind>=220 && choice->kind<=224) MarshalAttach(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=216 && choice->kind<=219) MarshalAbilityFinish(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind==215) MarshalAbilityToggle(input,reinterpret_cast<unsigned char*>(target),data);
    if(choice && choice->kind>=211 && choice->kind<=212) MarshalAbilityRouting(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=204 && choice->kind<=210) MarshalAbilityState(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=200 && choice->kind<=203) MarshalAbilityMovement(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=194 && choice->kind<=197) MarshalAbilityWeapon(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=191 && choice->kind<=193) MarshalSpecialAbility(input,reinterpret_cast<unsigned char*>(target),data);
    if(choice && choice->kind>=188 && choice->kind<=190) MarshalPowerRouting(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=165 && choice->kind<=187) MarshalSpecialPowerDerived(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=162 && choice->kind<=164) MarshalSpecialPowerBase(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=159 && choice->kind<=161) MarshalMotionCleanup(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=156 && choice->kind<=158) MarshalModuleFieldSet(input,reinterpret_cast<unsigned char*>(target),modelConditionFieldSets[choice->kind-156],data);
    if(choice && choice->kind>=154 && choice->kind<=155) MarshalModuleFieldSet(input,reinterpret_cast<unsigned char*>(target),stateEventFieldSets[choice->kind-154],data);
    if(choice && choice->kind>=152 && choice->kind<=153) MarshalResourceAllies(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=150 && choice->kind<=151) MarshalWallEnclose(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=147 && choice->kind<=149) MarshalTiberium(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=145 && choice->kind<=146) MarshalStealthVision(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=143 && choice->kind<=144) MarshalStatusEffects(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=140 && choice->kind<=142) MarshalCombatResponse(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=137 && choice->kind<=139) MarshalStructureWall(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=133 && choice->kind<=136) MarshalEconomyPower(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=128 && choice->kind<=129) MarshalQueueParking(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=130 && choice->kind<=132) MarshalModuleFieldSet(input,reinterpret_cast<unsigned char*>(target),buildingStructureFieldSets[choice->kind-130],data);
    if(choice && choice->kind && choice->kind<=2) MarshalBody(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=3 && choice->kind<13) MarshalClientUpdate(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=13 && choice->kind<=16) MarshalSoundSelector(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=17 && choice->kind<=22) MarshalLifecycle(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=23 && choice->kind<=29) MarshalDie(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind==127) MarshalModuleFieldSet(input,reinterpret_cast<unsigned char*>(target),crateWatcherFieldSets[0],data);
    if(choice && choice->kind>=123 && choice->kind<=125) MarshalSpawnUpdate(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=119 && choice->kind<=122) MarshalAssist(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=112 && choice->kind<=118) MarshalEconomy(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=104 && choice->kind<=111) MarshalAIUpdate(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=96 && choice->kind<=103) MarshalCrate(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=92 && choice->kind<=95) MarshalHealing(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind==91) MarshalWeaponSetNode(input,reinterpret_cast<unsigned char*>(target),0,data);
    if(choice && choice->kind>=87 && choice->kind<=90) MarshalReactive(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=84 && choice->kind<=86) MarshalFlight(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=80 && choice->kind<=83) MarshalMotion(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=77 && choice->kind<=79) MarshalBeamState(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=69 && choice->kind<=76) MarshalWeaponBehavior(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=61 && choice->kind<=68) MarshalVision(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind==59) MarshalSlaved(input,reinterpret_cast<unsigned char*>(target),data);
    if(choice && choice->kind>=55 && choice->kind<=58) MarshalCreate(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && choice->kind>=52 && choice->kind<=54) MarshalProduction(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    if(choice && ((choice->kind>=30 && choice->kind<=51) || choice->kind==60)) MarshalUpgrade(input,reinterpret_cast<unsigned char*>(target),choice->kind,data);
    target[0]=choice?type:0;
    auto id=input.attributes.find("id");if(id!=input.attributes.end()) target[1]=HashAssetId(id->second);
    data.Pop();
}
void TokenizeModuleTable(U32* input,const ModuleLayout& layout,void** slot,Relo::Tracker& procedures);
void TokenizeBeamShader(U32* input,void** slot,Relo::Tracker& procedures) {
    if(!input)return;auto wrapper=static_cast<Procedure*>(procedures.Push(slot,12,1));wrapper->name=2;wrapper->type=input[0];auto layout=BeamShaderLayout(BeamShaderKind(input[0]));if(layout)TokenizeModuleTable(input,*layout,&wrapper->offset,procedures);procedures.Pop();
}
void TokenizeModuleTable(U32* input,const ModuleLayout& layout,void** slot,Relo::Tracker& procedures) {
    auto table=static_cast<Procedure*>(procedures.Push(slot,12,layout.count+1));
    for(unsigned i=0;i<layout.count;++i) {
        const auto& t=layout.tokens[i];table[i].name=t.name;table[i].type=t.type;
        if(t.mode==10) {
            auto list=reinterpret_cast<NativeList*>(reinterpret_cast<unsigned char*>(input)+t.offset);auto table2=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,list->count+1));table2[0].name=1820487795u;table2[0].type=1564774360u;procedures.BookmarkMemberOffset(list,&table2[0].offset);for(U32 j=0;j<list->count;++j){table2[j+1].name=2974916843u;TokenizeBeamShader(static_cast<U32**>(list->data)[j],&table2[j+1].offset,procedures);}procedures.Pop();
        } else if(t.mode==1 || t.mode==3 || t.mode==6) {
            auto list=reinterpret_cast<NativeList*>(reinterpret_cast<unsigned char*>(input)+t.offset);
            auto count=static_cast<Procedure*>(procedures.Push(&table[i].offset,12,list->count+1));
            count[0].name=1820487795u;count[0].type=1564774360u;procedures.BookmarkMemberOffset(list,&count[0].offset);
            for(U32 j=0;j<list->count;++j) {
                count[j+1].name=2974916843u;
                if(t.mode==1) TokenizeModuleTable(reinterpret_cast<U32*>(static_cast<unsigned char*>(list->data)+t.stride*j),*t.nested,&count[j+1].offset,procedures);
                else procedures.BookmarkMemberOffset(static_cast<unsigned char*>(list->data)+(t.mode==6?8:4)*j,&count[j+1].offset);
            }
            procedures.Pop();
        } else if(t.mode==9) {
            TokenizeMotionMove(*reinterpret_cast<U32**>(reinterpret_cast<unsigned char*>(input)+t.offset),&table[i].offset,procedures);
        } else if(t.mode==8) {
            TokenizeObjectFilter(reinterpret_cast<ObjectFilter*>(reinterpret_cast<unsigned char*>(input)+t.offset),&table[i].offset,procedures);
        } else if(t.mode==7) {
            auto filter=*reinterpret_cast<ObjectFilter**>(reinterpret_cast<unsigned char*>(input)+t.offset);if(filter) TokenizeObjectFilter(filter,&table[i].offset,procedures);
        } else if(t.mode==4) {
            auto pointee=*reinterpret_cast<U32**>(reinterpret_cast<unsigned char*>(input)+t.offset);if(pointee) TokenizeModuleTable(pointee,*t.nested,&table[i].offset,procedures);
        } else if(t.mode==2) {
            auto pointee=*reinterpret_cast<void**>(reinterpret_cast<unsigned char*>(input)+t.offset);if(pointee) procedures.BookmarkMemberOffset(pointee,&table[i].offset);
        } else if(t.nested) TokenizeModuleTable(reinterpret_cast<U32*>(reinterpret_cast<unsigned char*>(input)+t.offset),*t.nested,&table[i].offset,procedures);
        else procedures.BookmarkMemberOffset(reinterpret_cast<unsigned char*>(input)+t.offset,&table[i].offset);
    }
    procedures.Pop();
}
void TokenizeMotionMove(U32* input,void** slot,Relo::Tracker& procedures) {
    if(!input) return;auto wrapper=static_cast<Procedure*>(procedures.Push(slot,12,1));wrapper->name=2;wrapper->type=input[0];int index=MotionMoveIndex(input[0]);
    if(index>=0) TokenizeModuleTable(input,*motionMoveChoices[index].tokens,&wrapper->offset,procedures);procedures.Pop();
}
void TokenizeModule(U32* input,void** slot,Relo::Tracker& procedures) {
    if(!input) return;
    // Polymorphic wrapper has exactly one record, without a sentinel.
    auto wrapper=static_cast<Procedure*>(procedures.Push(slot,12,1));wrapper->name=2;wrapper->type=input[0];
    if(auto choice=FindModule(input[0])) TokenizeModuleTable(input,*choice->tokens,&wrapper->offset,procedures);
    procedures.Pop();
}
void MarshalModuleList(const GameObjectChild& input,NativeList* list,Relo::Tracker& data) {
    list->count=static_cast<U32>(input.children.size());
    if(!list->count) return;
    auto slots=static_cast<U32**>(data.Push(&list->data,4,list->count));
    for(U32 i=0;i<list->count;++i) MarshalModule(input.children[i],reinterpret_cast<void**>(slots+i),data);
    data.Pop();
}
void TokenizeModuleList(NativeList* list,void** slot,Relo::Tracker& procedures) {
    auto table=static_cast<Procedure*>(procedures.Push(slot,12,list->count+1));
    table[0].name=1820487795u;table[0].type=1564774360u;procedures.BookmarkMemberOffset(list,&table[0].offset);
    for(U32 i=0;i<list->count;++i) {table[i+1].name=2974916843u;TokenizeModule(static_cast<U32**>(list->data)[i],&table[i+1].offset,procedures);}
    procedures.Pop();
}

// Original AudioVoiceReferentialEntry hashes Name without asset-ID lowercasing.





