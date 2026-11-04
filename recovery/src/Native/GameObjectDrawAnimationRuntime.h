// Complete Animation60, AnimationState140 and LuaEvent16, original draw-source 065/066/083.
void MarshalDrawAnimation(const GameObjectChild& input,unsigned char* output,Relo::Tracker& data) {MarshalModuleFields(input,output,drawAnimationFields,data);}
void EndianDrawAnimation(unsigned char* output) {EndianModuleFields(output,drawAnimationFields);}
void MarshalDrawLuaEvent(const GameObjectChild& input,unsigned char* output,Relo::Tracker& data) {MarshalModuleFields(input,output,drawLuaEventFields,data);}
void EndianDrawLuaEvent(unsigned char* output) {EndianModuleFields(output,drawLuaEventFields);}
void MarshalDrawAnimationState(const GameObjectChild& input,unsigned char* output,Relo::Tracker& data) {
    const char* names[]={"Animation","FXEvent","LuaEvent","ParticleSysBone","Script"};
    GameObjectChildGroups<5> groups(input.children,names);
    MarshalModuleFields(input,output,drawAnimationStateFields,data);
    MarshalDrawModelList(groups,0,output,100,60,data,MarshalDrawAnimation);
    if(auto child=groups.First(4)) {
        auto row=static_cast<unsigned char*>(data.Push(reinterpret_cast<void**>(output+108),8,1));
        MarshalString(data,row,child->text);data.Pop();
    }
    MarshalDrawModelList(groups,1,output,112,28,data,MarshalDrawFXEvent);
    MarshalDrawModelList(groups,2,output,120,16,data,MarshalDrawLuaEvent);
    MarshalDrawModelList(groups,3,output,128,36,data,MarshalDrawParticleSysBone);
}
void EndianDrawAnimationState(unsigned char* output) {
    EndianModuleFields(output,drawAnimationStateFields);
    EndianDrawModelList(output,100,60,EndianDrawAnimation);
    auto script=*reinterpret_cast<unsigned char**>(output+108);if(script)Swap(script);
    EndianDrawModelList(output,112,28,EndianDrawFXEvent);
    EndianDrawModelList(output,120,16,EndianDrawLuaEvent);
    EndianDrawModelList(output,128,36,EndianDrawParticleSysBone);
}
bool DrawAnimationAttribute(const std::string& path,const std::string& attribute) {
    ModuleFieldSet fs={nullptr,0};
    if(path=="AnimationState")fs=drawAnimationStateFieldSet;
    else if(path=="AnimationState/Animation")fs=drawAnimationFieldSet;
    else if(path=="AnimationState/LuaEvent")fs=drawLuaEventFieldSet;
    else if(path=="AnimationState/FXEvent")return DrawModelConditionAttribute("ModelConditionState/FXEvent",attribute);
    else if(path=="AnimationState/ParticleSysBone")return DrawModelConditionAttribute("ModelConditionState/ParticleSysBone",attribute);
    for(unsigned i=0;i<fs.count;++i)if(attribute==fs.fields[i].name)return true;return false;
}
unsigned DrawAnimationNodeKind(const std::string& path) {
    if(path=="AnimationState"||path=="AnimationState/Animation"||path=="AnimationState/FXEvent"||path=="AnimationState/LuaEvent"||path=="AnimationState/ParticleSysBone")return 2;
    if(path=="AnimationState/Script")return 4;
    return 0;
}
