void MarshalYurikoDomination(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    if(kind==292) {MarshalSpecialPowerDerived(input,output,183,data);return;}
    MarshalModuleFieldSet(input,output,lureFieldSet,data);
    auto list=reinterpret_cast<NativeList*>(output+60);
    for(const auto& child:input.children) if(child.name=="GuardOffset") ++list->count;
    if(list->count) {
        auto positions=static_cast<unsigned char*>(data.Push(&list->data,12,list->count));unsigned i=0;
        for(const auto& child:input.children) if(child.name=="GuardOffset")
            MarshalModuleFields(child,positions+12*i++,remainingVector3Fields,data);
        data.Pop();
    }
}
void EndianYurikoDomination(unsigned char* output,unsigned kind) {
    if(kind==292) {EndianSpecialPowerDerived(output,183);return;}
    EndianModuleFieldSet(output,lureFieldSet);
    auto list=reinterpret_cast<NativeList*>(output+60);
    for(U32 i=0;i<list->count;++i) EndianModuleFields(static_cast<unsigned char*>(list->data)+12*i,remainingVector3Fields);
    Swap(&list->count);
}
