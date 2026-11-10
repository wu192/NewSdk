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
