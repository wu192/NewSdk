unsigned DrawModelNodeKind(unsigned kind,const std::string& path){
    if(path=="ModelConditionState" || path=="AnimationState" || path=="AttachModel" || path=="EmbedPortal")return 2;
    if(path=="AttachModel/Model")return 1;
    if((kind==288 || kind==290) && path=="TreadTerrainParticles")return 4;
    if(kind==288 && path=="ArticulationBone")return 2;
    if(kind==290 && path=="TreadScrollingMap")return 2;
    auto v=DrawModelConditionNodeKind(path);if(v)return v;
    v=DrawAnimationNodeKind(path);if(v)return v;
    return kind==289?DrawDynamicsNodeKind(path):0;
}
