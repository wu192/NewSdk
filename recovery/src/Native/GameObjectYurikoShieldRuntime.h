void MarshalYurikoShield(const GameObjectChild& input,unsigned char* output,Relo::Tracker& data) {
    MarshalModuleFieldSet(input,output,yurikoShieldFieldSet,data);
    MarshalAbilityFinish(input,output,218,data);
}
void EndianYurikoShield(unsigned char* output) {
    EndianModuleFieldSet(output,yurikoShieldFieldSet);
    EndianAbilityFinish(output,218);
}
