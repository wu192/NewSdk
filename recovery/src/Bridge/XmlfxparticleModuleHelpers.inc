static void InitializedModuleRandom(XmlNode^ node,String^ name,unsigned int offset,Native::XmlfxparticleModuleInput& input) {
        Native::XmlfxparticleModuleRandom field; field.offset=offset;
        field.input.present=true; field.input.type="UNIFORM"; field.input.low="1.0"; field.input.high="1.0";
        XmlNode^ child=Child(node,name);
        if(child!=nullptr) {
            field.input.type=Attr(child,"Type","UNIFORM");
            field.input.low=Attr(child,"Low","1.0"); field.input.high=Attr(child,"High","1.0");
        }
        input.random.push_back(field);
    }
    static void ModuleRandCoord(XmlNode^ node,String^ name,unsigned int offset,Native::XmlfxparticleModuleInput& input) {
        XmlNode^ child=Child(node,name); if(child==nullptr) return;

        array<String^>^ axes={"x","y","z"};
        for(int i=0;i<3;++i) ModuleRandom(child,axes[i],offset+12*i,input);
    }
