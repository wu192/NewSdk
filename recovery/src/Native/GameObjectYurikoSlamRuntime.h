void MarshalYurikoSlam(const GameObjectChild& input,unsigned char* output,unsigned kind,Relo::Tracker& data) {
    if(kind==293) {
        MarshalModuleFieldSet(input,output,addLiftFieldSet,data);
        MarshalSpecialPowerDerived(input,output,183,data);
    } else if(kind==294) {
        MarshalModuleFieldSet(input,output,flingFieldSet,data);
        MarshalSpecialPowerBase(input,output,162,data);
    } else {
        MarshalModuleFieldSet(input,output,yurikoLiftFieldSet,data);
        MarshalModuleFieldSet(input,output,utilityFieldSets[236-225],data);
    }
}
void EndianYurikoSlam(unsigned char* output,unsigned kind) {
    if(kind==293) {EndianModuleFieldSet(output,addLiftFieldSet);EndianSpecialPowerDerived(output,183);}
    else if(kind==294) {EndianModuleFieldSet(output,flingFieldSet);EndianSpecialPowerBase(output,162);}
    else {EndianModuleFieldSet(output,yurikoLiftFieldSet);EndianModuleFieldSet(output,utilityFieldSets[236-225]);}
}
